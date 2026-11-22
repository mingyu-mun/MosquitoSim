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


def _write_probe(root, name, outcomes, room="R5x4", blocked=None):
    d = root / name
    d.mkdir(parents=True)
    fields = ["episode", "success", "caught", "trapped", "timeout", "sim_time", "room_type",
              "probe_subgoal", "probe_outcome", "probe_time", "probe_path_blocked"]
    with open(d / "episodes.csv", "w", newline="") as f:
        w = csv.DictWriter(f, fieldnames=fields)
        w.writeheader()
        for i, (sub, out) in enumerate(outcomes):
            w.writerow({"episode": f"val/s1/r00e{i:04d}", "success": "False", "caught": str(out == "caught"),
                        "trapped": "False", "timeout": "False", "sim_time": 3, "room_type": room,
                        "probe_subgoal": sub, "probe_outcome": out, "probe_time": 2.0 if out == "achieved" else 8.0,
                        "probe_path_blocked": str(bool(blocked and blocked[i]))})


def test_probe_summary_rates_and_pass(tmp_path):
    # policy p1: HIDE 9/10, policy p2: HIDE 7/10 -> mean 0.8 -> PASS; MOVE_TO 5/10 and 6/10 -> FAIL
    _write_probe(tmp_path, "probe-HIDE__val__p1s1__measured", [("HIDE", "achieved")] * 9 + [("HIDE", "timeout")])
    _write_probe(tmp_path, "probe-HIDE__val__p2s1__measured",
                 [("HIDE", "achieved")] * 7 + [("HIDE", "caught")] * 3, room="LShape")
    _write_probe(tmp_path, "probe-cycle__val__p1s2__measured",
                 [("MOVE_TO", "achieved")] * 5 + [("MOVE_TO", "timeout")] * 5,
                 blocked=[False] * 5 + [True] * 5)
    _write_probe(tmp_path, "probe-cycle__val__p2s2__measured", [("MOVE_TO", "achieved")] * 6 + [("MOVE_TO", "timeout")] * 4)
    _write_probe(tmp_path, "probe-HIDE-null__val__p1s1__measured", [("HIDE", "timeout")] * 10)
    rows = metrics.load_runs(tmp_path)
    per_seed, overall = metrics.probe_summary(rows)
    by = {(r["kind"], r["subgoal"], r["policy"]): r for r in per_seed}
    assert by[("probe", "HIDE", "p1")]["achieved"] == pytest.approx(0.9)
    assert by[("probe", "HIDE", "p2")]["caught"] == pytest.approx(0.3)
    assert by[("probe", "HIDE", "p2")]["by_room"] == {"LShape": (0.7, 10)}
    assert by[("probe", "MOVE_TO", "p1")]["blocked_achieved"] == 0.0 and by[("probe", "MOVE_TO", "p1")]["blocked_n"] == 5
    lo, hi = by[("probe", "HIDE", "p1")]["ci_low"], by[("probe", "HIDE", "p1")]["ci_high"]
    assert lo < 0.9 <= hi
    res = {(o["kind"], o["subgoal"]): o for o in overall}
    assert res[("probe", "HIDE")]["achieved_mean"] == pytest.approx(0.8) and res[("probe", "HIDE")]["passed"]
    assert res[("probe", "MOVE_TO")]["passed"] is False
    assert res[("probe-null", "HIDE")]["passed"] is None and res[("probe-null", "HIDE")]["achieved_mean"] == 0.0


def test_probe_runs_stay_out_of_condition_summary(tmp_path):
    _write_probe(tmp_path, "probe-HIDE__val__p1s1__measured", [("HIDE", "achieved")] * 3)
    _write(tmp_path, "fixed__val__p1s1__measured",
           [{"episode": "val/s1/r00e0000", "success": "True", "caught": "False", "trapped": "False",
             "timeout": "False", "sim_time": 5}])
    rows = [r for r in metrics.load_runs(tmp_path) if not r["condition"].startswith("probe")]
    assert {s["condition"] for s in metrics.summarize(rows)} == {"fixed"}


def test_hide_scenario_runs_are_their_own_kind(tmp_path):
    _write_probe(tmp_path, "hide-vlm__val__p1s1__measured", [("HIDE", "achieved")] * 6 + [("HIDE", "spotted")] * 4)
    _write_probe(tmp_path, "hide-rule_masked__val__p1s1__measured", [("HIDE", "achieved")] * 3 + [("HIDE", "spotted")] * 7)
    per_seed, overall = metrics.probe_summary(metrics.load_runs(tmp_path))
    by = {r["kind"]: r for r in per_seed}
    assert by["hide-vlm"]["achieved"] == pytest.approx(0.6) and by["hide-vlm"]["spotted"] == pytest.approx(0.4)
    assert by["hide-rule_masked"]["achieved"] == pytest.approx(0.3)
    assert all(o["passed"] is None for o in overall)  # pass verdicts are for the direct check only
    assert not [r for r in metrics.load_runs(tmp_path) if not metrics.is_check_run(r["condition"])]
