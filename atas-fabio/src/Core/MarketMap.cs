using System;
using System.Collections.Generic;
using System.Linq;

namespace FabioOrderflow.Core
{
    public sealed class LevelSet
    {
        public bool Valid;
        public decimal Poc, Vah, Val, High, Low;
        public Dictionary<string, object> ToJson()
        {
            if (!Valid) return null;
            return new Dictionary<string, object> { { "poc", Poc }, { "vah", Vah }, { "val", Val }, { "high", High }, { "low", Low } };
        }
    }

    public sealed class VolumeNode
    {
        public decimal Price;
        public string Kind;            // "HVN" or "LVN"
        public decimal Strength;       // smoothed volume at the node / average smoothed volume
    }

    public sealed class SwingLevel
    {
        public string Direction;           // "up": low came first; "down": high came first
        public DateTime From, To;
        public decimal Low, High, Price, Delta;
    }

    public sealed class SessionValue
    {
        public DateTime Date;
        public decimal Poc, Vah, Val, High, Low;
        public string ShiftVsPrevious = "";
    }

    /// <summary>
    /// Everything Fabio's method looks at besides the candle itself, measured by the engine and handed to Laya:
    /// developing (today's) value, the value ladder of the last sessions, the 3-month composition, volume nodes,
    /// session VWAP with deviation bands, session cumulative delta, delta levels, initial balance, overnight and
    /// London levels, the gap. Reference levels only: the trade rules stay in SetupEvaluator / with Laya.
    /// </summary>
    public sealed class MarketMap
    {
        public LevelSet Developing = new LevelSet();
        public LevelSet Overnight = new LevelSet();
        public decimal LondonHigh, LondonLow;
        public bool HasLondon;
        public decimal Vwap, VwapSd, SessionCvd;
        public List<VolumeNode> PriorNodes = new List<VolumeNode>();
        public List<VolumeNode> DevelopingNodes = new List<VolumeNode>();
        public List<SessionValue> ValueHistory = new List<SessionValue>();
        public List<Zone> DeltaLevels = new List<Zone>();
        public decimal IbHigh, IbLow; public bool IbComplete; public string IbBreakout = "";
        public SwingLevel Swing;           // delta (sniper) level of the last swing leg of the session

        public Dictionary<string, object> ToJson()
        {
            var d = new Dictionary<string, object>();
            d["developing_value"] = Developing.ToJson();
            d["value_history"] = ValueHistory.Select(v => (object)new Dictionary<string, object>
            {
                { "session", v.Date.ToString("yyyy-MM-dd") }, { "poc", v.Poc }, { "vah", v.Vah }, { "val", v.Val },
                { "high", v.High }, { "low", v.Low }, { "value_vs_previous", v.ShiftVsPrevious }
            }).ToList();
            if (Vwap > 0)
                d["vwap"] = new Dictionary<string, object>
                {
                    { "vwap", R(Vwap) }, { "sd", R(VwapSd) },
                    { "upper_1sd", R(Vwap + VwapSd) }, { "lower_1sd", R(Vwap - VwapSd) },
                    { "upper_2sd", R(Vwap + 2 * VwapSd) }, { "lower_2sd", R(Vwap - 2 * VwapSd) }
                };
            d["session_cumulative_delta"] = R(SessionCvd);
            d["volume_nodes_prior_session"] = Nodes(PriorNodes);
            d["volume_nodes_today"] = Nodes(DevelopingNodes);
            d["delta_levels_today"] = DeltaLevels.Select(z => (object)new Dictionary<string, object> { { "price", z.Price }, { "trade_side", z.Candidate.ToString() } }).ToList();
            if (IbHigh > 0)
                d["initial_balance"] = new Dictionary<string, object>
                {
                    { "high", IbHigh }, { "low", IbLow }, { "mid", R((IbHigh + IbLow) / 2) }, { "range", IbHigh - IbLow },
                    { "complete", IbComplete }, { "breakout", IbBreakout }
                };
            if (Swing != null)
                d["swing_delta_level"] = new Dictionary<string, object>
                {
                    { "swing", Swing.Direction }, { "from", Swing.From }, { "to", Swing.To },
                    { "swing_low", Swing.Low }, { "swing_high", Swing.High },
                    { "price", Swing.Price }, { "delta_at_price", R(Swing.Delta) }, { "trade_side", Swing.Direction == "up" ? "Long" : "Short" }
                };
            d["overnight"] = Overnight.ToJson();
            d["london"] = HasLondon ? new Dictionary<string, object> { { "high", LondonHigh }, { "low", LondonLow } } : null;
            return d;
        }

        private static List<object> Nodes(List<VolumeNode> n)
        {
            return n.Select(x => (object)new Dictionary<string, object> { { "price", x.Price }, { "kind", x.Kind }, { "strength", R(x.Strength) } }).ToList();
        }

        private static decimal R(decimal v) { return decimal.Round(v, 2); }

