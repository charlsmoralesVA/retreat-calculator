using System;
using System.Collections.Generic;
using System.Linq;
using FabioOrderflow.Core;

// Plain console test runner (no NuGet needed). Exit code 1 if anything fails.
namespace FabioOrderflow.Tests
{
    internal static class T
    {
        public static int Passed, Failed;
        public static void Expect(bool ok, string name)
        {
            if (ok) { Passed++; Console.WriteLine("  ok    " + name); }
            else { Failed++; Console.WriteLine("  FAIL  " + name); }
        }
        public static void Eq<TV>(TV expected, TV actual, string name)
        {
            bool ok = Equals(expected, actual);
            Expect(ok, ok ? name : string.Format("{0} (expected {1}, got {2})", name, expected, actual));
        }
        public static void Near(decimal expected, decimal actual, decimal tol, string name)
        {
            bool ok = Math.Abs(expected - actual) <= tol;
            Expect(ok, ok ? name : string.Format("{0} (expected {1}+-{2}, got {3})", name, expected, tol, actual));
        }
        public static void Section(string s) { Console.WriteLine("\n" + s); }
    }

    /// <summary>Synthetic market data so the whole pipeline can be exercised without ATAS.</summary>
    internal static class Synth
    {
        public const decimal Tick = 0.25m;
        static decimal R(decimal x) { return Math.Round(x / Tick) * Tick; }

        public static PriceLevelVol[] Gaussian(decimal center, decimal sd, decimal vol, decimal delta)
        {
            var px = new List<decimal>(); var w = new List<double>(); double sum = 0;
            for (decimal p = R(center - 3 * sd); p <= R(center + 3 * sd); p += Tick)
            {
                double z = (double)((p - center) / sd), ww = Math.Exp(-0.5 * z * z);
                px.Add(p); w.Add(ww); sum += ww;
            }
            decimal askShare = vol == 0 ? 0.5m : (1 + delta / vol) / 2;
            var res = new PriceLevelVol[px.Count];
            for (int i = 0; i < px.Count; i++)
            {
                decimal v = vol * (decimal)(w[i] / sum);
                res[i] = new PriceLevelVol(px[i], v * (1 - askShare), v * askShare);
            }
            return res;
        }

        public static PriceLevelVol[] Uniform(decimal lo, decimal hi, decimal vol, decimal delta)
        {
            var list = new List<PriceLevelVol>();
            int n = (int)Math.Round((hi - lo) / Tick) + 1;
            decimal askShare = vol == 0 ? 0.5m : (1 + delta / vol) / 2;
            for (int i = 0; i < n; i++)
            {
                decimal v = vol / n;
                list.Add(new PriceLevelVol(R(lo + i * Tick), v * (1 - askShare), v * askShare));
            }
            return list.ToArray();
        }

        public static Bar B(DateTime t, decimal o, decimal h, decimal l, decimal c, decimal vol, decimal delta)
        {
            return new Bar { Time = t, Open = o, High = h, Low = l, Close = c, Volume = vol, Delta = delta };
        }

        public static DateTime Open(DateTime day, int n) { return day.Date.AddHours(9).AddMinutes(30 + 5 * n); }

        /// <summary>78 five-minute bars of a quiet session whose value sits around 'center'.</summary>
        public static List<KeyValuePair<Bar, PriceLevelVol[]>> Session(DateTime day, decimal center)
        {
            var res = new List<KeyValuePair<Bar, PriceLevelVol[]>>();
            for (int n = 0; n < 78; n++)
            {
                decimal delta = n % 2 == 0 ? 100 : -100;
                var bar = B(Open(day, n), center, center + 0.5m, center - 0.5m, center + (n % 2 == 0 ? 0.25m : -0.25m), 1000, delta);
                res.Add(new KeyValuePair<Bar, PriceLevelVol[]>(bar, Gaussian(center, 2.0m, 1000, delta)));
            }
            return res;
        }
    }

