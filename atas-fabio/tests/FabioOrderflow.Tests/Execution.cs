using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FabioOrderflow.Core;

namespace FabioOrderflow.Tests
{
    /// <summary>Tests for the real-data path: raw trades -> bars/footprint -> engine -> simulated trades.</summary>
    internal static class ExecTests
    {
        static readonly DateTime D0 = new DateTime(2026, 3, 2);

        public static void Run()
        {
            TestAggregator();
            TestCsv();
            TestBacktesterFills();
            TestBacktesterDiscipline();
            TestStats();
            TestBacktesterNullModel();
            TestReplayEndToEnd();
        }

        // ------------------------------------------------------------------------------------
        static void TestAggregator()
        {
            T.Section("Tick aggregation (trades -> bars with footprint)");
            var p = Program.P();
            var agg = new TickAggregator(p, 5);
            var t0 = D0.AddHours(9).AddMinutes(30);
            Func<int, decimal, decimal, Side, Tick> tk = (sec, px, vol, s) => new Tick { Time = t0.AddSeconds(sec), Price = px, Volume = vol, Aggressor = s };

            var done = new List<AggBar>();
            done.AddRange(agg.Add(tk(1, 100m, 10, Side.Long)));
            done.AddRange(agg.Add(tk(2, 100.25m, 5, Side.Short)));
            done.AddRange(agg.Add(tk(3, 99.75m, 3, Side.Short)));
            T.Eq(0, done.Count, "bar stays open inside its 5-minute window");
            done.AddRange(agg.Add(tk(301, 100m, 1, Side.Long)));
            T.Eq(1, done.Count, "first tick of the next window closes the bar");
            var b = done[0].Bar;
            T.Eq(t0, b.Time, "bar time = window start");
            T.Eq(100m, b.Open, "open"); T.Eq(100.25m, b.High, "high"); T.Eq(99.75m, b.Low, "low"); T.Eq(99.75m, b.Close, "close");
            T.Eq(18m, b.Volume, "volume"); T.Eq(2m, b.Delta, "delta = buys - sells");
            T.Eq(3, done[0].Levels.Length, "one footprint level per traded price");
            var l = done[0].Levels.First(x => x.Price == 100.25m);
            T.Expect(l.Bid == 5 && l.Ask == 0, "seller at 100.25 lands on the bid side");

            // tick rule for unknown aggressors
            var agg2 = new TickAggregator(p, 5);
            agg2.Add(tk(1, 100m, 10, Side.None));        // first print: unknown -> split, delta-neutral
            agg2.Add(tk(2, 100.25m, 10, Side.None));     // up-tick = buy
            agg2.Add(tk(3, 100.25m, 10, Side.None));     // unchanged = same side as before
            agg2.Add(tk(4, 100m, 10, Side.None));        // down-tick = sell
            var bar2 = agg2.Flush()[0].Bar;
            T.Eq(10m, bar2.Delta, "tick rule: 0 + 10 + 10 - 10");

            // big trades
            var agg3 = new TickAggregator(p, 5);
            agg3.Add(tk(1, 100m, 60, Side.Long)); agg3.Add(tk(2, 100m, 100, Side.Long)); agg3.Add(tk(3, 100m, 59, Side.Long)); agg3.Add(tk(4, 100m, 75, Side.Short));
            var bar3 = agg3.Flush()[0].Bar;
            T.Eq(2, bar3.BigBuys, "big buys counted at >= 60 contracts");
            T.Eq(1, bar3.BigSells, "big sells counted");
            T.Eq(60, agg3.Big.Threshold, "3 big prints in a candle: filter unchanged");
            var agg4 = new TickAggregator(p, 5);
            for (int i = 0; i < 4; i++) agg4.Add(tk(i, 100m, 80, Side.Long));
            agg4.Flush();
            T.Eq(75, agg4.Big.Threshold, "4 big prints in a candle: filter scales up (stated rule)");
        }

