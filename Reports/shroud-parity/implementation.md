# Vanilla shroud、永久开图与原始边缘（task37）

原版默认遭遇战没有启用第二层“离开即时视野后变灰”的战争迷雾；未探索的黑色 shroud 仍存在，普通开图保留。本次修复核心可见性以及所有复用该接口的地形、单位、攻击、命中、小地图和特效路径，并用原始 SHROUD.SHP 替换旧的四边大多边形渐变。构建和原生运行由 Root 统一执行。最终统一构建0 warnings、0 errors，短原生验收21/21通过并生成5张真实截图；Root 与我均实看最终03/04，旧内黑带、亮牙、灰色长立面与灰楔均已消除，外向柔边正确。Task37当前目标完成；高度断层边缘仍可能显示保守的黑色侧面/小缺口，不能宣称与原版逐像素完全一致。前两轮失败图与修正依据全部保留在下文。

## 原始规则与特殊情况证据

[原版 rules.ini](/storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Reference/Maps/Metadata/rules.ini) 的 `[General]` 第172行是 `FogOfWar=no`，第551行是 `ShroudGrow=no`。`[MultiplayerDialogSettings]` 第2510行 `ShadowGrow=no`、2511行 `Shroud=yes`、2520行 `FogOfWar=no`；2484行注释将 ShadowGrow 标为 DESUPPORTED，因此不能把这一项单独当作定量的引擎行为证明。[原版手册](/storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Reference/FrontEndResearch/ra2-2000-english-manual-extracted.txt) 第590–595行说明黑幕是未探索区域，由单位 sight 揭开。

- Gap：`[GAGAP]` 8864行 `GapGenerator=yes`、8865/8866行两种半径均10格、8877行 `Power=-100`、8879行 `Powered=true`。手册1435–1440行说明其制造 shroud 并需要大量电力。
- 卫星：`[GASPYSAT]` 8833行 `SpySat=yes`、8837行 `Sight=5`、8842行 `Power=-100`、8843行 `Powered=true`；手册1465–1469行说明其显示战场和雷达的所有位置。
- 地图箱：`[Powerups]` 第22501行 `Reveal=10,REVEAL,yes ; reveal entire radar map`。手册844–849行同样确认可以开图，但没有说明它授予持续的卫星能力。

