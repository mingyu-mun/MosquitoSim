# 생성형 AI 활용 기록

논문의 "생성형 AI 활용·검증" 절에 쓸 기록입니다. 무엇을 맡겼고, 어떻게 검증했는지 적습니다.

| 날짜 | 도구 | 맡긴 일 | 결과물 | 검증 | 수정한 점 |
|---|---|---|---|---|---|
| 11/19–11/21 | Claude | 계획서 구체화, 설계 비교, 선행 연구 조사 | 계획 문서, WBS | 팀 검토, 참고 연구는 원문 확인 예정 | 서브골 교체 규칙 모순, 모기장 조건 해석, LLM 플래너 → 이미지 제외 VLM |
| 11/21 | Claude | 계획 최종 점검, 0단계 초기 코드 | 이 브랜치의 `docs/`, `python/`, `unity/Assets/Scripts/`, `configs/stage0.yaml` | Python 테스트, C#은 API 스텁으로 형식 검사만. Unity 컴파일·실행은 미확인 | |
| 11/21 | Claude | 1·2단계 시뮬레이터(방, 가구, 사람, 손, 모기장, 단서 물체, 후보 지점, 플래너, 카메라, 사이드 채널), 학습 설정, 평가 루프, VLM 서버, 설계 문서 | `unity/Assets/Scripts/`, `configs/stage1~2.yaml`, `python/evaluation/`, `python/planner_server/`, `docs/architecture.md` | Python 테스트 53개, C#은 스텁 형식 검사, 사이드 채널은 실제 mlagents_envs 1.1.0으로 송수신 확인. Unity 실행은 미확인 | |
| 11/21 | Claude | Unity 실행 검증 루프(자동 테스트), URP 전환, 외부 에셋 연결(Mixamo 사람·애니메이션, 가구 팩, 모기 모델), 방·가구·장식 다양화, 모기 시점·자유 시점 카메라, 전경 카메라 시야 정리 | `unity/Assets/Scripts/`, `unity/ProjectSettings/`, `unity/Assets/Scenes/Main.unity`, `docs/` | 작업 PC의 Unity 6000.3.29f1에서 자동 테스트 run01~run22(컴파일 오류 0, 관측 이상 0, 스크린샷 육안 확인), Python 테스트 53개 | 학습 전 확인할 것: 세트 B 가구 방향, 빌드 스모크 테스트 |
| 11/22 | Claude | Windows PC에서 0단계 첫 실행(설치 스크립트, 빌드 자동화, 스모크 테스트, 학습 시작)과 실패 원인 분석 | `requirements.txt`의 torch 고정, `smoke_test.py`·`evaluate.py`의 통계 채널 등록, `docs/architecture.md`의 실측값 | 실제 빌드로 스모크 테스트와 학습 로그 확인. onnx 내보내기 오류는 Linux에서 재현한 뒤 torch 2.2.2에서 내보내기·`OnnxPolicy` 로드·테스트 53개 통과 확인. 경고 원인은 채널 ID 대조와 `SideChannelManager` 재현으로 확인 | 학습 속도 추정(2,000 steps/s)이 실측(약 4,700 steps/s)과 달라 일정 계산을 다시 해야 함. 경고의 원인을 처음에 플래너 채널로 추측했으나 ID를 대조해 통계 채널로 정정 |
| 11/22 | Claude | 0단계·1단계 예행 학습(도형 에셋, 시드 1)과 1단계 정책의 fixed/rule 평가, Windows 실행 스크립트 | `scripts/windows/`, `JsonBuilder.cs`·`channel.py`의 한글 전송 수정과 테스트, `docs/architecture.md`의 결과 | TensorBoard 기록에서 성공률·커리큘럼 단계를 직접 읽음. 평가는 두 번 돌려 결과 일치 확인. 한글 깨짐은 `decisions.jsonl`에서 발견해 원인(ML-Agents 문자열이 ASCII 전용)을 라이브러리 소스로 확인하고, 수정 후 실제 빌드에서 한글이 온전히 오는지 확인 | 1단계의 `Mosquito/Success`(0.69)를 서브골 달성률로 읽을 뻔했으나 코드를 보고 흡혈 성공만 세는 값임을 확인. 서브골별 직접 지정 점검은 미구현이라 1단계 통과 여부는 판단 보류 |
| 11/22 | Claude | 0단계 성공률이 과적합인지 점검: 0단계 정책을 val·new_room·new_human에서 평가 시드 3개로 평가하고 실패를 방 형태별로 분류 | `docs/architecture.md`의 일반화 점검 결과 | 방 형태는 시드로 다시 계산한 뒤 `decisions.jsonl`의 후보 지점 분포와 30회 대조해 일치 확인. 실패 25건의 유형과 충돌 횟수를 `episodes.csv`에서 직접 읽음 | 처음에는 후보 지점의 절대 좌표로 방 크기를 대조하다 방마다 위치가 달라 어긋났고, 방 안에서의 분포 폭으로 바꿔 대조함. `new_human`은 0단계에서 `val`과 같은 조건이라 별도 근거로 쓰지 않음 |
