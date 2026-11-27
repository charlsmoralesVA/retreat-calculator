using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace FabioOrderflow.Core
{
    /// <summary>Result of one engine + simulation pass over a bar series.</summary>
    public sealed class RunResult
    {
        public FabioParams P;
        public BacktestParams Bp;
        public List<TradeRecord> Trades = new List<TradeRecord>();
        public List<string> Skipped = new List<string>();
        public int Signals, Sessions;
        public long ElapsedMs;
    }

    /// <summary>Summary of a group of trades, in R after commission.</summary>
    public sealed class Perf
    {
        public string Key = "";
        public int Trades, Wins;
        public decimal TotalR, GrossWinR, GrossLossR, MaxDrawdownR, Currency;
        public int MaxLosingStreak;
        public decimal WinRate { get { return Trades == 0 ? 0 : (decimal)Wins / Trades; } }
        public decimal AvgR { get { return Trades == 0 ? 0 : TotalR / Trades; } }
        public decimal ProfitFactor { get { return GrossLossR == 0 ? (GrossWinR > 0 ? 99m : 0m) : GrossWinR / GrossLossR; } }

        public static Perf Of(string key, IEnumerable<TradeRecord> trades, BacktestParams bp)
        {
            var p = new Perf { Key = key };
            decimal eq = 0, peak = 0; int streak = 0;
            foreach (var t in trades.OrderBy(x => x.EntryTime))
            {
                decimal r = NetR(t, bp);
                p.Trades++; p.TotalR += r; p.Currency += t.PnlCurrency;
                if (r > 0) { p.Wins++; p.GrossWinR += r; streak = 0; }
                else { p.GrossLossR -= r; streak++; p.MaxLosingStreak = Math.Max(p.MaxLosingStreak, streak); }
                eq += r; peak = Math.Max(peak, eq); p.MaxDrawdownR = Math.Max(p.MaxDrawdownR, peak - eq);
            }
            return p;
        }

        /// <summary>R after the round-turn commission (slippage is already in the fills).</summary>
        public static decimal NetR(TradeRecord t, BacktestParams bp)
        {
            if (t.Risk <= 0) return t.R;
            decimal commissionPts = bp.PointValue > 0 ? bp.CommissionPerContract / bp.PointValue : 0;
            return (t.PnlPoints - commissionPts) / t.Risk;
        }
    }

    /// <summary>One parameter combination of a sweep, scored on the in-sample period and checked on the out-of-sample one.</summary>
    public sealed class SweepRow
    {
        public Dictionary<string, string> Values = new Dictionary<string, string>();
        public int[] GridIndex;
        public Perf InSample, OutSample;
        public decimal Score, RobustScore;
        public string Label { get { return string.Join("  ", Values.Select(kv => kv.Key + "=" + kv.Value)); } }
    }

    public sealed class SweepResult
    {
        public DateTime Split;
        public List<string> Names = new List<string>();
        public List<string[]> Grid = new List<string[]>();
        public List<SweepRow> Rows = new List<SweepRow>();
        public int MinTrades;
        public SweepRow BestRobust;
    }

    public static class BarBacktest
    {
        /// <summary>Feeds every bar to a fresh engine (exactly as the chart does, one closed bar at a time) and simulates the set-ups.</summary>
        public static RunResult Run(BarSeries s, FabioParams p, BacktestParams bp, DateTime? tradeFrom = null, DateTime? tradeTo = null)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var engine = new FabioEngine(p) { Lean = true };
            var signals = new List<SetupEvaluation>();
            for (int i = 0; i < s.Bars.Count; i++)
            {
                var st = engine.Update(i, s.Bars[i], s.Levels[i], false);
                if (st.NewSignals.Count > 0) signals.AddRange(st.NewSignals);
            }
            var use = signals.Where(x =>
            {
                if (x.ConfirmGlobalIndex < 0 || x.ConfirmGlobalIndex >= s.Bars.Count) return false;
                var d = s.Bars[x.ConfirmGlobalIndex].Time.Date;
                return (!tradeFrom.HasValue || d >= tradeFrom.Value.Date) && (!tradeTo.HasValue || d < tradeTo.Value.Date);
            }).ToList();
            var bt = Backtester.Simulate(s.Bars, use, p, bp);
            sw.Stop();
            return new RunResult
            {
                P = p, Bp = bp, Trades = bt.Trades.Where(t => t.ExitReason != "open").ToList(), Skipped = bt.Skipped,
                Signals = use.Count, Sessions = engine.CompletedSessions, ElapsedMs = sw.ElapsedMilliseconds
            };
        }

        public static FabioParams Clone(FabioParams p)
        {
            var c = new FabioParams();
            foreach (var f in typeof(FabioParams).GetFields(BindingFlags.Public | BindingFlags.Instance)) f.SetValue(c, f.GetValue(p));
            return c;
        }

        /// <summary>Sets a FabioParams field by name (case-insensitive). Throws with the list of names if unknown.</summary>
        public static void Set(FabioParams p, string name, string value)
        {
            var f = typeof(FabioParams).GetField(name.Trim(), BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (f == null) throw new ArgumentException("unknown parameter '" + name + "'. Known: " + string.Join(", ", typeof(FabioParams).GetFields().Select(x => x.Name)));
            var inv = CultureInfo.InvariantCulture; string v = value.Trim();
            object o;
            if (f.FieldType == typeof(decimal)) o = decimal.Parse(v, inv);
            else if (f.FieldType == typeof(int)) o = int.Parse(v, inv);
            else if (f.FieldType == typeof(bool)) o = bool.Parse(v);
            else if (f.FieldType == typeof(TimeSpan)) o = TimeSpan.Parse(v, inv);
            else if (f.FieldType.IsEnum) o = Enum.Parse(f.FieldType, v, true);
            else throw new ArgumentException("cannot set " + name);
            f.SetValue(p, o);
        }

        /// <summary>Settings that differ from the defaults, for the report.</summary>
        public static Dictionary<string, string> Changed(FabioParams p)
        {
            var d = new FabioParams(); var res = new Dictionary<string, string>();
            foreach (var f in typeof(FabioParams).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var a = f.GetValue(p); var b = f.GetValue(d);
                if (!Equals(a, b)) res[f.Name] = Convert.ToString(a, CultureInfo.InvariantCulture);
            }
            return res;
        }

        /// <summary>
        /// The tested settings as one line ("AbsorbDeltaMult=1.5;ZoneToleranceTicks=4"), to paste into the indicator's and
        /// the strategy's "Engine settings from the backtest" box so ATAS runs exactly what was tested. Instrument and
        /// session settings (tick size, cash session) are set on the chart and left out.
        /// </summary>
        public static string SettingsLine(FabioParams p)
        {
            var skip = new HashSet<string> { "TickSize", "RthStart", "RthEnd", "RthOnly", "LondonStart" };
            return string.Join(";", Changed(p).Where(kv => !skip.Contains(kv.Key)).Select(kv => kv.Key + "=" + kv.Value));
        }

        /// <summary>Applies a settings line (Name=Value;Name=Value). Returns null when all applied, else the errors.</summary>
        public static string ApplySettingsLine(FabioParams p, string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            var errors = new List<string>();
            foreach (var part in line.Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var a = part.Trim();
                if (a.Length == 0) continue;
                int eq = a.IndexOf('=');
                if (eq <= 0) { errors.Add("'" + a + "' is not Name=Value"); continue; }
                try { Set(p, a.Substring(0, eq), a.Substring(eq + 1)); }
                catch (Exception e) { errors.Add(a.Substring(0, eq).Trim() + ": " + (e is ArgumentException ? "unknown setting" : "bad value")); }
            }
            return errors.Count == 0 ? null : string.Join("; ", errors);
        }

        /// <summary>The default grid: the thresholds SKILL.md leaves to the trader (all marked Assumed / Principle in FabioParams).</summary>
        public const string DefaultGrid =
            "AbsorbDeltaMult=1.0,1.25,1.5,2.0;ConfirmDeltaMult=0.5,0.75,1.0;ZoneToleranceTicks=4,6,8;DefaultRr=1.5,2.0;RequireApproachFilter=true,false";

        public static List<KeyValuePair<string, string[]>> ParseGrid(string spec)
        {
            var res = new List<KeyValuePair<string, string[]>>();
            foreach (var part in spec.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) throw new ArgumentException("grid expects Name=v1,v2;Name2=v1,v2 - got '" + part + "'");
                var name = part.Substring(0, eq).Trim();
                var vals = part.Substring(eq + 1).Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
                if (vals.Length == 0) throw new ArgumentException("no values for " + name);
                Set(new FabioParams(), name, vals[0]);      // validates the name and the value type
                res.Add(new KeyValuePair<string, string[]>(name, vals));
            }
            return res;
        }

        /// <summary>
        /// Walk-forward sweep. Every combination runs over the whole series (the engine only looks backwards), trades before
        /// `split` are in-sample, the rest out-of-sample. Combinations are ranked on in-sample only; the out-of-sample
        /// columns show whether that ranking held up on data it never saw. RobustScore averages a combination with its grid
        /// neighbours: a good setting sitting among bad ones is likely luck.
        /// </summary>
        public static SweepResult Sweep(BarSeries s, FabioParams basis, BacktestParams bp, string gridSpec, DateTime split, int minTrades,
                                        int threads = 0, DateTime? tradeFrom = null, DateTime? tradeTo = null, Action<int, int> progress = null)
        {
            var grid = ParseGrid(gridSpec);
            var res = new SweepResult { Split = split, MinTrades = minTrades };
            foreach (var g in grid) { res.Names.Add(g.Key); res.Grid.Add(g.Value); }
            var combos = new List<int[]>();
            Enumerate(grid.Select(g => g.Value.Length).ToArray(), 0, new int[grid.Count], combos);
            var rows = new SweepRow[combos.Count];
            int done = 0;
            var opt = new ParallelOptions { MaxDegreeOfParallelism = threads > 0 ? threads : Math.Max(1, Environment.ProcessorCount) };
            Parallel.For(0, combos.Count, opt, i =>
            {
                var p = Clone(basis);
                var row = new SweepRow { GridIndex = combos[i] };
                for (int k = 0; k < grid.Count; k++)
                {
                    string v = grid[k].Value[combos[i][k]];
                    Set(p, grid[k].Key, v); row.Values[grid[k].Key] = v;
                }
                var r = Run(s, p, bp, tradeFrom, tradeTo);
                row.InSample = Perf.Of("in-sample", r.Trades.Where(t => t.EntryTime.Date < split.Date), bp);
                row.OutSample = Perf.Of("out-of-sample", r.Trades.Where(t => t.EntryTime.Date >= split.Date), bp);
                row.Score = Score(row.InSample, minTrades);
                rows[i] = row;
                int d = Interlocked.Increment(ref done);
                if (progress != null) progress(d, combos.Count);
            });
            res.Rows = rows.ToList();
            foreach (var row in res.Rows)
            {
                var near = res.Rows.Where(o => Neighbour(o.GridIndex, row.GridIndex)).ToList();
                row.RobustScore = near.Count == 0 ? row.Score : (row.Score + near.Average(o => o.Score)) / 2;
            }
            res.Rows = res.Rows.OrderByDescending(r => r.Score).ToList();
            res.BestRobust = res.Rows.Where(r => r.InSample.Trades >= minTrades).OrderByDescending(r => r.RobustScore).FirstOrDefault();
            return res;
        }

        /// <summary>In-sample score: total R after costs, penalised by half the max drawdown; too few trades scores nothing.</summary>
        public static decimal Score(Perf p, int minTrades)
        {
            if (p.Trades < minTrades) return -999m;
            return p.TotalR - 0.5m * p.MaxDrawdownR;
        }

        private static bool Neighbour(int[] a, int[] b)
        {
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
            {
                int d = Math.Abs(a[i] - b[i]);
                if (d > 1) return false;
                diff += d;
            }
            return diff == 1;
        }

        private static void Enumerate(int[] sizes, int k, int[] cur, List<int[]> outp)
        {
            if (k == sizes.Length) { outp.Add((int[])cur.Clone()); return; }
            for (int i = 0; i < sizes[k]; i++) { cur[k] = i; Enumerate(sizes, k + 1, cur, outp); }
        }
    }
}
