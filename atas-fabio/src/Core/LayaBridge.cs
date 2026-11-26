using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace FabioOrderflow.Core
{
    public enum TicketStatus { Pending, Approved, Rejected, Expired, Auto, Taken, Skipped }

    /// <summary>One set-up handed to Laya (the AI reviewer) for an approve / reject decision.</summary>
    public sealed class SetupTicket
    {
        public string Id;
        public DateTime CreatedUtc;
        public DateTime? DecidedUtc;
        public string Instrument;
        public SetupEvaluation Setup;
        public TicketStatus Status;
        public string Reason = "";
        public string Outcome = "";       // filled in by the strategy: entered / skipped / target / stop / flat

        public Dictionary<string, object> ToJson()
        {
            var s = Setup;
            return new Dictionary<string, object>
            {
                { "id", Id }, { "created_utc", CreatedUtc }, { "decided_utc", DecidedUtc },
                { "instrument", Instrument }, { "status", Status.ToString().ToLowerInvariant() },
                { "reason", Reason }, { "outcome", Outcome },
                { "setup", s == null ? null : LayaBridge.SetupToJson(s) }
            };
        }
    }

    /// <summary>
    /// The meeting point between the ATAS strategy and Laya. Thread-safe: the strategy, the HTTP server and the
    /// trading callbacks all run on different threads.
    ///  * The strategy publishes each live set-up as a ticket and the current market state.
    ///  * Laya reads them (through BridgeServer / the MCP server) and approves or rejects a ticket with a reason.
    ///  * A ticket not decided within the timeout expires and is NOT traded.
    ///  * Every event is appended to a JSON-lines journal for review.
    /// Laya can only veto: the strategy's hard limits (stop, loss cap, max trades, flat at session end) stay in code.
    /// </summary>
    public sealed class LayaBridge
    {
        private readonly object _lock = new object();
        private readonly List<SetupTicket> _tickets = new List<SetupTicket>();
        private readonly List<Dictionary<string, object>> _journal = new List<Dictionary<string, object>>();
        private string _stateJson = "{}";
        private int _seq;

        public int ApprovalTimeoutSeconds = 45;
        public string JournalPath;            // optional JSON-lines file
        public int MaxKept = 500;

        public SetupTicket Publish(SetupEvaluation s, string instrument, DateTime nowUtc, bool needsApproval)
        {
            lock (_lock)
            {
                var t = new SetupTicket
                {
                    Id = nowUtc.ToString("yyyyMMdd-HHmmss") + "-" + (++_seq),
                    CreatedUtc = nowUtc, Instrument = instrument ?? "", Setup = s,
                    Status = needsApproval ? TicketStatus.Pending : TicketStatus.Auto
                };
                _tickets.Add(t);
                if (_tickets.Count > MaxKept) _tickets.RemoveAt(0);
                Log("setup", t.ToJson(), nowUtc);
                Monitor.PulseAll(_lock);
                return t;
            }
        }

        /// <summary>Laya's decision. False if the ticket is unknown or no longer pending (decided, expired).</summary>
        public bool Decide(string id, bool approve, string reason, DateTime nowUtc, out string error)
        {
            lock (_lock)
            {
                ExpireLocked(nowUtc);
                var t = _tickets.FirstOrDefault(x => x.Id == id);
                if (t == null) { error = "unknown set-up id"; return false; }
                if (t.Status != TicketStatus.Pending) { error = "set-up is " + t.Status.ToString().ToLowerInvariant() + ", not pending"; return false; }
                t.Status = approve ? TicketStatus.Approved : TicketStatus.Rejected;
                t.Reason = reason ?? "";
                t.DecidedUtc = nowUtc;
                Log("decision", t.ToJson(), nowUtc);
                Monitor.PulseAll(_lock);
                error = null;
                return true;
            }
        }

        /// <summary>Strategy side: current status of a ticket (expires it first if the timeout passed).</summary>
        public TicketStatus StatusOf(string id, DateTime nowUtc)
        {
            lock (_lock)
            {
                ExpireLocked(nowUtc);
                var t = _tickets.FirstOrDefault(x => x.Id == id);
                return t == null ? TicketStatus.Expired : t.Status;
            }
        }

        public void SetOutcome(string id, TicketStatus? status, string outcome, DateTime nowUtc)
        {
            lock (_lock)
            {
                var t = _tickets.FirstOrDefault(x => x.Id == id);
                if (t == null) return;
                if (status.HasValue) t.Status = status.Value;
                if (outcome != null) t.Outcome = outcome;
                Log("outcome", t.ToJson(), nowUtc);
            }
        }

        public void Event(string kind, Dictionary<string, object> data, DateTime nowUtc)
        {
            lock (_lock) Log(kind, data, nowUtc);
        }

        public void SetState(string json) { lock (_lock) _stateJson = string.IsNullOrEmpty(json) ? "{}" : json; }
        public string StateJson { get { lock (_lock) return _stateJson; } }

        public List<SetupTicket> Tickets(string status, DateTime nowUtc, int limit = 50)
        {
            lock (_lock)
            {
                ExpireLocked(nowUtc);
                IEnumerable<SetupTicket> q = _tickets;
                if (!string.IsNullOrEmpty(status) && status != "all")
                    q = q.Where(t => string.Equals(t.Status.ToString(), status, StringComparison.OrdinalIgnoreCase));
                return q.Reverse().Take(Math.Max(1, limit)).ToList();
            }
        }

        /// <summary>Long-poll: returns pending tickets as soon as there is one, or an empty list after the timeout.</summary>
        public List<SetupTicket> WaitForPending(TimeSpan timeout, Func<DateTime> clock)
        {
            var until = DateTime.UtcNow + timeout;
            lock (_lock)
            {
                while (true)
                {
                    ExpireLocked(clock());
                    var p = _tickets.Where(t => t.Status == TicketStatus.Pending).ToList();
                    if (p.Count > 0) return p;
                    var left = until - DateTime.UtcNow;
                    if (left <= TimeSpan.Zero) return p;
                    Monitor.Wait(_lock, left < TimeSpan.FromSeconds(1) ? left : TimeSpan.FromSeconds(1));
                }
            }
        }

        public List<Dictionary<string, object>> Journal(int limit)
        {
            lock (_lock) return _journal.Skip(Math.Max(0, _journal.Count - Math.Max(1, limit))).ToList();
        }

        private void ExpireLocked(DateTime nowUtc)
        {
            foreach (var t in _tickets)
                if (t.Status == TicketStatus.Pending && (nowUtc - t.CreatedUtc).TotalSeconds >= ApprovalTimeoutSeconds)
                {
                    t.Status = TicketStatus.Expired; t.Reason = "no decision within " + ApprovalTimeoutSeconds + " s"; t.DecidedUtc = nowUtc;
                    Log("expired", t.ToJson(), nowUtc);
                }
        }

        private void Log(string kind, Dictionary<string, object> data, DateTime nowUtc)
        {
            var e = new Dictionary<string, object> { { "time_utc", nowUtc }, { "event", kind }, { "data", data } };
            _journal.Add(e);
            if (_journal.Count > MaxKept) _journal.RemoveAt(0);
            if (!string.IsNullOrEmpty(JournalPath))
            {
                try { File.AppendAllText(JournalPath, Json.Write(e) + "\n"); } catch (Exception) { /* journal is best effort */ }
            }
        }

        // ------------------------------------------------------------------ JSON views used by the bridge
        public static Dictionary<string, object> SetupToJson(SetupEvaluation s)
        {
            return new Dictionary<string, object>
            {
                { "side", s.Side.ToString() }, { "model", s.Model }, { "tag", s.Tag },
                { "zone", s.Zone == null ? null : new Dictionary<string, object> { { "kind", s.Zone.Kind.ToString() }, { "price", s.Zone.Price }, { "label", s.Zone.Label } } },
                { "entry", s.Entry }, { "stop", s.Stop }, { "target", s.Target }, { "rr", decimal.Round(s.Rr, 2) },
                { "all_required_met", s.AllRequiredMet },
                { "confluence_met", s.ConfluenceMet }, { "confluence_total", s.ConfluenceCount },
                { "checks", s.Checks.Select(c => (object)new Dictionary<string, object>
                    {
                        { "name", c.Name }, { "state", c.State.ToString() }, { "required", c.Required },
                        { "source", c.Source.ToString() }, { "detail", c.Detail }
                    }).ToList() },
                { "checklist_text", s.ToChecklist() }
            };
        }

        /// <summary>Snapshot of what the engine sees, for Laya's context (bias, levels, VWAP, IB, history statistics).</summary>
        public static string StateToJson(EngineState st, string instrument, DateTime? lastBarExchangeTime, BacktestStats history, int openTrades)
        {
            var d = new Dictionary<string, object> { { "instrument", instrument ?? "" }, { "updated_utc", DateTime.UtcNow } };
            if (st == null) { d["ready"] = false; return Json.Write(d); }
            d["ready"] = st.Ready;
            d["in_cash_session"] = st.InRth;
            d["last_bar_exchange_time"] = lastBarExchangeTime;
            d["last_price"] = st.LastPrice;
            d["bias"] = new Dictionary<string, object>
            {
                { "final", st.Final.ToString() }, { "note", st.BiasNote }, { "last_value_shift", st.DailyShift.ToString() },
                { "narrative_5_sessions", st.Narrative.ToString() }, { "vs_3_month_value", st.Auction.ToString() }
            };
            if (st.Prior != null && st.Prior.Stats != null)
                d["prior_session"] = new Dictionary<string, object>
                {
                    { "poc", st.Prior.Stats.Poc }, { "vah", st.Prior.Stats.Vah }, { "val", st.Prior.Stats.Val },
                    { "high", st.Prior.High }, { "low", st.Prior.Low }, { "close", st.Prior.Close }
                };
            d["location_vs_prior_value"] = st.Location.ToString();
            if (st.Composition != null) d["composition_3_month"] = new Dictionary<string, object> { { "poc", st.Composition.Poc }, { "vah", st.Composition.Vah }, { "val", st.Composition.Val } };
            if (st.Vwap > 0) d["session_vwap"] = decimal.Round(st.Vwap, 2);
            if (st.Ib != null && st.Ib.Started) d["initial_balance"] = new Dictionary<string, object> { { "high", st.Ib.High }, { "low", st.Ib.Low }, { "complete", st.Ib.Complete }, { "breakout", st.Ib.Breakout.ToString() } };
            if (st.Gap != null) d["gap"] = new Dictionary<string, object> { { "size", st.Gap.Gap }, { "filled", st.Gap.Filled } };
            d["zones"] = st.Zones.Select(z => (object)new Dictionary<string, object> { { "kind", z.Kind.ToString() }, { "price", z.Price }, { "trade_side", z.Candidate.ToString() }, { "label", z.Label } }).ToList();
            d["signals_this_session"] = st.SignalsThisSession;
            if (st.Best != null) d["nearest_zone_checklist"] = SetupToJson(st.Best);
            if (history != null)
                d["history_on_chart"] = new Dictionary<string, object>
                {
                    { "trades", history.Trades }, { "win_rate", decimal.Round(history.WinRate, 3) }, { "total_r", decimal.Round(history.TotalR, 2) },
                    { "expectancy_r", decimal.Round(history.ExpectancyR, 3) }, { "max_drawdown_r", decimal.Round(history.MaxDrawdownR, 2) }, { "open_trades", openTrades }
                };
            return Json.Write(d);
        }
    }
}
