# 原图静态对象通行与地基

本次补上五张真实地图中原先仅保留 Raw Key/Value、没有物理占地的 1645 个对象。数据与 effects 的原对象美术共用冻结的[元数据](</storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Generated/ClassicExpansion/MapObjects/metadata.json>)；模拟层消费纯 C# 常量，不依赖 Godot、贴图宽高或素材加载是否完成。

源码和独立短检查均已交回 SOURCE STABLE。本任务没有 build、运行 C# 测试或启动原生游戏；最终集成与实际五步检查由 Root 统一进行。

## 数据、真实占地和默认值

[静态规则](</storage/Users/currentUser/dev/redalert-godot-csharp/Simulation/ClassicMapObjects.cs>)包含 91 个确定名称：34 类 terrain、31 类 structure、5 类 civil vehicle、21 类 smudge。源 SHA256 分别为：

- rules：`fd1e95cea0306ea78049dc81c8cd816e18c28c496872a1ff02edd50bd082062f`
- art：`b477f861063a9509e87b7836b78190204c73bea5ea5595a842c456f5920ea223`

建筑使用原 Art.Foundation 的宽高。例如 CANWY26=5×3、CAPARS13=3×4、CAMIAM01/02=4×2，CAAIRP=3×3、CAOILD=2×2、CABHUT=1×1。没有套统一 3×3、按图像高度猜占地或把动画/bib 的画布当成建筑地基。

树、标志和交通灯的原 Art.Foundation 均为 1×1；五辆民用车没有 Foundation，按其原地图单格位置占一格，不将 Size（乘员容量等其他语义）当成地基。smudge 使用其原 Rules.Width/Height 保持图像锚点，但无论大小都不产生移动/建造障碍。

本地静态模式的缺省策略明确为：缺 IsPassable/Buildable 时，smudge 可通行可建造，其他已知静态类型视作占用；缺 Immune/Crushable/SpawnsTiberium 时值为 false。原值、真实 Foundation 字串、图像别名、TemperateOccupationBits 均在规则表保留，并纳入指纹。未支持的未来名称不猜地基、不制造不可见墙，保留原始记录并计入 UnmappedOriginalObjects；当前五图未知数为零。

