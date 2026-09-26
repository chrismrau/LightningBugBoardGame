#!/usr/bin/env python3
"""Extract closed sector faces from GameBoardClosedPath.svg into faces.json."""

from __future__ import annotations

import argparse
import json
import math
import re
from collections import defaultdict
from pathlib import Path
from xml.etree import ElementTree as ET

DEFAULT_TOL = 3.0
REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_SVG = REPO_ROOT / "reference" / "board" / "GameBoardClosedPath.svg"
DEFAULT_OUT = Path(__file__).resolve().parent / "faces.json"


def local(tag: str) -> str:
    return tag.split("}")[-1]


def cubic(p0, p1, p2, p3, n: int = 12):
    pts = []
    for i in range(n + 1):
        t = i / n
        u = 1 - t
        x = u * u * u * p0[0] + 3 * u * u * t * p1[0] + 3 * u * t * t * p2[0] + t * t * t * p3[0]
        y = u * u * u * p0[1] + 3 * u * u * t * p1[1] + 3 * u * t * t * p2[1] + t * t * t * p3[1]
        pts.append((x, y))
    return pts


def sample_path(d: str, curve_n: int = 10):
    tokens = re.findall(r"[MmLlHhVvCcSsQqTtAaZz]|[-+]?\d*\.?\d+(?:e[-+]?\d+)?", d)
    i = 0
    cx = cy = sx = sy = 0.0
    last_c = None
    sub = []
    subs = []
    cmd = None

    def flush():
        nonlocal sub
        if len(sub) >= 2:
            subs.append(sub)
        sub = []

    while i < len(tokens):
        t = tokens[i]
        if re.match(r"[A-Za-z]", t):
            cmd = t
            i += 1
        if cmd in "Mm":
            x = float(tokens[i])
            y = float(tokens[i + 1])
            i += 2
            if cmd == "m":
                x += cx
                y += cy
            flush()
            cx, cy = x, y
            sx, sy = x, y
            sub = [(cx, cy)]
            cmd = "L" if cmd == "M" else "l"
        elif cmd in "Ll":
            x = float(tokens[i])
            y = float(tokens[i + 1])
            i += 2
            if cmd == "l":
                x += cx
                y += cy
            cx, cy = x, y
            sub.append((cx, cy))
            last_c = None
        elif cmd in "Hh":
            x = float(tokens[i])
            i += 1
            if cmd == "h":
                x += cx
            cx = x
            sub.append((cx, cy))
            last_c = None
        elif cmd in "Vv":
            y = float(tokens[i])
            i += 1
            if cmd == "v":
                y += cy
            cy = y
            sub.append((cx, cy))
            last_c = None
        elif cmd in "Cc":
            nums = [float(tokens[i + j]) for j in range(6)]
            i += 6
            if cmd == "c":
                nums = [
                    nums[0] + cx,
                    nums[1] + cy,
                    nums[2] + cx,
                    nums[3] + cy,
                    nums[4] + cx,
                    nums[5] + cy,
                ]
            p0 = (cx, cy)
            p1 = (nums[0], nums[1])
            p2 = (nums[2], nums[3])
            p3 = (nums[4], nums[5])
            pts = cubic(p0, p1, p2, p3, curve_n)
            sub.extend(pts[1:])
            cx, cy = p3
            last_c = p2
        elif cmd in "Ss":
            nums = [float(tokens[i + j]) for j in range(4)]
            i += 4
            if cmd == "s":
                nums = [nums[0] + cx, nums[1] + cy, nums[2] + cx, nums[3] + cy]
            p1 = (2 * cx - last_c[0], 2 * cy - last_c[1]) if last_c else (cx, cy)
            p0 = (cx, cy)
            p2 = (nums[0], nums[1])
            p3 = (nums[2], nums[3])
            pts = cubic(p0, p1, p2, p3, curve_n)
            sub.extend(pts[1:])
            cx, cy = p3
            last_c = p2
        elif cmd in "Zz":
            if sub and (abs(sub[-1][0] - sx) > 1e-6 or abs(sub[-1][1] - sy) > 1e-6):
                sub.append((sx, sy))
            flush()
            cx, cy = sx, sy
            last_c = None
        else:
            break
    flush()
    return subs


def parse_points(s: str):
    nums = list(map(float, re.findall(r"[-+]?\d*\.?\d+(?:e[-+]?\d+)?", s)))
    return [(nums[i], nums[i + 1]) for i in range(0, len(nums) - 1, 2)]


def load_edge_polylines(svg_path: Path):
    text = svg_path.read_text(errors="ignore")
    struct = re.sub(r'data:image/[^;]+;base64,[^"]+', "[OMITTED]", text)
    root = ET.fromstring(struct)
    edge_polylines = []
    image_offset = (0.0, 0.0)
    image_size = None
    for el in root.iter():
        tag = local(el.tag)
        if tag == "image":
            tr = el.get("transform") or ""
            m = re.search(r"translate\(([^)]+)\)", tr)
            if m:
                parts = m.group(1).replace(",", " ").split()
                image_offset = (float(parts[0]), float(parts[1]))
            w = el.get("width")
            h = el.get("height")
            if w and h:
                image_size = (float(w), float(h))
        elif tag == "path" and el.get("d"):
            edge_polylines.extend(sample_path(el.get("d")))
        elif tag == "line":
            edge_polylines.append(
                [
                    (float(el.get("x1", "0")), float(el.get("y1", "0"))),
                    (float(el.get("x2", "0")), float(el.get("y2", "0"))),
                ]
            )
        elif tag == "polyline" and el.get("points"):
            pts = parse_points(el.get("points"))
            if len(pts) >= 2:
                edge_polylines.append(pts)
        elif tag == "polygon" and el.get("points"):
            pts = parse_points(el.get("points"))
            if len(pts) >= 3:
                if pts[0] != pts[-1]:
                    pts = pts + [pts[0]]
                edge_polylines.append(pts)
    vb = [float(x) for x in re.search(r'viewBox="([^"]+)"', struct).group(1).split()]
    return edge_polylines, {"viewBox": vb, "imageOffset": image_offset, "imageSize": image_size}


