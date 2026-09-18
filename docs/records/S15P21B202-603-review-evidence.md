# Stage2 Earth — 범위·완료 조건·리뷰 근거

대상: S15P21B202-603-plan-2stage. 기획 기준: [chapter-2-plan.md](../planning/chapter-2-plan.md).

이 문서는 MR/Jira에 반영할 구체적인 근거다. 외부 MR 원문은 GitLab API 401로 확인하지 못했고 Jira 연결 도구도 없어 **외부 본문을 수정하지 않았다**. 이 문서를 추가했다고 외부 이슈의 범위 불일치가 해결된 것으로 간주하지 않는다. 아래 범위와 완료 조건을 해당 이슈/MR에 반영해야 한다.

## 범위와 추가 요청

- 기본 범위: Stage1_Astra 수준의 환경 구성을 가진 Stage2_Earth 초안, Terrain, 크레인·컨테이너·분리 로켓 모듈·사다리·수동 체결·오염/제독, 컨셉 아트와 Blender 모델.
- 후속 명시 요청: Terrain 재배치, 배수로 물과 얕은 물 감속/깊은 물 부유, 가스 외 작업 방해 요소와 압력 펌프, 멀티 검증.
- 컷씬 추가 근거: 사용자가 “1stage 엔딩 보면서 만든 우주선이 우주속을 유영하다가 … 운석에 맞고 산산조각 나면서 … 2stage 행성으로 불시착”을 요청했다. 구현은 **하나의 Stage1→Stage2 전환 시퀀스에 유영·충돌/파손·불시착 세 구간을 추가**한 것이다. 기존 인사/탑승/발사를 새로 만든 독립 컷씬 3종으로 설명하면 범위가 잘못 전달된다. 기존 Stage1_Ending 시간축을 23초 연장하며 별도 컷씬 씬을 세 개 추가하지 않는다. [구현과 동기화 근거](2026-09-18-stage2-arrival-cinematic.md).
- 사용자 추가 요청은 확인되지만 Jira의 현재 정의를 대체하지 않는다. 외부 이슈에 위 추가 범위를 명시하거나, 팀의 범위 기준에 따라 별도 이슈/MR로 분리한 뒤 머지 판단한다. 이번 수정에서 이미 요청받은 컷씬을 임의로 삭제하지 않았다.

## 템플릿을 대체할 완료 조건

| 완료 조건 | 확인 방법과 증거 | 현재 확인 수준 |
| --- | --- | --- |
| Stage2_Earth를 열면 TerrainCollider와 4개 지형 레이어, 작업 장비, 주변 폐허·식생이 존재하고 핵심 통행로를 막지 않는다 | Stage2ArtTests, 지형 검사 및 아래 이미지 | 기존 자동 검증 + 이번 회귀 검사 |
| 상호작용은 기존 ID 0~25와 압력 밸브 100~102로 정확히 29개이며 등록 목록과 일치한다 | Stage2ReviewTests의 밸브 0/2/4개 및 ID 변조 실패 검사 | 이번 회귀 검사 |
| Terrain 변환은 기존 지면을 보존하고 씬을 자동 저장하지 않으며 Undo 한 번으로 복원된다 | 변환 전후 씬 파일 바이트, 기존 지면 활성 상태, 신규 Terrain 제거 검사 | 이번 회귀 검사 |
| Terrain 보정은 원본 에셋을 덮어쓰지 않고 배수로 높이를 보존하며 Undo/Redo가 Terrain과 Collider의 데이터 참조를 함께 복원한다 | TerrainData 파일 바이트와 수로 높이, 참조/위치 복원 검사 | 이번 회귀 검사 |
| Stage1 완료 후 조립 형태를 유지한 우주선이 유영·충돌·분리·불시착을 마친 뒤 Stage2로 전환된다 | [컷씬 53개 관련 회귀 검사와 5개 렌더](../../artifacts/stage2-arrival/README.md) | 기존 로컬 시간축·씬 전환 검사; 다중 PC 전환 실측 아님 |
| 배수로 얕은 구간은 감속, 깊은 구간은 부유하며 지상으로 복귀할 수 있다 | [물 구현/검증](2026-09-18-stage2-drainage-water.md), Stage2WaterTests | 기존 로컬/서버 로직 검사 |
| 오염 누적·제독·복구 및 압력 경고/분출/감압이 서버 권한으로 동작한다 | [오염 검사](2026-09-18-stage2-contamination-audit.md), [압력 검사](../../artifacts/stage2-final-validation/README.md) | 자동 검사 및 실제 3프로세스 통신 |
| 호스트·원격 클라이언트·늦참 클라이언트가 상태를 공유하고 이탈한 조종사의 점유가 해제된다 | pressure-final 8 PASS, yard 5 PASS | localhost 실제 네트워크; 인터넷 품질 보장 아님 |
| 1~4인 협동 완주/재미, 인터넷 지연·손실, 최종 아트 품질을 사람이 확인한다 | chapter-2-plan 플레이 테스트 항목 | **미완료**, 자동 검사로 대신하지 않음 |