        static void TestCsv()
        {
            T.Section("CSV trade reader");
            string csv = "timestamp,price,volume,side\n# a comment\n\n2026-03-02 09:30:01.250,4500.25,12,B\n2026-03-02T14:30:02Z,4500.5,3,S\n2026-03-02 09:30:03,4500.25,2,\n";
            var ticks = CsvTicks.Read(new StringReader(csv), -5).ToList();
            T.Eq(3, ticks.Count, "header, comment and blank lines skipped");
            T.Eq(Side.Long, ticks[0].Aggressor, "B = buyer initiated");
            T.Eq(Side.Short, ticks[1].Aggressor, "S = seller initiated");
            T.Eq(Side.None, ticks[2].Aggressor, "missing side = unknown");
            T.Eq(D0.AddHours(9).AddMinutes(30).AddSeconds(2), ticks[1].Time, "UTC timestamp shifted by the offset into exchange time");
            T.Eq(D0.AddHours(9).AddMinutes(30).AddSeconds(1).AddMilliseconds(250), ticks[0].Time, "wall-clock timestamp kept as is");
            T.Eq(4500.25m, ticks[0].Price, "price parsed with invariant culture");

            var custom = CsvTicks.Read(new StringReader("2026-03-02 09:30:01,1,1,A\n"), 0, new[] { "A" }, new[] { "X" }).ToList();
            T.Eq(Side.Long, custom[0].Aggressor, "custom aggressor tokens");
            bool threw = false;
            try { CsvTicks.Read(new StringReader("2026-03-02 09:30:01,abc,1\n")).ToList(); } catch (FormatException) { threw = true; }
            T.Expect(threw, "bad numbers fail loudly instead of being skipped");
        }

        // ------------------------------------------------------------------------------------
        static List<Bar> QuietBars(DateTime start, int count, decimal o = 100m)
        {
            var l = new List<Bar>();
            for (int i = 0; i < count; i++)
                l.Add(Synth.B(start.AddMinutes(5 * i), o, o + 0.5m, o - 0.5m, o, 1000, 0));
            return l;
        }

        static SetupEvaluation Sig(Side side, int k, decimal stop, decimal target)
        {
            return new SetupEvaluation
            {
                Side = side, ConfirmGlobalIndex = k, Stop = stop, Target = target, Entry = 100m,
                Zone = new Zone { Kind = ZoneKind.Val, Price = 99m }, Model = "ZoneReaction"
            };
        }

        static Bar Bx(int n, decimal o, decimal h, decimal l, decimal c) { return Synth.B(Synth.Open(D0, n), o, h, l, c, 1000, 0); }

        static void TestBacktesterFills()
        {
            T.Section("Backtester fills (pessimistic by design)");
            var p = Program.P();
            var bp = new BacktestParams();   // 1 tick slippage, no loss cap interference

            // long: target
            var bars = new List<Bar> { Bx(0, 100, 100.5m, 99.5m, 100), Bx(1, 100, 101, 99.5m, 100.5m), Bx(2, 100.5m, 104.5m, 100, 104) };
            var r = Backtester.Simulate(bars, new[] { Sig(Side.Long, 0, 98.25m, 104.25m) }, p, bp);
            T.Eq(1, r.Trades.Count, "long trade taken on the NEXT bar");
            T.Eq(100.25m, r.Trades[0].Entry, "entry = next open + 1 tick slippage");
            T.Eq("target", r.Trades[0].ExitReason, "target exit");
            T.Near(2.0m, r.Trades[0].R, 0.001m, "R = (104.25 - 100.25) / 2.0");

            // long: stop pays slippage
            bars = new List<Bar> { Bx(0, 100, 100.5m, 99.5m, 100), Bx(1, 100, 101, 99.5m, 100.5m), Bx(2, 100.5m, 101, 98, 98.5m) };
            r = Backtester.Simulate(bars, new[] { Sig(Side.Long, 0, 98.25m, 104.25m) }, p, bp);
            T.Eq(98.0m, r.Trades[0].Exit, "stop exit pays one tick of slippage");
            T.Near(-1.125m, r.Trades[0].R, 0.001m, "loss is a bit worse than -1R");

            // both touched in the same bar -> stop wins
            bars = new List<Bar> { Bx(0, 100, 100.5m, 99.5m, 100), Bx(1, 100, 105, 98, 100) };
            r = Backtester.Simulate(bars, new[] { Sig(Side.Long, 0, 98.25m, 104.25m) }, p, bp);
            T.Eq("stop", r.Trades[0].ExitReason, "a bar touching both stop and target counts as a stop");

            // gap through the stop on a later bar fills at the open
            bars = new List<Bar> { Bx(0, 100, 100.5m, 99.5m, 100), Bx(1, 100, 100.5m, 99.75m, 100.25m), Bx(2, 97, 97.5m, 96, 96.5m) };
            r = Backtester.Simulate(bars, new[] { Sig(Side.Long, 0, 98.25m, 104.25m) }, p, bp);
            T.Eq("stop (gap)", r.Trades[0].ExitReason, "gap through the stop");
            T.Eq(97m, r.Trades[0].Exit, "fills at the gapped open, not at the stop");

            // short mirror
            bars = new List<Bar> { Bx(0, 100, 100.5m, 99.5m, 100), Bx(1, 100, 100.5m, 99, 99.5m), Bx(2, 99.5m, 100, 95, 96) };
            r = Backtester.Simulate(bars, new[] { Sig(Side.Short, 0, 101.75m, 95.75m) }, p, bp);
            T.Eq(99.75m, r.Trades[0].Entry, "short entry = next open - 1 tick");
            T.Near(2.0m, r.Trades[0].R, 0.001m, "short reaches 2R");

            // gapped through the stop at the entry
            bars = new List<Bar> { Bx(0, 100, 100.5m, 99.5m, 100), Bx(1, 98, 98.5m, 97.5m, 98) };
            r = Backtester.Simulate(bars, new[] { Sig(Side.Long, 0, 98.25m, 104.25m) }, p, bp);
            T.Eq(0, r.Trades.Count, "no trade if the next open is already through the stop");
            T.Eq(1, r.Skipped.Count, "the skipped signal is reported");

            // session end flatten
            var late = new List<Bar>();
            var d = D0.AddHours(15).AddMinutes(45);
            for (int i = 0; i < 3; i++) late.Add(Synth.B(d.AddMinutes(5 * i), 100, 100.5m, 99.5m, 100.25m, 1000, 0));
            late.Add(Synth.B(D0.AddDays(1).AddHours(9).AddMinutes(30), 100, 100.5m, 99.5m, 100, 1000, 0));
            r = Backtester.Simulate(late, new[] { Sig(Side.Long, 0, 98m, 110m) }, p, bp);
            T.Eq("session end", r.Trades[0].ExitReason, "flat at the end of the regular session");
            T.Eq(2, r.Trades[0].ExitBar, "exit on the last RTH bar");
        }