已完整获取的 [CNCNZ 原版 Allied Structures 说明](https://cncnz.com/games/red-alert-2/allied-structures/) 明确写出：Gap 范围里被敌方揭开的区域会 “quickly hidden again”；Gap 被毁或断电后，其周围 shroud “can be uncovered regularly again”。卫星 “does not counter the effects of enemy Gap Generators”，且 “If it is sold or destroyed, the owner’s shroud is reset”。本实现据此保留 Gap 对已探索格的重遮蔽、Gap 优先于卫星，以及失去最后一个受益卫星时的重新遮蔽。该网页的部分价格与本地 rules 不同，因此数值全部以本地原版数据为准。

以下界限没有冒充已精确复原：Gap 的 native 重遮蔽周期、敌军进入其范围后的确切缩小视距、卫星低电期间的精确重黑范围，以及地图箱在所有特殊情况下的记忆恢复顺序。当前沿用现有 sight 半径与五 tick 可见性刷新；Gap 保留当前直接 sight，单位离开后再次遮蔽。卫星低电只撤掉临时卫星揭示并保留自然探索；地图箱按一次开图处理，不能永久压制 Gap。这些是受数据约束的明确实现选择。

## 核心修复及一致的消费者

[World.Visibility.cs](/storage/Users/currentUser/dev/redalert-godot-csharp/Simulation/World.Visibility.cs) 复用现有每格 visibility byte：

| 位 | 含义 | 跨刷新保留 |
|---|---|---|
| 1 | 自然探索／一次开图 | 普通情况下保留；Gap 可以重新遮蔽 |
| 2 | 当前单位／建筑直接 sight | 每次重算，只供特殊遮蔽优先级等使用 |
| 4 | 当前已供电卫星揭示 | 每次重算，不写成自然探索 |
| 8 | 卫星曾生效的来源记录 | 卫星资产仍存在时保留，供卖毁后的重置判断 |

[World.cs](/storage/Users/currentUser/dev/redalert-godot-csharp/Simulation/World.cs) 的 Visible 和 Explored 现在一致查询 `(1|4)`；即时 LOS 位2不再被当作所有普通格的第二层 fog。现有完整 StateHash 已哈希每个 visibility byte，不新增未哈希的模拟字段。Root 统一更新规则版本，拒绝旧 peer/replay。

普通 sight 通过 OR 更新，保留来源记录。Gap 对敌方范围中的非直接 sight 格清掉自然探索和当前卫星揭示，保留卫星来源；己方和盟友不受影响。Gap 停止后不凭空恢复被覆盖的旧知识，可以再次侦察或由仍工作的卫星重新揭示。卫星暂时缺电或暂时被时空擦除不会被误判为资产毁灭；最后一个存活完整卫星失去后，先重置记忆，再由现有友军 sight 重新揭开。

[World.ClassicEconomy.cs](/storage/Users/currentUser/dev/redalert-godot-csharp/Simulation/World.ClassicEconomy.cs) 仅改约定的可见性函数和 Reveal 箱分支：卫星与 Gap 分层结算；箱子调用一次 `RevealClassicMap`，不再用永久布尔标志每次把全图填成即时视野。其它经济、飞机初始化和出口代码未改。

所有现有消费者通过相同的核心查询得到新语义，无需各自抹掉 tint：

- [Main 视野接口](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/Main.MatchLifecycle.cs) → 战场地形、桥、树石、矿物、武器特效、点击命中和维护目标。
- [ClassicUnitVisibleTo](/storage/Users/currentUser/dev/redalert-godot-csharp/Simulation/World.ClassicCombat.cs) → 战场单位、Hit 和小地图单位。
- [攻击指令](/storage/Users/currentUser/dev/redalert-godot-csharp/Simulation/World.Commands.cs) 与 [目标获取／持续攻击](/storage/Users/currentUser/dev/redalert-godot-csharp/Simulation/World.Combat.cs) → 普通已探索敌军可以被看到和攻击，仍遵守真实武器范围和 CanTarget。
- [MiniMap](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/MiniMap.cs) → 同一 Explored/Visible，离开 sight 的已探索地形不再变成灰色；雷达上的敌方标记同样受 Gap 和隐形检测限制。

隐形检测没有被开图替代。现有潜航/探测及间谍伪装判断保留；例如 rules `[DEST]` 的 Sight=7 与 SensorsSight=8 不相等，`[SUB]`、`[DLPH]`、`[SQD]` 的 Cloakable/Sensors 以及 SubTorpedo/SonicZap 的 `DecloakToFire=no` 均独立于地图是否揭开。传感器的所有特殊边界没有在本次额外修改。

## 原始 SHROUD 数据与边缘渲染

[SHROUD.SHP](/storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Reference/Shroud/shroud.shp) 与 [FOG.SHP](/storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Reference/Shroud/fog.shp) 来自 pinned 原始 `ra2.mix/conquer.mix`，完整来源在 [sources.json](/storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Reference/Shroud/sources.json)。仅精确读取两段共177,424字节，没有下载整包或读取引擎实现代码。

| 来源 | 全局 archive offset | bytes | SHA256 |
|---|---:|---:|---|
| shroud.shp | 13,778,928 | 88,712 | `9818f7e2685fb1e8ed75ceff568aaf5d36350c747cbd4f30856458bba12e4964` |
| fog.shp | 13,867,648 | 88,712 | `b8e3640435140041f0ac3d4ff18ad99b035a2292b685a2fd89c64bf1cc55e73f` |

每份头为 `(0,60,30,96)`。0–47是48个实存未压缩原始帧，48–95是48个空占位记录；不能把96全部说成96种有效边。原始记录、裁切、索引、每帧 SHA256 全保留在 [manifest](/storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Generated/Shroud/manifest.json) 和原索引文件中。Frame0 是900像素的实心 diamond；Frame47 虽然数据表同标 mask255，却全部透明，不能误选为全黑帧。

原始索引254在画布外，diamond 内是2–127的连续强度；FOG 内则64–127。显示用黑色 alpha，采用明确披露的 `(index*255+63)//127`、254→透明。原 SHP 与公开 YAML 不含 native alpha 数值 LUT，因此这一归一化是呈现推断，不能声称逐 alpha 复原。原始索引精确保留；未套用 unittem/cameo palette、重画边缘、另加 FOG 透明度乘数。

[生成脚本](/storage/Users/currentUser/dev/redalert-godot-csharp/Tools/classic_art/prepare_shroud.py) 提供可重复 CLI；使用原像素生成 [atlas](/storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Generated/Shroud/atlas.png) 与 [contact sheet](/storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Generated/Shroud/contact-sheet.png)。已用 read_image 查看接触表，原始渐变边和角均保留。[参考 README](/storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Reference/Shroud/README.md) 记录数据来源和推断界限。

[Battlefield.Shroud.cs](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/Battlefield.Shroud.cs) 只读取 shroud 序列，不画 FOG 层。原像素的方向必须解释为**透明缺损朝向**：16/32/64/128分别向屏幕右上/右下/左下/左上变透明，1/2/4/8分别向上/右/下/左角变透明。例如 frame1/mask16 的 opacity 质心为 `(24.012,17.636)`，透明缺损质心为 `(35.229,12.248)`，画布中心 `(29.5,14.5)`；透明侧是右上，黑侧是左下。首轮将此方向误当黑侧，已探索格配未知邻居会出现内黑带和外亮牙。修正为已探索格不画 shroud；在未知格按**已探索邻居**选择原边/角，透明侧朝已探索地，只有没有已探索邻居的未知格选原Frame0。对应相邻格的冗余角位仍被边位消去，全部256种邻域组合的46种非零边型均有对应原帧。

仅未知的一格边缘补 ground 作为原透明渐变的底图，不能把黑背景当作渐变底。原图 fringe 使用独立 ground-only 队列，仅合入 TMP 首 pass；[OriginalMaps ground hook](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/Battlefield.OriginalMaps.cs) 的原 `originalVisibleTiles` 保持已探索语义，矿物第二 pass、桥、岩石、foreground 和命中列表不加入未知格。若原桥 TMP 烘焙了桥体，未知 ground 改选真实原 substrate TMP，无匹配则不补该格；不借渐变显示桥。生成地图的伪 rock actor 不为 fringe 入队；矿物仍由 `KnownResource` 的 Explored early gate 拦截。新增审计 `maskPlacement=unknown-cell-known-neighbors`、`groundFringeCells`、`unexploredResourceCellsDrawn` 和 `unexploredActorTerrainTilesQueued`，供复验检查无资源/目标泄漏。

平面直接按60×30原画布绘制；坡面按真实 GroundFootprint 的四个地形角和中心分成四片纹理映射，保留原渐变并对齐高程。原始 ramp13 在该投影角度会压成一条线，零面积片被跳过并审计，避免向 GPU 提交无法三角化的多边形。遮蔽在单位和武器特效后绘制，黑幕不会只剩背景 tint。纹理随战场 ExitTree 释放。没有改 Terrain/ArtLibrary 或小地图文件；额外原图修改仅限 Root 许可的 Queue ground-only 分流与 TMP 首 pass 合并，矿物、桥和 foreground pass 不变。

## 数据验证与短原生验收

实际执行的资产 CLI：

```text
PYTHONDONTWRITEBYTECODE=1 python3 Tools/classic_art/prepare_shroud.py --fetch --prepare --validate
```

[validation.json](/storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Generated/Shroud/validation.json) 为 pass：4份来源文件、192条帧记录、96个实存 raster、96个空 placeholder、172,800个原始 raster 像素。原索引、帧头 SHA、PNG、atlas 与声明映射、透明 padding、mask表和方向探针全部匹配。完整重复生成得到299个字节完全一致的资产文件。另执行全部256邻域对照检查和当前源码 `git diff --check`，均通过。

短原生入口已在 [Main.ShroudChecks.cs](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/Main.ShroudChecks.cs) 完成并 source freeze，接口为 `RunClassicShroudChecks()` / `GetClassicShroudAudit()`。21个具名成功条件与 missingConditions 动态判断，覆盖侦察后离开仍可见、已探索敌军及武器射程大于 sight 的真实 Attack、潜航与探测独立、Gap/卫星优先级、10/11格边界、断电/卖毁及一次开图。首轮实际输出5张 GPU 截图：初始原边缘、侦察 sight、离开后的明亮探索、一次开图后 Gap 孔洞、侦察进入 Gap。用 GPU 地形亮度与小地图原始 RGBA 对比检查离开后不变灰。限制每次 Move 400tick、合计2000step、总时限90秒，报告输出 `Reports/shroud-parity-<UTC>-<ticks>/acceptance.json`。

检查只短暂停止 Main 的 `_Process`，保持 UI/GPU 与小地图更新，finally 恢复并返回菜单；不用会每帧强制全图 Reveal 的旧 terrainInspection 模式。特殊能力是明确开发者 fixture，通过真实 Core.Step 与合法 Move/Attack 命令验证，不冒称正常生产实战。实现分工未构建或启动原生游戏；构建和下面的首轮原生执行均由 Root 完成。

## 首轮 alpha12：21 逻辑通过，视觉未通过

[首轮 acceptance.json](/storage/Users/currentUser/dev/redalert-godot-csharp/Reports/shroud-parity-20261022-162935-85178/acceptance.json) 为 `passed=true`、`checkCount=21`、`captureCount=5`、`error=""`、`errors=[]`、`missingConditions=[]`，938 次 World step，总耗时约1.965秒；`processingRestored=true`、`returnedToDeployment=true`、`running=false`。这是核心行为、截图存在与数值探针通过，不等于视觉已完整复原。

探针 `(30,47)` 的证据：

| 状态 | visibility bits / direct sight | GPU patch亮度 | radar RGBA |
|---|---|---:|---|
| 初始未知 | 0 / false | 0 | `(4,8,6,255)` |
| 侦察进入 | 3 / true | 141.947089785621 | `(111,97,62,255)` |
| 侦察离开 | 1 / false | 141.947089785621 | `(111,97,62,255)` |
| 一次开图后敌方 Gap | 0 / false | 0 | `(4,8,6,255)` |
| 实际侦察进入 Gap | 3 / true | 75.4285714285714 | `(111,97,62,255)` |

离开 sight 后亮度和 radar RGBA 精确不变，证明不是只移除 tint；V3 在13格距离、18格武器范围、7格 sight 下真实 `Attack` 并开火，目标不在任何友军当前 LOS。另验证 Gap 十格仍黑、十一格保留探索、owner/ally 豁免；已供电卫星在 Gap 格只余来源位8而不显示；低电、真 Sell 和声明的 Health0 销毁、一次开图及潜艇/驱逐舰传感器场景都通过。

我实际用 read_image 看了[初始边缘](/storage/Users/currentUser/dev/redalert-godot-csharp/Reports/shroud-parity-20261022-162935-85178/01-original-shrouded-edges.png)、[离开后探索](/storage/Users/currentUser/dev/redalert-godot-csharp/Reports/shroud-parity-20261022-162935-85178/03-explored-after-depart.png)、[Gap 孔洞](/storage/Users/currentUser/dev/redalert-godot-csharp/Reports/shroud-parity-20261022-162935-85178/04-gap-hole-after-one-shot-reveal.png)。后两图清楚显示已探索侧有内黑带、黑幕侧有亮色锯齿。Root 同样确认此缺陷，因此**首轮视觉 FAIL，不据此 complete37**。不是覆盖或删除失败图；所有原证据保留。

已在 Root 新许可的渲染范围改为未知格＋已知邻居，并补仅 ground 的一格 fringe，World 核心与21项检查源码继续冻结。Godot4.7 的 `DrawPolygon` 显式 `Color[]` / `Vector2[]` 编译限定保留。修改通过 `git diff --check`；此极性修正随后由 Root 合并矿物 palette 修正统一构建为 alpha13，结果如下。精确 native alpha 数值 LUT 和低电记忆范围仍未因本项目运行通过而被追认为原版已证。

## alpha13：极性改善已实看，未知立面仍待修后复验

[alpha13 acceptance.json](/storage/Users/currentUser/dev/redalert-godot-csharp/Reports/shroud-parity-20261022-170735-480552/acceptance.json) 再次 `passed=true`、21个条件、5张截图、`missingConditions=[]`、`error=""`，938 step、约1.885秒，恢复处理并回到菜单。全部5张的 `maskPlacement=unknown-cell-known-neighbors`，`unexploredResourceCellsDrawn=0`、`unexploredActorTerrainTilesQueued=0`；未知地面 fringe 分别为41/68/68/67/76格。

Root 与我均实际 read_image 查看了新[离开后探索03](/storage/Users/currentUser/dev/redalert-godot-csharp/Reports/shroud-parity-20261022-170735-480552/03-explored-after-depart.png)和新[Gap 孔洞04](/storage/Users/currentUser/dev/redalert-godot-csharp/Reports/shroud-parity-20261022-170735-480552/04-gap-hole-after-one-shot-reveal.png)。旧内黑带与外侧亮牙消失，原透明侧朝已探索地，外向柔边明显改善；黄金矿色也已恢复。但04左侧约 `(80,790)→(270,925)` 有灰长立面、底部约 `(750,1130)` 有灰色L形立面，03底部有小灰楔，因此仍不 complete37。

源码确认生成图 fringe 虽然排除了 ore/rock actor，`QueueLandscapeTile` 仍为其生成 `TryLandscapeFace` 两侧几何；shroud 只贴地表 top diamond，长立面会越过该遮罩。Root 授权的小修已落盘并再次冻结：

- [Battlefield.cs](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/Battlefield.cs) 仅 fringe 传 `groundOnly:true`；[Landscape](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/Battlefield.Landscape.cs) 默认 false 保留已探索几何，true 不生成竖直 face。
- [原图 ground pass](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/Battlefield.OriginalMaps.cs) 仅 fringe 关闭 extra pixels 并不计 cliff；[原 TMP 绘制函数](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/ArtLibrary.OriginalMapTerrain.cs) 仅增加默认 true 的 `includeExtra` 开关，false 跳过 `layer.Extra`，其它绘制功能与 metadata 不动。
- [Shroud render audit](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/Battlefield.Shroud.cs) 新增 `unexploredVerticalFacesQueued`，最终应为0，以及 `fringeOriginalExtrasEnabled=false`。原两项资源/actor地形泄漏计数继续保留。

World 核心、21条件检查源码、原素材、ore/bridge/foreground pass、所有已探索格几何保持原样；Godot显式数组编译修正保留。最终小修已过 `git diff --check`，本分工未自行构建或启动 native；随后 Root 合并43入口，统一 build/restart 并完成下面的最终复验。alpha12/13失败证据均保留，不以数值PASS替代视觉检查。

## 最终复验：当前目标完成，保留高度边缘限制

Root 的最终统一构建为 **0 warnings、0 errors**。[最终 acceptance.json](/storage/Users/currentUser/dev/redalert-godot-csharp/Reports/shroud-parity-20261022-172458-36707/acceptance.json) 为 `passed=true`、`checkCount=21`、`captureCount=5`、`missingConditions=[]`、`error=""`、`errors=[]`，938 World step、约2.565秒；`processingRestored=true`、`returnedToDeployment=true`、`running=false`。原21条件检查源码和 World 核心未因两次视觉修正而改变。

全部5张真实GPU截图同时记录：

| 审计 | 每张结果 |
|---|---|
| `unexploredResourceCellsDrawn` | 0 |
| `unexploredActorTerrainTilesQueued` | 0 |
| `unexploredVerticalFacesQueued` | 0 |
| `knownTerrainDarkened` | 0 |
| `fogLayerDrawn` | false |
| `fringeOriginalExtrasEnabled` | false |
| `maskPlacement` | unknown-cell-known-neighbors |

我与 Root 均实际 read_image 查看最终[离开后探索03](/storage/Users/currentUser/dev/redalert-godot-csharp/Reports/shroud-parity-20261022-172458-36707/03-explored-after-depart.png)和[Gap 孔洞04](/storage/Users/currentUser/dev/redalert-godot-csharp/Reports/shroud-parity-20261022-172458-36707/04-gap-hole-after-one-shot-reveal.png)。03底部灰楔已经消失；04左侧旧灰色长墙和底部L形灰壁已消失，没有再发现未知资源、目标地形或竖直几何显露。外向原始渐变保持正确，普通探索地面继续明亮，Gap 仍只在其影响区域重黑。因此完成 Task37 的默认永久探索、特殊遮蔽保留与原始详细边缘目标，源码和资产继续冻结。

**仍保留的视觉边界：**04左侧约x80–270与底部约x700–850的高度断层，改为黑色侧面/窄小缺口；这是当前只为未知格补top ground、不补未知立面的保守遮蔽表现。它没有完全解决所有高度断层的像素衔接，不能宣称逐像素原版一致。精确 native alpha LUT 和卫星低电记忆规则也仍未被本项目通过的检查证明。五张37截图均使用未改的生成地图，`originalGroundFringeTiles=0`，因此不把这组图称作全部原图 TMP extra 路径的 GPU 覆盖；该路径关闭extra的修改已经统一编译，后续原图场景由 Root 的其它验收覆盖。开发者 fixture、未测正常生产等界限继续见原始 JSON；本报告不把这组短验收当成正常自主AI完整胜负实战。
