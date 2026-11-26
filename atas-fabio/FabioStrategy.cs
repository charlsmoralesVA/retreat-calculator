// FabioStrategy.cs - ATAS chart strategy skeleton. NOT COMPILED, NOT TESTED.
// Defaults to SIGNAL-ONLY (TradingEnabled = false). Test on a demo/sim account first.
// ATAS strategy API names (ChartStrategy, OpenPosition, CloseCurrentPosition, CurrentPosition,
// OnCalculate) are from memory; verify against the SDK samples shipped with ATAS.
using System;
using System.ComponentModel;
using ATAS.Strategies.Chart;
using ATAS.Indicators;
using FabioOrderflow;

namespace FabioOrderflow.Atas
{
    [DisplayName("Fabio Orderflow Strategy")]
    public class FabioStrategy : ChartStrategy
    {
        private readonly FabioLevelsIndicator _ind = new FabioLevelsIndicator();

        [DisplayName("Trading enabled (OFF = signals only)")] public bool TradingEnabled { get; set; } = false;
        [DisplayName("Contracts")] public int Contracts { get; set; } = 1;
        [DisplayName("Risk:Reward")] public decimal RewardRisk { get; set; } = 2m;   // SKILL.md: commonly 1:2
        [DisplayName("Stop buffer (ticks)")] public int StopBufferTicks { get; set; } = 2;
        [DisplayName("Max trades/day")] public int MaxTradesPerDay { get; set; } = 3;
        [DisplayName("Max daily loss (currency)")] public decimal MaxDailyLoss { get; set; } = 200m;

        private int _tradesToday;
        private DateTime _day;

        public FabioStrategy() { }

        protected override void OnInitialize() => Add(_ind);

        protected override void OnCalculate(int bar, decimal value)
        {
            if (bar != CurrentBar - 1) return;          // act on the last closed/forming bar only
            var c = GetCandle(bar);
            if (c.Time.Date != _day) { _day = c.Time.Date; _tradesToday = 0; }
            if (_tradesToday >= MaxTradesPerDay) return; // "don't be a machine gun"
            // TODO: daily-loss kill switch using realized PnL
            if (CurrentPosition != 0) return;            // one position at a time

            bool goLong = ((ValueDataSeries)_ind.DataSeries[3])[bar] != 0;
            bool goShort = ((ValueDataSeries)_ind.DataSeries[4])[bar] != 0;
            if (!goLong && !goShort) return;

            decimal tick = InstrumentInfo.TickSize;
            decimal entry = c.Close;
            decimal stop = goLong ? GetCandle(bar - 1).Low - StopBufferTicks * tick
                                  : GetCandle(bar - 1).High + StopBufferTicks * tick; // "just beyond the zone"
            decimal risk = Math.Abs(entry - stop);
            decimal target = goLong ? entry + RewardRisk * risk : entry - RewardRisk * risk;
            // TODO: target = previous control level / POC / value-area edge when closer than 1:2.

            AddLog($"{(goLong ? "LONG" : "SHORT")} signal @ {entry} stop {stop} tp {target} bias {_ind.CurrentBias}");
            if (!TradingEnabled) return;

            // TODO: place entry + attached stop/target orders per the ATAS strategy sample
            // OpenPosition(goLong ? "FabioLong" : "FabioShort", c, Contracts);
            _tradesToday++;
        }
    }
}
