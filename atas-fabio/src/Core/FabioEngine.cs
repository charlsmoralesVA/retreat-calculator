using System;
using System.Collections.Generic;
using System.Linq;

namespace FabioOrderflow.Core
{
    public sealed class EngineState
    {
        public bool InRth;
        public bool Ready;                       // at least one completed prior session exists
        public SessionSnapshot Prior;
        public Bias DailyShift, Narrative, Final;
        public AuctionPosition Auction;
        public ValueLocation Location;
        public string BiasNote = "";
        public ProfileStats Composition;
        public List<Zone> Zones = new List<Zone>();
        public IbInfo Ib;
        public GapInfo Gap;
        /// <summary>Checklist of the most relevant zone right now (monitoring; may still be "waiting").</summary>
        public SetupEvaluation Best;
        /// <summary>Confirmed signals on bars that have just closed.</summary>
        public List<SetupEvaluation> NewSignals = new List<SetupEvaluation>();
        /// <summary>One card per regular-session bar that has just closed (for Laya's candle-by-candle review).</summary>
        public List<CandleCard> ClosedCandles = new List<CandleCard>();
        public int SignalsThisSession;
        /// <summary>Session VWAP (context only: SKILL.md lists VWAP as a concept but gives no trade rule for it here).</summary>
        public decimal Vwap;
        public decimal LastPrice;
        public DateTime LastBarTime;
        /// <summary>Every reference level Fabio's method looks at (developing value, value ladder, nodes, VWAP bands, CVD, IB, overnight, London).</summary>
        public MarketMap Map = new MarketMap();
    }

    /// <summary>
    /// Stateful driver. Feed it one chart bar at a time (and again on every tick of the forming bar).
    ///  * Profiles use COMPLETED regular-session bars only, so repeated ticks never double count.
    ///  * A bar is judged for a signal exactly once, when the next bar arrives (i.e. at its close).
    ///  * ETH bars are ignored when RthOnly is set.
    /// </summary>
    public sealed class FabioEngine
    {
        private readonly FabioParams _p;
        private readonly SessionClock _clock;

        private readonly List<Bar> _rth = new List<Bar>();
        private readonly List<int> _rthGlobal = new List<int>();
        private readonly List<DateTime> _rthKey = new List<DateTime>();
        private readonly List<PriceLevelVol[]> _rthLevels = new List<PriceLevelVol[]>();
        private readonly List<SessionSnapshot> _sessions = new List<SessionSnapshot>();

        private int _accum = -1;
        private DateTime? _curKey;
        private int _curFirst;
        private VolumeProfile _curProfile = new VolumeProfile();
        private decimal _curOpen, _curHigh, _curLow, _curClose;
        private decimal _vwapPv, _vwapV, _vwapPv2, _sessionCvd;
        private List<VolumeNode> _priorNodes = new List<VolumeNode>();
        private List<SessionValue> _valueHistory = new List<SessionValue>();
        // overnight (bars outside the cash session) and London
        private VolumeProfile _onProfile = new VolumeProfile();
        private decimal _onHigh = decimal.MinValue, _onLow = decimal.MaxValue, _lonHigh = decimal.MinValue, _lonLow = decimal.MaxValue;
        private bool _onFrozen;
        private LevelSet _onStats = new LevelSet();
        private bool _lonHas; private decimal _lonHighF, _lonLowF;
        private int _ethIndex = -1; private Bar _ethBar; private PriceLevelVol[] _ethLevels;
        private int _onCacheIndex = -2; private LevelSet _onLive = new LevelSet();
        private readonly List<decimal> _sessionCloses = new List<decimal>();
        private readonly List<decimal> _allCloses = new List<decimal>();
        private ValueLocation _location = ValueLocation.Inside;
        private AuctionPosition _auction = AuctionPosition.Inside;
        private Bias _narrative = Bias.Neutral, _dailyShift = Bias.Neutral;
        private ProfileStats _comp;
        private int _signalsThisSession, _lastSignalRth = -1000, _lastSignalAbsorb = -1;
        private readonly List<SetupEvaluation> _pending = new List<SetupEvaluation>();
        private readonly List<CandleCard> _pendingCards = new List<CandleCard>();
        private bool _hasData;
        private EngineState _last;

