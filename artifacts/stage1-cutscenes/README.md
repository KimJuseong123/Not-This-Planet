# S15P21B202-587 리뷰 검증

Unity 6000.5.4f1 / Windows / 격리 프로젝트. 실제 Stage1 월드, 현재 런타임 플레이어·커마와 운석을 사용한다. 원본 작업용 Unity 에디터는 실행·저장·종료하지 않았다.

## 재생 자료

- [F10 입장 컷씬 녹화](entry.gif)
- [F9 공세 컷씬 녹화](assault.gif)
- [캡처 시각](frames.csv), [런타임 확인](runtime.txt)

GIF는 카메라의 연속 프레임을 원래 시각 간격으로 인코딩했다. 정지 이미지 슬라이드쇼가 아니다. 오디오와 OnGUI 검은 화면 띠는 포함하지 않는다. `CaptureStage1Cutscenes`가 DevManager와 같은 `PlayEntry` / `PlayAssault` 경로를 호출했다. 물리 키 입력 자동화나 다중 클라이언트 검증을 뜻하지 않는다.

## 직접 확인

1. Build Settings에 `Assets/Scenes/Stage1_EntryCutscene.unity`, `Stage1_AssaultCutscene.unity`가 활성화되어 있는지 확인한다.
2. `Stage1_Astra`를 Play한다. 자동 입장 컷씬 종료 후 움직임·시점·태블릿 복구를 확인한다.
3. F10으로 입장 컷씬을 다시 본다. 현재 플레이어의 커마와 실제 기지가 보이며 종료 후 같은 플레이어로 돌아오는지 확인한다.
4. F9로 공세 컷씬을 본다. 5초 경고 이후 실제 운석이 생성되고, 8초 후 조작이 복구되는지 확인한다. 초기 공세 간격 기본값은 10.5초다.
5. 새 Play 세션에서는 F9 대신 견인 주차대 `(23, 2.33, -19)`의 트리거에 진입한다. 동일 전환이 한 번만 발생해야 한다.
6. 런타임 MeteorSpawner의 두 Assault 배율을 1로 바꾸면 해당 간격 감소가 없어지는지 확인한다. 컷씬 전용 유성우가 종료 후 남지 않아야 한다.

## 자동 테스트 재현

```powershell
& '<Unity 6000.5.4f1>/Editor/Unity.exe' -batchmode -nographics -projectPath '<격리 복사본>/METAGAME' -runTests -testPlatform EditMode -testFilter 'Metagame.Gameplay.Tests.Stage1CutsceneTests;Metagame.Gameplay.Tests.MeteorSpawnerTests;Metagame.Gameplay.Tests.VegetationInteractionTests' -testResults '<출력>/review-tests.xml' -logFile '<출력>/review-tests.log'
```

[결과 XML](review-tests.xml): **37 passed / 0 failed**. 실제 Stage1 씬을 로드하는 Play Mode 전환 테스트도 포함한다. 입장·주차대 트리거·공세, 플레이어/파츠 동일성, 카메라/입력/태블릿 복구, 컷씬 효과 제거, 배율 변경, 서버 권위/늦참 상태 적용을 검증한다.

## 녹화 재현

격리 프로젝트의 `Assets/_Project/Scripts/Editor/`에 `tools/unity/CaptureStage1Cutscenes.cs`를 복사한 후 그래픽 모드로 실행한다.

```powershell
& '<Unity 6000.5.4f1>/Editor/Unity.exe' -projectPath '<격리 프로젝트>' -executeMethod CaptureStage1Cutscenes.Run -logFile '<출력>/capture.log'
python tools/unity/encode_cutscene_review.py '<격리 프로젝트>/Validation/Review587' artifacts/stage1-cutscenes
```

인코더는 Pillow가 필요하다. 새 캡처는 비어 있는 `Validation/Review587` 출력 폴더에서 시작한다. 녹화 도구가 해당 검증 에디터를 종료한다.

## 범위와 남은 확인

- 562 아웃라인은 [별도 MR !476](https://lab.ssafy.com/s15-metaverse-game-sub1/S15P21B202/-/merge_requests/476)으로 이미 병합됨. 이번 변경에서 렌더러·아웃라인 셰이더·머티리얼 생성 코드는 수정하지 않았다.
- 577 식생·지형은 기존 보존 요청에 따라 유지했다. develop 대비 변경에 포함되므로 577 선행 머지 또는 스택 검토가 필요하다. [변경 범위 기록](../../docs/records/2026-09-17-stage1-entry-assault-cutscenes.md)을 참고한다.
- 실제 2인 이상 접속, 지연/재접속, 최종 배포 빌드, 공세 수치의 플레이테스트는 이번 검증 범위 밖이다. 테스트 통과를 최종 밸런스 승인으로 간주하지 않는다.
- 그래픽 캡처 시작 시 기존 `UrpProjectSetupAutoRunner`가 Play Mode에서 씬 저장을 요청하는 예외가 재현됐다. 최초 구현 때도 관찰된 에디터 설정 문제이며, 이번 작업에서 해당 파일은 변경하지 않았다. 캡처와 런타임 복구 검사는 끝까지 완료됐다.
