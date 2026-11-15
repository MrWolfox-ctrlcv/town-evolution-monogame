# NPC 第一阶段（第 1–2 步）

本轮只做"居民能在岛上走、能听指令走过去"。劳动、饥饿、负重载具等留给后续。

分支 `npc-stage1`，独立工作树 `town-evolution-npc`，从主工作树 2026-11-15 未提交源码的快照起步；
主目录 `town-evolution-monogame` 没有被修改。

## 已实现

| 内容 | 位置 |
| --- | --- |
| 居民数值、物品重量、行走速度公式 | `src/Town.Core/Residents/NpcConfig.cs` |
| 居民状态、任务、存档形态 | `src/Town.Core/Residents/NpcState.cs` |
| 地块通行、四方向 A*（曼哈顿启发式） | `src/Town.Core/Residents/NpcNavigation.cs` |
| 逐 tick 位移、台阶高度平滑 | `src/Town.Core/Residents/NpcMover.cs` |
| 直接指派、到达判定、无路提示 | `src/Town.Core/Residents/NpcTasks.cs` |
| 居民集合、按住宅生成（每栋 2 个名额） | `src/Town.Core/Residents/NpcRoster.cs` |
| 演示岛地形与布置 | `src/Town.Core/NpcDemoIsland.cs` |
| 黑色方块与影子 | `src/Town.Game/Rendering/NpcRenderer.cs` |
| 点选、Shift 指派、地面标记 | `src/Town.Game/ResidentDemo.cs` |
| 演示岛启动、右下角面板 | `src/Town.Game/NpcDemoMode.cs` |

数值按计划稿：2 格/秒空载，负重后按 `1 − 0.3 × 负重/最大负重` 降到 1.4 格/秒；最大负重 12kg；
A* 代价平地 10、上台 15、下台 12；相邻高度差超过 1 级不可通行，水面与建筑都不可通行，土路可通行。

## 演示岛

`试玩NPC演示.cmd`（内部走 `scripts/Play-NpcDemo.ps1`，使用项目本地 .NET，不下载任何东西）。

- 24×18 平地，南侧一段两级台阶通往高台；台阶只有一条路，所以"绕行还是走台阶"可以直接看出来。
- 住宅在 (6,5)，两名居民在门前生成。
- 右下角面板显示选中居民的饱食/体力/负重/速度/所在格/任务。
- 点居民选中，**按住 Shift 点地面**下达"走到那一格"；目标不可站时自动退到最近的可站立邻格。
- 存档目录与正式版分开：`%LocalAppData%\TownEvolution.MonoGame.NpcDemo`。

## 验证

`tests/Npc.Checks`（独立工程，不动现有的 Town.Checks）：

```
dotnet run --project tests/Npc.Checks/Town.Npc.Checks.csproj
```

9 项全部通过：演示岛形状、住宅名额与生成、台阶可走/两级悬崖不可走/水面不可走、一秒钟走两格、
负重降速到 1.4、模拟 tick 端到端到达、无路时上报而不是反复重算、居民与任务存档往返、上下台阶脚下高度连续。

Windows 实机：演示岛与正式随机岛都正常渲染并截图；截图在 `review/npc-demo`、`review/npc-regression`。

## 尚未实现（下一步）

饥饿/体力随时间变化、进食、负重的实际搬运、农田/伐木/挖掘/加工的劳动、手推车与船。
居民目前只有一个"走到某格"的任务类型。
