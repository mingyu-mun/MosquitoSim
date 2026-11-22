# 생성형 AI 활용 기록

논문의 "생성형 AI 활용·검증" 절에 쓸 기록입니다. 무엇을 맡겼고, 어떻게 검증했는지 적습니다.

| 날짜 | 도구 | 맡긴 일 | 결과물 | 검증 | 수정한 점 |
|---|---|---|---|---|---|
| 11/19–11/21 | Claude | 계획서 구체화, 설계 비교, 선행 연구 조사 | 계획 문서, WBS | 팀 검토, 참고 연구는 원문 확인 예정 | 서브골 교체 규칙 모순, 모기장 조건 해석, LLM 플래너 → 이미지 제외 VLM |
| 11/21 | Claude | 계획 최종 점검, 0단계 초기 코드 | 이 브랜치의 `docs/`, `python/`, `unity/Assets/Scripts/`, `configs/stage0.yaml` | Python 테스트, C#은 API 스텁으로 형식 검사만. Unity 컴파일·실행은 미확인 | |
| 11/21 | Claude | 1·2단계 시뮬레이터(방, 가구, 사람, 손, 모기장, 단서 물체, 후보 지점, 플래너, 카메라, 사이드 채널), 학습 설정, 평가 루프, VLM 서버, 설계 문서 | `unity/Assets/Scripts/`, `configs/stage1~2.yaml`, `python/evaluation/`, `python/planner_server/`, `docs/architecture.md` | Python 테스트 53개, C#은 스텁 형식 검사, 사이드 채널은 실제 mlagents_envs 1.1.0으로 송수신 확인. Unity 실행은 미확인 | |
| 11/21 | Claude | Unity 실행 검증 루프(자동 테스트), URP 전환, 외부 에셋 연결(Mixamo 사람·애니메이션, 가구 팩, 모기 모델), 방·가구·장식 다양화, 모기 시점·자유 시점 카메라, 전경 카메라 시야 정리 | `unity/Assets/Scripts/`, `unity/ProjectSettings/`, `unity/Assets/Scenes/Main.unity`, `docs/` | 작업 PC의 Unity 6000.3.29f1에서 자동 테스트 run01~run22(컴파일 오류 0, 관측 이상 0, 스크린샷 육안 확인), Python 테스트 53개 | 학습 전 확인할 것: 세트 B 가구 방향, 빌드 스모크 테스트 |
| 11/22 | Claude | 서브골 평가: 플래너 입력 단일화(PlannerView), 마스킹 규칙 비교군, 직접 점검(probe), HIDE 시나리오, 사람 경계(시야·소리 방향 회전·둘러보기) | `python/mosquitosim/masked_rule.py`, `python/evaluation/`, `unity/Assets/Scripts/Agent/`, `unity/Assets/Scripts/Env/`, `docs/interface.md` | Python 테스트 69개, C#은 스텁 형식 검사. Unity 실행은 미확인(점검 요청 run24_probe_cycle 대기 중) | HIDE 정의를 "선택 후 경계, 발각 실패·경계 해제 성공"으로 변경, 정답 규칙과 마스킹 규칙 분리 |
