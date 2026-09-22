# Windows 빌드 대기 조사

- 사용자 증상: BuildProfileWindow delayCall의 Hold on 화면에서 수 분 대기, 숨은 저장 창 없음.
- 같은 프로젝트의 SpaceBuild.BuildWindowsDev 배치 실행에서도 멈춤 재현. Unity CPU/로그 갱신 거의 없음. 하위 프로세스는 `git diff --name-only -- .`에서 대기.
- BuildStamp.Run은 stdout.ReadToEnd 다음 stderr.ReadToEnd를 실행하여 stderr가 가득 차면 교착 가능. WaitForExit(5000)는 두 블로킹 읽기 뒤라 제한시간이 이를 막지 못함.
- 수정: 두 스트림 ReadToEndAsync를 먼저 시작, 프로세스 5초 제한, 초과 시 해당 프로세스 종료 시도, 스트림 완료에도 1초 제한. 실패 exit=-1 유지.
- 같은 빌드 재시도에서 대기 지점을 통과해 실제 씬/셰이더 빌드 단계 진입 확인.
- 빌드 창 자체의 별도 재현 검사는 미실시. 기존 Windows 빌드 함수를 통한 우회 경로를 검증.
- 사용자 승인 후 해당 Unity 강제 종료. 기존 빌드 폴더는 덮어쓰지 않음.
- 검사 로그: METAGAME/Logs/voice-windows-build-20260922.log (실패/대기), voice-windows-build-fixed-20260922.log (수정 후).
- 최종 결과: Windows 개발 빌드 Succeeded, 프로세스 종료 코드 0, 오류 0개, 1134.6 MB. 출력 `C:/Users/SSAFY/Desktop/NotThisPlanet-voice-test-20260922-0325/NotThisPlanet.exe`. 실행 파일·Data·Mono 런타임 존재 확인. 실제 실행/2PC 통화 검증은 미실시.
- AI 모델 팩은 게임 빌드에 포함되지 않음. 빌드에서는 Application.persistentDataPath/AI/p3 사용, 에디터의 artifacts 팩 경로와 다름.
