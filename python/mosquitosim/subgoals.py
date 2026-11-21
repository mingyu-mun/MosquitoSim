"""Subgoal names and planner output parsing (interface v3).

Planner output is a JSON object such as {"subgoal": "HIDE"}. Anything else is
an invalid response: it is logged and the previous subgoal is kept.
"""
from __future__ import annotations

import json
import re
from enum import IntEnum
from typing import Optional


class Subgoal(IntEnum):
    APPROACH = 0
    HIDE = 1
    EVADE = 2    # trained and evaluated from stage 2 (hand)
    MOVE_TO = 3  # reserved slot; only used if MOVE_TO is adopted


ACTIVE_SUBGOALS = (Subgoal.APPROACH, Subgoal.HIDE, Subgoal.EVADE)

# Subgoals the random training planner draws from, per stage (WAIT removed 11/21).
TRAINING_SUBGOALS = {
    0: (Subgoal.APPROACH,),
    1: (Subgoal.APPROACH, Subgoal.HIDE),
    2: (Subgoal.APPROACH, Subgoal.HIDE, Subgoal.EVADE),
    3: (Subgoal.APPROACH, Subgoal.HIDE, Subgoal.EVADE),
}

SUBGOAL_TIMEOUT_S = 8.0
FEED_TIME_S = 3.0
HIDE_RADIUS_M, HIDE_HOLD_S = 0.15, 1.0
EVADE_DISTANCE_M = 1.5

_JSON_RE = re.compile(r"\{[^{}]*\}")


def parse_planner_output(text: str, allow_move_to: bool = False) -> Optional[Subgoal]:
    """Return the subgoal in a planner response, or None if it is invalid.

    Accepts the first JSON object in the text so that a model wrapping its
    answer in a code fence still parses; rejects anything else.
    """
    if not isinstance(text, str):
        return None
    match = _JSON_RE.search(text)
    if not match:
        return None
    try:
        obj = json.loads(match.group(0))
    except json.JSONDecodeError:
        return None
    name = obj.get("subgoal") if isinstance(obj, dict) else None
    if not isinstance(name, str):
        return None
    try:
        sg = Subgoal[name.strip().upper()]
    except KeyError:
        return None
    if sg is Subgoal.MOVE_TO and not allow_move_to:
        return None
    return sg