    internal sealed class TodayPlan
    {
        public List<Bar> Bars = new List<Bar>();
        public List<PriceLevelVol[]> Levels = new List<PriceLevelVol[]>();
        public int ConfirmOffset = -1;
        public void Add(Bar b) { Bars.Add(b); Levels.Add(Synth.Uniform(b.Low, b.High, b.Volume, b.Delta)); }
    }

    internal sealed class RunResult
    {
        public List<SetupEvaluation> Signals = new List<SetupEvaluation>();
        public EngineState Final;
        public ProfileStats Prior;
        public int SignalsBeforeNextBar;
        public int ConfirmGlobalIndex;
        public FabioEngine Engine;
    }

    internal static class Program
    {
        internal static FabioParams P() { return new FabioParams { TickSize = Synth.Tick }; }

        // ---- scenario runner -------------------------------------------------------------------
        static RunResult Run(decimal[] centers, Func<ProfileStats, DateTime, TodayPlan> todayBuilder, FabioParams p, int repeats = 1)
        {
            var engine = new FabioEngine(p);
            var res = new RunResult { Engine = engine };
            int idx = 0;
            DateTime day = new DateTime(2026, 3, 2);
            VolumeProfile lastProfile = null;

            foreach (var c in centers)
            {
                lastProfile = new VolumeProfile();
                foreach (var kv in Synth.Session(day, c))
                {
                    foreach (var l in kv.Value) lastProfile.Add(l);
                    for (int r = 0; r < repeats; r++) engine.Update(idx, kv.Key, kv.Value, true);
                    idx++;
                }
                day = day.AddDays(1);
            }
            res.Prior = lastProfile.Compute(p.TickSize, p.ValueAreaPct);

            var plan = todayBuilder(res.Prior, day);
            for (int k = 0; k < plan.Bars.Count; k++)
            {
                EngineState st = null;
                for (int r = 0; r < repeats; r++)
                {
                    st = engine.Update(idx, plan.Bars[k], plan.Levels[k], true);
                    res.Signals.AddRange(st.NewSignals);   // delivered once, on the call that closes the previous bar
                }
                if (k == plan.ConfirmOffset) { res.SignalsBeforeNextBar = res.Signals.Count; res.ConfirmGlobalIndex = idx; }
                res.Final = st;
                idx++;
            }
            return res;
        }

        // ---- a textbook long at the prior VAL, built relative to the prior profile --------------
        internal static TodayPlan LongAtVal(ProfileStats prior, DateTime day, bool explosiveApproach = false, bool weakAbsorb = false, bool bearishConfirm = false)
        {
            var plan = new TodayPlan();
            decimal val = prior.Val, poc = prior.Poc;
            int n = 0;
            // 6 quiet opening bars around the POC (baseline-like activity)
            for (int i = 0; i < 6; i++, n++)
                plan.Add(Synth.B(Synth.Open(day, n), poc, poc + 0.5m, poc - 0.5m, poc + (i % 2 == 0 ? 0.25m : -0.25m), 1000, i % 2 == 0 ? 100 : -100));
            // approach: 6 bars falling into VAL
            decimal start = explosiveApproach ? val + 9m : val + 3.0m;
            decimal step = explosiveApproach ? 1.5m : 0.5m;
            decimal appDelta = explosiveApproach ? -30m : -110m;
            for (int m = 0; m < 6; m++, n++)
            {
                decimal o = start - step * m, c = o - step;
                plan.Add(Synth.B(Synth.Open(day, n), o, o + 0.25m, c - 0.25m, c, 1000, appDelta));
            }
            decimal at = start - step * 6;   // where the approach ended (~VAL)
            // absorption bar: heavy selling at VAL, no downside result
            plan.Add(Synth.B(Synth.Open(day, n++), at, at + 0.5m, val - 0.5m, at, 1500, weakAbsorb ? -20 : -300));
            // confirmation bar: buyers win in the body
            if (bearishConfirm)
                plan.Add(Synth.B(Synth.Open(day, n), at, at + 0.25m, at - 1.5m, at - 1.25m, 1200, -200));
            else
                plan.Add(Synth.B(Synth.Open(day, n), at, at + 2.0m, at - 0.25m, at + 1.75m, 1200, 200));
            plan.ConfirmOffset = n; n++;
            // the next bar arrives -> the confirm bar is final
            plan.Add(Synth.B(Synth.Open(day, n), at + 1.75m, at + 2.25m, at + 1.5m, at + 2.0m, 800, 40));
            return plan;
        }

