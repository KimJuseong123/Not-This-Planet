# Stage1 상호작용 오브젝트 가림 테두리 (S15P21B202-591)

- 작업 위치: S15P21B202-591-ui 워크트리 / 같은 이름의 브랜치.
- Unity 6000.5.4f1 / URP 17.5.0. 화면 직접 조작 없이 파일 수정 및 백그라운드 자동 테스트로 작업.

## 동작

Stage1_Astra의 ToolBase, SpaceshipPart, InteractableBase, Grabbable 아래 활성 메시를 자동 수집한다. 용접건, 방망이, 우주선 파츠, 고정못과 그 외 상호작용 물체가 대상이다. 플레이어 신체와 들고 있는 도구(원격 휴대 상태 포함)는 제외한다.

로컬 GameSession.LocalPlayer.Cam.Cam에만 적용한다. 화면 밖/카메라 레이어 밖은 제외하고, 카메라 깊이보다 뒤에 있는 실루엣 가장자리만 기본 2픽셀 금색으로 그린다. 부분 가림에서는 가려진 윤곽만 표시하고 물체 내부를 채우지 않는다. 물리 및 네트워크 상태는 변경하지 않는다.

## 구현 및 설정

- OccludedInteractionTargets: 대상 및 메시 캐시를 0.5초마다 갱신하고, 매 프레임 활성/휴대/시야/레이어 상태를 판정한다.
- OccludedInteractionRendererFeature: 대상마다 독립된 실루엣/깊이 마스크를 만들고 카메라 깊이와 비교해 합성한다. 상호작용 물체가 다른 대상 뒤에 숨어도 윤곽을 잃지 않는다. 합성 영역은 투영 사각형으로 제한한다.
- 마스크는 카메라 깊이를 변경하지 않는다. 각 합성 draw는 별도 MaterialPropertyBlock으로 텍스처를 바인딩하며, 기존 렌더러의 재질/property block을 건드리지 않는다.
- 두 셰이더는 Resources에서 로드하여 빌드에 포함한다.
- PC_Renderer와 Mobile_Renderer에 활성 피처를 등록했다. UrpProjectSetup 재실행 시에도 등록과 활성 상태를 유지한다.
- 색상과 두께는 각 Renderer의 Occluded Interaction Outline 설정에서 조절한다.
- 기존 용접 파츠 테두리는 유지한다. 씬/프리팹 수정이나 추가 Inspector 배선은 필요 없다.

## 검증

Unity 6000.5.4f1 컴파일 성공. 최종 자동 테스트 26/26 통과, 실패/건너뜀 0 (2026-09-17).

검증 결과와 자동 렌더 이미지: artifacts/occluded-interactions/tests-final.xml 및 같은 폴더의 PNG.

- EditMode: 카메라 시야/레이어, 비활성 메시, 원격 도구의 휴대·드롭, 중복 메시, 리소스 셰이더 컴파일, 깊이에 따른 실제 GPU 픽셀 검사.
- 실제 Stage1_Astra 씬을 읽어 도구·파츠·고정못의 활성 메시가 대상에 포함되는지 검사.
- 자동 Play Mode: 벽 뒤 표시, 상호작용 물체끼리의 가림, 일반 시야, 화면 밖, 로컬 플레이어가 없는 카메라, 낮은 품질 렌더러. 렌더 텍스처를 읽어 검증하며 화면/입력 자동화는 사용하지 않는다.
- 기존 PartOutlineGroupTests 회귀 검사 포함.

## 한계

- 실제 Stage1 전체 플레이 및 복수 네트워크 클라이언트 동시 실행은 검증하지 않았다. 로컬 카메라 제한과 원격 휴대 상태는 자동 테스트로 확인했다.
- 가림은 깊이를 기록하는 표면 기준이다. 깊이를 기록하지 않는 투명 유리, 파티클, 안개는 가림으로 취급하지 않는다.
- 대상마다 렌더 패스가 추가된다. 메시 검색 캐시 및 합성 영역 제한을 적용했으나, 실제 대규모 씬의 GPU 성능 수치는 측정하지 않았다.
- 거리 제한은 추가하지 않았다. 카메라 원근 클리핑 범위를 따른다.
## 2026-09-18 대상 제한 (S15P21B202-633)

- 방망이(MeteorBat), 용접 건(PlasmaWeldingTool), PartSpec의 핵심 파츠 Part_1~Part_4만 노란색 가림 테두리 대상으로 수집한다.
- 고정못은 파츠의 자식 메시까지 제외한다. 일반 Grabbable/InteractableBase인 식생·환경·소품도 대상에서 제외한다.
- 원격 도구의 비활성 스크립트, 휴대 도구 제외, 로컬 카메라·가림·레이어·시야 조건은 유지한다. 추가 Inspector 설정은 없다.
- Unity 6000.5.4f1 자동 검사: OccludedInteractionTests 및 OccludedInteractionRenderTests 총 23개 통과, 실패/건너뜀 0. 핵심 파츠 4개와 두 도구, 고정못 및 일반 잡기 오브젝트 제외, 실제 Stage1 씬 대상, GPU 픽셀 및 Play Mode 렌더링을 검사했다.
- 첫 실행은 씬 검사에서 파츠의 첫 메시로 자식 고정못을 선택해 1개 실패했다. 본체 메시를 선택하도록 테스트를 수정한 후 전체 재실행이 통과했다.
- 로컬 증거: artifacts/outline-633/results-final.xml, unity-final.log. 실제 전체 게임 플레이와 다중 클라이언트 실행은 수행하지 않았다.
