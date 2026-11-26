// Fabio Orderflow Strategy - ATAS chart strategy skeleton.
// SIGNAL-ONLY by default. Order placement is deliberately left as a TODO because the ATAS order API
// (OpenPosition / CloseCurrentPosition / stop and target orders) differs between versions and cannot be
// checked from outside ATAS; wire it from the sample strategy that ships with your ATAS SDK.
//
// Discipline built into the engine (not here): bias gate, battle-of-orders confirmation, stop beyond the
// zone, ~1:2 target, max signals per session, minimum bars between signals.
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ATAS.Strategies.Chart;
using FabioOrderflow.Core;

namespace FabioOrderflow.Atas
{
    [DisplayName("Fabio Orderflow Strategy")]
    public class FabioStrategy : ChartStrategy
    {
        private readonly FabioFeed _feed = new FabioFeed();
        [Display(Name = "Trading enabled (OFF = log signals only)", GroupName = "Execution", Order = 1)]
        public bool TradingEnabled { get; set; } = false;
        [Display(Name = "Contracts", GroupName = "Execution", Order = 2)]
        public int Contracts { get; set; } = 1;
        [Display(Name = "Time offset (hours)", GroupName = "Session", Order = 1)]
        public double TimeOffsetHours { get { return _feed.TimeOffsetHours; } set { _feed.TimeOffsetHours = value; } }

        protected override void OnCalculate(int bar, decimal value)
        {
            _feed.P.TickSize = InstrumentInfo.TickSize;
            var st = _feed.Process(GetCandle, bar, CurrentBar);

            foreach (var s in st.NewSignals)
            {
                // only act on a signal for the bar that just closed, never on historical replay
                if (s.ConfirmGlobalIndex < CurrentBar - 2) continue;

                string msg = string.Format("{0} {1} entry {2} stop {3} target {4} R:R {5:0.00}",
                    s.Side, s.Tag, s.Entry, s.Stop, s.Target, s.Rr);
                this.LogInfo(msg);                       // ATAS strategy logging (verify name in your SDK)
                if (!TradingEnabled) continue;

                // TODO(order placement): open `Contracts` in direction s.Side at market or on a retest,
                //   attach stop = s.Stop and target = s.Target (OCO), one position at a time,
                //   and add a max-daily-loss kill switch. Use the ATAS sample strategy for the exact calls.
            }
        }
    }
}
