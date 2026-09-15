# 참조 자료와 확인 범위

모든 경로는 저장소 루트 기준이다.

## 실제 씬·모델 참조

- `METAGAME/Assets/Scenes/Stage1_Astra.unity`: 오브젝트 이름과 Transform을 읽었다.
- `METAGAME/Assets/_Project/Art/Buildings/Hangar/PF_AstraHangar.prefab`: 씬에서 GUID `08676d99c293ca544ab1f94253bd5526`로 참조. FBX GUID `1db3c6b7f5f30354abe6bb2927f2942e`를 따라 `AstraHangar.fbx`와 연결 확인.
- `METAGAME/Assets/_Project/Art/Buildings/Hangar/AstraHangar.fbm/AstraHangar_BaseColor.png`: 직접 열어 펄 화이트·라벤더·티타늄·청록·황색 팔레트 확인. 첫 다섯 시트의 생성 참조로 제공.
- `METAGAME/Assets/_Project/Art/Buildings/Hangar/M_AstraHangar.mat`: 재질 설정 확인.
- `METAGAME/Assets/_Project/Models/AssemblyParts/part3-capsule-hangar/tripo_convert_8fb627ae-5d06-4519-b721-76d6f66cf488.fbx`: 기존 캡슐 플랫폼 FBX. `PF_Stage1_CapsuleHangar.prefab`의 GUID `773837a9ac02941438a61498362c472d` 참조 확인.
- 같은 FBX의 `.fbm/part3-capsule-hangar_basecolor.JPEG`: 직접 열어 흰 프레임·보라색 면·청록 매입등과 패널 이음새 확인.
- `tools/blender/build_astra_hangar.py`: 형상 구성과 재질 팔레트 참고.
- `METAGAME/Assets/_Project/Scripts/Editor/Stage1Buildings.cs`: 동향 건물, 경사로 연결점, 통로 및 랜딩 규격, 연석 배치 의도 확인.

## 시각 레퍼런스

- `artifacts/stage1-building-integration/hangar-in-scene.png`: 건물과 외부 플랫폼, 보라 식생, 둥근 바위의 관계. 모든 생성 시트에 스타일 레퍼런스로 제공.
- `docs/assets/previews/stage1-assembly-platforms/CapsuleHangar-scene.png`: 이전 플랫폼 배치와 외부 모델의 색상 관계. 현재 씬의 실시간 스크린샷으로 간주하지 않음.

## 제작·검토 기록

- 내장 `image_gen` 사용. 오브젝트별 독립 호출로 13개 PNG 생성.
- H06 생성 시 FBM 이미지 전달 중 읽기 오류가 발생해, 같은 도구에 기존 씬 렌더와 명시적 팔레트를 제공해 재시도했다. H06~13은 씬 렌더 한 장을 직접 이미지 참조로 사용.
- 생성 결과를 모두 시각 확인: 제목, 6개 패널, 대상 물체, 통일된 색감 확인.
- 일부 직교 뷰에 투시가 남고 세부 손상 위치·패널 개수가 다르다. 제작자는 README의 규격·방향을 우선해 정합해야 한다. H01·H05·H09는 그림의 길이/폭 인상과 실제 치수의 차이가 특히 크다.
- 통로 상면·건물 문턱·충돌체·실제 동선은 이 작업에서 변경하거나 플레이 검증하지 않았다.
- 원형 픽업장, Hangar 본체, 자연물은 기존 자산 재사용으로 분류했다. 추가 제안 2종은 확정 배치가 아닌 미술 보강안이다.

