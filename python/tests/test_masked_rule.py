"""The masked rule planner sees exactly the VLM's view and nothing more."""
import re
from pathlib import Path

from mosquitosim.masked_rule import POINT_KEYS, SKIN_KEYS, VIEW_KEYS, masked_rule

ROOT = Path(__file__).resolve().parents[2]


def view(**over):
    v = {
        "stage": 2, "situation": "", "sim_time": 3.0, "episode_seconds": 60.0, "human_walking": False, "alert": False,
        "hand_active": False, "hand_dist": 0.0, "hand_closing": 0.0, "landed": False,
        "feed_progress": 0.0, "feed_time": 3.0,
        "skins": [{"id": "S1", "name": "머리", "dist": 1.2}, {"id": "S2", "name": "왼팔", "dist": 0.7},
                  {"id": "S4", "name": "왼쪽 종아리", "dist": 2.0}],
        "candidates": [{"id": "P1", "dist": 0.6, "head_dist": 1.0, "height": 1.5},
                       {"id": "P2", "dist": 2.4, "head_dist": 1.8, "height": 1.0},
                       {"id": "P3", "dist": 1.1, "head_dist": 2.6, "height": 0.5}],
        "current_command": {"subgoal": "APPROACH", "target": "S1"},
    }
    v.update(over)
    return v


class Watched(dict):
    """A dict that records which keys were read."""

    def __init__(self, d, seen, prefix=""):
        super().__init__({k: wrap(v, seen, prefix + k + ".") for k, v in d.items()})
        self._seen, self._prefix = seen, prefix

    def __getitem__(self, k):
        self._seen.add(self._prefix + k)
        return super().__getitem__(k)

    def get(self, k, default=None):
        self._seen.add(self._prefix + k)
        return super().get(k, default)


def wrap(v, seen, prefix):
    if isinstance(v, dict):
        return Watched(v, seen, prefix)
    if isinstance(v, list):
        return [wrap(x, seen, prefix) for x in v]
    return v


def test_evade_when_the_hand_is_close_from_stage_2():
    assert masked_rule(view(hand_active=True, hand_dist=0.8)) == {"subgoal": "EVADE"}
    assert masked_rule(view(hand_active=True, hand_dist=1.3))["subgoal"] != "EVADE"
    assert masked_rule(view(stage=1, hand_active=True, hand_dist=0.5))["subgoal"] != "EVADE"


def test_hide_at_the_candidate_farthest_from_the_head_when_alert():
    assert masked_rule(view(alert=True)) == {"subgoal": "HIDE", "target": "P3"}
    tie = [{"id": "P1", "dist": 2.0, "head_dist": 2.0, "height": 1.0},
           {"id": "P2", "dist": 1.0, "head_dist": 2.0, "height": 1.0}]
    assert masked_rule(view(alert=True, candidates=tie))["target"] == "P2"
    assert masked_rule(view(stage=1, alert=True))["subgoal"] == "APPROACH"
    assert masked_rule(view(alert=True, candidates=[]))["subgoal"] == "APPROACH"


def test_hide_scenario_counts_the_alert_from_stage_1():
    assert masked_rule(view(stage=1, alert=True, situation="hide")) == {"subgoal": "HIDE", "target": "P3"}
    assert masked_rule(view(stage=1, alert=True))["subgoal"] == "APPROACH"


def test_approach_the_nearest_skin_and_never_move_to():
    assert masked_rule(view()) == {"subgoal": "APPROACH", "target": "S2"}
    for alert in (False, True):
        for hand in (0.3, 2.0):
            cmd = masked_rule(view(alert=alert, hand_active=True, hand_dist=hand))
            assert cmd["subgoal"] != "MOVE_TO"


def test_reads_only_view_keys():
    for v in (view(), view(alert=True), view(hand_active=True, hand_dist=0.5)):
        seen = set()
        masked_rule(Watched(v, seen))
        top = {k for k in seen if "." not in k}
        skin = {k.split(".", 1)[1] for k in seen if k.startswith("skins.")}
        point = {k.split(".", 1)[1] for k in seen if k.startswith("candidates.")}
        assert top <= VIEW_KEYS and skin <= SKIN_KEYS and point <= POINT_KEYS


def test_unity_view_has_exactly_these_keys():
    """PlannerView.WriteJson in Unity must write the keys listed here: nothing
    privileged (hidden, clear_path, covered, near_trap, alert time) may slip in."""
    src = (ROOT / "unity/Assets/Scripts/Agent/StateDescriber.cs").read_text(encoding="utf-8")
    body = src[src.index("public void WriteJson"):src.index("public static class StateDescriber")]
    keys = set(re.findall(r'Key\("(\w+)"\)', body))
    assert keys == VIEW_KEYS | SKIN_KEYS | POINT_KEYS
    for bad in ("hidden", "clear_path", "covered", "near_trap", "excluded", "pos", "remaining"):
        assert bad not in keys


def test_skips_skins_whose_approach_failed():
    v = view()
    v["skins"] = [{"id": "S1", "name": "a", "dist": 0.5, "failed": True},
                  {"id": "S2", "name": "b", "dist": 0.9, "failed": False}]
    assert masked_rule(v) == {"subgoal": "APPROACH", "target": "S2"}
    for s in v["skins"]:
        s["failed"] = True
    assert masked_rule(v) == {"subgoal": "APPROACH", "target": "S1"}
