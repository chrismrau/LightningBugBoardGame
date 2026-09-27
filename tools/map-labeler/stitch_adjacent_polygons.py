#!/usr/bin/env python3
"""Stitch SectorLayout polygons so Adjacency.json neighbors share boundary vertices."""

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


def closest_boundary_pair(ra, rb):
    """Return (pa_on_a, pb_on_b, distance) for closest boundary points."""
    best = (None, None, float("inf"))
    # points of A vs segments of B
    for p in ra:
        for i in range(len(rb)):
            a = rb[i]
            b = rb[(i + 1) % len(rb)]
            d, q, _ = dist_point_to_seg(p[0], p[1], a[0], a[1], b[0], b[1])
            if d < best[2]:
                best = (tuple(p), q, d)
    # points of B vs segments of A
    for p in rb:
        for i in range(len(ra)):
            a = ra[i]
            b = ra[(i + 1) % len(ra)]
            d, q, _ = dist_point_to_seg(p[0], p[1], a[0], a[1], b[0], b[1])
            if d < best[2]:
                best = (q, tuple(p), d)
    return best


def nearest_seg_index(ring, pt):
    best_i, best_d, best_q = 0, float("inf"), pt
    for i in range(len(ring)):
        a = ring[i]
        b = ring[(i + 1) % len(ring)]
        d, q, _ = dist_point_to_seg(pt[0], pt[1], a[0], a[1], b[0], b[1])
        if d < best_d:
            best_i, best_d, best_q = i, d, q
    return best_i, best_q, best_d


def insert_vertex(ring, pt, merge_tol: float = 2.0):
    """Insert pt onto nearest segment unless an existing vertex is already close."""
    pts = [tuple(p) for p in ring]
    for i, p in enumerate(pts):
        if dist(p, pt) <= merge_tol:
            # snap existing vertex to exact pt for shared identity
            pts[i] = (round(pt[0], 2), round(pt[1], 2))
            return pts, False
    i, q, _ = nearest_seg_index(pts, pt)
    new_pt = (round(pt[0], 2), round(pt[1], 2))
    # insert after vertex i
    pts.insert(i + 1, new_pt)
    return pts, True


def second_contact(ra, rb, first_mid, min_sep: float):
    """Find another close boundary pair, separated from first_mid."""
    candidates = []
    for p in ra:
        for i in range(len(rb)):
            a = rb[i]
            b = rb[(i + 1) % len(rb)]
            d, q, _ = dist_point_to_seg(p[0], p[1], a[0], a[1], b[0], b[1])
            mid = ((p[0] + q[0]) / 2, (p[1] + q[1]) / 2)
            if dist(mid, first_mid) >= min_sep:
                candidates.append((d, tuple(p), q, mid))
    for p in rb:
        for i in range(len(ra)):
            a = ra[i]
            b = ra[(i + 1) % len(ra)]
            d, q, _ = dist_point_to_seg(p[0], p[1], a[0], a[1], b[0], b[1])
            mid = ((q[0] + p[0]) / 2, (q[1] + p[1]) / 2)
            if dist(mid, first_mid) >= min_sep:
                candidates.append((d, q, tuple(p), mid))
    if not candidates:
        return None
    candidates.sort(key=lambda x: x[0])
    return candidates[0]


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


def stitch_pair(ra, rb, sep_frac: float = 0.15):
    """Return updated (ra, rb) sharing two identical boundary vertices."""
    pa, pb, d0 = closest_boundary_pair(ra, rb)
    if pa is None:
        return ra, rb, False
    m1 = ((pa[0] + pb[0]) / 2.0, (pa[1] + pb[1]) / 2.0)

    # separation scale from polygon sizes
    ca = poly_centroid(ra)
    cb = poly_centroid(rb)
    span = max(dist(ca, cb), 20.0)
    min_sep = max(8.0, span * sep_frac)

    second = second_contact(ra, rb, m1, min_sep)
    if second is None:
        # synthesize second point offset perpendicular to A↔B centerline
        dx, dy = cb[0] - ca[0], cb[1] - ca[1]
        L = math.hypot(dx, dy) or 1.0
        px, py = -dy / L, dx / L
        m2 = (m1[0] + px * min_sep, m1[1] + py * min_sep)
    else:
        m2 = second[3]

    ra2, _ = insert_vertex(ra, m1)
    ra2, _ = insert_vertex(ra2, m2)
    rb2, _ = insert_vertex(rb, m1)
    rb2, _ = insert_vertex(rb2, m2)
    return ra2, rb2, True


def needs_stitch(ra, rb, tol: float = 3.0) -> bool:
    """True if polygons do not already share a detectable boundary (same rules as reporter)."""
    def pt_key(p):
        return (round(p[0] / tol) * tol, round(p[1] / tol) * tol)

    va = {pt_key(p) for p in ra}
    vb = {pt_key(p) for p in rb}
    if len(va & vb) >= 2:
        return False

    def min_dist_point_to_ring(pt, ring):
        best = float("inf")
        for i in range(len(ring)):
            a = ring[i]
            b = ring[(i + 1) % len(ring)]
            d, _, _ = dist_point_to_seg(pt[0], pt[1], a[0], a[1], b[0], b[1])
            best = min(best, d)
        return best

    for k in range(len(ra)):
        p = ra[k]
        q = ra[(k + 1) % len(ra)]
        if dist(p, q) < tol * 0.5:
            continue
        if min_dist_point_to_ring(p, rb) <= tol and min_dist_point_to_ring(q, rb) <= tol:
            return False
    return True


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--layout", type=Path, default=DEFAULT_LAYOUT)
    ap.add_argument("--adjacency", type=Path, default=DEFAULT_ADJ)
    ap.add_argument("--tol", type=float, default=3.0)
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

    stitched = 0
    skipped = 0
    for a, b in pairs:
        ra = sectors[a].get("points") or []
        rb = sectors[b].get("points") or []
        if len(ra) < 3 or len(rb) < 3:
            skipped += 1
            continue
        if not needs_stitch(ra, rb, tol=args.tol):
            continue
        ra2, rb2, ok = stitch_pair(ra, rb)
        if not ok:
            skipped += 1
            continue
        sectors[a]["points"] = [list(p) for p in ra2]
        sectors[b]["points"] = [list(p) for p in rb2]
        sectors[a]["centroid"] = poly_centroid(ra2)
        sectors[b]["centroid"] = poly_centroid(rb2)
        stitched += 1

    layout["meta"]["assignmentCount"] = len(layout.get("assignments") or {})
    layout["meta"]["polygonStitchNote"] = (
        f"Stitched {stitched} adjacency pairs to share boundary vertices "
        f"(tol={args.tol}) for geometry/Adjacency alignment."
    )
    args.layout.write_text(json.dumps(layout, indent=2) + "\n")
    print(f"Stitched {stitched} pairs; skipped {skipped}; wrote {args.layout}")


if __name__ == "__main__":
    main()