## 검수 이미지

이미지는 생성형 컨셉 아트가 아니라 Unity 씬/실행 렌더다. 전체/환경 이미지는 이번에 갱신하고 기능별 이미지는 기존 검증 당시 자료를 재사용했다. 이번 에디터 안전성 수정은 씬 파일을 변경하지 않는다. 정지 렌더로 유체 시뮬레이션이나 멀티 동기화를 입증하지 않으며 그 부분은 별도 테스트 결과를 본다.

### Terrain·작업 동선·환경 밀도

![현재 전체 배치](../../artifacts/stage2-review/overview.png)

위 전체 이미지는 이번 리뷰 수정 후 현재 저장된 씬을 Unity 카메라로 다시 렌더한 것이다. 수면과 실행 중 압력 분출의 근접 모습은 아래 두 이미지로 확인한다.

![폐허와 식생](../../artifacts/stage2-review/ruins-and-vegetation.png)

### 배수로와 작업 방해 요소

![배수로 수면](../../artifacts/stage2-water/02-water-surface.png)

9월 18일 물 구현 시점. 물 표현은 수면 셰이더·깊이·흐름·거품/반사 기반이며 체적 유체 시뮬레이션을 구현한 것은 아니다.

![실행 중 압력 펌프 분출](../../artifacts/stage2-pressure/pump-0.png)

9월 18일 압력 펌프 실행 검수. [서버/원격 클라이언트 밀침 실측](../../artifacts/stage2-final-validation/pressure-final/network-smoke.json)과 함께 확인한다.

### Stage1→Stage2 전환

![완성 우주선 유영](../../artifacts/stage2-arrival/01-space-drift.png)

![운석 충돌 후 파손](../../artifacts/stage2-arrival/04-broken-ship.png)

![지구 비상 강하](../../artifacts/stage2-arrival/05-emergency-descent.png)

테스트용 조립 스냅샷으로 촬영한 고정 시점 렌더이며 게임 GUI/자막은 합성되지 않은 이미지다.

## 큰 변경 묶음의 검토 순서

1. 기획과 [초안 기록](2026-09-17-stage2-earth-draft.md): 게임 규칙/참조 구조.
2. [모델 라이브러리](2026-09-17-stage2-model-library.md), [모델 완성 기록](2026-09-17-stage2-model-completion.md): FBX, FBM 텍스처, Blender 원본, 방향별 PNG, Unity .meta로 파일 수가 증가하는 부분.
3. [씬 배치](2026-09-17-stage2-art-integration.md): 장비 부위 연결과 풍경, Terrain, collider/동선.
4. 위의 컷씬·오염·물·압력 기록: 각각 요청 근거, 서버 권한, 테스트 한계.
5. [최종 멀티 검증](../../artifacts/stage2-final-validation/README.md), [이번 리뷰 수정 검증](../../artifacts/stage2-review/README.md).

리뷰가 언급한 880여 파일은 외부 MR 기준을 확인하지 못해 재산정하지 않았다. 로컬 origin/develop 비교는 다른 변경 이력까지 포함할 수 있어 MR 파일 수로 제시하지 않는다.

## Terrain 도구의 안전한 사용

저장된 씬에서 변환/보정을 실행하면 저장되지 않은 미리보기가 생긴다. 기존 지면은 비활성 상태로 남고, 새 TerrainData/머티리얼/레이어는 매번 고유한 Preview- 폴더에 생성된다. 보정은 기존 지형의 복사본으로 작업하며 배수로/작업 구역의 높이와 기존 레이어를 보존한다. 다른 씬은 닫지 않는다. 저장하지 않은 편집이 있으면 실행을 거부한다.

검토 뒤 명시적으로 씬과 에셋을 저장하거나 Undo 한 번으로 되돌린다. Undo 후에도 새 에셋은 Redo를 위해 남는다. Redo 또는 미리보기 씬에서 참조 중인 폴더를 삭제하지 않는다. 최종 폐기할 때는 씬을 복원한 뒤 참조가 없는 해당 Preview- 폴더만 삭제한다. 실패 시에는 이 실행이 생성한 고유 폴더만 정리하고 씬의 변경을 롤백한다.
