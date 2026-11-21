"""Client for the VLM planner server (python/planner_server/server.py).

POST /plan  {"instruction": str, "images": [paths], "use_images": bool}
-> {"text": raw model output, "service_time": seconds of pure compute, "backend": str}

The evaluation loop sends one request at a time, so service_time is the
latency a single mosquito would see on the reference GPU.
"""
from __future__ import annotations

import json
import time
import urllib.request
from dataclasses import dataclass
from typing import Optional, Sequence


@dataclass
class PlanResult:
    text: str
    service_time: float     # server-measured compute time (s)
    wall_time: float        # client-measured round trip (s)
    error: Optional[str] = None


class PlannerClient:
    def __init__(self, url: str = "http://127.0.0.1:8008", timeout: float = 60.0) -> None:
        self.url = url.rstrip("/")
        self.timeout = timeout

    def plan(self, instruction: str, images: Sequence[str], use_images: bool) -> PlanResult:
        body = json.dumps({"instruction": instruction, "images": list(images),
                           "use_images": use_images}).encode("utf-8")
        req = urllib.request.Request(self.url + "/plan", data=body,
                                     headers={"Content-Type": "application/json"})
        t0 = time.perf_counter()
        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as resp:
                out = json.loads(resp.read().decode("utf-8"))
        except Exception as e:  # network error, timeout, bad JSON: an invalid response
            return PlanResult("", 0.0, time.perf_counter() - t0, error=str(e))
        wall = time.perf_counter() - t0
        return PlanResult(out.get("text", ""), float(out.get("service_time", wall)), wall)
