# Stage2 모델 라이브러리

2026-09-17 · 603 워크트리 · Blender MCP / Blender 5.2.2 LTS

[2장 기획](../../../../../docs/planning/chapter-2-plan.md)과 [씬 기반 컨셉](../../../../../docs/assets/concepts/stage2/v3-scene-draft/README.md)에 맞춘 **47종 로우폴리 모델 초안**입니다. 에셋별 정면·왼쪽·오른쪽·후면 PNG를 먼저 생성하고 해당 형상을 참고해 Blender MCP에서 모델링했습니다.

- FBX 47개, FBM 텍스처 폴더 47개, 편집 가능한 Blender 원본 47개, 공유 URP 재질 15개.
- [방향별 컨셉아트 188장](../../../../../docs/assets/concepts/stage2/model-references/README.md).
- [실제 모델 검수 렌더](../../../../../artifacts/stage2-models/README.md): 모델마다 네 방향과 사선 미리보기 5장.
- 검증 결과는 하단 보완 기록과 artifacts/stage2-models/unity-import-validation.json을 참고합니다. UV, 스케일, 메시 수, URP 재질, FBM 텍스처 경로를 검사합니다.

## 사용

**FBX와 같은 이름의 FBM 폴더를 함께 복사합니다.** 예: S2_Crane.fbx + S2_Crane.fbm/. FBM은 독립 메시 형식이 아니라 해당 FBX가 참조하는 PNG 텍스처 묶음입니다. Blender COPY 모드로 상대 경로를 기록했습니다. 기본 BaseColor 맵은 512×512이며 Blender에서 절차적 색 변화를 베이크했습니다. Unity의 공유 재질은 Textures 폴더의 같은 원본을 사용합니다. Source~의 Blender 파일에는 이미지를 패킹했습니다.

[누락 모델 대조 및 FBM 보완 기록](../../../../../docs/records/2026-09-17-stage2-model-completion.md)

FBX를 시각 오브젝트로 사용합니다. 단위는 미터이고 아래 치수는 Unity X(폭) × Y(높이) × Z(깊이)입니다. Source~ 폴더는 Unity가 직접 임포트하지 않는 Blender 원본 보관소입니다. Unity 자동 생성 메타와 재질 참조를 함께 유지합니다.

형상, 기본 색상 텍스처가 연결된 재질, 파츠 피벗을 제공합니다. 녹·스크래치·오염 PBR 텍스처, 베이크용 비중첩 UV, LOD, 충돌체, 애니메이션 클립은 제작하지 않았습니다. UV는 면 기준 투영으로 중첩될 수 있습니다. 식생 잎은 양면 폴리곤이며 폐곡면 메시가 아닙니다. 컨셉의 표면 노화와 식생 밀도는 단순화했습니다. 냉각탑은 온전한 테두리의 기본 형상으로 제작했습니다.

이번 산출물은 모델 라이브러리이며 Stage2_Earth 씬에 교체 배치하지 않았습니다. 기존 서버 권한 상호작용·충돌체·NetworkIdentity 아래의 시각 자식으로 연결해야 합니다. 모델 자체에 네트워크 컴포넌트를 추가하거나 시각 메시의 피벗을 게임 상태로 삼지 않습니다.

## 동작 연결 지침

- 크레인: Root → SlewPivot → Trolley → HookHeight. 회전은 SlewPivot, 가로 이동은 Trolley, 인양은 HookHeight에 연결합니다. 현재 케이블은 정적인 메시이므로 훅 높이에 맞춰 길이를 갱신하거나 LineRenderer로 교체해야 합니다. 고정 중간 발판은 회전부와 분리했습니다.
- 승강기 Cabin과 Gate, 컨테이너 LidHinge, 사다리 프레임, 콘솔 Lever 피벗은 정적 본체와 분리했습니다. 실제 이동 제한·닫힘 판정·서버 동기화는 기존 게임 시스템에서 처리합니다.
- 로켓 3개 이동 파츠는 각 3개의 전면 하단 볼트 피벗을 제공합니다. 모델 볼트 장식과 게임의 체결 완료 상태를 별도로 연결합니다.
- 수동 렌치는 AdjustableJaw만 분리했습니다. 공구 잡기 위치와 플레이어 애니메이션은 프리팹에서 조정합니다.
- 식생·폐허·바닥은 정적 시각 에셋입니다. 통행 가능 구역에 장식 메시 콜라이더를 일괄 생성하지 않습니다.