TemperateOccupationBits 是**同一格内的步兵位置**，不是多个邻格的 3×3 footprint。公开[地形对象词典，修订 1440](https://wiki.ra2diy.com/index.php?title=8.%E5%9C%B0%E5%BD%A2%E5%AF%B9%E8%B1%A1%E8%AF%8D%E5%85%B8&oldid=1440)说明 0–7 对应左/下/右位置的空余组合。本版沿用现有整格运动网格，保留位值而不假称复刻步兵 subcell；没有扩展砍树、碾压、摧毁、矿钻产矿、驻军或中立建筑捕获。SpawnsTiberium 和 Immune 等字段是保留的数据，不是本次启用的新交互。

## 共享坐标接口

`ClassicMapObjectRules.TryDecode(ClassicMapDefinition,ClassicMapObject,out ClassicMapObjectPlacement)` 是共享解析器：

- terrain：原 Key 的 `Y*1000+X`，Value 第一个名称。
- structures / units：原 CSV 的 Type、X、Y；只支持本批已知结构与五种车辆。
- smudge：原 CSV 的 Type、X、Y。
- X/Y 只减 SourceOriginX/Y，不缩放、不绕过障碍改位置。

`Map.OriginalObjects` 为 `IReadOnlyList<ClassicMapObjectPlacement>`。每项公开 Source（原记录）、Definition、SourceX/SourceY、X/Y、Width/Height、Name。结构 X/Y 是占地左上原格，terrain/车是原单格位置。Root 与 effects 使用同一份 typed placement 进行绘制锚点、fog、边界和深度处理，避免各自猜 Raw CSV。

## 物理接入

[独立 partial](</storage/Users/currentUser/dev/redalert-godot-csharp/Simulation/BattleMap.OriginalObjects.cs>)在原图加载末尾构建不可从外部修改的独立格子 mask：land=1、water=2、placement=4。动态 Buildings 数组只保留真实实体 ID，不填伪中立建筑 ID。

[地图](</storage/Users/currentUser/dev/redalert-godot-csharp/Simulation/BattleMap.cs>)的 Walkable、单位固定点身体扫掠及陆地 FootprintFree 已接入；[海空域](</storage/Users/currentUser/dev/redalert-godot-csharp/Simulation/BattleMap.Domains.cs>)的船只/两栖通行及船坞地基也检查 mask。空气导航先返回，不因树和城市地面障碍受阻。地图本身地形、矿、桥和坡检查仍同时生效；不清树、不清矿、不改原高程。

`ClassicMapObjectRules.Definitions` 是确定顺序的只读 canonical 表，无委托或 Godot 对象；亦提供 `AddToFingerprint(ref StableHash)`。Root 已反馈将该表纳入 ContentFingerprint，HashSchema=4。

## 已完成的数据检查与待执行短检查

Python 静态数据核对通过：规则表 91 行 × 14 字段逐项等于共享原元数据；1645 原记录全部可按同一格式解析；没有越界地基单元格被裁剪。

| 原图 | 原记录 | 地形对象 | 建筑 | 民用车 | 污迹 | 静态占用唯一格 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| mp01t4 | 185 | 156 | 29 | 0 | 0 | 222 |
| mp02t2 | 291 | 206 | 25 | 5 | 55 | 427 |
| mp08t2 | 249 | 249 | 0 | 0 | 0 | 249 |
| mp23t4 | 555 | 549 | 6 | 0 | 0 | 555 |
| mp25t6 | 365 | 365 | 0 | 0 | 0 | 365 |
| 总计 | 1645 | 1525 | 60 | 5 | 55 | 1818 |

按 Root 要求新增[官方开局短检查](</storage/Users/currentUser/dev/redalert-godot-csharp/Tests/ClassicMapObjectChecks.cs>)，入口 `ClassicMapObjectChecks.Run(args)`；只写检查，没有运行。每张真实 Generated 地图注册进 SourceRepository，取满 4/2/2/4/6 玩家，ClassicStart=true、StartingUnits=0，在 Tick0 向全部 MCV 原地发 Deploy。每图只执行一次 World.Step，总上限五步。严格要求 Accepted==MaxPlayers、Rejected==0、18 个玩家各有存活建造场且原 MCV 消失；不移位重试、不清树、不弱化断言。

失败结果会附官方起点、实际 MCV 格、原国家 Foundation，以及逐格 TileIndex、TerrainType、RampType、矿、高差和占位对象名称/坐标/宽高，供 Root 判断是原地形、矿、物件还是生成/部署逻辑的问题。此项实际通过与否以 Root 集中测试输出为准，不能把静态表比对代替官方开局验证。

## Root 集中验证：17/18 → 18/18

Root 已执行上文的真实五步开局检查，本节更新上文“待执行”状态。terrain 仍未自行 build、运行 C# 或启动 Native；本节依据 Root 的集中结果与已读取的[五步检查 JSON](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/skirmish-parity/map-object-checks.json>)记录证据。

首轮四图全部通过，DEFCON 6 为 5/6，总计 17/18。唯一失败者是 player1/Russia，官方起点和实际 MCV 都为 runtime `(80,78)`。旧展开公式 `CellX−Width/2, CellY−Height/2` 为 4×4 建造场选取左上 `(78,76)`，将原岩石所在 `(78,79)` 包进地基。

只读回查原地图与原 TMP 证实：该格 source `(79,80)` 的 Iso 记录为 `[79,80,422,0,10,0]`；Tile422 对应 [dlat04.tem](</storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Reference/Visual/Terrain/dlat04.tem>)，原 header 高度/地形/坡字节为 `00 0e 00`，即 Height=0、TerrainType=14、RampType=0。其原 OverlayPack 值 173 对应 TROCK01；[原规则](</storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Reference/Maps/Metadata/rules.ini#L21152>)明确 `Land=Rock`、`NoUseTileLandType=true`、`IsARock=true`。该格必须继续阻挡，原石头、原地形与矿数据均保留。

独立只读几何比较在同一份原图、矿、坡、高差和静态障碍上得到：旧偏移 `(-2,-2)` 为 17/18，统一偏移 `(-1,-1)` 为 18/18；`(-1,-2)` 也为 18/18，`(-2,-1)` 仍为 17/18。这是部署取整方向的几何证据，没有把它当成原版引擎实现的证明。

Root 随后统一偶数地基 pivot：展开使用 `X−(W−1)/2, Y−(H−1)/2`；收车使用其逆映射；AI 地基预检与测试诊断共用同一原点规则。该 4×4 建造场的新左上为 `(79,77)`，全部 16 格合法。Root 保留原五步硬断言，未清树、未清岩石、未移动官方起点或降低通过条件。

Root 集中构建反馈为 **0 warnings、0 errors**，随后 `--classic-map-objects` 实际结果如下：

| 原图 | Accepted | Rejected | 成功展开 |
| --- | ---: | ---: | ---: |
| mp01t4 | 4 | 0 | 4/4 |
| mp02t2 | 2 | 0 | 2/2 |
| mp08t2 | 2 | 0 | 2/2 |
| mp23t4 | 4 | 0 | 4/4 |
| mp25t6 | 6 | 0 | 6/6 |
| 总计 | 18 | 0 | **18/18** |

结果 JSON 为 `passed=true, worldSteps=5, stepCap=5, expanded=18, expectedExpanded=18`。其中 DEFCON 6 player1 仍记录官方/实际 MCV `(80,78)`，新地基 `(79,77)`、`footprintLegal=true`、`blocked=[]`，可直接核对原地部署行为。

Root 同时反馈 `--classic-replay` **6/6 通过**，共 **90/100 step cap、5008ms**；新增的原位置收车断言通过，展开/收车往返没有向右下漂移。此项为 Root 的集中回归反馈，本报告未另行重复运行。最终原生美术回归仍由 Root/effects 后续记录，原岩石可见素材及 TMP alpha 修正与本节物理检查分开交付。
