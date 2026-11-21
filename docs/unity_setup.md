# Unity 설정과 실행

## 1. 프로젝트 열기

1. Unity Hub에서 Unity 6 LTS를 설치합니다.
2. Hub의 **Add → Add project from disk**로 저장소의 `unity/` 폴더를 엽니다. `Packages/manifest.json`에 ML-Agents 4.1.0이 들어 있어 처음 열 때 패키지를 받고 `ProjectSettings/`를 만듭니다.
3. 렌더 파이프라인은 **URP 17.3**입니다(11/21에 Built-in에서 전환, VLM이 보는 플래너 이미지 품질 때문). 처음 열면 `MosquitoSim/Use URP`가 자동으로 돌아 `Assets/Settings/MosquitoSim_URP.asset`을 만들고 기본 파이프라인으로 지정합니다. 에셋을 가져올 때는 URP 호환판을 고르고, Built-in 전용 머티리얼은 **Window → Rendering → Render Pipeline Converter**로 바꿉니다. 카메라를 코드에서 렌더할 때는 `Camera.Render()` 대신 `CameraCapture.Render()`(URP 렌더 요청)를 씁니다.
4. 콘솔에 컴파일 오류가 없는지 확인합니다. 이 저장소의 C#은 Unity 없이 작성했으므로, 처음 열었을 때 나오는 오류는 그대로 이슈로 남겨 주세요.
5. `ProjectSettings/`, `Assets/Settings/`, `Packages/packages-lock.json`, `Assets/Scenes/Main.unity`는 커밋되어 있습니다. 씬을 다시 만들면 바뀐 `Main.unity`도 함께 커밋합니다(`Library/`, `AutoTest/`는 .gitignore에 있음).

## 1-1. 외부 에셋 (git에 없음, 팀 전용 zip)

라이선스상 재배포할 수 없어 `unity/Assets/ThirdParty/`는 .gitignore에 있습니다. 개발자끼리만 비공개 zip(`MosquitoSim_ThirdParty_<날짜>.zip`)으로 주고받고, 외부에는 공유하지 않습니다. 없으면 씬 빌더가 도형 사람·도형 가구·5 mm 구 모기로 대신 만듭니다(오류는 나지 않음). 학습 결과를 비교하려면 모두 에셋을 넣은 같은 빌드를 써야 합니다.

```text
unity/Assets/
├── ThirdParty.meta
└── ThirdParty/
    ├── Ch42_nonPBR.fbx            사람 캐릭터 (Mixamo Ch42, FBX for Unity, T-pose, With Skin)
    ├── Standing Idle.fbx          Mixamo, Without Skin
    ├── Walking.fbx                Mixamo, Without Skin
    ├── Walking Turn 180.fbx       Mixamo, Without Skin
    ├── mosquito/
    │   ├── source/MOSQUITO.fbx    Sketchfab 모기 모델 (CC BY, 출처 표기)
    │   └── textures/dif.jpg
    ├── Furniture Mega Pack/       Asset Store 무료 팩
    ├── Textures/                  자동 생성 (캐릭터 텍스처 추출본)
    └── _Generated/                자동 생성 (Human.controller, Mosquito.mat)
```

- 이름은 대소문자·공백까지 똑같아야 합니다(`Editor/HumanAssets.cs`, `Editor/FurnitureCatalog.cs`가 문자열 경로로 찾음).
- zip에는 **.meta 파일을 모두 포함**합니다. 커밋된 `Main.unity`가 사람·애니메이터·모기 머티리얼을 .meta의 GUID로 가리키므로, .meta 없이 받으면 참조가 끊깁니다.
- 받는 쪽: Unity를 닫고 zip을 `unity/Assets/`에 풀어 `unity/Assets/ThirdParty/Ch42_nonPBR.fbx`가 되게 합니다 → Unity 열기 → **MosquitoSim → Build Main Scene** → Play로 빨간 티셔츠 사람과 모기 모델이 보이는지 확인 → `git status`에 ThirdParty가 안 보이는지 확인.
- 가구 팩을 Asset Store에서 직접 Import하면 `Assets/Furniture Mega Pack/`에 생기고, 씬 빌드 때 `ThirdParty/`로 옮겨집니다. 무료 팩이므로 각자 Unity 계정으로 Add to My Assets를 해 둡니다.

씬 빌드 때 `ThirdPartyImport`가 Humanoid 리그로 가져오고, 텍스처를 `ThirdParty/Textures`로 꺼내고, `ThirdParty/_Generated/Human.controller`(서기↔걷기 블렌드, 180도 회전, IK)를 만듭니다. 피부 영역 S1~S5와 몸 충돌체는 Play 시작 때 뼈에 붙습니다(`HumanAvatar`).

## 2. 씬 만들기

메뉴 **MosquitoSim → Build Main Scene**을 누르면 `Assets/Scenes/Main.unity`가 만들어집니다. 한 씬으로 모든 단계와 평가 분할을 돌립니다(구조는 [architecture.md](architecture.md)).

