using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using FabioOrderflow.Core;

// fabio-replay: run raw trades through the same engine the ATAS indicator uses, then simulate the signals.
//
//   fabio-replay --ticks trades.csv --tick-size 0.25 --minutes 5 --rth 09:30-16:00 --point-value 50
//   fabio-replay --ticks trades.csv --set AbsorbDeltaMult=1.5 --set ZoneToleranceTicks=8 --trades-out trades_out.csv
//   fabio-replay --make-demo demo.csv --days 40 --seed 7        (SYNTHETIC data, only to check the pipeline)
namespace FabioOrderflow.ReplayTool
{
    internal static class Program
    {
        static int Main(string[] args)
        {
            try { return Run(args); }
            catch (Exception e) { Console.Error.WriteLine("error: " + e.Message); return 2; }
        }

        const string Help =
@"fabio-replay  - replay trades through the Fabio orderflow engine and simulate the signals

  --ticks FILE            CSV of trades: timestamp,price,volume[,aggressor]   (required unless --make-demo)
  --tick-size N           instrument tick size                                  (default 0.25)
  --minutes N             bar length in minutes                                 (default 5)
  --rth HH:MM-HH:MM       regular session in the clock of the timestamps        (default 09:30-16:00)
  --offset-hours N        added to timestamps that carry a UTC marker/offset    (default 0)
  --buy-tokens A,B        aggressor codes meaning 'buyer initiated'             (default B,BUY,1,+1)
  --sell-tokens A,B       aggressor codes meaning 'seller initiated'            (default S,SELL,-1)
  --point-value N         currency per 1.0 of price per contract                (default 1)
  --contracts N           contracts per trade                                   (default 1)
  --slippage-ticks N      adverse slippage on entry and stop exits              (default 1)
  --commission N          round-turn commission per contract                    (default 0)
  --max-daily-loss-r N    stop trading for the session after this loss in R     (default 3, 0 = off)
  --set Name=Value        override any FabioParams field (repeatable), e.g. AbsorbDeltaMult=1.5
  --show-checklists       print the full checklist of every signal
  --all-trades            list every simulated trade
  --trades-out FILE       write simulated trades to CSV
  --make-demo FILE        write SYNTHETIC trades (with --days N --seed N) and exit
";

        static int Run(string[] args)
        {
            if (args.Length == 0 || args.Contains("--help") || args.Contains("-h")) { Console.WriteLine(Help); return 0; }

            var opt = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var sets = new List<string>();
            var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--show-checklists" || a == "--all-trades") { flags.Add(a); continue; }
                if (!a.StartsWith("--")) throw new ArgumentException("unexpected argument " + a);
                if (i + 1 >= args.Length) throw new ArgumentException("missing value for " + a);
                if (a == "--set") sets.Add(args[++i]); else opt[a] = args[++i];
            }
            Func<string, string, string> get = (k, d) => opt.ContainsKey(k) ? opt[k] : d;
            var inv = CultureInfo.InvariantCulture;

            if (opt.ContainsKey("--make-demo"))
            {
                MakeDemo(opt["--make-demo"], int.Parse(get("--days", "30"), inv), int.Parse(get("--seed", "1"), inv), decimal.Parse(get("--tick-size", "0.25"), inv));
                return 0;
            }
            if (!opt.ContainsKey("--ticks")) throw new ArgumentException("--ticks FILE is required (see --help)");

            var p = new FabioParams { TickSize = decimal.Parse(get("--tick-size", "0.25"), inv) };
            var rth = get("--rth", "09:30-16:00").Split('-');
            if (rth.Length != 2) throw new ArgumentException("--rth expects HH:MM-HH:MM");
            p.RthStart = TimeSpan.Parse(rth[0], inv); p.RthEnd = TimeSpan.Parse(rth[1], inv);
            foreach (var s in sets) ApplySet(p, s);

            var bp = new BacktestParams
            {
                PointValue = decimal.Parse(get("--point-value", "1"), inv),
                Contracts = int.Parse(get("--contracts", "1"), inv),
                SlippageTicks = decimal.Parse(get("--slippage-ticks", "1"), inv),
                CommissionPerContract = decimal.Parse(get("--commission", "0"), inv),
                MaxDailyLossR = decimal.Parse(get("--max-daily-loss-r", "3"), inv)
            };
            int minutes = int.Parse(get("--minutes", "5"), inv);
            double offset = double.Parse(get("--offset-hours", "0"), inv);
            ICollection<string> buy = opt.ContainsKey("--buy-tokens") ? opt["--buy-tokens"].Split(',') : null;
            ICollection<string> sell = opt.ContainsKey("--sell-tokens") ? opt["--sell-tokens"].Split(',') : null;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            ReplayResult rep;
            using (var reader = File.OpenText(opt["--ticks"]))
                rep = Replay.Run(CsvTicks.Read(reader, offset, buy, sell), p, minutes);
            sw.Stop();

            Console.WriteLine("{0} bars ({1}-minute), {2} completed sessions, {3} signals   [{4} ms]", rep.Bars.Count, minutes, rep.Sessions, rep.Signals.Count, sw.ElapsedMilliseconds);
            if (rep.Bars.Count == 0) { Console.WriteLine("no trades read - check --ticks, --offset-hours and the timestamp format"); return 1; }
            int rthBars = rep.Bars.Count(b => b.Time.TimeOfDay >= p.RthStart && b.Time.TimeOfDay < p.RthEnd);
            if (rthBars == 0) Console.WriteLine("WARNING: no bar falls inside the regular session {0}-{1}. Adjust --rth / --offset-hours.", p.RthStart, p.RthEnd);

