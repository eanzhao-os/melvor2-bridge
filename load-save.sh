#!/bin/bash
# 信号驱动的进入存档流程（游戏需已带桥启动，停在角色选择界面）：
# 1. 等 bridge.log 出现 "game not bound yet"（= 角色选择界面就绪）
# 2. 点 Load (0.67, 0.27)
# 3. 等 gameAttached:true
# 4. 点 Confirm 关闭离线收益弹窗
BLOG="$APPDATA/Games By Malcs/Melvor Idle 2/logs/bridge.log"
BASE="http://127.0.0.1:27500"

echo "[1/4] wait for character screen"
ok=""
for i in $(seq 1 40); do
  if grep -q "game not bound yet" "$BLOG" 2>/dev/null; then ok=1; break; fi
  curl -s -m 8 $BASE/health 2>/dev/null | grep -q '"gameAttached":true' && { echo "already attached"; exit 0; }
  sleep 3
done
[ -z "$ok" ] && { echo "TIMEOUT: character screen signal not found"; exit 1; }
echo "ready after ~$((i*3))s"

echo "[2/4] click Load"
curl -s -m 15 -X POST -d '{"x":0.67,"y":0.27}' $BASE/click

echo; echo "[3/4] wait attach"
for i in $(seq 1 20); do
  sleep 4
  curl -s -m 8 $BASE/health 2>/dev/null | grep -q '"gameAttached":true' && { echo "ATTACHED (~$((i*4))s)"; break; }
done

echo "[4/4] dismiss offline popup"
curl -s -m 15 -X POST -d '{"x":0.49,"y":0.90}' $BASE/click > /dev/null
curl -s -m 8 $BASE/health
echo
