# Ready check: everything except ATAS

Everything below is installed by `bootstrap-mac.sh` and tested (`dotnet run --project tests/FabioOrderflow.Tests`,
`node app/tests/local_desk_test.cjs`). The only thing left is ATAS itself (the 14-day trial, section 3).

## 1. What runs, and what Laya watches on every candle
| Piece | What it does |
|---|---|
| **Fabio Orderflow Strategy** (inside ATAS) | Runs the Fabio engine on every bar and publishes each closed candle to Laya over a local bridge (port 8766); trades only set-ups Laya approves, with the stop, the loss limits and flat at 16:00; Trading OFF by default |
| **Fabio Orderflow Monitor** (inside ATAS) | The same engine on the chart: levels, set-ups and trades over all loaded history, history statistics, export for backtests |
| **Laya** (`laya/laya_runner.py`) | Reads every closed candle with the Fabio skill (Claude Code, your plan), builds the plan at 09:00 New York, approves or rejects every set-up |
| **Dashboard** (`./start-laya.sh` -> http://localhost:8770) | The tape with Laya's reads, chart, plan, market map, footprint, set-ups with Approve / Reject, Ask Laya, gamma levels, Backtests |
| **Backtests** (`./backtest.sh`, `./backtest-atas.sh`) | Each of Fabio's models alone and together, sweeps, in- and out-of-sample, Laya's review |

Every candle card Laya reads carries: the bar (delta and volume against average, body, close position); the **full
footprint** (bid x ask per price, imbalances); the footprint at the high and low; **big prints**; the **order book**
(walls, imbalance, pulled / eaten walls); the nearest zone and how price approached it; and the engine's checklist.
The state she reads with it carries: **bias** (value-area shifts, 5 sessions, 3-month composition); **prior and
developing POC / VAH / VAL**; value ladder; **volume nodes**; **VWAP** bands; **session delta**; **delta and swing
sniper levels**; **initial balance**; **overnight and London**; the **gap**; and the **gamma levels** you entered.

## 2. Before day 1 (5 minutes)
```
cd ~/retreat-calculator && git pull
cd atas-fabio && ./setup-mac.sh                # all ticks should be [x]
./start-laya.sh --practice                     # practice day on the dashboard; Ctrl+C to stop (about 20 Claude calls)
```
Optional, Laya always on like your other bots: `./laya-service.sh start` (then `pm2 startup` once for login).

## 3. Day 1: ATAS (about 45 minutes)
1. Install **ATAS X**, start the **ULTRA** trial, connect your **live CME data** (Rithmic / CQG / your broker).
2. Quit ATAS X, then: `cd ~/retreat-calculator/atas-fabio && ./install-mac.sh`. Red lines? Send them to Claude.
3. Open ATAS X: **ES** (or MES / NQ), **5 minutes**, **24-hour session**, **60 days** loaded, footprint (cluster) mode.
4. Add the indicator **Fabio Orderflow Monitor**. Check the panel: "cash session" during 09:30-16:00 New York.
5. Add the strategy **Fabio Orderflow Strategy** on the **demo** account: **Trading enabled OFF**, **Bridge for Laya
   enabled ON** (port 8766), **Require Laya approval before entering ON**. Start it.
6. `./start-laya.sh` (or the service): the dashboard's bridge pill turns green; Laya waits for 09:00 and 09:30.
7. Options levels (optional): read today's call wall / put wall / zero gamma off ATAS's options tools and enter them
   on the dashboard (**Options levels (gamma): enter today's from ATAS**, under the market map).
8. **Backtest ATAS's history right away:** in the indicator turn on **Export history for backtest**, then the
   dashboard's **Backtests -> Backtest the latest ATAS export** (or `./backtest-atas.sh`).

## 4. Every day
Before 09:00 New York: ATAS X with the chart, indicator and strategy; `./start-laya.sh`; Mac plugged in and awake.
09:00 read Laya's plan; 09:30-16:00 watch the tape and her alerts; after 16:00 look at the set-ups and her reads.
Days 1-5 Trading OFF (watch); days 6-14 Trading ON on the demo account; live only after the trial, small size.

## 5. Honest limits
- Laya reads each candle when it **closes** (10-60 s later), not inside a forming candle.
- The engine's automatic set-ups cover the core model (value-area zones + battle of orders) and a provisional IVB.
  The skill's other models are added on the Mac from `brain/04` (`MODELS_GUIDE.md`); Laya already uses them in her reads.
- Gamma and the order book are context, not Fabio's rules. The thresholds are starting values to tune with the backtests.
- Claude usage: about 78 reads per 5-minute session; at your plan's limit her reads fail and pending set-ups are not
  traded (safe). `./start-laya.sh --model sonnet` stretches it.
- Not yet compiled against your ATAS X or run on live data: step 3.2 does the compile. Not financial advice.
