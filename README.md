# Melvor2Bridge —— 和 AI agent 一起玩 Melvor Idle 2

Melvor2Bridge 让任意 AI agent 坐到你身边，和你一起玩《Melvor Idle 2: A RuneScape Adventure》：它看得到你的进度，帮你分析、规划、算账，按你的决定动手——拿主意的始终是你。

技术上它是一个注入游戏进程内的本地 HTTP 桥，通过 127.0.0.1:27500 暴露游戏状态与操作。任何能发本地 HTTP 请求、或能运行 curl / Python 的 AI agent 都能用——Claude Code、Codex CLI、Gemini CLI、Cursor、ZCode 等都行（仅作举例，不限于此）。

## 一起玩，不是替你玩

- **你拍板**：练哪个技能、选哪个目标、资源怎么花、路线怎么走，都由你决定。AI 把选项摆出来（2~3 个为宜），讲清利弊和理由，可以给出推荐，然后等你选。
- **只读随意，改动先问**：查状态、列选项、截图随时可以做；会改变游戏的操作（切换目标、启停训练、点击界面、进入存档、启动或重启游戏）要先说清做什么、为什么、有什么影响，你同意后再执行，执行完回报结果。
- **只做你交代的事**：交代一件做一件，不顺手改别的，也不擅自加步骤。你定下的规则（比如"等伐木升级了就帮我换下一种树"）只在说过的范围内照办，超出范围会停下来问。你说"你看着办"时，它也要给出具体方案请你点头，不直接替你做主。
- **不代肝**：不会在你不知情时推进游戏，也不跑无人值守的循环或定时脚本去自动刷、自动切目标。放置游戏本身的离线结算是游戏机制，不算在内。
- **一起看**：升级、解锁、离线收益结算这些时刻，把数据或截图分享给你，不会悄悄点掉弹窗。
- **说人话**：给能帮你做决定的信息——等级、当前在做什么、可选目标及其要求，条件允许时再给经验效率、耗时；不堆原始 JSON。
- **公平游戏**：只调用正常的游戏操作，不改存档、等级、物品等数值。

一段典型的开局是这样的：

> **你**：我们一起玩 Melvor Idle 2，先看看我现在的进度。
>
> **AI**：（GET /state）你伐木 Lv23、采矿 Lv12，现在在砍树。接下来想练哪个？
>
> **你**：先把伐木练上去。
>
> **AI**：能砍的树里有 3 种值得考虑：A 经验最快；B 经验稍慢，但木头更值钱；C 的木头是后面别的技能要用的材料。你想先冲等级，我推荐 A。要切过去吗？还是选别的？
>
> **你**：切 A。
>
> **AI**：（POST /select，再用 GET /state 核对）已切到 A，正在砍。想换路线随时叫我。

## 快速开始

1. 带桥启动游戏：

   ```bash
   powershell -ExecutionPolicy Bypass -File dist/launch-melvor-bridge.ps1
   ```

2. 把 skill/ 装给你的 agent（命名为 `melvor2-play`），复制或软链都可以：

   - Claude Code：`~/.claude/skills/melvor2-play/`
   - 多个 agent 共用：`~/.agents/skills/melvor2-play/`，再软链给各个 agent
   - SKILL.md 里的 `<本仓库>` 是启动脚本路径的占位符，装好后换成你本地仓库的实际路径

   支持 Agent Skills（SKILL.md）格式的 agent 会自动识别；不支持的，让它直接读 `skill/SKILL.md` 当说明，或者粘这段开场提示词：

   ```
   读一下 <本仓库>/skill/SKILL.md，然后按里面的规矩和我一起玩 Melvor Idle 2：你负责看状态、出主意，由我拍板；会改动游戏的操作先问我。
   ```

3. 然后开口就行，比如：

   - 我们一起玩 Melvor Idle 2，先看看我现在的进度。
   - 我伐木练得有点慢了，帮我看看接下来练什么好。
   - 我想把锻造冲上去，给我几个方案。

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
| `src/MelvorBridge/` | 桥（C# net10.0）：HTTP 服务 /state /entities /select /start /stop /screenshot /click /language /dir /project-setting /packages |
| `src/Injector/` | dnlib 注入器：`inject|restore|status <GodotSharp.dll> <MelvorBridge.dll>` |
| `src/LoadTest/` | 游戏外程序集加载验证工具（排障用） |
| `dist/launch-melvor-bridge.ps1` | 带注入启动游戏（自动检测/写 steam_appid） |
| `redeploy.sh` | 重建桥→部署→重启游戏（开发迭代用） |
| `load-save.sh` | 信号驱动自动进入存档（角色选择→Load→Confirm） |
| `analysis/` | 反编译产物（**不入库**，见下方声明；用 `ilspycmd -p -o analysis/src --nested-directories <游戏DLL>` 自行生成） |
| `skill/` | 通用 Agent Skill：SKILL.md 是给 agent 的一起玩说明，melvor.py 是紧凑输出的 CLI；安装方法见「快速开始」 |

## 常用操作

这些是开发和排障时的直接调用；平时和 agent 一起玩，由 agent 在你同意后调用。

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

## 启用官方中文（EA 首发未开放，但资源已内置）

游戏 PCK 内置了 `res://Locale/zh_CN.po`（另有 `fr.po`），但 `project.godot` 只注册了 `en_GB.po`，
所以界面永远是英文。用 Godot 的设备级配置覆盖即可启用（不修改游戏文件）：

在 `%APPDATA%/Games By Malcs/Melvor Idle 2/device_settings/override.cfg` 追加：

```ini
[internationalization]

locale/translations=PackedStringArray("res://Locale/en_GB.po", "res://Locale/zh_CN.po", "res://Locale/fr.po")
```

重启游戏即可。`Language` 设为自动（空）时跟随系统语言；也可用桥 `POST /language {"locale":"zh_CN"}` 显式切换（立即生效）。

## 仓库内容声明

本仓库**不提供、也不包含**任何游戏反编译源码或游戏原始文件（版权归属 Games by Malcs）：

- `analysis/`（游戏程序集与 GodotSharp 的反编译 C# 源码，约 53MB）——已列入 `.gitignore`，仅本地存在
- 游戏 DLL / PCK 等原始文件一律不收录；`dist/MelvorBridge.dll` 为本项目自行编译产物

仓库提供的全部内容（注入器、桥、脚本、文档）均为本项目原创代码。

## 注意

- 仅本机 127.0.0.1；仅调用正常游戏 API（不做存档/数值修改）
- 会改动游戏的操作（切换目标、启停、点击、进入存档、启动/重启游戏）由玩家确认后执行
- 云存档经 Nakama 正常同步；注入不触碰存档格式
- Steam "验证文件完整性" 会还原注入文件（重跑 injector 即可）
