using System;
using System.Collections.Generic;
using System.Linq;
using FabioOrderflow.Core;

namespace FabioOrderflow.Tests
{
    internal static class AddTests
    {
        public static void Run()
        {
            TestTime();
            TestConfluence();
            TestOpenTrade();
            TestJsonAndBridge();
            TestMarketMap();
        }

        static void TestMarketMap()
        {
            T.Section("Market map (everything besides the candle)");
            // volume nodes: two peaks with a thin area between
            var vp = new VolumeProfile();
            for (int i = 0; i <= 40; i++)
            {
                decimal px = 100m + i * 0.25m;
                double a = Math.Exp(-Math.Pow((i - 10) / 3.0, 2)), b = Math.Exp(-Math.Pow((i - 30) / 3.0, 2));
                vp.Add(px, (decimal)(500 * (a + b)) + 5, (decimal)(500 * (a + b)) + 5);
            }
            var nodes = MarketMap.FindNodes(vp, 0.25m);
            var hvn = nodes.Where(n => n.Kind == "HVN").Select(n => n.Price).OrderBy(x => x).ToList();
            var lvn = nodes.Where(n => n.Kind == "LVN").Select(n => n.Price).ToList();
            T.Expect(hvn.Count == 2 && Math.Abs(hvn[0] - 102.5m) <= 0.5m && Math.Abs(hvn[1] - 107.5m) <= 0.5m, "HVNs found at both volume peaks (" + string.Join(", ", hvn) + ")");
            T.Expect(lvn.Count >= 1 && Math.Abs(lvn[0] - 105m) <= 0.75m, "LVN found in the thin area between them (" + string.Join(", ", lvn) + ")");

            // sessions with overnight bars: value history, overnight, London, developing value, VWAP bands, CVD
            var p = Program.P();
            var engine = new FabioEngine(p);
            int idx = 0; var day = new DateTime(2026, 3, 2); EngineState st = null;
            foreach (var center in new decimal[] { 4500, 4504, 4508, 4512, 4516, 4520 })
            {
                foreach (var kv in Synth.Session(day, center)) st = engine.Update(idx++, kv.Key, kv.Value, true);
                day = day.AddDays(1);
            }
            // overnight from 18:00 the evening before to 09:25: range 4521-4526, London (03:00+) 4523-4527.5
            var onStart = day.AddDays(-1).AddHours(18);
            for (var t = onStart; t < day.AddHours(9).AddMinutes(30); t = t.AddMinutes(5))
            {
                bool london = t.TimeOfDay >= new TimeSpan(3, 0, 0) && t.Date == day;
                decimal lo = london ? 4523m : 4521m, hi = london ? 4527.5m : 4526m;
                var b = Synth.B(t, lo + 1, hi, lo, lo + 1.5m, 300, 20);
                st = engine.Update(idx++, b, Synth.Uniform(b.Low, b.High, b.Volume, b.Delta), true);
            }
            T.Expect(!st.InRth && st.Map.Overnight.Valid && st.Map.Overnight.High == 4527.5m, "before the open: the live overnight range is available for the pre-market plan");
            decimal cvd = 0;
            var bars = new List<Bar>();
            for (int n = 0; n < 12; n++)
            {
                decimal c = 4522m + (n % 4) * 0.5m;
                var b = Synth.B(Synth.Open(day, n), c, c + 0.75m, c - 0.75m, c + 0.25m, 1000, n % 3 == 0 ? 150 : -50);
                st = engine.Update(idx++, b, Synth.Uniform(b.Low, b.High, b.Volume, b.Delta), true);
                cvd += b.Delta; bars.Add(b);
            }
            var m = st.Map;
            T.Expect(m.Overnight.Valid && m.Overnight.High == 4527.5m && m.Overnight.Low == 4521m, "overnight high / low frozen at the cash open");
            T.Expect(m.HasLondon && m.LondonHigh == 4527.5m && m.LondonLow == 4523m, "London high / low (03:00 to the cash open)");
            T.Expect(m.Developing.Valid && m.Developing.Poc >= 4521m && m.Developing.Poc <= 4524m, "today's developing POC / VAH / VAL");
            T.Near(cvd, m.SessionCvd, 0.001m, "session cumulative delta = sum of today's bar deltas");
            T.Expect(m.Vwap > 4521m && m.Vwap < 4525m && m.VwapSd > 0, "session VWAP with a standard deviation for the bands");
            T.Eq(6, m.ValueHistory.Count, "value ladder of the last sessions");
            T.Expect(m.ValueHistory.Skip(1).All(v => v.ShiftVsPrevious == "higher"), "each session's value marked higher than the one before");
            var json = Json.Write(m.ToJson());
            T.Expect(json.Contains("developing_value") && json.Contains("upper_1sd") && json.Contains("value_history") && json.Contains("london"), "map serialised for Laya");

            // next evening: the overnight starts again
            var eve = Synth.B(day.AddHours(18), 4530, 4531, 4529, 4530.5m, 200, 0);
            st = engine.Update(idx++, eve, Synth.Uniform(eve.Low, eve.High, eve.Volume, eve.Delta), true);
            var eve2 = Synth.B(day.AddHours(18).AddMinutes(5), 4530, 4531, 4529, 4530.5m, 200, 0);
            st = engine.Update(idx++, eve2, Synth.Uniform(eve2.Low, eve2.High, eve2.Volume, eve2.Delta), true);
            T.Expect(st.Map.Overnight.Valid && st.Map.Overnight.Low == 4529m, "the next overnight starts fresh after the session");
        }

