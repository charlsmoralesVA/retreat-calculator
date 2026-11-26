using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FabioOrderflow.Core
{
    public sealed class BacktestParams
    {
        public decimal PointValue = 1m;              // currency per 1.0 of price per contract (e.g. ES = 50, NQ = 20)
        public int Contracts = 1;
        public decimal SlippageTicks = 1m;           // adverse, on the market entry and on stop exits
        public decimal CommissionPerContract = 0m;   // round turn
        public decimal MaxDailyLossR = 3m;           // stop trading for the session after this much loss; 0 = off
        public bool FlattenAtSessionEnd = true;
    }

    public sealed class TradeRecord
    {
        public int SignalBar, EntryBar, ExitBar;
        public DateTime EntryTime, ExitTime;
        public Side Side;
        public string Tag;
        public decimal Entry, Stop, Target, Exit, Risk, R, PnlPoints, PnlCurrency;
        public string ExitReason;
    }

    public sealed class GroupStats { public int Trades, Wins; public decimal TotalR; }

    public sealed class BacktestStats
    {
        public int Trades, Wins, Losses, MaxLosingStreak;
        public decimal WinRate, AvgWinR, AvgLossR, ExpectancyR, TotalR, ProfitFactor, MaxDrawdownR, TotalCurrency;
        public Dictionary<string, GroupStats> ByTag = new Dictionary<string, GroupStats>();
        public Dictionary<Side, GroupStats> BySide = new Dictionary<Side, GroupStats>();
    }

    public sealed class BacktestResult
    {
        public List<TradeRecord> Trades = new List<TradeRecord>();
        public List<string> Skipped = new List<string>();
        public BacktestStats Stats;

        public string Summary()
        {
            var s = Stats; var sb = new StringBuilder();
            sb.AppendFormat("Trades {0}  (skipped signals {1})\n", s.Trades, Skipped.Count);
            if (s.Trades == 0) return sb.ToString();
            sb.AppendFormat("Win rate {0:P1}   avg win {1:0.00}R   avg loss {2:0.00}R\n", s.WinRate, s.AvgWinR, s.AvgLossR);
            sb.AppendFormat("Expectancy {0:0.000}R per trade   total {1:0.00}R   profit factor {2}\n", s.ExpectancyR, s.TotalR,
                s.ProfitFactor == decimal.MaxValue ? "inf" : s.ProfitFactor.ToString("0.00"));
            sb.AppendFormat("Max drawdown {0:0.00}R   longest losing streak {1}   P&L {2:0.00}\n", s.MaxDrawdownR, s.MaxLosingStreak, s.TotalCurrency);
            foreach (var kv in s.BySide) sb.AppendFormat("  {0,-6} {1} trades  {2:0.00}R  win {3:P0}\n", kv.Key, kv.Value.Trades, kv.Value.TotalR, kv.Value.Trades == 0 ? 0m : (decimal)kv.Value.Wins / kv.Value.Trades);
            foreach (var kv in s.ByTag) sb.AppendFormat("  {0,-28} {1} trades  {2:0.00}R  win {3:P0}\n", kv.Key, kv.Value.Trades, kv.Value.TotalR, kv.Value.Trades == 0 ? 0m : (decimal)kv.Value.Wins / kv.Value.Trades);
            var reasons = Skipped.GroupBy(x => x).Select(g => g.Key + " x" + g.Count());
            if (Skipped.Count > 0) sb.AppendLine("Skipped: " + string.Join(", ", reasons));
            return sb.ToString();
        }
    }

    /// <summary>
    /// Bar-level simulation of the signals, deliberately pessimistic:
    ///  * entry = NEXT bar's open plus slippage (the signal exists only once the confirming bar has closed)
    ///  * a bar that touches both stop and target counts as a stop
    ///  * stop exits pay slippage; a gap through the stop fills at the open
    ///  * one position at a time, flat at the end of the session, daily loss cap in R
    /// It cannot know intrabar order, so it is a calibration aid, not proof of an edge.
    /// </summary>
    public static class Backtester
    {
        public static BacktestResult Simulate(IList<Bar> bars, IEnumerable<SetupEvaluation> signals, FabioParams p, BacktestParams bp)
        {
            var clock = new SessionClock(p.RthStart, p.RthEnd);
            var res = new BacktestResult();
            int busyUntil = -1;
            var dayR = new Dictionary<DateTime, decimal>();
            decimal slip = bp.SlippageTicks * p.TickSize;

            foreach (var sig in signals.OrderBy(s => s.ConfirmGlobalIndex))
            {
                int k = sig.ConfirmGlobalIndex, e = k + 1;
                if (k < 0 || e >= bars.Count) { res.Skipped.Add("no next bar"); continue; }
                if (e <= busyUntil) { res.Skipped.Add("already in a trade"); continue; }
                DateTime day = bars[k].Time.Date;
                if (!clock.IsRth(bars[e].Time) || bars[e].Time.Date != day) { res.Skipped.Add("next bar outside the session"); continue; }
                decimal soFar; dayR.TryGetValue(day, out soFar);
                if (bp.MaxDailyLossR > 0 && soFar <= -bp.MaxDailyLossR) { res.Skipped.Add("daily loss limit"); continue; }

                int dir = sig.Side == Side.Long ? 1 : -1;
                decimal fill = bars[e].Open + dir * slip;
                decimal risk = (fill - sig.Stop) * dir;
                if (risk <= 0) { res.Skipped.Add("gapped through the stop"); continue; }
                if ((sig.Target - fill) * dir <= 0) { res.Skipped.Add("gapped through the target"); continue; }

                int j = e; decimal exit = 0; string reason = null;
                for (; j < bars.Count; j++)
                {
                    var b = bars[j];
                    if (j > e)
                    {
                        if ((b.Open - sig.Stop) * dir <= 0) { exit = b.Open; reason = "stop (gap)"; break; }
                        if ((b.Open - sig.Target) * dir >= 0) { exit = b.Open; reason = "target (gap)"; break; }
                    }
                    bool hitStop = dir == 1 ? b.Low <= sig.Stop : b.High >= sig.Stop;
                    bool hitTarget = dir == 1 ? b.High >= sig.Target : b.Low <= sig.Target;
                    if (hitStop) { exit = sig.Stop - dir * slip; reason = "stop"; break; }          // stop wins a same-bar tie
                    if (hitTarget) { exit = sig.Target; reason = "target"; break; }
                    bool lastOfSession = j == bars.Count - 1 || !clock.IsRth(bars[j + 1].Time) || bars[j + 1].Time.Date != day;
                    if (lastOfSession && bp.FlattenAtSessionEnd) { exit = b.Close; reason = "session end"; break; }
                }
                if (reason == null) { j = bars.Count - 1; exit = bars[j].Close; reason = "end of data"; }

                decimal pts = (exit - fill) * dir;
                var t = new TradeRecord
                {
                    SignalBar = k, EntryBar = e, ExitBar = j, EntryTime = bars[e].Time, ExitTime = bars[j].Time,
                    Side = sig.Side, Tag = sig.Tag, Entry = fill, Stop = sig.Stop, Target = sig.Target, Exit = exit,
                    Risk = risk, R = pts / risk, PnlPoints = pts,
                    PnlCurrency = pts * bp.PointValue * bp.Contracts - bp.CommissionPerContract * bp.Contracts,
                    ExitReason = reason
                };
                res.Trades.Add(t);
                dayR[day] = soFar + t.R;
                busyUntil = j;
            }
            res.Stats = Summarize(res.Trades);
            return res;
        }

        public static BacktestStats Summarize(IList<TradeRecord> trades)
        {
            var s = new BacktestStats { Trades = trades.Count };
            if (trades.Count == 0) return s;
            decimal winSum = 0, lossSum = 0, cum = 0, peak = 0;
            int streak = 0;
            foreach (var t in trades)
            {
                if (t.R > 0) { s.Wins++; winSum += t.R; streak = 0; }
                else { s.Losses++; lossSum += t.R; streak++; s.MaxLosingStreak = Math.Max(s.MaxLosingStreak, streak); }
                cum += t.R; peak = Math.Max(peak, cum); s.MaxDrawdownR = Math.Max(s.MaxDrawdownR, peak - cum);
                s.TotalCurrency += t.PnlCurrency;

                GroupStats g;
                if (!s.ByTag.TryGetValue(t.Tag ?? "?", out g)) { g = new GroupStats(); s.ByTag[t.Tag ?? "?"] = g; }
                g.Trades++; g.TotalR += t.R; if (t.R > 0) g.Wins++;
                if (!s.BySide.TryGetValue(t.Side, out g)) { g = new GroupStats(); s.BySide[t.Side] = g; }
                g.Trades++; g.TotalR += t.R; if (t.R > 0) g.Wins++;
            }
            s.TotalR = cum;
            s.WinRate = (decimal)s.Wins / s.Trades;
            s.AvgWinR = s.Wins == 0 ? 0 : winSum / s.Wins;
            s.AvgLossR = s.Losses == 0 ? 0 : lossSum / s.Losses;
            s.ExpectancyR = cum / s.Trades;
            s.ProfitFactor = lossSum == 0 ? decimal.MaxValue : winSum / Math.Abs(lossSum);
            return s;
        }
    }
}
