# MosquitoSim

Unity 기반 모기 에이전트 시뮬레이터입니다. 서브골 조건부 PPO 하위 정책 위에 상위 플래너(규칙, VLM)를 얹고, 학습하지 않은 환경에서의 일반화와 환경 변화에 대한 적응, 그리고 VLM 추론 지연이 성공률에 주는 영향을 비교합니다.

## 구성

- **과제:** 사람에게 들키지 않고 피부에 착지해 흡혈. 0단계 흡혈, 1단계 공간(장애물), 2단계 사람 이동·손, 3단계 모기향(확장)
- **하위 정책:** 서브골 조건부 PPO(ML-Agents). 관측 59차원, 연속 가속 3차원. 시연 데이터 없음
- **플래너 지시:** 서브골(APPROACH, HIDE, MOVE_TO, EVADE는 2단계부터) + 대상 ID(피부 S1~S5, 후보 지점 P1~P8). 플래너는 사건 때만 호출
- **플래너 조건:** 서브골 고정(접근) 기준선, 마스킹 규칙(VLM과 같은 정보만 받는 규칙), VLM(상황 언어 지시문 + 카메라 5대의 잠재 이미지 텐서), 이미지를 뺀 VLM(절제), 정답 규칙(가려짐·길 열림을 정확히 아는 규칙, 상한 참고)
- **평가:** 같은 평가 시드로 짝지은 평가, 시드 3 × 30에피소드, 부트스트랩 95% 신뢰구간. VLM 호출 중에도 시뮬레이션을 멈추지 않으며(지연 주입), 양자화·토큰 가지치기·캐시 등으로 지연을 줄였을 때의 성공률을 측정

규격은 [docs/interface.md](docs/interface.md), Unity 설정과 0단계 실행 방법은 [docs/unity_setup.md](docs/unity_setup.md)에 있습니다.

## 디렉터리

```text
MosquitoSim/
├── unity/
│   ├── Packages/manifest.json        # Unity 6 LTS, ML-Agents 4.1.0
│   └── Assets/Scripts/
│       ├── Agent/                    # 에이전트, 모터, 관측, 서브골, 플래너, 지시문, 카메라, 사이드 채널
│       ├── Env/                      # 방, 가구, 사람, 손, 모기장, 단서 물체, 후보 지점
│       └── Editor/SceneBuilder.cs    # 메뉴 MosquitoSim > Build Main Scene
├── python/
│   ├── mosquitosim/                  # 관측 규격, 플래너 지시, 채널, 지연, ONNX 정책, 프롬프트
│   ├── training/smoke_test.py
│   ├── evaluation/                   # evaluate.py, eval_loop.py, metrics.py
│   ├── planner_server/               # VLM 서버 (transformers / vLLM / mock)
│   └── tests/
├── configs/stage0~2.yaml             # PPO, 커리큘럼, 단계별 설정
├── scripts/run_stage.sh              # 단계별 학습, 시드 여러 개
└── docs/                             # 규격, 설계, 설정 안내, AI 활용 기록
```

설계 설명은 [docs/architecture.md](docs/architecture.md)에 있습니다.

## 빠른 시작

```bash
python3.10 -m venv .venv && source .venv/bin/activate
pip install -r requirements.txt
pytest python/tests
```

Unity 쪽은 [docs/unity_setup.md](docs/unity_setup.md)를 따라 프로젝트를 열고 0단계 씬을 만든 뒤 빌드합니다.
