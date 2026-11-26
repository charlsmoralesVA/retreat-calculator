#!/usr/bin/env python3
"""
End-to-end check of Laya's tool chain, exactly as Claude uses it:
  MCP client -> fabio_atas_mcp.py (stdio) -> bridge (ATAS strategy or fabio-bridge-demo) on localhost.

  python3 check_connection.py                 status only (safe with ATAS running)
  python3 check_connection.py --decide        also waits for a set-up and APPROVES it (use with fabio-bridge-demo only)
Environment: FABIO_BRIDGE_URL / FABIO_BRIDGE_TOKEN, as for the MCP server.
"""
import asyncio
import json
import os
import sys
from pathlib import Path

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client

HERE = Path(__file__).resolve().parent


def payload(result) -> dict:
    sc = getattr(result, "structuredContent", None) or getattr(result, "structured_content", None)
    if isinstance(sc, dict):
        return sc.get("result", sc) if set(sc.keys()) == {"result"} else sc
    for c in result.content:
        text = getattr(c, "text", None)
        if text:
            try:
                return json.loads(text)
            except json.JSONDecodeError:
                return {"text": text}
    return {}


async def main(decide: bool) -> int:
    params = StdioServerParameters(command=sys.executable, args=[str(HERE / "fabio_atas_mcp.py")], env=dict(os.environ))
    async with stdio_client(params) as (read, write):
        async with ClientSession(read, write) as session:
            await session.initialize()
            tools = sorted(t.name for t in (await session.list_tools()).tools)
            print("tools:", ", ".join(tools))

            status = payload(await session.call_tool("atas_status", {}))
            if not status.get("ok"):
                print("NOT CONNECTED:", status.get("error"))
                return 1
            st = status["state"]
            print("connected. instrument:", st.get("instrument"), "| ready:", st.get("ready"),
                  "| bias:", (st.get("bias") or {}).get("final"))
            if not decide:
                return 0

            print("waiting for a set-up ...")
            for _ in range(10):
                w = payload(await session.call_tool("wait_for_setup", {"timeout_seconds": 30}))
                if w.get("setups"):
                    break
            if not w.get("setups"):
                print("no set-up arrived")
                return 1
            t = w["setups"][0]
            s = t["setup"]
            print(f"set-up {t['id']}: {s['side']} {s['tag']} entry {s['entry']} stop {s['stop']} target {s['target']}")
            d = payload(await session.call_tool("decide_setup", {"setup_id": t["id"], "approve": True,
                                                                  "reason": "connection check: approved by check_connection.py"}))
            print("decision accepted:", d.get("ok"), d.get("error", ""))
            await asyncio.sleep(2)
            j = payload(await session.call_tool("journal", {"limit": 10}))
            if not j.get("events"):
                print("journal: not available (" + str(j.get("error", "the replay may have finished")) + ")")
            for e in j.get("events", [])[-4:]:
                print("journal:", e["event"], "-", (e.get("data") or {}).get("outcome") or (e.get("data") or {}).get("reason", ""))
            prompt = await session.get_prompt("fabio_review")
            print("prompt fabio_review:", len(prompt.messages), "message(s)")
            return 0 if d.get("ok") else 1


if __name__ == "__main__":
    sys.exit(asyncio.run(main("--decide" in sys.argv)))
