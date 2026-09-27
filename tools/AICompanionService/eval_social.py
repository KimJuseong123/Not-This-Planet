"""Local EXE evaluation with synthetic phrases. No downloads, API keys or recordings."""
import argparse
import json
from pathlib import Path
import secrets
import subprocess
import sys
import httpx
import psutil
from smoke_portable import wait_health

CASES = [
    ("춤춰봐", "dance", "default", False),
    ("인사해", "greet", "default", False),
    ("다들 이리 와서 인사해", "greet", "all", True),
    ("파랑아 내 앞으로 와서 춤춰", "dance", "blue", True),
    ("초록아 하트 해줘", "heart", "green", False),
    ("노랑아 따봉", "cheer", "yellow", False),
    ("둘이 나 따라와", "follow", "two", False),
    ("한 명만 못 뽑아줘", "pull_pin", "one", False),
    ("다들 모여", "gather", "all", False),
    ("그만 따라와", "cancel", "default", False),
    ("다들 춤추지 마", "cancel", "all", False),
    ("못 뽑지 마", "cancel", "default", False),
    ("나 따라와", "follow", "default", False),
    ("여기서 기다려", "wait", "default", False),
    ("용접해줘", "none", "default", False),
    ("두 명 이리 와서 하트 해줘", "heart", "two", True),
]

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("pack",type=Path)
    parser.add_argument("--output",type=Path,required=True)
    parser.add_argument("--source",action="store_true",help="Evaluate source before freezing the final EXE")
    parser.add_argument("--cases",type=Path,help="Independent JSON cases; never added to the model prompt")
    args=parser.parse_args()
    pack=args.pack.resolve(); parent=psutil.Process()
    token=secrets.token_hex(32)
    command=([sys.executable,str(Path(__file__).with_name("portable_worker.py")),"--ollama-exe",str(pack/"runtime/ntp-ai-worker/ollama/ollama.exe")]
             if args.source else [str(pack/"runtime/ntp-ai-worker/ntp-ai-worker.exe")])
    process=subprocess.Popen(command+[
        "--model-root",str(pack/"models"),"--parent-pid",str(parent.pid),"--parent-started",str(parent.create_time()),
        "--port","18775","--ollama-port","11449"],stdin=subprocess.PIPE,
        creationflags=getattr(subprocess,"CREATE_NO_WINDOW",0))
    process.stdin.write((token+"\n").encode()); process.stdin.close()
    results=[]
    cases=(json.loads(args.cases.read_text(encoding="utf-8")) if args.cases else
           [dict(category="development",text=t,expected=[a,r,p]) for t,a,r,p in CASES])
    try:
        with httpx.Client(base_url="http://127.0.0.1:18775",headers={"Authorization":"Bearer "+token},timeout=35,trust_env=False) as client:
            health=wait_health(client,process)
            assert health["state"]=="ready" and health["command_schema"]=="social-v1",health
            for case in cases:
                text=case["text"]
                action,recipients,approach=case["expected"]
                response=client.post("/api/companion-social/text",json={"text":text})
                data=response.json()
                passed=response.status_code==200 and (data.get("action"),data.get("recipients"),data.get("approach"))==(action,recipients,approach)
                raw=data.get("raw_decision",data)
                results.append(dict(category=case["category"],text=text,expected=[action,recipients,approach],actual=data,passed=passed,
                    raw_passed=response.status_code==200 and (raw.get("action"),raw.get("recipients"),raw.get("approach"))==(action,recipients,approach),
                    http_status=response.status_code))
                print(json.dumps(results[-1],ensure_ascii=False),flush=True)
            client.post("/api/shutdown")
            process.wait(timeout=15)
    finally:
        if process.poll() is None: process.kill(); process.wait(timeout=10)
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(results,ensure_ascii=False,indent=2),encoding="utf-8")
    print(f"Passed {sum(r['passed'] for r in results)}/{len(results)}")
    return 0 if all(r["passed"] for r in results) else 1

if __name__=="__main__": sys.exit(main())