## 에셋 목록

| 에셋 | 파일 | 폭 × 높이 × 깊이(m) | 삼각형 | 추가 피벗 |
| --- | --- | --- | ---: | --- |
| Crane | [FBX](S2_Crane.fbx) · [FBM](S2_Crane.fbm/) · [Blender](Source~/S2_Crane.blend) | 30.13 × 22.05 × 4.55 | 9372 | SlewPivot, Trolley, HookHeight |
| Elevator | [FBX](S2_Elevator.fbx) · [FBM](S2_Elevator.fbm/) · [Blender](Source~/S2_Elevator.blend) | 4.45 × 11.00 × 4.00 | 4192 | LiftCabin, CabinGate |
| Console | [FBX](S2_Console.fbx) · [FBM](S2_Console.fbm/) · [Blender](Source~/S2_Console.blend) | 1.50 × 1.58 × 0.80 | 1192 | Lever_0, Lever_1, Lever_2 |
| Launchpad | [FBX](S2_Launchpad.fbx) · [FBM](S2_Launchpad.fbm/) · [Blender](Source~/S2_Launchpad.blend) | 6.00 × 0.57 × 6.00 | 7688 | 루트 |
| RocketBase | [FBX](S2_RocketBase.fbx) · [FBM](S2_RocketBase.fbm/) · [Blender](Source~/S2_RocketBase.blend) | 2.81 × 2.16 × 2.84 | 3520 | 루트 |
| RocketTank | [FBX](S2_RocketTank.fbx) · [FBM](S2_RocketTank.fbm/) · [Blender](Source~/S2_RocketTank.blend) | 2.90 × 3.40 × 3.05 | 2712 | Bolt_1, Bolt_2, Bolt_3 |
| RocketService | [FBX](S2_RocketService.fbx) · [FBM](S2_RocketService.fbm/) · [Blender](Source~/S2_RocketService.blend) | 2.90 × 3.40 × 3.05 | 3144 | Bolt_1.001, Bolt_2.001, Bolt_3.001 |
| RocketCapsule | [FBX](S2_RocketCapsule.fbx) · [FBM](S2_RocketCapsule.fbm/) · [Blender](Source~/S2_RocketCapsule.blend) | 2.90 × 3.40 × 3.05 | 2708 | Bolt_1.002, Bolt_2.002, Bolt_3.002 |
| Container | [FBX](S2_Container.fbx) · [FBM](S2_Container.fbm/) · [Blender](Source~/S2_Container.blend) | 5.10 × 4.20 × 5.24 | 3088 | RoofHinge |
| Ladder | [FBX](S2_Ladder.fbx) · [FBM](S2_Ladder.fbm/) · [Blender](Source~/S2_Ladder.blend) | 1.95 × 5.14 × 3.25 | 4260 | UpperPlatform, FrontFrame, RearFrame |
| Wrench | [FBX](S2_Wrench.fbx) · [FBM](S2_Wrench.fbm/) · [Blender](Source~/S2_Wrench.blend) | 0.21 × 0.55 × 0.08 | 1808 | AdjustableJaw |
| ToolRack | [FBX](S2_ToolRack.fbx) · [FBM](S2_ToolRack.fbm/) · [Blender](Source~/S2_ToolRack.blend) | 1.20 × 1.40 × 0.67 | 972 | 루트 |
| Sprayer | [FBX](S2_Sprayer.fbx) · [FBM](S2_Sprayer.fbm/) · [Blender](Source~/S2_Sprayer.blend) | 0.26 × 0.37 × 0.58 | 944 | 루트 |
| Shower | [FBX](S2_Shower.fbx) · [FBM](S2_Shower.fbm/) · [Blender](Source~/S2_Shower.blend) | 2.12 × 2.56 × 2.00 | 1256 | 루트 |
| GasTank | [FBX](S2_GasTank.fbx) · [FBM](S2_GasTank.fbm/) · [Blender](Source~/S2_GasTank.blend) | 3.60 × 2.93 × 2.20 | 2592 | 루트 |
| CrashedCraft | [FBX](S2_CrashedCraft.fbx) · [FBM](S2_CrashedCraft.fbm/) · [Blender](Source~/S2_CrashedCraft.blend) | 4.10 × 3.20 × 5.46 | 2868 | 루트 |
| Wall | [FBX](S2_Wall.fbx) · [FBM](S2_Wall.fbm/) · [Blender](Source~/S2_Wall.blend) | 4.00 × 2.85 × 0.41 | 412 | 루트 |
| Drain | [FBX](S2_Drain.fbx) · [FBM](S2_Drain.fbm/) · [Blender](Source~/S2_Drain.blend) | 1.00 × 0.28 × 2.00 | 2160 | 루트 |
| Slab | [FBX](S2_Slab.fbx) · [FBM](S2_Slab.fbm/) · [Blender](Source~/S2_Slab.blend) | 2.00 × 0.13 × 2.00 | 576 | 루트 |
| Rail | [FBX](S2_Rail.fbx) · [FBM](S2_Rail.fbm/) · [Blender](Source~/S2_Rail.blend) | 2.15 × 1.10 × 0.35 | 688 | 루트 |
| Bollard | [FBX](S2_Bollard.fbx) · [FBM](S2_Bollard.fbm/) · [Blender](Source~/S2_Bollard.blend) | 0.44 × 0.85 × 0.44 | 500 | 루트 |
| Facade | [FBX](S2_Facade.fbx) · [FBM](S2_Facade.fbm/) · [Blender](Source~/S2_Facade.blend) | 4.00 × 5.00 × 0.50 | 1444 | 루트 |
| CoolingTower | [FBX](S2_CoolingTower.fbx) · [FBM](S2_CoolingTower.fbm/) · [Blender](Source~/S2_CoolingTower.blend) | 9.60 × 17.95 × 9.60 | 2380 | 루트 |
| Chimney | [FBX](S2_Chimney.fbx) · [FBM](S2_Chimney.fbm/) · [Blender](Source~/S2_Chimney.blend) | 2.50 × 12.00 × 2.50 | 1932 | 루트 |
| Rubble | [FBX](S2_Rubble.fbx) · [FBM](S2_Rubble.fbm/) · [Blender](Source~/S2_Rubble.blend) | 2.08 × 0.61 × 1.43 | 500 | 루트 |
| Floodlight | [FBX](S2_Floodlight.fbx) · [FBM](S2_Floodlight.fbm/) · [Blender](Source~/S2_Floodlight.blend) | 1.39 × 2.57 × 1.34 | 1180 | LampTilt, LampTilt.001 |
| RouteBlue | [FBX](S2_RouteBlue.fbx) · [FBM](S2_RouteBlue.fbm/) · [Blender](Source~/S2_RouteBlue.blend) | 2.00 × 0.13 × 2.00 | 588 | 루트 |
| RouteAmber | [FBX](S2_RouteAmber.fbx) · [FBM](S2_RouteAmber.fbm/) · [Blender](Source~/S2_RouteAmber.blend) | 2.00 × 0.13 × 2.00 | 588 | 루트 |
| GrassDry | [FBX](S2_GrassDry.fbx) · [FBM](S2_GrassDry.fbm/) · [Blender](Source~/S2_GrassDry.blend) | 0.37 × 0.30 × 0.37 | 320 | 루트 |
| GrassSeed | [FBX](S2_GrassSeed.fbx) · [FBM](S2_GrassSeed.fbm/) · [Blender](Source~/S2_GrassSeed.blend) | 0.43 × 0.78 × 0.44 | 772 | 루트 |
| WeedRosette | [FBX](S2_WeedRosette.fbx) · [FBM](S2_WeedRosette.fbm/) · [Blender](Source~/S2_WeedRosette.blend) | 0.56 × 0.23 × 0.55 | 172 | 루트 |
| Wildflowers | [FBX](S2_Wildflowers.fbx) · [FBM](S2_Wildflowers.fbm/) · [Blender](Source~/S2_Wildflowers.blend) | 0.43 × 0.42 × 0.41 | 760 | 루트 |
| ScrubDry | [FBX](S2_ScrubDry.fbx) · [FBM](S2_ScrubDry.fbm/) · [Blender](Source~/S2_ScrubDry.blend) | 0.85 × 0.74 × 0.72 | 1208 | 루트 |
| Shrub | [FBX](S2_Shrub.fbx) · [FBM](S2_Shrub.fbm/) · [Blender](Source~/S2_Shrub.blend) | 0.87 × 0.83 × 0.81 | 2072 | 루트 |
| IvyLow | [FBX](S2_IvyLow.fbx) · [FBM](S2_IvyLow.fbm/) · [Blender](Source~/S2_IvyLow.blend) | 1.32 × 0.24 × 0.30 | 1256 | 루트 |
| IvyHanging | [FBX](S2_IvyHanging.fbx) · [FBM](S2_IvyHanging.fbm/) · [Blender](Source~/S2_IvyHanging.blend) | 1.32 × 1.32 × 0.30 | 1256 | 루트 |
| Moss | [FBX](S2_Moss.fbx) · [FBM](S2_Moss.fbm/) · [Blender](Source~/S2_Moss.blend) | 0.62 × 0.06 × 0.40 | 820 | 루트 |
| Lichen | [FBX](S2_Lichen.fbx) · [FBM](S2_Lichen.fbm/) · [Blender](Source~/S2_Lichen.blend) | 0.62 × 0.05 × 0.42 | 820 | 루트 |
| Reeds | [FBX](S2_Reeds.fbx) · [FBM](S2_Reeds.fbm/) · [Blender](Source~/S2_Reeds.blend) | 0.42 × 0.88 × 0.47 | 432 | 루트 |
| DeadSapling | [FBX](S2_DeadSapling.fbx) · [FBM](S2_DeadSapling.fbm/) · [Blender](Source~/S2_DeadSapling.blend) | 0.78 × 1.08 × 0.65 | 512 | 루트 |
| RocketAssembled | [FBX](S2_RocketAssembled.fbx) · [FBM](S2_RocketAssembled.fbm/) · [Blender](Source~/S2_RocketAssembled.blend) | 2.90 × 11.52 × 3.05 | 12084 | 루트 |
| BoltLoose | [FBX](S2_BoltLoose.fbx) · [FBM](S2_BoltLoose.fbm/) · [Blender](Source~/S2_BoltLoose.blend) | 0.30 × 0.48 × 0.30 | 2912 | BoltRotation |
| BoltTight | [FBX](S2_BoltTight.fbx) · [FBM](S2_BoltTight.fbm/) · [Blender](Source~/S2_BoltTight.blend) | 0.30 × 0.16 × 0.30 | 380 | BoltRotation.001 |
| Nut | [FBX](S2_Nut.fbx) · [FBM](S2_Nut.fbm/) · [Blender](Source~/S2_Nut.blend) | 0.24 × 0.28 × 0.12 | 3264 | 루트 |
| Bracket | [FBX](S2_Bracket.fbx) · [FBM](S2_Bracket.fbm/) · [Blender](Source~/S2_Bracket.blend) | 0.55 × 0.51 × 0.38 | 550 | 루트 |
| HelmetFilter | [FBX](S2_HelmetFilter.fbx) · [FBM](S2_HelmetFilter.fbm/) · [Blender](Source~/S2_HelmetFilter.blend) | 0.31 × 0.15 × 0.21 | 1800 | 루트 |
| FilterRack | [FBX](S2_FilterRack.fbx) · [FBM](S2_FilterRack.fbm/) · [Blender](Source~/S2_FilterRack.blend) | 1.10 × 1.30 × 0.50 | 3168 | 루트 |