            if (flags.Contains("--show-checklists"))
                foreach (var s in rep.Signals) Console.WriteLine("\n@ {0}\n{1}", rep.Bars[s.ConfirmGlobalIndex].Time, s.ToChecklist());

            var bt = Backtester.Simulate(rep.Bars, rep.Signals, p, bp);
            Console.WriteLine();
            Console.Write(bt.Summary());
            Console.WriteLine("(bar-level simulation: entry at the next open + slippage, stop wins same-bar ties. A calibration aid, not proof of an edge.)");

            if (flags.Contains("--all-trades"))
                foreach (var t in bt.Trades)
                    Console.WriteLine("{0:yyyy-MM-dd HH:mm} {1,-5} {2,-26} in {3} out {4} {5,-12} {6,6:0.00}R", t.EntryTime, t.Side, t.Tag, t.Entry, t.Exit, t.ExitReason, t.R);

            if (opt.ContainsKey("--trades-out"))
            {
                var sb = new StringBuilder("entry_time,exit_time,side,setup,entry,stop,target,exit,exit_reason,risk,r,pnl_points,pnl_currency\n");
                foreach (var t in bt.Trades)
                    sb.AppendFormat(inv, "{0:yyyy-MM-dd HH:mm:ss},{1:yyyy-MM-dd HH:mm:ss},{2},{3},{4},{5},{6},{7},{8},{9},{10:0.####},{11},{12:0.##}\n",
                        t.EntryTime, t.ExitTime, t.Side, t.Tag, t.Entry, t.Stop, t.Target, t.Exit, t.ExitReason, t.Risk, t.R, t.PnlPoints, t.PnlCurrency);
                File.WriteAllText(opt["--trades-out"], sb.ToString());
                Console.WriteLine("trades written to " + opt["--trades-out"]);
            }
            return 0;
        }

        static void ApplySet(FabioParams p, string assignment)
        {
            int eq = assignment.IndexOf('=');
            if (eq <= 0) throw new ArgumentException("--set expects Name=Value, got '" + assignment + "'");
            string name = assignment.Substring(0, eq).Trim(), val = assignment.Substring(eq + 1).Trim();
            FieldInfo f = typeof(FabioParams).GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (f == null) throw new ArgumentException("unknown parameter '" + name + "'. Known: " + string.Join(", ", typeof(FabioParams).GetFields().Select(x => x.Name)));
            var inv = CultureInfo.InvariantCulture;
            object v;
            if (f.FieldType == typeof(decimal)) v = decimal.Parse(val, inv);
            else if (f.FieldType == typeof(int)) v = int.Parse(val, inv);
            else if (f.FieldType == typeof(bool)) v = bool.Parse(val);
            else if (f.FieldType == typeof(TimeSpan)) v = TimeSpan.Parse(val, inv);
            else if (f.FieldType.IsEnum) v = Enum.Parse(f.FieldType, val, true);
            else throw new ArgumentException("cannot set " + name);
            f.SetValue(p, v);
        }

        // ---- SYNTHETIC data only: exercises the pipeline, says nothing about any market ----------------------------------
        static void MakeDemo(string path, int days, int seed, decimal tick)
        {
            var rnd = new Random(seed);
            var inv = CultureInfo.InvariantCulture;
            decimal center = 4500m, price = center;
            DateTime day = new DateTime(2026, 1, 5);
            long count = 0;
            using (var w = new StreamWriter(path))
            {
                w.WriteLine("# SYNTHETIC trades for pipeline checks only");
                w.WriteLine("timestamp,price,volume,side");
                for (int d = 0; d < days; d++, day = day.AddDays(1))
                {
                    if (day.DayOfWeek == DayOfWeek.Saturday || day.DayOfWeek == DayOfWeek.Sunday) continue;
                    center += (decimal)(rnd.NextDouble() - 0.45) * 12m;
                    price = center + (decimal)(rnd.NextDouble() - 0.5) * 6m;
                    DateTime t = day.AddHours(9).AddMinutes(30);
                    DateTime end = day.AddHours(16);
                    while (t < end)
                    {
                        t = t.AddMilliseconds(200 + rnd.Next(2500));
                        decimal pull = (center - price) * 0.01m;
                        decimal move = ((decimal)(rnd.NextDouble() - 0.5) * 1.2m + pull);
                        decimal np = Math.Round((price + move) / tick) * tick;
                        string side = np > price ? "B" : np < price ? "S" : (rnd.Next(2) == 0 ? "B" : "S");
                        if (rnd.NextDouble() < 0.08) side = side == "B" ? "S" : "B";    // noisy aggressor flag
                        price = np;
                        int vol = rnd.NextDouble() < 0.01 ? 60 + rnd.Next(120) : 1 + (int)(-Math.Log(1 - rnd.NextDouble()) * 6);
                        w.WriteLine(string.Format(inv, "{0:yyyy-MM-dd HH:mm:ss.fff},{1},{2},{3}", t, price, vol, side));
                        count++;
                    }
                }
            }
            Console.WriteLine("wrote {0:N0} SYNTHETIC trades to {1} (not real market data)", count, path);
        }
    }
}
