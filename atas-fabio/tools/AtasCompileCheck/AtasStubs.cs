// HAND-WRITTEN STUBS of the ATAS SDK, used only to compile-check and exercise the adapter on Linux.
// Members marked "test seam" do not exist in ATAS; they let the tests drive the indicator and strategy like the platform does.
// Every member below was taken from how ATAS's own open-source indicators (github.com/AtasPlatform/indicators) or a
// working public ATAS strategy use the API: names, parameter order, property-vs-field. The namespaces of a few types
// are inferred from the `using` lines in those files. This is still NOT the real SDK: build against your ATAS install.
using System;
using System.Collections.Generic;
using System.Drawing;

namespace ATAS.Indicators
{
    public enum VisualMode { Line, Histogram, Hide, Candles, Square, Hash, Dots, OnlyValueOnAxis, Bars, UpArrow, DownArrow, HighLow, Cross, Block }
    [Flags] public enum DrawingLayouts { None = 0, Historical = 1, LatestBar = 2, Final = 4 }
    public enum TradeDirection { Between, Buy, Sell }

    public struct CrossColor { }
    public static class CrossColorExtensions { public static CrossColor Convert(this Color c) { return new CrossColor(); } }

    public class PriceLevel { public decimal Price { get; set; } public decimal Bid { get; set; } public decimal Ask { get; set; } public decimal Volume { get; set; } }

    public class IndicatorCandle
    {
        public DateTime Time { get; set; }
        public DateTime LastTime { get; set; }
        public decimal Open { get; set; } public decimal High { get; set; } public decimal Low { get; set; } public decimal Close { get; set; }
        public decimal Volume { get; set; } public decimal Delta { get; set; }
        public List<PriceLevel> TestLevels = new List<PriceLevel>();
        public IEnumerable<PriceLevel> GetAllPriceLevels() { return TestLevels; }
    }

    public class CumulativeTrade
    {
        public DateTime Time { get; set; } public decimal FirstPrice { get; set; } public decimal Lastprice { get; set; }
        public decimal Volume { get; set; } public TradeDirection Direction { get; set; }
    }

    public interface IDataSeries { void Clear(); }

    public class ValueDataSeries : IDataSeries
    {
        public ValueDataSeries(string name) { }
        public ValueDataSeries(string id, string name) { }
        public VisualMode VisualType { get; set; }
        public CrossColor Color { get; set; }
        public int Width { get; set; }
        public bool ShowZeroValue { get; set; }
        public bool IsHidden { get; set; }
        public bool ShowCurrentValue { get; set; }
        // test seam: remembers what the indicator drew
        public readonly Dictionary<int, decimal> Values = new Dictionary<int, decimal>();
        public decimal this[int bar] { get { decimal v; return Values.TryGetValue(bar, out v) ? v : 0; } set { if (value == 0) Values.Remove(bar); else Values[bar] = value; } }
        public void Clear() { Values.Clear(); }
    }

    // ATAS creates a default series at index 0 (indicators replace it with DataSeries[0] = ...)
    public class DataSeriesCollection : List<IDataSeries> { public DataSeriesCollection() { Add(new ValueDataSeries("Default")); } }

    public class InstrumentInfoStub
    {
        public decimal TickSize { get; set; } = 0.25m;
        public TimeSpan TimeZoneOffset { get; set; }
        public string Instrument { get; set; } = "";
    }

    public class ChartInfoStub
    {
        public Rectangle Region { get; set; }
        public int GetXByBar(int bar, bool isStartOfBar = true) { return 0; }
        public int GetYByPrice(decimal price, bool isStartOfBar = true) { return 0; }
    }

    public abstract class Indicator
    {
        protected Indicator(bool useCandles = false) { }
        public DataSeriesCollection DataSeries { get; } = new DataSeriesCollection();
        public InstrumentInfoStub InstrumentInfo { get; } = new InstrumentInfoStub();
        public ChartInfoStub ChartInfo { get; } = new ChartInfoStub();
        public int CurrentBar { get { return TestCurrentBar; } }
        // test seams (not part of ATAS): drive the indicator like the platform does
        public int TestCurrentBar;
        public Func<int, IndicatorCandle> TestCandles;
        public readonly List<string> TestAlerts = new List<string>();
        public bool EnableCustomDrawing { get; set; }
        public bool DenyToChangePanel { get; set; }
        public bool DrawAbovePrice { get; set; }
        public IndicatorCandle GetCandle(int bar) { return TestCandles != null ? TestCandles(bar) : new IndicatorCandle(); }
        public bool IsNewSession(int bar) { return false; }
        protected void RecalculateValues() { }
        protected void SubscribeToDrawingEvents(DrawingLayouts layouts) { }
        protected void AddAlert(string alertFile, string message) { TestAlerts.Add(message); }
        protected abstract void OnCalculate(int bar, decimal value);
        protected virtual void OnRender(OFT.Rendering.Context.RenderContext context, DrawingLayouts layout) { }
        protected virtual void OnCumulativeTrade(CumulativeTrade trade) { }
        protected virtual void OnUpdateCumulativeTrade(CumulativeTrade trade) { }
    }
}

namespace OFT.Rendering.Tools
{
    public class RenderFont { public RenderFont(string family, float size) { } }
}

namespace OFT.Rendering.Settings
{
    public class RenderStringFormat { }
}

namespace OFT.Rendering.Context
{
    using OFT.Rendering.Settings;
    using OFT.Rendering.Tools;

    public class RenderContext
    {
        public void DrawString(string text, RenderFont font, Color color, Rectangle rect, RenderStringFormat format) { }
        public void FillRectangle(Color color, Rectangle rect) { }
        public Size MeasureString(string text, RenderFont font) { return new Size(7 * text.Length, 14); }
    }
}

namespace Utils.Common.Logging
{
    public enum LoggingLevel { Verbose, Debug, Info, Warning, Error, Critical }
}

namespace ATAS.DataFeedsCore
{
    public enum OrderDirections { Buy, Sell }
    public enum OrderTypes { Market, Limit, Stop, StopLimit }
    public class Portfolio { }
    public class Security { }
    public class Order
    {
        public Portfolio Portfolio { get; set; } public Security Security { get; set; }
        public OrderDirections Direction { get; set; } public OrderTypes Type { get; set; }
        public decimal QuantityToFill { get; set; } public decimal Price { get; set; } public decimal TriggerPrice { get; set; }
        public string Comment { get; set; }
    }
}

namespace ATAS.Strategies.Chart
{
    using ATAS.DataFeedsCore;
    using ATAS.Indicators;
    using Utils.Common.Logging;

    public abstract class ChartStrategy : Indicator
    {
        protected ChartStrategy() : base(true) { }
        public Portfolio Portfolio { get; }
        public Security Security { get; }
        public decimal CurrentPosition { get { return TestPosition; } }
        // test seams (not part of ATAS)
        public decimal TestPosition;
        public readonly List<Order> TestOrders = new List<Order>();
        public readonly List<Order> TestCancelled = new List<Order>();
        public readonly List<string> TestNotifications = new List<string>();
        protected bool CanProcess(int bar) { return true; }
        protected void OpenOrder(Order order) { TestOrders.Add(order); }
        protected void CancelOrder(Order order) { TestCancelled.Add(order); }
        protected void RaiseShowNotification(string message, string title = "", LoggingLevel level = LoggingLevel.Info) { TestNotifications.Add(message); }
        protected virtual void OnOrderChanged(Order order) { }
        protected virtual void OnStopping() { }
    }
}
