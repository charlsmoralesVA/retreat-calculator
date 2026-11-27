using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using FabioOrderflow.Core;

// fabio-bridge-demo: practice for Laya WITHOUT ATAS or live markets.
// Replays bars through the same engine as the ATAS strategy and serves the same bridge on localhost, so the
// fabio-atas MCP server and Laya work exactly as they will with ATAS. At every set-up the replay pauses until Laya
// decides (or the approval timeout passes), then prints how that trade would have played out.
//
//   fabio-bridge-demo                                   built-in SYNTHETIC example (one textbook long at the prior VAL)
//   fabio-bridge-demo --ticks trades.csv --tick-size 0.25 --minutes 5 --rth 09:30-16:00   your own trade history
//   options: --port 8766 --token SECRET --timeout 90 --speed 20 (bars per second) --hold (keep serving at the end)
//            --review-timeout 120   wait up to 120 s for Laya's read of EVERY candle (use with laya_runner.py)
namespace FabioOrderflow.BridgeDemo
{
    internal static class Program
    {
        static int Main(string[] args)
        {
            try { return Run(args); }
            catch (Exception e) { Console.Error.WriteLine("error: " + e.Message); return 2; }
        }

        static int Run(string[] args)
        {
            var opt = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool hold = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--hold") { hold = true; continue; }
                if (args[i] == "--help" || args[i] == "-h") { Console.WriteLine("see the comment at the top of Program.cs / laya/README.md"); return 0; }
                if (!args[i].StartsWith("--") || i + 1 >= args.Length) throw new ArgumentException("bad argument " + args[i]);
                opt[args[i]] = args[++i];
            }
            Func<string, string, string> get = (k, d) => opt.ContainsKey(k) ? opt[k] : d;
            var inv = CultureInfo.InvariantCulture;

            var p = new FabioParams { TickSize = decimal.Parse(get("--tick-size", "0.25"), inv) };
            var rth = get("--rth", "09:30-16:00").Split('-');
            p.RthStart = TimeSpan.Parse(rth[0], inv); p.RthEnd = TimeSpan.Parse(rth[1], inv);
            int port = int.Parse(get("--port", "8766"), inv);
            double speed = double.Parse(get("--speed", "20"), inv);

