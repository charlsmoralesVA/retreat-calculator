// Fabio Orderflow Strategy - ATAS chart strategy with the Laya bridge.
//
// What it does
//  * Runs the same engine as the indicator and reports every live set-up (ATAS notification + bridge ticket).
//  * Bridge (on by default, localhost only): Laya reads the market state and the set-ups and approves or rejects them.
//  * Trading (OFF by default). When on, ONE position at a time:
//      set-up -> [Laya approves, if "Require Laya approval"] -> market entry -> stop + target once filled ->
//      the other one is cancelled when one fills -> flat at the end of the cash session.
//    Hard limits stay in code whatever Laya says: stop on every trade, max losing trades per session,
//    max set-ups per session, no entry if price ran away from the set-up, flat at session end.
//
// API usage follows ATAS's developer docs (docs.atas.net: Chart strategies, Orders): OpenOrderAsync / CancelOrderAsync,
// CanProcess, OnOrderChanged / OnOrderRegisterFailed (trading threads -> everything below runs under one lock),
// cancel only Active orders and only once, close the position in OnStopping.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Threading.Tasks;
using ATAS.DataFeedsCore;
using ATAS.Indicators;
using ATAS.Strategies.Chart;
using Utils.Common.Logging;
using Core = FabioOrderflow.Core;

namespace FabioOrderflow.Atas
{
    [DisplayName("Fabio Orderflow Strategy")]
    public class FabioStrategy : ChartStrategy
    {
        private readonly object _sync = new object();
        private readonly FabioFeed _feed = new FabioFeed();
        private readonly Core.LayaBridge _bridge = new Core.LayaBridge();
        private Core.BridgeServer _server;
        private DateTime _stateAt;
        private int _stateBar = -1;

        // trade in progress
        private Order _entryOrder, _stopOrder, _targetOrder, _flattenOrder;
        private Core.SetupEvaluation _active;
        private string _activeTicket;
        private bool _bracketSent;
        private int _entryBar = -1;
        private readonly HashSet<Order> _cancelSent = new HashSet<Order>();

        // set-up waiting for Laya
        private Core.SetupEvaluation _awaiting;
        private string _awaitingTicket;

        private DateTime? _session;
        private int _lossesThisSession, _lastSignalHandled = -1, _lastLayaAlertSeq = -1;

        // ------------------------------------------------------------------ settings
        [Display(Name = "Trading enabled (OFF = report set-ups only)", GroupName = "1. Execution", Order = 10)]
        public bool TradingEnabled { get; set; } = false;
        [Display(Name = "Contracts", GroupName = "1. Execution", Order = 20)]
        public decimal Contracts { get; set; } = 1;
        [Display(Name = "Max losing trades per session", GroupName = "1. Execution", Order = 30)]
        public int MaxLossesPerSession { get; set; } = 2;
        [Display(Name = "Max entry slippage vs set-up price (ticks)", GroupName = "1. Execution", Order = 40)]
        public decimal MaxEntrySlipTicks { get; set; } = 8;
        [Display(Name = "Flatten when stopping the strategy", GroupName = "1. Execution", Order = 50)]
        public bool ClosePositionOnStopping { get; set; } = true;

        [Display(Name = "Bridge for Laya enabled (localhost only)", GroupName = "2. Laya bridge", Order = 10)]
        public bool BridgeEnabled { get; set; } = true;
        [Display(Name = "Bridge port", GroupName = "2. Laya bridge", Order = 20)]
        public int BridgePort { get; set; } = 8765;
        [Display(Name = "Bridge token (optional)", GroupName = "2. Laya bridge", Order = 30)]
        public string BridgeToken { get; set; } = "";
        [Display(Name = "Require Laya approval before entering", GroupName = "2. Laya bridge", Order = 40)]
        public bool RequireLayaApproval { get; set; } = true;
        [Display(Name = "Approval timeout (seconds)", GroupName = "2. Laya bridge", Order = 50)]
        public int ApprovalTimeoutSeconds { get { return _bridge.ApprovalTimeoutSeconds; } set { _bridge.ApprovalTimeoutSeconds = Math.Max(0, value); } }
        [Display(Name = "Journal file (empty = default)", GroupName = "2. Laya bridge", Order = 60)]
        public string JournalFile { get; set; } = "";

