// FabioLevelsIndicator.cs - ATAS indicator: prior-session POC/VAH/VAL, bias, confirmation arrows.
// NOT COMPILED. ATAS SDK member names are from memory; verify against your installed ATAS version
// (reference ATAS.Indicators.dll, ATAS.DataFeedsCore.dll, Utils.Common.dll from the ATAS install folder).
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using ATAS.Indicators;
using FabioOrderflow;

namespace FabioOrderflow.Atas
{
    [DisplayName("Fabio Orderflow Levels")]
    public class FabioLevelsIndicator : Indicator
    {
        private readonly ValueDataSeries _poc = new ValueDataSeries("Prev POC");
        private readonly ValueDataSeries _vah = new ValueDataSeries("Prev VAH");
        private readonly ValueDataSeries _val = new ValueDataSeries("Prev VAL");
        private readonly ValueDataSeries _long = new ValueDataSeries("Long confirm");
        private readonly ValueDataSeries _short = new ValueDataSeries("Short confirm");

        private readonly List<SessionProfile> _sessions = new List<SessionProfile>();
        private Dictionary<decimal, decimal> _curVol = new Dictionary<decimal, decimal>();
        private DateTime _curDate;
        private readonly FabioCore.ConfirmParams _cp = new FabioCore.ConfirmParams();

        [DisplayName("Absorb |delta| min")] public decimal AbsorbDelta { get => _cp.MinAbsorbDelta; set => _cp.MinAbsorbDelta = value; }
        [DisplayName("Confirm delta min")] public decimal ConfirmDelta { get => _cp.MinConfirmDelta; set => _cp.MinConfirmDelta = value; }
        [DisplayName("Zone tolerance")] public decimal ZoneTol { get => _cp.ZoneTolerance; set => _cp.ZoneTolerance = value; }

        public Bias CurrentBias { get; private set; } = Bias.Neutral;

        public FabioLevelsIndicator() : base(true)
        {
            DataSeries[0] = _poc; DataSeries.Add(_vah); DataSeries.Add(_val);
            DataSeries.Add(_long); DataSeries.Add(_short);
        }

        protected override void OnCalculate(int bar, decimal value)
        {
            var c = GetCandle(bar);
            var date = c.Time.Date; // TODO: use cash-session boundaries (RTH), not calendar days

            if (bar == 0 || date != _curDate)
            {
                if (_curVol.Count > 0)
                {
                    var p = FabioCore.BuildProfile(_curDate, _curVol);
                    if (p != null) _sessions.Add(p);
                }
                _curVol = new Dictionary<decimal, decimal>();
                _curDate = date;
                CurrentBias = FabioCore.NarrativeBias(_sessions);
            }

            // accumulate volume per price level (footprint data)
            foreach (var lvl in c.GetAllPriceLevels())
            {
                _curVol.TryGetValue(lvl.Price, out var v);
                _curVol[lvl.Price] = v + lvl.Volume;
            }

            var prev = _sessions.LastOrDefault();
            if (prev == null || bar < 1) return;
            _poc[bar] = prev.Poc; _vah[bar] = prev.Vah; _val[bar] = prev.Val;

            var absorb = ToBar(GetCandle(bar - 1));
            var confirm = ToBar(c);

            // Bias gate: only take longs in bullish/neutral (at VAL), shorts in bearish/neutral (at VAH).
            // TODO: encode Fabio's exact trade-with/against-bias rules from bias-and-mapping.md.
            if (CurrentBias != Bias.Bearish && FabioCore.LongConfirmed(absorb, confirm, prev.Val, _cp))
                _long[bar] = c.Low - 2 * InstrumentInfo.TickSize;
            if (CurrentBias != Bias.Bullish && FabioCore.ShortConfirmed(absorb, confirm, prev.Vah, _cp))
                _short[bar] = c.High + 2 * InstrumentInfo.TickSize;
        }

        private static FabioCore.Bar ToBar(IndicatorCandle c) => new FabioCore.Bar
        {
            Open = c.Open, High = c.High, Low = c.Low, Close = c.Close,
            Volume = c.Volume, Delta = c.Delta
            // TODO: fill BigTradeCount* (needs trade-level data; SKILL.md: ~60-75 contracts on 5m)
        };
    }
}
