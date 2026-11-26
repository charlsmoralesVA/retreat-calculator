// Fabio Orderflow Monitor - ATAS indicator.
// Draws prior-session POC/VAH/VAL and the composition value area, marks confirmed set-ups, and shows
// a live checklist (bias -> location -> approach -> battle of orders -> exits) for the nearest zone.
//
// ATAS-specific surface (verify against your ATAS SDK version if the build complains):
//   Indicator, OnCalculate(int, decimal), GetCandle, CurrentBar, InstrumentInfo.TickSize,
//   DataSeries / ValueDataSeries / VisualMode, OnRender(RenderContext, DrawingLayouts) [panel only]
// Define NO_PANEL in the project to drop the on-chart panel if OnRender does not match your version.
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ATAS.Indicators;
using FabioOrderflow.Core;

namespace FabioOrderflow.Atas
{
    [DisplayName("Fabio Orderflow Monitor")]
    public class FabioLevelsIndicator : Indicator
    {
        private readonly FabioFeed _feed = new FabioFeed();
        private readonly ValueDataSeries _poc = new ValueDataSeries("Prev POC");
        private readonly ValueDataSeries _vah = new ValueDataSeries("Prev VAH");
        private readonly ValueDataSeries _val = new ValueDataSeries("Prev VAL");
        private readonly ValueDataSeries _compVah = new ValueDataSeries("Composition VAH");
        private readonly ValueDataSeries _compVal = new ValueDataSeries("Composition VAL");
        private readonly ValueDataSeries _ibHigh = new ValueDataSeries("IB high");
        private readonly ValueDataSeries _ibLow = new ValueDataSeries("IB low");
        private readonly ValueDataSeries _longSig = new ValueDataSeries("Long set-up");
        private readonly ValueDataSeries _shortSig = new ValueDataSeries("Short set-up");

        public FabioLevelsIndicator() : base(true)
        {
            DataSeries[0] = _poc;
            DataSeries.Add(_vah); DataSeries.Add(_val);
            DataSeries.Add(_compVah); DataSeries.Add(_compVal);
            DataSeries.Add(_ibHigh); DataSeries.Add(_ibLow);
            DataSeries.Add(_longSig); DataSeries.Add(_shortSig);
            _longSig.VisualType = VisualMode.Dots; _shortSig.VisualType = VisualMode.Dots;
            _longSig.Width = 6; _shortSig.Width = 6;
        }

        public FabioFeed Feed { get { return _feed; } }

        // ---------------------------------------------------------------- settings
        [Display(Name = "Time offset (hours)", GroupName = "Session", Order = 1)]
        public double TimeOffsetHours { get { return _feed.TimeOffsetHours; } set { _feed.TimeOffsetHours = value; RecalculateValues(); } }
        [Display(Name = "RTH start (hh:mm)", GroupName = "Session", Order = 2)]
        public string RthStart { get { return _feed.P.RthStart.ToString(@"hh\:mm"); } set { TimeSpan t; if (TimeSpan.TryParse(value, out t)) { _feed.P.RthStart = t; RecalculateValues(); } } }
        [Display(Name = "RTH end (hh:mm)", GroupName = "Session", Order = 3)]
        public string RthEnd { get { return _feed.P.RthEnd.ToString(@"hh\:mm"); } set { TimeSpan t; if (TimeSpan.TryParse(value, out t)) { _feed.P.RthEnd = t; RecalculateValues(); } } }

        [Display(Name = "Require bias alignment", GroupName = "Rules", Order = 1)]
        public bool RequireBias { get { return _feed.P.RequireBiasAlignment; } set { _feed.P.RequireBiasAlignment = value; RecalculateValues(); } }
        [Display(Name = "Require reactor approach filter", GroupName = "Rules", Order = 2)]
        public bool RequireApproach { get { return _feed.P.RequireApproachFilter; } set { _feed.P.RequireApproachFilter = value; RecalculateValues(); } }
        [Display(Name = "Zone tolerance (ticks)", GroupName = "Rules", Order = 3)]
        public decimal ZoneTicks { get { return _feed.P.ZoneToleranceTicks; } set { _feed.P.ZoneToleranceTicks = value; RecalculateValues(); } }
        [Display(Name = "Absorb |delta| x average", GroupName = "Rules", Order = 4)]
        public decimal AbsorbMult { get { return _feed.P.AbsorbDeltaMult; } set { _feed.P.AbsorbDeltaMult = value; RecalculateValues(); } }
        [Display(Name = "Confirm delta x average", GroupName = "Rules", Order = 5)]
        public decimal ConfirmMult { get { return _feed.P.ConfirmDeltaMult; } set { _feed.P.ConfirmDeltaMult = value; RecalculateValues(); } }
        [Display(Name = "Use delta levels", GroupName = "Rules", Order = 6)]
        public bool UseDelta { get { return _feed.P.UseDeltaLevels; } set { _feed.P.UseDeltaLevels = value; RecalculateValues(); } }
        [Display(Name = "Enable IVB (provisional)", GroupName = "Rules", Order = 7)]
        public bool EnableIvb { get { return _feed.P.EnableIvb; } set { _feed.P.EnableIvb = value; RecalculateValues(); } }
        [Display(Name = "Max signals / session", GroupName = "Rules", Order = 8)]
        public int MaxSignals { get { return _feed.P.MaxSignalsPerSession; } set { _feed.P.MaxSignalsPerSession = value; RecalculateValues(); } }

        // ---------------------------------------------------------------- calculation
        protected override void OnCalculate(int bar, decimal value)
        {
            _feed.P.TickSize = InstrumentInfo.TickSize;
            var st = _feed.Process(GetCandle, bar, CurrentBar);

            if (st.InRth && st.Ready)
            {
                _poc[bar] = st.Prior.Stats.Poc;
                _vah[bar] = st.Prior.Stats.Vah;
                _val[bar] = st.Prior.Stats.Val;
                if (st.Composition != null) { _compVah[bar] = st.Composition.Vah; _compVal[bar] = st.Composition.Val; }
                if (st.Ib != null && st.Ib.Started) { _ibHigh[bar] = st.Ib.High; _ibLow[bar] = st.Ib.Low; }
            }

            // a signal belongs to the bar that just CLOSED (the previous one), so mark that bar
            foreach (var s in st.NewSignals)
            {
                var c = GetCandle(s.ConfirmGlobalIndex);
                if (s.Side == Side.Long) _longSig[s.ConfirmGlobalIndex] = c.Low - 2 * InstrumentInfo.TickSize;
                else _shortSig[s.ConfirmGlobalIndex] = c.High + 2 * InstrumentInfo.TickSize;
            }
        }

#if !NO_PANEL
        protected override void OnRender(RenderContext context, DrawingLayouts layout)
        {
            if (layout != DrawingLayouts.Final) return;
            var font = new RenderFont("Consolas", 9);
            int y = 30;
            foreach (var line in _feed.PanelText().Split('\n'))
            {
                context.DrawString(line, font, System.Drawing.Color.White, 10, y);
                y += 14;
            }
        }
#endif
    }
}