        // ---- mirror image: a short at the prior VAH --------------------------------------------
        static TodayPlan ShortAtVah(ProfileStats prior, DateTime day)
        {
            var plan = new TodayPlan();
            decimal vah = prior.Vah, poc = prior.Poc;
            int n = 0;
            for (int i = 0; i < 6; i++, n++)
                plan.Add(Synth.B(Synth.Open(day, n), poc, poc + 0.5m, poc - 0.5m, poc + (i % 2 == 0 ? 0.25m : -0.25m), 1000, i % 2 == 0 ? 100 : -100));
            decimal start = vah - 3.0m;
            for (int m = 0; m < 6; m++, n++)
            {
                decimal o = start + 0.5m * m, c = o + 0.5m;
                plan.Add(Synth.B(Synth.Open(day, n), o, c + 0.25m, o - 0.25m, c, 1000, 110));
            }
            decimal at = start + 0.5m * 6;
            plan.Add(Synth.B(Synth.Open(day, n++), at, vah + 0.5m, at - 0.5m, at, 1500, 300));          // buyers absorbed
            plan.Add(Synth.B(Synth.Open(day, n), at, at + 0.25m, at - 2.0m, at - 1.75m, 1200, -200));   // sellers win in the body
            plan.ConfirmOffset = n; n++;
            plan.Add(Synth.B(Synth.Open(day, n), at - 1.75m, at - 1.5m, at - 2.25m, at - 2.0m, 800, -40));
            return plan;
        }

        static readonly decimal[] RisingCenters = { 4500, 4504, 4508, 4512, 4516, 4520 };
        static readonly decimal[] FallingCenters = { 4520, 4516, 4512, 4508, 4504, 4500 };

        // ====================================================================================
        static void TestProfile()
        {
            T.Section("VolumeProfile");
            var vp = new VolumeProfile();
            decimal[] v = { 10, 20, 50, 100, 60, 30, 10 };
            for (int i = 0; i < v.Length; i++) vp.Add(100 + i, v[i] / 2, v[i] / 2);
            var s = vp.Compute(1m, 0.70m);
            T.Eq(103m, s.Poc, "POC is the heaviest price");
            T.Eq(101m, s.Val, "value area low (CME two-tick growth)");
            T.Eq(105m, s.Vah, "value area high");
            T.Eq(280m, s.Total, "total volume");

            var d = new VolumeProfile(); d.Add(100, 30, 80); d.Add(101, 5, 5);
            T.Eq(50m, d.DeltaAt(100), "delta = ask - bid");

            var a = new VolumeProfile(); a.Add(100, 1, 1);
            var b = new VolumeProfile(); b.Add(100, 2, 2); b.Add(101, 1, 1);
            a.Merge(b);
            T.Eq(6m, a.VolumeAt(100), "merge adds volume per price");
        }

