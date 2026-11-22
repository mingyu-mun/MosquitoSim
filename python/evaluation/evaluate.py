"""Paired evaluation of one planner condition on one split.

    python python/evaluation/evaluate.py --env builds/Main/MosquitoSim.x86_64 \
        --policy results/stage2_s1/Mosquito.onnx --stage 2 --split new_object \
        --condition vlm --latency measured --eval-seed 1 --episodes 30 \
        --planner-url http://127.0.0.1:8008 --out results/eval

Writes <out>/<condition>__<split>__p<policy_seed>s<eval_seed>__<latency>/episodes.csv,
decisions.jsonl and meta.json. Every condition with the same --eval-seed gets
the same episodes (layouts are seeded by eval seed, split, room and episode).
"""
from __future__ import annotations

import argparse
import csv
import json
import sys
from pathlib import Path
from typing import List, Tuple

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from eval_loop import (CONDITION_ALIASES, CONDITIONS, EPISODE_FIELDS, NO_LATENCY_CONDITIONS,  # noqa: E402
                       PROBE_FIELDS, EvalConfig, EvalLoop)
from mosquitosim.channel import MessageQueue, config_message, make_side_channel  # noqa: E402
from mosquitosim.latency import LatencyMode  # noqa: E402

SPLITS = {"val": 1, "familiar": 2, "new_room": 3, "new_object": 4, "new_human": 5, "net": 6}
PLANNER_EXTERNAL = 3.0
PLANNER_PROBE = 4.0
# --probe-subgoal -> Unity's probe_subgoal (Subgoal enum values; -1 cycles through the stage's subgoals)
PROBE_SUBGOALS = {"cycle": -1, "APPROACH": 0, "HIDE": 1, "EVADE": 2, "MOVE_TO": 3}


