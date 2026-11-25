"""Summaries and paired bootstrap differences over evaluate.py outputs.

    python python/evaluation/metrics.py results/eval --baseline rule_masked
    python python/evaluation/metrics.py results/eval --probe     # direct subgoal checks and HIDE scenarios

Each run directory holds episodes.csv. Episodes are paired across conditions
by (split, policy seed + eval seed, episode id), which evaluate.py makes
identical for every condition with the same --policy and --eval-seed.
"""
from __future__ import annotations

import argparse
import csv
import sys
from collections import defaultdict
from pathlib import Path
from typing import Dict, Iterable, List, Sequence, Tuple

import numpy as np

OUTCOMES = ("success", "caught", "trapped", "timeout")
PROBE_OUTCOMES = ("achieved", "timeout", "spotted", "caught", "trapped", "wrong_skin")
PASS_RATE = 0.8  # stage pass: mean over policy seeds of each subgoal's achieved rate


def _bool(v) -> float:
    return 1.0 if str(v).strip().lower() in ("true", "1") else 0.0


def load_runs(root: Path) -> List[dict]:
    rows = []
    for csv_path in sorted(Path(root).glob("*/episodes.csv")):
        condition, split, seed, latency = csv_path.parent.name.split("__")
        with open(csv_path, encoding="utf-8") as f:
            for r in csv.DictReader(f):
                rows.append({"condition": condition, "split": split, "seed": seed, "latency": latency,
                             "episode": r["episode"], **{k: _bool(r[k]) for k in OUTCOMES},
                             "sim_time": float(r["sim_time"] or 0),
                             "room_type": r.get("room_type") or "",
                             "probe_subgoal": r.get("probe_subgoal") or "",
                             # route benchmark (11/25); absent in older logs
                             "route_hops": int(float(r["route_hops"])) if r.get("route_hops") not in (None, "") else None,
                             "moveto_commands": int(float(r.get("moveto_commands") or 0)),
                             "moveto_blocked": int(float(r.get("moveto_blocked") or 0)),
                             "approach_commands": int(float(r.get("approach_commands") or 0)),
                             "approach_blocked": int(float(r.get("approach_blocked") or 0)),
                             "net": _bool(r.get("net")),
                             "net_enter_time": float(r.get("net_enter_time") or -1),
                             "probe_outcome": r.get("probe_outcome") or "",
                             "probe_time": float(r.get("probe_time") or 0),
                             "probe_path_blocked": _bool(r.get("probe_path_blocked")),
                             # None when the column is absent (older logs): not treated as "no hidden spot".
                             "probe_target_hidden": _bool(r["probe_target_hidden"]) if r.get("probe_target_hidden") else None,
                             # None when absent (logs before 11/22): arrival unknown.
                             "probe_arrived": _bool(r["probe_arrived"]) if r.get("probe_arrived") else None})
    return rows


def summarize(rows: Iterable[dict]) -> List[dict]:
    groups: Dict[Tuple[str, str, str], List[dict]] = defaultdict(list)
    for r in rows:
        groups[(r["split"], r["condition"], r["latency"])].append(r)
    out = []
    for (split, cond, lat), rs in sorted(groups.items()):
        rec = {"split": split, "condition": cond, "latency": lat, "n": len(rs)}
        for k in OUTCOMES:
            rec[k] = float(np.mean([r[k] for r in rs]))
        ok = [r["sim_time"] for r in rs if r["success"]]
        rec["time_to_feed"] = float(np.mean(ok)) if ok else float("nan")
        out.append(rec)
    return out