        static void TestBias()
        {
            T.Section("Bias (value shifts, narrative, composition)");
            Func<decimal, decimal, ProfileStats> ps = (lo, hi) => new ProfileStats { Val = lo, Vah = hi, Poc = (lo + hi) / 2 };
            T.Eq(Bias.Bullish, BiasEngine.ValueShift(ps(100, 110), ps(105, 115), false), "both edges higher = bullish");
            T.Eq(Bias.Neutral, BiasEngine.ValueShift(ps(100, 110), ps(105, 115), true), "strict mode: overlap = neutral");
            T.Eq(Bias.Bullish, BiasEngine.ValueShift(ps(100, 110), ps(111, 120), true), "strict mode: clean separation = bullish");
            T.Eq(Bias.Bearish, BiasEngine.ValueShift(ps(100, 110), ps(95, 105), false), "both edges lower = bearish");
            T.Eq(Bias.Neutral, BiasEngine.ValueShift(ps(100, 110), ps(102, 108), false), "inside day = neutral");

            Func<decimal, decimal, SessionSnapshot> ss = (lo, hi) => new SessionSnapshot { Stats = ps(lo, hi) };
            var up = new List<SessionSnapshot> { ss(100, 110), ss(104, 114), ss(108, 118), ss(103, 113), ss(107, 117) };
            T.Eq(Bias.Bullish, BiasEngine.Narrative(up, 5, false), "one counter-shift is outvoted by the trend");

            var comp = ps(100, 110);
            T.Eq(AuctionPosition.Above, BiasEngine.ClassifyAuction(new List<decimal> { 112, 113 }, comp, 2, AuctionPosition.Inside), "above composition VA");
            T.Eq(AuctionPosition.Above, BiasEngine.ClassifyAuction(new List<decimal> { 112, 105 }, comp, 2, AuctionPosition.Above), "one close back inside is not acceptance (auction not flipped)");
            T.Eq(AuctionPosition.Inside, BiasEngine.ClassifyAuction(new List<decimal> { 105, 106 }, comp, 2, AuctionPosition.Above), "two closes inside = accepted, auction flips to balanced");
            string note;
            T.Eq(Bias.Bullish, BiasEngine.FinalBias(Bias.Bearish, AuctionPosition.Above, true, out note), "bearish shift above composition VA = retracement, bias stays bullish");
            T.Eq(Bias.Bearish, BiasEngine.FinalBias(Bias.Bearish, AuctionPosition.Inside, true, out note), "inside composition VA the narrative decides");
        }

        static void TestZones()
        {
            T.Section("Zones");
            var prior = new ProfileStats { Val = 4518, Poc = 4520, Vah = 4522 };
            var p = P();
            T.Eq(ValueLocation.AcceptedAbove, ZoneEngine.Locate(new List<decimal> { 4523, 4524 }, prior, 2), "two closes above prior VAH = accepted above");
            T.Eq(ValueLocation.Inside, ZoneEngine.Locate(new List<decimal> { 4523, 4521 }, prior, 2), "a close back inside = not accepted");
            T.Eq(ValueLocation.AcceptedBelow, ZoneEngine.Locate(new List<decimal> { 4517, 4516 }, prior, 2), "two closes below prior VAL");

            var inside = ZoneEngine.Build(prior, ValueLocation.Inside, p);
            T.Eq(Side.Short, inside.First(z => z.Kind == ZoneKind.Vah).Candidate, "inside value: VAH is a fade short");
            T.Eq(Side.Long, inside.First(z => z.Kind == ZoneKind.Val).Candidate, "inside value: VAL is a fade long");
            T.Eq(Side.None, inside.First(z => z.Kind == ZoneKind.Poc).Candidate, "inside value: POC is a magnet, not a trade");
            var above = ZoneEngine.Build(prior, ValueLocation.AcceptedAbove, p);
            T.Eq(Side.Long, above.First(z => z.Kind == ZoneKind.Poc).Candidate, "accepted above: POC becomes a rejector (long)");
            var below = ZoneEngine.Build(prior, ValueLocation.AcceptedBelow, p);
            T.Eq(Side.Short, below.First(z => z.Kind == ZoneKind.Poc).Candidate, "accepted below: POC becomes a rejector (short)");

            // delta levels: balanced = outside the value area only, swing = anywhere
            var vp = new VolumeProfile();
            vp.Add(100, 400, 100); vp.Add(101, 10, 10); vp.Add(102, 500, 500); vp.Add(103, 10, 10); vp.Add(104, 50, 300);
            var st = new ProfileStats { Val = 101, Vah = 103, Poc = 102 };
            var bal = ZoneEngine.DeltaLevels(vp, st, 102m, DeltaLevelMode.Balanced);
            T.Eq(100m, bal.First(z => z.Candidate == Side.Long).Price, "balanced: strongest delta level below is outside the VA");
            T.Eq(104m, bal.First(z => z.Candidate == Side.Short).Price, "balanced: strongest delta level above is outside the VA");
            vp.Add(101, 600, 0);
            var swing = ZoneEngine.DeltaLevels(vp, st, 102m, DeltaLevelMode.Swing);
            T.Eq(101m, swing.First(z => z.Candidate == Side.Long).Price, "swing: most significant level counts even inside the VA");
        }

