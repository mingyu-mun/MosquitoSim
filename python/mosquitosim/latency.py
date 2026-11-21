"""Latency modes for evaluation and the command scheduler.

measured   d = the planner server's pure compute time for this call (main experiment)
fixed:<s>  d = s seconds for every call (latency curve; fixed:0 = zero-latency VLM)
realtime   time scale 1, wait on the wall clock (one sanity check)

With measured and fixed, Unity is frozen while Python waits for the planner
(ML-Agents waits for Python every step). After the answer, the simulation runs
d simulated seconds on the previous command, then the new command is sent.
"""
from __future__ import annotations

import heapq
import itertools
from dataclasses import dataclass, field
from typing import List, Optional, Tuple


@dataclass(frozen=True)
class LatencyMode:
    kind: str                    # "measured" | "fixed" | "realtime"
    fixed_s: float = 0.0

    @staticmethod
    def parse(text: str) -> "LatencyMode":
        t = text.strip().lower()
        if t in ("measured", "realtime"):
            return LatencyMode(t)
        if t.startswith("fixed:"):
            v = float(t.split(":", 1)[1])
            if v < 0:
                raise ValueError("fixed latency must be >= 0")
            return LatencyMode("fixed", v)
        raise ValueError(f"unknown latency mode {text!r}; use measured, fixed:<s> or realtime")

    def delay(self, service_time: float) -> float:
        """Simulated delay for one call (not used in realtime mode)."""
        if self.kind == "measured":
            return max(0.0, service_time)
        if self.kind == "fixed":
            return self.fixed_s
        raise ValueError("realtime mode has no simulated delay")

    @property
    def time_scale(self) -> float:
        return 1.0 if self.kind == "realtime" else 20.0

    def __str__(self) -> str:
        return f"fixed:{self.fixed_s:g}" if self.kind == "fixed" else self.kind


@dataclass(order=True)
class _Pending:
    apply_at: float
    seq: int
    room: int = field(compare=False)
    episode: str = field(compare=False)
    decision: int = field(compare=False)
    command: dict = field(compare=False)


class CommandScheduler:
    """Holds commands until simulated time reaches their apply time."""

    def __init__(self) -> None:
        self._heap: List[_Pending] = []
        self._seq = itertools.count()

    def schedule(self, room: int, episode: str, decision: int, command: dict, apply_at: float) -> None:
        heapq.heappush(self._heap, _Pending(apply_at, next(self._seq), room, episode, decision, command))

    def pop_ready(self, now: float, eps: float = 1e-9) -> List[Tuple[int, str, int, dict]]:
        out = []
        while self._heap and self._heap[0].apply_at <= now + eps:
            p = heapq.heappop(self._heap)
            out.append((p.room, p.episode, p.decision, p.command))
        return out

    def drop_room(self, room: int) -> None:
        """Forget pending commands of a room whose episode ended."""
        self._heap = [p for p in self._heap if p.room != room]
        heapq.heapify(self._heap)

    def __len__(self) -> int:
        return len(self._heap)

    def next_time(self) -> Optional[float]:
        return self._heap[0].apply_at if self._heap else None
