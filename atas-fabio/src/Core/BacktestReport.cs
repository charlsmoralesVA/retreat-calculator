using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace FabioOrderflow.Core
{
    /// <summary>Writes a backtest (and optional sweep) as summary.json, trades.csv, report.md and report.html.</summary>
    public sealed class BacktestReport
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public BarSeries Series;
        public RunResult Run;
        public SweepResult Sweep;
        public DateTime? TradeFrom, TradeTo;
        public string Title = "Fabio orderflow backtest";

        public Perf Overall { get { return Perf.Of("all", Run.Trades, Run.Bp); } }

        public List<Perf> ByYear() { return Group(t => t.EntryTime.Year.ToString(Inv)); }
        public List<Perf> ByMonth() { return Group(t => t.EntryTime.ToString("yyyy-MM", Inv)); }
        public List<Perf> BySetup() { return Group(t => t.Tag + " " + (t.Side == Side.Long ? "long" : "short")); }
        public List<Perf> ByHour() { return Group(t => t.EntryTime.ToString("HH", Inv) + ":00"); }
        public List<Perf> ByExit() { return Group(t => t.ExitReason); }

        private List<Perf> Group(Func<TradeRecord, string> key)
        {
            return Run.Trades.GroupBy(key).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => Perf.Of(g.Key, g, Run.Bp)).ToList();
        }

        public List<string> Caveats()
        {
            var c = new List<string>();
            if (Series.Header.Synthetic) c.Add("SYNTHETIC DATA: these numbers only show that the pipeline works. They say nothing about the strategy.");
            c.Add("Bar-level simulation: entry at the next bar's open plus slippage; when a bar touches both stop and target, the stop is assumed first.");
            c.Add("Laya's approval is not in this test: it measures the engine's set-ups. Laya can only remove trades from this list.");
            c.Add("The engine follows SKILL.md only; the models in the skill's missing reference files are not tested.");
            if (Run.Trades.Count < 100) c.Add("Fewer than 100 trades: too few to tell an edge from luck.");
            if (Sweep != null) c.Add("Sweep: choose settings by the out-of-sample columns and by robustness, not by the best in-sample line.");
            if (Series.Rolls.Any(r => r.Method == "price-gap")) c.Add("Some contract rolls were estimated from price gaps (single-contract data); profiles near those dates are approximate.");
            if (Series.Bars.Count > 0 && Series.Bars[Series.Bars.Count / 2].BigBuys < 0) c.Add("No big-trade data in this file (ATAS history has none): the big-trade confluence is unknown here; it never decides a set-up.");
            return c;
        }

        // ---------- JSON ----------
        public Dictionary<string, object> ToJson()
        {
            var d = new Dictionary<string, object>();
            var h = Series.Header;
            d["title"] = Title;
            d["data"] = new Dictionary<string, object>
            {
                { "instrument", h.Instrument }, { "source", h.Source }, { "synthetic", h.Synthetic }, { "bar_minutes", h.Minutes },
                { "tick_size", h.TickSize }, { "first_bar", Series.First }, { "last_bar", Series.Last }, { "bars", Series.Bars.Count },
                { "sessions", Series.Sessions }, { "rolls", Series.Rolls.Count },
                { "trade_from", TradeFrom.HasValue ? (object)TradeFrom.Value.ToString("yyyy-MM-dd", Inv) : null },
                { "trade_to", TradeTo.HasValue ? (object)TradeTo.Value.ToString("yyyy-MM-dd", Inv) : null }
            };
            var bp = Run.Bp;
            d["costs"] = new Dictionary<string, object>
            {
                { "point_value", bp.PointValue }, { "contracts", bp.Contracts }, { "slippage_ticks", bp.SlippageTicks },
                { "commission_round_turn", bp.CommissionPerContract }, { "max_daily_loss_r", bp.MaxDailyLossR },
                { "max_losing_trades_per_session", bp.MaxLosingTradesPerSession }, { "flat_at_session_end", bp.FlattenAtSessionEnd }
            };
            d["settings_changed"] = BarBacktest.Changed(Run.P).ToDictionary(kv => kv.Key, kv => (object)kv.Value);
            d["settings_line"] = BarBacktest.SettingsLine(Run.P);
            d["signals"] = Run.Signals;
            d["skipped"] = Run.Skipped.GroupBy(x => x).ToDictionary(g => g.Key, g => (object)g.Count());
            d["overall"] = PerfJson(Overall);
            d["by_year"] = ByYear().Select(PerfJson).Cast<object>().ToList();
            d["by_setup"] = BySetup().Select(PerfJson).Cast<object>().ToList();
            d["by_hour"] = ByHour().Select(PerfJson).Cast<object>().ToList();
            d["by_exit"] = ByExit().Select(PerfJson).Cast<object>().ToList();
            d["by_month"] = ByMonth().Select(PerfJson).Cast<object>().ToList();
            if (Sweep != null)
            {
                d["sweep"] = new Dictionary<string, object>
                {
                    { "split", Sweep.Split.ToString("yyyy-MM-dd", Inv) }, { "min_trades", Sweep.MinTrades },
                    { "grid", Sweep.Names.Select((n, i) => (object)(n + "=" + string.Join(",", Sweep.Grid[i]))).ToList() },
                    { "combinations", Sweep.Rows.Count },
                    { "best_robust", Sweep.BestRobust == null ? null : RowJson(Sweep.BestRobust) },
                    { "top_in_sample", Sweep.Rows.Take(15).Select(r => (object)RowJson(r)).ToList() }
                };
            }
            d["caveats"] = Caveats().Cast<object>().ToList();
            return d;
        }

        private static Dictionary<string, object> PerfJson(Perf p)
        {
            return new Dictionary<string, object>
            {
                { "group", p.Key }, { "trades", p.Trades }, { "win_rate", R(p.WinRate) }, { "avg_r", R(p.AvgR) }, { "total_r", R(p.TotalR) },
                { "profit_factor", R(p.ProfitFactor) }, { "max_drawdown_r", R(p.MaxDrawdownR) }, { "max_losing_streak", p.MaxLosingStreak },
                { "pnl_currency", decimal.Round(p.Currency, 0) }
            };
        }

        private static Dictionary<string, object> RowJson(SweepRow r)
        {
            return new Dictionary<string, object>
            {
                { "settings", r.Values.ToDictionary(kv => kv.Key, kv => (object)kv.Value) },
                { "in_sample", PerfJson(r.InSample) }, { "out_of_sample", PerfJson(r.OutSample) },
                { "score", R(r.Score) }, { "robust_score", R(r.RobustScore) }
            };
        }

        private static decimal R(decimal v) { return decimal.Round(v, 3); }

        // ---------- CSV ----------
        public string TradesCsv()
        {
            var sb = new StringBuilder("entry_time,exit_time,side,setup,entry,stop,target,exit,exit_reason,risk_points,r,r_after_costs,pnl_currency\n");
            foreach (var t in Run.Trades)
                sb.AppendFormat(Inv, "{0:yyyy-MM-dd HH:mm},{1:yyyy-MM-dd HH:mm},{2},{3},{4},{5},{6},{7},{8},{9},{10:0.###},{11:0.###},{12:0.##}\n",
                    t.EntryTime, t.ExitTime, t.Side, t.Tag, t.Entry, t.Stop, t.Target, t.Exit, t.ExitReason, t.Risk, t.R, Perf.NetR(t, Run.Bp), t.PnlCurrency);
            return sb.ToString();
        }

        // ---------- Markdown ----------
        public string Markdown()
        {
            var sb = new StringBuilder();
            var h = Series.Header; var o = Overall;
            sb.AppendLine("# " + Title).AppendLine();
            if (h.Synthetic) sb.AppendLine("> **SYNTHETIC DATA - pipeline check only, not a strategy result.**").AppendLine();
            sb.AppendFormat(Inv, "{0} {1}-minute bars, {2:yyyy-MM-dd} to {3:yyyy-MM-dd}, {4} cash sessions, source: {5}. Costs: {6} tick(s) slippage, {7} round turn per contract, point value {8}.\n\n",
                h.Instrument, h.Minutes, Series.First, Series.Last, Series.Sessions, h.Source, Run.Bp.SlippageTicks, Run.Bp.CommissionPerContract, Run.Bp.PointValue);
            sb.AppendLine("## Overall (R after costs)").AppendLine();
            sb.AppendLine(Table(new[] { o }));
            sb.AppendLine("## By year").AppendLine().AppendLine(Table(ByYear()));
            sb.AppendLine("## By set-up").AppendLine().AppendLine(Table(BySetup()));
            sb.AppendLine("## By entry hour (New York)").AppendLine().AppendLine(Table(ByHour()));
            sb.AppendLine("## By exit").AppendLine().AppendLine(Table(ByExit()));
            var changed = BarBacktest.Changed(Run.P);
            sb.AppendLine("## Settings").AppendLine();
            sb.AppendLine(changed.Count == 0 ? "All defaults." : string.Join("\n", changed.Select(kv => "- " + kv.Key + " = " + kv.Value))).AppendLine();
            var line = BarBacktest.SettingsLine(Run.P);
            sb.AppendLine(line.Length == 0 ? "In ATAS: leave \"Engine settings from the backtest\" empty (defaults)." : "In ATAS, paste into \"Engine settings from the backtest\" (indicator and strategy): `" + line + "`").AppendLine();
            if (Sweep != null)
            {
                sb.AppendFormat(Inv, "## Sweep: {0} combinations, in-sample before {1:yyyy-MM-dd}, out-of-sample from then on\n\n", Sweep.Rows.Count, Sweep.Split);
                sb.AppendLine("| # | Settings | IS trades | IS R | IS max DD | OOS trades | OOS R | OOS avg R | robust |");
                sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
                int i = 0;
                foreach (var r in Sweep.Rows.Take(15))
                    sb.AppendFormat(Inv, "| {0} | {1} | {2} | {3:0.0} | {4:0.0} | {5} | {6:0.0} | {7:0.00} | {8:0.0} |\n", ++i, r.Label, r.InSample.Trades, r.InSample.TotalR, r.InSample.MaxDrawdownR, r.OutSample.Trades, r.OutSample.TotalR, r.OutSample.AvgR, r.RobustScore);
                if (Sweep.BestRobust != null)
                    sb.AppendLine().AppendLine("Most robust in-sample: **" + Sweep.BestRobust.Label + "** (out-of-sample " +
                        string.Format(Inv, "{0} trades, {1:0.0}R, avg {2:0.00}R", Sweep.BestRobust.OutSample.Trades, Sweep.BestRobust.OutSample.TotalR, Sweep.BestRobust.OutSample.AvgR) + ")");
                sb.AppendLine();
            }
            sb.AppendLine("## Read this before deciding anything").AppendLine();
            foreach (var c in Caveats()) sb.AppendLine("- " + c);
            return sb.ToString();
        }

        private static string Table(IEnumerable<Perf> rows)
        {
            var sb = new StringBuilder("| Group | Trades | Win % | Avg R | Total R | Profit factor | Max DD (R) | Losing streak |\n|---|---|---|---|---|---|---|---|\n");
            foreach (var p in rows)
                sb.AppendFormat(Inv, "| {0} | {1} | {2:0}% | {3:0.00} | {4:0.0} | {5:0.00} | {6:0.0} | {7} |\n", p.Key, p.Trades, p.WinRate * 100, p.AvgR, p.TotalR, p.ProfitFactor, p.MaxDrawdownR, p.MaxLosingStreak);
            return sb.ToString();
        }

        // ---------- HTML ----------
        public string Html()
        {
            var h = Series.Header; var o = Overall;
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
            sb.Append("<title>Fabio Backtest</title><style>");
            sb.Append(":root{--bg:#f7f7f5;--panel:#fff;--ink:#1d1d1b;--muted:#6b6b66;--line:#e3e2dc;--pos:#1f7a4d;--neg:#b23b3b;--accent:#2f5d8a;--warn-bg:#fff4d6;--warn-ink:#6b4b00}");
            sb.Append("@media (prefers-color-scheme:dark){:root{--bg:#141413;--panel:#1d1d1b;--ink:#ecebe6;--muted:#9b9a94;--line:#33332f;--pos:#5cc08c;--neg:#e07a7a;--accent:#7fb0e0;--warn-bg:#3a300f;--warn-ink:#f1d48a}}");
            sb.Append("*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font:15px/1.5 -apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif}");
            sb.Append("main{max-width:1040px;margin:0 auto;padding:24px 16px 48px}h1{font-size:24px;margin:0 0 4px}h2{font-size:17px;margin:28px 0 10px}");
            sb.Append(".sub{color:var(--muted);margin:0 0 16px}.warn{background:var(--warn-bg);color:var(--warn-ink);padding:10px 14px;border-radius:8px;margin:12px 0;font-weight:600}");
            sb.Append(".tiles{display:grid;grid-template-columns:repeat(auto-fit,minmax(140px,1fr));gap:10px}.tile{background:var(--panel);border:1px solid var(--line);border-radius:10px;padding:12px}");
            sb.Append(".tile b{display:block;font-size:22px;font-variant-numeric:tabular-nums}.tile span{color:var(--muted);font-size:13px}");
            sb.Append(".card{background:var(--panel);border:1px solid var(--line);border-radius:10px;padding:12px;overflow-x:auto}");
            sb.Append("table{border-collapse:collapse;width:100%;font-variant-numeric:tabular-nums;font-size:14px}th,td{padding:6px 8px;border-bottom:1px solid var(--line);text-align:right;white-space:nowrap}");
            sb.Append("th:first-child,td:first-child{text-align:left}th{color:var(--muted);font-weight:600}.pos{color:var(--pos)}.neg{color:var(--neg)}");
            sb.Append("svg{display:block;width:100%;height:auto}ul{padding-left:20px}li{margin:4px 0}</style></head><body><main>");
            sb.Append("<h1>").Append(E(Title)).Append("</h1>");
            sb.AppendFormat(Inv, "<p class=\"sub\">{0} · {1}-minute bars · {2:yyyy-MM-dd} to {3:yyyy-MM-dd} · {4} cash sessions · source {5}</p>",
                E(h.Instrument), h.Minutes, Series.First, Series.Last, Series.Sessions, E(h.Source));
            if (h.Synthetic) sb.Append("<div class=\"warn\">Synthetic data: this report only shows that the pipeline works. It says nothing about the strategy.</div>");
            sb.Append("<div class=\"tiles\">");
            Tile(sb, o.Trades.ToString(Inv), "trades");
            Tile(sb, (o.WinRate * 100).ToString("0", Inv) + "%", "win rate");
            Tile(sb, Signed(o.TotalR, "0.0") + "R", "total, after costs");
            Tile(sb, Signed(o.AvgR, "0.00") + "R", "average per trade");
            Tile(sb, o.ProfitFactor.ToString("0.00", Inv), "profit factor");
            Tile(sb, o.MaxDrawdownR.ToString("0.0", Inv) + "R", "max drawdown");
            sb.Append("</div>");

            sb.Append("<h2>Equity (cumulative R after costs)</h2><div class=\"card\">").Append(EquitySvg()).Append("</div>");
            sb.Append("<h2>By year</h2><div class=\"card\">").Append(HtmlTable(ByYear())).Append("</div>");
            sb.Append("<h2>By set-up</h2><div class=\"card\">").Append(HtmlTable(BySetup())).Append("</div>");
            sb.Append("<h2>By entry hour (New York)</h2><div class=\"card\">").Append(HtmlTable(ByHour())).Append("</div>");
            sb.Append("<h2>By exit</h2><div class=\"card\">").Append(HtmlTable(ByExit())).Append("</div>");
            if (Sweep != null)
            {
                sb.AppendFormat(Inv, "<h2>Sweep: {0} combinations · in-sample before {1:yyyy-MM-dd} · out-of-sample after</h2><div class=\"card\"><table><tr><th>Settings</th><th>IS trades</th><th>IS R</th><th>IS max DD</th><th>OOS trades</th><th>OOS R</th><th>OOS avg R</th><th>Robust</th></tr>", Sweep.Rows.Count, Sweep.Split);
                foreach (var r in Sweep.Rows.Take(15))
                    sb.AppendFormat(Inv, "<tr{8}><td>{0}</td><td>{1}</td><td class=\"{9}\">{2:0.0}</td><td>{3:0.0}</td><td>{4}</td><td class=\"{10}\">{5:0.0}</td><td>{6:0.00}</td><td>{7:0.0}</td></tr>",
                        E(r.Label), r.InSample.Trades, r.InSample.TotalR, r.InSample.MaxDrawdownR, r.OutSample.Trades, r.OutSample.TotalR, r.OutSample.AvgR, r.RobustScore,
                        r == Sweep.BestRobust ? " style=\"font-weight:700\"" : "", r.InSample.TotalR >= 0 ? "pos" : "neg", r.OutSample.TotalR >= 0 ? "pos" : "neg");
                sb.Append("</table></div>");
                if (Sweep.BestRobust != null) sb.Append("<p>Bold: the most robust in-sample setting (good, and its neighbours good too).</p>");
            }
            var changed = BarBacktest.Changed(Run.P);
            sb.Append("<h2>Settings</h2><p>").Append(changed.Count == 0 ? "All defaults." : E(string.Join(" · ", changed.Select(kv => kv.Key + " = " + kv.Value)))).Append("</p>");
            var sline = BarBacktest.SettingsLine(Run.P);
            sb.Append("<p>In ATAS, \"Engine settings from the backtest\" (indicator and strategy): ")
              .Append(sline.Length == 0 ? "leave empty (defaults)." : "<code style=\"user-select:all\">" + E(sline) + "</code>").Append("</p>");
            sb.AppendFormat(Inv, "<p class=\"sub\">Costs: {0} tick(s) slippage on entry and stops, {1} round turn per contract, point value {2}, max {3} losing trades per session, flat at 16:00.</p>",
                Run.Bp.SlippageTicks, Run.Bp.CommissionPerContract, Run.Bp.PointValue, Run.Bp.MaxLosingTradesPerSession);
            sb.Append("<h2>Read this before deciding anything</h2><ul>");
            foreach (var c in Caveats()) sb.Append("<li>").Append(E(c)).Append("</li>");
            sb.Append("</ul></main></body></html>");
            return sb.ToString();
        }

        private static void Tile(StringBuilder sb, string v, string label)
        {
            sb.Append("<div class=\"tile\"><b>").Append(E(v)).Append("</b><span>").Append(E(label)).Append("</span></div>");
        }

        private static string Signed(decimal v, string fmt) { return (v > 0 ? "+" : "") + v.ToString(fmt, Inv); }

        private string HtmlTable(IEnumerable<Perf> rows)
        {
            var sb = new StringBuilder("<table><tr><th>Group</th><th>Trades</th><th>Win %</th><th>Avg R</th><th>Total R</th><th>Profit factor</th><th>Max DD (R)</th><th>Losing streak</th></tr>");
            foreach (var p in rows)
                sb.AppendFormat(Inv, "<tr><td>{0}</td><td>{1}</td><td>{2:0}%</td><td class=\"{8}\">{3:0.00}</td><td class=\"{8}\">{4:0.0}</td><td>{5:0.00}</td><td>{6:0.0}</td><td>{7}</td></tr>",
                    E(p.Key), p.Trades, p.WinRate * 100, p.AvgR, p.TotalR, p.ProfitFactor, p.MaxDrawdownR, p.MaxLosingStreak, p.TotalR >= 0 ? "pos" : "neg");
            return sb.Append("</table>").ToString();
        }

        private string EquitySvg()
        {
            var trades = Run.Trades.OrderBy(t => t.EntryTime).ToList();
            const int W = 960, H = 260, L = 48, Rm = 12, T = 12, B = 28;
            if (trades.Count == 0) return "<p>No trades.</p>";
            var pts = new List<decimal> { 0 };
            decimal eq = 0;
            foreach (var t in trades) { eq += Perf.NetR(t, Run.Bp); pts.Add(eq); }
            decimal min = Math.Min(0, pts.Min()), max = Math.Max(0, pts.Max());
            if (max == min) max = min + 1;
            Func<int, double> x = i => L + (double)i / Math.Max(1, pts.Count - 1) * (W - L - Rm);
            Func<decimal, double> y = v => T + (double)((max - v) / (max - min)) * (H - T - B);
            var sb = new StringBuilder();
            sb.AppendFormat(Inv, "<svg viewBox=\"0 0 {0} {1}\" role=\"img\" aria-label=\"Equity curve in R\">", W, H);
            sb.AppendFormat(Inv, "<line x1=\"{0}\" x2=\"{1}\" y1=\"{2:0.0}\" y2=\"{2:0.0}\" style=\"stroke:var(--line)\"/>", L, W - Rm, y(0));
            foreach (var v in new[] { min, max }.Where(v => v != 0))
                sb.AppendFormat(Inv, "<text x=\"{0}\" y=\"{1:0.0}\" text-anchor=\"end\" style=\"fill:var(--muted);font-size:12px\">{2}R</text>", L - 6, y(v) + 4, Signed(v, "0"));
            sb.AppendFormat(Inv, "<text x=\"{0}\" y=\"{1:0.0}\" text-anchor=\"end\" style=\"fill:var(--muted);font-size:12px\">0</text>", L - 6, y(0) + 4);
            int lastYear = -1;
            for (int i = 0; i < trades.Count; i++)
            {
                int yr = trades[i].EntryTime.Year;
                if (yr == lastYear) continue;
                lastYear = yr;
                sb.AppendFormat(Inv, "<line x1=\"{0:0.0}\" x2=\"{0:0.0}\" y1=\"{1}\" y2=\"{2}\" style=\"stroke:var(--line)\"/><text x=\"{3:0.0}\" y=\"{4}\" style=\"fill:var(--muted);font-size:12px\">{5}</text>", x(i), T, H - B, x(i) + 4, H - 8, yr);
            }
            if (Sweep != null)
            {
                int si = trades.FindIndex(t => t.EntryTime.Date >= Sweep.Split.Date);
                if (si >= 0)
                    sb.AppendFormat(Inv, "<line x1=\"{0:0.0}\" x2=\"{0:0.0}\" y1=\"{1}\" y2=\"{2}\" style=\"stroke:var(--muted);stroke-dasharray:4 4\"/><text x=\"{3:0.0}\" y=\"{4}\" style=\"fill:var(--muted);font-size:12px\">out-of-sample &#8594;</text>",
                        x(si), T, H - B, x(si) + 4, T + 12);
            }
            sb.Append("<polyline fill=\"none\" style=\"stroke:var(--accent);stroke-width:2\" points=\"");
            int step = Math.Max(1, pts.Count / 2000);
            for (int i = 0; i < pts.Count; i += step) sb.AppendFormat(Inv, "{0:0.0},{1:0.0} ", x(i), y(pts[i]));
            sb.AppendFormat(Inv, "{0:0.0},{1:0.0}", x(pts.Count - 1), y(pts[pts.Count - 1]));
            sb.Append("\"/></svg>");
            return sb.ToString();
        }

        private static string E(string s) { return WebUtility.HtmlEncode(s ?? ""); }

        public void WriteAll(string dir)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "summary.json"), Json.Write(ToJson()));
            File.WriteAllText(Path.Combine(dir, "trades.csv"), TradesCsv());
            File.WriteAllText(Path.Combine(dir, "report.md"), Markdown());
            File.WriteAllText(Path.Combine(dir, "report.html"), Html());
        }
    }
}