        static void TestClockAndFilter()
        {
            T.Section("Session clock and big-trade filter");
            var c = new SessionClock(new TimeSpan(9, 30, 0), new TimeSpan(16, 0, 0));
            var d = new DateTime(2026, 3, 2);
            T.Expect(c.IsRth(d.AddHours(9).AddMinutes(30)), "09:30 is RTH");
            T.Expect(!c.IsRth(d.AddHours(9).AddMinutes(25)), "09:25 is not RTH");
            T.Expect(!c.IsRth(d.AddHours(16)), "16:00 bar is outside RTH");
            T.Expect(c.SessionKey(d.AddHours(3)) == null, "overnight bars have no session key");

            var f = new BigTradeFilter(P());
            T.Eq(60, f.Threshold, "starts at ~60 contracts (stated)");
            f.Adapt(3); T.Eq(60, f.Threshold, "3 big trades per candle is allowed");
            f.Adapt(4); T.Eq(75, f.Threshold, "more than 3 per candle scales the filter up");
            f.Adapt(0); T.Eq(60, f.Threshold, "quiet bar relaxes back toward the base filter");
            T.Expect(f.IsBig(60) && !f.IsBig(59), "filter boundary");
        }

        static void TestIbAndGap()
        {
            T.Section("Initial balance and gap (information only)");
            var day = new DateTime(2026, 3, 2);
            var bars = new List<Bar>();
            for (int n = 0; n < 12; n++) bars.Add(Synth.B(Synth.Open(day, n), 4500, 4505 + (n == 3 ? 3 : 0), 4495 - (n == 7 ? 2 : 0), 4501, 1000, 0));
            var ib = InitialBalance.Compute(bars, new TimeSpan(9, 30, 0), 60);
            T.Expect(ib.Started, "IB started");
            T.Expect(!ib.Complete, "IB not complete while its last bar is still the last bar");
            bars.Add(Synth.B(Synth.Open(day, 12), 4501, 4510, 4500, 4509, 1000, 0));
            ib = InitialBalance.Compute(bars, new TimeSpan(9, 30, 0), 60);
            T.Eq(4508m, ib.High, "IB high"); T.Eq(4493m, ib.Low, "IB low");
            T.Expect(ib.Complete, "IB complete once a later bar exists");
            T.Eq(Side.Long, ib.Breakout, "close above IB high = breakout up");
            T.Eq(12, ib.BreakIndex, "breakout bar");

            var g = InitialBalance.ComputeGap(4480, bars);
            T.Eq(20m, g.Gap, "gap size"); T.Expect(!g.Filled, "gap not filled");
            bars.Add(Synth.B(Synth.Open(day, 13), 4509, 4510, 4479, 4482, 1000, 0));
            T.Expect(InitialBalance.ComputeGap(4480, bars).Filled, "gap filled once price trades through the prior close");
        }

