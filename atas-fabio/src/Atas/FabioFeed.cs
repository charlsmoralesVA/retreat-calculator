// Shared glue between ATAS candles and the ATAS-independent engine (used by the indicator and the strategy).
// ATAS API used here, as ATAS's own indicators use it (github.com/AtasPlatform/indicators):
//   IndicatorCandle.Time (UTC), Open/High/Low/Close/Volume/Delta, GetAllPriceLevels() -> PriceLevel.Price/Bid/Ask
//   CumulativeTrade.Time/FirstPrice/Volume/Direction (one aggressive order, all its fills together)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ATAS.Indicators;
using Core = FabioOrderflow.Core;

namespace FabioOrderflow.Atas
{
    public sealed class FabioFeed
    {
        public readonly Core.FabioParams P = new Core.FabioParams();
        public readonly Core.BacktestParams Bt = new Core.BacktestParams { LeaveLastTradeOpen = true };

        /// <summary>Exchange time zone of the cash session (IANA or Windows id). ES/NQ: America/New_York.</summary>
        public string TimeZoneId = "America/New_York";

        public Core.FabioEngine Engine;
        public Core.BigTradeFilter Big;
        public Core.EngineState State;
        public Core.BacktestResult Trades;
        public readonly List<Core.SetupEvaluation> Signals = new List<Core.SetupEvaluation>();
        /// <summary>Every chart bar in exchange time; index = ATAS bar index.</summary>
        public readonly List<Core.Bar> Bars = new List<Core.Bar>();
        public string LastError;
        public bool ZoneFound { get; private set; }

        private TimeZoneInfo _zone;
        private readonly Dictionary<int, int> _bigBuys = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _bigSells = new Dictionary<int, int>();
        private readonly HashSet<string> _bigSeen = new HashSet<string>();
        private bool _tradeFeedSeen;
        private int _lastBar = -1, _tradesBar = -1;
        private DateTime _tradesAt;

        public void Reset()
        {
            Engine = new Core.FabioEngine(P);
            Big = new Core.BigTradeFilter(P);
            _zone = Core.ExchangeTime.Find(TimeZoneId);
            ZoneFound = _zone != null;
            Signals.Clear(); Bars.Clear(); _bigBuys.Clear(); _bigSells.Clear(); _bigSeen.Clear();
            _lastBar = -1; _tradesBar = -1; Trades = null; LastError = null; State = null;
        }

        /// <summary>Call from OnCalculate. fallbackOffset = InstrumentInfo.TimeZoneOffset (used only if the zone id is unknown).</summary>
        public Core.EngineState Process(IndicatorCandle c, int bar, int currentBar, TimeSpan fallbackOffset)
        {
            if (bar == 0 || Engine == null) Reset();

            if (bar > _lastBar && _lastBar >= 0)
            {
                int nb, ns; _bigBuys.TryGetValue(_lastBar, out nb); _bigSells.TryGetValue(_lastBar, out ns);
                Big.Adapt(nb + ns);      // SKILL.md: more than ~3 big trades per candle -> scale the filter up
                _bigSeen.Clear();
            }
            _lastBar = bar;

            var b = new Core.Bar
            {
                Time = _zone != null ? Core.ExchangeTime.FromUtc(c.Time, _zone) : c.Time.Add(fallbackOffset),
                Open = c.Open, High = c.High, Low = c.Low, Close = c.Close, Volume = c.Volume, Delta = c.Delta
            };
            if (_tradeFeedSeen)
            {
                int n; b.BigBuys = _bigBuys.TryGetValue(bar, out n) ? n : 0; b.BigSells = _bigSells.TryGetValue(bar, out n) ? n : 0;
            }

            var raw = c.GetAllPriceLevels();
            var levels = raw == null ? new Core.PriceLevelVol[0] : raw.Select(l => new Core.PriceLevelVol(l.Price, l.Bid, l.Ask)).ToArray();

            if (bar < Bars.Count) Bars[bar] = b;
            else { while (Bars.Count < bar) Bars.Add(b); Bars.Add(b); }

            State = Engine.Update(bar, b, levels, bar == currentBar - 1);
            Signals.AddRange(State.NewSignals);

            // virtual trades over everything loaded: once when history is done, then on each new bar (and at most once a second)
            if (bar >= currentBar - 1 && (bar != _tradesBar || (DateTime.UtcNow - _tradesAt).TotalSeconds >= 1))
            {
                Trades = Core.Backtester.Simulate(Bars, Signals, P, Bt);
                _tradesBar = bar; _tradesAt = DateTime.UtcNow;
            }
            return State;
        }

