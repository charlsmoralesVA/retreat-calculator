using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ATAS.Indicators;
using FabioOrderflow.Atas;
using FabioOrderflow.Core;

namespace FabioOrderflow.Tests
{
    /// <summary>
    /// Runs the real ATAS adapter classes (FabioLevelsIndicator, FabioStrategy) against the stubs, calling them the way
    /// ATAS does: OnCalculate(bar) for every loaded bar during history load, then repeatedly for the forming bar.
    /// Candle times are UTC, exactly like ATAS candles. This checks the adapter's logic, not ATAS's rendering.
    /// </summary>
    internal static class HostSimTests
    {
        static readonly TimeZoneInfo Ny = ExchangeTime.Find("America/New_York");

        public static void Run()
        {
            TestIndicatorHistory();
            TestIndicatorLive();
            TestStrategyOrders();
            TestStrategySessionEnd();
        }

        sealed class Scenario { public List<IndicatorCandle> Candles = new List<IndicatorCandle>(); public int Confirm; public decimal Val; public int FirstToday; }

        static IndicatorCandle ToCandle(Bar b, PriceLevelVol[] lv)
        {
            var c = new IndicatorCandle
            {
                Time = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(b.Time, DateTimeKind.Unspecified), Ny),
                Open = b.Open, High = b.High, Low = b.Low, Close = b.Close, Volume = b.Volume, Delta = b.Delta
            };
            foreach (var l in lv) c.TestLevels.Add(new PriceLevel { Price = l.Price, Bid = l.Bid, Ask = l.Ask, Volume = l.Bid + l.Ask });
            return c;
        }

        /// <summary>Six sessions of rising value, then the textbook long at the prior VAL on 8 March 2026 (US daylight-saving day).</summary>
        static Scenario Build(bool runUp, bool addEveningBar)
        {
            var sc = new Scenario();
            var p = Program.P();
            var day = new DateTime(2026, 3, 2);
            VolumeProfile last = null;
            foreach (var center in new decimal[] { 4500, 4504, 4508, 4512, 4516, 4520 })
            {
                last = new VolumeProfile();
                foreach (var kv in Synth.Session(day, center)) { foreach (var l in kv.Value) last.Add(l); sc.Candles.Add(ToCandle(kv.Key, kv.Value)); }
                day = day.AddDays(1);
            }
            var prior = last.Compute(p.TickSize, p.ValueAreaPct);
            sc.Val = prior.Val;
            var plan = Program.LongAtVal(prior, day);
            sc.FirstToday = sc.Candles.Count;
            for (int k = 0; k < plan.Bars.Count; k++) sc.Candles.Add(ToCandle(plan.Bars[k], plan.Levels[k]));
            sc.Confirm = sc.FirstToday + plan.ConfirmOffset;
            int n = plan.Bars.Count;
            if (runUp)
            {
                var r = Synth.B(Synth.Open(day, n++), sc.Val + 2.0m, sc.Val + 8m, sc.Val + 1.75m, sc.Val + 7.5m, 1500, 300);
                sc.Candles.Add(ToCandle(r, Synth.Uniform(r.Low, r.High, r.Volume, r.Delta)));
            }
            var q = Synth.B(Synth.Open(day, n++), sc.Val + 2.0m, sc.Val + 2.5m, sc.Val + 1.5m, sc.Val + 2.25m, 800, 0);
            sc.Candles.Add(ToCandle(q, Synth.Uniform(q.Low, q.High, q.Volume, q.Delta)));
            if (addEveningBar)
            {
                var e = Synth.B(day.Date.AddHours(16).AddMinutes(5), sc.Val + 2.0m, sc.Val + 2.5m, sc.Val + 1.5m, sc.Val + 2.25m, 300, 0);
                sc.Candles.Add(ToCandle(e, Synth.Uniform(e.Low, e.High, e.Volume, e.Delta)));
            }
            return sc;
        }

        static void Calc(Indicator ind, int bar)
        {
            var m = ind.GetType().GetMethod("OnCalculate", BindingFlags.Instance | BindingFlags.NonPublic);
            m.Invoke(ind, new object[] { bar, 0m });
        }