        static void TestEvaluatorRules()
        {
            T.Section("Battle of orders (unit)");
            var p = P();
            var s = new BarStats { AvgAbsDelta = 100, AvgRange = 1.0m, AvgVolume = 1000, Count = 20 };
            var day = new DateTime(2026, 3, 2);
            var absorb = Synth.B(day, 100.5m, 101m, 99.75m, 100.5m, 1500, -300);
            T.Expect(BattleOfOrders.IsAbsorption(absorb, Side.Long, 100m, s, p), "heavy selling at support with no result = absorption");
            var noEffort = Synth.B(day, 100.5m, 101m, 99.75m, 100.5m, 1500, -50);
            T.Expect(!BattleOfOrders.IsAbsorption(noEffort, Side.Long, 100m, s, p), "light selling is not effort");
            var sellersWon = Synth.B(day, 101m, 101m, 99.75m, 99.9m, 1500, -300);
            T.Expect(!BattleOfOrders.IsAbsorption(sellersWon, Side.Long, 100m, s, p), "sellers closing at the lows got a result: not absorption");
            var farAway = Synth.B(day, 105.5m, 106m, 104.75m, 105.5m, 1500, -300);
            T.Expect(!BattleOfOrders.IsAbsorption(farAway, Side.Long, 100m, s, p), "no absorption away from the zone");
            var shortAbs = Synth.B(day, 99.5m, 100.25m, 99.4m, 99.5m, 1500, 300);
            T.Expect(BattleOfOrders.IsAbsorption(shortAbs, Side.Short, 100m, s, p), "mirror: heavy buying at resistance, no result");

            var conf = Synth.B(day, 100m, 102m, 99.9m, 101.75m, 1200, 200);
            T.Expect(BattleOfOrders.IsConfirmation(conf, absorb, Side.Long, s, p), "buyers win in the body with aligned delta");
            var weakBody = Synth.B(day, 100m, 102m, 98m, 100.5m, 1200, 200);
            T.Expect(!BattleOfOrders.IsConfirmation(weakBody, absorb, Side.Long, s, p), "a long wick with a small body is not 'in the body'");
        }

        // ====================================================================================
        static void TestEngineLong()
        {
            T.Section("Engine: textbook long at the prior VAL (rising value)");
            var r = Run(RisingCenters, (pr, day) => LongAtVal(pr, day), P());
            T.Eq(6, r.Engine.CompletedSessions, "six prior sessions are completed (today is still open)");
            T.Eq(Bias.Bullish, r.Final.Final, "bias is bullish after five rising value shifts");
            T.Eq(0, r.SignalsBeforeNextBar, "no signal while the confirming bar is still the last (forming) bar");
            T.Eq(1, r.Signals.Count, "exactly one signal once the confirming bar closes");
            if (r.Signals.Count == 1)
            {
                var sig = r.Signals[0];
                Console.WriteLine(sig.ToChecklist());
                T.Eq(Side.Long, sig.Side, "side is long");
                T.Eq(ZoneKind.Val, sig.Zone.Kind, "zone is the prior VAL");
                T.Eq(r.ConfirmGlobalIndex, sig.ConfirmGlobalIndex, "signal is stamped on the confirming bar");
                T.Expect(sig.AllRequiredMet, "all required checks met");
                T.Expect(sig.Stop < r.Prior.Val, "stop sits beyond the zone");
                T.Expect(sig.Target > sig.Entry && sig.Rr >= 1.0m, "target above entry with at least 1:1");
                T.Near(2.0m, sig.Rr, 0.01m, "no closer control level worth taking: falls back to ~1:2");
            }
        }

