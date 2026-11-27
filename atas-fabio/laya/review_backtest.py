#!/usr/bin/env python3
"""
Laya reviews a backtest: reads the summary that `fabio-backtest run` / `sweep` wrote, asks Laya (Claude Code with the
Fabio skill) what it shows and which changes within Fabio's method are worth testing, and writes laya_review.md next to
the report. One Claude call, on your plan.

  python3 laya/review_backtest.py reports/es-sweep
  python3 laya/review_backtest.py reports/es-run --model opus

Standard library only. Needs Claude Code logged in (`claude` on PATH).
"""
import argparse
import json
import shlex
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from laya_runner import ask_laya  # noqa: E402  (same Claude Code call as the per-candle reads)

INSTRUCTIONS = HERE / "LAYA_BACKTEST_INSTRUCTIONS.md"


def build_prompt(summary: dict, skill_command: str) -> str:
    keep = {k: summary.get(k) for k in ("data", "costs", "settings_changed", "signals", "skipped", "overall", "by_year",
                                        "by_setup", "by_hour", "by_exit", "sweep", "caveats")}
    months = summary.get("by_month") or []
    keep["by_month_worst"] = sorted(months, key=lambda m: m.get("total_r", 0))[:6]
    keep["by_month_best"] = sorted(months, key=lambda m: -m.get("total_r", 0))[:6]
    head = (skill_command.strip() + "\n\n") if skill_command else ""
    return (head + INSTRUCTIONS.read_text(encoding="utf-8") + "\n\n---\nBACKTEST_SUMMARY:\n"
            + json.dumps(keep, separators=(",", ":"), default=str) + "\n\nReply with only the JSON object described above.")


def to_markdown(r: dict, summary: dict) -> str:
    d = summary.get("data", {})
    o = summary.get("overall", {})
    lines = ["# Laya's review of the backtest", ""]
    if d.get("synthetic"):
        lines += ["> **Synthetic data: the numbers say nothing about the strategy.**", ""]
    lines += [f"{d.get('instrument', '?')} {d.get('first_bar', '')[:10]} to {d.get('last_bar', '')[:10]}: "
              f"{o.get('trades', 0)} trades, win {round(100 * float(o.get('win_rate', 0)))}%, "
              f"{o.get('total_r', 0)}R after costs, max drawdown {o.get('max_drawdown_r', 0)}R.", ""]
    lines += ["**Verdict.** " + str(r.get("verdict", "")), "", "**Data.** " + str(r.get("data_quality", "")), ""]
    for title, key in (("What works", "what_works"), ("What fails", "what_fails")):
        items = r.get(key) or []
        if items:
            lines += [f"## {title}", ""] + [f"- {x}" for x in items] + [""]
    lines += ["## Focus", "", str(r.get("focus", "")), ""]
    sugg = r.get("suggestions") or []
    if sugg:
        lines += ["## Changes worth testing", ""]
        for i, s in enumerate(sugg, 1):
            lines += [f"{i}. **{s.get('change', '')}**", f"   - Why: {s.get('why', '')}", f"   - Fabio: {s.get('fabio_basis', '')}"]
            if s.get("test_grid"):
                lines += [f"   - Test: `./backtest.sh sweep --bars <your bar file> --grid \"{s['test_grid']}\"`"]
        lines += [""]
    lines += ["## What Laya would filter live", "", str(r.get("live_filter", "")), "",
              "## Next step", "", str(r.get("next_step", "")), "",
              "_Laya's reading of a backtest; not financial advice._"]
    return "\n".join(lines) + "\n"


def main() -> int:
    ap = argparse.ArgumentParser(description="Laya reviews a fabio-backtest report with the Fabio skill.")
    ap.add_argument("report_dir", help="folder written by fabio-backtest run / sweep (contains summary.json)")
    ap.add_argument("--claude", default="claude", help="command that runs Claude Code (default: claude)")
    ap.add_argument("--model", default="", help="sonnet, opus ... (default: your Claude Code default)")
    ap.add_argument("--skill-command", default="", help="e.g. /favio-orderflow-scalp to force-load the skill")
    ap.add_argument("--max-seconds", type=float, default=300)
    a = ap.parse_args()

    folder = Path(a.report_dir).expanduser()
    path = folder / "summary.json"
    if not path.exists():
        print(f"no summary.json in {folder}: run ./backtest.sh run (or sweep) first", file=sys.stderr)
        return 1
    summary = json.loads(path.read_text(encoding="utf-8"))
    print("Laya is reading the backtest (one Claude call, up to a few minutes) ...")
    try:
        review = ask_laya(shlex.split(a.claude), build_prompt(summary, a.skill_command), a.model, a.max_seconds)
    except Exception as e:  # noqa: BLE001 - report any failure of the Claude call plainly
        print(f"Laya could not review it: {e}", file=sys.stderr)
        return 2
    (folder / "laya_review.json").write_text(json.dumps(review, indent=2), encoding="utf-8")
    md = to_markdown(review, summary)
    (folder / "laya_review.md").write_text(md, encoding="utf-8")
    print(md)
    print(f"saved: {folder / 'laya_review.md'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