def route_summary(rows: Iterable[dict]) -> List[dict]:
    """Route-finding benchmark per split x condition x latency (11/25).

    route_found: a straight-leg route over the candidates existed at the first decision (route_hops >= 0);
    success_if_route: feeding success among those episodes. moveto_blocked / approach_blocked: share of
    commands that were not a straight flight when given (the planner sent the mosquito into a wall).
    net_entered / net_enter_s: net rooms only. efficiency: shortest route hops / MOVE_TO commands used,
    over successful episodes that needed a detour (1 = no extra hops).
    """
    groups: Dict[Tuple[str, str, str], List[dict]] = defaultdict(list)
    for r in rows:
        if r.get("route_hops") is None:
            continue
        groups[(r["split"], r["condition"], r["latency"])].append(r)
    out = []
    for (split, cond, lat), rs in sorted(groups.items()):
        found = [r for r in rs if r["route_hops"] >= 0]
        mt = sum(r["moveto_commands"] for r in rs)
        ap = sum(r["approach_commands"] for r in rs)
        nets = [r for r in rs if r["net"]]
        entered = [r for r in nets if r["net_enter_time"] >= 0]
        detours = [r for r in found if r["route_hops"] > 0 and r["success"]]
        nan = float("nan")
        out.append({
            "split": split, "condition": cond, "latency": lat, "n": len(rs),
            "success": float(np.mean([r["success"] for r in rs])),
            "route_found": len(found) / len(rs),
            "success_if_route": float(np.mean([r["success"] for r in found])) if found else nan,
            "moveto_per_ep": mt / len(rs),
            "moveto_blocked": sum(r["moveto_blocked"] for r in rs) / mt if mt else nan,
            "approach_blocked": sum(r["approach_blocked"] for r in rs) / ap if ap else nan,
            "net_n": len(nets),
            "net_entered": len(entered) / len(nets) if nets else nan,
            "net_enter_s": float(np.median([r["net_enter_time"] for r in entered])) if entered else nan,
            "efficiency": float(np.mean([min(1.0, r["route_hops"] / max(1, r["moveto_commands"])) for r in detours]))
            if detours else nan,
        })
    return out


def bootstrap_mean(x: Sequence[float], n_boot: int = 10000, seed: int = 0) -> Tuple[float, float, float]:
    """Mean of x and its 95% percentile interval."""
    x = np.asarray(x, float)
    if x.size == 0:
        return float("nan"), float("nan"), float("nan")
    rng = np.random.default_rng(seed)
    boots = x[rng.integers(0, x.size, size=(n_boot, x.size))].mean(axis=1)
    lo, hi = np.percentile(boots, [2.5, 97.5])
    return float(x.mean()), float(lo), float(hi)


def is_check_run(condition: str) -> bool:
    """Direct subgoal checks and HIDE scenario runs (kept out of the feeding-success summary)."""
    return condition.startswith("probe") or condition.startswith("hide-")


def policy_seed(seed: str) -> str:
    """'p2s3' -> 'p2' (runs of one policy with different eval seeds pool together)."""
    return seed.split("s", 1)[0]