        static void TestEngineFilters()
        {
            T.Section("Engine: the same set-up must NOT fire when Fabio's conditions fail");
            var explosive = Run(RisingCenters, (pr, day) => LongAtVal(pr, day, explosiveApproach: true), P());
            T.Eq(0, explosive.Signals.Count, "price exploded effortlessly into the level: no trade (reactor filter)");

            var weak = Run(RisingCenters, (pr, day) => LongAtVal(pr, day, weakAbsorb: true), P());
            T.Eq(0, weak.Signals.Count, "no effort at the zone, nothing absorbed: no trade");

            var bear = Run(RisingCenters, (pr, day) => LongAtVal(pr, day, bearishConfirm: true), P());
            T.Eq(0, bear.Signals.Count, "other side did not win the battle: no trade");

            var counter = Run(FallingCenters, (pr, day) => LongAtVal(pr, day), P());
            T.Eq(Bias.Bearish, counter.Final.Final, "falling value shifts: bias is bearish");
            T.Eq(0, counter.Signals.Count, "long against the long-term auction: no trade");

            var loose = P(); loose.RequireBiasAlignment = false;
            var counterLoose = Run(FallingCenters, (pr, day) => LongAtVal(pr, day), loose);
            T.Eq(1, counterLoose.Signals.Count, "with bias alignment switched off the same battle does fire (rule is what blocked it)");

            var noApproach = P(); noApproach.RequireApproachFilter = false;
            var explosiveLoose = Run(RisingCenters, (pr, day) => LongAtVal(pr, day, explosiveApproach: true), noApproach);
            T.Eq(1, explosiveLoose.Signals.Count, "with the reactor filter switched off the explosive approach fires (filter is what blocked it)");
        }

        static void TestEngineMonitoring()
        {
            T.Section("Engine: monitoring and state hygiene");
            var once = Run(RisingCenters, (pr, day) => LongAtVal(pr, day), P(), 1);
            var many = Run(RisingCenters, (pr, day) => LongAtVal(pr, day), P(), 4);
            T.Eq(1, many.Signals.Count, "repeated ticks on the forming bar never duplicate a signal");
            T.Near(once.Final.Prior.Stats.Total, many.Final.Prior.Stats.Total, 0.001m, "repeated ticks never double count volume in the profile");
            T.Eq(once.Final.Prior.Stats.Poc, many.Final.Prior.Stats.Poc, "same POC with or without repeated ticks");

            T.Expect(once.Final.Best != null, "a checklist is always available for the nearest zone");
            T.Expect(once.Final.Zones.Count >= 3, "zones are published for drawing");

            // warm-up: first session has no prior profile -> nothing to trade yet
            var engine = new FabioEngine(P());
            var day = new DateTime(2026, 3, 2);
            EngineState st = null; int idx = 0;
            foreach (var kv in Synth.Session(day, 4500)) st = engine.Update(idx++, kv.Key, kv.Value, true);
            T.Expect(!st.Ready, "no prior session on day one: engine reports not ready");

            // ETH bars are ignored
            var eth = Synth.B(day.AddHours(3), 4500, 4501, 4499, 4500, 100, 0);
            st = engine.Update(idx++, eth, Synth.Uniform(4499, 4501, 100, 0), false);
            T.Expect(!st.InRth, "overnight bar is flagged non-RTH");
        }


        static void TestEngineShortAndIvb()
        {
            T.Section("Engine: short side mirrors the long side");
            var r = Run(FallingCenters, (pr, day) => ShortAtVah(pr, day), P());
            T.Eq(Bias.Bearish, r.Final.Final, "bias bearish after falling value");
            T.Eq(1, r.Signals.Count, "one short signal");
            if (r.Signals.Count == 1)
            {
                var sig = r.Signals[0];
                Console.WriteLine(sig.ToChecklist());
                T.Eq(Side.Short, sig.Side, "side is short");
                T.Eq(ZoneKind.Vah, sig.Zone.Kind, "zone is the prior VAH");
                T.Expect(sig.Stop > r.Prior.Vah, "stop sits above the zone");
                T.Expect(sig.Target < sig.Entry, "target below entry");
            }
            var wrongBias = Run(RisingCenters, (pr, day) => ShortAtVah(pr, day), P());
            T.Eq(0, wrongBias.Signals.Count, "short against rising value is blocked by the long-term auction");

            T.Section("Engine: IVB switched on does not crash and stays information-first");
            var p = P(); p.EnableIvb = true;
            var iv = Run(RisingCenters, (pr, day) => LongAtVal(pr, day), p);
            T.Expect(iv.Final.Ib != null && iv.Final.Ib.Started, "IB range is tracked");
            T.Expect(iv.Final.Gap != null, "gap info is tracked");
        }

