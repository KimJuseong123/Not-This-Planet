# Stage 1 도구 거치대 컨셉

작업 브랜치: `S15P21B202-594-art`

원본 도구의 둥근 SF 형태, 흰색 하우징, 연보라색 패널, 청록색 포인트를 반영한 거치대 2종입니다. 거치대 구조가 가려지지 않도록 모든 컨셉은 도구를 제외한 빈 상태로 제작했습니다.

| 종류 | 대표 컨셉 | 정면 | 후면 | 왼쪽 | 오른쪽 |
|---|---|---|---|---|---|
| 방망이 | [컨셉](meteor-bat/01-concept.png) | [정면](meteor-bat/02-front.png) | [후면](meteor-bat/03-back.png) | [왼쪽](meteor-bat/04-left.png) | [오른쪽](meteor-bat/05-right.png) |
| 용접건 | [컨셉](plasma-welding-gun/01-concept.png) | [정면](plasma-welding-gun/02-front.png) | [후면](plasma-welding-gun/03-back.png) | [왼쪽](plasma-welding-gun/04-left.png) | [오른쪽](plasma-welding-gun/05-right.png) |

각 이미지는 하나의 거치대와 하나의 시점만 포함한 개별 PNG입니다. 정면은 베이스의 청록색 상태등이 있는 면입니다. 왼쪽 그림에서는 정면이 화면 왼쪽을, 오른쪽 그림에서는 정면이 화면 오른쪽을 향합니다.

## 설계 의도

- 방망이: 앞쪽 낮은 손잡이 끝 받침과 뒤쪽 높은 U자 받침으로 세워 기대는 구조. 손잡이를 잡고 위로 꺼낼 수 있는 개방형 디자인.
- 용접건: 앞쪽 높은 몸통 받침과 뒤쪽 낮은 손잡이 끝 받침의 2점 지지 구조. 양쪽에서 손잡이를 잡을 공간을 확보. 오른쪽 베이스에 전원 포트와 점검 패널 배치.

이미지 생성 기반의 컨셉아트이며, 정밀 CAD 도면이나 동일 3D 모델을 회전 렌더링한 결과는 아닙니다. 시점별 패널·나사·쿠션 형태에 작은 차이가 있으므로 모델링 시 대표 컨셉을 기준으로 통일하고 원본 도구에 맞춰 받침 간격과 크기를 조정합니다.

## 원본 참고 자료

저장소 루트 기준:

- 방망이 FBX: `METAGAME/Assets/_Project/Models/Props/Stage1/SF_Stage1_MeteorBat.fbx`
- 방망이 텍스처: `METAGAME/Assets/_Project/Textures/Props/Stage1/MeteorBat/T_Stage1_MeteorBat_BaseColor.jpg`
- 방망이 기존 프리뷰: `docs/assets/previews/SF_Stage1_MeteorBat.png`
- 용접건 FBX: `METAGAME/Assets/_Project/Models/Props/Stage1/PlasmaWeldingGun/SF_Stage1_PlasmaWeldingGun.fbx`
- 용접건 텍스처: `METAGAME/Assets/_Project/Textures/Props/Stage1/PlasmaWeldingGun/T_Stage1_PlasmaGun_Part00..30_BaseColor.jpg`

이 브랜치에는 두 도구의 원래 .fbm 폴더가 없고, FBX 내부에는 과거 .fbm 텍스처 경로가 남아 있습니다. 저장소에 분리 보관된 텍스처를 FBX의 재질 파트 번호에 맞춰 연결하여 Blender로 확인했습니다. 확인용 이미지는 [방망이](references/bat-textured.png), [용접건](references/gun-textured.png)에 있습니다. 이 렌더는 형태와 색상 확인용이며 Unity 조명·셰이더를 그대로 재현하지 않습니다.

## 생성 기록

내장 image_gen 도구 사용. 각 결과물을 개별 호출로 생성하고, 방향별 이미지는 해당 대표 컨셉을 참조했습니다. 원본 결과는 보존하고 이 폴더에 복사했습니다. 최종 프롬프트는 [PROMPTS.md](PROMPTS.md)에 기록했습니다.
