# Backtest before the trial

Goal: test Fabio's engine on years of real ES / NQ data **before** the ATAS trial starts, decide which set-ups and
settings to keep, and paste the chosen settings into ATAS on day 1.

## Can ATAS backtest 2020 (or 2022) to today?
Not realistically, for this strategy. ATAS gives two ways to test, both useful, neither for years of footprint:
- **The indicator on loaded history.** "Fabio Orderflow Monitor" calculates every bar ATAS has loaded and shows every
  set-up, trade and the totals ("History on this chart"). How far back ATAS loads depends on your data connection and
  the chart type (ATAS's own help says so), and footprint data is heavy. Expect months on one chart, not years.
- **Market Replay** replays past sessions tick by tick, so you can watch Laya work a past day. It is for one day at a
  time, not for statistics.

So the years-long test runs on the Mac with **fabio-backtest**. It uses the same engine code as the indicator. A test
checks that a history exported from the indicator gives the same trades in fabio-backtest as on the chart.

```
Databento (CME trades) --import--> data/ES.bars.gz --run / sweep--> reports/.../report.html --> Laya's review
ATAS indicator "Export history for backtest" ------^                                         --> settings line for ATAS
```

## Today: three steps (no ATAS needed)

### 1. Practise the commands (5 minutes, synthetic data)
```
cd ~/retreat-calculator/atas-fabio
./backtest.sh demo                                   # writes data/DEMO.bars.gz (SYNTHETIC, labelled as such)
./backtest.sh run --bars data/DEMO.bars.gz --from 2023-06-01
open reports/es-run/report.html
```
The first call builds the tool (about a minute). Synthetic numbers mean nothing: this only proves the steps work.

### 2. Get real data: Databento (paid, pay as you go)
Databento sells CME Globex history with the aggressor side of every trade, which the footprint and delta need. You
see the price before you pay.
```
pip3 install databento
export DATABENTO_API_KEY=db-...          # from the Databento portal; keep it out of files and chats
python3 tools/databento_fetch.py --symbol ES --start 2022-01-01 --end 2025-11-26          # price only
python3 tools/databento_fetch.py --symbol ES --start 2022-01-01 --end 2025-11-26 --buy    # buy + download
brew install zstd                        # the files come zstd-compressed
```
- **Range.** 2022 to today covers a bear year, two bull years and this year. Add 2020-2021 (the Covid crash and its
  rebound) if the price is fine: more regimes make a stronger test. Repeat with `--symbol NQ` for NQ.
- **Without the script (portal):** dataset GLBX.MDP3, schema **trades**, symbol **ES.FUT** (parent, so every contract
  and exact rolls), encoding **CSV**, **map symbols on**, split by **day**. The script was written for Databento's
  Python client but could not be run from here: check its price against the portal.
- The raw files are large (gigabytes per year). After the import you only need the bar file and can delete them.

### 3. Import, test, sweep, ask Laya
```
./backtest.sh import --databento data/databento/ES --instrument ES       # -> data/ES.bars.gz (about 1 million trades a second)
./backtest.sh run   --bars data/ES.bars.gz --from 2022-04-01             # the defaults
./backtest.sh sweep --bars data/ES.bars.gz --from 2022-04-01 --split 2024-07-01
python3 laya/review_backtest.py reports/es-sweep                          # Laya's reading + changes to test
open reports/es-sweep/report.html
```
- `--from 2022-04-01`: the first months of data warm up the 3-month composition, so trading starts after them.
- `--split`: the sweep ranks settings on the period before it (in-sample) and shows how they did after it
  (out-of-sample), on data they were not chosen on.
- Costs by default: 1 tick slippage on entries and stops, $4 round turn (ES / NQ; micros $1), at most 2 losing
  trades per session (like the strategy), flat at 16:00. Set your own: `--commission 4.50 --slippage-ticks 1`.
- Your own grid: `--grid "AbsorbDeltaMult=1.25,1.5;ZoneToleranceTicks=4,6"`. Every setting name is in `src/Core/FabioParams.cs`.

## Reading the report
- **R after costs.** 1R = the risk of the trade (entry to stop). +0.2R average over 300 trades = +60R.
- **By set-up** (VAL / VAH / POC, long / short): which of Fabio's set-ups carry the result. This is how you decide
  which to focus on.
- **By entry hour:** whether some times of day only lose (for example the first minutes after the open).
- **By year:** an edge that only exists in one year is not an edge.
- **Max drawdown and losing streak:** what you must be able to sit through live.

**My decision rules** (suggestions, not Fabio's):
1. At least about 100 trades overall, and 30 in any group you act on.
2. Keep a setting change only if it is better **out-of-sample** and its neighbours in the grid are good too (the
   "robust" column). The best in-sample line alone is usually luck.
3. Positive after costs in most years, not just one.
4. Fewer changes beat more: change one or two settings, not seven.

## Using the result in ATAS
Every report prints a **settings line**, for example `AbsorbDeltaMult=1.5;ZoneToleranceTicks=4`. In ATAS, paste it into
**"Engine settings from the backtest"** in both the indicator ("5. Backtest") and the strategy ("4. Rules"). The panel
confirms it, or shows **ENGINE SETTINGS NOT APPLIED** with the reason. ATAS then runs exactly the tested settings.

## Trial day 1: check on ATAS's own data
1. In the indicator, turn on **Export history for backtest** (after the chart has loaded as many days as it will).
   The panel shows `export: N bars -> ~/Documents/FabioBacktest/<instrument>_5m_<from>_<to>.bars.gz`. Turn it off again.
2. `./backtest.sh run --bars ~/Documents/FabioBacktest/<that file> --max-losers 0 --commission 0`
   With those two options it uses the indicator's own rules for its history totals. Its trades must match "History on
   this chart"; if not, send both to Claude.
3. Then the simulator stage of `SETUP_GUIDE.md` (Laya approving live set-ups).

## What this test cannot tell you
- **Laya is not in it.** It trades every set-up the engine finds; Laya can only remove trades. The simulator days
  show what her filter adds.
- **Bar-level fills.** Entry at the next bar's open plus slippage; when a bar touches both the stop and the target, the
  stop counts first (pessimistic).
- **Only the method in SKILL.md.** The models in the skill's missing reference files are not tested.
- **Big trades** are counted from single trade records, which can undercount compared with ATAS's grouped trades. They
  are information only and never decide a set-up.
- Past results do not promise future ones. Not financial advice.