def probe_summary(rows: Iterable[dict]) -> Tuple[List[dict], List[dict]]:
    """Direct subgoal check. Returns (per subgoal x policy seed x run kind, per subgoal x run kind).

    Run kind: "probe" (direct check, ground-truth targets), "probe-null" (do-nothing baseline), or
    "hide-<condition>" (HIDE scenario with that planner choosing the spot). The pass verdict applies
    to "probe": mean over policy seeds >= PASS_RATE.

    HIDE counts differently by kind (11/25):
    - probe / probe-null (does the policy carry out HIDE?): achieved only if the mosquito reached the
      spot (probe_arrived) and was not spotted. Unspotted without arriving is not execution; it is
      counted apart as "unarrived_unspotted".
    - hide-<condition> (planner comparison): achieved = not spotted, as the task defines it; arrival
      and whether the chosen spot was hidden are reported beside it.
    """
    groups: Dict[Tuple[str, str, str, str], List[dict]] = defaultdict(list)
    skipped: Dict[Tuple[str, str, str, str], int] = defaultdict(int)
    for r in rows:
        if not r["probe_subgoal"] or not is_check_run(r["condition"]):
            continue
        cond = r["condition"]
        kind = cond if cond.startswith("hide-") else ("probe-null" if cond.endswith("-null") else "probe")
        if kind != "probe-null" and not kind.startswith("hide-") and r["probe_subgoal"] == "HIDE" \
                and r["probe_target_hidden"] is not None and not r["probe_target_hidden"]:
            # Ground-truth HIDE with no hidden candidate in the room: nothing to test, left out of the rate.
            skipped[(r["split"], kind, r["probe_subgoal"], policy_seed(r["seed"]))] += 1
            continue
        if kind != "probe-null" and r["probe_subgoal"] == "MOVE_TO" and r["probe_path_blocked"]:
            # MOVE_TO is a straight flight (11/25); a target behind an obstacle (only when no candidate was
            # reachable) tests the planner's routing, not the policy, so it is left out of the rate.
            skipped[(r["split"], kind, r["probe_subgoal"], policy_seed(r["seed"]))] += 1
            continue
        groups[(r["split"], kind, r["probe_subgoal"], policy_seed(r["seed"]))].append(r)
    per_seed = []
    for (split, kind, sub, pseed), rs in sorted(groups.items()):
        unspotted = [r["probe_outcome"] == "achieved" for r in rs]
        execution = sub == "HIDE" and not kind.startswith("hide-")
        if execution and any(r["probe_arrived"] is None for r in rs):
            print(f"warning: {split}/{kind}/HIDE/{pseed}: no probe_arrived column; "
                  f"HIDE counted without the arrival check", file=sys.stderr)
            execution = False
        ok = [1.0 if u and (not execution or r["probe_arrived"]) else 0.0 for u, r in zip(unspotted, rs)]
        rate, lo, hi = bootstrap_mean(ok)
        times = [r["probe_time"] for r in rs if r["probe_outcome"] == "achieved"]
        blocked = [x for x, r in zip(ok, rs) if r["probe_path_blocked"]]
        rec = {"split": split, "kind": kind, "subgoal": sub, "policy": pseed, "n": len(rs),
               "skipped": skipped[(split, kind, sub, pseed)],  # HIDE: no hidden spot; MOVE_TO: not reachable
               "achieved": rate, "ci_low": lo, "ci_high": hi,
               "median_s": float(np.median(times)) if times else float("nan"),
               "blocked_n": len(blocked), "blocked_achieved": float(np.mean(blocked)) if blocked else float("nan")}
        for o in PROBE_OUTCOMES:
            if o != "achieved":  # "achieved" is the counted rate above (HIDE: arrival checked)
                rec[o] = float(np.mean([r["probe_outcome"] == o for r in rs]))
        if sub == "HIDE":
            # Choice vs execution: did it pick a hidden spot, did it get there, and did hiding work once there.
            arrived = [u for u, r in zip(unspotted, rs) if r["probe_arrived"]]
            rec["chose_hidden"] = float(np.mean([bool(r["probe_target_hidden"]) for r in rs]))
            rec["arrived"] = len(arrived) / len(rs)
            rec["achieved_if_arrived"] = float(np.mean(arrived)) if arrived else float("nan")
            rec["unarrived_unspotted"] = sum(1 for u, r in zip(unspotted, rs) if u and not r["probe_arrived"])
        by_room: Dict[str, List[float]] = defaultdict(list)
        for x, r in zip(ok, rs):
            by_room[r["room_type"] or "?"].append(x)
        rec["by_room"] = {k: (float(np.mean(v)), len(v)) for k, v in sorted(by_room.items())}
        per_seed.append(rec)
    per_sub: Dict[Tuple[str, str, str], List[dict]] = defaultdict(list)
    for rec in per_seed:
        per_sub[(rec["split"], rec["kind"], rec["subgoal"])].append(rec)
    overall = []
    for (split, kind, sub), recs in sorted(per_sub.items()):
        mean = float(np.mean([r["achieved"] for r in recs]))
        overall.append({"split": split, "kind": kind, "subgoal": sub, "policies": len(recs),
                        "achieved_mean": mean,
                        "passed": (mean >= PASS_RATE) if kind == "probe" else None})
    return per_seed, overall


def paired_bootstrap(a: Sequence[float], b: Sequence[float], n_boot: int = 10000,
                     seed: int = 0) -> Tuple[float, float, float]:
    """Mean of (a - b) over paired episodes and its 95% percentile interval."""
    a, b = np.asarray(a, float), np.asarray(b, float)
    if a.shape != b.shape or a.size == 0:
        raise ValueError("need equally sized, non-empty paired samples")
    d = a - b
    rng = np.random.default_rng(seed)
    idx = rng.integers(0, d.size, size=(n_boot, d.size))
    boots = d[idx].mean(axis=1)
    lo, hi = np.percentile(boots, [2.5, 97.5])
    return float(d.mean()), float(lo), float(hi)


