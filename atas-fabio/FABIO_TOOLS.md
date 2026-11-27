# Fabio's tools: what ATAS provides and what Laya watches

Source for "what Fabio uses": the `fabio-orderflow-strategy` skill (`SKILL.md`, the only part of the skill that is synced
here; its `references/` files are missing). Source for ATAS plans: atas.net/pricing, checked during this build. Check
the pricing page again when you start the trial, because plans change.

**Real time, here, means:** the engine (inside ATAS) updates every level on every bar, and big prints as they trade.
Laya reads **every closed candle** with the whole market map, about 10-60 s after the close. She does not read inside a
forming candle; a Claude call per tick is not possible.

## 1. Gamma and delta

| You asked about | What it is | In ATAS? | In Fabio's method? | Laya |
|---|---|---|---|---|
| **Delta (order flow)** | Aggressive buys minus aggressive sells: per bar, per price (footprint), cumulative | **Yes, every plan** (footprint, bar delta, Cumulative Delta, delta profiles) | **Yes, it is the core of the confirmation**: absorption vs aggression, delta / sniper levels | **Yes, every candle** |
| **Gamma (options, GEX)** | Dealers' options gamma exposure per strike | Options Chain Suite (GEX profile, OI profile, key levels, expected move): **PRO and up**, needs a **Rithmic or Interactive Brokers** connection. Options X-Ray (GEX heatmap, strike profile): **ULTRA only** | **No.** The skill never mentions gamma, GEX or options | Not wired |
| **Delta (options, DEX)** | Dealers' options delta exposure per strike | Options X-Ray Suite (strike profile GEX / DEX / VEX / CEX, flow delta): **ULTRA only** | **No** | Not wired |

Adding gamma to Laya's rules would no longer be Fabio's method. It could be shown to her as extra context only. That
would need ULTRA or an options-data connection. It would also need a check that ATAS lets a custom indicator read those
values, which I have not verified.

## 2. Fabio's tools, one by one

Legend:
- **Engine:** **Rule** = required before a set-up; **Confluence** = shown and scored, not required; **Map** = a reference level handed to Laya; **No** = not built.
- **Laya:** what she sees on every candle.

### Pillar 1: bias (direction)
| Tool (how Fabio uses it) | ATAS plan | Engine | Laya |
|---|---|---|---|
| Daily volume profile of the cash session: value higher / lower / overlapping day over day | All plans (Market Profile, Fixed / Custom profiles) | **Rule** (`BiasEngine`) | Yes: bias + value ladder |
| Multi-session narrative (5 sessions or more) | All plans | **Rule** | Yes: `value_history` |
| 3-month composition profile; the auction flips only on acceptance back inside it | All plans (load 60 days or more) | **Rule**: a long against the long-term auction is blocked | Yes: `composition_3_month` |
| Stacked profiles (acceptance below VAL / above the swing point that started the move) | All plans | **No**: rules are in the missing reference files | Sees the levels; no rule |

### Pillar 2: location (zones)
| Tool | ATAS plan | Engine | Laya |
|---|---|---|---|
| Prior VAH (sellers defend) / VAL (buyers defend) | All plans | **Rule**: zones | Yes |
| POC: magnet while balanced, rejector after acceptance outside value | All plans | **Rule** | Yes |
| Today's developing POC / VAH / VAL | All plans (Dynamic POC / developing levels) | **Map** | Yes |
| High / low volume nodes | All plans | **Map** (thresholds are mine) | Yes |
| Delta / sniper levels: balanced = only outside value; swing = the strongest level even inside value | All plans (drag a profile with delta over the swing) | **Confluence** (session levels) + **Map** (swing sniper level, new) | Yes: `delta_levels_today`, `swing_delta_level` |
| VWAP | All plans (VWAP, Anchored VWAP) | **Map**: session VWAP with ±1 / ±2 SD bands (the bands are my addition) | Yes |
| Pre-market mapping: London + previous day, overnight profile, last line of defense | All plans (needs the 24-hour session on the chart) | **Map**: overnight high / low / POC, London high / low. Profile shape (balanced / P / V) is not classified | Yes: she builds the day's plan at 09:00 New York |
| Initial balance / IVB breakout | All plans | **Map**: IB high / low / breakout. The IVB model rules (Model 1 / 2, statistical target) are in the missing reference; a provisional version is off by default | Yes: levels only |
| Gap from the prior close, gap fill | All plans | **Map**: size, filled or open. The gap-model rules are missing | Yes |

