// Fabio Orderflow Strategy - ATAS chart strategy.
// Trading is OFF by default: it then only reports set-ups. When switched on it trades ONE position at a time:
//   market entry on the set-up -> once filled, a stop order at the set-up stop and a limit order at the target ->
//   when one of them fills, the other is cancelled. Flat at the end of the cash session. Stops for the session after
//   MaxLossesPerSession losing trades. Use a SIMULATION account first.
// Order API as used by a working public ATAS strategy sample: OpenOrder(new Order { Portfolio, Security, Direction,
// Type, QuantityToFill, Price, TriggerPrice, Comment }), CancelOrder, CurrentPosition, CanProcess, OnOrderChanged,
// OnStopping, RaiseShowNotification.
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ATAS.DataFeedsCore;
using ATAS.Indicators;
using ATAS.Strategies.Chart;
using Utils.Common.Logging;
using Core = FabioOrderflow.Core;

namespace FabioOrderflow.Atas
{
    [DisplayName("Fabio Orderflow Strategy")]
    public class FabioStrategy : ChartStrategy
    {
        private readonly FabioFeed _feed = new FabioFeed();

        private Order _entryOrder, _stopOrder, _targetOrder;
        private Core.SetupEvaluation _active;
        private bool _entrySent, _bracketPlaced, _flattenSent;
        private int _entryBar = -1;
        private decimal _entryRef;
        private DateTime? _session;
        private int _lossesThisSession;
        private int _lastSignalHandled = -1;

        [Display(Name = "Trading enabled (OFF = report set-ups only)", GroupName = "1. Execution", Order = 10)]
        public bool TradingEnabled { get; set; } = false;
        [Display(Name = "Contracts", GroupName = "1. Execution", Order = 20)]
        public decimal Contracts { get; set; } = 1;
        [Display(Name = "Max losing trades per session", GroupName = "1. Execution", Order = 30)]
        public int MaxLossesPerSession { get; set; } = 2;
        [Display(Name = "Flatten when stopping the strategy", GroupName = "1. Execution", Order = 40)]
        public bool ClosePositionOnStopping { get; set; } = true;

        [Display(Name = "Exchange time zone", GroupName = "2. Session", Order = 10)]
        public string TimeZoneId { get { return _feed.TimeZoneId; } set { _feed.TimeZoneId = value; } }
        [Display(Name = "Require bias alignment", GroupName = "3. Rules", Order = 10)]
        public bool RequireBias { get { return _feed.P.RequireBiasAlignment; } set { _feed.P.RequireBiasAlignment = value; } }
        [Display(Name = "Max set-ups per session", GroupName = "3. Rules", Order = 20)]
        public int MaxSignals { get { return _feed.P.MaxSignalsPerSession; } set { _feed.P.MaxSignalsPerSession = Math.Max(1, value); } }

        protected override void OnCalculate(int bar, decimal value)
        {
            try
            {
                _feed.P.TickSize = InstrumentInfo.TickSize;
                var st = _feed.Process(GetCandle(bar), bar, CurrentBar, InstrumentInfo.TimeZoneOffset);

                foreach (var s in st.NewSignals)
                {
                    if (s.ConfirmGlobalIndex < CurrentBar - 2 || s.ConfirmGlobalIndex == _lastSignalHandled) continue;  // history: report nothing
                    _lastSignalHandled = s.ConfirmGlobalIndex;
                    RaiseShowNotification(string.Format("Fabio set-up: {0} {1} entry {2} stop {3} target {4} R:R {5:0.0}",
                        s.Side, s.Tag, s.Entry, s.Stop, s.Target, s.Rr), level: LoggingLevel.Warning);
                    if (TradingEnabled && CanProcess(bar)) TryEnter(s);
                }

                if (!CanProcess(bar)) return;
                ManagePosition(st);
            }
            catch (Exception e)
            {
                RaiseShowNotification("Fabio strategy error: " + e.Message, level: LoggingLevel.Warning);
            }
        }

        protected override void OnOrderChanged(Order order)
        {
            base.OnOrderChanged(order);
            try { if (_feed.State != null) ManagePosition(_feed.State); }
            catch (Exception e) { RaiseShowNotification("Fabio strategy error: " + e.Message, level: LoggingLevel.Warning); }
        }

        protected override void OnStopping()
        {
            if (ClosePositionOnStopping) Flatten("strategy stopped");
            base.OnStopping();
        }