        /// <summary>
        /// Big trades ("prints"), live only: one cumulative trade = one aggressive order. Counted once, on the bar where it
        /// first reaches the filter (SKILL.md: ~60-75 contracts on the 5-minute chart). Historical bars show "no trade data".
        /// </summary>
        public void RegisterCumulative(int bar, DateTime time, decimal firstPrice, bool isBuy, bool isSell, decimal volume)
        {
            _tradeFeedSeen = true;
            if (Big == null || bar < 0 || (!isBuy && !isSell) || !Big.IsBig(volume)) return;
            string key = time.Ticks + "|" + firstPrice + "|" + (isBuy ? "B" : "S");
            if (!_bigSeen.Add(key)) return;
            var d = isBuy ? _bigBuys : _bigSells;
            int n; d.TryGetValue(bar, out n); d[bar] = n + 1;
        }

        public string PanelText(bool showChecklist)
        {
            var sb = new StringBuilder();
            sb.AppendLine("FABIO ORDERFLOW MONITOR");
            if (!string.IsNullOrEmpty(LastError)) sb.AppendLine("ERROR: " + LastError);
            if (!ZoneFound) sb.AppendLine("Time zone '" + TimeZoneId + "' not found: using chart time");
            var st = State;
            if (st == null) { sb.AppendLine("waiting for data"); return sb.ToString(); }
            if (Bars.Count > 0) sb.AppendFormat("Last bar {0:ddd dd MMM HH:mm} exchange time - {1}\n", Bars[Bars.Count - 1].Time, st.InRth ? "cash session" : "outside the cash session");
            if (!st.Ready) { sb.AppendLine("collecting the first full cash session..."); return sb.ToString(); }

            sb.AppendFormat("Bias: {0}  ({1})\n", st.Final, st.BiasNote);
            sb.AppendFormat("  last value shift {0} | 5-session narrative {1} | vs 3-month value {2}\n", st.DailyShift, st.Narrative, st.Auction);
            sb.AppendFormat("Prev POC {0}  VAH {1}  VAL {2}  | price {3} prior value\n", st.Prior.Stats.Poc, st.Prior.Stats.Vah, st.Prior.Stats.Val,
                st.Location == Core.ValueLocation.Inside ? "inside" : st.Location == Core.ValueLocation.AcceptedAbove ? "accepted above" : "accepted below");
            if (st.Vwap > 0) sb.AppendFormat("Session VWAP {0:0.00} (price {1}, context only)\n", st.Vwap, st.LastPrice >= st.Vwap ? "above" : "below");
            if (st.Ib != null && st.Ib.Started) sb.AppendFormat("IB {0} - {1}{2}\n", st.Ib.Low, st.Ib.High, st.Ib.Complete ? "" : " (building)");

            if (Trades != null)
            {
                var s = Trades.Stats; int open = Trades.Trades.Count(t => t.ExitReason == "open");
                sb.AppendFormat("History on this chart: {0} trades{1}", s.Trades, open > 0 ? " (+1 open)" : "");
                if (s.Trades > 0) sb.AppendFormat("  win {0:P0}  total {1:0.0}R  avg {2:0.00}R  max DD {3:0.0}R", s.WinRate, s.TotalR, s.ExpectancyR, s.MaxDrawdownR);
                sb.AppendLine();
            }
            sb.AppendFormat("Signals this session: {0}/{1}\n", st.SignalsThisSession, P.MaxSignalsPerSession);
            if (showChecklist && st.Best != null) sb.Append(st.Best.ToChecklist());
            return sb.ToString();
        }
    }
}
