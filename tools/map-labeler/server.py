#!/usr/bin/env python3
"""Local HTTP server for the sector map labeler UI."""

from __future__ import annotations

import argparse
import json
import mimetypes
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlparse

from sync_faces_from_layout import sync_files

REPO_ROOT = Path(__file__).resolve().parents[2]
TOOL_DIR = Path(__file__).resolve().parent
LAYOUT_PATH = REPO_ROOT / "Data" / "Map" / "SectorLayout.json"
SECTORS_PATH = REPO_ROOT / "Data" / "Map" / "Sectors.json"
ADJ_PATH = REPO_ROOT / "Data" / "Map" / "Adjacency.json"
FACES_PATH = TOOL_DIR / "faces.json"
BOARD_DIR = REPO_ROOT / "reference" / "board"


def ensure_faces(tol: float = 3.0) -> None:
    """Ensure faces.json exists (SVG extract), then align geometry with SectorLayout."""
    if not FACES_PATH.exists():
        from extract_faces import build_payload

        payload = build_payload(BOARD_DIR / "GameBoardClosedPath.svg", tol=tol)
        FACES_PATH.write_text(json.dumps(payload, indent=2) + "\n")
    else:
        meta = json.loads(FACES_PATH.read_text()).get("meta", {})
        if meta.get("faceCount") != 155 or meta.get("danglingCount", 0) != 0:
            from extract_faces import build_payload

            payload = build_payload(BOARD_DIR / "GameBoardClosedPath.svg", tol=tol)
            FACES_PATH.write_text(json.dumps(payload, indent=2) + "\n")

    if LAYOUT_PATH.exists():
        layout = json.loads(LAYOUT_PATH.read_text())
        if layout.get("sectors"):
            changed = sync_files(LAYOUT_PATH, FACES_PATH, write=True)
            if changed:
                print(f"[map-labeler] synced {changed} faces from SectorLayout.json")


class Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt, *args):
        print(f"[map-labeler] {self.address_string()} {fmt % args}")

    def _send(self, code: int, body: bytes, content_type: str):
        self.send_response(code)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def _send_json(self, code: int, obj):
        raw = json.dumps(obj, indent=2).encode("utf-8")
        self._send(code, raw, "application/json; charset=utf-8")

    def _read_json(self):
        length = int(self.headers.get("Content-Length", "0"))
        raw = self.rfile.read(length) if length else b"{}"
        return json.loads(raw.decode("utf-8"))

    def do_GET(self):
        path = urlparse(self.path).path
        if path == "/" or path == "/index.html":
            self._send(200, (TOOL_DIR / "index.html").read_bytes(), "text/html; charset=utf-8")
            return
        if path == "/app.js":
            self._send(200, (TOOL_DIR / "app.js").read_bytes(), "application/javascript; charset=utf-8")
            return
        if path == "/style.css":
            self._send(200, (TOOL_DIR / "style.css").read_bytes(), "text/css; charset=utf-8")
            return
        if path == "/api/bootstrap":
            ensure_faces()
            layout = {}
            if LAYOUT_PATH.exists():
                layout = json.loads(LAYOUT_PATH.read_text())
            self._send_json(
                200,
                {
                    "sectors": json.loads(SECTORS_PATH.read_text()),
                    "adjacency": json.loads(ADJ_PATH.read_text()),
                    "faces": json.loads(FACES_PATH.read_text()),
                    "layout": layout,
                    "paths": {
                        "layout": str(LAYOUT_PATH.relative_to(REPO_ROOT)).replace("\\", "/"),
                        "boardImage": "/board/GameBoardMarkup.png",
                        "faces": str(FACES_PATH.relative_to(REPO_ROOT)).replace("\\", "/"),
                    },
                },
            )
            return
        if path.startswith("/board/"):
            name = path[len("/board/") :]
            if "/" in name or name.startswith("."):
                self._send_json(400, {"error": "bad path"})
                return
            file_path = BOARD_DIR / name
            if not file_path.is_file():
                self._send_json(404, {"error": "not found"})
                return
            ctype = mimetypes.guess_type(str(file_path))[0] or "application/octet-stream"
            self._send(200, file_path.read_bytes(), ctype)
            return
        self._send_json(404, {"error": "not found"})

    def do_POST(self):
        path = urlparse(self.path).path
        if path == "/api/layout":
            payload = self._read_json()
            LAYOUT_PATH.parent.mkdir(parents=True, exist_ok=True)
            LAYOUT_PATH.write_text(json.dumps(payload, indent=2) + "\n")
            faces_synced = 0
            if FACES_PATH.exists() and payload.get("sectors"):
                faces_synced = sync_files(LAYOUT_PATH, FACES_PATH, write=True)
            self._send_json(
                200,
                {
                    "ok": True,
                    "path": str(LAYOUT_PATH.relative_to(REPO_ROOT)).replace("\\", "/"),
                    "assignments": len(payload.get("assignments", {})),
                    "facesSynced": faces_synced,
                },
            )
            return
        self._send_json(404, {"error": "not found"})


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--port", type=int, default=8765)
    args = ap.parse_args()
    ensure_faces()
    server = ThreadingHTTPServer((args.host, args.port), Handler)
    print(f"Map labeler at http://{args.host}:{args.port}/")
    print(f"Saves layout to {LAYOUT_PATH.relative_to(REPO_ROOT)}")
    print(f"Keeps faces in sync at {FACES_PATH.relative_to(REPO_ROOT)}")
    server.serve_forever()


if __name__ == "__main__":
    main()
