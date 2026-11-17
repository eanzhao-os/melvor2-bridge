#!/bin/bash
# 重建桥 → 部署 → 重启游戏（开发迭代用）
# 环境变量 MELVOR2_GAME_DIR 指定游戏目录（默认探测常见 Steam 库位置）
set -e
ROOT="$(cd "$(dirname "$0")" && pwd)"

detect_game_dir() {
  [ -n "$MELVOR2_GAME_DIR" ] && { echo "$MELVOR2_GAME_DIR"; return; }
  for d in "/c/Program Files (x86)/Steam/steamapps/common/Melvor Idle 2" \
           /d/SteamLibrary/steamapps/common/Melvor\ Idle\ 2 \
           /e/SteamLibrary/steamapps/common/Melvor\ Idle\ 2; do
    [ -f "$d/Melvor2.exe" ] && { echo "$d"; return; }
  done
  echo "ERROR: 游戏目录未找到，请设置 MELVOR2_GAME_DIR" >&2; exit 1
}

GAME="$(detect_game_dir)"
DATA="$GAME/data_GamesByMalcs.Melvor2_windows_x86_64"
GODOTSHARP="$(cygpath -w "$DATA/GodotSharp.dll")"

cd "$ROOT/src/MelvorBridge"
echo "[1/4] build bridge"
dotnet build -c Release -p:GodotSharpPath="$GODOTSHARP" 2>&1 | grep -E " error " || true

echo "[2/4] stop game"
taskkill //IM Melvor2.exe //F 2>/dev/null || true
sleep 3

echo "[3/4] deploy"
cp bin/Release/net10.0/MelvorBridge.dll "$GAME/"
cp bin/Release/net10.0/MelvorBridge.dll "$DATA/"

echo "[4/4] restart game"
powershell -Command "Start-Process -FilePath '$(cygpath -w "$GAME/Melvor2.exe")' -WorkingDirectory '$(cygpath -w "$GAME")'" 2>/dev/null
echo "restarted; run ./load-save.sh to enter the save"
