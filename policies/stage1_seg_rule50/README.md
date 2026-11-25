# 1단계 하위 정책, 규칙 혼합 50 % (실험 브랜치 `exp/stage0-mix`, 11/25)

지금까지 가장 좋은 구성이다. VLM 비교에는 이 정책을 쓰는 것을 권한다.

- `Mosquito_s1.onnx`, `Mosquito_s2.onnx`, `Mosquito_s3.onnx`: 학습 시드 1·2·3
- 학습: 0단계 `configs/exp/s0mix.yaml`(500만 스텝) → 1단계 `configs/exp/h2_seg_rule50.yaml`(300만 스텝)
- 직접 지정 점검(`val`, 평가 시드 1~3 × 30회, 시드 3개 평균): APPROACH 87.4 %, HIDE 84.4 %, MOVE_TO 85.6 %
  (시드별 APPROACH 87·86·90, HIDE 81·87·86, MOVE_TO 87·88·82: 모든 시드가 80 % 이상)
- 흡혈 성공(`val`, 90회 중): `fixed` 84, `rule_masked` 78, `rule_oracle` 90

관측 정의(59차원)와 평가 규칙은 `eval/masked-rule`(`aa12f29`)과 같아서 그 빌드에서 그대로 평가할 수 있다.

    python python/evaluation/evaluate.py --env <빌드> --policy policies/stage1_seg_rule50/Mosquito_s1.onnx \
        --stage 1 --split val --condition rule_masked --policy-seed 1 --episodes 30

`rule_masked` 평가는 다른 작업이 CPU를 쓰던 중에 돌았다(측정 지연이 조금 길었을 수 있다).
