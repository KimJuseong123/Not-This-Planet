"""공유·제출용 문서를 만든다 — `docs/*.md` → md 사본 + HTML + PDF (S15P21B202-323).

왜 있나: 공유 폴더(`Desktop/METAGAME-share/`)의 HTML·PDF 는 md 에서 만든 생성물인데, 처음 만든
스크립트가 남아 있지 않아 09-07 에 다시 만들 수 없었다. 생성물은 리포에 넣지 않고(README §"이 문서들을
고칠 때") 만드는 법만 여기 둔다.

쓰는 법 (리포 루트에서):
    python scripts/docs/render_share.py docs/workflow.md docs/e2e-checklist.md --out "C:/Users/<나>/Desktop/METAGAME-share"

의존: `pip install --user markdown` (표준 라이브러리 아님 — CI 가 아니라 사람 PC 에서만 돈다).
PDF 는 Edge 가 있으면 헤드리스로 찍고, 없으면 HTML 까지만 만든다.

문서 안의 상대 링크(`./x.md`)는 공유본에서 깨지므로 GitLab 의 develop 파일 주소로 바꾼다.
"""
import argparse
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path

try:
    import markdown
except ImportError:  # pragma: no cover
    sys.exit("python-markdown 이 없다: python -m pip install --user markdown")

GITLAB_DOCS = "https://lab.ssafy.com/s15-metaverse-game-sub1/S15P21B202/-/blob/develop/docs/"
EDGE_CANDIDATES = [
    r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
]

# 08-28 공유본의 스타일 그대로(표·인용·코드·인쇄). 흐름도 카드 CSS 는 표로 대체했으므로 뺐다.
CSS = """
:root{--fg:#1f2328;--muted:#59636e;--line:#d0d7de;--bg:#fff;--soft:#f6f8fa;--accent:#0969da}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--fg);font:15px/1.65 'Pretendard','Malgun Gothic','Apple SD Gothic Neo',system-ui,sans-serif}
main{max-width:1040px;margin:0 auto;padding:40px 32px 80px}
h1{font-size:28px;border-bottom:2px solid var(--line);padding-bottom:10px;margin:0 0 6px}
h2{font-size:21px;margin:44px 0 12px;padding-bottom:6px;border-bottom:1px solid var(--line)}
h3{font-size:17px;margin:28px 0 8px}
blockquote{margin:12px 0;padding:10px 16px;border-left:4px solid var(--accent);background:var(--soft);color:var(--muted)}
table{border-collapse:collapse;width:100%;margin:12px 0 18px;font-size:14px}
th,td{border:1px solid var(--line);padding:7px 10px;vertical-align:top;text-align:left}
th{background:var(--soft);font-weight:700}tr:nth-child(even) td{background:#fbfcfd}
code{font:13px/1.5 Consolas,'D2Coding','Cascadia Mono',monospace;background:var(--soft);padding:1px 5px;border-radius:4px}
pre{background:var(--soft);padding:12px 14px;border-radius:8px;overflow-x:auto}pre code{background:none;padding:0}
a{color:var(--accent);text-decoration:none}a:hover{text-decoration:underline}
.meta{color:var(--muted);font-size:13px;margin-bottom:28px}
@media print{main{padding:0}h2{page-break-after:avoid}tr{page-break-inside:avoid}}
"""


def relink(md: str) -> str:
    """`](./x.md#a)` → GitLab 주소. 이미지·절대 주소는 그대로."""
    return re.sub(r"\]\(\./([^)#\s]+\.md)(#[^)\s]*)?\)", lambda m: f"]({GITLAB_DOCS}{m.group(1)}{m.group(2) or ''})", md)


def to_html(md_text: str, title: str, stamp: str) -> str:
    body = markdown.markdown(relink(md_text), extensions=["tables", "fenced_code", "sane_lists"])
    return ("<!doctype html><html lang=\"ko\"><head><meta charset=\"utf-8\">"
            "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">"
            f"<title>{title}</title><style>{CSS}</style></head><body><main>"
            f"<div class=\"meta\">S15P21B202 · METAGAME · {stamp}</div>{body}</main></body></html>")


def find_edge() -> str | None:
    for p in EDGE_CANDIDATES:
        if os.path.exists(p):
            return p
    return shutil.which("msedge")


def to_pdf(html_path: Path, pdf_path: Path, edge: str) -> bool:
    """Edge 헤드리스로 PDF. 세 함정을 피한다(2026-09-07 실측):
    - Windows 의 `msedge.exe` 는 브라우저를 띄운 뒤 **바로 rc 0 으로 돌아온다** — PDF 는 몇 초 뒤 자식
      프로세스가 쓴다. 반환값을 믿으면 "실패" 로 보이고, 옛 파일이 있으면 "성공" 으로 보인다
      → 찍기 전에 옛 PDF 를 지우고, 파일이 생겨 크기가 멈출 때까지 기다린다(최대 90초)
    - Edge 가 이미 켜져 있으면 실행 중인 인스턴스에 넘기고 끝날 수 있다 → 임시 프로필로 독립 인스턴스
    - 임시 프로필은 브라우저가 다 쓴 뒤에 지워야 한다 → 기다린 뒤 정리"""
    import tempfile
    import time
    if pdf_path.exists():
        pdf_path.unlink()
    prof = tempfile.mkdtemp(prefix="edge-pdf-")
    cmd = [edge, "--headless=new", "--disable-gpu", "--no-first-run", "--no-pdf-header-footer",
           f"--user-data-dir={prof}", f"--print-to-pdf={pdf_path}", html_path.resolve().as_uri()]
    subprocess.run(cmd, capture_output=True, text=True, timeout=120)
    deadline, last = time.time() + 90, -1
    while time.time() < deadline:
        time.sleep(1)
        size = pdf_path.stat().st_size if pdf_path.exists() else 0
        if size > 1000 and size == last:      # 생겼고 1초 동안 크기가 그대로 = 다 썼다
            break
        last = size
    ok = pdf_path.exists() and pdf_path.stat().st_size > 1000
    shutil.rmtree(prof, ignore_errors=True)
    if not ok:
        print(f"   pdf 실패: 90초 안에 {pdf_path.name} 이 생기지 않았다")
    return ok


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("files", nargs="+", help="docs/*.md")
    ap.add_argument("--out", required=True, help="공유 폴더")
    ap.add_argument("--stamp", default=None, help="머리 날짜 (기본: 오늘)")
    a = ap.parse_args()
    out = Path(a.out)
    out.mkdir(parents=True, exist_ok=True)
    import datetime
    stamp = a.stamp or datetime.date.today().isoformat()
    edge = find_edge()
    for f in a.files:
        src = Path(f)
        text = src.read_text(encoding="utf-8")
        title = next((ln.lstrip("# ").strip() for ln in text.splitlines() if ln.startswith("# ")), src.stem)
        (out / src.name).write_text(text, encoding="utf-8", newline="\n")
        html = out / (src.stem + ".html")
        html.write_text(to_html(text, title, stamp), encoding="utf-8")
        line = f"{src.name} → md · html"
        if edge:
            pdf = out / (src.stem + ".pdf")
            line += " · pdf" if to_pdf(html, pdf, edge) else " · (pdf 실패)"
        else:
            line += " · (Edge 없음 — pdf 생략)"
        print(line)
    return 0


if __name__ == "__main__":
    sys.exit(main())
