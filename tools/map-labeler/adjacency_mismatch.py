#!/usr/bin/env python3
"""Compare board-geometry neighbors (from SectorLayout) to Adjacency.json."""

from __future__ import annotations

import argparse
import json
import math
from collections import Counter, defaultdict
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_LAYOUT = REPO_ROOT / "Data" / "Map" / "SectorLayout.json"
DEFAULT_ADJ = REPO_ROOT / "Data" / "Map" / "Adjacency.json"
DEFAULT_SECTORS = REPO_ROOT / "Data" / "Map" / "Sectors.json"
DEFAULT_OUT_JSON = Path(__file__).resolve().parent / "adjacency-mismatch-report.json"
DEFAULT_OUT_MD = Path(__file__).resolve().parent / "adjacency-mismatch-report.md"
DEFAULT_FALSE_POSITIVES = Path(__file__).resolve().parent / "geometry-false-positives.json"


def load_false_positive_edges(path: Path) -> set[tuple[str, str]]:
    if not path.is_file():
        return set()
    doc = json.loads(path.read_text())
    out = set()
    for e in doc.get("edges") or []:
        out.add(edge_key(e["a"], e["b"]))
    return out


def edge_key(a: str, b: str) -> tuple[str, str]:
    return (a, b) if a < b else (b, a)


def pt_key(p, tol: float) -> tuple:
    return (round(p[0] / tol) * tol, round(p[1] / tol) * tol)


def dist_point_to_seg(px, py, ax, ay, bx, by) -> float:
    abx, aby = bx - ax, by - ay
    apx, apy = px - ax, py - ay
    ab2 = abx * abx + aby * aby
    if ab2 <= 1e-12:
        return math.hypot(apx, apy)
    t = max(0.0, min(1.0, (apx * abx + apy * aby) / ab2))
    return math.hypot(px - (ax + t * abx), py - (ay + t * aby))


def min_dist_point_to_ring(pt, ring) -> float:
    best = float("inf")
    n = len(ring)
    for i in range(n):
        a = ring[i]
        b = ring[(i + 1) % n]
        best = min(best, dist_point_to_seg(pt[0], pt[1], a[0], a[1], b[0], b[1]))
    return best


def build_geometric_neighbors(sectors_geo: dict, tol: float = 3.0) -> dict[str, set[str]]:
    """sectorId -> neighbors sharing a boundary.

    Combines:
    - ≥2 shared snapped vertices (robust for long shared arcs)
    - shared-edge probe: both endpoints of a boundary segment lie within
      ``tol`` of the other ring (catches short/radial joins lost to simplify)
    """
    rings = {sid: (geo.get("points") or []) for sid, geo in sectors_geo.items()}
    sector_verts = {sid: {pt_key(p, tol) for p in pts} for sid, pts in rings.items()}
    ids = sorted(rings)
    nbrs: dict[str, set[str]] = defaultdict(set)

    for i, a in enumerate(ids):
        ra = rings[a]
        va = sector_verts[a]
        if len(ra) < 2:
            continue
        for b in ids[i + 1 :]:
            rb = rings[b]
            vb = sector_verts[b]
            if len(rb) < 2:
                continue
            shared = va & vb
            adjacent = len(shared) >= 2
            if not adjacent:
                # Probe A's segments against B's boundary.
                for k in range(len(ra)):
                    p = ra[k]
                    q = ra[(k + 1) % len(ra)]
                    if math.hypot(p[0] - q[0], p[1] - q[1]) < tol * 0.5:
                        continue
                    if (
                        min_dist_point_to_ring(p, rb) <= tol
                        and min_dist_point_to_ring(q, rb) <= tol
                    ):
                        adjacent = True
                        break
            if adjacent:
                nbrs[a].add(b)
                nbrs[b].add(a)
    return nbrs


def build_json_neighbors(edges: list) -> dict[str, set[str]]:
    nbrs: dict[str, set[str]] = defaultdict(set)
    for e in edges:
        a, b = e["a"], e["b"]
        nbrs[a].add(b)
        nbrs[b].add(a)
    return nbrs


def undirected_edge_set(nbrs: dict[str, set[str]]) -> set[tuple[str, str]]:
    out = set()
    for a, bs in nbrs.items():
        for b in bs:
            out.add(edge_key(a, b))
    return out


