using System;

namespace FabioOrderflow.Core
{
    /// <summary>
    /// SKILL.md: filter big trades on the 5-minute chart using ~60-75 contracts; allow no more than ~3 big
    /// trades per candle, and if there are more, scale the contract filter up.
    /// The scale-up factor and the relax rule are mine; the 60 / 3 numbers are stated.
    /// </summary>
    public sealed class BigTradeFilter
    {
        private readonly FabioParams _p;
        private readonly int _baseMin;
        public int Threshold { get; private set; }

        public BigTradeFilter(FabioParams p)
        {
            _p = p;
            _baseMin = p.BigTradeMin;
            Threshold = p.BigTradeMin;
        }

        public bool IsBig(decimal contracts) { return contracts >= Threshold; }

        /// <summary>Call once per closed bar with the number of big prints seen on either side.</summary>
        public void Adapt(int bigPrintsInBar)
        {
            if (bigPrintsInBar > _p.MaxBigTradesPerBar)
                Threshold = (int)Math.Ceiling(Threshold * _p.BigTradeScaleUp);
            else if (bigPrintsInBar == 0 && Threshold > _baseMin)
                Threshold = Math.Max(_baseMin, (int)Math.Floor(Threshold / _p.BigTradeScaleUp));
        }

        public void Reset() { Threshold = _baseMin; }
    }
}
