import csv
import sys
from pathlib import Path

import numpy as np
import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "evaluation"))
import metrics  # noqa: E402
from mosquitosim.latency import CommandScheduler, LatencyMode  # noqa: E402


def test_latency_modes():
    assert LatencyMode.parse("measured").delay(0.42) == 0.42
    assert LatencyMode.parse("fixed:0.25").delay(5.0) == 0.25
    assert LatencyMode.parse("FIXED:0").delay(1.0) == 0.0
    assert LatencyMode.parse("realtime").time_scale == 1.0
    assert LatencyMode.parse("measured").time_scale == 20.0
    assert str(LatencyMode.parse("fixed:0.5")) == "fixed:0.5"
    for bad in ("fast", "fixed:-1", "fixed:x"):
        with pytest.raises(ValueError):
            LatencyMode.parse(bad)


def test_scheduler_order_and_drop():
    s = CommandScheduler()
    s.schedule(0, "e0", 1, {"a": 1}, 0.30)
    s.schedule(1, "e1", 0, {"b": 1}, 0.10)
    s.schedule(0, "e0", 2, {"c": 1}, 0.10)
    assert s.pop_ready(0.05) == []
    assert [x[2] for x in s.pop_ready(0.1)] == [0, 2]  # same time: scheduling order
    s.drop_room(0)
    assert len(s) == 0


def test_paired_bootstrap():
    a = [1, 1, 1, 0] * 10
    b = [1, 0, 0, 0] * 10
    mean, lo, hi = metrics.paired_bootstrap(a, b, n_boot=2000)
    assert mean == pytest.approx(0.5)
    assert lo < 0.5 < hi and lo > 0.2
    with pytest.raises(ValueError):
        metrics.paired_bootstrap([1], [1, 0])


def _write(root, name, rows):
    d = root / name
    d.mkdir(parents=True)
    with open(d / "episodes.csv", "w", newline="") as f:
        w = csv.DictWriter(f, fieldnames=["episode", "success", "caught", "trapped", "timeout", "sim_time"])
        w.writeheader()
        w.writerows(rows)


def test_load_summarize_compare(tmp_path):
    eps = [f"eval_net/s1/r00e{k:04d}" for k in range(6)]
    _write(tmp_path, "rule__net__p1s1__measured",
           [{"episode": e, "success": "True", "caught": "False", "trapped": "False", "timeout": "False", "sim_time": 10}
            for e in eps])
    _write(tmp_path, "vlm__net__p1s1__measured",
           [{"episode": e, "success": str(i % 2 == 0), "caught": "False", "trapped": "False",
             "timeout": str(i % 2 == 1), "sim_time": 20} for i, e in enumerate(eps)])
    rows = metrics.load_runs(tmp_path)
    assert len(rows) == 12
    summ = {s["condition"]: s for s in metrics.summarize(rows)}
    assert summ["rule"]["success"] == 1.0 and summ["vlm"]["success"] == 0.5
    cmp = metrics.compare(rows, "rule")
    assert len(cmp) == 1 and cmp[0]["diff"] == pytest.approx(-0.5) and cmp[0]["n"] == 6
