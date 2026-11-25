"""Masked rule planner: a hand-written rule that sees only what the VLM sees.

It reads nothing but the request's "view", the same structure Unity renders the
VLM instruction from (StateDescriber.PlannerView). So it cannot know whether a
candidate is hidden or has a clear path, whether a skin is covered, how long
the alert lasts, or anything about the scene geometry. Compare with the oracle
rule (condition "rule_oracle", Unity's Planners.Rule), which knows all of that
and serves as the upper reference.

Like the oracle rule, the masked rule is fixed before evaluation and not tuned
on evaluation results.
"""
from __future__ import annotations

from typing import Dict, Mapping

# Same as Planners.ThreatDistance in Unity.
THREAT_DISTANCE = 1.0

# Every key the masked rule may read. tests/test_masked_rule.py checks that it
# reads nothing else and that Unity's view carries nothing beyond these.
VIEW_KEYS = frozenset({
    "stage", "situation", "sim_time", "episode_seconds", "human_walking", "alert", "hand_active", "hand_dist",
    "hand_closing", "landed", "feed_progress", "feed_time", "skins", "candidates", "current_command",
})
SKIN_KEYS = frozenset({"id", "name", "dist", "failed"})
POINT_KEYS = frozenset({"id", "dist", "head_dist", "height"})


def masked_rule(view: Mapping) -> Dict[str, str]:
    """One command from the view, in the planner wire format.

    1. The hand is moving and within 1 m: EVADE (stage 2+).
    2. The human is alert (stage 2+, or the HIDE scenario, situation "hide"):
       HIDE at the candidate farthest from the human's
       head (ties: nearer to the mosquito). Without knowing which points are
       hidden, distance from the eyes is the best available guess.
    3. Otherwise APPROACH the nearest skin that has not failed this episode (all failed: the nearest).

    MOVE_TO is never chosen: deciding that the way to a skin is blocked needs
    scene geometry, which the view does not have.
    """
    stage = view["stage"]
    if stage >= 2 and view["hand_active"] and view["hand_dist"] < THREAT_DISTANCE:
        return {"subgoal": "EVADE"}

    candidates = view["candidates"]
    hide_scenario = view["situation"] == "hide"
    if (stage >= 2 or hide_scenario) and view["alert"] and candidates:
        far = max(candidates, key=lambda c: (c["head_dist"], -c["dist"]))
        return {"subgoal": "HIDE", "target": far["id"]}

    # Skip skins whose APPROACH already timed out this episode (the fixed and oracle planners do the same);
    # when every skin has failed, start over with all of them.
    skins = [s for s in view["skins"] if not s.get("failed")] or view["skins"]
    near = min(skins, key=lambda s: s["dist"])
    return {"subgoal": "APPROACH", "target": near["id"]}
