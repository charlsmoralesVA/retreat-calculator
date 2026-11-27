using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using FabioOrderflow.Core;

// fabio-backtest: years of CME trades -> bar file -> the same engine as the ATAS indicator -> report.
//
//   fabio-backtest import --databento ~/Downloads/GLBX-20240101-XXXX --instrument ES --out data/ES.bars.gz
//   fabio-backtest run    --bars data/ES.bars.gz --from 2022-01-01 --out reports/es-defaults
//   fabio-backtest sweep  --bars data/ES.bars.gz --from 2022-01-01 --split 2024-07-01 --out reports/es-sweep
//   fabio-backtest demo   --out data/DEMO.bars.gz        (SYNTHETIC, to practise the steps)
namespace FabioOrderflow.BacktestTool
{
    internal static class Program
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        const string Help =
@"fabio-backtest - backtest Fabio's orderflow engine on years of data, outside ATAS

COMMANDS
  import   trades -> bar file (5-minute bars with footprint, contract rolls stitched)
             --databento PATH...   Databento trades files (.csv, .csv.gz, .csv.zst) or folders of them; '-' = stdin
             --ticks FILE          or a generic CSV: timestamp,price,volume[,aggressor]  (timestamps in New York time
                                   unless they carry Z / an offset)
             --instrument ES       name for the reports; sets the tick size and costs for ES MES NQ MNQ
             --out FILE            bar file to write                     (default data/<instrument>.bars.gz)
             --tick-size N --minutes N --tz America/New_York
             --mode auto|parent|continuous   parent = all contracts (ES.FUT, exact rolls); continuous = one at a time
  run      backtest one set of settings
             --bars FILE           a bar file (from import, demo, or the ATAS indicator's export)
             --from DATE --to DATE trade window (earlier data still warms the engine up)
             --set Name=Value      change a setting (repeatable), e.g. --set AbsorbDeltaMult=1.5
             --out DIR             report folder                        (default reports/<instrument>-run)
  sweep    try a grid of settings: rank on the in-sample period, check on the out-of-sample one
             --split DATE          first out-of-sample day (default: 70% of the sessions in)
             --grid ""A=1,2;B=x,y""  default: " + BarBacktest.DefaultGrid + @"
             --min-trades N        in-sample trades needed to rank a setting (default 30)
             --threads N
  demo     write a SYNTHETIC bar file to practise with   (--out FILE --days N --seed N)
  info     describe a bar file                            (--bars FILE)

COSTS (run, sweep)
  --point-value N      currency per point per contract   (ES 50, MES 5, NQ 20, MNQ 2)
  --commission N       round turn per contract           (default ES/NQ 4.00, micros 1.00)
  --slippage-ticks N   on the entry and on stop exits     (default 1)
  --max-losers N       losing trades per session before stopping, like the strategy (default 2, 0 = off)
  --max-daily-loss-r N (default 3, 0 = off)
";

        static int Main(string[] args)
        {
            try { return Run(args); }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                Console.Error.WriteLine("error: " + e.Message);
                return 2;
            }
        }

        sealed class Opts
        {
            public string Command;
            public readonly Dictionary<string, List<string>> Values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            public string Get(string k, string d = null) { List<string> v; return Values.TryGetValue(k, out v) && v.Count > 0 ? v[v.Count - 1] : d; }
            public List<string> All(string k) { List<string> v; return Values.TryGetValue(k, out v) ? v : new List<string>(); }
            public bool Has(string k) { return Values.ContainsKey(k); }
        }

