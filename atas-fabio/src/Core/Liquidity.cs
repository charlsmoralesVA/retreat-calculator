using System;
using System.Collections.Generic;
using System.Linq;

namespace FabioOrderflow.Core
{
    public sealed class LiquidityWall
    {
        public decimal Price, Size;
        public decimal Strength;          // size / average resting size per price within the range
        public int DistanceTicks;         // from the candle's close; negative = below
        public bool IsAsk;                // resting offer (sellers) or bid (buyers)
    }

    /// <summary>
    /// The order book (what ATAS's heatmap draws) around price at a candle's close: the biggest resting bids and offers,
    /// the totals and their imbalance, and what happened to the walls of the previous candle: pulled (gone while price
    /// never traded there) or traded through. Resting orders can be cancelled at will; Fabio's confirmation rests on
    /// executed orders. This is context for Laya, never a trade rule (Assumed: the book is not in SKILL.md).
    /// </summary>
    public sealed class LiquiditySnapshot
    {
        public decimal Price;
        public int RangeTicks;
        public List<LiquidityWall> BidWalls = new List<LiquidityWall>();
        public List<LiquidityWall> AskWalls = new List<LiquidityWall>();
        public decimal BidTotal, AskTotal;
        public decimal Imbalance;          // (bids - asks) / (bids + asks) within the range, -1..+1
        public List<LiquidityWall> Pulled = new List<LiquidityWall>();
        public List<LiquidityWall> TradedThrough = new List<LiquidityWall>();

        public Dictionary<string, object> ToJson()
        {
            Func<LiquidityWall, object> w = x => new Dictionary<string, object>
            {
                { "price", x.Price }, { "size", x.Size }, { "x_avg", decimal.Round(x.Strength, 1) }, { "ticks_away", x.DistanceTicks },
                { "side", x.IsAsk ? "offer" : "bid" }
            };
            return new Dictionary<string, object>
            {
                { "range_ticks", RangeTicks }, { "bid_walls", BidWalls.Select(w).ToList() }, { "ask_walls", AskWalls.Select(w).ToList() },
                { "bids_total", BidTotal }, { "asks_total", AskTotal }, { "imbalance", decimal.Round(Imbalance, 2) },
                { "pulled_since_last_candle", Pulled.Select(w).ToList() }, { "traded_through_since_last_candle", TradedThrough.Select(w).ToList() }
            };
        }
    }

    /// <summary>Live order book fed by ATAS's market-depth updates. Thread-safe: updates arrive on the data thread.</summary>
    public sealed class LiquidityBook
    {
        private readonly object _lock = new object();
        private readonly Dictionary<decimal, decimal> _bids = new Dictionary<decimal, decimal>();
        private readonly Dictionary<decimal, decimal> _asks = new Dictionary<decimal, decimal>();
        private List<LiquidityWall> _lastWalls = new List<LiquidityWall>();
        public bool HasData { get; private set; }

        public void Clear()
        {
            lock (_lock) { _bids.Clear(); _asks.Clear(); HasData = false; _lastWalls = new List<LiquidityWall>(); }
        }

        /// <summary>One price level of the book; volume 0 removes it.</summary>
        public void Set(decimal price, decimal volume, bool isAsk)
        {
            lock (_lock)
            {
                var d = isAsk ? _asks : _bids;
                if (volume <= 0) d.Remove(price); else d[price] = volume;
                HasData = true;
            }
        }

        public decimal SizeAt(decimal price, bool isAsk)
        {
            lock (_lock) { decimal v; return (isAsk ? _asks : _bids).TryGetValue(price, out v) ? v : 0; }
        }

        /// <summary>
        /// Snapshot at a candle's close. candleLow / candleHigh: the candle's range, to tell a pulled wall (price never got
        /// there) from one that was traded through. Walls = the largest levels at least 2x the average size in range.
        /// </summary>
        public LiquiditySnapshot Snapshot(decimal price, decimal tick, int rangeTicks, int maxWalls, decimal candleLow, decimal candleHigh)
        {
            if (tick <= 0) tick = 0.25m;
            lock (_lock)
            {
                var s = new LiquiditySnapshot { Price = price, RangeTicks = rangeTicks };
                decimal lo = price - rangeTicks * tick, hi = price + rangeTicks * tick;
                var bids = _bids.Where(kv => kv.Key >= lo && kv.Key <= hi).ToList();
                var asks = _asks.Where(kv => kv.Key >= lo && kv.Key <= hi).ToList();
                s.BidTotal = bids.Sum(kv => kv.Value); s.AskTotal = asks.Sum(kv => kv.Value);
                decimal tot = s.BidTotal + s.AskTotal;
                s.Imbalance = tot > 0 ? (s.BidTotal - s.AskTotal) / tot : 0;
                int n = bids.Count + asks.Count;
                decimal avg = n > 0 ? tot / n : 0;
                Func<KeyValuePair<decimal, decimal>, bool, LiquidityWall> mk = (kv, ask) => new LiquidityWall
                {
                    Price = kv.Key, Size = kv.Value, Strength = avg > 0 ? kv.Value / avg : 0, DistanceTicks = (int)Math.Round((kv.Key - price) / tick), IsAsk = ask
                };
                s.BidWalls = bids.Where(kv => avg > 0 && kv.Value >= 2 * avg).OrderByDescending(kv => kv.Value).Take(maxWalls).Select(kv => mk(kv, false)).ToList();
                s.AskWalls = asks.Where(kv => avg > 0 && kv.Value >= 2 * avg).OrderByDescending(kv => kv.Value).Take(maxWalls).Select(kv => mk(kv, true)).ToList();

                foreach (var w in _lastWalls)
                {
                    decimal now; (w.IsAsk ? _asks : _bids).TryGetValue(w.Price, out now);
                    if (now >= w.Size / 2) continue;                               // still there
                    var moved = new LiquidityWall { Price = w.Price, Size = w.Size, Strength = w.Strength, DistanceTicks = (int)Math.Round((w.Price - price) / tick), IsAsk = w.IsAsk };
                    if (w.Price >= candleLow && w.Price <= candleHigh) s.TradedThrough.Add(moved); else s.Pulled.Add(moved);
                }
                _lastWalls = s.BidWalls.Concat(s.AskWalls).ToList();
                return s;
            }
        }
    }
}
