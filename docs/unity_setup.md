# Unity 설정과 0단계 실행

## 1. 프로젝트 열기

1. Unity Hub에서 Unity 6 LTS를 설치합니다.
2. Hub의 **Add → Add project from disk**로 저장소의 `unity/` 폴더를 엽니다. `Packages/manifest.json`에 ML-Agents 4.1.0이 들어 있어 처음 열 때 패키지를 받고 `ProjectSettings/`를 만듭니다.
3. 콘솔에 컴파일 오류가 없는지 확인합니다. 이 저장소의 C#은 Unity 없이 작성했으므로, 처음 열었을 때 나오는 오류는 그대로 이슈로 남겨 주세요.
4. 새로 생긴 `ProjectSettings/`, `Packages/packages-lock.json`을 커밋합니다(`Library/`는 .gitignore에 있음).

## 2. 0단계 씬 만들기

메뉴 **MosquitoSim → Build Stage 0 Scene**을 누르면 `Assets/Scenes/Stage0.unity`가 만들어집니다.

- 4×3×2.5m 방 16개(10m 간격), 방마다 서 있는 사람과 피부 영역 5개(머리, 양 팔뚝, 양 종아리), 모기 1마리
- 모기: `MosquitoMotor`, `MosquitoAgent`, Behavior Parameters(이름 Mosquito, 관측 59, 연속 행동 3), Decision Requester(주기 5)
- `SimSettings`: 물리 간격 0.01초

## 3. 손으로 확인 (WBS 1.7)

1. 방 하나만 남기고 나머지를 끄거나, Scene 뷰에서 Room_00을 봅니다.
2. Room_00의 Mosquito에서 Behavior Parameters의 **Behavior Type**을 Heuristic Only로 바꿉니다.
3. Play 후 WASD(수평), Space/Left Shift(위/아래)로 날려 목표 피부 영역에 천천히 다가가면 착지 후 흡혈 시간(기본 1초) 뒤 에피소드가 끝납니다.
4. 벽에 빠르게 부딪혀도 통과하지 않는지 봅니다.

## 4. 빌드와 스모크 테스트 (WBS 1.8)

1. Behavior Type을 Default로 되돌리고, **File → Build Profiles**에서 Linux(서버 학습) 또는 사용 중인 OS로 빌드합니다. 출력은 `builds/Stage0/`(커밋하지 않음).
2. Python 환경:

   ```bash
   python3.10 -m venv .venv && source .venv/bin/activate
   pip install -r requirements.txt
   pytest python/tests
   python python/training/smoke_test.py --env builds/Stage0/MosquitoSim.x86_64 --steps 5000
   ```

   스모크 테스트는 관측 크기 59, NaN, 값 범위, 원-핫을 매 스텝 확인하고 초당 결정 수를 출력합니다.

## 5. 0단계 학습 (WBS 1.10)

```bash
scripts/run_stage.sh 0 builds/Stage0/MosquitoSim.x86_64 1 2 3
tensorboard --logdir results
```

TensorBoard의 `Mosquito/Success`가 성공률입니다. 착지 커리큘럼은 `Environment/Lesson Number`로 확인합니다.