        static void TestJsonAndBridge()
        {
            T.Section("Bridge building blocks (JSON, tickets, timeouts)");
            var obj = new Dictionary<string, object> { { "a", 1.5m }, { "b", "quote \" and \\ and \n" }, { "c", true }, { "d", null }, { "e", new List<object> { 1, "x" } } };
            var back = Json.Parse(Json.Write(obj)) as Dictionary<string, object>;
            T.Expect(back != null && (decimal)back["a"] == 1.5m && (string)back["b"] == "quote \" and \\ and \n" && (bool)back["c"] && back["d"] == null && ((List<object>)back["e"]).Count == 2, "JSON round trip with escapes, null, list");
            bool threw = false; try { Json.Parse("{\"a\": }"); } catch (FormatException) { threw = true; }
            T.Expect(threw, "malformed JSON is rejected");

            var br = new LayaBridge { ApprovalTimeoutSeconds = 30 };
            var t0 = new DateTime(2026, 3, 2, 15, 0, 0, DateTimeKind.Utc);
            var sig = new SetupEvaluation { Side = Side.Long, Zone = new Zone { Kind = ZoneKind.Val, Price = 100m }, Entry = 101, Stop = 99, Target = 105, Rr = 2 };
            var tk = br.Publish(sig, "ES", t0, true);
            T.Eq(TicketStatus.Pending, br.StatusOf(tk.Id, t0.AddSeconds(10)), "pending until decided");
            string err;
            T.Expect(br.Decide(tk.Id, true, "ok", t0.AddSeconds(12), out err), "decision inside the timeout accepted");
            T.Expect(!br.Decide(tk.Id, false, "again", t0.AddSeconds(13), out err) && err.Contains("approved"), "second decision refused");
            var tk2 = br.Publish(sig, "ES", t0, true);
            T.Eq(TicketStatus.Expired, br.StatusOf(tk2.Id, t0.AddSeconds(31)), "no decision within 30 s -> expired");
            T.Expect(!br.Decide(tk2.Id, true, "late", t0.AddSeconds(32), out err), "a late approval is refused");
            T.Expect(!br.Decide("nope", true, "x", t0, out err) && err.Contains("unknown"), "unknown id refused");
            var tk3 = br.Publish(sig, "ES", t0, false);
            T.Eq(TicketStatus.Auto, tk3.Status, "approval not required -> ticket marked auto");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var none = new LayaBridge().WaitForPending(TimeSpan.FromMilliseconds(300), () => DateTime.UtcNow);
            T.Expect(none.Count == 0 && sw.ElapsedMilliseconds >= 250, "long-poll returns empty after its timeout");
            T.Expect(br.Journal(50).Count >= 5, "journal records set-ups, decisions and expiries");
        }

        static void TestTime()
        {
            T.Section("Exchange time and sessions");
            var ny = ExchangeTime.Find("America/New_York");
            T.Expect(ny != null, "New York zone found by IANA id");
            T.Expect(ExchangeTime.Find("Eastern Standard Time") != null, "New York zone found by Windows id");
            T.Expect(ExchangeTime.Find("Not/AZone") == null, "unknown zone returns null instead of throwing");
            T.Eq(new DateTime(2026, 3, 2, 9, 30, 0), ExchangeTime.FromUtc(new DateTime(2026, 3, 2, 14, 30, 0), ny), "winter: 14:30 UTC = 09:30 New York");
            T.Eq(new DateTime(2026, 7, 1, 9, 30, 0), ExchangeTime.FromUtc(new DateTime(2026, 7, 1, 13, 30, 0), ny), "summer (daylight saving): 13:30 UTC = 09:30 New York");

            var globex = new SessionClock(new TimeSpan(18, 0, 0), new TimeSpan(17, 0, 0));
            var d = new DateTime(2026, 3, 2);
            T.Expect(!globex.IsRth(d.AddHours(17).AddMinutes(30)), "crossing session: the 17:00-18:00 break is outside");
            T.Eq((DateTime?)d, globex.SessionKey(d.AddHours(18)), "crossing session: 18:00 starts today's session");
            T.Eq((DateTime?)d, globex.SessionKey(d.AddDays(1).AddHours(2)), "crossing session: 02:00 next day still belongs to it");
        }

