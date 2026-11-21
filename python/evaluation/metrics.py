"""Summaries and paired bootstrap differences over evaluate.py outputs.

    python python/evaluation/metrics.py results/eval --baseline rule

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
                             "sim_time": float(r["sim_time"] or 0)})
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
    ap.add_argument("--baseline", default="rule")
    ap.add_argument("--metric", default="success", choices=OUTCOMES)
    args = ap.parse_args()
    rows = load_runs(Path(args.root))
    if not rows:
        print("no episodes.csv found", file=sys.stderr)
        return 1
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
