#!/usr/bin/env python3
"""Sync tools/map-labeler/faces.json geometry from Data/Map/SectorLayout.json.

faces.json keeps face ids + board meta (viewBox / imageOffset). Once sectors are
labeled, polygon points/centroids live in SectorLayout and must be copied back
so the labeler overlay and on-disk faces stay aligned.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_LAYOUT = REPO_ROOT / "Data" / "Map" / "SectorLayout.json"
DEFAULT_FACES = Path(__file__).resolve().parent / "faces.json"


def sync_faces_from_layout(layout: dict, faces_doc: dict) -> tuple[dict, int]:
    """Return (updated faces_doc, number of faces whose geometry changed)."""
    by_face: dict[str, dict] = {}
    for sid, geo in (layout.get("sectors") or {}).items():
        fid = geo.get("faceId")
        if not fid or not isinstance(geo.get("points"), list) or len(geo["points"]) < 3:
            continue
        by_face[fid] = geo

    changed = 0
    for face in faces_doc.get("faces") or []:
        fid = face.get("faceId")
        geo = by_face.get(fid)
        if not geo:
            continue
        new_pts = [[p[0], p[1]] for p in geo["points"]]
        new_c = None
        if isinstance(geo.get("centroid"), list) and len(geo["centroid"]) >= 2:
            new_c = [geo["centroid"][0], geo["centroid"][1]]
        pts_changed = face.get("points") != new_pts
        c_changed = new_c is not None and face.get("centroid") != new_c
        if pts_changed or c_changed:
            face["points"] = new_pts
            if new_c is not None:
                face["centroid"] = new_c
            changed += 1

    meta = faces_doc.setdefault("meta", {})
    meta["syncedFromSectorLayout"] = True
    meta["syncedSectorCount"] = len(by_face)
    meta["faceCount"] = len(faces_doc.get("faces") or [])
    return faces_doc, changed


def sync_files(layout_path: Path, faces_path: Path, *, write: bool = True) -> int:
    if not layout_path.is_file():
        raise SystemExit(f"missing layout: {layout_path}")
    if not faces_path.is_file():
        raise SystemExit(f"missing faces: {faces_path}")
    layout = json.loads(layout_path.read_text())
    faces_doc = json.loads(faces_path.read_text())
    faces_doc, changed = sync_faces_from_layout(layout, faces_doc)
    if write:
        faces_path.write_text(json.dumps(faces_doc, indent=2) + "\n")
    return changed


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--layout", type=Path, default=DEFAULT_LAYOUT)
    ap.add_argument("--faces", type=Path, default=DEFAULT_FACES)
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()
    changed = sync_files(args.layout, args.faces, write=not args.dry_run)
    print(
        f"{'would update' if args.dry_run else 'updated'} {changed} faces "
        f"from {args.layout} → {args.faces}"
    )


if __name__ == "__main__":
    main()