        static void TestConfluence()
        {
            T.Section("Confluence lines (information only)");
            var p = Program.P();
            var day = new DateTime(2026, 3, 2, 10, 0, 0);
            var a = Synth.B(day, 101m, 102m, 100m, 101.5m, 1000, -300);
            var bars = new List<Bar> { a };
            var levels = new[]
            {
                new PriceLevelVol(100m, 300, 20), new PriceLevelVol(100.25m, 120, 20), new PriceLevelVol(100.5m, 40, 30),
                new PriceLevelVol(101m, 40, 40), new PriceLevelVol(101.5m, 30, 60), new PriceLevelVol(102m, 20, 30)
            };
            var ctx = new EvalContext { Levels = new List<PriceLevelVol[]> { levels } };
            var c = SetupEvaluator.FootprintCheck(bars, 0, Side.Long, ctx, p);
            T.Eq(CheckState.Met, c.State, "heavy selling printed at the low, close well above it");
            Console.WriteLine("    " + c.Detail);

            var stuck = new List<Bar> { Synth.B(day, 101m, 102m, 100m, 100.25m, 1000, -300) };
            T.Eq(CheckState.NotMet, SetupEvaluator.FootprintCheck(stuck, 0, Side.Long, ctx, p).State, "close still on the selling: not absorbed");
            T.Eq(CheckState.Unknown, SetupEvaluator.FootprintCheck(bars, 0, Side.Long, null, p).State, "no footprint data: unknown, never guessed");

            var zone = new Zone { Kind = ZoneKind.Val, Price = 100m, Candidate = Side.Long };
            var near = new EvalContext { DeltaLevels = new List<Zone> { new Zone { Price = 99.75m, Candidate = Side.Long } } };
            var far = new EvalContext { DeltaLevels = new List<Zone> { new Zone { Price = 95m, Candidate = Side.Long } } };
            T.Eq(CheckState.Met, SetupEvaluator.DeltaLevelCheck(zone, Side.Long, near, 1.5m).State, "delta level within tolerance of the zone");
            T.Eq(CheckState.NotMet, SetupEvaluator.DeltaLevelCheck(zone, Side.Long, far, 1.5m).State, "delta level far from the zone");
            T.Eq(CheckState.Unknown, SetupEvaluator.DeltaLevelCheck(zone, Side.Long, new EvalContext(), 1.5m).State, "no delta level yet: unknown");

            // the engine fills the confluence lines and VWAP on the textbook set-up
            var engine = new FabioEngine(p);
            int idx = 0; var dd = new DateTime(2026, 3, 2); VolumeProfile last = null;
            foreach (var center in new decimal[] { 4500, 4504, 4508, 4512, 4516, 4520 })
            {
                last = new VolumeProfile();
                foreach (var kv in Synth.Session(dd, center)) { foreach (var l in kv.Value) last.Add(l); engine.Update(idx++, kv.Key, kv.Value, true); }
                dd = dd.AddDays(1);
            }
            var plan = Program.LongAtVal(last.Compute(p.TickSize, p.ValueAreaPct), dd);
            var sigs = new List<SetupEvaluation>(); EngineState st = null;
            for (int k = 0; k < plan.Bars.Count; k++) { st = engine.Update(idx++, plan.Bars[k], plan.Levels[k], true); sigs.AddRange(st.NewSignals); }
            T.Eq(1, sigs.Count, "textbook set-up still fires with the confluence lines added");
            if (sigs.Count == 1)
            {
                Console.WriteLine(sigs[0].ToChecklist());
                T.Expect(sigs[0].Checks.Any(x => x.Name.StartsWith("Footprint") && x.State != CheckState.Unknown), "footprint line evaluated from the bar's rows");
                T.Expect(sigs[0].ConfluenceCount == 3, "three confluence lines: big trades, footprint, delta level");
            }
            decimal lo = plan.Bars.Min(b => b.Low), hi = plan.Bars.Max(b => b.High);
            T.Expect(st.Vwap >= lo && st.Vwap <= hi, "session VWAP lies inside today's range");
        }

        static void TestOpenTrade()
        {
            T.Section("Open trades on the chart");
            var p = Program.P();
            var t0 = new DateTime(2026, 3, 2, 10, 0, 0);
            var bars = new List<Bar>();
            for (int i = 0; i < 4; i++) bars.Add(Synth.B(t0.AddMinutes(5 * i), 100, 100.5m, 99.5m, 100.25m, 1000, 0));
            var sig = new SetupEvaluation { Side = Side.Long, ConfirmGlobalIndex = 0, Stop = 98m, Target = 110m, Zone = new Zone { Kind = ZoneKind.Val } };
            var r = Backtester.Simulate(bars, new[] { sig }, p, new BacktestParams { LeaveLastTradeOpen = true });
            T.Eq("open", r.Trades[0].ExitReason, "a running trade is reported as open");
            T.Eq(0, r.Stats.Trades, "open trades are left out of the statistics");
            r = Backtester.Simulate(bars, new[] { sig }, p, new BacktestParams());
            T.Eq("session end", r.Trades[0].ExitReason, "without the chart option it is closed at the last bar");
        }
    }
}
