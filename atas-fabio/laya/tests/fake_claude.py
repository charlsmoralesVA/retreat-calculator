#!/usr/bin/env python3
"""Stand-in for `claude -p` in tests: reads the runner's prompt from stdin and answers like Laya would (JSON)."""
import json, re, sys, os
prompt = sys.stdin.read()
if "PREMARKET_SESSION:" in prompt:
    session = re.search(r"PREMARKET_SESSION: (\S+)", prompt).group(1)
    plan = {"session": session, "bias": "bullish", "bias_reason": "Fake Laya: value migrating higher for five sessions.",
            "context": "Overnight held inside prior value.", "key_levels": [{"price": 4517.0, "name": "prior VAL", "why": "fade long"}],
            "scenarios": [{"name": "VAL fade", "if": "sellers absorbed at VAL", "then": "long", "model": "value-area fade", "levels": [4517.0]}],
            "no_trade": ["middle of value"], "summary": "Bullish; buy absorption at VAL; stay out around the POC."}
    print(json.dumps({"type": "result", "subtype": "success", "result": json.dumps(plan)}))
    sys.exit(0)
if "favio-orderflow-scalp skill" in prompt and "list every file" in prompt:
    print(json.dumps({"type": "result", "subtype": "success", "result": json.dumps({"skill_found": True, "skill_name": "favio-orderflow-scalp", "files": ["SKILL.md", "PLAYBOOK.md", "brain/04_models_and_entries.md"], "note": "fake"})}))
    sys.exit(0)
if "BACKTEST_SUMMARY:" in prompt:
    data = json.loads(prompt.split("BACKTEST_SUMMARY:\n", 1)[1].split("\n\nReply with only", 1)[0])
    synthetic = data.get("data", {}).get("synthetic")
    review = {"verdict": ("Synthetic data: the pipeline runs, the numbers mean nothing." if synthetic else "Fake review."),
              "data_quality": "synthetic" if synthetic else "ok", "what_works": ["pipeline end to end"], "what_fails": [],
              "focus": "not enough evidence yet",
              "suggestions": [{"change": "tighter location", "why": "fake", "fabio_basis": "Assumed: zone tolerance is not given",
                               "test_grid": "ZoneToleranceTicks=4,6"}],
              "live_filter": "reject set-ups in the middle of value", "next_step": "import real data"}
    print(json.dumps({"type": "result", "subtype": "success", "result": json.dumps(review)}))
    sys.exit(0)
log = os.environ.get("FAKE_CLAUDE_LOG")
if log:
    with open(log, "a") as f:
        f.write(json.dumps({"argv": sys.argv[1:], "chars": len(prompt), "has_instructions": "Fabio" in prompt}) + "\n")
seqs = json.loads(re.search(r"NEW_CANDLE_SEQS: (\[.*?\])", prompt).group(1))
ids = json.loads(re.search(r"PENDING_SETUP_IDS: (\[.*?\])", prompt).group(1))
data = json.loads(prompt.split("DATA:\n", 1)[1].split("\n\nReply with only", 1)[0])
reviews = []
for c in data["new"]:
    ctx = c.get("context", {})
    if c.get("engine_signal"):
        reviews.append({"seq": c["seq"], "phase": "setup_long", "action": "alert_long", "confidence": 75, "model": "value-area fade",
                        "read": f"Buyers won the body at {ctx.get('nearest_zone')} after sellers were absorbed.", "next": "hold above the zone"})
    elif (ctx.get("distance_to_zone_ticks") or 99) < 8:
        reviews.append({"seq": c["seq"], "phase": "approaching_zone", "action": "watch", "confidence": 40,
                        "read": f"Price near {ctx.get('nearest_zone')}; watching who wins.", "next": "absorption at the zone"})
    else:
        reviews.append({"seq": c["seq"], "phase": "away_from_zones", "action": "wait", "confidence": 10,
                        "read": "In the middle, no zone: nothing to do.", "next": ""})
decisions = [{"id": i, "approve": True, "reason": "Fake Laya: three pillars line up."} for i in ids]
print(json.dumps({"type": "result", "subtype": "success", "result": json.dumps({"reviews": reviews, "setup_decisions": decisions})}))
