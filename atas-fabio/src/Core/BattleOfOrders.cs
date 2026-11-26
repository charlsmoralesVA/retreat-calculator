using System;
using System.Collections.Generic;

namespace FabioOrderflow.Core
{
    public sealed class BarStats
    {
        public decimal AvgAbsDelta, AvgRange, AvgVolume;
        public int Count;

        /// <summary>Averages over the bars BEFORE endExclusive (so the bar being judged never judges itself).</summary>
        public static BarStats Rolling(IReadOnlyList<Bar> bars, int endExclusive, int window)
        {
            var s = new BarStats();
            int start = Math.Max(0, endExclusive - window);
            for (int k = start; k < endExclusive; k++)
            {
                s.AvgAbsDelta += Math.Abs(bars[k].Delta);
                s.AvgRange += bars[k].Range;
                s.AvgVolume += bars[k].Volume;
                s.Count++;
            }
            if (s.Count > 0) { s.AvgAbsDelta /= s.Count; s.AvgRange /= s.Count; s.AvgVolume /= s.Count; }
            return s;
        }
    }

    public sealed class ApproachResult
    {
        public ApproachState State = ApproachState.Unknown;
        public decimal Efficiency;   // progress per unit of effort, 1.0 = typical
    }

    /// <summary>
    /// Pillar 3 - confirmation (SKILL.md): at the zone read the battle of orders on executed data. The
    /// universal trigger is "one side loses, the other wins": aggressive participants on one side get
    /// ABSORBED (lots of effort, no result), then the other side enters aggressively in the BODY of the
    /// candle and gets results (the candle closes their way).
    /// The reactor principle: judge HOW price arrived (effort vs result on the approach).
    /// All thresholds are relative to recent bars so they work on any instrument, but the multipliers are mine.
    /// </summary>
    public static class BattleOfOrders
    {
        public static bool Touches(Bar b, Side side, decimal zone, decimal tol)
        {
            if (side == Side.Long) return b.Low <= zone + tol && b.Low >= zone - tol;
            if (side == Side.Short) return b.High >= zone - tol && b.High <= zone + tol;
            return false;
        }

        /// <summary>The side being faded shows heavy effort at the zone but price made no progress for it.</summary>
        public static bool IsAbsorption(Bar a, Side side, decimal zone, BarStats s, FabioParams p)
        {
            decimal tol = p.ZoneToleranceTicks * p.TickSize;
            if (!Touches(a, side, zone, tol)) return false;
            decimal range = Math.Max(a.Range, p.TickSize);
            if (side == Side.Long)
            {
                bool effort = a.Delta <= -p.AbsorbDeltaMult * s.AvgAbsDelta
                              || (p.EffortBigPrints > 0 && a.BigSells >= p.EffortBigPrints);
                decimal progress = (a.Open - a.Close) / range;     // sellers' result
                return effort && progress <= p.MaxAbsorbProgress;
            }
            else
            {
                bool effort = a.Delta >= p.AbsorbDeltaMult * s.AvgAbsDelta
                              || (p.EffortBigPrints > 0 && a.BigBuys >= p.EffortBigPrints);
                decimal progress = (a.Close - a.Open) / range;     // buyers' result
                return effort && progress <= p.MaxAbsorbProgress;
            }
        }

        /// <summary>The other side wins: strong body closing their way with aligned delta.</summary>
        public static bool IsConfirmation(Bar c, Bar a, Side side, BarStats s, FabioParams p)
        {
            decimal range = Math.Max(c.Range, p.TickSize);
            if (side == Side.Long)
            {
                return c.Close > c.Open
                    && (c.Close - c.Open) / range >= p.MinBodyFraction
                    && c.Delta >= p.ConfirmDeltaMult * s.AvgAbsDelta
                    && (!p.RequireBreakAbsorbExtreme || c.Close > a.High);
            }
            return c.Close < c.Open
                && (c.Open - c.Close) / range >= p.MinBodyFraction
                && c.Delta <= -p.ConfirmDeltaMult * s.AvgAbsDelta
                && (!p.RequireBreakAbsorbExtreme || c.Close < a.Low);
        }

        /// <summary>
        /// Effort vs result on the approach into the zone. Long setup = price FELL into support, so the effort
        /// is sellers' net aggression and the result is the distance dropped. Struggle (progress per effort is
        /// low) favours the fade; an effortless explosion into the level does not.
        /// </summary>
        public static ApproachResult MeasureApproach(IReadOnlyList<Bar> bars, int minIndex, int endExclusive, Side side, BarStats s, FabioParams p)
        {
            var r = new ApproachResult();
            int n = Math.Min(p.ApproachBars, endExclusive - minIndex);
            if (n < 2 || s.Count < p.MinStatsBars || s.AvgRange <= 0 || s.AvgAbsDelta <= 0) return r;

            int start = endExclusive - n;
            decimal startOpen = bars[start].Open, progress, netDelta = 0;
            if (side == Side.Long)
            {
                decimal lo = decimal.MaxValue;
                for (int k = start; k < endExclusive; k++) { lo = Math.Min(lo, bars[k].Low); netDelta += bars[k].Delta; }
                progress = Math.Max(0, startOpen - lo);
            }
            else
            {
                decimal hi = decimal.MinValue;
                for (int k = start; k < endExclusive; k++) { hi = Math.Max(hi, bars[k].High); netDelta += bars[k].Delta; }
                progress = Math.Max(0, hi - startOpen);
            }

            decimal effort = side == Side.Long ? -netDelta : netDelta;
            if (effort < 0) effort = 0;
            decimal moveRatio = progress / (n * s.AvgRange);
            decimal effortRatio = Math.Max(effort / (n * s.AvgAbsDelta), 0.25m);
            r.Efficiency = moveRatio / effortRatio;
            r.State = r.Efficiency <= p.StruggleEff ? ApproachState.Struggle
                    : r.Efficiency >= p.ExplosiveEff ? ApproachState.Effortless
                    : ApproachState.Normal;
            return r;
        }
    }
}