        public FabioEngine(FabioParams p)
        {
            _p = p;
            _clock = new SessionClock(p.RthStart, p.RthEnd);
        }

        public int CompletedSessions { get { return _sessions.Count; } }

        /// <summary>
        /// Backtest mode: only closed bars are evaluated and no candle cards, market map or display zones are built
        /// (they exist for Laya and the chart). The set-ups are identical to the full mode (checked by the tests).
        /// </summary>
        public bool Lean;

        public void Reset()
        {
            _rth.Clear(); _rthGlobal.Clear(); _rthKey.Clear(); _rthLevels.Clear(); _sessions.Clear();
            _accum = -1; _curKey = null; _curFirst = 0; _curProfile = new VolumeProfile();
            _sessionCloses.Clear(); _allCloses.Clear();
            _location = ValueLocation.Inside; _auction = AuctionPosition.Inside;
            _narrative = Bias.Neutral; _dailyShift = Bias.Neutral; _comp = null;
            _signalsThisSession = 0; _lastSignalRth = -1000; _lastSignalAbsorb = -1;
            _pending.Clear(); _pendingCards.Clear(); _last = null;
            _priorNodes = new List<VolumeNode>(); _valueHistory = new List<SessionValue>();
            ResetOvernight(); _onStats = new LevelSet(); _lonHas = false; _onCacheIndex = -2;
        }

        // ------------------------------------------------------------------ overnight / London (bars outside the cash session)
        private void ResetOvernight()
        {
            _onProfile = new VolumeProfile(); _onHigh = decimal.MinValue; _onLow = decimal.MaxValue;
            _lonHigh = decimal.MinValue; _lonLow = decimal.MaxValue; _onFrozen = false; _ethIndex = -1;
        }

        private void TrackEth(int index, Bar bar, PriceLevelVol[] levels)
        {
            if (_ethIndex >= 0 && index != _ethIndex) CommitEth();
            if (_onFrozen) ResetOvernight();                        // first bar after the cash session: a new overnight begins
            _ethIndex = index; _ethBar = bar; _ethLevels = levels ?? new PriceLevelVol[0];
        }

        private void CommitEth()
        {
            if (_ethIndex < 0 || _ethBar == null) return;
            foreach (var l in _ethLevels) _onProfile.Add(l);
            _onHigh = Math.Max(_onHigh, _ethBar.High); _onLow = Math.Min(_onLow, _ethBar.Low);
            var t = _ethBar.Time.TimeOfDay;
            if (t >= _p.LondonStart && t < _p.RthStart) { _lonHigh = Math.Max(_lonHigh, _ethBar.High); _lonLow = Math.Min(_lonLow, _ethBar.Low); }
            _ethIndex = -1;
        }

        private LevelSet OvernightLevels()
        {
            var ls = MarketMap.Levels(_onProfile, _p.TickSize, _p.ValueAreaPct);
            if (ls.Valid) { ls.High = _onHigh; ls.Low = _onLow; }
            return ls;
        }

        private void FreezeOvernight()
        {
            if (_onFrozen) return;
            CommitEth();
            _onStats = OvernightLevels();
            _lonHas = _lonHigh > decimal.MinValue; _lonHighF = _lonHigh; _lonLowF = _lonLow;
            _onFrozen = true;
        }