- 방 16개(12 m 간격). 방 모양과 가구는 Play할 때 환경 파라미터에 따라 만들어지므로, 편집 화면에는 사람과 모기만 보입니다.
- 방마다 사람(피부 S1~S5, 손), 모기장, 플래너 카메라 5대, 모기
- 모기: `MosquitoMotor`, `MosquitoAgent`, Behavior Parameters(이름 Mosquito, 관측 59, 연속 행동 3), Decision Requester(주기 5)
- 조명: 방마다 천장 점광원 1개(그림자 없음), 약한 태양광, 평면 앰비언트.
- 레이어 8(사람), 9(라벨, 플래너 카메라만 봄), 10(디버그 표시, 플래너 카메라는 안 봄)을 번호로 씁니다. Tags and Layers에서 이름을 붙여도 되고 안 붙여도 됩니다.
- Build Profiles의 Scene List에 `Main.unity`를 넣습니다.

## 3. 손으로 확인 (WBS 1.7)

Python 없이 Play하면 Hierarchy의 **Global** 오브젝트에 있는 `SimSettings`의 "Play in the editor without Python" 값으로 돕니다(학습·평가·자동 테스트 때는 무시). 기본값은 2단계, Train 분할, 규칙 플래너, 자동 조종, 추적 시점입니다. **Flight**를 Keyboard로 바꾸면 보고 있는 방의 모기를 직접 조종합니다(W/S 앞뒤, A/D 좌우, Space 위, Left Shift 아래, 카메라 방향 기준). 나머지 방은 자동 조종입니다. 아래는 예전 방식입니다.

1. 방 하나만 남기고 나머지를 끄거나, Scene 뷰에서 Room_00을 봅니다.
2. Room_00의 Mosquito에서 Behavior Parameters의 **Behavior Type**을 Heuristic Only로 바꿉니다.
3. Play 후 WASD(수평), Space/Left Shift(위/아래)로 날려 목표 피부(에피소드마다 무작위)에 천천히 다가가면 착지 후 흡혈 시간(기본 1초) 뒤 에피소드가 끝납니다. Python 없이 Play하면 0단계 기본값(빈 방, 무작위 플래너)으로 돕니다.
4. 벽에 빠르게 부딪혀도 통과하지 않는지 봅니다.
5. 모기 시점: Play 중 **V**로 방 전체 / 추적 / 1인칭 / 자유 시점을 바꾸고(자유 시점: 오른쪽 마우스 끌기로 둘러보기, WASD 이동, Space/Ctrl 위아래, Shift 빠르게, 휠 속도, F 모기 뒤로 이동), **Q/E**(또는 [ ], 쉼표/마침표, PageUp/PageDown, 숫자 1~9·0, 표시줄 아래 버튼)로 방을, **H**로 HUD(지시, 목표까지 거리, 속도, 착지·흡혈, 누적 결과)를 켜고 끕니다. 노란 선과 십자가 현재 서브골 목표입니다(`MosquitoView`, 메인 카메라에 자동으로 붙음).

## 4. 빌드와 스모크 테스트 (WBS 1.8)

1. Behavior Type을 Default로 되돌리고, **File → Build Profiles**에서 Linux(서버 학습) 또는 사용 중인 OS로 빌드합니다. 출력은 `builds/Main/`(커밋하지 않음). 학습용은 `-nographics`로 돌려도 되지만, VLM 평가는 카메라 이미지가 필요하므로 그래픽이 있는 빌드와 실행이 필요합니다.
2. Python 환경:

   ```bash
   python3.10 -m venv .venv && source .venv/bin/activate
   pip install -r requirements.txt
   pytest python/tests
   python python/training/smoke_test.py --env builds/Main/MosquitoSim.x86_64 --steps 5000
   ```

   스모크 테스트는 관측 크기 59, NaN, 값 범위, 원-핫을 매 스텝 확인하고 초당 결정 수를 출력합니다.

## 5. 학습 (WBS 1.10, 2.3, 3.2)

```bash
scripts/run_stage.sh 0 builds/Main/MosquitoSim.x86_64 1 2 3
scripts/run_stage.sh 1 builds/Main/MosquitoSim.x86_64 1 2 3   # stage0_s<seed>에서 이어서
scripts/run_stage.sh 2 builds/Main/MosquitoSim.x86_64 1 2 3   # stage1_s<seed>에서 이어서
tensorboard --logdir results
```

TensorBoard의 `Mosquito/Success`가 성공률입니다. 착지 커리큘럼은 `Environment/Lesson Number`로 확인합니다.

## 6. 평가

평가 루프와 VLM 서버 사용법은 [architecture.md](architecture.md)의 "평가 코드" 절에 있습니다. 서버는 `requirements-planner.txt`로 별도 venv에 설치합니다(mlagents 1.1.0이 오래된 numpy·protobuf를 고정하기 때문).