class MLAgentsEnv:
    """EnvAdapter over mlagents_envs.UnityEnvironment with the planner channel."""

    def __init__(self, file_name, params: dict, time_scale: float, image_dir: str,
                 capture_images: bool, image_size: int, no_graphics: bool, seed: int, port: int) -> None:
        from mlagents_envs.environment import UnityEnvironment
        from mlagents_envs.side_channel.engine_configuration_channel import EngineConfigurationChannel
        from mlagents_envs.side_channel.environment_parameters_channel import EnvironmentParametersChannel
        from mlagents_envs.side_channel.stats_side_channel import StatsSideChannel

        self.queue = MessageQueue()
        self.channel = make_side_channel(self.queue)
        engine = EngineConfigurationChannel()
        env_params = EnvironmentParametersChannel()
        self.env = UnityEnvironment(file_name=file_name, seed=seed, no_graphics=no_graphics,
                                    side_channels=[engine, env_params, self.channel, StatsSideChannel()],
                                    base_port=port)
        engine.set_configuration_parameters(time_scale=time_scale)
        for k, v in params.items():
            env_params.set_float_parameter(k, float(v))
        self.channel.send(config_message(image_dir, log=True, capture_images=capture_images,
                                         image_size=image_size))
        self.env.reset()
        self.name = next(iter(self.env.behavior_specs))

    def get_obs(self) -> Tuple[np.ndarray, np.ndarray]:
        dec, _ = self.env.get_steps(self.name)
        return dec.agent_id, dec.obs[0]

    def set_actions(self, agent_ids, actions) -> None:
        from mlagents_envs.base_env import ActionTuple
        self.env.set_actions(self.name, ActionTuple(continuous=np.asarray(actions, dtype=np.float32)))

    def step(self) -> None:
        self.env.step()

    def messages(self) -> List[dict]:
        return self.queue.pop_all()

    def send(self, msg: dict) -> None:
        self.channel.send(msg)

    def close(self) -> None:
        self.env.close()


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--env", default=None, help="Unity build; omit to connect to the Editor (Play)")
    ap.add_argument("--policy", required=True, help="ONNX policy exported by mlagents-learn")
    ap.add_argument("--stage", type=int, required=True)
    ap.add_argument("--split", choices=sorted(SPLITS), required=True)
    ap.add_argument("--condition", choices=CONDITIONS + tuple(CONDITION_ALIASES), required=True)
    ap.add_argument("--latency", default="measured", help="measured | fixed:<s> | realtime")
    ap.add_argument("--eval-seed", type=int, default=1)
    ap.add_argument("--policy-seed", type=int, default=1, help="training seed of --policy (for pairing)")
    ap.add_argument("--episodes", type=int, default=30)
    ap.add_argument("--rooms", type=int, default=16)
    ap.add_argument("--planner-url", default="http://127.0.0.1:8008")
    ap.add_argument("--image-size", type=int, default=448)
    ap.add_argument("--out", default="results/eval")
    ap.add_argument("--port", type=int, default=5105)
    ap.add_argument("--probe-subgoal", choices=list(PROBE_SUBGOALS), default="cycle",
                    help="--condition probe: the subgoal to check")
    ap.add_argument("--scenario", choices=("none", "hide"), default="none",
                    help="hide: every episode is the HIDE scenario; the condition's planner picks the spot")
    ap.add_argument("--null-policy", action="store_true",
                    help="act with zeros instead of --policy (do-nothing baseline for the probe)")
    args = ap.parse_args()

    latency = LatencyMode.parse(args.latency)
    args.condition = CONDITION_ALIASES.get(args.condition, args.condition)
    if args.condition in NO_LATENCY_CONDITIONS and latency.kind != "measured":
        print(f"note: {args.condition} has no planner latency; --latency is ignored")
    probe = args.condition == "probe"
    label = args.condition
    if probe:
        label = f"probe-{args.probe_subgoal}" + ("-null" if args.null_policy else "")
    elif args.scenario == "hide":
        label = f"hide-{args.condition}"
    run_name = (f"{label}__{args.split}__p{args.policy_seed}s{args.eval_seed}__"
                f"{str(latency).replace(':', '')}")
    out = Path(args.out) / run_name
    out.mkdir(parents=True, exist_ok=True)

    from mosquitosim.onnx_policy import OnnxPolicy
    from mosquitosim.planner_client import PlannerClient
    policy = (lambda obs: np.zeros((len(obs), 3), np.float32)) if args.null_policy else OnnxPolicy(args.policy)
    planner = PlannerClient(args.planner_url).plan if args.condition.startswith("vlm") else None

    params = {
        "stage": args.stage, "env_split": SPLITS[args.split],
        "planner": PLANNER_PROBE if probe else PLANNER_EXTERNAL,
        "probe_subgoal": PROBE_SUBGOALS[args.probe_subgoal],
        "hide_scenario": 1.0 if args.scenario == "hide" and not probe else 0.0,
        "eval_seed": args.eval_seed, "landing_radius": 0.02, "landing_speed": 0.3, "feed_time": 3.0,
        "time_penalty": 0.005, "prev_stage_mix": 0.0,
    }
    needs_images = args.condition == "vlm"
    env = MLAgentsEnv(args.env, params, latency.time_scale, str((out / "images").resolve()),
                      capture_images=needs_images, image_size=args.image_size,
                      no_graphics=not needs_images, seed=args.eval_seed, port=args.port)

    decisions_f = open(out / "decisions.jsonl", "w", encoding="utf-8")
    cfg = EvalConfig(args.condition, latency, args.episodes, args.rooms)
    loop = EvalLoop(env, policy, cfg, planner,
                    on_decision=lambda d: decisions_f.write(json.dumps(d, ensure_ascii=False) + "\n"),
                    on_episode=lambda e: print(f"{e['episode']}: success={e['success']} caught={e['caught']}"))
    try:
        result = loop.run()
    finally:
        decisions_f.close()
        env.close()

    fields = ["condition", "latency", *EPISODE_FIELDS, *PROBE_FIELDS]
    with open(out / "episodes.csv", "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=fields)
        w.writeheader()
        for e in sorted(result.episodes, key=lambda e: e["episode"]):
            w.writerow({k: e.get(k) for k in fields})
    meta = {**vars(args), "run": run_name, "steps": result.steps, "sim_time": result.sim_time,
            "wall_time": result.wall_time, "n_episodes": len(result.episodes),
            "n_decisions": len(result.decisions)}
    (out / "meta.json").write_text(json.dumps(meta, indent=2, ensure_ascii=False), encoding="utf-8")
    ok = sum(1 for e in result.episodes if e["success"])
    print(f"{run_name}: {ok}/{len(result.episodes)} success, {result.wall_time:.0f}s wall -> {out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
