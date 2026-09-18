# Stage2 모델 라이브러리 제작 기록

## 요청과 범위

603 브랜치에서 기존 컨셉아트를 먼저 커밋한 뒤, 에셋마다 정면·왼쪽·오른쪽·후면을 각각 별도 PNG로 제작하고 Blender MCP로 모델링했다. 선행 커밋은 `e323f783 art: Stage2 씬 기반 컨셉아트 8종 추가`이다. 이후 방향 아트·모델은 후속 작업물이며 이 기록 시점에는 커밋하지 않았다.

- [기준 기획](../planning/chapter-2-plan.md)
- [방향 아트 160장과 디자인 명세](../assets/concepts/stage2/model-references/README.md)
- [모델 40종 사용 안내](../../METAGAME/Assets/_Project/Models/Stage2/README_KO.md)
- [실제 모델 검수 렌더 200장](../../artifacts/stage2-models/README.md)

## 제작

장비·로켓·우주선·환경 28종과 식생 12종을 제작했다. 내장 ImageGen으로 방향별 컨셉을 생성하고, 연결된 Blender 5.2.2 LTS의 MCP 코드 실행으로 각 에셋을 독립된 씬에 모델링했다. 기존 사용자의 Scene은 보존했다. FBX와 에셋별 `.blend` 원본을 저장했으며 원본은 Unity 임포트 제외 폴더 Source~에 보관한다.

크레인 끝면, 렌치 옆면, 분사기와 우주선 앞뒤처럼 시점이 잘못 반복된 이미지는 재생성했다. 모델 검수에서 크레인 훅을 트롤리 하위로 수정하고, 조종석 미리보기의 잘림과 추락 우주선 유리창이 선체에 가려지는 문제를 수정했다.

## 검증

- [Blender 개별 결과](../../artifacts/stage2-models/README.md): 40개 FBX 재임포트, 원본 크기·메시 수·UV 일치.
- [Unity 결과](../../artifacts/stage2-models/unity-import-validation.json): Unity 6000.5.4f1, 40/40, URP 재질·UV·유효한 경계 통과. 배치 프로세스 종료 코드 0.
- [최종 교차 검증](../../artifacts/stage2-models/delivery-validation.json): 40개 모두 Unity 축 변환 후 치수, 삼각형 수, 메시 수 일치 및 방향 아트 4장 존재 확인.
- 스크립트 컴파일 오류 없음. 기존 테스트의 구식 API 경고 및 Mirror 어셈블리 해석 경고는 로그에 남아 있으나 임포트 검증은 완료됐다.

## 결과의 범위

로우폴리 형상과 기본 공유 재질, 면 투영 UV, 조작용 피벗을 제공한다. 컨셉아트의 사실적인 녹·오염·스크래치와 같은 PBR 텍스처를 베이크하지 않았다. UV는 중첩 가능하며 식생은 양면 폴리곤이다. LOD·충돌체·애니메이션 클립은 포함하지 않는다.

Stage2_Earth 씬의 시각물을 이 모델들로 교체하거나 새 네트워크 동작을 추가하지 않았다. 기존 서버 권한 기믹에 시각 자식과 피벗을 연결하는 후속 통합이 필요하다. 이번 검증은 에셋 임포트와 형상 검증이며 멀티플레이 실기 플레이를 검증했다는 뜻이 아니다.