def run(
    layout_path: Path,
    adj_path: Path,
    sectors_path: Path,
    tol: float = 3.0,
    false_positives_path: Path = DEFAULT_FALSE_POSITIVES,
) -> dict:
    layout = json.loads(layout_path.read_text())
    adj = json.loads(adj_path.read_text())
    sectors_doc = json.loads(sectors_path.read_text())
    sectors = {s["id"]: s for s in sectors_doc["sectors"]}
    false_positives = load_false_positive_edges(false_positives_path)

    geo = layout.get("sectors") or {}
    assignments = layout.get("assignments") or {}
    if not geo and assignments:
        # Rebuild sector-keyed geometry from faces if needed (not expected).
        raise SystemExit("SectorLayout.json missing sectors{} geometry; re-save from the labeler.")

    labeled = set(geo.keys())
    all_ids = set(sectors.keys())
    unlabeled = sorted(all_ids - labeled)
    unknown_labels = sorted(labeled - all_ids)

    geo_nbrs_raw = build_geometric_neighbors(geo, tol=tol)
    json_nbrs = build_json_neighbors(adj.get("edges") or [])
    json_edges_all = undirected_edge_set(json_nbrs)

    suppressed = sorted(
        false_positives & (undirected_edge_set(geo_nbrs_raw) - json_edges_all)
    )

    # Strip user-confirmed geometry false positives from geo neighbor sets.
    geo_nbrs: dict[str, set[str]] = defaultdict(set)
    for a, bs in geo_nbrs_raw.items():
        for b in bs:
            if edge_key(a, b) in false_positives:
                continue
            geo_nbrs[a].add(b)

    geo_edges = undirected_edge_set(geo_nbrs)
    missing = sorted(geo_edges - json_edges_all)
    extra = sorted(json_edges_all - geo_edges)
    def edge_row(a: str, b: str) -> dict:
        sa, sb = sectors.get(a, {}), sectors.get(b, {})
        return {
            "a": a,
            "b": b,
            "aPlanet": sa.get("planet") or sa.get("displayName"),
            "bPlanet": sb.get("planet") or sb.get("displayName"),
            "aZone": sa.get("zone"),
            "bZone": sb.get("zone"),
        }

    missing_rows = [edge_row(a, b) for a, b in missing]
    extra_rows = [edge_row(a, b) for a, b in extra]
    suppressed_rows = [edge_row(a, b) for a, b in suppressed]

    # Per-sector degree deltas for triage.
    degree_issues = []
    for sid in sorted(labeled):
        g = len(geo_nbrs.get(sid, ()))
        j = len(json_nbrs.get(sid, ()))
        if g != j:
            only_geo = sorted(geo_nbrs.get(sid, ()) - json_nbrs.get(sid, ()))
            only_json = sorted(json_nbrs.get(sid, ()) - geo_nbrs.get(sid, ()))
            degree_issues.append(
                {
                    "id": sid,
                    "planet": sectors[sid].get("planet") or sectors[sid].get("displayName"),
                    "zone": sectors[sid].get("zone"),
                    "geoDegree": g,
                    "jsonDegree": j,
                    "missingNeighbors": only_geo,
                    "extraNeighbors": only_json,
                }
            )

    by_zone = Counter()
    for row in missing_rows + extra_rows:
        by_zone[row["aZone"] or "?"] += 1
        if row["bZone"] != row["aZone"]:
            by_zone[row["bZone"] or "?"] += 1

    return {
        "meta": {
            "layout": str(layout_path.relative_to(REPO_ROOT)).replace("\\", "/"),
            "adjacency": str(adj_path.relative_to(REPO_ROOT)).replace("\\", "/"),
            "falsePositives": str(false_positives_path.relative_to(REPO_ROOT)).replace("\\", "/")
            if false_positives_path.is_file()
            else None,
            "snapTol": tol,
            "labeledSectors": len(labeled),
            "sectorCount": len(all_ids),
            "geoEdgeCount": len(geo_edges),
            "jsonEdgeCount": len(json_edges_all),
            "missingEdgeCount": len(missing),
            "extraEdgeCount": len(extra),
            "suppressedFalsePositiveCount": len(suppressed),
            "unlabeledCount": len(unlabeled),
            "unknownLabelCount": len(unknown_labels),
            "sectorsWithDegreeMismatch": len(degree_issues),
        },
        "unlabeled": unlabeled,
        "unknownLabels": unknown_labels,
        "missingEdges": missing_rows,
        "extraEdges": extra_rows,
        "suppressedFalsePositives": suppressed_rows,
        "degreeIssues": degree_issues,
        "zoneTouchCounts": dict(by_zone),
    }


