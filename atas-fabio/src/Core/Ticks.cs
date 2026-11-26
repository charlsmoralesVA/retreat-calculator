using System;
using System.Collections.Generic;

namespace FabioOrderflow.Core
{
    /// <summary>One executed trade. Aggressor: Long = buyer lifted the offer, Short = seller hit the bid, None = unknown.</summary>
    public sealed class Tick
    {
        public DateTime Time;
        public decimal Price, Volume;
        public Side Aggressor;
    }

    public sealed class AggBar
    {
        public Bar Bar;
        public PriceLevelVol[] Levels;
    }

    /// <summary>
    /// Builds time bars with footprint levels (bid/ask volume per price), delta and big-trade counts from raw trades.
    /// This is what lets the engine run on real data outside ATAS.
    ///  * Unknown aggressor: tick rule (up-tick = buy, down-tick = sell, unchanged = previous side); if still unknown the
    ///    volume is split evenly (delta-neutral).
    ///  * Big trades: a single record at or above the filter (SKILL.md: ~60-75 contracts on the 5-minute chart, no more than
    ///    ~3 per candle, otherwise the filter scales up). Feeds that split one order into many fills will under-count.
    ///  * Bars only exist for windows that contain trades.
    /// </summary>
    public sealed class TickAggregator
    {
        private readonly FabioParams _p;
        private readonly int _minutes;
        public readonly BigTradeFilter Big;

        private bool _open;
        private DateTime _start;
        private decimal _o, _h, _l, _c, _vol, _delta;
        private int _bigBuy, _bigSell;
        private readonly SortedDictionary<decimal, decimal[]> _lv = new SortedDictionary<decimal, decimal[]>();
        private decimal _lastPrice;
        private bool _hasLast;
        private Side _lastSide = Side.None;

        public TickAggregator(FabioParams p, int barMinutes)
        {
            if (barMinutes <= 0) throw new ArgumentException("barMinutes must be positive");
            _p = p; _minutes = barMinutes; Big = new BigTradeFilter(p);
        }

        public DateTime WindowStart(DateTime t)
        {
            int m = (int)(t.TimeOfDay.TotalMinutes / _minutes) * _minutes;
            return t.Date.AddMinutes(m);
        }

        /// <summary>Returns the bar(s) completed by this tick (zero or one).</summary>
        public List<AggBar> Add(Tick t)
        {
            var done = new List<AggBar>();
            var ws = WindowStart(t.Time);
            if (_open && ws > _start) done.Add(CloseBar());
            if (!_open) Begin(ws);
            Apply(t);                       // an out-of-order tick is folded into the current bar
            return done;
        }

        public List<AggBar> Flush()
        {
            var done = new List<AggBar>();
            if (_open) done.Add(CloseBar());
            return done;
        }

        private decimal Round(decimal price) { return Math.Round(price / _p.TickSize) * _p.TickSize; }

        private void Begin(DateTime ws)
        {
            _open = true; _start = ws; _vol = 0; _delta = 0; _bigBuy = 0; _bigSell = 0; _lv.Clear();
            _o = decimal.MinValue;
        }

        private void Apply(Tick t)
        {
            decimal px = Round(t.Price);
            Side side = t.Aggressor;
            if (side == Side.None)
            {
                if (_hasLast && px > _lastPrice) side = Side.Long;
                else if (_hasLast && px < _lastPrice) side = Side.Short;
                else side = _lastSide;
            }
            if (t.Aggressor != Side.None || side != Side.None) _lastSide = side;
            _lastPrice = px; _hasLast = true;

            if (_o == decimal.MinValue) { _o = px; _h = px; _l = px; }
            _h = Math.Max(_h, px); _l = Math.Min(_l, px); _c = px;

            decimal[] lv;
            if (!_lv.TryGetValue(px, out lv)) { lv = new decimal[2]; _lv[px] = lv; }   // [0] = bid (sells), [1] = ask (buys)
            if (side == Side.Long) { lv[1] += t.Volume; _delta += t.Volume; }
            else if (side == Side.Short) { lv[0] += t.Volume; _delta -= t.Volume; }
            else { lv[0] += t.Volume / 2; lv[1] += t.Volume / 2; }
            _vol += t.Volume;

            if (Big.IsBig(t.Volume))
            {
                if (side == Side.Long) _bigBuy++;
                else if (side == Side.Short) _bigSell++;
            }
        }

        private AggBar CloseBar()
        {
            var levels = new PriceLevelVol[_lv.Count];
            int i = 0;
            foreach (var kv in _lv) levels[i++] = new PriceLevelVol(kv.Key, kv.Value[0], kv.Value[1]);
            var bar = new Bar
            {
                Time = _start, Open = _o, High = _h, Low = _l, Close = _c,
                Volume = _vol, Delta = _delta, BigBuys = _bigBuy, BigSells = _bigSell
            };
            Big.Adapt(_bigBuy + _bigSell);
            _open = false;
            return new AggBar { Bar = bar, Levels = levels };
        }
    }
}
