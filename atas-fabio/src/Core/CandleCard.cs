using System;
using System.Collections.Generic;
using System.Linq;

namespace FabioOrderflow.Core
{
    /// <summary>
    /// Everything about ONE closed candle that Laya needs to read it the way the Fabio skill reads the tape:
    /// the bar itself (effort vs result), its footprint extremes and big prints, where it sits against the zones,
    /// how price approached the nearest zone, the context (bias, value, VWAP, IB) and the engine's own checklist.
    /// Built by the engine when a bar closes; published on the bridge for Laya to review.
    /// </summary>
    public sealed class CandleCard
    {
        public int Seq;                      // chart bar index (monotonic)
        public DateTime Time;                // bar open, exchange time
        public DateTime Session;
        public decimal Open, High, Low, Close, Volume, Delta;
        public decimal DeltaVsAvg, RangeVsAvg, VolumeVsAvg;   // 1.0 = typical recent bar
        public decimal BodyFraction, ClosePosition;           // close position: 0 = at the low, 1 = at the high
        public int BigBuys = -1, BigSells = -1;
        // footprint
        public decimal BarPoc, MaxAskPrice, MaxAskVolume, MaxBidPrice, MaxBidVolume;
        public decimal BidShareAtLow, AskShareAtHigh;         // share of the bar's selling printed in the bottom rows / buying in the top rows
        public decimal DeltaAtHigh, DeltaAtLow;
        /// <summary>The bar's full footprint: bid (sellers hitting) and ask (buyers lifting) volume at every price.</summary>
        public PriceLevelVol[] Ladder;
        public decimal ImbalanceRatio = 3m;                   // [Assumed] diagonal imbalance threshold for the summary below
        // context
        public string Bias = "", BiasNote = "", Location = "", AuctionVs3Month = "";
        public decimal PriorPoc, PriorVah, PriorVal, Vwap, VwapSd, IbHigh, IbLow;
        public decimal DevPoc, DevVah, DevVal, SessionCvd;
        public string NearestZone = ""; public decimal NearestZonePrice; public string NearestZoneSide = ""; public decimal NearestZoneTicks;   // nearest TRADE zone
        public string NearestLevel = ""; public decimal NearestLevelPrice;                                                                   // nearest level of any kind
        public string ApproachState = ""; public decimal ApproachEfficiency;
        public bool EngineSignal;            // the engine fired a set-up on this candle
        public SetupEvaluation Checklist;    // engine checklist of the most relevant zone at this candle

        /// <summary>Order book around price at the candle's close (live only; null when ATAS sends no market depth).</summary>
        public LiquiditySnapshot Liquidity;

        public Dictionary<string, object> ToJson(bool withChecklist = true)
        {
            var d = new Dictionary<string, object>
            {
                { "seq", Seq }, { "time", Time }, { "session", Session.ToString("yyyy-MM-dd") },
                { "bar", new Dictionary<string, object> {
                    { "open", Open }, { "high", High }, { "low", Low }, { "close", Close }, { "volume", Volume }, { "delta", Delta },
                    { "delta_vs_avg", R(DeltaVsAvg) }, { "range_vs_avg", R(RangeVsAvg) }, { "volume_vs_avg", R(VolumeVsAvg) },
                    { "body_fraction", R(BodyFraction) }, { "close_position", R(ClosePosition) },
                    { "big_buys", BigBuys < 0 ? null : (object)BigBuys }, { "big_sells", BigSells < 0 ? null : (object)BigSells } } },
                { "footprint", new Dictionary<string, object> {
                    { "bar_poc", BarPoc }, { "max_ask", new Dictionary<string, object> { { "price", MaxAskPrice }, { "volume", R(MaxAskVolume) } } },
                    { "max_bid", new Dictionary<string, object> { { "price", MaxBidPrice }, { "volume", R(MaxBidVolume) } } },
                    { "bid_share_at_low", R(BidShareAtLow) }, { "ask_share_at_high", R(AskShareAtHigh) },
                    { "delta_at_high", R(DeltaAtHigh) }, { "delta_at_low", R(DeltaAtLow) } } },
                { "context", new Dictionary<string, object> {
                    { "bias", Bias }, { "bias_note", BiasNote }, { "vs_3_month_value", AuctionVs3Month }, { "location_vs_prior_value", Location },
                    { "prior_poc", PriorPoc }, { "prior_vah", PriorVah }, { "prior_val", PriorVal },
                    { "vwap", R(Vwap) }, { "price_vs_vwap", Vwap <= 0 ? "" : Close >= Vwap ? "above" : "below" },
                    { "vwap_sd", R(VwapSd) }, { "vwap_position_sd", VwapSd > 0 ? R((Close - Vwap) / VwapSd) : 0m },
                    { "developing_poc", DevPoc }, { "developing_vah", DevVah }, { "developing_val", DevVal },
                    { "session_cumulative_delta", R(SessionCvd) },
                    { "ib_high", IbHigh }, { "ib_low", IbLow },
                    { "nearest_zone", NearestZone }, { "nearest_zone_price", NearestZonePrice }, { "nearest_zone_trade_side", NearestZoneSide },
                    { "distance_to_zone_ticks", R(NearestZoneTicks) },
                    { "nearest_level", NearestLevel }, { "nearest_level_price", NearestLevelPrice },
                    { "approach", ApproachState }, { "approach_efficiency", R(ApproachEfficiency) } } },
                { "engine_signal", EngineSignal }
            };
            if (withChecklist && Checklist != null)
                d["engine_checklist"] = new Dictionary<string, object>
                {
                    { "model", Checklist.Model }, { "tag", Checklist.Tag }, { "side", Checklist.Side.ToString() },
                    { "required_met", Checklist.RequiredMetCount }, { "required_total", Checklist.RequiredCount },
                    { "confluence_met", Checklist.ConfluenceMet }, { "confluence_total", Checklist.ConfluenceCount },
                    { "checks", Checklist.Checks.Select(c => (object)(c.State == CheckState.Met ? "[x] " : c.State == CheckState.NotMet ? "[ ] " : "[?] ") + c.Name + (string.IsNullOrEmpty(c.Detail) ? "" : " - " + c.Detail)).ToList() }
                };
            if (Liquidity != null) d["liquidity"] = Liquidity.ToJson();
            if (Ladder != null && Ladder.Length > 0) d["footprint_ladder"] = LadderJson();
            return d;
        }

