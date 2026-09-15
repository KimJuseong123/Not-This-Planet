# Blender → FBX → Astra ver2 반입 규칙

## 기준 크기와 방향

프로젝트의 목표는 **Unity 1 unit = 1m**다. Unity 공식 문서도 미터 단위와 파일 단위 변환을 설명한다. 자동 기본값을 믿고 100배 확대·축소한 채 진행하지 말고, 먼저 1m 기준 정육면체를 함께 반입해 실측한다. [Unity 모델 준비 문서](https://docs.unity3d.com/6000.0/Documentation/Manual/models-preparing.html)

Blender에서는 미터 기준으로 모델링하고 오브젝트 Scale을 1,1,1로 정리한다. FBX의 Forward/Up 축 변환은 Blender 버전과 기존 파일 규칙에 맞춰 사용한다. `-Z Forward / Y Up`을 시작 프리셋으로 사용할 수 있으나, **반입 후 Unity의 +Y가 위, +Z가 도구 발사 방향인지 직접 확인한 파일만 승인**한다. 이름만 같은 프리셋을 재현하는 것보다 실제 결과가 기준이다. [Blender FBX 축 변환 설명](https://docs.blender.org/manual/en/5.2/files/import_export/fbx_legacy.html)

## 납품 단위

- `AST_T01_PocketRocket.fbx`처럼 제작 목록 ID를 사용한다. 동일 물건 세 개를 위해 FBX 세 개를 만들지 않는다.
- 같은 롤러·막대는 변형별 파일 또는 명확한 하위 오브젝트로 제공한다.
- `.blend`, `.fbx`, 외부 텍스처 폴더, 정면/측면/후면/바닥 참고 이미지, 기준 크기 메모를 함께 둔다.
- 텍스처는 FBX 내부 삽입 대신 외부 파일로 제공한다. Unity도 Embed Media를 피하도록 안내한다. [Unity 텍스처 준비](https://docs.unity3d.com/6000.0/Documentation/Manual/models-preparing.html)
- 조명·카메라·사용하지 않는 배경·테스트 물리체는 납품 FBX에서 제외한다. 발광 렌즈와 재질 슬롯은 포함한다.

## 권장 노드 구조

```text
AST_T01_PocketRocket
  Visual
    Body
    Grip
    Fins
    Exhaust
    IndicatorLens
  Anchors
    Grip_R
    Grip_L
    Muzzle
    VFX_Exhaust
```

이름은 제작자가 제공하는 연결 후보점이다. **노드를 같은 이름으로 만들었다고 기존 스크립트에 자동 연결되지는 않는다.** Unity 반입 시 기존 `Model`·`Muzzle`·조작부 참조와 대응시킨다. 사용하지 않는 왼손 위치나 VFX 노드를 억지로 추가할 필요는 없다.

기믹은 다음처럼 가동부를 나눈다.

- 승강기: 고정 Frame / 움직이는 Platform / Platform 아래 DeckFittings / BrakeGrip / 상단 Release / 하단 Reset.
- 격리실: 고정 Frame / 위로 움직이는 Door / 위로 나타나는 Plate / DialA·B·C / NeedleA·B·C / 고정 TargetNotch / IndicatorLens.
- 대포: 고정 Base / Barrel / EntryGrip / RearIgniter / Seat / LaunchExit / WalkOut / FuseStart·FuseEnd. 현재 회전 조준·포신 반동 애니메이션은 게임 기능에 연결되어 있지 않다.
- TNT: Charge / WireSocket. 별도 DetonatorBox / PlungerStem / Handle / Lens. 바위는 Intact와 Debris를 나눈다.
- 연료: CanBody / Handle / Spout / LevelWindow / Lens. 차량의 FuelPort와 Gauge는 별도.
- 탐색기: Handle / Shaft / Coil / Display / LedLens. ScanRing은 Unity 효과이므로 FBX 메시로 고정하지 않는다.

## 피벗

| 종류 | 피벗 기준 |
|---|---|
| 들고 굴리는 물건 | 몸체 중심 또는 명세의 기존 루트. 롤러는 원통 중심, 긴 판은 중앙 |
| 다이얼 바늘 | 정확한 회전 중심, 기존 로컬 Z 회전 |
| 승강기 데크 | 이동 플랫폼 중심. 난간과 하강 손잡이는 데크를 따라야 함 |
| 문 | 문짝 중심. 기존 월드 Y 이동을 그대로 연결 |
| 발판 | 원판 중심. 발자국 표시가 원판을 따라야 함 |
| 건물·고정 받침 | 바닥 기준 원점. 바닥은 씬의 약 Y=1 기준에 맞춤 |
| 연료 게이지 | 표시창을 고정하고 Fill을 분리. 기존 채움은 왼쪽부터 확장되는 X 스케일 |

현재 일부 프로토타입은 루트 자체가 치수 배율이다. 예를 들어 긴 판의 루트는 `(8,.2,1.15)`, 용접기는 `.5`, 다이얼 손잡이는 `.5`다. 실물 치수로 만든 FBX를 그 아래 배율 1로 그대로 붙이면 이중 확대된다. **물리 루트와 참조를 유지하고 새 Visual 전용 자식에서 크기를 맞춘다.** 비균일 배율의 상위 노드가 있는 경우 반입 결과의 길이·회전축·행렬을 확인한다. 기존 루트를 Scale=1로 바꾸어 해결하면 충돌체·그립·네트워크 기준이 바뀔 수 있다.

## 충돌과 재질

외형을 바꾸는 작업의 기본값은 **기존 Unity Collider와 Rigidbody 유지**다. FBX의 자동 MeshCollider 생성으로 교체하지 않는다. 특히 긴 판·롤러·경사판·썰매·문틀의 통과 면은 단순해야 한다. 새로 충돌체가 필요하면 반입 단계에서 별도로 검토한다.

손잡이가 보이는데 잡히는 충돌체가 멀리 있거나, 평평한 바닥이 실제로 울퉁불퉁하면 직관성이 나빠진다. 보이는 접촉 면과 실제 충돌 면을 비교한다. 시각적 장식·그림·LED에 별도 충돌체를 넣지 않는다.

재질 슬롯은 `Body`, `Grip`, `Accent`, `Lens`, `Window`처럼 목적별로 제공한다. 렌즈·잔량 Fill은 상태에 따라 색이나 크기가 바뀌므로 본체와 하나로 합치지 않는다. 색만으로 행동을 설명하지 않도록 손잡이·주입구·갈고리·팬·발자국의 형태를 함께 사용한다. 현재 아이콘은 원본 PNG와 분리된 판 메시로 제공되어 있다.

삼각형 수·텍스처 해상도는 이 문서에서 최종 성능 예산으로 확정하지 않았다. 초기 제작 권장치는 손도구 LOD0 약 3–8천 삼각형, 작은 소품 0.5–2천, 텍스처 1K 내외이나 이는 **제작 제안**이다. 실제 동시 노출량·타깃 하드웨어를 기준으로 최종 조정한다.

## 반입 완료 확인

1. 1m 기준 큐브와 실제 치수가 맞고 위/앞 방향이 올바르다.
2. 모델 교체 전후 Collider, Rigidbody 질량, 스크립트, 네트워크 식별용 루트 경로를 유지했다.
3. 손으로 잡는 위치, 1인칭 복제본, 놓았을 때 자세, 노즐·케이블 시작점을 확인했다.
4. 원격 플레이어의 장비 외형도 같은 상태를 표시한다. 별도 피어 확인 전에는 멀티플레이 검증 완료로 표시하지 않는다.
5. 승강기·문·다이얼·발판의 가동부가 올바른 부모와 피벗을 가진다.
6. 파츠가 기존 낮은 통로와 문, 승강기, 귀환로를 통과한다.
7. 노란 외곽·빨간 LED·부품 색과 형태·끈·그림 안내가 새 재질에서 사라지지 않는다.
8. 삭제된 놀이 시설을 과거 제작 목록 때문에 다시 넣지 않는다.