        [Display(Name = "Exchange time zone", GroupName = "3. Session", Order = 10)]
        public string TimeZoneId { get { return _feed.TimeZoneId; } set { _feed.TimeZoneId = value; } }
        [Display(Name = "Require bias alignment", GroupName = "4. Rules", Order = 10)]
        public bool RequireBias { get { return _feed.P.RequireBiasAlignment; } set { _feed.P.RequireBiasAlignment = value; } }
        [Display(Name = "Max set-ups per session", GroupName = "4. Rules", Order = 20)]
        public int MaxSignals { get { return _feed.P.MaxSignalsPerSession; } set { _feed.P.MaxSignalsPerSession = Math.Max(1, value); } }
        [Display(Name = "Engine settings from the backtest", GroupName = "4. Rules", Order = 30, Description = "Paste the settings line of a fabio-backtest report (e.g. AbsorbDeltaMult=1.5;ZoneToleranceTicks=4). Use the same line in the indicator.")]
        public string EngineSettings { get { return _feed.EngineSettings; } set { _feed.EngineSettings = value ?? ""; } }

        [Display(Name = "Order book range for Laya (ticks)", GroupName = "4. Rules", Order = 40, Description = "Resting bids / offers within this many ticks of the close are summarised on every candle card (walls, imbalance, pulled walls). 0 = off.")]
        public int BookRangeTicks { get; set; } = 20;

        // ------------------------------------------------------------------ order book (market depth), for Laya's context
        private bool _bookLoaded;
        protected override void MarketDepthChanged(MarketDataArg depth)
        {
            try
            {
                if (BookRangeTicks <= 0 || depth == null) return;
                if (!_bookLoaded)
                {
                    _bookLoaded = true;
                    var info = MarketDepthInfo;
                    if (info != null)
                        foreach (var d in info.GetMarketDepthSnapshot())
                            if (d.DataType == MarketDataType.Ask || d.DataType == MarketDataType.Bid) _feed.Book.Set(d.Price, d.Volume, d.DataType == MarketDataType.Ask);
                }
                if (depth.DataType == MarketDataType.Ask || depth.DataType == MarketDataType.Bid)
                    _feed.Book.Set(depth.Price, depth.Volume, depth.DataType == MarketDataType.Ask);
            }
            catch (Exception e) { this.LogError("Fabio strategy: market depth", e); }
        }

        public Core.LayaBridge Bridge { get { return _bridge; } }
        public FabioFeed Feed { get { return _feed; } }

        // ------------------------------------------------------------------ calculation
        protected override void OnCalculate(int bar, decimal value)
        {
            lock (_sync)
            {
                try
                {
                    _feed.P.TickSize = InstrumentInfo.TickSize;
                    var st = _feed.Process(GetCandle(bar), bar, CurrentBar, InstrumentInfo.TimeZoneOffset);
                    bool live = bar >= CurrentBar - 1;
                    var now = DateTime.UtcNow;

                    if (live) { EnsureBridge(); PublishState(bar, now, st.NewSignals.Count > 0); NotifyLayaAlerts(); }

                    foreach (var s in st.NewSignals)
                    {
                        if (s.ConfirmGlobalIndex < CurrentBar - 2 || s.ConfirmGlobalIndex == _lastSignalHandled) continue;   // history: nothing to report
                        _lastSignalHandled = s.ConfirmGlobalIndex;
                        bool needsApproval = BridgeEnabled && RequireLayaApproval;
                        var ticket = _bridge.Publish(s, InstrumentInfo.Instrument, now, needsApproval);
                        RaiseShowNotification(string.Format("Fabio set-up {0}: {1} {2} entry {3} stop {4} target {5} R:R {6:0.0}{7}",
                            ticket.Id, s.Side, s.Tag, s.Entry, s.Stop, s.Target, s.Rr, needsApproval ? " - waiting for Laya" : ""), level: LoggingLevel.Warning);

                        if (!TradingEnabled) { _bridge.SetOutcome(ticket.Id, null, "not traded: trading is off", now); continue; }
                        if (needsApproval) { _awaiting = s; _awaitingTicket = ticket.Id; }
                        else if (CanProcess(bar)) TryEnter(s, ticket.Id, now);
                    }

                    // candles after the set-up tickets: when Laya wakes on a candle, its set-up is already pending
                    foreach (var c in st.ClosedCandles)
                    {
                        // the book is live: attach it only to the candle that has just closed, never to history
                        if (live && _feed.Book.HasData)
                            c.Liquidity = _feed.Book.Snapshot(c.Close, InstrumentInfo.TickSize, BookRangeTicks, 3, c.Low, c.High);
                        _bridge.PublishCandle(c);
                    }

                    if (!CanProcess(bar)) return;
                    CheckApproval(st, now);
                    ManagePosition(st, now);
                }
                catch (Exception e)
                {
                    this.LogError("Fabio strategy error", e);
                }
            }
        }

