using System;
using System.Collections.Generic;
using System.Linq;

namespace FabioOrderflow.Core
{
    /// <summary>
    /// Turns one candidate zone into a checklist: bias -> location -> approach -> battle of orders -> exits.
    /// The checklist IS the monitoring: the chart shows which conditions are met so you can see how close the
    /// market is to a setup and judge the discretionary parts yourself.
    /// </summary>
    public static class SetupEvaluator
    {
        public static SetupEvaluation EvaluateZoneReaction(
            IReadOnlyList<Bar> bars, int i, int minIndex, Zone zone, Bias bias, IList<Zone> allZones, FabioParams p, string model)
        {
            var side = zone.Candidate;
            var ev = new SetupEvaluation { Model = model, Zone = zone, Side = side, ConfirmIndex = i };
            var bar = bars[i];
            // baseline = normal activity BEFORE the set-up began (approach + absorption + confirmation bars are excluded)
            var stats = BarStats.Rolling(bars, Math.Max(0, i - p.MaxConfirmBars - p.ApproachBars), p.StatsWindow);
            bool statsOk = stats.Count >= p.MinStatsBars && stats.AvgAbsDelta > 0 && stats.AvgRange > 0;
            decimal tol = p.ZoneToleranceTicks * p.TickSize;
            int first = Math.Max(minIndex, i - p.MaxConfirmBars);   // never look back across the session boundary

            // most recent absorption bar before the confirming bar, and whether the zone was touched at all
            int absorb = -1;
            bool touched = Touches(bars, first, i, side, zone.Price, tol);
            if (statsOk)
                for (int j = i - 1; j >= first; j--)
                    if (BattleOfOrders.IsAbsorption(bars[j], side, zone.Price, stats, p)) { absorb = j; break; }
            ev.AbsorbIndex = absorb;

            // 1. bias (pillar 1)
            bool aligned = bias == Bias.Neutral || (bias == Bias.Bullish && side == Side.Long) || (bias == Bias.Bearish && side == Side.Short);
            ev.Checks.Add(new Check
            {
                Name = "Bias supports " + side, Source = Provenance.Principle, Required = p.RequireBiasAlignment,
                State = aligned ? CheckState.Met : CheckState.NotMet,
                Detail = "bias " + bias
            });

            // 2. location (pillar 2)
            ev.Checks.Add(new Check
            {
                Name = "Price at zone " + zone.Label, Source = Provenance.Stated, Required = true,
                State = touched ? CheckState.Met : CheckState.NotMet,
                Detail = "zone " + zone.Price
            });

            // 3. reactor: how did price arrive?
            ApproachResult ap = new ApproachResult();
            if (touched && statsOk) ap = BattleOfOrders.MeasureApproach(bars, minIndex, absorb >= 0 ? absorb : i, side, stats, p);
            ev.Checks.Add(new Check
            {
                Name = "Approach not effortless (reactor)", Source = Provenance.Principle, Required = p.RequireApproachFilter,
                State = ap.State == ApproachState.Unknown ? CheckState.Unknown : ap.State == ApproachState.Effortless ? CheckState.NotMet : CheckState.Met,
                Detail = ap.State == ApproachState.Unknown ? "" : string.Format("{0}, efficiency {1:0.00}", ap.State, ap.Efficiency)
            });

            // 4. the losing side is absorbed
            ev.Checks.Add(new Check
            {
                Name = "Losing side absorbed (effort, no result)", Source = Provenance.Principle, Required = true,
                State = !statsOk ? CheckState.Unknown : absorb >= 0 ? CheckState.Met : CheckState.NotMet,
                Detail = absorb >= 0 ? "bar " + absorb + " delta " + bars[absorb].Delta : ""
            });

            // 5. the winning side wins in the body of the candle
            bool wins = absorb >= 0 && BattleOfOrders.IsConfirmation(bar, bars[absorb], side, stats, p);
            ev.Checks.Add(new Check
            {
                Name = "Winning side enters in the body and closes its way", Source = Provenance.Principle, Required = true,
                State = !statsOk ? CheckState.Unknown : wins ? CheckState.Met : CheckState.NotMet,
                Detail = "delta " + bar.Delta
            });

            // 6. big trades on the winning side (info only)
            int winnerBig = side == Side.Long ? bar.BigBuys : bar.BigSells;
            ev.Checks.Add(new Check
            {
                Name = "Big trades with the winner", Source = Provenance.Stated, Required = false,
                State = winnerBig < 0 ? CheckState.Unknown : winnerBig >= 1 ? CheckState.Met : CheckState.NotMet,
                Detail = winnerBig < 0 ? "no trade data" : winnerBig + " prints"
            });

            // 7. exits: stop just beyond the zone, target at a previous control level / POC / value edge, else ~1:2
            ev.Entry = bar.Close;
            decimal buffer = p.StopBufferTicks * p.TickSize;
            if (side == Side.Long)
            {
                decimal ext = absorb >= 0 ? bars[absorb].Low : bar.Low;
                ev.Stop = Math.Min(zone.Price, ext) - buffer;
            }
            else
            {
                decimal ext = absorb >= 0 ? bars[absorb].High : bar.High;
                ev.Stop = Math.Max(zone.Price, ext) + buffer;
            }
            decimal risk = side == Side.Long ? ev.Entry - ev.Stop : ev.Stop - ev.Entry;
            if (risk > 0)
            {
                decimal sgn = side == Side.Long ? 1 : -1;
                var candidates = allZones.Where(z => z != zone && (z.Price - ev.Entry) * sgn > 0)
                                         .Select(z => z.Price).OrderBy(x => Math.Abs(x - ev.Entry));
                decimal chosen = 0; bool found = false;
                foreach (var c in candidates)
                    if (Math.Abs(c - ev.Entry) / risk >= p.MinRr) { chosen = c; found = true; break; }
                ev.Target = found ? chosen : ev.Entry + sgn * p.DefaultRr * risk;
                ev.Rr = Math.Abs(ev.Target - ev.Entry) / risk;
            }
            ev.Checks.Add(new Check
            {
                Name = "Stop beyond zone, R:R acceptable", Source = Provenance.Stated, Required = true,
                State = risk > 0 && ev.Rr >= p.MinRr ? CheckState.Met : CheckState.NotMet,
                Detail = risk > 0 ? string.Format("risk {0}, R:R {1:0.00}", risk, ev.Rr) : "entry on the wrong side of the stop"
            });
            return ev;
        }

        private static bool Touches(IReadOnlyList<Bar> bars, int from, int to, Side side, decimal zone, decimal tol)
        {
            for (int k = from; k <= to; k++)
                if (BattleOfOrders.Touches(bars[k], side, zone, tol)) return true;
            return false;
        }
    }
}