        /// <summary>
        /// Rows from the high to the low: [price, bid, ask, delta], at most 48 rows (wider bars are grouped by a few ticks),
        /// plus diagonal imbalances (ask at a price vs bid one tick lower, and the mirror) of at least ImbalanceRatio and
        /// the runs of 3 or more stacked ones. Raw data for Laya's footprint read; the ratio is an Assumed convention.
        /// </summary>
        public Dictionary<string, object> LadderJson()
        {
            var rows = Ladder.OrderByDescending(l => l.Price).ToList();
            decimal tick = rows.Count > 1 ? rows.Zip(rows.Skip(1), (a, b) => a.Price - b.Price).Where(x => x > 0).DefaultIfEmpty(0.25m).Min() : 0.25m;
            var bid = rows.ToDictionary(l => l.Price, l => l.Bid);
            var buyImb = new List<decimal>(); var sellImb = new List<decimal>();
            foreach (var l in rows)
            {
                decimal below, above;
                if (bid.TryGetValue(l.Price - tick, out below) && l.Ask >= ImbalanceRatio * Math.Max(below, 1) && l.Ask > 0) buyImb.Add(l.Price);
                var up = rows.FirstOrDefault(x => x.Price == l.Price + tick);
                above = up.Price == l.Price + tick ? up.Ask : 0;
                if (l.Bid >= ImbalanceRatio * Math.Max(above, 1) && l.Bid > 0) sellImb.Add(l.Price);
            }
            int group = Math.Max(1, (int)Math.Ceiling(rows.Count / 48.0));
            var outRows = new List<object>();
            for (int i = 0; i < rows.Count; i += group)
            {
                var g = rows.Skip(i).Take(group).ToList();
                decimal b = g.Sum(x => x.Bid), a = g.Sum(x => x.Ask);
                outRows.Add(new List<object> { g[0].Price, b, a, a - b });
            }
            return new Dictionary<string, object>
            {
                { "rows_high_to_low", outRows }, { "ticks_per_row", group },
                { "buy_imbalances", buyImb }, { "sell_imbalances", sellImb },
                { "stacked_buy_imbalances", Stacked(buyImb, tick) }, { "stacked_sell_imbalances", Stacked(sellImb, tick) },
                { "imbalance_ratio", ImbalanceRatio }
            };
        }

        private static List<object> Stacked(List<decimal> prices, decimal tick)
        {
            var res = new List<object>();
            var p = prices.OrderBy(x => x).ToList();
            int start = 0;
            for (int i = 1; i <= p.Count; i++)
            {
                if (i < p.Count && p[i] - p[i - 1] == tick) continue;
                if (i - start >= 3) res.Add(new List<object> { p[start], p[i - 1] });
                start = i;
            }
            return res;
        }

        private static decimal R(decimal v) { return decimal.Round(v, 2); }

        /// <summary>Footprint summary of one bar.</summary>
        public static void FillFootprint(CandleCard c, Bar b, PriceLevelVol[] levels, FabioParams p)
        {
            if (levels == null || levels.Length == 0) return;
            c.Ladder = levels;
            decimal edge = p.FootprintEdgeTicks * p.TickSize, maxVol = -1, bidLow = 0, bidAll = 0, askHigh = 0, askAll = 0;
            c.MaxAskVolume = -1; c.MaxBidVolume = -1;
            foreach (var l in levels)
            {
                decimal v = l.Bid + l.Ask;
                if (v > maxVol) { maxVol = v; c.BarPoc = l.Price; }
                if (l.Ask > c.MaxAskVolume) { c.MaxAskVolume = l.Ask; c.MaxAskPrice = l.Price; }
                if (l.Bid > c.MaxBidVolume) { c.MaxBidVolume = l.Bid; c.MaxBidPrice = l.Price; }
                bidAll += l.Bid; askAll += l.Ask;
                if (l.Price <= b.Low + edge) { bidLow += l.Bid; c.DeltaAtLow += l.Ask - l.Bid; }
                if (l.Price >= b.High - edge) { askHigh += l.Ask; c.DeltaAtHigh += l.Ask - l.Bid; }
            }
            c.BidShareAtLow = bidAll > 0 ? bidLow / bidAll : 0;
            c.AskShareAtHigh = askAll > 0 ? askHigh / askAll : 0;
        }
    }
}
