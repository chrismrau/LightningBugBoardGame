#!/usr/bin/env python3
"""Re-inject GameBoard.labels.json into GameBoard.svg (labels group only)."""
import json, re
from pathlib import Path

HERE = Path(__file__).resolve().parent
labels = json.loads((HERE / "GameBoard.labels.json").read_text())["labels"]
svg_path = HERE / "GameBoard.svg"
svg = svg_path.read_text()
svg = re.sub(r'\n?<g id="sector-labels"[^>]*>.*?</g>\s*', "\n", svg, flags=re.S)
lines = [
  '  <g id="sector-labels" data-source="GameBoard.labels.json" data-note="best-effort labels from Sectors.json + board art">',
  '    <style type="text/css"><![CDATA[',
  '      #sector-labels text { font-family: DejaVu Sans, Arial, sans-serif; paint-order: stroke; stroke: #000; stroke-width: 3px; stroke-linejoin: round; }',
  '      #sector-labels .lbl-planet { fill: #ffe566; font-size: 22px; font-weight: 700; }',
  '      #sector-labels .lbl-empty { fill: #7ec8e3; font-size: 16px; font-weight: 600; }',
  '      #sector-labels .lbl-space { fill: #c9a0ff; font-size: 15px; font-weight: 600; }',
  '      #sector-labels .lbl-id { fill: #c8d0d8; font-size: 12px; font-weight: 500; }',
  '    ]]></style>',
]
for sid, L in sorted(labels.items(), key=lambda kv: (kv[1]["zone"], kv[1]["ring"], kv[1]["index"], kv[0])):
    if L.get("planet"):
        cls = "lbl-planet"
    elif L["zone"] in ("Border Space", "Rim Space"):
        cls = "lbl-space"
    else:
        cls = "lbl-empty"
    lines.append(
        f'    <text id="label-{sid}" data-sector-id="{sid}" data-confidence="{L["confidence"]}" '
        f'class="{cls}" x="{L["x"]}" y="{L["y"]}" text-anchor="middle">{L["label"]}</text>'
    )
    if L.get("planet"):
        lines.append(
            f'    <text data-sector-id="{sid}" class="lbl-id" x="{L["x"]}" y="{L["y"] + 16}" '
            f'text-anchor="middle">{L["shortId"]}</text>'
        )
lines.append("  </g>")
block = "\n".join(lines) + "\n"
svg = svg.rstrip()
if not svg.endswith("</svg>"):
    raise SystemExit("GameBoard.svg missing closing </svg>")
svg_path.write_text(svg[:-6] + block + "</svg>\n")
print(f"Injected {len(labels)} labels into {svg_path.name}")
