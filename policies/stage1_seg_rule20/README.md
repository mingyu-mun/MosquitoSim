# 1단계 하위 정책 (실험 브랜치 `exp/stage0-mix`, 11/24)

- `Mosquito_s1.onnx`, `Mosquito_s2.onnx`, `Mosquito_s3.onnx`: 학습 시드 1·2·3
- 학습: 0단계 `configs/exp/s0mix.yaml`(500만 스텝) → 1단계 `configs/exp/g1_seg_rule20.yaml`(300만 스텝)
- 직접 지정 점검(`val`, 평가 시드 1~3 × 30회, 시드 3개 평균): APPROACH 83.0 %, HIDE 82.2 %, MOVE_TO 83.0 %
- 흡혈 성공(`val`, 90회 중): `fixed` 85, `rule_masked` 72, `rule_oracle` 90

관측 정의(59차원)와 평가 규칙은 `eval/masked-rule`(`aa12f29`)과 같다. 바뀐 것은 학습 쪽 스위치뿐이라
`aa12f29`로 만든 빌드에서 그대로 평가할 수 있다.

    python python/evaluation/evaluate.py --env <빌드> --policy policies/stage1_seg_rule20/Mosquito_s1.onnx \
        --stage 1 --split val --condition rule_masked --policy-seed 1 --episodes 30

`rule_mix` 없이 같은 구조로 학습하면 미달이었고(61.1 / 80.0 / 74.8), 약한 부분은 팔 착지(70 %)와
돌아가야 하는 MOVE_TO(53 %)다. 자세한 기록은 `docs/architecture.md`의 11/24 항목.
