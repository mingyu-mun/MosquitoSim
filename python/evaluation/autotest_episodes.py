"""Route benchmark from Unity auto-test runs (11/25).

The auto-test runner writes AutoTest/<id>/episodes.jsonl, one episode_end record per line (the same
record the planner channel sends). This reads one or more such folders and prints metrics.route_summary
for each, labelled by folder name, plus outcome counts.

    python -m evaluation.autotest_episodes AutoTest/run58_route_fixed AutoTest/run59_route_masked ...
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import List

from evaluation import metrics


def load(folder: Path) -> List[dict]:
    rows = []
    for line in (folder / "episodes.jsonl").read_text(encoding="utf-8").splitlines():
        if not line.strip():
            continue
        r = json.loads(line)
        if r.get("route_hops") is None:
            continue
        rows.append({
            "split": "autotest", "condition": folder.name, "latency": "-",
            "success": bool(r["success"]), "caught": bool(r["caught"]), "trapped": bool(r["trapped"]),
            "timeout": bool(r["timeout"]), "sim_time": float(r["sim_time"]),
            "route_hops": int(r["route_hops"]),
            "moveto_commands": int(r.get("moveto_commands", 0)), "moveto_blocked": int(r.get("moveto_blocked", 0)),
            "approach_commands": int(r.get("approach_commands", 0)),
            "approach_blocked": int(r.get("approach_blocked", 0)),
            "net": bool(r.get("net", False)), "net_enter_time": float(r.get("net_enter_time", -1)),
            "room": r.get("room"), "room_type": r.get("room_type"),
        })
    return rows


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("folders", nargs="+")
    args = ap.parse_args()
    for f in args.folders:
        rows = load(Path(f))
        if not rows:
            print(f"{f}: no episodes", file=sys.stderr)
            continue
        (s,) = metrics.route_summary(rows)
        direct = [r for r in rows if r["route_hops"] == 0]
        detour = [r for r in rows if r["route_hops"] > 0]
        rate = lambda rs: f"{sum(r['success'] for r in rs)}/{len(rs)}" if rs else "-"
        times = sorted(r["sim_time"] for r in rows if r["success"])
        print(f"{Path(f).name}: n={s['n']} success={s['success']:.3f} caught={sum(r['caught'] for r in rows)} "
              f"timeout={sum(r['timeout'] for r in rows)} | direct {rate(direct)} detour {rate(detour)} "
              f"no-route {rate([r for r in rows if r['route_hops'] < 0])} | "
              f"feed time median {times[len(times) // 2] if times else float('nan'):.1f}s | "
              f"moveto/ep {s['moveto_per_ep']:.2f} moveto_blocked {s['moveto_blocked']:.3f} "
              f"approach_blocked {s['approach_blocked']:.3f} | detour success {s['detour_success']:.3f} "
              f"(without MOVE_TO {s['detour_self']}) | net_entered {s['net_entered']:.3f} "
              f"(median {s['net_enter_s']:.1f}s) | efficiency {s['efficiency']:.2f}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