        private void EnsureBridge()
        {
            if (!BridgeEnabled) { if (_server != null) { _server.Dispose(); _server = null; } return; }
            if (_server != null) return;
            if (string.IsNullOrEmpty(_bridge.JournalPath))
            {
                try
                {
                    string path = JournalFile;
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FabioBridge");
                        Directory.CreateDirectory(dir);
                        path = Path.Combine(dir, "journal.jsonl");
                    }
                    _bridge.JournalPath = path;
                }
                catch (Exception) { /* journal stays in memory only */ }
            }
            _server = new Core.BridgeServer(_bridge, BridgePort, BridgeToken);
            if (_server.Start()) this.LogInfo("Fabio bridge for Laya listening on {0}", _server.Url);
            else RaiseShowNotification("Fabio bridge: " + _server.LastError, level: LoggingLevel.Warning);
        }

        /// <summary>Shows Laya's own alerts (her candle reviews with action alert_long / alert_short) as ATAS notifications.</summary>
        private void NotifyLayaAlerts()
        {
            foreach (var r in _bridge.Reviews(5))
            {
                int seq = r.ContainsKey("seq") ? Convert.ToInt32(r["seq"]) : -1;
                if (seq <= _lastLayaAlertSeq) continue;
                _lastLayaAlertSeq = seq;
                string action = r.ContainsKey("action") ? Convert.ToString(r["action"]) : "";
                if (action != null && action.StartsWith("alert"))
                    RaiseShowNotification("Laya " + action.Replace('_', ' ') + ": " + Convert.ToString(r["read"]), level: LoggingLevel.Warning);
            }
        }

        private void PublishState(int bar, DateTime now, bool force)
        {
            // every new bar and every new set-up at once; repeated ticks of the same bar at most twice a second
            if (_server == null || (!force && bar == _stateBar && (now - _stateAt).TotalMilliseconds < 500)) return;
            _stateAt = now; _stateBar = bar;
            var trades = _feed.Trades;
            int open = 0;
            if (trades != null) foreach (var t in trades.Trades) if (t.ExitReason == "open") open++;
            _bridge.SetState(Core.LayaBridge.StateToJson(_feed.State, InstrumentInfo.Instrument,
                _feed.Bars.Count > 0 ? (DateTime?)_feed.Bars[_feed.Bars.Count - 1].Time : null, trades == null ? null : trades.Stats, open));
        }

        // ------------------------------------------------------------------ Laya approval
        private void CheckApproval(Core.EngineState st, DateTime now)
        {
            if (_awaiting == null) return;
            var status = _bridge.StatusOf(_awaitingTicket, now);
            if (status == Core.TicketStatus.Pending) return;

            var s = _awaiting; var id = _awaitingTicket;
            _awaiting = null; _awaitingTicket = null;
            if (status != Core.TicketStatus.Approved)
            {
                _bridge.SetOutcome(id, null, "not traded: " + status.ToString().ToLowerInvariant(), now);
                return;
            }
            if (!st.InRth) { _bridge.SetOutcome(id, Core.TicketStatus.Skipped, "skipped: cash session over", now); return; }

            decimal price = _feed.Bars.Count > 0 ? _feed.Bars[_feed.Bars.Count - 1].Close : s.Entry;
            decimal slip = MaxEntrySlipTicks * InstrumentInfo.TickSize;
            bool beyondStop = s.Side == Core.Side.Long ? price <= s.Stop : price >= s.Stop;
            bool beyondTarget = s.Side == Core.Side.Long ? price >= s.Target : price <= s.Target;
            if (beyondStop || beyondTarget || Math.Abs(price - s.Entry) > slip)
            {
                _bridge.SetOutcome(id, Core.TicketStatus.Skipped, string.Format("skipped: price {0} moved too far from the set-up entry {1}", price, s.Entry), now);
                RaiseShowNotification("Fabio: Laya approved, but price moved too far - set-up skipped", level: LoggingLevel.Warning);
                return;
            }
            TryEnter(s, id, now);
        }

