---
name: melvor2-play
description: 通过本地 HTTP 桥接服务操控 Melvor Idle 2（梅尔沃放置2）游戏。当用户想玩、查看、操作、代肝、规划 Melvor Idle 2 时使用：查询技能等级/当前动作、切换训练目标、启停动作、截图看界面、模拟点击。桥服务运行在 http://127.0.0.1:27500（需游戏以注入方式启动）。
---

# Melvor Idle 2 游戏操控

你（AI agent）通过 **MelvorBridge**（注入在游戏进程内的本地 HTTP 服务）直接读写游戏状态并执行操作。桥只监听 127.0.0.1。

## 第一步：检查桥是否在线

```bash
curl -s -m 3 http://127.0.0.1:27500/health
```

- `{"bridge":"0.1.0","gameAttached":true}` → 直接玩
- `gameAttached:false` → 游戏在主菜单/角色选择，需要进入存档（见下文"进入存档"）
- 连接失败 → 游戏没以注入方式启动，运行：
  `powershell -ExecutionPolicy Bypass -File "<本仓库>/dist/launch-melvor-bridge.ps1"`
  （该脚本会杀掉未注入的游戏进程并带注入重启；用户在 Steam 里手动启动的游戏没有桥）

## API 速查（全部 JSON）

| 端点 | 方法 | 说明 |
|---|---|---|
| `/health` | GET | 桥版本 + 是否已连接存档 |
| `/state` | GET | **核心**：全部技能等级、当前活动动作 |
| `/entities` | GET | 全部实体（技能/树/怪/物品都是实体）概览 |
| `/entities?id=<id>&options=1` | GET | 实体详情 + 组件 + 动作可选项（如可砍的树） |
| `/select` | POST `{"entityId":"MelvorBase:Woodcutting","optionId":"MelvorBase:YewTree"}` | 选择动作目标（自动开始训练） |
| `/start` | POST `{"entityId":"MelvorBase:Woodcutting"}` | 开始当前动作 |
| `/stop` | POST | 停止所有活动动作 |
| `/screenshot?width=800` | GET | 返回 pngBase64（解码存文件后用视觉分析） |
| `/click` | POST `{"x":0.5,"y":0.5}` | 模拟鼠标点击（归一化坐标 0~1），用于 API 未覆盖的 UI |
| `/packages` | GET | 游戏数据包清单（`?name=xxx` 读内容） |
| `/bridge-log?n=50` | GET | 桥日志（排障用） |

## 游戏模型（逆向结论）

- 游戏是 **Godot 4.7 + C#/.NET 10**，ECS 架构：万物皆 `GameEntity`，行为在组件上
- **技能实体**：`MelvorBase:Woodcutting`（伐木）、`MelvorBase:Mining`、`MelvorBase:Fishing`、`MelvorBase:Firemaking`、`MelvorBase:Smithing`…（`/state` 的 skills 列表里有全部）
- 每个技能实体有 `Action` 组件：`/select` 选目标并自动 `TryStart()`；`/stop` 全停
- 训练目标也是实体：如 `MelvorBase:YewTree`（紫杉树）。用 `/entities?id=<skill>&options=1` 查全部可选目标
- 上限等级 99；放置游戏——**离线也在结算收益**，切目标随时可逆

## 标准操作流

1. `GET /state` → 掌握现状（等级、在砍什么）
2. `GET /entities?id=<技能>&options=1` → 列出可选目标
3. `POST /select` 切目标（等级不够的目标会报错，换低级目标）
4. `GET /state` 确认 `activeActions` 已变化

## 进入存档（gameAttached:false 时）

游戏每次启动停在角色选择界面。流程（截图确认界面后执行）：
1. `POST /click {"x":0.68,"y":0.26}` 点 Load（先截图核对按钮位置，界面可能变化）
2. 等约 15 秒（存档加载+离线结算）
3. `POST /click {"x":0.49,"y":0.90}` 点离线收益弹窗的 Confirm
4. `GET /health` 确认 `gameAttached:true`

拿不准界面时：`/screenshot` → 解码 base64 到文件 → 看图确认按钮位置再点。

## 原则

- 只调用正常游戏 API 玩游戏（选目标/开始/停止），**不做数值作弊**（修改存档/等级/物品会破坏游戏体验且可能触发云存档校验）
- 放置游戏的正确策略：选一个能挂机的目标让它跑，偶尔回来升级路线
- 游戏更新（Steam 验证文件）会还原注入的 GodotSharp.dll → 重跑 launch 脚本重新注入即可
