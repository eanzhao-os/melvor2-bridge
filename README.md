# Melvor2Bridge —— Melvor Idle 2 的 AI 操控桥

让 AI agent（ZCode / 任何能发 HTTP 的程序）直接操控《Melvor Idle 2: A RuneScape Adventure》。
桥以代码注入方式运行在游戏进程内，通过本地 HTTP 服务（127.0.0.1:27500）暴露游戏状态与操作。

## 逆向结论（2026-11-17，游戏 v0.11.6-alpha.1）

| 项 | 结论 |
|---|---|
| 引擎 | Godot 4.7.2 mono（**不是**一代的网页 JS） |
| 游戏逻辑 | C# / .NET 10，主程序集 `GamesByMalcs.Melvor2.dll`（8003 类型，ECS 架构） |
| 资源 | 嵌入 exe 的 PCK（目录 AES 加密，不破解——运行时经游戏自身通道读取） |
| 启动链 | native hostfxr → 主程序集的 `GodotPlugins.Game.Main::godotsharp_game_main_init`（UnmanagedCallersOnly，**主 DLL 是 ReadyToRun 编译，重写会破坏 native 导出**） |
| 内容数据 | JSON 数据包（`res://Data/Packages/*.json`），可被 `user://local_data/` 覆盖 |
| 云 | Nakama（MelvorCloud）+ Satori；存档目录 `%APPDATA%/Games By Malcs/Melvor Idle 2/steam/<id>/saves/` |
| mod.io 集成 | 本版本未实装（EA 首发），主程序集无 ModManager |

## 注入方案（最终形态）

```
游戏文件（均自动备份 .orig，可还原）：
  data_GamesByMalcs.Melvor2_windows_x86_64/
    GodotSharp.dll                    ← dnlib 注入：
        <Module>::.cctor              → call MelvorBridge.StartupHook::Initialize()
        GodotObject..ctor×4           → call MelvorBridge.StartupHook::OnNativeObjectCreated()  (主线程钩子)
    GamesByMalcs.Melvor2.deps.json    ← 追加 MelvorBridge/1.0.0 条目（进 TPA，使程序集可解析）
    MelvorBridge.dll                  ← 桥本体（32KB）
  Melvor2.exe 同级: MelvorBridge.dll, steam_appid.txt(3218350)
```

为什么这样设计（踩坑记录）：
- `DOTNET_STARTUP_HOOKS` 对 Godot 的 hostfxr 组件宿主**无效**
- 直接改主 DLL 会丢失 R2R native 导出 → native 空指针崩溃（c0000005）
- `Assembly.LoadFrom` 在极早期上下文 MissingMethod（netstandard facade / BCL 拆分）
- 非 TPA 程序集无法被解析 → 必须进 deps.json
- Godot API 非主线程调用必崩 → 用 `GodotObject..ctor` 作主线程钩子 + `Callable.CallDeferred` 转发

## 组件

| 路径 | 说明 |
|---|---|
| `src/MelvorBridge/` | 桥（C# net10.0）：HTTP 服务 /state /entities /select /start /stop /screenshot /click /packages |
| `src/Injector/` | dnlib 注入器：`inject|restore|status <GodotSharp.dll> <MelvorBridge.dll>` |
| `src/LoadTest/` | 游戏外程序集加载验证工具（排障用） |
| `dist/launch-melvor-bridge.ps1` | 带注入启动游戏（自动检测/写 steam_appid） |
| `redeploy.sh` | 重建桥→部署→重启游戏（开发迭代用） |
| `load-save.sh` | 信号驱动自动进入存档（角色选择→Load→Confirm） |
| `analysis/` | 反编译产物（**不入库**，见下方声明；用 `ilspycmd -p -o analysis/src --nested-directories <游戏DLL>` 自行生成） |
| `~/.agents/skills/melvor2-play/` | ZCode skill（SKILL.md + melvor.py CLI） |

## 常用操作

```bash
# 带桥启动游戏
powershell -ExecutionPolicy Bypass -File dist/launch-melvor-bridge.ps1

# 自动进入存档（游戏在角色选择界面时）
bash load-save.sh

# 查状态 / 列选项 / 切换目标 / 停止
curl -s http://127.0.0.1:27500/state
curl -s "http://127.0.0.1:27500/entities?id=MelvorBase:Woodcutting&options=1"
curl -s -X POST -d '{"entityId":"MelvorBase:Woodcutting","optionId":"MelvorBase:YewTree"}' http://127.0.0.1:27500/select
curl -s -X POST http://127.0.0.1:27500/stop

# 游戏更新后重新注入（Steam 验证文件会还原 GodotSharp.dll）
cd src/Injector && dotnet run -- inject "<data目录>/GodotSharp.dll" ../../dist/MelvorBridge.dll

# 完全卸载注入
dotnet run -- restore "<data目录>/GodotSharp.dll"
```

## 游戏模型速记

- 万物实体：技能=`MelvorBase:Woodcutting`，训练目标=`MelvorBase:YewTree`
- 技能实体组件：`Experience`(等级) / `Action`(训练) / `Milestones` / `Pets`…
- `/select` 调用 `SelectTree/SelectFish/...` 并自动开始训练；`/stop` 停全部
- 截图 `/screenshot`、UI 点击 `/click`（归一化坐标）覆盖 API 之外的操作

## 仓库内容声明

本仓库**不提供、也不包含**任何游戏反编译源码或游戏原始文件（版权归属 Games by Malcs）：

- `analysis/`（游戏程序集与 GodotSharp 的反编译 C# 源码，约 53MB）——已列入 `.gitignore`，仅本地存在
- 游戏 DLL / PCK 等原始文件一律不收录；`dist/MelvorBridge.dll` 为本项目自行编译产物

仓库提供的全部内容（注入器、桥、脚本、文档）均为本项目原创代码。

## 注意

- 仅本机 127.0.0.1；仅调用正常游戏 API（不做存档/数值修改）
- 云存档经 Nakama 正常同步；注入不触碰存档格式
- Steam "验证文件完整性" 会还原注入文件（重跑 injector 即可）
