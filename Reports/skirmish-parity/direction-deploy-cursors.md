# 方向与基地车部署光标（task-33）

方向、受阻方向、有效部署与无效部署已接入现有原版软件指针路径；已补上选中己方基地车后再次左键点击自身的部署动作。Root 集中构建已通过，0 warnings、0 errors；原生短验收实际通过 17/17 项检查并生成 13 组截图，累计 191 ticks、8 accepted、0 rejected。最终证据见 [acceptance.json](/storage/Users/currentUser/dev/redalert-godot-csharp/Reports/direction-deploy-20261022-151506-49770/acceptance.json)。

## 450 帧原始数据与动作映射证据

[原始 mouse.shp](/storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Reference/Cursors/Original/mouse.shp) 的头为 55×43、450 帧；SHA256 为 `e5a356737787d681dd1a2b1255c7c7d1e9bd8334e5674705f2ccc39cd12634df`。[原始 mousepal.pal](/storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Reference/Cursors/Original/mousepal.pal) 为 768 字节，SHA256 为 `4dcbe78b534a58fc972bce4b25aa58a2a56c9a5bb0ffbfcf5d6194be5dbde60c`。450 帧已存在于完整原生画布的 [atlas](/storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Generated/Cursors/atlas.png)，本次只更新序列元数据，没有重绘、缩放、改色或重新输出像素文件。