        static void TestStress()
        {
            T.Section("Stress: 60 random sessions");
            var p = P();
            var engine = new FabioEngine(p);
            var rnd = new Random(12345);
            decimal price = 4500m;
            int idx = 0, signals = 0, maxPerSession = 0;
            var perSession = new Dictionary<DateTime, int>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            DateTime day = new DateTime(2026, 1, 5);
            bool ok = true;
            try
            {
                for (int d = 0; d < 60; d++, day = day.AddDays(1))
                {
                    for (int n = 0; n < 78; n++)
                    {
                        decimal step = (decimal)(rnd.NextDouble() - 0.5) * 3m;
                        decimal o = price, c = Math.Round((price + step) / 0.25m) * 0.25m;
                        decimal h = Math.Max(o, c) + (decimal)rnd.NextDouble() * 0.75m, l = Math.Min(o, c) - (decimal)rnd.NextDouble() * 0.75m;
                        h = Math.Round(h / 0.25m) * 0.25m; l = Math.Round(l / 0.25m) * 0.25m;
                        decimal delta = (decimal)(rnd.NextDouble() - 0.5) * 600m;
                        var bar = Synth.B(Synth.Open(day, n), o, h, l, c, 1000, delta);
                        var st = engine.Update(idx++, bar, Synth.Uniform(l, h, 1000, delta), true);
                        foreach (var sg in st.NewSignals)
                        {
                            signals++;
                            int cnt; perSession.TryGetValue(day, out cnt); perSession[day] = cnt + 1;
                            if (sg.Stop == sg.Entry || sg.Rr < p.MinRr) ok = false;
                        }
                        price = c;
                    }
                }
            }
            catch (Exception e) { ok = false; Console.WriteLine(e); }
            sw.Stop();
            foreach (var v in perSession.Values) maxPerSession = Math.Max(maxPerSession, v);
            Console.WriteLine("  {0} bars, {1} signals, max {2}/session, {3} ms", idx, signals, maxPerSession, sw.ElapsedMilliseconds);
            T.Expect(ok, "no exceptions and every signal has a valid stop and R:R");
            T.Expect(maxPerSession <= p.MaxSignalsPerSession, "never more than the per-session cap (not a machine gun)");
            T.Expect(sw.ElapsedMilliseconds < 20000, "fast enough to run bar by bar on a chart");
            T.Eq(59, engine.CompletedSessions, "completed sessions counted correctly");
        }

        static void TestChecklistText()
        {
            T.Section("Checklist text");
            var r = Run(RisingCenters, (pr, day) => LongAtVal(pr, day, bearishConfirm: true), P());
            var best = r.Final.Best;
            T.Expect(best != null && best.ToChecklist().Contains("Losing side absorbed"), "checklist lists the battle-of-orders conditions");
            T.Expect(best != null && !best.AllRequiredMet, "a failed battle shows as waiting, not as a signal");
            if (best != null) Console.WriteLine(best.ToChecklist());
        }

        static int Main()
        {
            TestProfile();
            TestBias();
            TestZones();
            TestClockAndFilter();
            TestIbAndGap();
            TestEvaluatorRules();
            TestEngineLong();
            TestEngineFilters();
            TestEngineMonitoring();
            TestEngineShortAndIvb();
            TestStress();
            TestChecklistText();
            ExecTests.Run();
            AddTests.Run();
            HostSimTests.Run();
            BacktestTests.Run();
            Console.WriteLine("\n{0} passed, {1} failed", T.Passed, T.Failed);
            return T.Failed == 0 ? 0 : 1;
        }
    }
}