def poly_area(poly):
    a = 0.0
    for i in range(len(poly)):
        x1, y1 = poly[i]
        x2, y2 = poly[(i + 1) % len(poly)]
        a += x1 * y2 - x2 * y1
    return abs(a) / 2


def poly_centroid(poly):
    a = 0.0
    cx = cy = 0.0
    n = len(poly)
    for i in range(n):
        x1, y1 = poly[i]
        x2, y2 = poly[(i + 1) % n]
        cross = x1 * y2 - x2 * y1
        a += cross
        cx += (x1 + x2) * cross
        cy += (y1 + y2) * cross
    a *= 0.5
    if abs(a) < 1e-6:
        return (
            sum(p[0] for p in poly) / n,
            sum(p[1] for p in poly) / n,
        )
    return (cx / (6 * a), cy / (6 * a))


def simplify_poly(poly, max_points: int = 48):
    """Keep polygon manageable for the browser while preserving shape."""
    if len(poly) <= max_points:
        return [(round(x, 2), round(y, 2)) for x, y in poly]
    # Evenly sample along the ring.
    out = []
    for i in range(max_points):
        idx = int(round(i * (len(poly) - 1) / (max_points - 1)))
        x, y = poly[idx]
        out.append((round(x, 2), round(y, 2)))
    if out[0] != out[-1]:
        # ensure ring-ish uniqueness without forcing duplicate close
        pass
    # drop consecutive duplicates
    cleaned = []
    for p in out:
        if not cleaned or p != cleaned[-1]:
            cleaned.append(p)
    return cleaned


def extract_faces(edge_polylines, tol: float = DEFAULT_TOL):
    def snap(p):
        return (round(p[0] / tol) * tol, round(p[1] / tol) * tol)

    graph = defaultdict(set)
    for pl in edge_polylines:
        chain = []
        for p in pl:
            sp = snap(p)
            if not chain or sp != chain[-1]:
                chain.append(sp)
        for a, b in zip(chain, chain[1:]):
            graph[a].add(b)
            graph[b].add(a)

    nbr_sorted = {
        v: sorted(nbrs, key=lambda w: math.atan2(w[1] - v[1], w[0] - v[0]))
        for v, nbrs in graph.items()
    }

    def next_left(prev, curr):
        nbrs = nbr_sorted[curr]
        i = nbrs.index(prev)
        return nbrs[(i + 1) % len(nbrs)]

    used = set()
    faces = []
    for start_a in graph:
        for start_b in graph[start_a]:
            if (start_a, start_b) in used:
                continue
            face = [start_a]
            a, b = start_a, start_b
            ok = True
            for _ in range(30000):
                used.add((a, b))
                face.append(b)
                nxt = next_left(a, b)
                a, b = b, nxt
                if (a, b) == (start_a, start_b):
                    break
                if (a, b) in used:
                    ok = False
                    break
            else:
                ok = False
            if ok and len(face) >= 3:
                if face[0] == face[-1]:
                    face = face[:-1]
                faces.append(face)

    faces_sorted = sorted(faces, key=poly_area)
    # Drop outer face (largest).
    interior = faces_sorted[:-1] if faces_sorted else []
    interior = [f for f in interior if 500 < poly_area(f) < 500000]

    deg1 = [v for v, n in graph.items() if len(n) == 1]
    return interior, {
        "vertexCount": len(graph),
        "edgeCount": sum(len(n) for n in graph.values()) // 2,
        "danglingCount": len(deg1),
        "faceCount": len(interior),
        "snapTol": tol,
    }


def build_payload(svg_path: Path, tol: float = DEFAULT_TOL):
    edges, meta = load_edge_polylines(svg_path)
    faces, stats = extract_faces(edges, tol=tol)
    face_docs = []
    for i, face in enumerate(faces):
        cx, cy = poly_centroid(face)
        face_docs.append(
            {
                "faceId": f"face-{i:03d}",
                "centroid": [round(cx, 2), round(cy, 2)],
                "area": round(poly_area(face), 1),
                "points": simplify_poly(face),
            }
        )
    return {
        "meta": {
            "sourceSvg": str(svg_path.relative_to(REPO_ROOT)).replace("\\", "/"),
            "viewBox": meta["viewBox"],
            "imageOffset": list(meta["imageOffset"]),
            "imageSize": list(meta["imageSize"]) if meta["imageSize"] else None,
            **stats,
        },
        "faces": face_docs,
    }


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--svg", type=Path, default=DEFAULT_SVG)
    ap.add_argument("--out", type=Path, default=DEFAULT_OUT)
    ap.add_argument("--tol", type=float, default=DEFAULT_TOL)
    args = ap.parse_args()
    payload = build_payload(args.svg, tol=args.tol)
    args.out.write_text(json.dumps(payload, indent=2) + "\n")
    print(
        f"Wrote {args.out} with {payload['meta']['faceCount']} faces "
        f"(dangling={payload['meta']['danglingCount']}, tol={args.tol})"
    )


if __name__ == "__main__":
    main()
