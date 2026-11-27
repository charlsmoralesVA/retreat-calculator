#!/usr/bin/env python3
"""Stand-in for `claude -p` in tests: reads the runner's prompt from stdin and answers like Laya would (JSON)."""
import json, re, sys, os
prompt = sys.stdin.read()
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
        reviews.append({"seq": c["seq"], "phase": "setup_long", "action": "alert_long", "confidence": 75,
                        "read": f"Buyers won the body at {ctx.get('nearest_zone')} after sellers were absorbed.", "next": "hold above the zone"})
    elif (ctx.get("distance_to_zone_ticks") or 99) < 8:
        reviews.append({"seq": c["seq"], "phase": "approaching_zone", "action": "watch", "confidence": 40,
                        "read": f"Price near {ctx.get('nearest_zone')}; watching who wins.", "next": "absorption at the zone"})
    else:
        reviews.append({"seq": c["seq"], "phase": "away_from_zones", "action": "wait", "confidence": 10,
                        "read": "In the middle, no zone: nothing to do.", "next": ""})
decisions = [{"id": i, "approve": True, "reason": "Fake Laya: three pillars line up."} for i in ids]
print(json.dumps({"type": "result", "subtype": "success", "result": json.dumps({"reviews": reviews, "setup_decisions": decisions})}))
