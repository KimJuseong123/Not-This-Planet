# P01 운반 롤러 — Blender 제작 및 씬 적용

2026-09-15. 대상: `METAGAME/Assets/Scenes/Stage1_Astra_ver2.unity`.

## 제작물

- [Blender 원본](AST_P01_TransportRollers.blend), [전체 미리보기](overview.png).
- 정면/측면/후면/바닥: `front.png`, `side.png`, `rear.png`, `bottom.png`.
- Unity FBX, 프리팹, URP Lit 재질: `METAGAME/Assets/_Project/Art/AstraP01/`.
- 생성 스크립트: `tools/build_astra_p01_rollers.py` — Blender MCP의 Python 실행으로 제작.
- 반입 도구: `METAGAME/Assets/_Project/Scripts/Editor/AstraP01RollerArt.cs`.

| 변형 | 길이 × 지름(m) | 수량 | 기존 질량(kg) |
|---|---|---:|---:|
| Standard | 3.0 × .28 | 12 | 3 |
| Heavy | 3.2 × .80 | 3 | 10 |
| Practice | 1.6 × .32 | 2 | 2 |
| Interior | 1.8 × .32 | 2 | 3 |

각 모델 674 vertices / 1,344 triangles. 중심 피벗, 연속적인 매끈한 원통, 둥근 테두리와 안으로 들어간 끝마개. 흰색 금속 본체·주황 밴드·어두운 끝마개·강철 슬롯을 분리했다. 굵은 롤러는 기존 주황색 본체를 유지한다. 모터, 돌출 손잡이, 추가 충돌체, 글자/숫자는 없다. 단색 재질이므로 외부 이미지 텍스처가 필요하지 않다.

## 적용 방법과 보존 범위

Blender Z 길이축을 FBX `-Z Forward / Y Up`으로 내보내어 Unity Y 길이축으로 반입했다. 네 FBX의 실측 크기와 중심, 별도 1m 큐브를 Unity에서 확인했다. 기준 큐브는 게임 씬에 배치하지 않았다.

기존 원통 아래 `P01 Blender Visual` 프리팹 자식을 붙였다. 기존 원통의 렌더러만 비활성화하고, 자식 배율을 `(1/지름, 2/길이, 1/지름)`으로 보정해 기존 원통 프리미티브의 로컬 1×2×1 공간에 맞췄다. 기존 부모의 회전·비균일 배율과 합쳐진 실제 월드 범위까지 대조했다.

기존 오브젝트 이름, 경로, 위치, 회전, 배율, Collider, Rigidbody, Grabbable 및 기타 스크립트와 직렬화된 참조를 유지했다. 각 프리팹은 URP Lit 재질 4개를 공유한다.

## 검증

Unity 6000.5.4f1의 기존 분리 검증 프로젝트 `.local-validation/astra-ver2`에서 실행했다. 열린 원본 Unity Editor에 대한 MCP 연결은 제공되지 않았다. 검증 후 씬과 새 에셋을 원본 프로젝트로 복사하고 해시를 대조했다.

- Unity 컴파일 및 FBX 반입 성공, 실행 종료 코드 0.
- 1m 기준, 4개 변형 크기·축·피벗 검사 통과.
- 19개 모두 기존 렌더 범위와 새 렌더 범위 일치(허용 오차 2mm).
- 기존 물리 컴포넌트·스크립트의 변경 전후 직렬화 비교 통과.
- 씬 저장/재열기 후 19개 인스턴스와 URP 재질 참조 확인.
- 원래 씬 레코드 12,515개 중 삭제 0개. 기존 레코드 변경은 부모의 자식 목록 19개와 렌더러 활성값 19개뿐. 새로운 프리팹 인스턴스/Transform 레코드 38개. [직렬화 비교 결과](serialization.json).
- [반입 검사 결과](integration.txt), 실제 씬 렌더: [일반](standard-in-scene.png), [굵은 롤러](heavy-in-scene.png), [연습](practice-in-scene.png), [실내](interior-in-scene.png).
- 첫 렌더의 재질 초기화가 끝나도록 미리보기는 카메라를 4회 렌더한 후 캡처한다.

실행 메서드: `Metagame.EditorTools.AstraP01RollerArt.Apply`. 이미 적용된 씬에서는 중복 적용을 거부한다. `RenderScenePreviews`는 씬을 수정하지 않고 미리보기만 생성한다. 상세 실행 로그는 분리 프로젝트의 `p01-apply.log`, `p01-preview.log`에 있다. 라이선스 초기 연결 경고 후 로컬 라이선스로 실행을 완료했으며 C# 컴파일 오류는 없었다.

## 남은 확인 사항

원래 배치의 굵은 롤러 3개는 X=38/39.3/40.6에 길이 3.2m로 놓여 서로 겹친다. 실내 롤러 2개도 X=46.5/47.05에 길이 1.8m로 놓여 겹친다. 외형 교체 시 좌표를 유지했으므로 이 겹침과 주변 썰매에 의한 가림이 남아 있으며, 겹친 표면에서는 밴드 경계가 깜빡일 수 있다. 새 메시 단독에는 겹친 표면이 없다. 배치 수정은 별도 작업이다.

실제 플레이어의 잡기/놓기, 멀티플레이 피어, 전체 실행 파일 빌드는 수행하지 않았다. 물리 및 네트워크 설정은 직렬화 비교로 보존을 확인했다. 원본 Unity에서 해당 씬이 열린 상태였다면 디스크 변경을 다시 불러와야 화면에 반영된다.
