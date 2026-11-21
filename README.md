# MosquitoSim

Unity 기반 모기 에이전트 시뮬레이터입니다. 서브골 조건부 PPO 하위 정책 위에 상위 플래너(규칙, VLM)를 얹고, 학습하지 않은 환경에서의 일반화와 환경 변화에 대한 적응, 그리고 VLM 추론 지연이 성공률에 주는 영향을 비교합니다.

## 구성

- **과제:** 사람에게 들키지 않고 피부에 착지해 흡혈. 0단계 흡혈, 1단계 공간(장애물), 2단계 사람 이동·손, 3단계 모기향(확장)
- **하위 정책:** 서브골 조건부 PPO(ML-Agents). 관측 59차원, 연속 가속 3차원. 시연 데이터 없음
- **서브골:** APPROACH, HIDE, EVADE(2단계부터) + MOVE_TO 예약 칸. 플래너는 사건 때만 호출
- **플래너 조건:** 서브골 고정(접근) 기준선, 규칙, VLM(상황 언어 지시문 + 카메라 5대의 잠재 이미지 텐서), 이미지를 뺀 VLM(절제)
- **평가:** 같은 평가 시드로 짝지은 평가, 시드 3 × 30에피소드, 부트스트랩 95% 신뢰구간. VLM 호출 중에도 시뮬레이션을 멈추지 않으며(지연 주입), 양자화·토큰 가지치기·캐시 등으로 지연을 줄였을 때의 성공률을 측정

규격은 [docs/interface.md](docs/interface.md), Unity 설정과 0단계 실행 방법은 [docs/unity_setup.md](docs/unity_setup.md)에 있습니다.

## 디렉터리

```text
MosquitoSim/
├── unity/
│   ├── Packages/manifest.json        # Unity 6 LTS, ML-Agents 4.1.0
│   └── Assets/Scripts/
│       ├── Agent/                    # ObsLayout, ObsBuilder, MosquitoMotor, MosquitoAgent, Subgoal
│       ├── Env/                      # RoomArea, SkinZone, SimSettings
│       └── Editor/Stage0SceneBuilder.cs
├── python/
│   ├── mosquitosim/                  # obs_spec (관측 규격), subgoals (플래너 출력 파싱)
│   ├── training/smoke_test.py        # 랜덤 정책 스모크 테스트
│   ├── evaluation/
│   └── tests/                        # 규격 테스트, C# 관측 순서 대조
├── configs/stage0.yaml               # PPO + 착지 커리큘럼
├── scripts/run_stage.sh              # 단계별 학습, 시드 여러 개
└── docs/                             # 규격, 설정 안내, AI 활용 기록
```

## 빠른 시작

```bash
python3.10 -m venv .venv && source .venv/bin/activate
pip install -r requirements.txt
pytest python/tests
```

Unity 쪽은 [docs/unity_setup.md](docs/unity_setup.md)를 따라 프로젝트를 열고 0단계 씬을 만든 뒤 빌드합니다.
