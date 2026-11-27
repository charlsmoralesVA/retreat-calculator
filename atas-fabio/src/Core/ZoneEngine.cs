using System;
using System.Collections.Generic;
using System.Linq;

namespace FabioOrderflow.Core
{
    /// <summary>
    /// Pillar 2 - location (SKILL.md): value-area high (expensive for buyers, sellers defend), value-area low
    /// (cheap, buyers defend) and the point of control (a magnet while balanced; a rejector once price has
    /// accepted outside value). Delta / sniper levels refine broad zones: in a balanced area only levels
    /// OUTSIDE the value area; in a swing, the most significant level even inside it.
    /// </summary>
    public static class ZoneEngine
    {
        /// <summary>Acceptance outside the prior value area = the last N closes all on the same side.</summary>
        public static ValueLocation Locate(IList<decimal> sessionCloses, ProfileStats prior, int acceptanceBars)
        {
            if (prior == null || sessionCloses.Count == 0) return ValueLocation.Inside;
            int n = Math.Max(1, acceptanceBars);
            if (sessionCloses.Count < n) return ValueLocation.Inside;
            bool above = true, below = true;
            for (int i = sessionCloses.Count - n; i < sessionCloses.Count; i++)
            {
                if (sessionCloses[i] <= prior.Vah) above = false;
                if (sessionCloses[i] >= prior.Val) below = false;
            }
            return above ? ValueLocation.AcceptedAbove : below ? ValueLocation.AcceptedBelow : ValueLocation.Inside;
        }

        public static List<Zone> Build(ProfileStats prior, ValueLocation loc, FabioParams p)
        {
            var z = new List<Zone>();
            if (prior == null) return z;
            switch (loc)
            {
                case ValueLocation.Inside:
                    z.Add(new Zone { Kind = ZoneKind.Vah, Price = prior.Vah, Candidate = Side.Short, Label = "Prev VAH (sellers defend)", Source = Provenance.Stated });
                    z.Add(new Zone { Kind = ZoneKind.Val, Price = prior.Val, Candidate = Side.Long, Label = "Prev VAL (buyers defend)", Source = Provenance.Stated });
                    z.Add(new Zone { Kind = ZoneKind.Poc, Price = prior.Poc, Candidate = Side.None, Label = "Prev POC (magnet)", Source = Provenance.Stated });
                    break;
                case ValueLocation.AcceptedAbove:
                    z.Add(new Zone { Kind = ZoneKind.Poc, Price = prior.Poc, Candidate = Side.Long, Label = "Prev POC (rejector after acceptance above)", Source = Provenance.Stated });
                    z.Add(new Zone { Kind = ZoneKind.Vah, Price = prior.Vah, Candidate = Side.None, Label = "Prev VAH (reference)" });
                    z.Add(new Zone { Kind = ZoneKind.Val, Price = prior.Val, Candidate = Side.None, Label = "Prev VAL (reference)" });
                    break;
                case ValueLocation.AcceptedBelow:
                    z.Add(new Zone { Kind = ZoneKind.Poc, Price = prior.Poc, Candidate = Side.Short, Label = "Prev POC (rejector after acceptance below)", Source = Provenance.Stated });
                    z.Add(new Zone { Kind = ZoneKind.Vah, Price = prior.Vah, Candidate = Side.None, Label = "Prev VAH (reference)" });
                    z.Add(new Zone { Kind = ZoneKind.Val, Price = prior.Val, Candidate = Side.None, Label = "Prev VAL (reference)" });
                    break;
            }
            return z;
        }

        /// <summary>
        /// Most significant aggression (largest |delta| at a price) on each side of the current price.
        /// Balanced: only levels outside [Val, Vah] of the profile. Swing: anywhere in the profile.
        /// "Significant" = largest |delta| is my reading of the rule; the reference files may define it more precisely.
        /// </summary>
        public static List<Zone> DeltaLevels(VolumeProfile profile, ProfileStats stats, decimal lastPrice, DeltaLevelMode mode)
        {
            var res = new List<Zone>();
            if (profile == null || profile.IsEmpty || stats == null) return res;

            decimal bestAbove = 0, bestBelow = 0, priceAbove = 0, priceBelow = 0;
            foreach (var px in profile.Prices)
            {
                decimal d = profile.DeltaAt(px), a = Math.Abs(d);
                bool outside = px > stats.Vah || px < stats.Val;
                if (mode == DeltaLevelMode.Balanced && !outside) continue;
                if (px > lastPrice && a > bestAbove) { bestAbove = a; priceAbove = px; }
                if (px < lastPrice && a > bestBelow) { bestBelow = a; priceBelow = px; }
            }
            if (bestAbove > 0)
                res.Add(new Zone { Kind = ZoneKind.DeltaLevel, Price = priceAbove, Candidate = Side.Short, Label = "Delta level above", Source = Provenance.Principle });
            if (bestBelow > 0)
                res.Add(new Zone { Kind = ZoneKind.DeltaLevel, Price = priceBelow, Candidate = Side.Long, Label = "Delta level below", Source = Provenance.Principle });
            return res;
        }
    }
}
