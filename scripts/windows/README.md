# Windows에서 0단계·1단계 학습 돌리기

11/22에 Windows 11 + RTX 5070 PC에서 실제로 돌려 본 절차입니다. 스크립트는 저장소를 `C:\MosquitoSim`에 두는 것을 전제로 합니다(사용자 폴더에 한글이 들어간 PC에서 경로 문제를 피하려고 고정했습니다). 다른 위치를 쓰려면 각 `.ps1` 맨 위의 `$Root`를 바꿉니다.

| 파일 | 하는 일 |
| --- | --- |
| `1_setup.bat` → `setup.ps1` | Git·Python 3.10·Unity Hub 중 없는 것을 winget으로 설치(매번 물어봄), 저장소 받기, `.venv` 만들기, `requirements.txt` 설치, pytest |
| `2_train_stage0.bat` → `train_stage0.ps1` | `requirements.txt` 재설치, 스모크 테스트, 0단계 PPO 학습 |
| `stage1_after_stage0.ps1` | 0단계가 끝나기를 기다렸다가 마지막 10개 요약의 평균 보상이 기준(기본 0.7) 이상이면 1단계를 `--initialize-from`으로 학습 |
| `3_tensorboard.bat` | 학습 곡선 보기 |

## 순서

1. `1_setup.bat` 실행.
2. Unity Hub에서 `C:\MosquitoSim\unity`를 Unity 6000.3.29f1로 열고, 메뉴 MosquitoSim > Build Main Scene.
3. File > Build Profiles > Windows. Scene List에 `Assets/Scenes/Main.unity`를 넣고 `C:\MosquitoSim\builds\Main\MosquitoSim.exe`로 빌드.
4. `2_train_stage0.bat` 실행. 다른 시드는 `2_train_stage0.bat 2`.
5. 1단계까지 이어서 돌리려면 다른 창에서
   `powershell -ExecutionPolicy Bypass -File scripts\windows\stage1_after_stage0.ps1 -Seed 1`

로그는 `C:\MosquitoSim\logs_setup\`(git 제외)에 남습니다. `train_status.txt`에 단계별 시각이 한 줄씩 쌓입니다.

## Windows에서 다른 점

- **Python은 3.10.11을 씁니다.** 3.10.12는 Windows 설치 파일이 없습니다. mlagents 1.1.0의 지원 범위(3.10.1~3.10.12) 안입니다.
- **학습은 CPU로 돌립니다(`--torch-device cpu`).** PyPI의 Windows용 torch는 CPU 빌드이고, 정책이 256×2 MLP라 충분합니다.
- **`scripts/run_stage.sh`는 bash용**이라 여기 스크립트가 같은 인자로 `mlagents-learn`을 부릅니다(`--no-graphics --time-scale 20 --num-envs 4`, 포트 `5005 + 10 × 시드`).
- Unity가 설치 중에 띄우는 Visual Studio Installer는 필요 없습니다.
- 에디터에 패키지 오류 창이 떠 있으면 에디터 스크립트가 멈춥니다. 창을 닫아야 진행됩니다.

## 11/22 실측 (시드 1, 외부 에셋 없이 도형 사람·도형 가구·구 모기)

| 항목 | 값 |
| --- | --- |
| 플레이어 빌드 | 첫 빌드 147초, 125 MB |
| 스모크 테스트 | 6,396 decisions/s (빌드 1개, 방 16개, 무작위 정책) |
| 0단계 500만 스텝 | 16분 53초 (약 4,900 steps/s) |
| 1단계 300만 스텝 | 10분 23초 (약 4,800 steps/s) |

외부 에셋을 넣은 빌드에서는 값이 달라질 수 있습니다.