        public EngineState Update(int index, Bar bar, PriceLevelVol[] levels, bool forming)
        {
            if (index == 0 && _hasData) Reset();
            _hasData = true;

            DateTime? key = _clock.SessionKey(bar.Time);
            if (!key.HasValue && _p.RthOnly) { TrackEth(index, bar, levels); return Snapshot(false); }
            FreezeOvernight();                                                         // the cash session has started
            DateTime sessionKey = key ?? bar.Time.Date;

            int last = _rth.Count - 1;
            if (last >= 0 && _rthGlobal[last] == index)
            {
                _rth[last] = bar; _rthLevels[last] = levels ?? new PriceLevelVol[0];   // tick on the forming bar
            }
            else if (last < 0 || index > _rthGlobal[last])
            {
                CloseThrough(last);                                                    // the previous bar is now final
                _rth.Add(bar); _rthGlobal.Add(index); _rthKey.Add(sessionKey); _rthLevels.Add(levels ?? new PriceLevelVol[0]);
            }
            else return _last ?? Snapshot(false);                                      // older bar: ignore

            int cur = _rth.Count - 1;
            SwitchSession(cur);
            var st = Snapshot(true);
            if (!Lean) st.Best = Evaluate(cur, false);
            st.NewSignals.AddRange(_pending);
            _pending.Clear();
            st.ClosedCandles.AddRange(_pendingCards);
            _pendingCards.Clear();
            st.SignalsThisSession = _signalsThisSession;
            _last = st;
            return st;
        }

        // ------------------------------------------------------------------ internals

        private void CloseThrough(int through)
        {
            for (int k = _accum + 1; k <= through; k++)
            {
                SwitchSession(k);
                var best = Evaluate(k, true);     // registers a signal in _pending when everything required is met
                if (!Lean) _pendingCards.Add(BuildCard(k, best));
                Accumulate(k);
            }
        }

        private void SwitchSession(int k)
        {
            DateTime key = _rthKey[k];
            if (_curKey.HasValue && _curKey.Value == key) return;
            if (_curKey.HasValue) FinalizeSession();
            _curKey = key; _curFirst = k; _curProfile = new VolumeProfile();
            _curOpen = _rth[k].Open; _curHigh = _rth[k].High; _curLow = _rth[k].Low; _curClose = _rth[k].Close;
            _sessionCloses.Clear();
            _vwapPv = 0; _vwapV = 0; _vwapPv2 = 0; _sessionCvd = 0;
            _location = ValueLocation.Inside;
            _signalsThisSession = 0; _lastSignalRth = -1000; _lastSignalAbsorb = -1;
        }

        private void FinalizeSession()
        {
            var stats = _curProfile.Compute(_p.TickSize, _p.ValueAreaPct);
            if (stats == null) return;
            _sessions.Add(new SessionSnapshot
            {
                Date = _curKey.Value, Profile = _curProfile, Stats = stats,
                Open = _curOpen, High = _curHigh, Low = _curLow, Close = _curClose
            });
            int keep = Math.Max(_p.CompositionSessions, _p.NarrativeSessions) + 5;
            while (_sessions.Count > keep) _sessions.RemoveAt(0);
            _dailyShift = BiasEngine.LastShift(_sessions, _p.StrictValueShift);
            _narrative = BiasEngine.Narrative(_sessions, _p.NarrativeSessions, _p.StrictValueShift);
            _comp = BiasEngine.Composition(_sessions, _p.CompositionSessions, _p.TickSize, _p.ValueAreaPct, _p.NarrativeSessions);
            _priorNodes = MarketMap.FindNodes(_sessions[_sessions.Count - 1].Profile, _p.TickSize);
            _valueHistory = new List<SessionValue>();
            for (int i = Math.Max(0, _sessions.Count - 6); i < _sessions.Count; i++)
            {
                var ss = _sessions[i];
                var sv = new SessionValue { Date = ss.Date, Poc = ss.Stats.Poc, Vah = ss.Stats.Vah, Val = ss.Stats.Val, High = ss.High, Low = ss.Low };
                if (i > 0)
                {
                    var b = BiasEngine.ValueShift(_sessions[i - 1].Stats, ss.Stats, _p.StrictValueShift);
                    sv.ShiftVsPrevious = b == Bias.Bullish ? "higher" : b == Bias.Bearish ? "lower" : "overlapping";
                }
                _valueHistory.Add(sv);
            }
        }