        public static LevelSet Levels(VolumeProfile p, decimal tick, decimal vaPct)
        {
            var s = p == null ? null : p.Compute(tick, vaPct);
            if (s == null) return new LevelSet();
            return new LevelSet { Valid = true, Poc = s.Poc, Vah = s.Vah, Val = s.Val, High = s.High, Low = s.Low };
        }

        /// <summary>
        /// High and low volume nodes: peaks and troughs of the volume histogram after smoothing over a few ticks.
        /// HVN = local peak at least 1.25x the average; LVN = local trough at most 0.6x the average, lying between two
        /// heavier areas. The skill names HVN/LVN; these thresholds are mine.
        /// </summary>
        public static List<VolumeNode> FindNodes(VolumeProfile p, decimal tick, int smoothTicks = 2, int maxEach = 3)
        {
            var res = new List<VolumeNode>();
            if (p == null || p.IsEmpty || tick <= 0) return res;
            var prices = p.Prices.ToList();
            decimal lo = prices.First(), hi = prices.Last();
            int n = (int)Math.Round((hi - lo) / tick) + 1;
            if (n < 2 * smoothTicks + 3 || n > 200000) return res;
            var v = new decimal[n];
            foreach (var px in prices) v[(int)Math.Round((px - lo) / tick)] += p.VolumeAt(px);
            var sm = new decimal[n];
            for (int i = 0; i < n; i++)
            {
                decimal sum = 0; int cnt = 0;
                for (int j = Math.Max(0, i - smoothTicks); j <= Math.Min(n - 1, i + smoothTicks); j++) { sum += v[j]; cnt++; }
                sm[i] = sum / cnt;
            }
            decimal avg = sm.Average();
            if (avg <= 0) return res;
            int r = Math.Max(1, smoothTicks);
            var peaks = new List<VolumeNode>(); var troughs = new List<VolumeNode>();
            for (int i = r; i < n - r; i++)
            {
                bool isMax = true, isMin = true;
                for (int j = i - r; j <= i + r; j++)
                {
                    if (j == i) continue;
                    if (sm[j] > sm[i]) isMax = false;
                    if (sm[j] < sm[i]) isMin = false;
                }
                if (isMax && sm[i] >= 1.25m * avg) peaks.Add(new VolumeNode { Price = lo + i * tick, Kind = "HVN", Strength = sm[i] / avg });
                if (isMin && sm[i] <= 0.6m * avg)
                {
                    bool heavierBelow = false, heavierAbove = false;
                    for (int j = 0; j < i; j++) if (sm[j] >= avg) { heavierBelow = true; break; }
                    for (int j = i + 1; j < n; j++) if (sm[j] >= avg) { heavierAbove = true; break; }
                    if (heavierBelow && heavierAbove) troughs.Add(new VolumeNode { Price = lo + i * tick, Kind = "LVN", Strength = sm[i] / avg });
                }
            }
            res.AddRange(Dedupe(peaks.OrderByDescending(x => x.Strength), tick * (2 * r + 1)).Take(maxEach));
            res.AddRange(Dedupe(troughs.OrderBy(x => x.Strength), tick * (2 * r + 1)).Take(maxEach));
            return res;
        }

        /// <summary>
        /// SKILL.md: refine a zone into a delta / sniper level by dragging a volume profile over the relevant swing and
        /// reading where the most significant aggression occurred; in a swing the most significant level counts even
        /// inside value. Here the "relevant swing" is the session's last leg between its high and its low (whichever came
        /// first to whichever came last), and "most significant" is the largest |delta| at one price. Both are my reading.
        /// </summary>
        public static SwingLevel SwingDeltaLevel(IReadOnlyList<Bar> bars, IReadOnlyList<PriceLevelVol[]> levels, int first, int last)
        {
            if (last - first < 2) return null;
            int iH = first, iL = first;
            for (int i = first; i <= last; i++) { if (bars[i].High >= bars[iH].High) iH = i; if (bars[i].Low <= bars[iL].Low) iL = i; }
            int a = Math.Min(iH, iL), b = Math.Max(iH, iL);
            if (b - a < 1) return null;
            var prof = new VolumeProfile();
            for (int i = a; i <= b; i++) if (levels[i] != null) foreach (var l in levels[i]) prof.Add(l);
            if (prof.IsEmpty) return null;
            decimal best = 0, bestPx = 0;
            foreach (var px in prof.Prices) { var d = prof.DeltaAt(px); if (Math.Abs(d) > Math.Abs(best)) { best = d; bestPx = px; } }
            if (best == 0) return null;
            return new SwingLevel
            {
                Direction = iL < iH ? "up" : "down", From = bars[a].Time, To = bars[b].Time,
                Low = bars[iL].Low, High = bars[iH].High, Price = bestPx, Delta = best
            };
        }

        private static IEnumerable<VolumeNode> Dedupe(IEnumerable<VolumeNode> ordered, decimal minGap)
        {
            var kept = new List<VolumeNode>();
            foreach (var x in ordered) if (kept.All(k => Math.Abs(k.Price - x.Price) >= minGap)) { kept.Add(x); yield return x; }
        }
    }
}
