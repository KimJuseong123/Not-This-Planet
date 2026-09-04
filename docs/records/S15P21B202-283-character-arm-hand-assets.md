# S15P21B202-283 캐릭터 팔·손 에셋 추가 기록

## 작업 정보

- Jira: [S15P21B202-283](https://ssafy.atlassian.net/browse/S15P21B202-283)
- 브랜치: `S15P21B202-283-character-arm-hand-assets`
- 기준: `develop` 커밋 `29804894772a9e42217fe2fc6f0ff36e225388a0`
- 범위: 팔·손 FBX와 PBR 텍스처 추가, 정면 프리뷰 등록, 에셋 카탈로그 갱신
- 제외: 씬·Terrain·기존 캐릭터·Prefab·Material·애니메이션 변경

## 입력 원본

| 원본 | SHA-256 |
| --- | --- |
| `character_arm.zip` | `483B7DCE2B0019ED78CE034EA4A1F6F6F6C2DF18856166FB52008C5B44019FF8` |
| `character_hand.zip` | `1141BD79BECED6F881C92492868D11FBAAB0D0B62976D0A55264CDCFA7E3CD28` |
| `character_arm_front.png` | `D5B7925F4C58CCCFEC244745467409CF7EFA7C4FEFCE9336FFF082FFEC099A04` |
| `character_hand_front.png` | `E61A3EA107F7E9260F5DB09777159C70DE9A94E13AA514A3068BC51F0C45A9F0` |

## 산출물 위치

| 구분 | 경로 |
| --- | --- |
| 팔 FBX | `METAGAME/Assets/_Project/Models/Characters/Parts/CharacterArm/character_arm.fbx` |
| 팔 텍스처 | `METAGAME/Assets/_Project/Models/Characters/Parts/CharacterArm/tripo_convert_759ddb75-b9a1-4d38-a8c2-a13b354250d0.fbm/` |
| 손 FBX | `METAGAME/Assets/_Project/Models/Characters/Parts/CharacterHand/character_hand.fbx` |
| 손 텍스처 | `METAGAME/Assets/_Project/Models/Characters/Parts/CharacterHand/tripo_convert_9564344a-1944-47e0-8924-7fe1cb3470ad.fbm/` |
| 팔 정면 프리뷰 | `docs/assets/previews/character_arm_front.png` |
| 손 정면 프리뷰 | `docs/assets/previews/character_hand_front.png` |
| 카탈로그 | `docs/asset-catalog.md` |

각 모델의 `.fbm` 폴더에는 `basecolor`, `normal`, `roughness`, `metallic`, `rm` 텍스처가 한 장씩 있다. FBX 내부에 원본 UUID 경로가 기록되어 있어 `.fbm` 폴더명은 원본 그대로 보존했다.

## Unity 임포트 설정

- FBX: 애니메이션·카메라·라이트 임포트 비활성화, 스케일 1, Material 자동 생성 비활성화
- BaseColor: sRGB 활성화
- Normal: Normal map 타입, sRGB 비활성화
- Roughness·Metallic·RM: sRGB 비활성화
- 모든 신규 에셋과 폴더에 Unity `.meta` 파일 포함

## 확인 방법

1. Unity Project 창에서 `Assets/_Project/Models/Characters/Parts/CharacterArm`과 `CharacterHand`를 연다.
2. `character_arm.fbx`, `character_hand.fbx`가 각각 한 개씩 보이는지 확인한다.
3. 각 UUID `.fbm` 폴더에 텍스처 5장이 있는지 확인한다.
4. Normal 텍스처의 Texture Type이 `Normal map`인지, BaseColor 외 나머지 데이터 텍스처의 sRGB가 꺼져 있는지 확인한다.
5. `docs/asset-catalog.md`의 `캐릭터 팔·손 원본` 표에서 정면 프리뷰 2장이 표시되는지 확인한다.

## 제한 사항과 후속 작업

- 두 FBX는 독립된 원본 메시이며 현재 플레이어 리그와 결합하거나 스킨 가중치를 설정하지 않았다.
- 실제 치수·축·피벗·폴리곤 수와 기존 캐릭터 비율 호환성은 아직 검증하지 않았다.
- Material과 Prefab이 없으므로 현재 상태로 씬에 직접 배치하거나 기존 캐릭터를 교체하지 않는다.
- Unity 라이선스 확인이 필요한 별도 임포트 검증은 수행하지 않았으며, 저장소의 기존 Tripo 에셋 임포트 규칙과 동일한 `.meta` 구조를 적용했다.