        private void Accumulate(int k)
        {
            var b = _rth[k];
            foreach (var l in _rthLevels[k]) { _curProfile.Add(l); _vwapPv += l.Price * (l.Bid + l.Ask); _vwapV += l.Bid + l.Ask; _vwapPv2 += l.Price * l.Price * (l.Bid + l.Ask); }
            _sessionCvd += b.Delta;
            _curHigh = Math.Max(_curHigh, b.High);
            _curLow = Math.Min(_curLow, b.Low);
            _curClose = b.Close;
            _sessionCloses.Add(b.Close);
            _allCloses.Add(b.Close);
            if (_allCloses.Count > 500) _allCloses.RemoveRange(0, _allCloses.Count - 500);
            if (_sessions.Count > 0) _location = ZoneEngine.Locate(_sessionCloses, _sessions[_sessions.Count - 1].Stats, _p.AcceptanceBars);
            _auction = BiasEngine.ClassifyAuction(_allCloses, _comp, _p.AcceptanceBars, _auction);
            _accum = k;
        }

        private List<Bar> SessionBars()
        {
            return _rth.GetRange(_curFirst, _rth.Count - _curFirst);
        }

        private List<Zone> SessionDeltaLevels(decimal price)
        {
            if (_curProfile.IsEmpty) return new List<Zone>();
            var devStats = _curProfile.Compute(_p.TickSize, _p.ValueAreaPct);
            return ZoneEngine.DeltaLevels(_curProfile, devStats, price, _p.DeltaMode);
        }

        private List<Zone> BuildZones(SessionSnapshot prior, decimal price, out IbInfo ib)
        {
            var zones = ZoneEngine.Build(prior.Stats, _location, _p);
            if (_p.UseDeltaLevels) zones.AddRange(SessionDeltaLevels(price));
            ib = InitialBalance.Compute(SessionBars(), _p.RthStart, _p.IbMinutes);
            if (_p.EnableIvb && ib.Complete && ib.Breakout == Side.Long)
                zones.Add(new Zone { Kind = ZoneKind.IbHigh, Price = ib.High, Candidate = Side.Long, Label = "IB high (retest after breakout up)", Source = Provenance.Assumed });
            if (_p.EnableIvb && ib.Complete && ib.Breakout == Side.Short)
                zones.Add(new Zone { Kind = ZoneKind.IbLow, Price = ib.Low, Candidate = Side.Short, Label = "IB low (retest after breakout down)", Source = Provenance.Assumed });
            return zones;
        }

        private SetupEvaluation Evaluate(int k, bool closed)
        {
            if (_sessions.Count == 0) return null;
            var prior = _sessions[_sessions.Count - 1];
            decimal price = _rth[k].Close;
            IbInfo ib;
            var zones = BuildZones(prior, price, out ib);
            string note;
            Bias final = BiasEngine.FinalBias(_narrative, _auction, _comp != null, out note);
            var ctx = new EvalContext { Levels = _rthLevels, DeltaLevels = SessionDeltaLevels(price) };

            SetupEvaluation best = null;
            foreach (var z in zones)
            {
                if (z.Candidate == Side.None) continue;
                string model = (z.Kind == ZoneKind.IbHigh || z.Kind == ZoneKind.IbLow) ? "IVB-2 (provisional)" : "ZoneReaction";
                var ev = SetupEvaluator.EvaluateZoneReaction(_rth, k, _curFirst, z, final, zones, _p, model, ctx);
                ev.Closed = closed;
                ev.ConfirmGlobalIndex = _rthGlobal[k];
                if (IsBetter(ev, best, price)) best = ev;
            }

            if (closed && best != null && best.AllRequiredMet
                && _signalsThisSession < _p.MaxSignalsPerSession
                && k - _lastSignalRth >= _p.MinBarsBetweenSignals
                && best.AbsorbIndex != _lastSignalAbsorb)
            {
                _signalsThisSession++; _lastSignalRth = k; _lastSignalAbsorb = best.AbsorbIndex;
                _pending.Add(best);
            }
            return best;
        }

