# 인터페이스 규격 v3

하위 정책, 플래너, 평가 코드가 공유하는 규격입니다. 계획 문서의 "인터페이스 규격 v3"과 "최종 점검(11/21)"을 코드 기준으로 옮겼습니다. 관측 순서는 `python/mosquitosim/obs_spec.py`가 기준이고, `unity/Assets/Scripts/Agent/ObsLayout.cs`가 같은 값을 갖습니다. 둘이 다르면 `python/tests/test_cs_layout.py`가 실패합니다.

## 시간

- 물리 고정 간격 0.01초(`SimSettings`). 1.5m/s에서 한 스텝 1.5cm로, 착지 허용 오차 2cm보다 작습니다.
- 결정 주기 5스텝(0.05초), 결정 사이에는 직전 행동을 반복합니다.
- 에피소드 60초 = 6,000스텝(`MaxStep`).

## 행동

연속 3차원, 각 축 −1~1, 월드 축. 크기를 1로 자른 뒤 목표 가속도(최대 5m/s²)로 쓰고, 속도는 1.5m/s에서 자릅니다.

착지 중에는 위치가 피부에 고정되고, 피부 바깥 방향(법선) 성분이 0.5를 넘는 행동이 오면 0.3m/s로 이륙합니다.

## 관측 (59차원, 월드 축)

서브골 원-핫은 처음부터 4칸(접근·숨기·회피·이동)입니다(최종 점검 1-1). 대기는 11/21에 제거했습니다. 넷째 칸(MOVE_TO)은 채택하지 않으면 늘 0이고, 회피 칸은 2단계부터 쓰입니다.

| 시작 | 크기 | 이름 | 값 | 없는 단계 |
|---|---|---|---|---|
| 0 | 3 | self_velocity | 속도 / 1.5 | |
| 3 | 3 | target_skin_rel_pos | (목표 피부 영역 중심 − 모기) / 5, [−1, 1]로 자름 | |
| 6 | 1 | target_skin_dist | 거리 / 5, 최대 1 | |
| 7 | 3 | subgoal_target_rel_pos | (서브골 목표 지점 − 모기) / 5 | |
| 10 | 1 | subgoal_target_dist | 거리 / 5 | |
| 11 | 3 | human_velocity | 사람 속도 / 2 | 0~1단계 |
| 14 | 1 | human_alert | 경계 0/1 | 0~1단계 |
| 15 | 3 | hand_rel_pos | (손 − 모기) / 5 | 0~1단계 |
| 18 | 3 | hand_velocity | 손 속도 / 8 | 0~1단계 |
| 21 | 1 | hand_active | 0/1 | 0~1단계 |
| 22 | 3 | hazard_rel_pos | (위험 지점 − 모기) / 5 | 위험 없음 |
| 25 | 1 | hazard_active | 0/1 | 위험 없음 |
| 26 | 1 | hazard_exposure | 누적 노출 / 한도 | 위험 없음 |
| 27 | 26 | rays | 거리 / 2 (안 맞으면 1) | |
| 53 | 4 | subgoal_onehot | APPROACH, HIDE, EVADE, MOVE_TO | |
| 57 | 1 | remaining_time | 남은 시간 비율 | |
| 58 | 1 | landed | 0/1 | |

레이 방향은 {−1, 0, 1}³에서 원점을 뺀 26개를 x, y, z 순서로 중첩해 나열하고 정규화한 것입니다.

회피는 손이 생기는 2단계부터 학습합니다. 0·1단계에는 가상 위험 지점을 쓰지 않습니다(최종 점검 1-3, 11/21 결정).

## 서브골

