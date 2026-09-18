# Stage1 Astra — Magnetic Storm

보라색 볼류메트릭 연기와 청록색 자기력선으로 표현한 이동형 자기장 폭풍 컨셉.

## 결과물

| 파일 | 용도 |
| --- | --- |
| magnetic-storm-front.png | 정면 / 진행 방향에서 폭풍을 바라봄 |
| magnetic-storm-left.png | 왼쪽 / 화면 왼쪽으로 진행 |
| magnetic-storm-right.png | 오른쪽 / 화면 오른쪽으로 진행 |
| magnetic-storm-back.png | 후면 / 얇은 연기와 자기력선의 후류 |
| magnetic-storm-top.png | 위 / 화면 위쪽으로 진행 |
| magnetic-storm-loop.gif | 정면 고정 시점, 960×400, 20fps, 120프레임, 6초 무한 반복 |

각 방향은 분할 패널이 없는 별도의 단일 PNG다. 원본 해상도는 약 1944×809이며 각 파일별 정확한 크기는 검증 자료를 참고한다.

## 기존 에셋과의 연결

- Stage1_Astra 씬, 기존 FBX 에셋 목록 및 빌딩 제작 코드, 해당 모델의 저장된 렌더/씬 프리뷰와 FBM 색상 텍스처를 참고했다. Unity 씬을 새로 실행하거나 FBX 전체를 다시 렌더링한 것은 아니다.
- 참고: `METAGAME/Assets/_Project/Art/Buildings/Hangar/AstraHangar.fbx`, `AstraHangar.fbm/AstraHangar_BaseColor.png`: 라벤더 패널, 회청색 금속, 청록색 조명.
- 참고: `METAGAME/Assets/_Project/Models/Environment/Stage1/alien_mushroom_tree.fbx`, `alien_mushroom_tree.fbm/alien_mushroom_tree_basecolor.jpg`: 부드러운 유기적 덩어리, 보라색 식생과 청록색 포인트.
- 참고 프리뷰: `artifacts/stage1-building-integration/shelter-in-scene.png`, `artifacts/stage1-hangar/preview-concept.png`.
- 기존 `Stage1AtmosphereProfile.asset`의 halfExtents (12, 2.5, 5)를 기준으로 전체 폭 24m × 높이 5m × 깊이 10m를 디자인 목표로 삼았다. 이 수치는 PNG에서 측정한 정확한 투영 치수가 아니다.
- `Stage1DustStormHazard.ApplyWind`는 폭풍 진행 방향으로 힘을 주므로, 앞쪽이 조밀하고 뒤쪽에 후류가 남는 형태로 잡았다.

## 제작 방향

연기는 보라색 중간 밀도 덩어리, 어두운 내부, 반투명 외곽으로 나눈다. 청록색 자기력선은 연기 사이를 휘감으며 흐르고, 소량의 부유 파편으로 물체를 미는 힘을 표현한다. 세로로 긴 토네이도보다 낮고 넓은 이동형 폭풍에 초점을 맞췄다.

좌표 약속: 위 +Y, 폭 X, 진행 +Z. 정면 +Z, 후면 -Z, 왼쪽 -X, 오른쪽 +X. 각 이미지는 같은 마스터를 참고한 생성형 컨셉이므로, 정확히 일치하는 3D 정투영 도면은 아니다.

## GIF와 루프

정면 ImageGen 컨셉을 바탕으로 주기적인 연기 변형, 자기력선을 따라 이동하는 발광, 닫힌 궤도를 도는 입자를 합성한 2D 모션 컨셉이다. 실제 3D 유체 시뮬레이션이나 Unity 실시간 VFX는 아니다.

모든 움직임은 6초 주기로 돌아온다. t=0과 t=6초는 같고, 실제 GIF에는 0~5.95초의 120프레임만 넣어 중복 끝 프레임으로 인한 멈춤을 피한다. GIF 반복 횟수는 0(무한 반복)이다. 검증 수치는 `loop-validation.json`에 기록한다.

## 재현

방향별 PNG는 내장 ImageGen 도구로 제작했다. 최종 프롬프트는 `prompts.md`에 있다.

GIF 재생성: Node.js와 Sharp를 준비하고 `node make-loop.cjs <이 폴더의 절대경로> [sharp 모듈의 절대경로]`를 실행한다.

이 패키지는 컨셉 자료만 추가하며 씬, FBX, 재질, 게임 동작은 변경하지 않았다.

