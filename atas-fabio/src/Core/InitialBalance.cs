using System;
using System.Collections.Generic;

namespace FabioOrderflow.Core
{
    public sealed class IbInfo
    {
        public bool Started, Complete;
        public decimal High, Low;
        public Side Breakout = Side.None;   // first close outside the IB after it completes
        public int BreakIndex = -1;
    }

    public sealed class GapInfo
    {
        public decimal PriorClose, Open, Gap;
        public bool Filled;
    }

    /// <summary>
    /// Information-only helpers for two models that SKILL.md names but whose rules live in the reference files:
    ///  * IVB / initial-balance (opening-range) breakout (Model 1 profile-framed, Model 2 orderflow-confirmed,
    ///    statistical protection level / TP)
    ///  * gap model (gap fills)
    /// Here: the IB range, the breakout state and the gap/fill status. Trade rules are NOT guessed.
    /// </summary>
    public static class InitialBalance
    {
        /// <summary>sessionBars = this session's RTH bars in order. IB = bars opening within ibMinutes of the session start.</summary>
        public static IbInfo Compute(IReadOnlyList<Bar> sessionBars, TimeSpan sessionStart, int ibMinutes)
        {
            var ib = new IbInfo();
            if (sessionBars.Count == 0) return ib;
            TimeSpan end = sessionStart + TimeSpan.FromMinutes(ibMinutes);
            decimal hi = decimal.MinValue, lo = decimal.MaxValue;
            int lastIb = -1;
            for (int k = 0; k < sessionBars.Count; k++)
            {
                if (sessionBars[k].Time.TimeOfDay >= end) break;
                hi = Math.Max(hi, sessionBars[k].High);
                lo = Math.Min(lo, sessionBars[k].Low);
                lastIb = k;
            }
            if (lastIb < 0) return ib;
            ib.Started = true; ib.High = hi; ib.Low = lo;
            ib.Complete = lastIb < sessionBars.Count - 1 || sessionBars[sessionBars.Count - 1].Time.TimeOfDay >= end;
            if (!ib.Complete) return ib;
            for (int k = lastIb + 1; k < sessionBars.Count; k++)
            {
                if (sessionBars[k].Close > hi) { ib.Breakout = Side.Long; ib.BreakIndex = k; break; }
                if (sessionBars[k].Close < lo) { ib.Breakout = Side.Short; ib.BreakIndex = k; break; }
            }
            return ib;
        }

        public static GapInfo ComputeGap(decimal priorClose, IReadOnlyList<Bar> sessionBars)
        {
            if (sessionBars.Count == 0) return null;
            var g = new GapInfo { PriorClose = priorClose, Open = sessionBars[0].Open };
            g.Gap = g.Open - priorClose;
            if (g.Gap == 0) { g.Filled = true; return g; }
            for (int k = 0; k < sessionBars.Count; k++)
                if (sessionBars[k].Low <= priorClose && sessionBars[k].High >= priorClose) { g.Filled = true; break; }
            return g;
        }
    }
}
