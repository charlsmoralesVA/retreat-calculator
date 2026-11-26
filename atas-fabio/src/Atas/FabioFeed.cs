// Shared glue between ATAS candles and the ATAS-independent FabioEngine.
// ATAS-specific surface used here (verify against your ATAS SDK version):
//   IndicatorCandle: Time, Open, High, Low, Close, Volume, Delta, GetAllPriceLevels()
//   PriceLevel:      Price, Bid, Ask
using System;
using System.Collections.Generic;
using System.Linq;
using ATAS.Indicators;
using FabioOrderflow.Core;

namespace FabioOrderflow.Atas
{
    public sealed class FabioFeed
    {
        public readonly FabioParams P = new FabioParams();

        /// <summary>Hours to ADD to candle.Time so it matches the clock the RTH session times refer to (0 if the chart is already in exchange time).</summary>
        public double TimeOffsetHours;

        public FabioEngine Engine;
        public BigTradeFilter Big;
        public EngineState State;
        public readonly List<SetupEvaluation> Signals = new List<SetupEvaluation>();

        private readonly Dictionary<int, int> _bigBuys = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _bigSells = new Dictionary<int, int>();
        private int _lastBar = -1;

        /// <summary>Call once per ATAS OnCalculate. getCandle = this.GetCandle, currentBar = this.CurrentBar.</summary>
        public EngineState Process(Func<int, IndicatorCandle> getCandle, int bar, int currentBar)
        {
            if (bar == 0 || Engine == null)
            {
                Engine = new FabioEngine(P);
                Big = new BigTradeFilter(P);
                Signals.Clear(); _bigBuys.Clear(); _bigSells.Clear(); _lastBar = -1;
            }

            if (bar > _lastBar && _lastBar >= 0)
            {
                int nb, ns; _bigBuys.TryGetValue(_lastBar, out nb); _bigSells.TryGetValue(_lastBar, out ns);
                Big.Adapt(nb + ns);   // SKILL.md: >~3 big trades per candle -> scale the filter up
            }
            _lastBar = bar;

            var c = getCandle(bar);
            var b = new Bar
            {
                Time = c.Time.AddHours(TimeOffsetHours),
                Open = c.Open, High = c.High, Low = c.Low, Close = c.Close,
                Volume = c.Volume, Delta = c.Delta
            };
            int buys, sells;
            if (_bigBuys.TryGetValue(bar, out buys)) b.BigBuys = buys; else if (_bigSells.Count > 0 || _bigBuys.Count > 0) b.BigBuys = 0;
            if (_bigSells.TryGetValue(bar, out sells)) b.BigSells = sells; else if (_bigSells.Count > 0 || _bigBuys.Count > 0) b.BigSells = 0;

            var levels = c.GetAllPriceLevels().Select(l => new PriceLevelVol(l.Price, l.Bid, l.Ask)).ToArray();
            State = Engine.Update(bar, b, levels, bar == currentBar - 1);
            Signals.AddRange(State.NewSignals);
            return State;
        }

        /// <summary>
        /// Optional hook for the big-trade info check. Call it from whichever trade callback your ATAS version offers
        /// (one call per executed trade). Without it the big-trade line stays "no trade data" and nothing else changes.
        /// </summary>
        public void RegisterTrade(int bar, decimal contracts, bool isBuy)
        {
            if (Big == null || !Big.IsBig(contracts)) return;
            var d = isBuy ? _bigBuys : _bigSells;
            int n; d.TryGetValue(bar, out n); d[bar] = n + 1;
        }

        public string PanelText()
        {
            var st = State;
            if (st == null) return "Fabio: waiting for data";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("FABIO ORDERFLOW MONITOR");
            if (!st.InRth) { sb.AppendLine("outside regular session"); return sb.ToString(); }
            if (!st.Ready) { sb.AppendLine("collecting the first full session..."); return sb.ToString(); }
            sb.AppendFormat("Bias: {0}  ({1})\n", st.Final, st.BiasNote);
            sb.AppendFormat("  last value shift {0} | narrative {1} | auction {2}\n", st.DailyShift, st.Narrative, st.Auction);
            sb.AppendFormat("Location vs prior value: {0}\n", st.Location);
            sb.AppendFormat("Prev POC {0}  VAH {1}  VAL {2}\n", st.Prior.Stats.Poc, st.Prior.Stats.Vah, st.Prior.Stats.Val);
            if (st.Ib != null && st.Ib.Started) sb.AppendFormat("IB {0}-{1}{2}\n", st.Ib.Low, st.Ib.High, st.Ib.Complete ? " (set)" : " (building)");
            if (st.Gap != null && st.Gap.Gap != 0) sb.AppendFormat("Gap {0:+0.##;-0.##} {1}\n", st.Gap.Gap, st.Gap.Filled ? "filled" : "open");
            sb.AppendFormat("Signals this session: {0}/{1}\n", st.SignalsThisSession, P.MaxSignalsPerSession);
            if (st.Best != null) sb.Append(st.Best.ToChecklist());
            return sb.ToString();
        }
    }
}