        // ------------------------------------------------------------------ orders
        private void TryEnter(Core.SetupEvaluation s, string ticketId, DateTime now)
        {
            if (_entryOrder != null || CurrentPosition != 0) { _bridge.SetOutcome(ticketId, Core.TicketStatus.Skipped, "skipped: already in a trade", now); return; }
            if (_lossesThisSession >= MaxLossesPerSession)
            {
                _bridge.SetOutcome(ticketId, Core.TicketStatus.Skipped, "skipped: loss limit for this session reached", now);
                RaiseShowNotification("Fabio: loss limit for this session reached, set-up not traded", level: LoggingLevel.Warning);
                return;
            }
            _active = s; _activeTicket = ticketId; _bracketSent = false; _entryBar = CurrentBar - 1;
            _entryOrder = new Order
            {
                Portfolio = Portfolio, Security = Security,
                Direction = s.Side == Core.Side.Long ? OrderDirections.Buy : OrderDirections.Sell,
                Type = OrderTypes.Market, QuantityToFill = Contracts, Comment = "Fabio entry " + s.Tag
            };
            _bridge.SetOutcome(ticketId, Core.TicketStatus.Taken, "entry sent", now);
            _ = PlaceAsync(_entryOrder);
        }

        private async Task PlaceAsync(Order order)
        {
            try { await OpenOrderAsync(order); }
            catch (Exception e)
            {
                this.LogError("Fabio: order rejected", e);
                lock (_sync) OnRejected(order, e.Message);
            }
        }

        private async Task CancelAsync(Order order)
        {
            try { await CancelOrderAsync(order); }
            catch (Exception e) { this.LogError("Fabio: cancel failed", e); }
        }

        private void CancelOnce(Order o)
        {
            if (o == null || o.State != OrderStates.Active || !_cancelSent.Add(o)) return;
            _ = CancelAsync(o);
        }

        protected override void OnOrderChanged(Order order)
        {
            base.OnOrderChanged(order);
            lock (_sync)
            {
                try { HandleOrder(order); }
                catch (Exception e) { this.LogError("Fabio strategy error", e); }
            }
        }

        protected override void OnOrderRegisterFailed(Order order, string message)
        {
            base.OnOrderRegisterFailed(order, message);
            lock (_sync) OnRejected(order, message);
        }

        private void HandleOrder(Order order)
        {
            var now = DateTime.UtcNow;
            if (order.State == OrderStates.Failed) { OnRejected(order, "rejected"); return; }
            var status = order.Status();

            if (order == _entryOrder && !_bracketSent && (status == OrderStatus.Filled || status == OrderStatus.PartlyFilled || CurrentPosition != 0))
            {
                PlaceBracket(now);
                return;
            }
            if (order == _stopOrder && status == OrderStatus.Filled)
            {
                _lossesThisSession++;
                CancelOnce(_targetOrder);
                Finish("stop", now);
                return;
            }
            if (order == _targetOrder && status == OrderStatus.Filled)
            {
                CancelOnce(_stopOrder);
                Finish("target", now);
                return;
            }
            if (order == _flattenOrder && status == OrderStatus.Filled) { _flattenOrder = null; }
        }

        private void OnRejected(Order order, string message)
        {
            var now = DateTime.UtcNow;
            if (order == _entryOrder)
            {
                RaiseShowNotification("Fabio: entry order rejected - " + message, level: LoggingLevel.Warning);
                if (_activeTicket != null) _bridge.SetOutcome(_activeTicket, null, "entry rejected: " + message, now);
                ResetTrade();
            }
            else if (order == _flattenOrder)
            {
                RaiseShowNotification("Fabio: flatten order rejected - will retry. " + message, level: LoggingLevel.Warning);
                _flattenOrder = null;
            }
            else if (order == _stopOrder || order == _targetOrder)
            {
                // an unprotected position is not acceptable
                RaiseShowNotification("Fabio: protective order rejected - flattening. " + message, level: LoggingLevel.Warning);
                Flatten("protective order rejected", now);
            }
        }

