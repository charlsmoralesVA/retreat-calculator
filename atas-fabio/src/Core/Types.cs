using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FabioOrderflow.Core
{
    public enum Side { None = 0, Long = 1, Short = -1 }
    public enum Bias { Neutral = 0, Bullish = 1, Bearish = -1 }

    /// <summary>Where price is relative to the composition (3-month) value area.</summary>
    public enum AuctionPosition { Inside, Above, Below }

    /// <summary>Where price is relative to the PRIOR session's value area (after acceptance).</summary>
    public enum ValueLocation { Inside, AcceptedAbove, AcceptedBelow }

    public enum CheckState { Unknown, NotMet, Met }

    /// <summary>
    /// Where a rule comes from, so nothing here is passed off as Fabio's when it is not.
    /// Stated    = the rule or number is written in SKILL.md.
    /// Principle = SKILL.md states the idea; the numeric implementation is mine (calibrate).
    /// Assumed   = not in SKILL.md at all (needs the reference files or your own testing).
    /// </summary>
    public enum Provenance { Stated, Principle, Assumed }

    public enum ZoneKind { Vah, Val, Poc, DeltaLevel, IbHigh, IbLow }
    public enum ApproachState { Unknown, Struggle, Normal, Effortless }

    /// <summary>Volume traded at one price inside a bar. Bid = sells hitting the bid, Ask = buys lifting the offer.</summary>
    public struct PriceLevelVol
    {
        public decimal Price, Bid, Ask;
        public PriceLevelVol(decimal price, decimal bid, decimal ask) { Price = price; Bid = bid; Ask = ask; }
    }

    public sealed class Bar
    {
        public DateTime Time;
        public decimal Open, High, Low, Close, Volume, Delta;
        /// <summary>Aggressive prints at or above the big-trade filter. -1 = not available.</summary>
        public int BigBuys = -1, BigSells = -1;
        public decimal Range { get { return High - Low; } }
        public decimal Body { get { return Math.Abs(Close - Open); } }
    }

    public sealed class Zone
    {
        public ZoneKind Kind;
        public decimal Price;
        /// <summary>Side a reaction at this zone would be traded on (None = reference level / target only).</summary>
        public Side Candidate;
        public string Label;
        public Provenance Source = Provenance.Principle;
        public override string ToString() { return string.Format("{0} {1} ({2})", Label, Price, Candidate); }
    }

    public sealed class Check
    {
        public string Name;
        public CheckState State;
        public Provenance Source;
        public bool Required;
        public string Detail = "";
    }

    public sealed class SetupEvaluation
    {
        public string Model = "ZoneReaction";
        public Zone Zone;
        public Side Side;
        public int AbsorbIndex = -1;          // index into the engine's RTH bar list
        public int ConfirmIndex = -1;         // same
        public int ConfirmGlobalIndex = -1;   // chart bar index (set by the engine)
        public List<Check> Checks = new List<Check>();
        public decimal Entry, Stop, Target, Rr;
        public bool Closed;                   // false while the confirming bar is still forming

        public bool AllRequiredMet { get { return Checks.All(c => !c.Required || c.State == CheckState.Met); } }
        public int Score { get { return Checks.Count(c => c.State == CheckState.Met); } }
        public int RequiredCount { get { return Checks.Count(c => c.Required); } }
        public int RequiredMetCount { get { return Checks.Count(c => c.Required && c.State == CheckState.Met); } }
        public int ConfluenceCount { get { return Checks.Count(c => !c.Required); } }
        public int ConfluenceMet { get { return Checks.Count(c => !c.Required && c.State == CheckState.Met); } }

        public string Tag
        {
            get { return Zone == null ? Model : string.Format("{0} {1}", Model, Zone.Kind.ToString().ToUpperInvariant()); }
        }

        public string ToChecklist()
        {
            var sb = new StringBuilder();
            sb.AppendFormat("{0} {1} @ {2}{3}\n", Side, Tag, Zone == null ? 0 : Zone.Price, Closed ? "" : " (forming)");
            foreach (var c in Checks)
            {
                string mark = c.State == CheckState.Met ? "[x]" : c.State == CheckState.NotMet ? "[ ]" : "[?]";
                sb.AppendFormat(" {0} {1}{2} <{3}>{4}\n", mark, c.Name, c.Required ? "" : " (info)", c.Source,
                    string.IsNullOrEmpty(c.Detail) ? "" : " - " + c.Detail);
            }
            if (Entry != 0) sb.AppendFormat(" entry {0}  stop {1}  target {2}  R:R {3:0.00}\n", Entry, Stop, Target, Rr);
            sb.AppendFormat(" confluence {0}/{1} (information, not required)\n", ConfluenceMet, ConfluenceCount);
            sb.AppendFormat(" => {0}\n", AllRequiredMet ? "ALL REQUIRED CHECKS MET" : "waiting");
            return sb.ToString();
        }
    }
}