            var bridge = new LayaBridge { ApprovalTimeoutSeconds = int.Parse(get("--timeout", "90"), inv) };
            using (var server = new BridgeServer(bridge, port, get("--token", "")))
            {
                if (!server.Start()) { Console.Error.WriteLine(server.LastError); return 1; }
                Console.WriteLine("Bridge for Laya on " + server.Url + "  (approval timeout " + bridge.ApprovalTimeoutSeconds + " s)");

                List<AggBar> bars;
                string instrument;
                if (opt.ContainsKey("--ticks"))
                {
                    instrument = Path.GetFileNameWithoutExtension(opt["--ticks"]);
                    var agg = new TickAggregator(p, int.Parse(get("--minutes", "5"), inv));
                    bars = new List<AggBar>();
                    using (var r = File.OpenText(opt["--ticks"]))
                        foreach (var t in CsvTicks.Read(r, double.Parse(get("--offset-hours", "0"), inv))) bars.AddRange(agg.Add(t));
                    bars.AddRange(agg.Flush());
                    Console.WriteLine("Replaying " + bars.Count + " bars from " + opt["--ticks"]);
                }
                else
                {
                    instrument = "SYNTHETIC-ES";
                    bars = Synthetic.Build(p);
                    Console.WriteLine("Replaying " + bars.Count + " SYNTHETIC bars (practice data, not a market)");
                }

                var engine = new FabioEngine(p);
                var allBars = new List<Bar>();
                var signals = new List<SetupEvaluation>();
                int delayMs = speed <= 0 ? 0 : (int)(1000 / speed);
                int reviewTimeout = int.Parse(get("--review-timeout", "0"), inv);
                DateTime practiceDay = bars.Count > 0 ? bars[bars.Count - 1].Bar.Time.Date : DateTime.MinValue;   // reviews awaited on the last session only
                var sessionClock = new SessionClock(p.RthStart, p.RthEnd);
                bool planWaited = false;
                for (int i = 0; i < bars.Count; i++)
                {
                    bool practice = bars[i].Bar.Time.Date == practiceDay && sessionClock.IsRth(bars[i].Bar.Time);
                    // before the practice day opens, give Laya time to build her pre-market plan (like 09:00-09:30 live)
                    if (practice && !planWaited && reviewTimeout > 0)
                    {
                        planWaited = true;
                        Console.WriteLine("Pre-market: waiting up to " + (2 * reviewTimeout) + " s for Laya's plan for " + practiceDay.ToString("yyyy-MM-dd", inv) + "...");
                        var until = DateTime.UtcNow.AddSeconds(2 * reviewTimeout);
                        Func<bool> hasPlan = () => { var pl = bridge.Plan; return pl != null && Convert.ToString(pl["session"]) == practiceDay.ToString("yyyy-MM-dd", inv); };
                        while (!hasPlan() && DateTime.UtcNow < until) Thread.Sleep(200);
                        var plan = bridge.Plan;
                        Console.WriteLine(hasPlan() ? "LAYA PLAN: " + plan["bias"] + " - " + (plan.ContainsKey("summary") ? plan["summary"] : "") : "(no plan within the wait)");
                    }
                    allBars.Add(bars[i].Bar);
                    var st = engine.Update(i, bars[i].Bar, bars[i].Levels, false);
                    bridge.SetState(LayaBridge.StateToJson(st, instrument, bars[i].Bar.Time, null, 0));

                    // 1. set-up tickets first, so Laya finds them pending when she wakes on the candle
                    var tickets = new List<KeyValuePair<SetupTicket, SetupEvaluation>>();
                    foreach (var s in st.NewSignals)
                    {
                        signals.Add(s);
                        var tk = bridge.Publish(s, instrument, DateTime.UtcNow, true);
                        tickets.Add(new KeyValuePair<SetupTicket, SetupEvaluation>(tk, s));
                        Console.WriteLine();
                        Console.WriteLine("SET-UP " + tk.Id + " at " + bars[s.ConfirmGlobalIndex].Bar.Time.ToString("yyyy-MM-dd HH:mm", inv) + " - waiting for Laya...");
                        Console.Write(s.ToChecklist());
                    }

                    // 2. the closed candle(s); optionally wait for Laya's read of each
                    foreach (var c in st.ClosedCandles)
                    {
                        bridge.PublishCandle(c);
                        if (reviewTimeout <= 0 || c.Time.Date != practiceDay) continue;
                        var until = DateTime.UtcNow.AddSeconds(reviewTimeout);
                        while (bridge.ReviewOf(c.Seq) == null && DateTime.UtcNow < until) Thread.Sleep(100);
                        var rv = bridge.ReviewOf(c.Seq);
                        Console.WriteLine(string.Format(inv, "{0:HH:mm} seq {1}  LAYA {2}", c.Time, c.Seq,
                            rv == null ? "(no read within " + reviewTimeout + " s)" : rv["phase"] + " / " + rv["action"] + " - " + rv["read"]));
                    }

                    // 3. wait for the decision on each set-up, then replay its outcome
                    foreach (var kv in tickets)
                    {
                        var tk = kv.Key; var s = kv.Value;
                        var status = TicketStatus.Pending;
                        while (status == TicketStatus.Pending) { Thread.Sleep(200); status = bridge.StatusOf(tk.Id, DateTime.UtcNow); }
                        var decided = bridge.Tickets("all", DateTime.UtcNow, 500).First(x => x.Id == tk.Id);
                        Console.WriteLine("LAYA: " + status.ToString().ToUpperInvariant() + (string.IsNullOrEmpty(decided.Reason) ? "" : " - " + decided.Reason));
                        if (status == TicketStatus.Approved)
                        {
                            var future = bars.Select(b => b.Bar).ToList();
                            var res = Backtester.Simulate(future, new[] { s }, p, new BacktestParams());
                            var tr = res.Trades.FirstOrDefault();
                            string outcome = tr == null ? "not filled (" + string.Join(", ", res.Skipped) + ")" : string.Format(inv, "{0} at {1}, {2:0.00}R", tr.ExitReason, tr.Exit, tr.R);
                            bridge.SetOutcome(tk.Id, TicketStatus.Taken, "replay: " + outcome, DateTime.UtcNow);
                            Console.WriteLine("REPLAY OUTCOME: " + outcome);
                        }
                        else bridge.SetOutcome(tk.Id, null, "not traded: " + status.ToString().ToLowerInvariant(), DateTime.UtcNow);
                    }
                    if (delayMs > 0 && practice) Thread.Sleep(delayMs);                    // history loads at once, like a chart
                }

                var taken = bridge.Tickets("all", DateTime.UtcNow, 500);
                Console.WriteLine();
                Console.WriteLine(string.Format("Replay finished: {0} set-ups, {1} approved, {2} rejected, {3} expired.", taken.Count,
                    taken.Count(t => t.Status == TicketStatus.Taken), taken.Count(t => t.Status == TicketStatus.Rejected), taken.Count(t => t.Status == TicketStatus.Expired)));
                if (hold)
                {
                    Console.WriteLine("Still serving " + server.Url + " (Ctrl+C to stop).");
                    Thread.Sleep(Timeout.Infinite);
                }
            }
            return 0;
        }
    }

    /// <summary>Six sessions of rising value, then a textbook long at the prior VAL (same shape as the test suite).</summary>
    internal static class Synthetic
    {
        const decimal Tick = 0.25m;
        static decimal R(decimal x) { return Math.Round(x / Tick) * Tick; }
        static DateTime At(DateTime day, int n) { return day.Date.AddHours(9).AddMinutes(30 + 5 * n); }

        static PriceLevelVol[] Uniform(decimal lo, decimal hi, decimal vol, decimal delta)
        {
            int n = (int)Math.Round((hi - lo) / Tick) + 1;
            decimal ask = (1 + delta / vol) / 2, v = vol / n;
            return Enumerable.Range(0, n).Select(i => new PriceLevelVol(R(lo + i * Tick), v * (1 - ask), v * ask)).ToArray();
        }

        static AggBar B(DateTime t, decimal o, decimal h, decimal l, decimal c, decimal vol, decimal delta)
        {
            return new AggBar { Bar = new Bar { Time = t, Open = o, High = h, Low = l, Close = c, Volume = vol, Delta = delta }, Levels = Uniform(l, h, vol, delta) };
        }

        public static List<AggBar> Build(FabioParams p)
        {
            var bars = new List<AggBar>();
            var day = new DateTime(2026, 3, 2);
            VolumeProfile last = null;
            foreach (var bs in new decimal[] { 4500, 4504, 4508, 4512, 4516, 4520 })
            {
                last = new VolumeProfile();
                for (int n = 0; n < 78; n++)
                {
                    decimal c = R(bs + 4.5m * (1 - Math.Abs(4m * n / 78m - 2m)));
                    var b = B(At(day, n), c, c + 0.5m, c - 0.5m, c + (n % 2 == 0 ? 0.25m : -0.25m), 1000, n % 2 == 0 ? 100 : -100);
                    foreach (var l in b.Levels) last.Add(l);
                    bars.Add(b);
                }
                day = day.AddDays(1);
            }
            var prior = last.Compute(p.TickSize, p.ValueAreaPct);
            decimal val = prior.Val, poc = prior.Poc;
            // overnight session before the practice day (18:00 -> 09:25): quiet inside prior value, London a bit higher
            for (var t = day.AddDays(-1).AddHours(18); t < day.AddHours(9).AddMinutes(30); t = t.AddMinutes(5))
            {
                bool london = t.Date == day && t.TimeOfDay >= new TimeSpan(3, 0, 0);
                decimal mid = london ? poc + 1m : poc - 0.5m;
                bars.Add(B(t, mid, mid + 0.75m, mid - 0.75m, mid + 0.25m, 250, 15));
            }
            int k = 0;
            for (int i = 0; i < 6; i++, k++) bars.Add(B(At(day, k), poc, poc + 0.5m, poc - 0.5m, poc + (i % 2 == 0 ? 0.25m : -0.25m), 1000, i % 2 == 0 ? 100 : -100));
            decimal start = val + 3m;
            for (int m = 0; m < 6; m++, k++) { decimal o = start - 0.5m * m, c = o - 0.5m; bars.Add(B(At(day, k), o, o + 0.25m, c - 0.25m, c, 1000, -110)); }
            bars.Add(B(At(day, k++), val, val + 0.5m, val - 0.5m, val, 1500, -300));           // sellers absorbed at VAL
            bars.Add(B(At(day, k++), val, val + 2m, val - 0.25m, val + 1.75m, 1200, 200));     // buyers win the body
            bars.Add(B(At(day, k++), val + 2m, val + 8m, val + 1.75m, val + 7.5m, 1500, 300));  // run-up
            for (int i = 0; i < 4; i++) bars.Add(B(At(day, k++), val + 7.5m, val + 8m, val + 7m, val + 7.5m, 800, 0));
            return bars;
        }
    }
}
