# MosquitoSim

Unity 기반 모기 AI 시뮬레이터 프로젝트입니다. 모기 에이전트의 행동 학습과 상위 수준의 작업 계획을 결합하고, 학습하지 않은 환경에서의 일반화(generalization)와 환경 변화에 대한 적응(adaptation)을 비교합니다.

## 프로젝트 개념

- **Unity 시뮬레이션**: 모기, 사람, 장애물과 사물이 있는 실내 환경을 구성합니다.
- **RL (Reinforcement Learning)**: 환경과의 상호작용을 통해 모기 에이전트의 행동을 학습합니다.
- **BC (Behavior Cloning) / Expert Demonstration**: 전문가 시연 데이터를 바탕으로 행동을 모방 학습하고, RL과의 결합 효과를 비교합니다.
- **Rule-Based Task Planning**: 명시적인 규칙을 이용해 상위 수준의 작업을 계획합니다.
- **VLM (Vision-Language Model) Task Planning**: 시각·언어 정보를 활용하는 상위 수준의 작업 계획을 비교 대상으로 둡니다.

## 일반화와 적응

### Zero-shot generalization

학습에 사용하지 않은 새로운 장애물, 사물, 방 구조에서도 추가 학습이나 정책 업데이트 없이 행동할 수 있는지를 평가합니다. 환경이 달라져도 이미 학습한 정책과 계획 방식으로 수행하는 능력을 의미합니다.

### Adaptation

모기향의 등장이나 사람의 움직임 등으로 환경 조건이 변할 때, 추가 경험이나 학습을 통해 행동을 조정하는 능력을 평가합니다. 추가 학습 없이 수행하는 zero-shot generalization과 구분하여 비교합니다. 적응에 사용할 구체적인 학습 방식과 평가 절차는 추후 확정합니다.

## 비교군

| 번호 | 비교군 | 구성 |
| --- | --- | --- |
| 1 | Pure RL | RL만 사용 |
| 2 | Pure BC | 전문가 시연을 이용한 BC만 사용 |
| 3 | BC+RL | BC와 RL 결합 |
| 4 | Rule-Based Planning+RL | 규칙 기반 작업 계획과 RL 결합 |
| 5 | VLM Planning+RL | VLM 작업 계획과 RL 결합 |
| 6 | Rule-Based Planning+BC+RL | 규칙 기반 작업 계획, BC, RL 결합 |
| 7 | VLM Planning+BC+RL | VLM 작업 계획, BC, RL 결합 |

새로운 공간 구성에서의 zero-shot generalization과 변화한 환경에서의 adaptation을 구분하여 각 비교군의 특성을 살펴봅니다.

## 초기 디렉터리 구조

```text
MosquitoSim/
├── README.md
├── unity/
│   └── Assets/
│       ├── Scenes/.gitkeep
│       ├── Scripts/.gitkeep
│       ├── Prefabs/.gitkeep
│       └── Materials/.gitkeep
├── python/
│   ├── training/.gitkeep
│   └── evaluation/.gitkeep
├── demonstrations/.gitkeep
├── configs/.gitkeep
└── docs/.gitkeep
```

현재는 프로젝트 개념과 빈 디렉터리 구조만 정리한 초기 단계입니다. 모델 아키텍처, 구체적인 알고리즘, 관측·행동 공간, 보상 설계 및 구현 코드는 아직 확정하지 않았습니다. 각 디렉터리에는 구조 유지를 위한 빈 `.gitkeep`만 포함합니다.
