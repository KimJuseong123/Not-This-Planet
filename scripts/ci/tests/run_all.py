"""scripts/ci 오프라인 테스트 전체 실행 (S15P21B202-103).

실행: python scripts/ci/tests/run_all.py
각 테스트는 ci_common.http 를 가짜로 바꾸므로 서로 간섭하지 않도록 **별도 프로세스**로 돌린다.
네트워크·토큰·러너가 필요 없어 로컬에서도 그대로 돌아간다.
"""
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))


def main() -> int:
    files = sorted(f for f in os.listdir(HERE) if f.startswith("test_") and f.endswith(".py"))
    if not files:
        print("테스트 파일이 없습니다"); return 1
    env = dict(os.environ, PYTHONUTF8="1", PYTHONIOENCODING="utf-8")
    failed = []
    for f in files:
        r = subprocess.run([sys.executable, os.path.join(HERE, f)], capture_output=True, text=True,
                           encoding="utf-8", errors="replace", env=env)
        if r.returncode == 0:
            print(f"  ✅ {f}")
        else:
            failed.append(f)
            print(f"  ❌ {f} — 실패")
            for line in (r.stdout or "").rstrip().split("\n")[-6:]:
                print(f"       {line}")
            for line in (r.stderr or "").rstrip().split("\n")[-6:]:
                print(f"       {line}")
    print(f"\n== 오프라인 테스트: 통과 {len(files) - len(failed)} · 실패 {len(failed)} ==")
    if failed:
        print("   실패: " + ", ".join(failed))
        print("   ❌ CI 스크립트 회귀 — 고친 뒤 다시 push 하세요")
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