        private void PlaceBracket(DateTime now)
        {
            if (_active == null || _bracketSent) return;
            decimal qty = Math.Abs(CurrentPosition);
            if (qty == 0) qty = Contracts;
            bool longPos = _active.Side == Core.Side.Long;
            var exitDir = longPos ? OrderDirections.Sell : OrderDirections.Buy;
            var stop = new Order { Portfolio = Portfolio, Security = Security, Direction = exitDir, Type = OrderTypes.Stop, TriggerPrice = Round(_active.Stop), QuantityToFill = qty, Comment = "Fabio stop" };
            var target = new Order { Portfolio = Portfolio, Security = Security, Direction = exitDir, Type = OrderTypes.Limit, Price = Round(_active.Target), QuantityToFill = qty, Comment = "Fabio target" };
            string ticket = _activeTicket;
            _stopOrder = stop; _targetOrder = target; _bracketSent = true;
            if (ticket != null) _bridge.SetOutcome(ticket, null, "in trade: stop " + stop.TriggerPrice + " target " + target.Price, now);
            _ = PlaceAsync(stop);                                  // the stop first: protection before profit
            if (_targetOrder == target) _ = PlaceAsync(target);    // skipped if the stop was rejected and we flattened
        }

        private void ManagePosition(Core.EngineState st, DateTime now)
        {
            var key = st.InRth && _feed.Bars.Count > 0 ? (DateTime?)_feed.Bars[_feed.Bars.Count - 1].Time.Date : null;
            if (key != null && key != _session) { _session = key; _lossesThisSession = 0; }
            if (CurrentPosition == 0) _flattenOrder = null;     // flat again: a later flatten may be sent if ever needed

            // flat at the end of the cash session
            if (!st.InRth && (CurrentPosition != 0 || _entryOrder != null)) { Flatten("cash session ended", now); return; }

            // filled but the fill event was missed: protect the position anyway
            if (_entryOrder != null && !_bracketSent && CurrentPosition != 0) { PlaceBracket(now); return; }

            // entry never filled -> give up after 2 bars
            if (_entryOrder != null && !_bracketSent && CurrentPosition == 0 && _entryBar >= 0 && CurrentBar - 1 - _entryBar > 2)
            {
                CancelOnce(_entryOrder);
                if (_activeTicket != null) _bridge.SetOutcome(_activeTicket, null, "entry not filled", now);
                ResetTrade();
                return;
            }

            // position closed some other way (by hand, by the broker): clean up the bracket
            if (_bracketSent && CurrentPosition == 0 && _flattenOrder == null)
            {
                CancelOnce(_stopOrder); CancelOnce(_targetOrder);
                Finish("closed outside the strategy", now);
            }
        }

        private void Flatten(string why, DateTime now)
        {
            CancelOnce(_entryOrder); CancelOnce(_stopOrder); CancelOnce(_targetOrder);
            if (CurrentPosition != 0 && _flattenOrder == null)
            {
                _flattenOrder = new Order
                {
                    Portfolio = Portfolio, Security = Security,
                    Direction = CurrentPosition > 0 ? OrderDirections.Sell : OrderDirections.Buy,
                    Type = OrderTypes.Market, QuantityToFill = Math.Abs(CurrentPosition), Comment = "Fabio flatten: " + why
                };
                _ = PlaceAsync(_flattenOrder);
                RaiseShowNotification("Fabio: flattening (" + why + ")", level: LoggingLevel.Warning);
            }
            if (_activeTicket != null) _bridge.SetOutcome(_activeTicket, null, "flat: " + why, now);
            if (_awaitingTicket != null) { _bridge.SetOutcome(_awaitingTicket, Core.TicketStatus.Skipped, "skipped: " + why, now); _awaiting = null; _awaitingTicket = null; }
            ResetTrade();
        }

        private void Finish(string outcome, DateTime now)
        {
            if (_activeTicket != null) _bridge.SetOutcome(_activeTicket, null, outcome, now);
            ResetTrade();
        }

        private void ResetTrade()
        {
            _entryOrder = null; _stopOrder = null; _targetOrder = null; _active = null; _activeTicket = null;
            _bracketSent = false; _entryBar = -1;
        }

        protected override void OnStopping()
        {
            lock (_sync) { if (ClosePositionOnStopping) Flatten("strategy stopped", DateTime.UtcNow); }
            base.OnStopping();
        }

        protected override void OnDispose()
        {
            lock (_sync) { if (_server != null) { _server.Dispose(); _server = null; } }
            base.OnDispose();
        }

        private decimal Round(decimal price)
        {
            decimal tick = InstrumentInfo.TickSize <= 0 ? 0.25m : InstrumentInfo.TickSize;
            return Math.Round(price / tick) * tick;
        }
    }
}
