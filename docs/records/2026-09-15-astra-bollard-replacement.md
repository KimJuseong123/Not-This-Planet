# Stage1_Astra 안전봉 FBX 교체

## 변경

- `Assets/_Project/Models/Bollard/safety-bollard/`의 FBX와 FBM 텍스처를 사용한다.
- `PF_Stage1_Bollard`를 만들고 기존 `Lamp · foot/body/light/cap` 42세트를 대체한다.
- 기존 168개 조각의 Renderer만 숨겨 원래 콜라이더와 직렬화 참조를 보존한다.
- 원본 FBX는 일체형 메시와 하나의 재질 슬롯을 유지한다. 원본 파일을 수정하지 않는다.
- 원본 BaseColor의 무채색 표시등 영역을 마스크로 구분한다. 아이보리 외장과 보라색 마감은 색상 변경 대상에서 제외한다.
- `NotThisPlanet/Bollard Signal`은 URP Lit의 표면 처리와 상수 버퍼를 사용한다. 신호 색상은 마스크 영역의 Albedo와 Emission에만 적용한다.
- Metallic R / (1 − Roughness) A 텍스처를 생성하고 Normal 텍스처를 Normal Map으로 가져온다.

## 색상 연결

| 기존 구역/상태 | 새 안전봉 신호 |
| --- | --- |
| 주변 경로 / Ion cyan | 기존 청록색 |
| 주거지·격납고 / Habitat amber | 기존 주황색 |
| 견인 장치 / Route lime | 기존 라임색 |
| 모듈 안내 / Signal orchid | 기존 보라색 |
| 중앙 조립 0~3단계 | Route amber → cyan → orchid → lime |
| 중앙 조립 완료 | 기존 Assembly ready 색상 |

`AssemblyGuidanceLight`의 바닥등 재질과 새 안전봉 재질 배열은 분리한다. 원래 단색 바닥 재질을 FBX 전체에 적용해 텍스처가 사라지는 문제를 피한다. 기존 씬에서 새 필드가 비어 있어도 바닥등 동작은 유지된다.

## 재사용

Unity 메뉴: `Tools > Not This Planet > Safety Bollards`.

1. Prepare Assets: PBR 텍스처·마스크·기본 프리팹 준비.
2. Replace Astra Bollards: 저장된 Stage1_Astra의 기존 봉에 새 외형 추가. 중복 설치는 거부한다.
3. Validate: 개수·바운드·색상·충돌체·중앙 색상 연결 검사.
4. Align Feet to Current Surfaces: 수평 위치와 크기를 유지하며 현재 지지면에 맞춰 외형과 기존 충돌체를 함께 높이 조정.

씬 수정은 Undo 그룹으로 묶는다. 최초 교체 전 사본과 검사 결과는 `tmp/bollard-integration/`에 보관한다.

## 검증

- 최초 교체 검사: 42개 모델, 168개 원본 충돌체 유지, 중앙 동적 안전봉 4개, 상태별 색상 일치 통과.
- 기존 직렬화 오브젝트 삭제 0개. 최초 교체 시 기존 Renderer 168개의 enabled 값과 안내등 설정만 변경됨.
- Unity 렌더링으로 주황색·라임색 신호에 외장 텍스처가 유지됨을 확인.
- 회귀 테스트: `AssemblyGuidanceBollardTests` — 조립 4단계·완료·이전 단계 복귀와 기존 씬 호환성.
- 최종 테스트 실행 및 지지면 정렬 결과는 작업 완료 시 갱신한다.
