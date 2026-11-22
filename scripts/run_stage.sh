#!/usr/bin/env bash
# Train one stage for several seeds.
# usage: scripts/run_stage.sh <stage> <unity_build_path> [seeds...]
#   scripts/run_stage.sh 0 builds/Main/MosquitoSim.x86_64 1 2 3
# Stage s > 0 continues from stage(s-1)_s<seed> with --initialize-from.
# Extra mlagents-learn options can be passed via MLA_ARGS, e.g. MLA_ARGS="--resume".
set -euo pipefail

stage="${1:?stage number}"
build="${2:?path to Unity build}"
shift 2
seeds=("$@")
[ ${#seeds[@]} -eq 0 ] && seeds=(1 2 3)

config="configs/stage${stage}.yaml"
[ -f "$config" ] || { echo "missing $config" >&2; exit 1; }

for seed in "${seeds[@]}"; do
  run_id="stage${stage}_s${seed}"
  init=()
  if [ "$stage" -gt 0 ]; then
    prev="stage$((stage - 1))_s${seed}"
    [ -d "results/${prev}" ] || { echo "missing results/${prev}: train stage $((stage - 1)) first" >&2; exit 1; }
    init=(--initialize-from "$prev")
  fi
  echo "== $run_id ${init[*]:-}"
  mlagents-learn "$config" \
    --run-id "$run_id" \
    --seed "$seed" \
    --env "$build" \
    --no-graphics \
    --time-scale 20 \
    --num-envs "${NUM_ENVS:-4}" \
    --base-port "$((5005 + 10 * seed))" \
    ${init[@]+"${init[@]}"} \
    ${MLA_ARGS:-}
done
