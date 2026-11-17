#!/usr/bin/env python
# Melvor Idle 2 桥 CLI —— AI 友好的紧凑输出
# 用法:
#   python melvor.py health
#   python melvor.py state
#   python melvor.py entities
#   python melvor.py detail <entityId>            # 组件+选项
#   python melvor.py options <entityId>           # 仅动作选项
#   python melvor.py select <entityId> <optionId>
#   python melvor.py start <entityId>
#   python melvor.py stop
#   python melvor.py shot [outfile.png] [width]
#   python melvor.py click <x> <y>                # 归一化坐标
#   python melvor.py log [n]
import json, sys, urllib.request, base64, os

BASE = "http://127.0.0.1:27500"

def req(path, body=None):
    data = json.dumps(body).encode() if body is not None else None
    r = urllib.request.Request(BASE + path, data=data, headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(r, timeout=30) as resp:
        return json.loads(resp.read())

def main():
    a = sys.argv[1:]
    if not a:
        print(__doc__); return
    cmd = a[0]
    if cmd == "health":
        print(json.dumps(req("/health"), ensure_ascii=False))
    elif cmd == "state":
        d = req("/state")
        print("==", d.get("now"), "==")
        for s in d.get("skills", []):
            print(f"  {s.get('name') or s.get('id')}: Lv{s.get('level')}")
        acts = d.get("activeActions", [])
        print("active:", ", ".join(f"{x.get('entityName')}[{x.get('description')}]" for x in acts) or "(none)")
    elif cmd == "entities":
        for e in req("/entities"):
            print(f"  {e.get('id')}  Lv{e.get('level') or '-'}")
    elif cmd == "detail":
        d = req(f"/entities?id={a[1]}&options=1")
        opts = d.get("options") or []
        print(f"{d.get('name')} ({d.get('id')})")
        print("components:", ", ".join((d.get("components") or {}).keys()))
        for o in opts:
            print(f"  option: {o.get('id')}  ({o.get('name')})")
    elif cmd == "options":
        d = req(f"/entities?id={a[1]}&options=1")
        for o in (d.get("options") or []):
            print(f"{o.get('id')}\t{o.get('name')}")
    elif cmd == "select":
        print(json.dumps(req("/select", {"entityId": a[1], "optionId": a[2]}), ensure_ascii=False))
    elif cmd == "start":
        print(json.dumps(req("/start", {"entityId": a[1]}), ensure_ascii=False))
    elif cmd == "stop":
        print(json.dumps(req("/stop"), ensure_ascii=False))
    elif cmd == "shot":
        w = a[2] if len(a) > 2 else "900"
        d = req(f"/screenshot?width={w}")
        out = a[1] if len(a) > 1 else os.path.join(os.environ.get("TEMP", "/tmp"), "melvor_shot.png")
        open(out, "wb").write(base64.b64decode(d["pngBase64"]))
        print(f"saved {out} ({d['bytes']} bytes)")
    elif cmd == "click":
        print(json.dumps(req("/click", {"x": float(a[1]), "y": float(a[2])}), ensure_ascii=False))
    elif cmd == "log":
        print("\n".join(req(f"/bridge-log?n={a[1] if len(a)>1 else 30}").get("lines", [])))
    elif cmd == "lang":
        if len(a) > 1:
            print(json.dumps(req("/language", {"locale": a[1]}), ensure_ascii=False))
        else:
            d = req("/language")
            print("current:", d.get("current") or "(auto)")
            print("loaded :", ", ".join(d.get("loadedLocales", [])))
    else:
        print("unknown:", cmd)

if __name__ == "__main__":
    main()