        private CandleCard BuildCard(int k, SetupEvaluation best)
        {
            var b = _rth[k];
            var c = new CandleCard
            {
                Seq = _rthGlobal[k], Time = b.Time, Session = _rthKey[k],
                Open = b.Open, High = b.High, Low = b.Low, Close = b.Close, Volume = b.Volume, Delta = b.Delta,
                BigBuys = b.BigBuys, BigSells = b.BigSells, Checklist = best
            };
            var stats = BarStats.Rolling(_rth, k, _p.StatsWindow);
            if (stats.AvgAbsDelta > 0) c.DeltaVsAvg = b.Delta / stats.AvgAbsDelta;
            if (stats.AvgRange > 0) c.RangeVsAvg = b.Range / stats.AvgRange;
            if (stats.AvgVolume > 0) c.VolumeVsAvg = b.Volume / stats.AvgVolume;
            decimal range = Math.Max(b.Range, _p.TickSize);
            c.BodyFraction = b.Body / range;
            c.ClosePosition = (b.Close - b.Low) / range;
            CandleCard.FillFootprint(c, b, _rthLevels[k], _p);

            string note;
            var final = BiasEngine.FinalBias(_narrative, _auction, _comp != null, out note);
            c.Bias = final.ToString(); c.BiasNote = note; c.Location = _location.ToString();
            c.AuctionVs3Month = _comp == null ? "unknown (needs 5+ sessions)" : _auction.ToString();

            decimal pv = _vwapPv, v = _vwapV, pv2 = _vwapPv2;
            foreach (var l in _rthLevels[k]) { decimal vol = l.Bid + l.Ask; pv += l.Price * vol; v += vol; pv2 += l.Price * l.Price * vol; }
            if (v > 0)
            {
                c.Vwap = pv / v;
                double var = (double)(pv2 / v - c.Vwap * c.Vwap);
                c.VwapSd = var > 0 ? (decimal)Math.Sqrt(var) : 0;
            }
            c.SessionCvd = _sessionCvd + b.Delta;
            var devLv = MarketMap.Levels(DevelopingProfile(k), _p.TickSize, _p.ValueAreaPct);
            if (devLv.Valid) { c.DevPoc = devLv.Poc; c.DevVah = devLv.Vah; c.DevVal = devLv.Val; }

            if (_sessions.Count > 0)
            {
                var prior = _sessions[_sessions.Count - 1];
                c.PriorPoc = prior.Stats.Poc; c.PriorVah = prior.Stats.Vah; c.PriorVal = prior.Stats.Val;
                IbInfo ib;
                var zones = BuildZones(prior, b.Close, out ib);
                if (ib != null && ib.Started) { c.IbHigh = ib.High; c.IbLow = ib.Low; }
                Zone near = null, level = null;
                foreach (var z in zones)
                {
                    if (level == null || Math.Abs(z.Price - b.Close) < Math.Abs(level.Price - b.Close)) level = z;
                    if (z.Candidate != Side.None && (near == null || Math.Abs(z.Price - b.Close) < Math.Abs(near.Price - b.Close))) near = z;
                }
                if (level != null) { c.NearestLevel = level.Label; c.NearestLevelPrice = level.Price; }
                if (near != null)
                {
                    c.NearestZone = near.Label; c.NearestZonePrice = near.Price; c.NearestZoneSide = near.Candidate.ToString();
                    c.NearestZoneTicks = Math.Abs(near.Price - b.Close) / _p.TickSize;
                    if (near.Candidate != Side.None)
                    {
                        var baseline = BarStats.Rolling(_rth, Math.Max(0, k - _p.ApproachBars), _p.StatsWindow);
                        var ap = BattleOfOrders.MeasureApproach(_rth, _curFirst, k + 1, near.Candidate, baseline, _p);
                        c.ApproachState = ap.State.ToString(); c.ApproachEfficiency = ap.Efficiency;
                    }
                }
            }
            c.EngineSignal = _pending.Any(sg => sg.ConfirmIndex == k);
            return c;
        }

        private static bool IsBetter(SetupEvaluation ev, SetupEvaluation best, decimal price)
        {
            if (best == null) return true;
            if (ev.AllRequiredMet != best.AllRequiredMet) return ev.AllRequiredMet;
            if (ev.Score != best.Score) return ev.Score > best.Score;
            return Math.Abs(ev.Zone.Price - price) < Math.Abs(best.Zone.Price - price);
        }