def to_markdown(report: dict) -> str:
    m = report["meta"]
    lines = [
        "# Adjacency mismatch report",
        "",
        f"Geometry from `{m['layout']}` vs edges in `{m['adjacency']}` (snap tol {m['snapTol']}px).",
        "",
        "## Summary",
        "",
        f"- Labeled sectors: **{m['labeledSectors']}** / {m['sectorCount']}",
        f"- Geometry edges: **{m['geoEdgeCount']}**",
        f"- Adjacency.json edges: **{m['jsonEdgeCount']}**",
        f"- Missing in Adjacency.json (geometry has, JSON lacks): **{m['missingEdgeCount']}**",
        f"- Extra in Adjacency.json (JSON has, geometry lacks): **{m['extraEdgeCount']}**",
        f"- Suppressed geometry false positives: **{m.get('suppressedFalsePositiveCount', 0)}**",
        f"- Sectors with degree mismatch: **{m['sectorsWithDegreeMismatch']}**",
        "",
        "Interpretation:",
        "",
        "- **Missing** → likely need to **add** an edge to `Adjacency.json` (if the shared boundary is real).",
        "- **Extra** → likely need to **remove** an edge (or the layout label is wrong / shared edge too short to detect).",
        "- **Suppressed** → geometry false positives confirmed non-adjacent by review (`geometry-false-positives.json`).",
        "",
    ]

    if report["unlabeled"]:
        lines += ["## Unlabeled sectors", "", *[f"- `{x}`" for x in report["unlabeled"]], ""]
    if report["unknownLabels"]:
        lines += ["## Unknown labels (not in Sectors.json)", "", *[f"- `{x}`" for x in report["unknownLabels"]], ""]

    lines += ["## Missing edges (add candidates)", ""]
    if not report["missingEdges"]:
        lines.append("_None._")
        lines.append("")
    else:
        lines += ["| a | b | zones |", "|---|---|---|"]
        for r in report["missingEdges"]:
            a = r["aPlanet"] or r["a"]
            b = r["bPlanet"] or r["b"]
            lines.append(
                f"| `{r['a']}` ({a}) | `{r['b']}` ({b}) | {r['aZone']} / {r['bZone']} |"
            )
        lines.append("")

    lines += ["## Extra edges (remove candidates)", ""]
    if not report["extraEdges"]:
        lines.append("_None._")
        lines.append("")
    else:
        lines += ["| a | b | zones |", "|---|---|---|"]
        for r in report["extraEdges"]:
            a = r["aPlanet"] or r["a"]
            b = r["bPlanet"] or r["b"]
            lines.append(
                f"| `{r['a']}` ({a}) | `{r['b']}` ({b}) | {r['aZone']} / {r['bZone']} |"
            )
        lines.append("")

    lines += ["## Suppressed geometry false positives", ""]
    suppressed = report.get("suppressedFalsePositives") or []
    if not suppressed:
        lines.append("_None._")
        lines.append("")
    else:
        lines += ["| a | b | zones |", "|---|---|---|"]
        for r in suppressed:
            a = r["aPlanet"] or r["a"]
            b = r["bPlanet"] or r["b"]
            lines.append(
                f"| `{r['a']}` ({a}) | `{r['b']}` ({b}) | {r['aZone']} / {r['bZone']} |"
            )
        lines.append("")

    lines += ["## Per-sector degree mismatches", ""]
    if not report["degreeIssues"]:
        lines.append("_None._")
        lines.append("")
    else:
        for d in report["degreeIssues"]:
            name = d["planet"] or d["id"]
            lines.append(
                f"- **{name}** `{d['id']}` ({d['zone']}): geo={d['geoDegree']} json={d['jsonDegree']}"
            )
            if d["missingNeighbors"]:
                lines.append(f"  - missing neighbors: {', '.join(f'`{x}`' for x in d['missingNeighbors'])}")
            if d["extraNeighbors"]:
                lines.append(f"  - extra neighbors: {', '.join(f'`{x}`' for x in d['extraNeighbors'])}")
        lines.append("")

    return "\n".join(lines) + "\n"


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--layout", type=Path, default=DEFAULT_LAYOUT)
    ap.add_argument("--adjacency", type=Path, default=DEFAULT_ADJ)
    ap.add_argument("--sectors", type=Path, default=DEFAULT_SECTORS)
    ap.add_argument("--tol", type=float, default=3.0)
    ap.add_argument("--false-positives", type=Path, default=DEFAULT_FALSE_POSITIVES)
    ap.add_argument("--out-json", type=Path, default=DEFAULT_OUT_JSON)
    ap.add_argument("--out-md", type=Path, default=DEFAULT_OUT_MD)
    args = ap.parse_args()

    report = run(
        args.layout,
        args.adjacency,
        args.sectors,
        tol=args.tol,
        false_positives_path=args.false_positives,
    )
    args.out_json.write_text(json.dumps(report, indent=2) + "\n")
    args.out_md.write_text(to_markdown(report))
    m = report["meta"]
    print(
        f"Wrote {args.out_md} and {args.out_json}: "
        f"missing={m['missingEdgeCount']} extra={m['extraEdgeCount']} "
        f"suppressedFP={m.get('suppressedFalsePositiveCount', 0)} "
        f"degreeIssues={m['sectorsWithDegreeMismatch']}"
    )


if __name__ == "__main__":
    main()