        static void TestBacktesterDiscipline()
        {
            T.Section("Backtester discipline (one position, daily loss cap)");
            var p = Program.P();
            // three identical stop-outs: signal bar, entry bar, stop bar
            var bars = new List<Bar>();
            int n = 0;
            for (int k = 0; k < 3; k++)
            {
                bars.Add(Bx(n++, 100, 100.5m, 99.5m, 100));
                bars.Add(Bx(n++, 100, 100.5m, 99.5m, 100));
                bars.Add(Bx(n++, 100, 100, 97, 97.5m));
            }
            var sigs = new[] { Sig(Side.Long, 0, 98.25m, 104.25m), Sig(Side.Long, 3, 98.25m, 104.25m), Sig(Side.Long, 6, 98.25m, 104.25m) };

            var r = Backtester.Simulate(bars, sigs, p, new BacktestParams { MaxDailyLossR = 0 });
            T.Eq(3, r.Trades.Count, "loss cap off: all three trades taken");

            r = Backtester.Simulate(bars, sigs, p, new BacktestParams { MaxDailyLossR = 1.5m });
            T.Eq(2, r.Trades.Count, "loss cap 1.5R: stops after -2.25R");
            T.Expect(r.Skipped.Contains("daily loss limit"), "skip reason recorded");

            // overlapping signals: second one arrives while the first is still open
            var quiet = QuietBars(D0.AddHours(9).AddMinutes(30), 12);
            r = Backtester.Simulate(quiet, new[] { Sig(Side.Long, 0, 90m, 120m), Sig(Side.Long, 3, 90m, 120m) }, p, new BacktestParams());
            T.Eq(1, r.Trades.Count, "only one position at a time");
            T.Expect(r.Skipped.Contains("already in a trade"), "overlap reported");
        }

        static void TestStats()
        {
            T.Section("Backtest statistics");
            var trades = new List<TradeRecord>();
            foreach (var rr in new[] { 2m, -1m, -1m, 2m, 2m })
                trades.Add(new TradeRecord { R = rr, Side = Side.Long, Tag = "ZoneReaction VAL", PnlCurrency = rr * 10 });
            var s = Backtester.Summarize(trades);
            T.Eq(5, s.Trades, "trade count"); T.Eq(3, s.Wins, "wins"); T.Eq(0.6m, s.WinRate, "win rate");
            T.Eq(2m, s.AvgWinR, "average win"); T.Eq(-1m, s.AvgLossR, "average loss");
            T.Eq(0.8m, s.ExpectancyR, "expectancy"); T.Eq(4m, s.TotalR, "total R");
            T.Eq(3m, s.ProfitFactor, "profit factor = 6 / 2");
            T.Eq(2m, s.MaxDrawdownR, "max drawdown from the +2R peak down to 0R");
            T.Eq(2, s.MaxLosingStreak, "losing streak");
            T.Eq(5, s.ByTag["ZoneReaction VAL"].Trades, "grouped by set-up tag");
            T.Eq(0, Backtester.Summarize(new List<TradeRecord>()).Trades, "empty result is safe");
        }