        // ------------------------------------------------------------------------------------
        private void TryEnter(Core.SetupEvaluation s)
        {
            if (_entrySent || CurrentPosition != 0) return;
            if (_lossesThisSession >= MaxLossesPerSession) { RaiseShowNotification("Fabio: loss limit for this session reached, set-up not traded"); return; }

            _active = s;
            _entryRef = s.Entry;
            _entrySent = true; _bracketPlaced = false; _entryBar = CurrentBar - 1;
            _entryOrder = new Order
            {
                Portfolio = Portfolio, Security = Security,
                Direction = s.Side == Core.Side.Long ? OrderDirections.Buy : OrderDirections.Sell,
                Type = OrderTypes.Market, QuantityToFill = Contracts, Comment = "Fabio entry " + s.Tag
            };
            OpenOrder(_entryOrder);
        }

        private void ManagePosition(Core.EngineState st)
        {
            var key = st.InRth && _feed.Bars.Count > 0 ? (DateTime?)_feed.Bars[_feed.Bars.Count - 1].Time.Date : null;
            if (key != null && key != _session) { _session = key; _lossesThisSession = 0; }
            if (CurrentPosition == 0) _flattenSent = false;

            // entry never filled (rejected / not executed) -> give up after 2 bars instead of waiting forever
            if (_entrySent && !_bracketPlaced && CurrentPosition == 0 && _entryBar >= 0 && CurrentBar - 1 - _entryBar > 2)
            {
                RaiseShowNotification("Fabio: entry order not filled, set-up abandoned", level: LoggingLevel.Warning);
                CancelSafe(_entryOrder); ResetTrade(); return;
            }

            // flat at the end of the cash session
            if (!st.InRth && (CurrentPosition != 0 || _entrySent)) { Flatten("cash session ended"); return; }

            // entry filled -> protect it with a stop and a target
            if (_entrySent && !_bracketPlaced && CurrentPosition != 0 && _active != null)
            {
                decimal qty = Math.Abs(CurrentPosition);
                var exitDir = CurrentPosition > 0 ? OrderDirections.Sell : OrderDirections.Buy;
                _stopOrder = new Order { Portfolio = Portfolio, Security = Security, Direction = exitDir, Type = OrderTypes.Stop, TriggerPrice = Round(_active.Stop), QuantityToFill = qty, Comment = "Fabio stop" };
                _targetOrder = new Order { Portfolio = Portfolio, Security = Security, Direction = exitDir, Type = OrderTypes.Limit, Price = Round(_active.Target), QuantityToFill = qty, Comment = "Fabio target" };
                OpenOrder(_stopOrder);
                OpenOrder(_targetOrder);
                _bracketPlaced = true;
                return;
            }

            // stop or target filled -> cancel the other one
            if (_bracketPlaced && CurrentPosition == 0)
            {
                decimal last = _feed.Bars.Count > 0 ? _feed.Bars[_feed.Bars.Count - 1].Close : _entryRef;
                bool lost = _active != null && (_active.Side == Core.Side.Long ? last < _entryRef : last > _entryRef);
                if (lost) _lossesThisSession++;
                CancelSafe(_stopOrder); CancelSafe(_targetOrder);
                ResetTrade();
            }
        }

        private void Flatten(string why)
        {
            CancelSafe(_stopOrder); CancelSafe(_targetOrder);
            if (CurrentPosition != 0 && !_flattenSent)
            {
                _flattenSent = true;
                OpenOrder(new Order
                {
                    Portfolio = Portfolio, Security = Security,
                    Direction = CurrentPosition > 0 ? OrderDirections.Sell : OrderDirections.Buy,
                    Type = OrderTypes.Market, QuantityToFill = Math.Abs(CurrentPosition), Comment = "Fabio flatten: " + why
                });
                RaiseShowNotification("Fabio: flattening (" + why + ")", level: LoggingLevel.Warning);
            }
            ResetTrade();
        }

        private void CancelSafe(Order o)
        {
            if (o == null) return;
            try { CancelOrder(o); } catch (Exception) { /* already filled or cancelled */ }
        }

        private void ResetTrade()
        {
            _entryOrder = null; _stopOrder = null; _targetOrder = null; _active = null;
            _entrySent = false; _bracketPlaced = false; _entryBar = -1;
        }

        private decimal Round(decimal price)
        {
            decimal tick = InstrumentInfo.TickSize <= 0 ? 0.25m : InstrumentInfo.TickSize;
            return Math.Round(price / tick) * tick;
        }
    }
}