| id | 서브골 | 목표 지점 | 달성(학습) | 보상 |
|---|---|---|---|---|
| 0 | APPROACH | 목표 피부 영역 중심 | 착지 후 흡혈 시간 유지 | 거리 감소 × 0.1, 흡혈 +1 |
| 1 | HIDE | 사람 머리에서 가려지는 가장 가까운 후보 지점(0.25m 격자) | 0.15m 이내 1초 | 거리 감소 × 0.1, 달성 +0.3 |
| 2 | EVADE | 위험 지점(손, 모기향). 2단계부터 | 1.5m 이상 | 거리 증가 × 0.1, 달성 +0.3 |
| 3 | MOVE_TO | 후보 지점 ID (채택 시) | 0.15m 이내 | 미정 |

공통: 잡힘 −1, 모기향 노출 초과 −1, 충돌 −0.01(스텝당), 시간 −0.005/초.

교체 규칙(학습): 달성, 실패, 시간 초과(8초) 때 교체. 결정마다 평균 5초에 한 번 무작위 중간 교체. 흡혈 중에는 무작위 중간 교체를 하지 않지만, 2단계부터 위협 사건(손 활성 등) 때의 사건 교체는 착지 중에도 허용합니다(최종 점검 1-2).

직접 지정 점검(평가): 서브골 하나를 지정해 8초 안에 달성하는지 봅니다.

## 단계별 학습 서브골

| 단계 | 플래너 | 서브골 |
|---|---|---|
| 0 | fixed | APPROACH만 |
| 1 | random | APPROACH, HIDE (+ MOVE_TO 채택 시) |
| 2~ | random | APPROACH, HIDE, EVADE (+ MOVE_TO) |

## 환경 파라미터 (ML-Agents environment_parameters)

| 이름 | 값 | 구현 |
|---|---|---|
| stage | 0, 1, 2, 3 | MosquitoAgent |
| planner | 0 fixed, 1 random, 2 rule, 3 vlm, 4 vlm_noimg | 1단계부터 |
| landing_radius | 착지 허용 거리(m), 커리큘럼 0.06 → 0.02 | MosquitoAgent |
| landing_speed | 착지 허용 속도(m/s), 0.6 → 0.3 | MosquitoAgent |
| feed_time | 흡혈 시간(초), 1 → 3 | MosquitoAgent |
| time_penalty | 초당 시간 페널티 | MosquitoAgent |
| env_split, vlm_config, pause_on_decision, eval_seed, latency_mode | 계획 문서 표 참조 | 1단계부터 |

## 플래너 호출 시점

2초 주기 호출은 없고 사건 때만 부릅니다(11/21, VLM 호출 오버헤드 감소). 사건: 에피소드 시작, 서브골 종료(달성·실패·8초 시간 초과), 경계 시작·끝, 손 활성, 모기향 등장. 같은 순간의 사건은 한 번으로 묶고, 호출 사이는 최소 1초입니다. 규칙 플래너도 같은 시점에만 판단합니다. 착지 중에도 사건이 생기면 부릅니다.

## 플래너 입출력

요청(Unity → planner_server, JSON):

```json
{
  "episode_id": "eval_new_room/s1/e017",
  "sim_time": 14.2,
  "instruction": "[과제] ... [상황] ... [출력] ...",
  "images": {"mosquito": "<png base64>", "overview": ["...", "...", "...", "..."]},
  "rule_subgoal": "APPROACH",
  "vlm_config": "c_bf16"
}
```

응답: `{"subgoal": "HIDE"}`. `mosquitosim.subgoals.parse_planner_output`이 받아들이지 않는 응답은 무효로 기록하고 직전 서브골을 유지합니다.

지연 주입(`latency_mode=injected`): 결정 시점 t에 입력을 캡처하고, 계산하는 실제 시간 동안 시뮬레이션을 멈춘 뒤, 설정의 지연 분포에서 뽑은 d만큼 시뮬레이션 시간이 지난 t+d에 결과를 적용합니다. 그 사이 정책은 직전 서브골로 납니다.

## 로그

결정 로그 한 줄: 시각, 에피소드 ID, 입력(이미지 파일 경로, 지시문), 원문 응답, 서브골, 유효 여부, 지연, 같은 상황의 규칙 서브골. 이 로그가 지연 실험의 재생 데이터입니다.
