// Fabio Orderflow Monitor - ATAS indicator.
//  * prior-session POC / VAH / VAL, 3-month composition value area, session VWAP, initial balance
//  * set-up arrows on the bar that confirmed the battle of orders
//  * entry / stop / target of every set-up over ALL loaded history, with the result (target = green square,
//    stop = red cross, session end = dot) and the history statistics in the panel
//  * a live checklist for the nearest zone
// The ATAS API calls follow ATAS's own open-source indicators (github.com/AtasPlatform/indicators).
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Drawing;
using System.Linq;
using ATAS.Indicators;
using OFT.Rendering.Context;
using OFT.Rendering.Settings;
using OFT.Rendering.Tools;
using Core = FabioOrderflow.Core;

namespace FabioOrderflow.Atas
{
    [DisplayName("Fabio Orderflow Monitor")]
    public class FabioLevelsIndicator : Indicator
    {
        private readonly FabioFeed _feed = new FabioFeed();

        private readonly ValueDataSeries _poc = new ValueDataSeries("FabioPrevPoc", "Prev POC") { VisualType = VisualMode.Line, Color = System.Drawing.Color.Gold.Convert(), Width = 2 };
        private readonly ValueDataSeries _vah = new ValueDataSeries("FabioPrevVah", "Prev VAH") { VisualType = VisualMode.Line, Color = System.Drawing.Color.IndianRed.Convert(), Width = 1 };
        private readonly ValueDataSeries _val = new ValueDataSeries("FabioPrevVal", "Prev VAL") { VisualType = VisualMode.Line, Color = System.Drawing.Color.MediumSeaGreen.Convert(), Width = 1 };
        private readonly ValueDataSeries _compVah = new ValueDataSeries("FabioCompVah", "3-month VAH") { VisualType = VisualMode.Hash, Color = System.Drawing.Color.Gray.Convert(), Width = 1 };
        private readonly ValueDataSeries _compVal = new ValueDataSeries("FabioCompVal", "3-month VAL") { VisualType = VisualMode.Hash, Color = System.Drawing.Color.Gray.Convert(), Width = 1 };
        private readonly ValueDataSeries _vwap = new ValueDataSeries("FabioVwap", "Session VWAP") { VisualType = VisualMode.Line, Color = System.Drawing.Color.DeepSkyBlue.Convert(), Width = 1 };
        private readonly ValueDataSeries _ibHigh = new ValueDataSeries("FabioIbHigh", "IB high") { VisualType = VisualMode.Hash, Color = System.Drawing.Color.Orchid.Convert(), Width = 1 };
        private readonly ValueDataSeries _ibLow = new ValueDataSeries("FabioIbLow", "IB low") { VisualType = VisualMode.Hash, Color = System.Drawing.Color.Orchid.Convert(), Width = 1 };
        private readonly ValueDataSeries _longSig = new ValueDataSeries("FabioLongSetup", "Long set-up") { VisualType = VisualMode.UpArrow, Color = System.Drawing.Color.Lime.Convert(), Width = 2 };
        private readonly ValueDataSeries _shortSig = new ValueDataSeries("FabioShortSetup", "Short set-up") { VisualType = VisualMode.DownArrow, Color = System.Drawing.Color.Red.Convert(), Width = 2 };
        private readonly ValueDataSeries _entry = new ValueDataSeries("FabioEntry", "Trade entry") { VisualType = VisualMode.Hash, Color = System.Drawing.Color.White.Convert(), Width = 2 };
        private readonly ValueDataSeries _stop = new ValueDataSeries("FabioStop", "Trade stop") { VisualType = VisualMode.Hash, Color = System.Drawing.Color.Red.Convert(), Width = 2 };
        private readonly ValueDataSeries _target = new ValueDataSeries("FabioTarget", "Trade target") { VisualType = VisualMode.Hash, Color = System.Drawing.Color.Lime.Convert(), Width = 2 };
        private readonly ValueDataSeries _win = new ValueDataSeries("FabioWin", "Exit at target") { VisualType = VisualMode.Square, Color = System.Drawing.Color.Lime.Convert(), Width = 3 };
        private readonly ValueDataSeries _loss = new ValueDataSeries("FabioLoss", "Exit at stop") { VisualType = VisualMode.Cross, Color = System.Drawing.Color.Red.Convert(), Width = 3 };
        private readonly ValueDataSeries _flat = new ValueDataSeries("FabioFlat", "Exit at session end") { VisualType = VisualMode.Dots, Color = System.Drawing.Color.Silver.Convert(), Width = 3 };

        private RenderFont _font = new RenderFont("Consolas", 9);
        private readonly RenderStringFormat _format = new RenderStringFormat();   // default = top-left text
        private int _lastAlertBar = -1;

