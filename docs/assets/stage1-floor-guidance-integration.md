# Stage1_Astra 바닥 유도등 교체

## 배치와 색상

기존 `Runway color` 94개와 하우징 94개, `light strip` 32개, `Pickup / departure marker` 12개를 제거하고 `PF_Astra_FloorLight` 138개로 교체한다. 씬의 새 인스턴스 이름은 `Astra Floor Guidance - …`이다. 기존 등기구의 위치, 긴 축 방향, 바닥 점유 크기를 기준으로 맞추고, 가까운 바닥 표면에 높이와 기울기를 맞춘다. 보행을 방해하지 않도록 인스턴스의 콜라이더는 비활성화한다.

기존 경로 재질의 `_BaseColor`를 그대로 복사한다. Route amber/cyan/orchid/lime, Ion cyan, Habitat amber의 여섯 색상과 조립 완료 색상을 각각 독립된 렌즈 재질로 유지한다. 세로형 볼라드와 건물 조명은 별도 오브젝트이므로 이번 바닥등 교체 대상에 포함하지 않는다.

## 발광과 후처리

- 재질: `Assets/_Project/Materials/FloorGuidance/`
- 본체는 원본 재질, 렌즈는 전용 색상 재질을 사용한다. 흰색 Emission 마스크로 렌즈 영역만 빛난다.
- Emission은 원래 색을 선형 색으로 변환한 후 가장 밝은 채널을 2.4로 정규화한다. 밝은 중심에서도 색 차이를 유지하도록 렌즈의 흰 정반사와 환경 반사를 줄였다.
- Unity의 재임포트 과정에서도 `_EMISSION`이 유지되도록 `BakedEmissive` 플래그를 설정했다. 라이트맵을 새로 베이크한 것은 아니다.
- 씬 전용 `Stage1_FloorGuidanceVolume.asset`은 기존 볼륨을 복제해 색보정과 톤매핑을 보존한다. Bloom: intensity 0.8, threshold 0.9, scatter 0.85, 흰색 tint, 고품질 필터.
- 씬의 미리보기 카메라에서 HDR와 후처리를 활성화했다. 실제 플레이어 카메라는 기존 `AuthoredMapSettings.ConfigureCamera` 경로로 활성화된다.

Bloom은 화면의 밝은 부분에 작용하는 후처리이므로 씬의 다른 발광체에도 영향을 준다. 수십 개의 추가 실시간 Point Light를 배치하거나 프로젝트 공용 렌더링 설정을 변경하지 않는다.

## 조립 진행 연동

조립 구역의 8개 바닥등은 `AssemblyGuidanceLight`의 새 렌즈 배열에 연결한다. 단계 변경 및 조립 완료 시 두 번째 재질 슬롯만 교체하여 본체 텍스처가 덮어써지지 않도록 한다. 기존 링, 세로형 조명, 볼라드의 연동과 `MaterialForPart` 색상 원본은 유지한다.

## 검증 기록

`artifacts/floor-guidance-integration/`에 씬 재로드 검사, 직렬화 보존 감사, EditMode 결과, Play Mode 스모크 결과와 실제 Unity 렌더를 보관한다. 플레이어 빌드와 다중 접속 테스트는 이 작업의 검증 범위에 포함하지 않는다.

EditMode의 `AssemblyGuidanceBollardTests` 7개가 통과했다. 신규 검사는 네 조립 단계, 완료, 잘못된 첫 부품으로 돌아가는 상태를 연속 검증하며, 본체 재질과 발광 마스크 보존을 확인한다. Play Mode 검사에서 Unity Editor의 `SearchDatabase.EnumerateAll` 검색 인덱싱 예외가 기록되었다. 게임 스크립트가 아닌 에디터 검색 스택에서 발생했으며, 유도등·플레이어 카메라 검사는 통과했다.

작업 도구는 `tools/unity/Stage1FloorGuidance.cs`와 `publish_floor_guidance.py`이다. 기존 격리 검증 프로젝트에서 Unity API로 수정하고, 대상 오브젝트 및 관련 참조만 원본 씬에 반영한다. 원본 씬의 동시 변경 여부를 해시로 검사하며 관계없는 씬 블록은 보존한다.
