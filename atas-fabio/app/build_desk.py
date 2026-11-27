#!/usr/bin/env python3
"""Builds laya-desk.html (the published app) from laya-desk.src.html + example-data.json."""
import json
from pathlib import Path
here = Path(__file__).resolve().parent
data = json.dumps(json.loads((here / "example-data.json").read_text()), separators=(",", ":")).replace("<", "\\u003c")
html = (here / "laya-desk.src.html").read_text().replace("__EXAMPLE_DATA__", data)
(here / "laya-desk.html").write_text(html)
print("wrote laya-desk.html", len(html), "bytes")
