#!/usr/bin/env python3
"""Make every Adjacency.json neighbor pair share exact coinciding boundary vertices.

For each adjacent pair A–B, any vertex of A that lies on/near B's boundary is
inserted into B at A's exact coordinates (or an existing nearby B vertex is
snapped). The reverse pass covers B→A. Repeat until stable so triple junctions
propagate.
"""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_LAYOUT = REPO_ROOT / "Data" / "Map" / "SectorLayout.json"
DEFAULT_ADJ = REPO_ROOT / "Data" / "Map" / "Adjacency.json"


def dist(a, b) -> float:
    return math.hypot(a[0] - b[0], a[1] - b[1])


def dist_point_to_seg(px, py, ax, ay, bx, by):
    abx, aby = bx - ax, by - ay
    apx, apy = px - ax, py - ay
    ab2 = abx * abx + aby * aby
    if ab2 <= 1e-12:
        return math.hypot(apx, apy), (ax, ay), 0.0
    t = max(0.0, min(1.0, (apx * abx + apy * aby) / ab2))
    qx, qy = ax + t * abx, ay + t * aby
    return math.hypot(px - qx, py - qy), (qx, qy), t


def poly_centroid(pts):
    if not pts:
        return [0.0, 0.0]
    n = len(pts)
    a = 0.0
    cx = cy = 0.0
    for i in range(n):
        x1, y1 = pts[i]
        x2, y2 = pts[(i + 1) % n]
        cross = x1 * y2 - x2 * y1
        a += cross
        cx += (x1 + x2) * cross
        cy += (y1 + y2) * cross
    a *= 0.5
    if abs(a) < 1e-6:
        return [
            round(sum(p[0] for p in pts) / n, 2),
            round(sum(p[1] for p in pts) / n, 2),
        ]
    return [round(cx / (6 * a), 2), round(cy / (6 * a), 2)]


def exact(p, nd=2):
    return (round(p[0], nd), round(p[1], nd))


def nearest_seg_index(ring, pt):
    best_i, best_d, best_q = 0, float("inf"), pt
    for i in range(len(ring)):
        a = ring[i]
        b = ring[(i + 1) % len(ring)]
        d, q, _ = dist_point_to_seg(pt[0], pt[1], a[0], a[1], b[0], b[1])
        if d < best_d:
            best_i, best_d, best_q = i, d, q
    return best_i, best_q, best_d


def ensure_vertex(ring, pt, merge_tol: float):
    """Ensure ring contains exact(pt). Snap nearby vertex or insert on nearest seg.

    Returns (new_ring, changed).
    """
    target = exact(pt)
    pts = [exact(p) for p in ring]
    for i, p in enumerate(pts):
        if p == target:
            return pts, False
        if dist(p, target) <= merge_tol:
            pts[i] = target
            return pts, True
    i, _, d = nearest_seg_index(pts, target)
    if d > merge_tol * 4:
        # Point is not really on this ring — skip rather than distort.
        return pts, False
    pts.insert(i + 1, target)
    return pts, True


def near_boundary_vertices(src, dst, near_tol: float):
    """Vertices of src within near_tol of dst's boundary."""
    out = []
    for p in src:
        _, _, d = nearest_seg_index(dst, p)
        if d <= near_tol:
            out.append(exact(p))
    return out


def sync_pair(ra, rb, near_tol: float, merge_tol: float):
    """Propagate near-boundary vertices both ways. Returns (ra, rb, n_changes)."""
    changes = 0
    # A → B
    for p in near_boundary_vertices(ra, rb, near_tol):
        rb, ch = ensure_vertex(rb, p, merge_tol)
        changes += int(ch)
    # B → A
    for p in near_boundary_vertices(rb, ra, near_tol):
        ra, ch = ensure_vertex(ra, p, merge_tol)
        changes += int(ch)
    return ra, rb, changes


def orphan_count(ra, rb, near_tol: float) -> int:
    ea = {exact(p) for p in ra}
    eb = {exact(p) for p in rb}
    n = 0
    for p in near_boundary_vertices(ra, rb, near_tol):
        if p not in eb:
            n += 1
    for p in near_boundary_vertices(rb, ra, near_tol):
        if p not in ea:
            n += 1
    return n


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--layout", type=Path, default=DEFAULT_LAYOUT)
    ap.add_argument("--adjacency", type=Path, default=DEFAULT_ADJ)
    ap.add_argument(
        "--near-tol",
        type=float,
        default=3.0,
        help="Max distance for a vertex to count as on the neighbor boundary",
    )
    ap.add_argument(
        "--merge-tol",
        type=float,
        default=2.0,
        help="Snap existing vertex instead of inserting when within this distance",
    )
    ap.add_argument("--max-iters", type=int, default=12)
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    layout = json.loads(args.layout.read_text())
    adj = json.loads(args.adjacency.read_text())
    sectors = layout["sectors"]

    pairs = []
    for e in adj.get("edges") or []:
        a, b = e["a"], e["b"]
        if a in sectors and b in sectors:
            pairs.append((a, b) if a < b else (b, a))
    pairs = sorted(set(pairs))

    before_orphans = 0
    for a, b in pairs:
        ra = [exact(p) for p in (sectors[a].get("points") or [])]
        rb = [exact(p) for p in (sectors[b].get("points") or [])]
        if len(ra) < 3 or len(rb) < 3:
            continue
        before_orphans += orphan_count(ra, rb, args.near_tol)

    total_changes = 0
    for it in range(1, args.max_iters + 1):
        iter_changes = 0
        for a, b in pairs:
            ra = [exact(p) for p in (sectors[a].get("points") or [])]
            rb = [exact(p) for p in (sectors[b].get("points") or [])]
            if len(ra) < 3 or len(rb) < 3:
                continue
            ra2, rb2, ch = sync_pair(ra, rb, args.near_tol, args.merge_tol)
            if ch:
                sectors[a]["points"] = [list(p) for p in ra2]
                sectors[b]["points"] = [list(p) for p in rb2]
                sectors[a]["centroid"] = poly_centroid(ra2)
                sectors[b]["centroid"] = poly_centroid(rb2)
                iter_changes += ch
        total_changes += iter_changes
        print(f"iter {it}: {iter_changes} vertex snaps/inserts")
        if iter_changes == 0:
            break

    after_orphans = 0
    problem = 0
    for a, b in pairs:
        ra = [exact(p) for p in (sectors[a].get("points") or [])]
        rb = [exact(p) for p in (sectors[b].get("points") or [])]
        if len(ra) < 3 or len(rb) < 3:
            continue
        o = orphan_count(ra, rb, args.near_tol)
        after_orphans += o
        if o:
            problem += 1

    print(
        f"orphans before={before_orphans} after={after_orphans} "
        f"problem_pairs={problem} total_changes={total_changes}"
    )

    if args.dry_run:
        print("dry-run: not writing")
        return

    layout["meta"]["assignmentCount"] = len(layout.get("assignments") or {})
    layout["meta"]["boundaryVertexSyncNote"] = (
        f"Synced coinciding boundary vertices across {len(pairs)} adjacency pairs "
        f"(nearTol={args.near_tol}, mergeTol={args.merge_tol}); "
        f"{total_changes} snaps/inserts; orphans {before_orphans}→{after_orphans}."
    )
    args.layout.write_text(json.dumps(layout, indent=2) + "\n")
    print(f"wrote {args.layout}")


if __name__ == "__main__":
    main()
