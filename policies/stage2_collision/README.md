# Stage 2 policies — escape point + collision_penalty 0.001 (11/26)

Branch `exp/hide-arrive` build (based on `exp/evade-away`, itself based on main `a6b73a7`).
Config `configs/exp/s2hc.yaml` = `configs/stage2.yaml` + `collision_penalty: 0.001` (default 0.01).
Initialised per seed from `t_h2_seg_rule50_s<n>` (stage 1), 1M steps, CPU.

Files: `Mosquito_s<n>.onnx` (final, 1M), `Mosquito_s<n>_50k.onnx`, `Mosquito_s<n>_200k.onnx`.

## Direct probes (val, eval seeds 1–3 × 30 = 90 per policy seed; HIDE counts only arrived + unspotted)

| subgoal | s1 | s2 | s3 | mean (1M) | mean (200k) | before (escape point only, 1M) |
|---|---|---|---|---|---|---|
| APPROACH | 77.8 | 85.6 | 82.2 | **81.9** | 70.4 | 76.3 |
| HIDE | 84.4 | 84.4 | 80.0 | **83.0** | 60.0 | 67.0 |
| MOVE_TO | 98.9 | 98.9 | 100.0 | **99.3** | 94.1 | 97.8 |
| EVADE | 87.8 | 81.1 | 84.4 | **84.4** | 83.0 | 85.6 |

Repeat with the same config (`s2hc2`, new training run, seeds 1–3): APPROACH 80.4 (80.0·83.3·77.8),
HIDE 81.5 (82.2·81.1·81.1), MOVE_TO 98.5, EVADE 85.2 — all ≥ 80%.

## Failures at 1M (270 probes per subgoal)

- APPROACH: timeout 41, caught 8 (before: timeout 47, caught 17).
- HIDE: arrived+unspotted 224; arrived but spotted 8; not arrived 38 (23 spotted, 15 unspotted).
- EVADE: all failures are caught (12 · 19 · 16 %).
- MOVE_TO: timeout 2.

HIDE by free space around the hiding point (new probe column `probe_target_clear`):
< 0.2 m: 68/101 arrived; ≥ 0.2 m: 164/169 arrived. Before the change, tight points (< 0.2 m)
were ~52% and failed mosquitoes were pressed against furniture (`probe_min_clear` median 0.11 m),
which is why a lower collision penalty helps.

## Plans (val, 30 episodes per seed, feeding success)

fixed 24 · 27 · 22, rule_masked 25 · 26 · 18, rule_oracle 12 · 10 · 7 (rule_oracle still low; not investigated yet).

## Training curves

`docs/figures/stage2_collision_training.png`, raw values in `docs/figures/stage2_collision_training_curves.csv`.
`Subgoal/EVADE/Achieved` is always 1.0 in TensorBoard because caught/feeding end the episode before the
subgoal stat is recorded; use the probe numbers above.

## Other variants tried on 11/26 (1M, mean of 3 seeds, A/H/M/E %)

| variant | APPROACH | HIDE | MOVE_TO | EVADE |
|---|---|---|---|---|
| 3M steps (seed 1 only) | 61.1 | 61.1 | 93.3 | 85.6 |
| hide_hold 1 s | 75.2 | 65.9 | 93.3 | 85.6 |
| hide_hold 1 s + approach_time_penalty 0.05 | 77.8 | 68.9 | 93.7 | 86.7 |
| hide_arrive_bonus 0.5 | 77.0 | 71.9 | 98.5 | 85.2 |
| hide_radius 0.08 (training only) | 74.1 | 53.3 | 93.3 | 86.3 |
| collision_penalty 0.001 + hide_arrive_bonus 0.5 | 81.9 | 80.4 | 99.3 | 83.3 |
