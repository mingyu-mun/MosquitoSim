"""Low-level policy observation layout (interface v3, 59 dims).

This file is the single source of truth on the Python side. The Unity side
mirrors it in unity/Assets/Scripts/Agent/ObsLayout.cs, and
python/tests/test_cs_layout.py checks that the two agree.

All vectors are in world axes. Every value is scaled to roughly [-1, 1]
(or [0, 1]) by the divisor in ``scale``; elements that do not exist in the
current stage are written as 0.
"""
from __future__ import annotations

from dataclasses import dataclass
from itertools import product
from typing import Dict, List, Tuple

import numpy as np

# Physical constants shared with Unity (MosquitoMotor / ObsBuilder).
MAX_SPEED = 1.5          # m/s
MAX_ACCEL = 5.0          # m/s^2
RAY_LENGTH = 2.0         # m
POS_SCALE = 5.0          # m, relative positions and distances
HUMAN_VEL_SCALE = 2.0    # m/s
HAND_VEL_SCALE = 8.0     # m/s

NUM_SUBGOAL_SLOTS = 4    # APPROACH, HIDE, EVADE, MOVE_TO (reserved)


@dataclass(frozen=True)
class Field:
    name: str
    size: int
    scale: str
    absent_stages: str = ""


FIELDS: List[Field] = [
    Field("self_velocity", 3, "/MAX_SPEED"),
    Field("target_skin_rel_pos", 3, "/POS_SCALE"),
    Field("target_skin_dist", 1, "/POS_SCALE"),
    Field("subgoal_target_rel_pos", 3, "/POS_SCALE"),
    Field("subgoal_target_dist", 1, "/POS_SCALE"),
    Field("human_velocity", 3, "/HUMAN_VEL_SCALE", "0-1"),
    Field("human_alert", 1, "0/1", "0-1"),
    Field("hand_rel_pos", 3, "/POS_SCALE", "0-1"),
    Field("hand_velocity", 3, "/HAND_VEL_SCALE", "0-1"),
    Field("hand_active", 1, "0/1", "0-1"),
    Field("hazard_rel_pos", 3, "/POS_SCALE", "no hazard"),
    Field("hazard_active", 1, "0/1", "no hazard"),
    Field("hazard_exposure", 1, "exposure/limit", "no hazard"),
    Field("rays", 26, "/RAY_LENGTH (1 = no hit)"),
    Field("subgoal_onehot", NUM_SUBGOAL_SLOTS, "one-hot"),
    Field("remaining_time", 1, "fraction of episode left"),
    Field("landed", 1, "0/1"),
]


def _build_index() -> Dict[str, Tuple[int, int]]:
    index, start = {}, 0
    for f in FIELDS:
        index[f.name] = (start, start + f.size)
        start += f.size
    return index


INDEX: Dict[str, Tuple[int, int]] = _build_index()
OBS_SIZE: int = sum(f.size for f in FIELDS)


def ray_directions() -> np.ndarray:
    """26 unit directions: every (x, y, z) in {-1, 0, 1}^3 except the origin,
    in nested x, y, z order. ObsLayout.RayDirections uses the same order."""
    dirs = [d for d in product((-1, 0, 1), repeat=3) if d != (0, 0, 0)]
    arr = np.asarray(dirs, dtype=np.float64)
    return arr / np.linalg.norm(arr, axis=1, keepdims=True)


def split(obs: np.ndarray) -> Dict[str, np.ndarray]:
    """Name the parts of one observation vector (or a batch, last axis)."""
    obs = np.asarray(obs)
    if obs.shape[-1] != OBS_SIZE:
        raise ValueError(f"expected last dim {OBS_SIZE}, got {obs.shape[-1]}")
    return {name: obs[..., a:b] for name, (a, b) in INDEX.items()}


def check(obs: np.ndarray, atol: float = 1e-4) -> List[str]:
    """Return a list of problems with one observation (empty if fine)."""
    problems = []
    obs = np.asarray(obs, dtype=np.float64)
    if obs.shape != (OBS_SIZE,):
        return [f"shape {obs.shape} != ({OBS_SIZE},)"]
    if not np.all(np.isfinite(obs)):
        problems.append("non-finite values")
    if np.any(np.abs(obs) > 1.5):
        problems.append("values outside [-1.5, 1.5]; check scaling")
    parts = split(obs)
    onehot = parts["subgoal_onehot"]
    if not (np.isclose(onehot.sum(), 1.0, atol=atol) and set(np.unique(onehot)) <= {0.0, 1.0}):
        problems.append(f"subgoal one-hot invalid: {onehot}")
    for flag in ("human_alert", "hand_active", "hazard_active", "landed"):
        if parts[flag][0] not in (0.0, 1.0):
            problems.append(f"{flag} not 0/1: {parts[flag][0]}")
    rays = parts["rays"]
    if np.any(rays < -atol) or np.any(rays > 1 + atol):
        problems.append("ray distances outside [0, 1]")
    return problems
