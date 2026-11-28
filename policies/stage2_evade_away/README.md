# 2단계 하위 정책, EVADE 탈출점 관측 (실험 브랜치 `exp/evade-away`, 11/26)

main `a6b73a7`에서 바꾼 것은 하나다. EVADE 동안 관측의 `subgoal_target_*` 칸에 손 위치 대신
손에서 정반대로 1.5 m 떨어진 점을 넣는다(`MosquitoAgent.EvadePoint`). 손 위치는 `hand_*` 칸에 그대로 들어간다.
학습 설정(`configs/stage2.yaml`)과 평가 규칙은 main과 같다. 관측 정의가 바뀌었으므로 이 정책은
이 브랜치로 만든 빌드에서 평가해야 한다.

- `Mosquito_s<n>.onnx`: 최종(100만 스텝), `_50k`, `_200k`: 적응 지점 체크포인트
- 학습: `--initialize-from t_h2_seg_rule50_s<n>`(= `policies/stage1_seg_rule50`), 시드 1·2·3

직접 지정 점검(`val`, 평가 시드 1~3 × 30회, `metrics.py --probe`), 정책 시드 3개 평균:

| 적응 지점 | APPROACH | HIDE | MOVE_TO | EVADE |
|---|---|---|---|---|
| 0 (1단계 정책) | 83.3 | 39.6 | 98.5 | **85.9** |
| 5만 | 75.2 | 34.8 | 95.6 | 85.9 |
| 20만 | 70.4 | 49.5 | 95.2 | 85.6 |
| 100만 | 76.3 | 67.0 | 97.8 | 85.6 |

EVADE는 통과, APPROACH와 HIDE는 미달이다.
