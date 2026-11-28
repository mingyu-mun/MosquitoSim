import sys
from pathlib import Path

import numpy as np
import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "evaluation"))
from eval_loop import EvalConfig, EvalLoop, episode_index, room_quotas  # noqa: E402
from fake_env import FakeEnv  # noqa: E402
from mosquitosim.latency import LatencyMode  # noqa: E402
from mosquitosim.planner_client import PlanResult  # noqa: E402


def zero_policy(obs):
    return np.zeros((obs.shape[0], 3), np.float32)


def make_planner(text='{"subgoal": "MOVE_TO", "target": "P5"}', service=0.3):
    calls = []

    def plan(instruction, images, use_images):
        calls.append(use_images)
        return PlanResult(text, service, service)
    plan.calls = calls
    return plan


def run(condition, latency="measured", planner=None, episodes=8, rooms=4):
    env = FakeEnv(rooms=rooms)
    cfg = EvalConfig(condition, LatencyMode.parse(latency), episodes=episodes, rooms=rooms)
    res = EvalLoop(env, zero_policy, cfg, planner).run()
    return env, res


def test_quotas_and_ids():
    assert room_quotas(30, 16) == [2] * 14 + [1] * 2
    assert sum(room_quotas(30, 16)) == 30
    assert episode_index("eval_net/s1/r03e0012") == 12


@pytest.mark.parametrize("cond,key", [("rule_oracle", "rule_command"), ("rule", "rule_command"),
                                      ("rule_masked", "rule_masked_command"), ("fixed", "fixed_command"),
                                      ("nearest", "nearest_command")])
def test_in_unity_commands_apply_without_delay(cond, key):
    env, res = run(cond)
    assert len(res.episodes) == 8
    counted = [d for d in res.decisions]
    assert counted and all(d["delay"] == 0.0 for d in counted)
    for d in counted:
        got = [c for c in env.received if c["episode"] == d["episode"] and c["decision"] == d["decision"]]
        assert got and got[0]["command"] == d[key]
        assert got[0]["t"] - d["requested_at"] <= 0.05 + 1e-9


def test_measured_latency_delays_command_by_service_time():
    planner = make_planner(service=0.3)
    env, res = run("vlm", "measured", planner)
    assert all(planner.calls)  # vlm sends images
    for d in res.decisions:
        assert d["valid"] and d["command"] == {"subgoal": "MOVE_TO", "target": "P5"}
        got = [c for c in env.received if c["episode"] == d["episode"] and c["decision"] == d["decision"]]
        lag = got[0]["t"] - d["requested_at"]
        assert 0.3 - 1e-9 <= lag <= 0.3 + 0.05 + 1e-9


def test_fixed_latency_and_noimg():
    planner = make_planner(service=0.01)
    env, res = run("vlm_noimg", "fixed:1.0", planner)
    assert not any(planner.calls)  # no images for the ablation
    for d in res.decisions:
        assert d["delay"] == 1.0
        got = [c for c in env.received if c["episode"] == d["episode"] and c["decision"] == d["decision"]]
        if got:  # a command due after the episode ended is dropped
            assert got[0]["t"] - d["requested_at"] >= 1.0 - 1e-9


def test_invalid_answer_keeps_previous_command():
    planner = make_planner(text='{"subgoal": "HIDE", "target": "P99"}')
    env, res = run("vlm", "measured", planner)
    assert res.decisions and not any(d["valid"] for d in res.decisions)
    counted = {d["episode"] for d in res.decisions}
    assert not [c for c in env.received if c["episode"] in counted]


def test_pending_commands_dropped_at_episode_end():
    # 2 s latency never fits before the 2 s episode ends at the halfway request
    planner = make_planner(service=1.9)
    env, res = run("vlm", "measured", planner)
    for c in env.received:
        ep_end = [e for e in res.episodes if e["episode"] == c["episode"]]
        if ep_end:
            assert c["t"] <= (episode_index(c["episode"]) + 1) * 40 * 0.05 + 1e-9


def test_realtime_mode_does_not_block():
    planner = make_planner(service=0.0)
    env, res = run("vlm", "realtime", planner, episodes=4)
    assert len(res.episodes) == 4
    assert res.decisions and all(d["valid"] for d in res.decisions)


def test_masked_rule_uses_the_view_and_hides_on_alert():
    env, res = run("rule_masked")
    assert res.decisions and all(d["condition"] == "rule_masked" for d in res.decisions)
    starts = [d for d in res.decisions if d["events"] == ["episode_start"]]
    alerts = [d for d in res.decisions if d["events"] == ["alert_start"]]
    assert starts and alerts
    # fake view: S1 nearest skin, P1 farthest from the head; the oracle (rule_command) says HIDE P2
    assert all(d["command"] == {"subgoal": "APPROACH", "target": "S1"} for d in starts)
    assert all(d["command"] == {"subgoal": "HIDE", "target": "P1"} for d in alerts)


def test_old_rule_name_maps_to_the_oracle():
    _, res = run("rule")
    assert res.decisions and all(d["condition"] == "rule_oracle" for d in res.decisions)


def test_probe_records_probe_fields_and_sends_nothing():
    env = FakeEnv(rooms=4, probe=True)
    cfg = EvalConfig("probe", LatencyMode.parse("measured"), episodes=8, rooms=4)
    res = EvalLoop(env, zero_policy, cfg).run()
    assert len(res.episodes) == 8 and not res.decisions and not env.received
    e = res.episodes[0]
    assert e["condition"] == "probe" and e["room_type"] in ("LShape", "R5x4")
    assert {e["probe_outcome"] for e in res.episodes} == {"achieved", "timeout"}


def test_probe_rejects_planner_requests():
    env = FakeEnv(rooms=2)  # sends requests like an external-planner build
    cfg = EvalConfig("probe", LatencyMode.parse("measured"), episodes=2, rooms=2)
    with pytest.raises(RuntimeError):
        EvalLoop(env, zero_policy, cfg).run()


def test_room_type_is_recorded_for_every_condition():
    _, res = run("fixed")
    assert all(e["room_type"] for e in res.episodes)
