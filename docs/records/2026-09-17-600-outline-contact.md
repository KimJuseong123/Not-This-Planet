# 600: 고정 파츠 접촉 후 outline 오류 조사

## 작업 기준

- 브랜치: `S15P21B202-600-qa`, 원격 동명 브랜치의 `56794cd0`에서 워크트리 생성.
- 작업 폴더: `.local-worktrees/600`, Unity 프로젝트: 그 아래 `METAGAME`.
- Unity 6000.5.4f1, URP 17.5.0, Mirror. 씬·프리팹·네트워크 프로토콜 변경 없음.
- 사용자 증상: 멀티에서 하단 파츠를 발사대에 고정한 뒤 다른 파츠를 접촉시키면 렉과 outline 관련 오류 발생. 당시 전체 오류·호스트/참가자 구분은 확보하지 못함.

## 재현된 원인

`PartOutlineRendererFeature.DrawMask`는 파츠마다 `RasterCommandBuffer.SetGlobalFloat`를 호출한다. 그러나 해당 RenderGraph 패스가 전역 상태 변경을 선언하지 않았다. 실제 URP의 명령 검증을 사용한 회귀 테스트에서 다음 예외를 로컬/원격 상태 모두 재현했다.

```text
InvalidOperationException: Part Outline Mask: Modifying global state from this command buffer is not allowed. Please ensure your render graph pass allows modifying global state.
BaseCommandBuffer.ThrowIfGlobalStateNotAllowed
RasterCommandBuffer.SetGlobalFloat
PartOutlineRendererFeature.PartOutlinePass.DrawMask
```

테두리가 필요한 동안 카메라 렌더링마다 같은 호출을 하므로 반복 예외가 가능하다. 그리기가 로컬이라는 것과 게임 진행에 영향을 주지 않는다는 것은 다르다. 렌더링과 게임/네트워크 업데이트가 같은 Unity 프로세스에서 실행되므로 반복 예외·로그가 화면 멈춤이나 멀티 지연처럼 보일 수 있다. 실제 사용자 세션의 FPS, RTT, 프로파일러 측정은 없으므로 네트워크 지연 또는 체감 성능 개선 수치를 주장하지 않는다.

## 용접 입력 없이 발생할 수 있는 이유

`WorldSync`는 변경이 없어도 1초 간격으로 기존 비드 스냅샷을 보낸다. 참가자의 `RpcApplyWeld → WeldBead.RestoreChains → RefreshContact`는 현재 로컬 위치에서 접촉을 다시 계산한다. 기존 비드 근처에 새 파츠를 옮기고 동일 스냅샷을 복원하면 새 용접 입력 없이 접합과 테두리 조건이 생기는 것을 테스트로 확인했다.

반대로 비드가 전혀 없는 두 파츠를 접촉시킨 경우에는, 하단이 도킹되어 있어도 로컬/원격 모두 테두리가 활성화되지 않았다. 따라서 위 경로는 보고된 상황을 설명할 수 있는 재현 사례이며, 당시 사용자 세션의 정확한 트리거라고 확정할 수는 없다. 비드 없는 신규 세션에서도 재발하면 최초 오류 스택과 양쪽 로그가 필요하다.

## 수정

1. 마스크 패스에 `AllowGlobalStateModification(true)`를 선언하여 실제 전역값 쓰기를 RenderGraph에 알렸다. 검사 비활성화나 예외 무시는 하지 않았다.
2. 실제 GPU 검증에서 별도의 표시 결함을 발견했다. 마스크 셰이더의 `Properties`에 있는 `_OutlineGroup = 0`이 전역 그룹값보다 우선되어 테두리가 나오지 않았다. 해당 재질 속성을 제거하고 전역 이름을 `_MetagamePartOutlineGroup`으로 바꿨다. 이름 변경은 기존 `.mat`에 남은 `_OutlineGroup: 0`과의 충돌도 피한다. 재질 에셋을 재저장할 필요가 없다.
3. 로컬/원격 도킹 상태의 비드 없는 접촉, 비드 스냅샷 복원에 따른 활성화, 실제 RenderGraph 패스의 마스크 명령 검증을 회귀 테스트에 추가했다. 테스트 어셈블리에 RenderGraph를 제공하는 Core Runtime 참조를 추가했다.

## 검증

- 격리 프로젝트: 워크트리의 `.local-validation/project`. 이 브랜치의 소스/셰이더와 원본 GUID를 사용하며 패키지는 로컬 캐시의 동일 버전이다.
- 수정 전 회귀 테스트: 5개 중 3개 통과, 2개가 위의 `Part Outline Mask` 예외로 실패 (`baseline.xml`).
- RenderGraph 수정 후 관련 70개 테스트 통과 (`after.xml`): outline, docking, bead joining/restoration, assembly link state.
- GPU 비교: RenderGraph 수정만으로는 예외 0개지만 테두리 유무 이미지 차이 0픽셀. 셰이더 속성 충돌을 제거하면 320×240 이미지에서 2,144픽셀 차이, 연속 60회 렌더 요청에서 오류 0개 (`render-global.log`).
- 최종 소스도 70/70 통과 (`final.xml`). 원래 `M_PartOutlineMask.mat`와 `M_PartOutline.mat`를 사용한 GPU 검증도 2,144픽셀 차이와 60회 연속 렌더 요청 오류 0개를 확인했다 (`render-final.log`). `with-outline.png`에서 두 파츠의 합쳐진 청록색 테두리를 시각적으로 확인했다. 최종 셰이더와 검증에 사용한 셰이더의 SHA-256도 동일하다.
- 두 프로세스의 실제 Mirror 접속, 실제 Stage1 장면, 네트워크 성능 측정은 실행하지 않았다. 원격 상태 플래그와 실제 복원 메서드를 이용한 격리 재현이다.

## 실제 멀티 재검증 순서

1. 같은 수정 버전의 호스트와 참가자로 신규 세션에 진입한다. 오류 수를 각각 기록한다.
2. 비드가 전혀 없는 하단 파츠를 도킹하고 다른 파츠를 접촉/분리한다. 용접 테두리가 생기지 않고 outline 오류가 없어야 한다.
3. 비드가 남은 고정 파츠에 다른 파츠를 접촉시킨다. 새 용접 입력 없이도 스냅샷 복원이 발생하는 1초 이상 관찰한다.
4. 실제 용접으로 접합한 파츠의 합쳐진 테두리를 확인하고 60초 유지한다. 양쪽에서 최초 오류·반복 오류와 프레임 시간을 확인한다.
5. 비드를 절단하여 접합을 해제하고 다시 용접한다. 테두리 해제/재표시 및 파츠 물리가 유지되어야 한다.

워크트리 생성 시의 LFS 폰트 경고와 라이선스 연결 초기 실패는 별개였다. 정상 LFS 필터로 최종 변경 범위를 확인했으며 폰트·영상·아이콘을 수정하지 않았다.