def compare(rows: List[dict], baseline: str, metric: str = "success") -> List[dict]:
    by_key: Dict[Tuple, Dict[str, float]] = defaultdict(dict)
    for r in rows:
        by_key[(r["split"], r["seed"], r["episode"])][f"{r['condition']}|{r['latency']}"] = r[metric]
    conds = sorted({k for v in by_key.values() for k in v})
    base = [c for c in conds if c.split("|")[0] == baseline]
    out = []
    for split in sorted({k[0] for k in by_key}):
        for b in base:
            for c in conds:
                if c == b:
                    continue
                pairs = [(v[c], v[b]) for k, v in by_key.items() if k[0] == split and c in v and b in v]
                if not pairs:
                    continue
                mean, lo, hi = paired_bootstrap([p[0] for p in pairs], [p[1] for p in pairs])
                out.append({"split": split, "condition": c, "baseline": b, "metric": metric,
                            "n": len(pairs), "diff": mean, "ci_low": lo, "ci_high": hi})
    return out


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("root")
    ap.add_argument("--baseline", default="rule_masked")
    ap.add_argument("--metric", default="success", choices=OUTCOMES)
    ap.add_argument("--probe", action="store_true", help="summarize direct subgoal checks instead")
    ap.add_argument("--route", action="store_true", help="route-finding benchmark (net rooms and detours)")
    args = ap.parse_args()
    rows = load_runs(Path(args.root))
    if not rows:
        print("no episodes.csv found", file=sys.stderr)
        return 1
    if args.route:
        rs = route_summary([r for r in rows if not is_check_run(r["condition"])])
        if not rs:
            print("no route fields found (logs before 11/25?)", file=sys.stderr)
            return 1
        print("split\tcondition\tlatency\tn\tsuccess\troute_found\tsuccess_if_route\tmoveto/ep\t"
              "moveto_blocked\tapproach_blocked\tnet_entered(n)\tnet_enter_s\tefficiency")
        for r in rs:
            print(f"{r['split']}\t{r['condition']}\t{r['latency']}\t{r['n']}\t{r['success']:.3f}\t{r['route_found']:.3f}\t"
                  f"{r['success_if_route']:.3f}\t{r['moveto_per_ep']:.2f}\t{r['moveto_blocked']:.3f}\t{r['approach_blocked']:.3f}\t"
                  f"{r['net_entered']:.3f}({r['net_n']})\t{r['net_enter_s']:.1f}\t{r['efficiency']:.2f}")
        return 0
    if args.probe:
        per_seed, overall = probe_summary(rows)
        if not per_seed:
            print("no probe runs found", file=sys.stderr)
            return 1
        print("split\tkind\tsubgoal\tpolicy\tn\tachieved\t95% CI\tmedian_s\ttimeout\tcaught\t"
              "blocked_achieved(n)\tskipped\tby_room")
        for r in per_seed:
            rooms = " ".join(f"{k}:{v[0]:.2f}({v[1]})" for k, v in r["by_room"].items())
            print(f"{r['split']}\t{r['kind']}\t{r['subgoal']}\t{r['policy']}\t{r['n']}\t{r['achieved']:.3f}\t"
                  f"[{r['ci_low']:.2f}, {r['ci_high']:.2f}]\t{r['median_s']:.1f}\t{r['timeout']:.2f}\t"
                  f"{r['caught']:.2f}\t{r['blocked_achieved']:.2f}({r['blocked_n']})\t{r['skipped']}\t{rooms}")
        hide = [r for r in per_seed if r["subgoal"] == "HIDE" and "arrived" in r]
        if hide:
            print("\nHIDE: probe counts only arrived + unspotted; hide-<condition> counts unspotted")
            print("split\tkind\tpolicy\tn\tachieved\tarrived\tachieved_if_arrived\tunarrived_unspotted\tchose_hidden")
            for r in hide:
                print(f"{r['split']}\t{r['kind']}\t{r['policy']}\t{r['n']}\t{r['achieved']:.3f}\t{r['arrived']:.3f}\t"
                      f"{r['achieved_if_arrived']:.3f}\t{r['unarrived_unspotted']}\t{r['chose_hidden']:.3f}")
        print(f"\nmean over policy seeds (pass: >= {PASS_RATE:.0%})")
        for o in overall:
            verdict = "" if o["passed"] is None else ("PASS" if o["passed"] else "FAIL")
            print(f"{o['split']}\t{o['kind']}\t{o['subgoal']}\tpolicies={o['policies']}\t"
                  f"{o['achieved_mean']:.3f}\t{verdict}")
        return 0
    rows = [r for r in rows if not is_check_run(r["condition"])]
    print("split\tcondition\tlatency\tn\tsuccess\tcaught\ttrapped\ttimeout\ttime_to_feed")
    for s in summarize(rows):
        print(f"{s['split']}\t{s['condition']}\t{s['latency']}\t{s['n']}\t{s['success']:.3f}\t"
              f"{s['caught']:.3f}\t{s['trapped']:.3f}\t{s['timeout']:.3f}\t{s['time_to_feed']:.1f}")
    print(f"\npaired difference in {args.metric} vs {args.baseline} (95% bootstrap CI)")
    for c in compare(rows, args.baseline, args.metric):
        print(f"{c['split']}\t{c['condition']} - {c['baseline']}\tn={c['n']}\t"
              f"{c['diff']:+.3f} [{c['ci_low']:+.3f}, {c['ci_high']:+.3f}]")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