        static Opts Parse(string[] args)
        {
            var o = new Opts { Command = args[0].ToLowerInvariant() };
            string key = null;
            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i];
                if (a.StartsWith("--")) { key = a; if (!o.Values.ContainsKey(key)) o.Values[key] = new List<string>(); continue; }
                if (key == null) throw new ArgumentException("unexpected argument '" + a + "' (see --help)");
                o.Values[key].Add(a);
                if (key != "--databento") key = null;
            }
            return o;
        }

        static int Run(string[] args)
        {
            if (args.Length == 0 || args[0] == "--help" || args[0] == "-h" || args[0] == "help") { Console.WriteLine(Help); return 0; }
            var o = Parse(args);
            switch (o.Command)
            {
                case "import": return Import(o);
                case "run": return RunOne(o);
                case "sweep": return SweepCmd(o);
                case "demo": return Demo(o);
                case "info": return Info(o);
                default: throw new ArgumentException("unknown command '" + o.Command + "' (import, run, sweep, demo, info)");
            }
        }

        // ------------------------------------------------------------------ instruments

        static decimal DefaultTick(string inst) { return 0.25m; }

        /// <summary>ES, MES, NQ, MNQ from names like "ES", "ESZ5", "MNQH6" (ATAS exports carry the contract).</summary>
        static string Root(string inst)
        {
            string u = (inst ?? "").ToUpperInvariant();
            foreach (var r in new[] { "MES", "MNQ", "ES", "NQ" }) if (u.StartsWith(r)) return r;
            return u;
        }

        static decimal DefaultPointValue(string inst)
        {
            switch (Root(inst)) { case "ES": return 50; case "MES": return 5; case "NQ": return 20; case "MNQ": return 2; default: return 1; }
        }

        static decimal DefaultCommission(string inst)
        {
            switch (Root(inst)) { case "ES": case "NQ": return 4.00m; case "MES": case "MNQ": return 1.00m; default: return 0; }
        }

        // ------------------------------------------------------------------ import

        static int Import(Opts o)
        {
            string inst = o.Get("--instrument", "ES").ToUpperInvariant();
            var p = new FabioParams { TickSize = decimal.Parse(o.Get("--tick-size", DefaultTick(inst).ToString(Inv)), Inv) };
            int minutes = int.Parse(o.Get("--minutes", "5"), Inv);
            string tzId = o.Get("--tz", "America/New_York");
            var zone = ExchangeTime.Find(tzId);
            if (zone == null) throw new ArgumentException("time zone '" + tzId + "' not found");
            string outPath = o.Get("--out", Path.Combine("data", inst + ".bars.gz"));

            var header = new BarFileHeader { Instrument = inst, TickSize = p.TickSize, Minutes = minutes, TimeZone = tzId };
            var sw = Stopwatch.StartNew();

            if (o.Has("--ticks"))
            {
                header.Source = "csv:" + Path.GetFileName(o.Get("--ticks"));
                using (var w = new BarFile.Writer(outPath, header))
                using (var r = OpenAny(o.Get("--ticks")))
                {
                    var agg = new TickAggregator(p, minutes); int n = 0;
                    foreach (var t in CsvTicks.Read(r))
                        foreach (var b in agg.Add(t)) { w.Write(new BarRecord { Bar = b.Bar, Levels = b.Levels, Symbol = inst }); n++; }
                    foreach (var b in agg.Flush()) { w.Write(new BarRecord { Bar = b.Bar, Levels = b.Levels, Symbol = inst }); n++; }
                    Console.WriteLine("{0:N0} bars written to {1}  [{2:0.0} s]", n, outPath, sw.Elapsed.TotalSeconds);
                }
                return 0;
            }

            var files = ExpandInputs(o.All("--databento"));
            if (files.Count == 0) throw new ArgumentException("give --databento PATH (files or folders) or --ticks FILE");
            string mode = o.Get("--mode", "auto").ToLowerInvariant();
            bool single;
            if (mode == "parent") single = false;
            else if (mode == "continuous") single = true;
            else
            {
                // raw contract symbols (ESH4) or several contracts at once -> pick the front by volume (exact rolls);
                // one instrument id at a time (a continuous symbol such as ES.v.0, or no symbol column) -> follow it
                var syms = new HashSet<string>();
                using (var r = OpenAny(files[0]))
                    foreach (var t in DatabentoCsv.Read(r, inst).Take(300000)) syms.Add(t.Symbol);
                single = syms.Count <= 1 && syms.All(x => x.StartsWith("id") || x == inst);
                Console.WriteLine(single
                    ? "feed: a continuous contract (one instrument at a time); rolls are estimated from price gaps. For exact rolls download the parent symbol (e.g. ES.FUT) with 'map symbols' on."
                    : "feed: contract by contract (" + string.Join(", ", syms.Take(6)) + (syms.Count > 6 ? ", ..." : "") + "); the front month is chosen by volume each day.");
            }
            header.Source = "databento:" + (single ? "continuous" : "parent");

            long trades = 0; DateTime lastShown = DateTime.MinValue;
            using (var w = new BarFile.Writer(outPath, header))
            {
                var imp = new TradeImporter(p, minutes, w, zone, single);
                foreach (var f in files)
                {
                    using (var r = OpenAny(f))
                        foreach (var t in DatabentoCsv.Read(r, inst))
                        {
                            imp.Add(t);
                            if (++trades % 5000000 == 0 && (DateTime.UtcNow - lastShown).TotalSeconds > 2)
                            {
                                lastShown = DateTime.UtcNow;
                                Console.WriteLine("  {0:N0} trades read, at {1:yyyy-MM-dd}  [{2:0} s]", trades, t.Utc, sw.Elapsed.TotalSeconds);
                            }
                        }
                }
                imp.Finish();
                Console.WriteLine("{0:N0} trades -> {1:N0} bars, {2:yyyy-MM-dd} to {3:yyyy-MM-dd}, {4} contract rolls -> {5}  [{6:0} s]",
                    trades, imp.Bars, imp.FirstTime, imp.LastTime, single ? imp.Warnings.Count : imp.Stitcher.Rolls.Count, outPath, sw.Elapsed.TotalSeconds);
                if (!single)
                    foreach (var r in imp.Stitcher.Rolls) Console.WriteLine("  roll {0:yyyy-MM-dd} {1} -> {2}  offset {3}", r.TradingDate, r.OldSymbol, r.NewSymbol, r.Offset);
                foreach (var warn in imp.Warnings.Take(20)) Console.WriteLine("  note: " + warn);
            }
            return 0;
        }

        static List<string> ExpandInputs(List<string> inputs)
        {
            var res = new List<string>();
            foreach (var i in inputs)
            {
                string path = ExpandHome(i);
                if (path == "-") { res.Add(path); continue; }
                if (Directory.Exists(path))
                {
                    var found = Directory.GetFiles(path, "*", SearchOption.AllDirectories)
                        .Where(f => f.EndsWith(".csv") || f.EndsWith(".csv.gz") || f.EndsWith(".csv.zst"))
                        .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal).ToList();
                    if (found.Count == 0) throw new ArgumentException("no .csv / .csv.gz / .csv.zst files in " + path);
                    res.AddRange(found);
                }
                else if (File.Exists(path)) res.Add(path);
                else throw new ArgumentException("not found: " + path);
            }
            return res;
        }

        static string ExpandHome(string p)
        {
            if (p.StartsWith("~/")) return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), p.Substring(2));
            return p;
        }

        static TextReader OpenAny(string path)
        {
            if (path == "-") return Console.In;
            path = ExpandHome(path);
            if (path.EndsWith(".zst"))
            {
                var psi = new ProcessStartInfo("zstd", "-dc \"" + path + "\"") { RedirectStandardOutput = true, UseShellExecute = false };
                Process proc;
                try { proc = Process.Start(psi); }
                catch (Exception) { throw new InvalidOperationException("reading .zst files needs zstd: run 'brew install zstd' (or download from Databento with compression 'none')"); }
                return proc.StandardOutput;
            }
            Stream s = File.OpenRead(path);
            if (path.EndsWith(".gz")) s = new GZipStream(s, CompressionMode.Decompress);
            return new StreamReader(s, System.Text.Encoding.UTF8, false, 1 << 20);
        }

        // ------------------------------------------------------------------ run / sweep

        static void Costs(Opts o, string inst, BacktestParams bp)
        {
            bp.PointValue = decimal.Parse(o.Get("--point-value", DefaultPointValue(inst).ToString(Inv)), Inv);
            bp.CommissionPerContract = decimal.Parse(o.Get("--commission", DefaultCommission(inst).ToString(Inv)), Inv);
            bp.SlippageTicks = decimal.Parse(o.Get("--slippage-ticks", "1"), Inv);
            bp.MaxLosingTradesPerSession = int.Parse(o.Get("--max-losers", "2"), Inv);
            bp.MaxDailyLossR = decimal.Parse(o.Get("--max-daily-loss-r", "3"), Inv);
            bp.Contracts = 1;
            if (!o.Has("--point-value") && bp.PointValue == 1)
                Console.WriteLine("note: unknown instrument '" + inst + "': point value 1 and no commission. Pass --point-value and --commission for currency results (R results are unaffected).");
        }

        static DateTime? Date(Opts o, string k)
        {
            var v = o.Get(k);
            if (v == null) return null;
            return DateTime.ParseExact(v, "yyyy-MM-dd", Inv);
        }

        static BarSeries LoadSeries(Opts o, FabioParams p, out DateTime? from, out DateTime? to)
        {
            string path = o.Get("--bars");
            if (path == null) throw new ArgumentException("--bars FILE is required (make one with 'import' or 'demo')");
            path = ExpandHome(path);
            from = Date(o, "--from"); to = Date(o, "--to");
            // 130 calendar days before the trade window warm up the 3-month composition and the 5-session narrative
            DateTime? loadFrom = from.HasValue ? from.Value.AddDays(-130) : (DateTime?)null;
            var sw = Stopwatch.StartNew();
            var s = BarFile.Load(path, true, p.RthStart, p.RthEnd, loadFrom, to);
            Console.WriteLine("{0}: {1:N0} cash-session bars, {2} sessions, {3:yyyy-MM-dd} to {4:yyyy-MM-dd}, {5} rolls{6}  [load {7:0.0} s]",
                Path.GetFileName(path), s.Bars.Count, s.Sessions, s.First, s.Last, s.Rolls.Count, s.Header.Synthetic ? "  ** SYNTHETIC **" : "", sw.Elapsed.TotalSeconds);
            if (s.Bars.Count == 0) throw new InvalidOperationException("no cash-session bars in that range");
            p.TickSize = s.Header.TickSize;
            return s;
        }

        static FabioParams Params(Opts o)
        {
            var p = new FabioParams();
            foreach (var a in o.All("--set"))
            {
                int eq = a.IndexOf('=');
                if (eq <= 0) throw new ArgumentException("--set expects Name=Value, got '" + a + "'");
                BarBacktest.Set(p, a.Substring(0, eq), a.Substring(eq + 1));
            }
            return p;
        }

        static int RunOne(Opts o)
        {
            var p = Params(o);
            DateTime? from, to;
            var s = LoadSeries(o, p, out from, out to);
            var bp = new BacktestParams(); Costs(o, s.Header.Instrument, bp);
            var r = BarBacktest.Run(s, p, bp, from, to);
            var rep = new BacktestReport { Series = s, Run = r, TradeFrom = from, TradeTo = to, Title = "Fabio orderflow backtest - " + s.Header.Instrument };
            string dir = ExpandHome(o.Get("--out", Path.Combine("reports", s.Header.Instrument.ToLowerInvariant() + "-run")));
            rep.WriteAll(dir);
            PrintSummary(rep, r);
            Console.WriteLine("report: " + Path.Combine(dir, "report.html"));
            return 0;
        }

        static int SweepCmd(Opts o)
        {
            var p = Params(o);
            DateTime? from, to;
            var s = LoadSeries(o, p, out from, out to);
            var bp = new BacktestParams(); Costs(o, s.Header.Instrument, bp);
            var keys = s.Bars.Select(b => b.Time.Date).Where(d => !from.HasValue || d >= from.Value).Distinct().ToList();
            DateTime split = Date(o, "--split") ?? keys[Math.Min(keys.Count - 1, (int)(keys.Count * 0.7))];
            string grid = o.Get("--grid", BarBacktest.DefaultGrid);
            int minTrades = int.Parse(o.Get("--min-trades", "30"), Inv);
            int threads = int.Parse(o.Get("--threads", "0"), Inv);
            int combos = BarBacktest.ParseGrid(grid).Aggregate(1, (a, g) => a * g.Value.Length);
            Console.WriteLine("sweep: {0} combinations, in-sample before {1:yyyy-MM-dd}, out-of-sample from then on", combos, split);
            var sw = Stopwatch.StartNew(); int lastPct = -1;
            var series = s;      // bars before --from only warm the engine up; trades are kept from --from on
            var res = BarBacktest.Sweep(series, p, bp, grid, split, minTrades, threads, from, to, (d, n) =>
            {
                int pct = d * 10 / n;
                if (pct != lastPct) { lastPct = pct; Console.WriteLine("  {0}/{1} done  [{2:0} s]", d, n, sw.Elapsed.TotalSeconds); }
            });
            // the chosen setting, run on the whole trade window for the report
            var chosen = res.BestRobust ?? res.Rows.First();
            var pc = BarBacktest.Clone(p);
            foreach (var kv in chosen.Values) BarBacktest.Set(pc, kv.Key, kv.Value);
            var r = BarBacktest.Run(s, pc, bp, from, to);
            var rep = new BacktestReport { Series = s, Run = r, Sweep = res, TradeFrom = from, TradeTo = to, Title = "Fabio orderflow sweep - " + s.Header.Instrument };
            string dir = ExpandHome(o.Get("--out", Path.Combine("reports", s.Header.Instrument.ToLowerInvariant() + "-sweep")));
            rep.WriteAll(dir);
            Console.WriteLine();
            Console.WriteLine("top 5 in-sample (and how they did out-of-sample):");
            foreach (var row in res.Rows.Take(5))
                Console.WriteLine("  IS {0,4} trades {1,7:0.0}R | OOS {2,4} trades {3,7:0.0}R | {4}", row.InSample.Trades, row.InSample.TotalR, row.OutSample.Trades, row.OutSample.TotalR, row.Label);
            Console.WriteLine("most robust: " + chosen.Label);
            Console.WriteLine();
            PrintSummary(rep, r);
            Console.WriteLine("report: " + Path.Combine(dir, "report.html"));
            return 0;
        }

        static void PrintSummary(BacktestReport rep, RunResult r)
        {
            var o = rep.Overall;
            if (rep.Series.Header.Synthetic) Console.WriteLine("** SYNTHETIC DATA: pipeline check only, not a strategy result **");
            Console.WriteLine("{0} set-ups, {1} trades, win {2:0}%, total {3:0.0}R after costs, avg {4:0.00}R, profit factor {5:0.00}, max drawdown {6:0.0}R  [{7:0.0} s]",
                r.Signals, o.Trades, o.WinRate * 100, o.TotalR, o.AvgR, o.ProfitFactor, o.MaxDrawdownR, r.ElapsedMs / 1000.0);
            foreach (var y in rep.ByYear()) Console.WriteLine("  {0}: {1,4} trades  win {2,3:0}%  {3,7:0.0}R", y.Key, y.Trades, y.WinRate * 100, y.TotalR);
            var line = BarBacktest.SettingsLine(r.P);
            Console.WriteLine(line.Length == 0 ? "ATAS engine settings: defaults (leave the box empty)" : "ATAS engine settings line (indicator + strategy): " + line);
        }

        // ------------------------------------------------------------------ demo / info

        static int Demo(Opts o)
        {
            string outPath = ExpandHome(o.Get("--out", Path.Combine("data", "DEMO.bars.gz")));
            int days = int.Parse(o.Get("--days", "500"), Inv), seed = int.Parse(o.Get("--seed", "7"), Inv);
            var p = new FabioParams();
            var zone = ExchangeTime.Find("America/New_York");
            var sw = Stopwatch.StartNew();
            var header = new BarFileHeader { Instrument = "ES", TickSize = 0.25m, Minutes = 5, Source = "synthetic", Synthetic = true };
            using (var w = new BarFile.Writer(outPath, header))
            {
                var imp = new TradeImporter(p, 5, w, zone, false);
                foreach (var t in SyntheticMarket.Trades(new DateTime(2023, 1, 2), days, seed)) imp.Add(t);
                imp.Finish();
                Console.WriteLine("wrote {0:N0} SYNTHETIC bars ({1} calendar days, {2} rolls) to {3}  [{4:0.0} s]", imp.Bars, days, imp.Stitcher.Rolls.Count, outPath, sw.Elapsed.TotalSeconds);
            }
            Console.WriteLine("Synthetic data has no edge: use it only to practise the commands.");
            return 0;
        }

        static int Info(Opts o)
        {
            string path = ExpandHome(o.Get("--bars") ?? throw new ArgumentException("--bars FILE is required"));
            BarFileHeader h = null; long bars = 0; var rolls = new List<RollEvent>(); DateTime first = DateTime.MaxValue, last = DateTime.MinValue;
            using (var r = BarFile.OpenText(path))
                foreach (var x in BarFile.ReadAll(r, hh => h = hh))
                {
                    var roll = x as RollEvent;
                    if (roll != null) { rolls.Add(roll); continue; }
                    var b = ((BarRecord)x).Bar; bars++;
                    if (b.Time < first) first = b.Time; if (b.Time > last) last = b.Time;
                }
            Console.WriteLine("{0}: {1} {2}-minute bars, {3:N0} bars, {4:yyyy-MM-dd HH:mm} to {5:yyyy-MM-dd HH:mm} ({6}), source {7}{8}",
                Path.GetFileName(path), h.Instrument, h.Minutes, bars, first, last, h.TimeZone, h.Source, h.Synthetic ? ", SYNTHETIC" : "");
            foreach (var r in rolls) Console.WriteLine("  roll {0:yyyy-MM-dd} {1} -> {2} offset {3} ({4})", r.TradingDate, r.OldSymbol, r.NewSymbol, r.Offset, r.Method);
            return 0;
        }
    }
}