动作名称、帧起点、数量、interval 与符号对齐转录自 [MouseCursors 默认数据表](http://ares-developers.github.io/Ares-docs/_downloads/MouseCursors.txt)，字段含义见 [Mouse Cursors 文档](https://ares-developers.github.io/Ares-docs/new/mousecursors.html)。该文档针对 YR，不能把它当作 RA2 可执行文件的行为证明；本次使用的所有帧均位于原版 RA2 的 0–449 范围，并与本地原始 SHP 解码像素核对。通过 read_image 查看了完整 atlas 以及 [NE 原帧 003](/storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Generated/Cursors/Frames/003.png)、[受阻 NE 原帧 011](/storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Generated/Cursors/Frames/011.png)、[部署原帧 110](/storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Generated/Cursors/Frames/110.png)、[无效部署原帧 119](/storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Generated/Cursors/Frames/119.png)，确认绿色方向、原生红色禁止符号及金色部署图形，未自行合成禁止标志。

| 状态 | nativeName | 有效帧 | 受阻帧 | 55×43 画布 hotspot |
|---|---|---:|---:|---:|
| 北 | MoveN / NoMoveN | 2 | 10 | (27,0) |
| 东北 | MoveNE / NoMoveNE | 3 | 11 | (54,0) |
| 东 | MoveE / NoMoveE | 4 | 12 | (54,21) |
| 东南 | MoveSE / NoMoveSE | 5 | 13 | (54,42) |
| 南 | MoveS / NoMoveS | 6 | 14 | (27,42) |
| 西南 | MoveSW / NoMoveSW | 7 | 15 | (0,42) |
| 西 | MoveW / NoMoveW | 8 | 16 | (0,21) |
| 西北 | MoveNW / NoMoveNW | 9 | 17 | (0,0) |
| 展开基地车 | Deploy / NoDeploy | 110–118 | 119 | (27,21) |

方向帧为单帧、interval=0。Deploy 为 9 帧、interval=4；4/60 秒是沿用现有渲染时钟的明确换算，原 SHP 不包含秒数。文档只证明 Left/Center/Right 与 Top/Middle/Bottom 符号，不证明 native 的 Right/Bottom 数字偏移。本实现明确采用零起始像素索引约定：x=0/27/54、y=0/21/42；54/42 不是从 SHP 读取的数值，也未声称已反编译验证其 native 数字。完整画布在 pointer-hotspot 处绘制，sourceRect 已烘焙进画布，不能再次裁剪或补偏移。

[生成脚本](/storage/Users/currentUser/dev/redalert-godot-csharp/Tools/classic_art/prepare_cursors.py) 新增 `--catalog-only`，使新增动作无需改动 450 个 PNG、索引文件和 atlas。完整 prepare 路径与 metadata-only 路径共用同一个序列定义函数。

本任务实际执行：

```text
PYTHONDONTWRITEBYTECODE=1 python3 Tools/classic_art/prepare_cursors.py --catalog-only --validate
```

退出码 0；[验证记录](/storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Generated/Cursors/validation.json) 确认 450/450 帧完整画布像素与原始索引一致、23 个序列完整存在且范围/对齐/interval 一致。[目录](/storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Generated/Cursors/catalog.json) 保存每帧来源矩形、原始索引 SHA256、atlas 区域及所有序列信息。此数据校验步骤未启动 Godot 或构建 native；Root 随后完成了统一构建和原生验收，结果见文末。

## 光标条件与实际输入

[窗口输入选择器](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/Main.WindowInput.cs) 现在先处理整个客户端外缘，再处理战场动作。右缘始终是侧栏最外侧；战场与侧栏接缝保持普通内区。开关、窗口焦点、菜单/选项、聊天输入及雷达拖拽均与现有相机滚动条件一致。菜单继续 arrow，维护模式在普通战场区域优先于部署。外缘优先显示方向或受阻方向。

[战场光标 partial](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/Battlefield.Cursors.cs) 的 `CanScrollScreen` 使用与 PanScreen 相同的屏幕到网格转换和 ClampCameraCell，只查询、不移动相机。斜向只被限制一轴时仍可沿另一轴移动，因此仅在实际位移为零时选择受阻帧。

MCV 悬停复用现有 Hit 命中，不用鼠标所在格或另造贴图包围框。要求命中的己方存活 MCV 已被选中，且只有一个己方单位 actor；选区中的陈旧 ID、建筑或敌方项不会变成部署 actor。Shift/Ctrl/Alt、观战、维护、放置、支援、进攻工具与外缘滚动均不会抢成部署。

`CanDeployMcv` 无修改地对齐当前核心：

- 用 MCV 的 OriginCountry 查地基；起点为 `CellX-(Width-1)/2, CellY-(Height-1)/2`，保留偶数地基的北侧中央格 pivot。
- 复用 Map.FootprintFree 的真实地形、资源、原图障碍和格子合法性，不套用普通建筑的排队、资金、可见性或相邻建造限制。
- 排除 MCV 自身后，以核心相同的 long 定点、半径和含相切 AABB 检查所有地面单位；不因所属方或可见性漏掉阻挡者。
- 不减 EntityCount；核心临时置死 MCV 时尚未从单位列表移除。查询不临时改 Health、位置或地图。

[战场输入](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/Battlefield.cs) 只改了后段输入分支。首次左键仍只选择；选中后再次左键自身，在双击同类选择分支之前，经 `Main.Issue(Deploy)` 部署。对应 release 被消费，不清空选区，也不额外播放选择音。无效部署点击保留选区并提示，不发命令。右键自身部署复用同一判断；进攻移动模式不再误触自身部署。D 的正常命令路径保留，仍可展开 MCV、收起建造厂及部署其它支持单位。没有改前段 DrawUnit、Terrain、ArtLibrary 或核心模拟文件。

## Root 的短原生验收入口

独占新增 [Main.DirectionDeployChecks.cs](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/Main.DirectionDeployChecks.cs)：

```csharp
RunDirectionDeployChecks();
GetDirectionDeployAudit();
```

入口通过实际 Viewport.PushInput、正常 Main.Issue 与真实 GPU FramePostDraw 工作，不运行旧长 suite。本次已实际完成 17 项检查和 13 组 PNG/JSON，输出根目录按 `Reports/direction-deploy-<UTC>-<ticks>/` 命名。每张图使用独立标签子目录，避免同名 arrow 等覆盖先前证据。

1. 八方向各渲染一次，断言当前 desired key、实际 library key/frame、hotspot、overlay 可见和完整 450 帧来源；包含侧栏最右外缘。
2. 接缝为 arrow；八个相机极限逐一检查受阻 key，另保存受阻东向 frame12 PNG；只限一轴的东北方向仍可移动。
3. 首次左键只选中 MCV，不排 Deploy，不改变世界哈希；重复查询不改世界哈希或相机；渲染有效部署 110–118。
4. 维护工具覆盖部署，菜单释放 battle pointer 并返回 arrow。
5. 使用正常 Move 把原始开局 MCV 驶到地图现有矿格，不改地形或单位位置；渲染 119，点击无效部署时不发包且保留选区。
6. 正常 Move 返回空地，实际左键恰好排一个 Deploy；等待核心正常 stepping 生成并完成建造厂，保存 PNG。
7. 实际键盘 D 收起建造厂，再 D 展开新的 MCV，确认旧快捷键路径保留。

序列截图阶段使用既有 inspection 暂停模拟；需要移动/部署时恢复正常 Main._Process。没有直接生成实体、缩短建造时长、填充资金或修改地图。finally 恢复 edge preference/fast，返回菜单，确认没有遗留 inspection、运动或鼠标捕获。

现有软件指针/鼠标捕获策略未改；该原生入口明确面向 OpenHarmony 无 MouseWarp 的既有软件 overlay 路径。MouseWarp 平台的原生 Confined 指针绘制策略不在本次变更内。实现分工没有自行构建或启动原生游戏；下面的实际运行由 Root 在统一构建后执行。

## 最终原生结果与证据

Root 集中构建结果为 **0 warnings、0 errors**。实际调用 `RunDirectionDeployChecks()` 后，[汇总记录](/storage/Users/currentUser/dev/redalert-godot-csharp/Reports/direction-deploy-20261022-151506-49770/acceptance.json) 为 `passed=true`、`checkCount=17`、`captureCount=13`、`error=""`。最终世界为 191 ticks、8 accepted、0 rejected，状态 hash 为 `60484ef7e3ee4c04`；`directEntitySpawns=0`、`oldLongSuitesRun=false`。清理结果 `returnedToDeployment=true`、`running=false`，已返回菜单。

关键真实 GPU 图像：

- [侧栏最外侧向右滚动](/storage/Users/currentUser/dev/redalert-godot-csharp/Reports/direction-deploy-20261022-151506-49770/edge-e/scroll_e.png)：Root 已 read_image 确认原版绿色向右箭头出现在侧栏最外缘。
- [战场与侧栏接缝](/storage/Users/currentUser/dev/redalert-godot-csharp/Reports/direction-deploy-20261022-151506-49770/sidebar-seam/arrow.png)：普通 arrow，不把内接缝当作滚动外缘。
- [相机右侧限位](/storage/Users/currentUser/dev/redalert-godot-csharp/Reports/direction-deploy-20261022-151506-49770/right-limit/scroll_e_blocked.png)：NoMoveE 原帧 12。
- [选中 MCV 的部署悬停](/storage/Users/currentUser/dev/redalert-godot-csharp/Reports/direction-deploy-20261022-151506-49770/mcv-deploy/deploy.png)：Root 已 read_image 确认星状原帧 110 悬停图标。
- [矿格上的无效部署](/storage/Users/currentUser/dev/redalert-godot-csharp/Reports/direction-deploy-20261022-151506-49770/mcv-no-deploy/deploy_invalid.png)：NoDeploy 原帧 119，实际无效点击保留选区并且不发 Deploy。
- [实际左键展开完成后的建造厂](/storage/Users/currentUser/dev/redalert-godot-csharp/Reports/direction-deploy-20261022-151506-49770/left-click-yard/arrow.png)：通过正常命令与真实模拟 stepping 完成；后续 D 收起及重新展开检查同样通过。

全部八方向的当前 key/frame/hotspot、真实截图路径、13 次绘制帧号与窗口输入审计均保存在 [完整验收记录](/storage/Users/currentUser/dev/redalert-godot-csharp/Reports/direction-deploy-20261022-151506-49770/acceptance.json)。本轮收尾只更新此 Markdown，没有再修改源码或资产。