        /// <summary>
        /// Sanity check that the simulator itself has no hidden bias: on a driftless random walk, entries taken at random
        /// with stop = 1 unit and target = 2 units should win about 1/3 of the time (slightly less: slippage, stop wins ties).
        /// </summary>
        static void TestBacktesterNullModel()
        {
            T.Section("Backtester null model (random entries on a driftless random walk)");
            var p = Program.P();
            var rnd = new Random(42);
            Func<double> gauss = () => { double u1 = 1.0 - rnd.NextDouble(), u2 = rnd.NextDouble(); return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2); };
            foreach (var dir in new[] { Side.Long, Side.Short })
            {
                var bars = new List<Bar>(); var sigs = new List<SetupEvaluation>();
                decimal price = 1000m; int idx = 0;
                for (int d = 0; d < 1600; d++)
                {
                    DateTime day = D0.AddDays(d);
                    for (int n = 0; n < 78; n++, idx++)
                    {
                        decimal o = price, c = Math.Round((price + (decimal)gauss() * 1.0m) / 0.25m) * 0.25m;
                        decimal h = Math.Max(o, c) + Math.Round((decimal)Math.Abs(gauss()) * 0.3m / 0.25m) * 0.25m;
                        decimal l = Math.Min(o, c) - Math.Round((decimal)Math.Abs(gauss()) * 0.3m / 0.25m) * 0.25m;
                        bars.Add(Synth.B(Synth.Open(day, n), o, h, l, c, 1000, 0));
                        price = c;
                        if (n % 13 == 0 && n > 0 && n < 70)
                            sigs.Add(Sig(dir, idx, dir == Side.Long ? c - 4m : c + 4m, dir == Side.Long ? c + 8m : c - 8m));   // 1R = 4, 2R target = 8
                    }
                }
                var r = Backtester.Simulate(bars, sigs, p, new BacktestParams { SlippageTicks = 0, MaxDailyLossR = 0, FlattenAtSessionEnd = false });   // no session cut-off: truncation would favour the nearer stop
                int wins = r.Trades.Count(t => t.ExitReason == "target");
                int stops = r.Trades.Count(t => t.ExitReason.StartsWith("stop"));
                decimal rate = (decimal)wins / Math.Max(1, wins + stops);
                Console.WriteLine("  {0}: {1} trades, target {2}, stop {3}, hit rate {4:P1} (theory for 1:2 = 33.3%)", dir, r.Trades.Count, wins, stops, rate);
                T.Expect(r.Trades.Count > 2500, dir + ": enough trades for a statistical check");
                T.Expect(rate > 0.30m && rate < 0.36m, dir + ": target-before-stop rate close to the 1/3 expected on a fair random walk");
                T.Expect(Math.Abs(r.Stats.ExpectancyR) < 0.15m, dir + ": expectancy near zero without costs or an edge");
            }
        }

        // ------------------------------------------------------------------------------------
        /// <summary>Turns a bar plus its footprint levels into raw trades (open first, close last, volume per side preserved).</summary>
        static List<Tick> TicksFromBar(Bar b, PriceLevelVol[] levels)
        {
            var ticks = new List<Tick>();
            int sec = 0;
            Action<decimal, decimal, decimal> emit = (px, bid, ask) =>
            {
                if (bid > 0) ticks.Add(new Tick { Time = b.Time.AddSeconds(sec++ % 299), Price = px, Volume = bid, Aggressor = Side.Short });
                if (ask > 0) ticks.Add(new Tick { Time = b.Time.AddSeconds(sec++ % 299), Price = px, Volume = ask, Aggressor = Side.Long });
            };
            var ordered = levels.OrderBy(l => l.Price).ToList();
            var open = ordered.First(l => l.Price == b.Open);
            var close = ordered.First(l => l.Price == b.Close);
            bool same = b.Open == b.Close;
            emit(open.Price, same ? open.Bid / 2 : open.Bid, same ? open.Ask / 2 : open.Ask);
            foreach (var l in ordered) if (l.Price != b.Open && l.Price != b.Close) emit(l.Price, l.Bid, l.Ask);
            if (same) emit(close.Price, close.Bid / 2, close.Ask / 2);
            else emit(close.Price, close.Bid, close.Ask);
            return ticks;
        }

