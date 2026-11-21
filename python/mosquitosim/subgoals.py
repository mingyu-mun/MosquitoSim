"""Planner commands: subgoal + target ID (interface v3, 11/21).

A planner answers with one JSON object:

    {"subgoal": "APPROACH", "target": "S2"}   skin zone S1..S5
    {"subgoal": "HIDE",     "target": "P3"}   candidate point P1..P8
    {"subgoal": "MOVE_TO",  "target": "P6"}   candidate point P1..P8
    {"subgoal": "EVADE"}                      target ignored

The low-level policy is unchanged: the chosen ID only decides which point goes
into the "subgoal target" observation slot. Anything that does not parse, or
names an ID that was not offered at this decision, is an invalid response: it
is logged and the previous command is kept.
"""
from __future__ import annotations

import json
import re
from dataclasses import dataclass
from enum import IntEnum
from typing import Collection, Optional


class Subgoal(IntEnum):
    APPROACH = 0
    HIDE = 1
    EVADE = 2    # trained and evaluated from stage 2 (hand)
    MOVE_TO = 3  # adopted 11/21: go to a candidate point (e.g. open side of a net)


# What each subgoal targets: "skin", "point" or None (no target).
TARGET_KIND = {
    Subgoal.APPROACH: "skin",
    Subgoal.HIDE: "point",
    Subgoal.MOVE_TO: "point",
    Subgoal.EVADE: None,
}

SKIN_IDS = ("S1", "S2", "S3", "S4", "S5")  # head, left/right forearm, left/right calf
SKIN_NAMES = {"S1": "머리", "S2": "왼팔", "S3": "오른팔", "S4": "왼쪽 종아리", "S5": "오른쪽 종아리"}
NUM_CANDIDATE_POINTS = 8                   # P1..P8, renumbered at every decision

# Subgoals the random training planner draws from, per stage (WAIT removed 11/21).
TRAINING_SUBGOALS = {
    0: (Subgoal.APPROACH,),
    1: (Subgoal.APPROACH, Subgoal.HIDE, Subgoal.MOVE_TO),
    2: (Subgoal.APPROACH, Subgoal.HIDE, Subgoal.MOVE_TO, Subgoal.EVADE),
    3: (Subgoal.APPROACH, Subgoal.HIDE, Subgoal.MOVE_TO, Subgoal.EVADE),
}

SUBGOAL_TIMEOUT_S = 8.0
FEED_TIME_S = 3.0
HIDE_RADIUS_M, HIDE_HOLD_S = 0.15, 1.0
MOVE_TO_RADIUS_M = 0.15
EVADE_DISTANCE_M = 1.5

_JSON_RE = re.compile(r"\{[^{}]*\}")


@dataclass(frozen=True)
class Command:
    subgoal: Subgoal
    target: Optional[str] = None  # "S1".."S5", "P1".."P8" or None for EVADE

    def to_json(self) -> str:
        d = {"subgoal": self.subgoal.name}
        if self.target is not None:
            d["target"] = self.target
        return json.dumps(d)


def point_ids(n: int = NUM_CANDIDATE_POINTS):
    return tuple(f"P{i}" for i in range(1, n + 1))


def parse_planner_output(text: str,
                         offered_points: Collection[str] = point_ids(),
                         offered_skins: Collection[str] = SKIN_IDS) -> Optional[Command]:
    """Return the command in a planner response, or None if it is invalid.

    Accepts the first JSON object in the text so that a model wrapping its
    answer in a code fence or a sentence still parses. ``offered_points`` and
    ``offered_skins`` are the IDs shown at this decision.
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
    if not isinstance(obj, dict) or not isinstance(obj.get("subgoal"), str):
        return None
    try:
        sg = Subgoal[obj["subgoal"].strip().upper()]
    except KeyError:
        return None

    kind = TARGET_KIND[sg]
    if kind is None:
        return Command(sg)  # EVADE: any target is ignored

    target = obj.get("target")
    if not isinstance(target, str):
        return None
    target = target.strip().upper()
    allowed = offered_skins if kind == "skin" else offered_points
    if target not in {t.upper() for t in allowed}:
        return None
    return Command(sg, target)
