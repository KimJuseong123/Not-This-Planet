"""repo-hygiene 새 검사 오프라인 테스트 (S15P21B202-189).

실행: python scripts/ci/tests/test_hygiene_guards.py

경우:
  1. 경로 길이 상한 — 2026-08-31 파이프라인 10건을 죽인 189자 경로를 그대로 재현
  2. 인증키·비밀키 금지 (.pem / id_rsa) · 개인 작업 메모 금지 (*.local.*)
  3. 에셋 위치 위반을 최상위 폴더 단위로 묶기 (Mirror 임포트 수백 줄 노이즈)
  4. 서드파티 패키지에서 새로 들어온 씬·프리팹은 선언 검사에서 제외 (수정한 것은 검사 유지)
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
os.environ.setdefault("CI_PROJECT_ID", "1")

import repo_hygiene as H  # noqa: E402

# 2026-08-31 실측: MR !94 가 넣은 경로. 리포 기준 189자 + 러너 빌드 경로 71자 = 260 ≥ MAX_PATH
REAL_189 = ("METAGAME/Assets/_Project/Models/Environment/Stage1/Renewal/Nature/alien_rock_round_cracked/"
            "tripo_convert_0e01cc6d-83b2-4384-ac95-a2345b791727.fbm/alien_rock_round_cracked_metallic.JPEG.meta")


def test_too_long():
    assert len(REAL_189) == 189, f"픽스처 경로가 {len(REAL_189)}자 — 실측값 189 와 다르다"
    short = "METAGAME/Assets/_Project/Scripts/Runtime/Core/Tuning.cs"

    bad = H.too_long([short, REAL_189])
    assert [p for p, _ in bad] == [REAL_189], bad
    assert bad[0][1] == 189

    # 10-52 에셋 정리로 174자가 된 뒤에는 통과해야 한다 (여유 6자)
    after_cleanup = REAL_189.replace("/Renewal/Nature/", "/")
    assert len(after_cleanup) == 174, len(after_cleanup)
    assert H.too_long([after_cleanup]) == [], "정리 후 경로가 막히면 안 된다"

    # 긴 것부터 정렬
    longer = REAL_189 + "xx"
    assert [ln for _, ln in H.too_long([REAL_189, longer])] == [191, 189]
    print("  경로 길이: 189자 실측 경로 차단 · 174자 통과 · 긴 것부터")


def test_key_files_forbidden():
    def why(path):
        return next((w for rx, w in H.FORBIDDEN if rx.search(path)), None)

    assert why("J15B202T.pem") == "인증키·비밀키"
    assert why("infra/keys/deploy.KEY") == "인증키·비밀키"
    assert why("secrets/app.p12") == "인증키·비밀키"
    assert why(".ssh/id_rsa") == "SSH 개인키"
    assert why("id_ed25519.pub") == "SSH 개인키"
    # 흔한 정상 파일이 걸리면 안 된다
    for ok in ("docs/records/S15P21B202-169-ec2-setup.md", "scripts/ci/ci_common.py",
               "METAGAME/Assets/_Project/Scripts/Runtime/Network/NetworkSession.cs",
               "backend/src/main/resources/application.yml", "monkey.keystore.md"):
        assert why(ok) is None, ok
    # 개인 작업 메모 (S15P21B202-309) — git add -A 에 딸려 들어가는 것을 막는다
    assert why("TEAM-SHARE.local.md") == "개인 작업 메모(.local.*)"
    assert why("ARCHITECTURE-REVIEW.local.md") == "개인 작업 메모(.local.*)"
    assert why("notes.local.json") == "개인 작업 메모(.local.*)"
    assert why("docs/records/S15P21B202-309-ci-signal-holes.md") is None, "보통 문서는 통과"
    assert why("METAGAME/Assets/_Project/Scripts/LocalPlayer.cs") is None, "이름에 local 이 들어간 코드는 통과"
    print("  인증키 금지: .pem/.key/.p12/id_rsa 차단 · 개인 메모(.local.*) 차단 · 정상 파일 오탐 없음")


def test_location_problems_deduped():
    base_top = {"Scenes", "Scenes.meta", "Settings", "Settings.meta", "TutorialInfo", "_Project", "_Project.meta"}
    A = H.ASSETS
    # Mirror 임포트 재현: 새 최상위 폴더 하나에 파일 수백 개
    mirror = [f"{A}Mirror/Core/File{i}.cs" for i in range(300)]
    mirror += [f"{A}ScriptTemplates/T{i}.cs.txt" for i in range(4)]
    msgs = H.location_problems(mirror, base_top)
    assert len(msgs) == 2, f"폴더 2개니 메시지 2줄이어야 한다: {len(msgs)}줄"
    assert any("'Mirror'" in m and "304" not in m and "300" in m for m in msgs), msgs
    assert any("'ScriptTemplates'" in m and "4" in m for m in msgs), msgs

    # 기존 폴더·_Project 아래는 통과
    assert H.location_problems([f"{A}_Project/Scripts/X.cs", f"{A}Scenes/Main.unity"], base_top) == []

    # Assets/ 바로 아래 파일은 따로 한 줄로 묶는다
    direct = H.location_problems([f"{A}Mirror.meta", f"{A}foo.txt", f"{A}bar.txt"], base_top)
    assert len(direct) == 1 and "3개" in direct[0], direct
    # 기존 폴더의 .meta 는 위반이 아니다
    assert H.location_problems([f"{A}Scenes.meta"], base_top) == []
    print("  위치 위반: 파일 304개 → 2줄로 묶임 · 기존 폴더/_Project 통과 · 직속 파일 1줄")


def test_scene_changes_thirdparty():
    A = H.ASSETS
    changes = [
        ("M", f"{A}Scenes/Lobby.unity"),                                  # 내가 고친 씬 → 검사 대상
        ("A", f"{A}_Project/Prefabs/PF_New.prefab"),                      # 내가 추가한 프리팹 → 검사 대상
        ("A", f"{A}_Project/ThirdParty/Mirror/BandwidthGraph.prefab"),     # 서드파티 임포트 → 제외
        ("A", f"{A}_Project/ThirdParty/Mirror/Examples/Room.unity"),       # 서드파티 임포트 → 제외
        ("M", f"{A}_Project/ThirdParty/Mirror/Edited.prefab"),             # 서드파티지만 수정 → 검사 대상
        ("A", f"{A}_Project/Scripts/Runtime/Network/MirrorLaunchArgs.cs"),  # 씬·프리팹 아님
    ]
    scene, imported = H.scene_changes(changes)
    assert scene == [f"{A}Scenes/Lobby.unity", f"{A}_Project/Prefabs/PF_New.prefab",
                     f"{A}_Project/ThirdParty/Mirror/Edited.prefab"], scene
    assert imported == [f"{A}_Project/ThirdParty/Mirror/BandwidthGraph.prefab",
                        f"{A}_Project/ThirdParty/Mirror/Examples/Room.unity"], imported
    # 서드파티만 들어온 MR 은 선언 검사 대상이 0 → '없음' 선언이 정당해진다
    only_tp = [("A", f"{A}_Project/ThirdParty/Mirror/A.prefab"), ("A", f"{A}_Project/ThirdParty/Mirror/B.prefab")]
    assert H.scene_changes(only_tp)[0] == []
    print("  서드파티 신규 씬·프리팹 제외 · 수정한 것은 검사 유지")


def main() -> int:
    test_too_long()
    test_key_files_forbidden()
    test_location_problems_deduped()
    test_scene_changes_thirdparty()
    print("TEST_HYGIENE_GUARDS OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
