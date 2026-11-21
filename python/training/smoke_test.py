"""Random-policy smoke test against a Unity build or the Editor (WBS 1.8).

Checks the observation shape and values on every step and reports
throughput. Run with the Editor in Play mode by omitting --env.

    python python/training/smoke_test.py --env builds/Stage0/MosquitoSim.x86_64 --steps 5000
"""
from __future__ import annotations

import argparse
import sys
import time
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from mosquitosim import obs_spec  # noqa: E402


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--env", default=None, help="build path; omit to connect to the Editor")
    ap.add_argument("--steps", type=int, default=2000)
    ap.add_argument("--time-scale", type=float, default=20.0)
    ap.add_argument("--seed", type=int, default=0)
    args = ap.parse_args()

    from mlagents_envs.environment import UnityEnvironment
    from mlagents_envs.base_env import ActionTuple
    from mlagents_envs.side_channel.engine_configuration_channel import EngineConfigurationChannel

    engine = EngineConfigurationChannel()
    env = UnityEnvironment(file_name=args.env, seed=args.seed, side_channels=[engine],
                           no_graphics=args.env is not None)
    engine.set_configuration_parameters(time_scale=args.time_scale)
    env.reset()
    name = next(iter(env.behavior_specs))
    spec = env.behavior_specs[name]
    obs_shape = spec.observation_specs[0].shape
    print(f"behavior {name}: obs {obs_shape}, actions {spec.action_spec}")
    if obs_shape != (obs_spec.OBS_SIZE,):
        print(f"FAIL: obs size {obs_shape} != {obs_spec.OBS_SIZE}")
        return 1

    rng = np.random.default_rng(args.seed)
    problems, episodes, decisions = {}, 0, 0
    t0 = time.time()
    for _ in range(args.steps):
        dec, term = env.get_steps(name)
        episodes += len(term)
        for batch in (dec, term):
            for o in batch.obs[0]:
                for p in obs_spec.check(o):
                    problems[p] = problems.get(p, 0) + 1
        n = len(dec)
        decisions += n
        if n:
            act = rng.uniform(-1, 1, size=(n, spec.action_spec.continuous_size)).astype(np.float32)
            env.set_actions(name, ActionTuple(continuous=act))
        env.step()
    dt = time.time() - t0
    env.close()

    print(f"{decisions} decisions, {episodes} episodes ended, {decisions / dt:.0f} decisions/s")
    if problems:
        for p, c in problems.items():
            print(f"PROBLEM x{c}: {p}")
        return 1
    print("OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