### Pillar 3: confirmation (battle of orders)
| Tool | ATAS plan | Engine | Laya |
|---|---|---|---|
| Delta per candle against recent candles | All plans (footprint / bar delta) | **Rule** | Yes |
| Absorption: big effort, no result | All plans | **Rule** | Yes |
| The other side wins in the body of the candle (closes their way) | All plans | **Rule** | Yes |
| Effort vs result on the approach (the reactor filter: struggle = good, effortless explosion = skip) | All plans | **Rule** | Yes |
| Footprint at the candle's high / low (the losing side's heavy prints at the extreme) | All plans (cluster / footprint chart) | **Confluence** | Yes: bid / ask share and delta at the high and low |
| Big trades ("bubbles"): about 60-75 contracts on 5 minutes; scale up if more than 3 per candle | **PLUS and up** (Big Trades) | **Confluence**: filter 60, auto-scaled; live only, not in history | Yes: `big_buys`, `big_sells` |
| Order book / heatmap (resting bids and offers) | All plans (DOM, Heatmap) | **Context** (not in SKILL.md, so never a rule): walls near price, imbalance, walls pulled or eaten since the last candle; live only | Yes: `liquidity` on every live candle; on the dashboard tape |
| Session cumulative delta | All plans (Cumulative Delta) | **Map** | Yes |
| Stop just beyond the zone; target at the prior control level / POC / value edge, or about 1:2 | Order management with real futures: **PRO and up** | **Rule**: bracket order, flat at 16:00 | Yes: sees entry / stop / target, approves or rejects |
| Trapped participants (reload vs rejection) | All plans | **No** (reference file) | Can read it from the footprint; no rule |

### Supplementary (deep-charts) and the trend models
| Tool | ATAS plan | Engine | Laya |
|---|---|---|---|
| Delta print | All plans have the data | **No**: definition is in the missing reference | No |
| Deep-effort / best-effort algo, path of least resistance, range charts (40 / 80 range) | Range charts: **PLUS and up** | **No**; the engine is tested on 5-minute bars only | No |
| Momentum, reload-trend, locked-value and stair models; band squeeze; explosion rule; volume ledge | All plans have the data | **Not yet**: the model framework is ready (`MODELS_GUIDE.md`); the rules are in the new skill's `brain/04`, on the Mac | Yes, in her reads: she has the new skill |

## 3. Plans

**PRO covers every tool Fabio uses**: real-time futures data, real futures orders, all profiles, footprint, delta, Big
Trades, range charts, Market Replay, 12 indicators per chart (this setup needs about 5).

**Your plan: ULTRA.** It has everything PRO has. Its extras, and how this setup uses them:
- **AI Connect:** Laya draws her plan on the ATAS chart and reviews the day inside ATAS (`laya/LAYA_AI_CONNECT.md`,
  setup guide 2.8). The per-candle reads stay with the runner and bridge.
- **Indicators per chart:** no limit.
- **Options X-Ray (GEX / DEX):** for your own eyes only; not Fabio's method, so not given to Laya.
- **MBO data, extended API:** not used.

START and PLUS show futures on a 15-minute delay, so they cannot be used to trade this.

## 4. On the ATAS chart, to see what Laya sees
5-minute ES / NQ, 24-hour session, 60 days loaded, footprint (cluster) chart mode, plus:
- Fabio Orderflow Monitor (all the levels above, set-ups, panel)
- Cumulative Delta
- Big Trades (filter 60)
- optional: Market Profile with delta, to drag over a swing yourself and check Laya's sniper level

## 5. What limits accuracy today
1. **The engine's models.** Laya reads every candle with the full new skill (`favio-orderflow-scalp`, on the Mac), so
   she can apply all of Fabio's models in her reads. The engine (automatic set-ups, backtests) has ZoneReaction and a
   provisional IVB so far; each further model is added from the skill's `brain/04` chapter by Claude on the Mac,
   following `MODELS_GUIDE.md`, then compared and tuned with `./backtest.sh models` / `sweep`.
2. **The thresholds are mine, not Fabio's**: absorption and confirmation multipliers, zone tolerance, node detection,
   which swing to profile. Tune them on the trial with the history totals.
3. **Not yet compiled against the real ATAS** or run on real market data; that happens on your Mac on trial day 1.
4. Big prints are live only: past days on the chart show set-ups without them.

Not financial advice.
