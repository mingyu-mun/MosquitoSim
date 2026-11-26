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


WARMUP_INSTRUCTION = ("[과제] 사람에게 들키지 않고 피부에 착지해 흡혈하라.\n모기: 비행 중.\n피부: S1 얼굴 1.50m\n"
                      "[출력] 다음 중 하나를 JSON으로:\n{\"subgoal\": \"APPROACH\", \"target\": \"S1~S5 중 하나\"}\n")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--backend", choices=["mock", "transformers", "openai"], default="mock")
    # 11/25: 4B BF16 (about 9 GB) fits a 12 GB GPU next to Unity; 8B BF16 (about 17 GB) does not.
    # A local folder (models/Qwen3-VL-4B-Instruct) works as well as a Hub id.
    ap.add_argument("--model", default="Qwen/Qwen3-VL-4B-Instruct")
    ap.add_argument("--warmup", type=int, default=2,
                    help="calls before serving, so CUDA start-up is not counted in the first measured latency")
    ap.add_argument("--openai-url", default="http://127.0.0.1:8000/v1")
    ap.add_argument("--mock-delay", type=float, default=0.0)
    ap.add_argument("--dtype", default="bfloat16")
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--port", type=int, default=8008)
    args = ap.parse_args()

    PlannerHandler.backend = make_backend(args)
    for _ in range(args.warmup if args.backend != "mock" else 0):
        PlannerHandler.backend.generate(WARMUP_INSTRUCTION, [], False)
    if hasattr(PlannerHandler.backend, "memory"):
        print(f"model {args.model}: {PlannerHandler.backend.memory()}")
    server = ThreadingHTTPServer((args.host, args.port), PlannerHandler)
    print(f"planner server: {args.backend} {args.model} on http://{args.host}:{args.port}")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