## 기획 대조

| 기획 요소 | 모델 대응 |
| --- | --- |
| §3 지구 불시착, 폐허 발사장 | CrashedCraft, Wall, Facade, CoolingTower, Chimney, Rubble |
| §6 컨테이너를 열고 파츠 확보 | Container의 지붕 힌지와 내부 받침 |
| §7 고정 축 크레인, 시야 제한, 협동 인양 | Crane, Elevator, Console, Launchpad, 로켓 4종 |
| §8 이동식 사다리와 고소 작업 | Ladder의 A형 프레임·발판·난간·바퀴 |
| §9 수동 렌치, 전면 하단 3볼트 | Wrench, ToolRack, 로켓 이동 파츠별 볼트 피벗 |
| §10 오염·제독 | Sprayer, Shower, GasTank. 분사·가스는 런타임 효과로 분리 |
| §12 멀티플레이 | 시각 자산만 제공. 서버 권한 로직과 상태 동기화는 기존 시스템 유지 |

재생성은 tools/blender/Stage2/build_assets.py를 Blender MCP로 실행합니다. 기본적으로 기존 파일을 덮어쓰지 않습니다. Unity 메뉴 Not This Planet / Validation / Import and Verify Stage2 Models에서 임포트를 다시 검증할 수 있습니다.