        static void TestReplayEndToEnd()
        {
            T.Section("Replay end to end: raw trades -> engine -> simulated trade");
            var p = Program.P();
            var ticks = new List<Tick>();
            VolumeProfile lastProfile = null;
            decimal[] bases = { 4500, 4504, 4508, 4512, 4516, 4520 };
            DateTime day = D0;
            foreach (var bs in bases)
            {
                lastProfile = new VolumeProfile();
                for (int n = 0; n < 78; n++)
                {
                    // triangle wave: price sweeps the whole day's range, so value is a wide, flat area
                    decimal c = Math.Round((bs + 4.5m * (1 - Math.Abs(4m * n / 78m - 2m))) / 0.25m) * 0.25m;
                    decimal delta = n % 2 == 0 ? 100 : -100;
                    var bar = Synth.B(Synth.Open(day, n), c, c + 0.5m, c - 0.5m, c + (n % 2 == 0 ? 0.25m : -0.25m), 1000, delta);
                    var lv = Synth.Uniform(bar.Low, bar.High, bar.Volume, bar.Delta);
                    foreach (var l in lv) lastProfile.Add(l);
                    ticks.AddRange(TicksFromBar(bar, lv));
                }
                day = day.AddDays(1);
            }
            var prior = lastProfile.Compute(p.TickSize, p.ValueAreaPct);

            var plan = Program.LongAtVal(prior, day);
            for (int k = 0; k < plan.Bars.Count; k++) ticks.AddRange(TicksFromBar(plan.Bars[k], plan.Levels[k]));
            // price then runs up through the target
            decimal val = prior.Val;
            int nextN = plan.Bars.Count;
            var run = Synth.B(Synth.Open(day, nextN), val + 2.0m, val + 8m, val + 1.75m, val + 7.5m, 1500, 300);
            ticks.AddRange(TicksFromBar(run, Synth.Uniform(run.Low, run.High, run.Volume, run.Delta)));
            var after = Synth.B(Synth.Open(day, nextN + 1), val + 7.5m, val + 8m, val + 7m, val + 7.5m, 800, 0);
            ticks.AddRange(TicksFromBar(after, Synth.Uniform(after.Low, after.High, after.Volume, after.Delta)));

            var rep = Replay.Run(ticks, p, 5);
            T.Eq(6 * 78 + plan.Bars.Count + 2, rep.Bars.Count, "every five-minute window became a bar");
            T.Eq(6, rep.Sessions, "six completed sessions seen by the engine");
            T.Eq(Bias.Bullish, rep.LastState.Final, "bias bullish from rising value");
            T.Eq(1, rep.Signals.Count, "exactly one set-up found from raw trades");
            if (rep.Signals.Count == 1)
            {
                var sig = rep.Signals[0];
                T.Eq(Side.Long, sig.Side, "long"); T.Eq(ZoneKind.Val, sig.Zone.Kind, "at the prior VAL");
                T.Expect(sig.AllRequiredMet, "all required checks met on real-format data");
                var big = sig.Checks.First(c => c.Name.StartsWith("Big trades"));
                T.Expect(big.State != CheckState.Unknown, "big-trade line has data when trades are available");

                // the nearest reference level worth >= 1:1 is the prior VAH, so that is the target (stated: previous control level / value area)
                T.Eq(prior.Vah, sig.Target, "target = prior VAH (nearest reference level worth at least 1:1)");

                var bt = Backtester.Simulate(rep.Bars, rep.Signals, p, new BacktestParams { PointValue = 50 });
                T.Eq(1, bt.Trades.Count, "one simulated trade");
                if (bt.Trades.Count == 1)
                {
                    Console.WriteLine(bt.Summary());
                    T.Eq("target", bt.Trades[0].ExitReason, "target reached on the run-up bar");
                    // fill = signal bar close + 1 tick slippage = val + 2.0; stop = val - 1.0 => risk 3.0; exit at the VAH
                    decimal fill = prior.Val + 2.0m, risk = 3.0m;
                    T.Eq(fill, bt.Trades[0].Entry, "entry = next open + 1 tick");
                    T.Near((prior.Vah - fill) / risk, bt.Trades[0].R, 0.001m, "R = (VAH - fill) / risk");
                    T.Near((prior.Vah - fill) * 50m, bt.Trades[0].PnlCurrency, 0.01m, "P&L = points x 50 point value");
                }
            }
        }
    }
}
