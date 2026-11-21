"""VLM planner server. One request at a time, so the measured service time is
the latency a single mosquito sees on this GPU.

    python python/planner_server/server.py --backend transformers \
        --model Qwen/Qwen3-VL-8B-Instruct --port 8008
    python python/planner_server/server.py --backend openai \
        --openai-url http://127.0.0.1:8000/v1 --model Qwen/Qwen3-VL-8B-Instruct-FP8
    python python/planner_server/server.py --backend mock --mock-delay 0.3   # no GPU

POST /plan {"instruction": str, "images": [paths], "use_images": bool}
-> {"text": str, "service_time": float, "backend": str}
GET /health -> {"ok": true, "backend": ..., "model": ...}
"""
from __future__ import annotations

import argparse
import json
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from backends import make_backend  # noqa: E402


class PlannerHandler(BaseHTTPRequestHandler):
    backend = None
    lock = threading.Lock()

    def log_message(self, fmt, *args):  # quiet
        pass

    def _send(self, code: int, payload: dict) -> None:
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.path == "/health":
            self._send(200, {"ok": True, "backend": self.backend.name, "model": self.backend.model})
        else:
            self._send(404, {"error": "not found"})

    def do_POST(self):
        if self.path != "/plan":
            self._send(404, {"error": "not found"})
            return
        try:
            n = int(self.headers.get("Content-Length", "0"))
            req = json.loads(self.rfile.read(n).decode("utf-8"))
            with self.lock:  # serialize: queue time is excluded from service_time
                t0 = time.perf_counter()
                text = self.backend.generate(req["instruction"], req.get("images", []),
                                             bool(req.get("use_images", True)))
                service = time.perf_counter() - t0
            self._send(200, {"text": text, "service_time": service, "backend": self.backend.name})
        except Exception as e:  # report as a failed call; the client logs it as invalid
            self._send(500, {"error": repr(e)})


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--backend", choices=["mock", "transformers", "openai"], default="mock")
    ap.add_argument("--model", default="Qwen/Qwen3-VL-8B-Instruct")
    ap.add_argument("--openai-url", default="http://127.0.0.1:8000/v1")
    ap.add_argument("--mock-delay", type=float, default=0.0)
    ap.add_argument("--dtype", default="bfloat16")
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--port", type=int, default=8008)
    args = ap.parse_args()

    PlannerHandler.backend = make_backend(args)
    server = ThreadingHTTPServer((args.host, args.port), PlannerHandler)
    print(f"planner server: {args.backend} {args.model} on http://{args.host}:{args.port}")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
