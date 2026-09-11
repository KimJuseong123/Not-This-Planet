# S15P21B202-387 캐릭터 손 외형 에셋 교체 기록

## 작업 정보

- Jira: [S15P21B202-387](https://ssafy.atlassian.net/browse/S15P21B202-387)
- 브랜치: `S15P21B202-387-character-hand-replacement`
- 기준: `develop` 커밋 `03dc68c`
- 범위: 1인칭 손 FBX·PBR 텍스처·정면 프리뷰 교체, 프리팹 연결 확인, 에셋 카탈로그 갱신
- 제외: 물리 손, 도구 잡기 위치, 캐릭터 리깅·애니메이션, 씬 변경

## 입력 원본

| 원본 | SHA-256 |
| --- | --- |
| `character_hand.zip` | `5533D92F4733B758DD771555DB60008DA6CDFCC92B0FBAD804FB6BD3426C3BDC` |
| `hand_front.png` | `BFF91D7860BEE0194BED410B836351464B4120C3786B205728FC11F8F733397C` |

ZIP에는 FBX 1개와 BaseColor·Normal·Roughness·Metallic·RM 텍스처가 한 장씩 들어 있다.

## 산출물 위치

| 구분 | 경로 |
| --- | --- |
| 손 FBX | `METAGAME/Assets/_Project/Models/Characters/Parts/CharacterHand/character_hand.fbx` |
| 손 텍스처 | `METAGAME/Assets/_Project/Models/Characters/Parts/CharacterHand/tripo_convert_a35e2d37-793f-4226-a1df-44583d0b0c71.fbm/` |
| 런타임 프리팹 | `METAGAME/Assets/_Project/Prefabs/Resources/CharacterHand.prefab` |
| 손 정면 프리뷰 | `docs/assets/previews/character_hand_front.png` |
| 카탈로그 | `docs/asset-catalog.md` |

## 교체 방식

- 런타임이 사용하는 `character_hand.fbx` 경로와 Unity GUID를 유지하고 FBX 내용만 교체했다.
- 새 FBX 내부 텍스처 경로에 맞춰 `.fbm` 폴더 이름은 신규 Tripo UUID로 갱신했다.
- 동일한 용도의 텍스처 `.meta`를 함께 이동해 기존 Texture GUID와 임포트 설정을 유지했다.
- `CharacterHand.prefab`은 동일 FBX GUID를 계속 참조하며 `FirstPersonHandView`가 좌우 손을 생성한다.

## Unity 임포트 설정

- FBX: 애니메이션·카메라·라이트 임포트 비활성화, 내장 PBR Material 임포트 활성화
- BaseColor: sRGB 활성화
- Normal: Normal map 타입, sRGB 비활성화
- Roughness·Metallic·RM: sRGB 비활성화
- `FirstPersonHandView`가 메시의 가장 긴 축을 0.28m로 자동 정규화한다.

## 확인 방법

1. Unity Project 창에서 `Assets/_Project/Models/Characters/Parts/CharacterHand`를 연다.
2. `character_hand.fbx`와 신규 UUID `.fbm` 폴더가 각각 한 개씩 보이는지 확인한다.
3. `.fbm` 폴더에 BaseColor·Normal·Roughness·Metallic·RM 텍스처 5장이 있는지 확인한다.
4. `Assets/_Project/Prefabs/Resources/CharacterHand.prefab`을 열어 신규 손 외형이 표시되는지 확인한다.
5. Play Mode에서 로컬 플레이어의 왼손과 오른손이 모두 표시되는지 확인한다.
6. `docs/asset-catalog.md`의 `캐릭터 팔·손 원본` 표에서 신규 정면 프리뷰가 표시되는지 확인한다.

## 자동 검증 결과

- Unity 6000.5.4f1 배치 임포트와 프리팹 재생성을 정상 완료했다.
- `character_hand.fbx`: MeshRenderer 1개, URP/Lit 재질 1개, 신규 BaseColor 연결을 확인했다.
- `CharacterHand.prefab`: MeshRenderer 1개와 동일 URP/Lit 재질·신규 BaseColor 연결을 확인했다.
- 리소스 생성·기존 좌우 반전·물리 컴포넌트 미포함 검증 3건은 통과했다.
- 기존 1인칭 손 EditMode 테스트 5건은 에디터 생명주기 강제 호출 과정의 `ShouldRunBehaviour()` assertion 및 표시 상태 assertion으로 실패했다. 이번 작업에서는 손의 회전·위치·좌우 미러, 런타임 코드와 테스트 코드를 변경하지 않았다.

## 제한 사항과 후속 작업

- 이번 변경은 1인칭 표시용 손의 외형만 교체한다. 기존 물리 손과 잡기 판정은 변경하지 않는다.
- 도구가 손에서 떨어져 보이는 문제는 도구별 손잡이 기준점과 잡기 위치를 맞추는 별도 작업이 필요하다.
- 신규 FBX의 실제 좌우 방향과 손목 각도는 Play Mode에서 최종 확인하고 필요하면 `FirstPersonHandView.Settings`를 조정한다.
