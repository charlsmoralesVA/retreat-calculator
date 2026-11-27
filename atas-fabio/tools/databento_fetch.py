#!/usr/bin/env python3
"""
Get CME trades from Databento for the backtest: shows the PRICE FIRST, buys only with --buy, then downloads.

  pip3 install databento
  export DATABENTO_API_KEY=db-...                    (from your Databento account page; never put it in a file here)
  python3 tools/databento_fetch.py --symbol ES --start 2022-01-01 --end 2025-11-26           # price only
  python3 tools/databento_fetch.py --symbol ES --start 2022-01-01 --end 2025-11-26 --buy     # submit + download
  ./backtest.sh import --databento data/databento/ES --instrument ES

It asks for the parent symbol (ES.FUT: every ES contract, so contract rolls are exact), the "trades" schema of
GLBX.MDP3 (CME Globex), CSV with symbols mapped, one file per day, zstd-compressed (`brew install zstd` to read them).
Written against Databento's Python client (databento.Historical: metadata.get_cost, batch.submit_job, batch.download);
not run from here, so check the price it prints against the Databento portal before buying.
"""
import argparse
import os
import sys
import time
from pathlib import Path


def main() -> int:
    ap = argparse.ArgumentParser(description="Price, buy and download Databento CME trades for fabio-backtest.")
    ap.add_argument("--symbol", default="ES", help="product root: ES, NQ, MES, MNQ (default ES)")
    ap.add_argument("--start", required=True, help="first day, YYYY-MM-DD")
    ap.add_argument("--end", required=True, help="day after the last, YYYY-MM-DD")
    ap.add_argument("--out", default="data/databento", help="download folder (default data/databento/<symbol>)")
    ap.add_argument("--buy", action="store_true", help="submit the job (this costs money) and download it")
    ap.add_argument("--job", default="", help="download an already submitted job id")
    a = ap.parse_args()

    key = os.environ.get("DATABENTO_API_KEY", "")
    if not key:
        print("Set DATABENTO_API_KEY first (Databento portal -> API keys).", file=sys.stderr)
        return 1
    try:
        import databento as db
    except ImportError:
        print("Install the client first: pip3 install databento", file=sys.stderr)
        return 1

    client = db.Historical(key)
    out = Path(a.out).expanduser() / a.symbol.upper()
    req = dict(dataset="GLBX.MDP3", symbols=[a.symbol.upper() + ".FUT"], stype_in="parent", schema="trades",
               start=a.start, end=a.end)

    if not a.job:
        cost = client.metadata.get_cost(**req)
        size = client.metadata.get_billable_size(**req)
        print(f"{a.symbol.upper()}.FUT trades {a.start} -> {a.end}: about ${cost:,.2f} "
              f"({size / 1e9:,.1f} GB billable, uncompressed)")
        if not a.buy:
            print("Nothing bought. Add --buy to submit the job.")
            return 0
        job = client.batch.submit_job(**req, encoding="csv", compression="zstd", split_duration="day",
                                      map_symbols=True, pretty_px=False, pretty_ts=False)
        job_id = job["id"] if isinstance(job, dict) else getattr(job, "id", job)
        print(f"submitted job {job_id}; waiting for Databento to prepare the files ...")
    else:
        job_id = a.job

    while True:
        jobs = client.batch.list_jobs(states="done")
        if any((j.get("id") if isinstance(j, dict) else getattr(j, "id", None)) == job_id for j in jobs):
            break
        time.sleep(30)
        print("  still preparing ...")
    out.mkdir(parents=True, exist_ok=True)
    files = client.batch.download(job_id=job_id, output_dir=out)
    print(f"downloaded {len(files)} files to {out}")
    print(f"next: ./backtest.sh import --databento {out} --instrument {a.symbol.upper()}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