        public FabioLevelsIndicator() : base(true)
        {
            DenyToChangePanel = true;
            EnableCustomDrawing = true;
            SubscribeToDrawingEvents(DrawingLayouts.Final);

            DataSeries[0] = _poc;
            DataSeries.Add(_vah); DataSeries.Add(_val); DataSeries.Add(_compVah); DataSeries.Add(_compVal); DataSeries.Add(_vwap);
            DataSeries.Add(_ibHigh); DataSeries.Add(_ibLow); DataSeries.Add(_longSig); DataSeries.Add(_shortSig);
            DataSeries.Add(_entry); DataSeries.Add(_stop); DataSeries.Add(_target); DataSeries.Add(_win); DataSeries.Add(_loss); DataSeries.Add(_flat);
            foreach (var s in new[] { _poc, _vah, _val, _compVah, _compVal, _vwap, _ibHigh, _ibLow, _longSig, _shortSig, _entry, _stop, _target, _win, _loss, _flat })
                s.ShowZeroValue = false;
        }

        public FabioFeed Feed { get { return _feed; } }

        // ---------------------------------------------------------------- settings
        [Display(Name = "Exchange time zone", GroupName = "1. Session", Order = 10, Description = "Clock of the cash session. ES/NQ: America/New_York (or Eastern Standard Time).")]
        public string TimeZoneId { get { return _feed.TimeZoneId; } set { _feed.TimeZoneId = value; RecalculateValues(); } }
        [Display(Name = "Cash session start (hh:mm)", GroupName = "1. Session", Order = 20)]
        public string RthStart { get { return _feed.P.RthStart.ToString(@"hh\:mm"); } set { TimeSpan t; if (TimeSpan.TryParse(value, out t)) { _feed.P.RthStart = t; RecalculateValues(); } } }
        [Display(Name = "Cash session end (hh:mm)", GroupName = "1. Session", Order = 30)]
        public string RthEnd { get { return _feed.P.RthEnd.ToString(@"hh\:mm"); } set { TimeSpan t; if (TimeSpan.TryParse(value, out t)) { _feed.P.RthEnd = t; RecalculateValues(); } } }

        [Display(Name = "Require bias alignment", GroupName = "2. Rules", Order = 10)]
        public bool RequireBias { get { return _feed.P.RequireBiasAlignment; } set { _feed.P.RequireBiasAlignment = value; RecalculateValues(); } }
        [Display(Name = "Require reactor approach filter", GroupName = "2. Rules", Order = 20)]
        public bool RequireApproach { get { return _feed.P.RequireApproachFilter; } set { _feed.P.RequireApproachFilter = value; RecalculateValues(); } }
        [Display(Name = "Zone tolerance (ticks)", GroupName = "2. Rules", Order = 30)]
        public decimal ZoneTicks { get { return _feed.P.ZoneToleranceTicks; } set { _feed.P.ZoneToleranceTicks = Math.Max(0, value); RecalculateValues(); } }
        [Display(Name = "Absorption: |delta| x average", GroupName = "2. Rules", Order = 40)]
        public decimal AbsorbMult { get { return _feed.P.AbsorbDeltaMult; } set { _feed.P.AbsorbDeltaMult = Math.Max(0, value); RecalculateValues(); } }
        [Display(Name = "Confirmation: delta x average", GroupName = "2. Rules", Order = 50)]
        public decimal ConfirmMult { get { return _feed.P.ConfirmDeltaMult; } set { _feed.P.ConfirmDeltaMult = Math.Max(0, value); RecalculateValues(); } }
        [Display(Name = "Big trade filter (contracts)", GroupName = "2. Rules", Order = 60, Description = "SKILL.md: ~60-75 contracts on the 5-minute chart")]
        public int BigTradeMin { get { return _feed.P.BigTradeMin; } set { _feed.P.BigTradeMin = Math.Max(1, value); RecalculateValues(); } }
        [Display(Name = "Trade delta levels as zones", GroupName = "2. Rules", Order = 70)]
        public bool UseDelta { get { return _feed.P.UseDeltaLevels; } set { _feed.P.UseDeltaLevels = value; RecalculateValues(); } }
        [Display(Name = "Enable IVB zones (provisional)", GroupName = "2. Rules", Order = 80)]
        public bool EnableIvb { get { return _feed.P.EnableIvb; } set { _feed.P.EnableIvb = value; RecalculateValues(); } }
        [Display(Name = "Max set-ups per session", GroupName = "2. Rules", Order = 90)]
        public int MaxSignals { get { return _feed.P.MaxSignalsPerSession; } set { _feed.P.MaxSignalsPerSession = Math.Max(1, value); RecalculateValues(); } }

        [Display(Name = "Show trades (entry/stop/target/result)", GroupName = "3. Display", Order = 10)]
        public bool ShowTrades { get; set; } = true;
        [Display(Name = "Show panel", GroupName = "3. Display", Order = 20)]
        public bool ShowPanel { get; set; } = true;
        [Display(Name = "Show checklist in panel", GroupName = "3. Display", Order = 30)]
        public bool ShowChecklist { get; set; } = true;
        [Display(Name = "Slippage for history stats (ticks)", GroupName = "3. Display", Order = 40)]
        public decimal SlippageTicks { get { return _feed.Bt.SlippageTicks; } set { _feed.Bt.SlippageTicks = Math.Max(0, value); RecalculateValues(); } }

