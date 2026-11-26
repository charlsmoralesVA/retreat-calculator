using System;
using System.Collections.Generic;
using System.Linq;

namespace FabioOrderflow.Core
{
    public sealed class ProfileStats
    {
        public decimal Poc, Vah, Val, High, Low, Total;
    }

    /// <summary>Bid/ask volume per price. Delta at a price = Ask - Bid.</summary>
    public sealed class VolumeProfile
    {
        private readonly Dictionary<decimal, decimal> _bid = new Dictionary<decimal, decimal>();
        private readonly Dictionary<decimal, decimal> _ask = new Dictionary<decimal, decimal>();

        public bool IsEmpty { get { return _bid.Count == 0; } }
        public int Levels { get { return _bid.Count; } }

        public void Add(decimal price, decimal bid, decimal ask)
        {
            decimal b, a;
            _bid.TryGetValue(price, out b); _bid[price] = b + bid;
            _ask.TryGetValue(price, out a); _ask[price] = a + ask;
        }

        public void Add(PriceLevelVol l) { Add(l.Price, l.Bid, l.Ask); }

        public void Merge(VolumeProfile other)
        {
            foreach (var kv in other._bid) Add(kv.Key, kv.Value, other._ask[kv.Key]);
        }

        public decimal VolumeAt(decimal price)
        {
            decimal b, a; _bid.TryGetValue(price, out b); _ask.TryGetValue(price, out a); return a + b;
        }

        public decimal DeltaAt(decimal price)
        {
            decimal b, a; _bid.TryGetValue(price, out b); _ask.TryGetValue(price, out a); return a - b;
        }

        public IEnumerable<decimal> Prices { get { return _bid.Keys.OrderBy(x => x); } }

        /// <summary>POC, value area (CME-style: grow by the heavier pair of ticks) and extremes.</summary>
        public ProfileStats Compute(decimal tick, decimal vaPct)
        {
            if (IsEmpty || tick <= 0) return null;
            decimal lo = _bid.Keys.Min(), hi = _bid.Keys.Max();
            int n = (int)Math.Round((hi - lo) / tick) + 1;
            if (n <= 0 || n > 500000) return null;

            var vol = new decimal[n];
            foreach (var kv in _bid)
            {
                int idx = (int)Math.Round((kv.Key - lo) / tick);
                vol[idx] += kv.Value + _ask[kv.Key];
            }

            decimal max = vol.Max();
            double mid = (n - 1) / 2.0, bestDist = double.MaxValue;
            int poc = 0;
            for (int i = 0; i < n; i++)
            {
                if (vol[i] != max) continue;
                double d = Math.Abs(i - mid);
                if (d < bestDist) { bestDist = d; poc = i; }   // ties -> nearest the middle of the profile
            }

            decimal total = vol.Sum();
            decimal target = total * vaPct;
            decimal acc = vol[poc];
            int lowI = poc, highI = poc;
            while (acc < target && (lowI > 0 || highI < n - 1))
            {
                bool canUp = highI < n - 1, canDn = lowI > 0;
                decimal up = canUp ? vol[highI + 1] + (highI + 2 < n ? vol[highI + 2] : 0) : -1;
                decimal dn = canDn ? vol[lowI - 1] + (lowI - 2 >= 0 ? vol[lowI - 2] : 0) : -1;
                if (canUp && (!canDn || up >= dn))
                {
                    for (int k = 0; k < 2 && highI < n - 1; k++) { highI++; acc += vol[highI]; }
                }
                else
                {
                    for (int k = 0; k < 2 && lowI > 0; k++) { lowI--; acc += vol[lowI]; }
                }
            }

            return new ProfileStats
            {
                Poc = lo + poc * tick,
                Vah = lo + highI * tick,
                Val = lo + lowI * tick,
                High = hi,
                Low = lo,
                Total = total
            };
        }
    }
}