        private EngineState Snapshot(bool inRth)
        {
            var st = new EngineState { InRth = inRth };
            st.Prior = _sessions.Count > 0 ? _sessions[_sessions.Count - 1] : null;
            st.Ready = st.Prior != null;
            st.DailyShift = _dailyShift; st.Narrative = _narrative; st.Auction = _auction; st.Location = _location;
            st.Composition = _comp;
            string note;
            st.Final = BiasEngine.FinalBias(_narrative, _auction, _comp != null, out note);
            st.BiasNote = note;
            if (Lean) return st;
            if (inRth && st.Ready && _rth.Count > 0)
            {
                IbInfo ib;
                st.Zones = BuildZones(st.Prior, _rth[_rth.Count - 1].Close, out ib);
                st.Ib = ib;
                st.Gap = InitialBalance.ComputeGap(st.Prior.Close, SessionBars());
            }
            if (inRth && _rth.Count > 0)
            {
                var lastBar = _rth[_rth.Count - 1];
                st.LastPrice = lastBar.Close; st.LastBarTime = lastBar.Time;
                decimal pv = _vwapPv, v = _vwapV;
                if (_accum < _rth.Count - 1 && _rthKey[_rth.Count - 1] == _curKey)   // include the forming bar
                    foreach (var l in _rthLevels[_rth.Count - 1]) { pv += l.Price * (l.Bid + l.Ask); v += l.Bid + l.Ask; }
                st.Vwap = v > 0 ? pv / v : 0;
            }
            st.Map = BuildMap(st, inRth);
            return st;
        }

        /// <summary>Developing profile of the current session through bar k (completed bars plus bar k itself).</summary>
        private VolumeProfile DevelopingProfile(int k)
        {
            var dev = new VolumeProfile();
            dev.Merge(_curProfile);
            if (k >= 0 && k < _rth.Count && _accum < k && _rthKey[k] == _curKey) foreach (var l in _rthLevels[k]) dev.Add(l);
            return dev;
        }

        private MarketMap BuildMap(EngineState st, bool inRth)
        {
            var m = new MarketMap { ValueHistory = _valueHistory, PriorNodes = _priorNodes };
            if (_onFrozen) { m.Overnight = _onStats; if (_lonHas) { m.HasLondon = true; m.LondonHigh = _lonHighF; m.LondonLow = _lonLowF; } }
            else
            {
                if (_onCacheIndex != _ethIndex) { _onCacheIndex = _ethIndex; _onLive = OvernightLevels(); }
                m.Overnight = _onLive;
                if (_lonHigh > decimal.MinValue) { m.HasLondon = true; m.LondonHigh = _lonHigh; m.LondonLow = _lonLow; }
            }
            if (!inRth || _rth.Count == 0 || _curKey == null) return m;

            int k = _rth.Count - 1;
            var dev = DevelopingProfile(k);
            m.Developing = MarketMap.Levels(dev, _p.TickSize, _p.ValueAreaPct);
            m.DevelopingNodes = MarketMap.FindNodes(dev, _p.TickSize);
            decimal pv = _vwapPv, v = _vwapV, pv2 = _vwapPv2, cvd = _sessionCvd;
            if (_accum < k && _rthKey[k] == _curKey)
            {
                foreach (var l in _rthLevels[k]) { decimal vol = l.Bid + l.Ask; pv += l.Price * vol; v += vol; pv2 += l.Price * l.Price * vol; }
                cvd += _rth[k].Delta;
            }
            if (v > 0)
            {
                m.Vwap = pv / v;
                double var = (double)(pv2 / v - m.Vwap * m.Vwap);
                m.VwapSd = var > 0 ? (decimal)Math.Sqrt(var) : 0;
            }
            m.SessionCvd = cvd;
            m.DeltaLevels = SessionDeltaLevels(_rth[k].Close);
            m.Swing = MarketMap.SwingDeltaLevel(_rth, _rthLevels, _curFirst, k);
            if (st.Ib != null && st.Ib.Started) { m.IbHigh = st.Ib.High; m.IbLow = st.Ib.Low; m.IbComplete = st.Ib.Complete; m.IbBreakout = st.Ib.Breakout.ToString(); }
            return m;
        }
    }
}
