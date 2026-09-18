# Stage2 누락 모델 및 FBM 보완

기존 방향 아트 160장·모델 40종을 먼저 `f2ceeca5`로 커밋한 뒤, 2장 기획 §13의 아트 산출물과 모델 목록을 대조했다.

## 누락 대조

| 기획 요구 | 대응 |
| --- | --- |
| 분리형 로켓 | 기존 RocketBase, RocketTank, RocketService, RocketCapsule |
| 로켓 완성 형태 | **추가 RocketAssembled**. 기존 파츠를 쌓아 만든 별도 정적 외형 |
| 크레인 회전부·팔·인양 장치·훅·와이어·조종석 | 기존 Crane, Console. 와이어의 런타임 길이 갱신은 모델과 별개 |
| 발사대·승강기·승강장·사다리·발판 | 기존 Launchpad, Elevator, Ladder에 포함 |
| 수동 렌치 | 기존 Wrench |
| 큰 볼트·너트·브래킷, 체결 상태 외형 | 기존 로켓의 체결 볼트와 별도로 **BoltLoose, BoltTight, Nut, Bracket 추가** |
| 컨테이너·내부 받침 | 기존 Container에 포함 |
| 방독면·보관대 후보 | **HelmetFilter, FilterRack 추가**. 토끼 우주복 헬멧 부착형 필터 후보이며 채택된 게임 규칙으로 확정하지 않음 |
| 제독 분사기 | 기존 Sprayer, Shower |
| 손상 우주선·폐허·오염 시설 | 기존 CrashedCraft, 환경 모듈, GasTank |
| 지구 식생 | 기존 12종 |
| 분사·오염·제독 효과와 캐릭터 동작 | 메시 모델과 다른 VFX·애니메이션 작업. 이번 모델 누락 목록에서 제외 |

추가 7종은 각각 정면·왼쪽·오른쪽·후면 PNG를 먼저 만든 뒤 Blender MCP로 모델링한다. 기존 자료를 포함한 목록은 [방향 아트 안내](../assets/concepts/stage2/model-references/README.md)와 [모델 안내](../../METAGAME/Assets/_Project/Models/Stage2/README_KO.md)를 따른다.

## FBX / FBM 구성

`.fbm`은 메시 파일 형식이 아닌 외부 텍스처 폴더다. 각 `S2_Name.fbx` 옆에 `S2_Name.fbm/`을 두고 FBX의 RelativeFilename이 해당 폴더 PNG를 참조하도록 Blender COPY 모드로 내보낸다. FBX만 이동하면 텍스처가 누락되므로 둘을 함께 복사한다.

Blender에서 15개 기본 재질에 미세한 절차적 색 변화가 있는 512×512 BaseColor 텍스처를 베이크했다. 공통 텍스처는 Unity 공유 재질용 Textures 폴더에, 모델별 사용 텍스처 사본은 각 FBM에 둔다. 텍스처를 외부 서비스에서 가져오거나 빈 폴더로 대체하지 않았다. Blender 원본에도 이미지를 패킹한다.

이 텍스처는 기본 색상 표면 표현이다. 컨셉아트의 손상·오염을 그대로 재현한 고유 PBR 베이크, Normal/AO 맵, LOD는 포함하지 않는다. 기존 면 투영 UV의 중첩·반복을 허용한다.

## 검증 근거

최종 결과: **47 FBX / 47 FBM 폴더 / 47 Blender 원본 / 방향 아트 188장**. Unity 6000.5.4f1 임포트 47/47 통과, 종료 코드 0. 원본 대비 Unity 치수·삼각형·메시 수, 상대 텍스처 경로, FBM과 공통 텍스처의 SHA256 일치 검사에서 실패 0건.

시각 검수에서 완성형 로켓의 복제 파츠 위치가 겹치는 문제를 발견하여 원본 월드 좌표와 적층 높이를 정점에 적용했다. 최종 높이 약 11.515m를 확인하고 재임포트했다. 체결 볼트는 머리가 와셔에 밀착되도록, 느슨한 볼트는 나사산과 머리 아래 와셔가 보이도록 보완했다. 로켓·브래킷·필터 후면 아트가 정면을 반복한 사례는 다시 생성했다.

- FBX 상대 텍스처 경로: [검증 JSON](../../artifacts/stage2-models/fbx-relative-texture-validation.json)
- Unity 메시·재질·FBM 텍스처: [임포트 결과](../../artifacts/stage2-models/unity-import-validation.json)
- 원본과 Unity 치수·삼각형·메시 수 및 이미지 존재: [최종 교차 검증](../../artifacts/stage2-models/delivery-validation.json)

씬 교체, 게임 규칙 추가, 새 멀티플레이 동작은 이번 범위에 포함하지 않는다. 기존 서버 권한 기믹의 시각 자식으로 통합할 수 있는 에셋 라이브러리다.