        static ValueDataSeries Series(object ind, string field)
        {
            return (ValueDataSeries)ind.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ind);
        }

        static void TestIndicatorHistory()
        {
            T.Section("ATAS indicator, history load (adapter driven like ATAS, UTC candles)");
            var sc = Build(runUp: true, addEveningBar: false);
            var ind = new FabioLevelsIndicator();
            ind.InstrumentInfo.TickSize = 0.25m;
            ind.InstrumentInfo.TimeZoneOffset = TimeSpan.FromHours(8);   // a trader whose chart shows UTC+8: must not matter
            ind.TestCandles = i => sc.Candles[i];
            ind.TestCurrentBar = sc.Candles.Count;
            for (int i = 0; i < sc.Candles.Count; i++) Calc(ind, i);

            var f = ind.Feed;
            T.Expect(f.LastError == null, "no error inside the indicator" + (f.LastError == null ? "" : ": " + f.LastError));
            T.Expect(f.ZoneFound, "New York time zone resolved");
            T.Eq(new DateTime(2026, 3, 8, 9, 30, 0), f.Bars[sc.FirstToday].Time, "UTC candle converted to 09:30 New York on the daylight-saving day");
            T.Eq(1, f.Signals.Count, "one set-up found over the loaded history");
            T.Expect(Series(ind, "_longSig").Values.ContainsKey(sc.Confirm), "long arrow drawn on the confirming bar");
            T.Expect(Series(ind, "_poc").Values.ContainsKey(sc.FirstToday) && Series(ind, "_val").Values[sc.FirstToday] == sc.Val, "prior POC/VAL drawn on today's bars");
            T.Expect(Series(ind, "_vwap").Values.Count > 0, "session VWAP drawn");

            T.Expect(f.Trades != null && f.Trades.Trades.Count == 1, "the set-up became one trade on the chart");
            if (f.Trades != null && f.Trades.Trades.Count == 1)
            {
                var t = f.Trades.Trades[0];
                T.Eq(sc.Confirm + 1, t.EntryBar, "entry on the bar after the confirmation");
                T.Eq("target", t.ExitReason, "trade reached its target on the run-up");
                T.Expect(Series(ind, "_entry").Values.ContainsKey(t.EntryBar) && Series(ind, "_stop").Values.ContainsKey(t.ExitBar), "entry and stop lines drawn across the trade");
                T.Expect(Series(ind, "_win").Values.ContainsKey(t.ExitBar), "green result marker on the exit bar");
            }
            T.Eq(0, ind.TestAlerts.Count, "no alerts for history, only for live set-ups");

            string panel = f.PanelText(true);
            T.Expect(panel.Contains("History on this chart: 1 trades"), "panel shows the history statistics");
            T.Expect(panel.Contains("Bias: Bullish"), "panel shows the bias");
            Console.WriteLine(string.Join("\n", panel.Split('\n').Take(9).Select(x => "    | " + x)));

            var render = ind.GetType().GetMethod("OnRender", BindingFlags.Instance | BindingFlags.NonPublic);
            bool ok = true;
            try { render.Invoke(ind, new object[] { new OFT.Rendering.Context.RenderContext(), DrawingLayouts.Final }); } catch (Exception) { ok = false; }
            T.Expect(ok, "panel rendering code runs without errors");
        }

        static void TestIndicatorLive()
        {
            T.Section("ATAS indicator, live (bars arrive one by one, several ticks each)");
            var sc = Build(runUp: true, addEveningBar: false);
            var ind = new FabioLevelsIndicator();
            ind.InstrumentInfo.TickSize = 0.25m;
            ind.TestCandles = i => sc.Candles[i];
            for (int i = 0; i < sc.Candles.Count; i++)
            {
                ind.TestCurrentBar = i + 1;
                for (int tick = 0; tick < 3; tick++) Calc(ind, i);
            }
            T.Expect(ind.Feed.LastError == null, "no error inside the indicator");
            T.Eq(1, ind.Feed.Signals.Count, "the same single set-up live as in history");
            T.Eq(1, ind.TestAlerts.Count, "exactly one alert, raised when the confirming bar closed");
            if (ind.TestAlerts.Count > 0) Console.WriteLine("    alert: " + ind.TestAlerts[0]);
        }

        static void TestStrategyOrders()
        {
            T.Section("ATAS strategy order flow (simulated fills)");
            var sc = Build(runUp: true, addEveningBar: false);
            var s = new FabioStrategy { TradingEnabled = true, Contracts = 2 };
            s.InstrumentInfo.TickSize = 0.25m;
            s.TestCandles = i => sc.Candles[i];
            SetupEvaluation sig = null;
            for (int i = 0; i < sc.Candles.Count; i++)
            {
                s.TestCurrentBar = i + 1;
                Calc(s, i);
                if (s.TestOrders.Count == 1 && s.TestPosition == 0)
                {
                    sig = s.GetType().GetField("_active", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(s) as SetupEvaluation;
                    s.TestPosition = 2;            // broker fills the market entry
                    Calc(s, i);
                }
                if (s.TestPosition != 0 && sig != null && sc.Candles[i].High >= sig.Target)
                {
                    s.TestPosition = 0;            // the target limit order fills
                    Calc(s, i);
                }
            }
            T.Expect(s.TestOrders.Count >= 3, "entry + stop + target orders sent");
            if (s.TestOrders.Count >= 3)
            {
                var entry = s.TestOrders[0]; var stop = s.TestOrders[1]; var tgt = s.TestOrders[2];
                T.Expect(entry.Type == ATAS.DataFeedsCore.OrderTypes.Market && entry.Direction == ATAS.DataFeedsCore.OrderDirections.Buy && entry.QuantityToFill == 2, "market BUY entry for 2 contracts");
                T.Expect(stop.Type == ATAS.DataFeedsCore.OrderTypes.Stop && stop.Direction == ATAS.DataFeedsCore.OrderDirections.Sell && sig != null && stop.TriggerPrice == sig.Stop, "SELL stop at the set-up stop");
                T.Expect(tgt.Type == ATAS.DataFeedsCore.OrderTypes.Limit && tgt.Direction == ATAS.DataFeedsCore.OrderDirections.Sell && sig != null && tgt.Price == sig.Target, "SELL limit at the set-up target");
                T.Expect(stop.QuantityToFill == 2 && tgt.QuantityToFill == 2, "stop and target cover the whole position");
            }
            T.Eq(3, s.TestOrders.Count, "no extra orders (no duplicate brackets)");
            T.Eq(2, s.TestCancelled.Count, "after the target filled, the leftover bracket orders were cancelled");
            T.Expect(s.TestNotifications.Any(m => m.StartsWith("Fabio set-up")), "set-up reported in the ATAS notifications");
        }

        static void TestStrategySessionEnd()
        {
            T.Section("ATAS strategy flattens at the end of the cash session");
            var sc = Build(runUp: false, addEveningBar: true);
            var s = new FabioStrategy { TradingEnabled = true };
            s.InstrumentInfo.TickSize = 0.25m;
            s.TestCandles = i => sc.Candles[i];
            for (int i = 0; i < sc.Candles.Count; i++)
            {
                s.TestCurrentBar = i + 1;
                Calc(s, i);
                if (s.TestOrders.Count == 1 && s.TestPosition == 0) { s.TestPosition = 1; Calc(s, i); }
            }
            var last = s.TestOrders.LastOrDefault();
            T.Expect(last != null && last.Type == ATAS.DataFeedsCore.OrderTypes.Market && last.Direction == ATAS.DataFeedsCore.OrderDirections.Sell && last.Comment.Contains("flatten"),
                "position still open after 16:00 New York -> market SELL to flatten");
            T.Eq(1, s.TestOrders.Count(o => o.Comment != null && o.Comment.Contains("flatten")), "flatten order sent once, not on every tick");

            var off = new FabioStrategy();       // trading OFF by default
            off.InstrumentInfo.TickSize = 0.25m;
            off.TestCandles = i => sc.Candles[i];
            for (int i = 0; i < sc.Candles.Count; i++) { off.TestCurrentBar = i + 1; Calc(off, i); }
            T.Eq(0, off.TestOrders.Count, "trading OFF by default: set-ups reported, no orders");
            T.Expect(off.TestNotifications.Any(m => m.StartsWith("Fabio set-up")), "set-up still reported with trading off");
        }
    }
}