        [Display(Name = "Alert on new set-up", GroupName = "4. Alerts", Order = 10)]
        public bool UseAlerts { get; set; } = true;
        [Display(Name = "Alert sound file", GroupName = "4. Alerts", Order = 20)]
        public string AlertFile { get; set; } = "alert1";

        // ---------------------------------------------------------------- calculation
        protected override void OnCalculate(int bar, decimal value)
        {
            try
            {
                if (bar == 0) DataSeries.ForEach(x => x.Clear());
                _feed.P.TickSize = InstrumentInfo.TickSize;
                var st = _feed.Process(GetCandle(bar), bar, CurrentBar, InstrumentInfo.TimeZoneOffset);

                if (st.InRth && st.Ready)
                {
                    _poc[bar] = st.Prior.Stats.Poc; _vah[bar] = st.Prior.Stats.Vah; _val[bar] = st.Prior.Stats.Val;
                    if (st.Composition != null) { _compVah[bar] = st.Composition.Vah; _compVal[bar] = st.Composition.Val; }
                    if (st.Ib != null && st.Ib.Started) { _ibHigh[bar] = st.Ib.High; _ibLow[bar] = st.Ib.Low; }
                }
                if (st.InRth && st.Vwap > 0) _vwap[bar] = st.Vwap;

                // a set-up belongs to the bar that just CLOSED, so the arrow goes on that bar
                foreach (var s in st.NewSignals)
                {
                    var c = GetCandle(s.ConfirmGlobalIndex);
                    if (s.Side == Core.Side.Long) _longSig[s.ConfirmGlobalIndex] = c.Low - 2 * InstrumentInfo.TickSize;
                    else _shortSig[s.ConfirmGlobalIndex] = c.High + 2 * InstrumentInfo.TickSize;

                    if (UseAlerts && bar >= CurrentBar - 1 && _lastAlertBar != s.ConfirmGlobalIndex)
                    {
                        _lastAlertBar = s.ConfirmGlobalIndex;
                        AddAlert(AlertFile, string.Format("Fabio {0} {1}: entry {2} stop {3} target {4} (R:R {5:0.0})", s.Side, s.Tag, s.Entry, s.Stop, s.Target, s.Rr));
                    }
                }

                if (ShowTrades && _feed.Trades != null && bar >= CurrentBar - 1) DrawTrades();
            }
            catch (Exception e)
            {
                _feed.LastError = e.Message;   // shown in the panel instead of breaking the chart
            }
        }

        private void DrawTrades()
        {
            foreach (var t in _feed.Trades.Trades)
            {
                for (int j = t.EntryBar; j <= t.ExitBar && j < _feed.Bars.Count; j++)
                {
                    _entry[j] = t.Entry; _stop[j] = t.Stop; _target[j] = t.Target;
                }
                if (t.ExitReason == "open") continue;
                if (t.ExitReason.StartsWith("target")) _win[t.ExitBar] = t.Exit;
                else if (t.ExitReason.StartsWith("stop")) _loss[t.ExitBar] = t.Exit;
                else _flat[t.ExitBar] = t.Exit;
            }
        }

        protected override void OnCumulativeTrade(CumulativeTrade trade) { Register(trade); }
        protected override void OnUpdateCumulativeTrade(CumulativeTrade trade) { Register(trade); }

        private void Register(CumulativeTrade trade)
        {
            try
            {
                _feed.RegisterCumulative(CurrentBar - 1, trade.Time, trade.FirstPrice,
                    trade.Direction == TradeDirection.Buy, trade.Direction == TradeDirection.Sell, trade.Volume);
            }
            catch (Exception e) { _feed.LastError = e.Message; }
        }

        // ---------------------------------------------------------------- panel
        protected override void OnRender(RenderContext context, DrawingLayouts layout)
        {
            if (!ShowPanel || layout != DrawingLayouts.Final) return;
            var lines = _feed.PanelText(ShowChecklist).TrimEnd('\n').Split('\n');
            int lineH = Math.Max(12, context.MeasureString("Ag", _font).Height + 2);
            int width = 0;
            foreach (var l in lines) width = Math.Max(width, context.MeasureString(l, _font).Width);
            var box = new System.Drawing.Rectangle(8, 8, width + 16, lines.Length * lineH + 12);
            context.FillRectangle(System.Drawing.Color.FromArgb(185, 12, 16, 24), box);
            int y = box.Top + 6;
            foreach (var l in lines)
            {
                var color = l.StartsWith(" [x]") ? System.Drawing.Color.LightGreen : l.StartsWith(" [ ]") ? System.Drawing.Color.Salmon : l.StartsWith("ERROR") ? System.Drawing.Color.OrangeRed
                          : l.Contains("ALL REQUIRED") ? System.Drawing.Color.Lime : System.Drawing.Color.Gainsboro;
                context.DrawString(l, _font, color, new System.Drawing.Rectangle(box.Left + 8, y, width + 8, lineH), _format);
                y += lineH;
            }
        }
    }
}
