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
            TestStrategyRiskPaths();
            TestStrategySessionEnd();
            TestLayaApproval();
            TestBridgeHttp();
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

        // ---------------------------------------------------------------- strategy helpers
        static FabioStrategy NewStrategy(Scenario sc, bool trading, bool bridge, bool approval)
        {
            var s = new FabioStrategy { TradingEnabled = trading, BridgeEnabled = bridge, RequireLayaApproval = approval, Contracts = 2, BridgePort = 18765 };
            s.InstrumentInfo.TickSize = 0.25m;
            s.InstrumentInfo.Instrument = "ES";
            s.TestCandles = i => sc.Candles[i];
            return s;
        }

        static void Changed(FabioStrategy s, ATAS.DataFeedsCore.Order o)
        {
            s.GetType().GetMethod("OnOrderChanged", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(s, new object[] { o });
        }

        static void Fill(FabioStrategy s, ATAS.DataFeedsCore.Order o, decimal newPosition)
        {
            o.TestStatus = ATAS.DataFeedsCore.OrderStatus.Filled; o.State = ATAS.DataFeedsCore.OrderStates.Done;
            s.TestPosition = newPosition;
            Changed(s, o);
        }

        static ATAS.DataFeedsCore.Order Find(FabioStrategy s, string commentStart)
        {
            return s.TestOrders.LastOrDefault(o => o.Comment != null && o.Comment.StartsWith(commentStart));
        }

        /// <summary>Feeds bars live; on each bar lets the test react (fill orders, decide tickets).</summary>
        static void RunLive(FabioStrategy s, Scenario sc, Action<int> afterBar)
        {
            for (int i = 0; i < sc.Candles.Count; i++)
            {
                s.TestCurrentBar = i + 1;
                Calc(s, i);
                if (afterBar != null) afterBar(i);
            }
        }

        static void TestStrategyOrders()
        {
            T.Section("ATAS strategy order flow (no approval step, simulated fills)");
            var sc = Build(runUp: true, addEveningBar: false);
            var s = NewStrategy(sc, trading: true, bridge: false, approval: false);
            SetupEvaluation sig = null;
            RunLive(s, sc, i =>
            {
                var entry = Find(s, "Fabio entry");
                if (entry != null && s.TestPosition == 0 && entry.TestStatus == ATAS.DataFeedsCore.OrderStatus.Placed)
                {
                    sig = s.Bridge.Tickets("all", DateTime.UtcNow).First().Setup;
                    Fill(s, entry, 2);                                  // broker fills the market entry
                }
                var tgt = Find(s, "Fabio target");
                if (tgt != null && tgt.TestStatus == ATAS.DataFeedsCore.OrderStatus.Placed && sig != null && sc.Candles[i].High >= sig.Target)
                    Fill(s, tgt, 0);                                    // the target limit fills
            });
            T.Eq(3, s.TestOrders.Count, "entry + stop + target, nothing else");
            if (s.TestOrders.Count == 3)
            {
                var entry = s.TestOrders[0]; var stop = s.TestOrders[1]; var tgt = s.TestOrders[2];
                T.Expect(entry.Type == ATAS.DataFeedsCore.OrderTypes.Market && entry.Direction == ATAS.DataFeedsCore.OrderDirections.Buy && entry.QuantityToFill == 2, "market BUY entry for 2 contracts");
                T.Expect(stop.Type == ATAS.DataFeedsCore.OrderTypes.Stop && stop.Direction == ATAS.DataFeedsCore.OrderDirections.Sell && sig != null && stop.TriggerPrice == sig.Stop, "SELL stop at the set-up stop");
                T.Expect(tgt.Type == ATAS.DataFeedsCore.OrderTypes.Limit && tgt.Direction == ATAS.DataFeedsCore.OrderDirections.Sell && sig != null && tgt.Price == sig.Target, "SELL limit at the set-up target");
                T.Expect(stop.QuantityToFill == 2 && tgt.QuantityToFill == 2, "stop and target cover the whole position");
                T.Expect(s.TestCancelled.Count == 1 && s.TestCancelled[0] == stop, "target filled -> only the stop is cancelled, once");
            }
            var t0 = s.Bridge.Tickets("all", DateTime.UtcNow).FirstOrDefault();
            T.Expect(t0 != null && t0.Status == TicketStatus.Taken && t0.Outcome == "target", "journal: set-up taken, outcome target");
        }

        static void TestStrategyRiskPaths()
        {
            T.Section("ATAS strategy risk paths");
            // 1. rejected entry -> no position, no bracket, ready again
            var sc = Build(runUp: true, addEveningBar: false);
            var s = NewStrategy(sc, true, false, false);
            s.TestRejectNextOrder = "insufficient margin";
            RunLive(s, sc, null);
            T.Eq(0, s.TestOrders.Count, "rejected entry: no orders recorded as placed");
            var tk = s.Bridge.Tickets("all", DateTime.UtcNow).First();
            T.Expect(tk.Outcome.StartsWith("entry rejected"), "journal records the rejection");

            // 2. stop order rejected -> flatten immediately (never leave a position unprotected)
            sc = Build(runUp: true, addEveningBar: false);
            s = NewStrategy(sc, true, false, false);
            bool done = false;
            RunLive(s, sc, i =>
            {
                var entry = Find(s, "Fabio entry");
                if (!done && entry != null && s.TestPosition == 0)
                {
                    done = true;
                    s.TestRejectNextOrder = "stop price invalid";          // the first bracket order (the stop) is rejected
                    Fill(s, entry, 2);
                }
            });
            T.Expect(s.TestOrders.Any(o => o.Comment.StartsWith("Fabio flatten") && o.Direction == ATAS.DataFeedsCore.OrderDirections.Sell && o.QuantityToFill == 2),
                "stop rejected -> market SELL 2 to flatten");

            // 3. losses are counted from the stop fill: after MaxLossesPerSession the next set-up is refused
            sc = Build(runUp: false, addEveningBar: false);
            s = NewStrategy(sc, true, false, false);
            s.MaxLossesPerSession = 0;
            RunLive(s, sc, null);
            T.Eq(0, s.TestOrders.Count, "loss limit reached -> set-up not traded");
            T.Expect(s.Bridge.Tickets("all", DateTime.UtcNow).First().Outcome.Contains("loss limit"), "reason recorded");
        }

        static void TestStrategySessionEnd()
        {
            T.Section("ATAS strategy flattens at the end of the cash session");
            var sc = Build(runUp: false, addEveningBar: true);
            var s = NewStrategy(sc, true, false, false);
            RunLive(s, sc, i =>
            {
                var entry = Find(s, "Fabio entry");
                if (entry != null && s.TestPosition == 0 && entry.TestStatus == ATAS.DataFeedsCore.OrderStatus.Placed) Fill(s, entry, 1);
            });
            // a few more ticks on the evening bar while the flatten order is working
            for (int k = 0; k < 3; k++) Calc(s, sc.Candles.Count - 1);
            var flat = s.TestOrders.Where(o => o.Comment != null && o.Comment.Contains("flatten")).ToList();
            T.Expect(flat.Count == 1 && flat[0].Direction == ATAS.DataFeedsCore.OrderDirections.Sell, "position open after 16:00 New York -> one market SELL to flatten");
            T.Expect(s.TestCancelled.Count(o => o.Comment == "Fabio stop" || o.Comment == "Fabio target") == 2, "stop and target cancelled when flattening");

            var off = NewStrategy(sc, false, false, false);
            RunLive(off, sc, null);
            T.Eq(0, off.TestOrders.Count, "trading OFF by default: set-ups reported, no orders");
            T.Expect(off.TestNotifications.Any(m => m.StartsWith("Fabio set-up")), "set-up still reported with trading off");
        }

        static void TestLayaApproval()
        {
            T.Section("Laya approval (strategy waits for the decision)");
            // approve -> entry
            var sc = Build(runUp: true, addEveningBar: false);
            var s = NewStrategy(sc, true, true, true);
            s.BridgePort = 18766;
            bool decided = false;
            RunLive(s, sc, i =>
            {
                var pending = s.Bridge.Tickets("pending", DateTime.UtcNow);
                if (!decided && pending.Count > 0)
                {
                    T.Eq(0, s.TestOrders.Count, "no order while Laya has not decided");
                    string err;
                    decided = s.Bridge.Decide(pending[0].Id, true, "VAL held, sellers absorbed, buyers won the body", DateTime.UtcNow, out err);
                    Calc(s, i);                                       // next tick sees the decision
                }
            });
            DisposeBridge(s);
            T.Expect(decided, "a pending set-up was offered to Laya");
            T.Expect(s.TestOrders.Count >= 1 && s.TestOrders[0].Comment.StartsWith("Fabio entry"), "approved -> entry sent");

            // reject -> nothing
            sc = Build(runUp: true, addEveningBar: false);
            s = NewStrategy(sc, true, true, true); s.BridgePort = 18767;
            RunLive(s, sc, i =>
            {
                foreach (var p in s.Bridge.Tickets("pending", DateTime.UtcNow)) { string err; s.Bridge.Decide(p.Id, false, "against the auction", DateTime.UtcNow, out err); Calc(s, i); }
            });
            DisposeBridge(s);
            T.Eq(0, s.TestOrders.Count, "rejected -> no order");
            T.Expect(s.Bridge.Tickets("all", DateTime.UtcNow).First().Outcome.Contains("rejected"), "journal: not traded, rejected");

            // no answer -> expires -> nothing
            sc = Build(runUp: true, addEveningBar: false);
            s = NewStrategy(sc, true, true, true); s.BridgePort = 18768; s.ApprovalTimeoutSeconds = 0;
            RunLive(s, sc, null);
            DisposeBridge(s);
            T.Eq(0, s.TestOrders.Count, "Laya silent past the timeout -> no order");
            T.Expect(s.Bridge.Tickets("all", DateTime.UtcNow).First().Outcome.Contains("expired"), "journal: expired");

            // approved too late, price already ran -> skipped
            sc = Build(runUp: true, addEveningBar: false);
            s = NewStrategy(sc, true, true, true); s.BridgePort = 18769; s.MaxEntrySlipTicks = 0;
            RunLive(s, sc, i =>
            {
                foreach (var p in s.Bridge.Tickets("pending", DateTime.UtcNow)) { string err; s.Bridge.Decide(p.Id, true, "ok", DateTime.UtcNow, out err); }
            });
            DisposeBridge(s);
            T.Eq(0, s.TestOrders.Count, "approved but price moved past the allowed slippage -> skipped");
            T.Expect(s.Bridge.Tickets("all", DateTime.UtcNow).First().Status == TicketStatus.Skipped, "journal: skipped");
        }

        static void DisposeBridge(FabioStrategy s)
        {
            s.GetType().GetMethod("OnDispose", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(s, null);
        }

        static void TestBridgeHttp()
        {
            T.Section("Bridge over HTTP (what Laya's MCP server calls), driven by the strategy");
            var sc = Build(runUp: true, addEveningBar: false);
            var s = NewStrategy(sc, false, true, true);
            s.BridgePort = 18770; s.BridgeToken = "secret"; s.ApprovalTimeoutSeconds = 120;
            using (var http = new System.Net.Http.HttpClient { BaseAddress = new Uri("http://localhost:18770/") })
            {
                http.DefaultRequestHeaders.Add("X-Bridge-Token", "secret");
                RunLive(s, sc, null);
                Func<string, string> get = u => http.GetStringAsync(u).Result;

                var health = Json.Parse(get("health")) as Dictionary<string, object>;
                T.Expect(health != null && (bool)health["ok"], "GET /health");
                var state = Json.Parse(get("state")) as Dictionary<string, object>;
                T.Expect(state != null && (string)((Dictionary<string, object>)state["bias"])["final"] == "Bullish", "GET /state: bias for Laya");
                T.Expect(state.ContainsKey("prior_session") && state.ContainsKey("session_vwap") && state.ContainsKey("nearest_zone_checklist"), "GET /state: levels, VWAP, checklist");

                var wait = Json.Parse(get("setups/wait?timeout=1")) as Dictionary<string, object>;
                var list = (List<object>)wait["setups"];
                T.Eq(1, list.Count, "GET /setups/wait returns the pending set-up");
                var ticket = (Dictionary<string, object>)list[0];
                var setup = (Dictionary<string, object>)ticket["setup"];
                T.Expect((string)setup["side"] == "Long" && (bool)setup["all_required_met"], "set-up JSON: side and checklist");
                T.Expect(((List<object>)setup["checks"]).Count >= 9, "set-up JSON: every check with state and source");

                string id = (string)ticket["id"];
                var resp = http.PostAsync("setups/" + id + "/decision", new System.Net.Http.StringContent("{\"approve\": false, \"reason\": \"test\"}")).Result;
                T.Eq(200, (int)resp.StatusCode, "POST decision accepted");
                resp = http.PostAsync("setups/" + id + "/decision", new System.Net.Http.StringContent("{\"approve\": true}")).Result;
                T.Eq(409, (int)resp.StatusCode, "a second decision on the same set-up is refused");
                resp = http.PostAsync("setups/" + id + "/decision", new System.Net.Http.StringContent("not json")).Result;
                T.Expect((int)resp.StatusCode >= 400, "bad body is refused");

                var journal = Json.Parse(get("journal?limit=10")) as Dictionary<string, object>;
                T.Expect(((List<object>)journal["events"]).Count >= 2, "GET /journal shows the set-up and the decision");

                using (var anon = new System.Net.Http.HttpClient { BaseAddress = new Uri("http://localhost:18770/") })
                    T.Eq(401, (int)anon.GetAsync("state").Result.StatusCode, "wrong or missing token is refused");
            }
            DisposeBridge(s);
        }
    }
}
