// HAND-WRITTEN STUBS. These mirror what I BELIEVE the ATAS SDK exposes. They are not the real API.
using System;
using System.Collections.Generic;

namespace ATAS.Indicators
{
    public enum VisualMode { Line, Dots, Histogram, Hide }
    public enum DrawingLayouts { Historical, LatestCandle, Final }

    public class PriceLevel { public decimal Price, Bid, Ask, Volume; }

    public class IndicatorCandle
    {
        public DateTime Time;
        public decimal Open, High, Low, Close, Volume, Delta;
        public IEnumerable<PriceLevel> GetAllPriceLevels() { return new List<PriceLevel>(); }
    }

    public class DataSeriesBase { }

    public class ValueDataSeries : DataSeriesBase
    {
        public ValueDataSeries(string name) { }
        public VisualMode VisualType; public int Width;
        public decimal this[int bar] { get { return 0; } set { } }
    }

    public class InstrumentInfoStub { public decimal TickSize = 0.25m; }
    public class RenderFont { public RenderFont(string name, int size) { } }
    public class RenderContext { public void DrawString(string text, RenderFont font, System.Drawing.Color color, int x, int y) { } }

    public abstract class Indicator
    {
        protected Indicator(bool useCandles) { }
        public List<DataSeriesBase> DataSeries = new List<DataSeriesBase>();
        public InstrumentInfoStub InstrumentInfo = new InstrumentInfoStub();
        public int CurrentBar;
        public IndicatorCandle GetCandle(int bar) { return new IndicatorCandle(); }
        protected void RecalculateValues() { }
        protected abstract void OnCalculate(int bar, decimal value);
        protected virtual void OnRender(RenderContext context, DrawingLayouts layout) { }
    }
}

namespace ATAS.Strategies.Chart
{
    using ATAS.Indicators;

    public abstract class ChartStrategy
    {
        public InstrumentInfoStub InstrumentInfo = new InstrumentInfoStub();
        public int CurrentBar;
        public IndicatorCandle GetCandle(int bar) { return new IndicatorCandle(); }
        protected abstract void OnCalculate(int bar, decimal value);
    }

    public static class StrategyLogExtensions
    {
        public static void LogInfo(this ChartStrategy s, string message) { }
    }
}
