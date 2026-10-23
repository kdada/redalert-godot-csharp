# 原版遭遇战地图导入与数据保真

已导入五张原始官方地图，采用真实 IsoMapPack5、OverlayPack、OverlayDataPack、Waypoints 和原版预览像素；不以 seed 重绘地图。地图数据和新增纯 C# 接口已交 Lead，原生运行和核心模拟集成由 Lead 负责。本任务没有运行 build 或启动游戏。

| MapId | 原版 CSF 名称 | 原 Size | runtime 矩阵 | 原起点数 | 矿石 / 宝石格 | 最大可航水域 | 岸边 3×3 船坞候选 |
|---|---|---|---|---:|---:|---:|---:|
| mp01t4 | South Pacific (2-4) | 105×95 | 199×199 | 4 | 318 / 251 | 7299 | 338 |
| mp02t2 | Isle of War (2) | 100×100 | 199×199 | 2 | 308 / 59 | 9106 | 228 |
| mp08t2 | Little Big Lake (2) | 40×80 | 119×119 | 2 | 255 / 51 | 670 | 91 |
| mp23t4 | Hammer and Sickle (2-4) | 97×103 | 199×199 | 4 | 853 / 118 | 3588 | 279 |
| mp25t6 | DEFCON 6 (2-6) | 106×114 | 219×219 | 6 | 915 / 265 | 0 | 0 |

南太平洋具有两大岛和大面积连通海域，满足海军与船坞地图的数据条件。船坞候选均是原始可航水格组成的平坦 3×3 区块，旁有可行走陆地。DEFCON 6 没有水格，且原文件明确将 GAYARD/NAYARD 的 TechLevel 设为 -1；该限制保留为 ShipyardsAllowed=false，不能把它擅改成海战图。

## 来源与既有 CnCNet 变体

原始数据来自[原资产仓库固定提交](https://github.com/toarrntnz/ts-redalert2-assets/tree/acfa5b834899e7fab4c8499ccb4b49c402927b44/game-res)。五个官方文件均取自原 multi.mix，具有 Official=yes，且与同档案 missions.pkt 中的官方记录一致。英文地图名从同档案 language.mix 的 ra2.csf 读取；不根据文件名猜测名称。temperat.ini、snow.ini、rules.ini 取自同一 ra2.mix/local.mix；本次取回的 rules.ini 与项目既有副本逐字节一致。来源提交、嵌套档案、原文件名、绝对偏移、HTTP Range、字节数和 SHA256 全部保存在[来源清单](/storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Reference/Maps/sources.json)。这里的“原版”指该固定档案中的未改原字节，不把 CnCNet 平衡版混入官方运行目录。

既有两个参考文件另与原始官方基图逐记录比较，完整结果见[变体对照](/storage/Users/currentUser/dev/redalert-godot-csharp/Reports/skirmish-parity/reference-map-comparison.json)：

| 参考变体 | 官方基图 | 实测原版程度 |
|---|---|---|
| Malibu Cliffs LE | mp17t6.map，原 6 人图 | 相同坐标的 25728 条地形记录中 18979 条相同，6749 条改变；地形记录保留比例 73.767879%。起点由原六处变为两处新位置；矿格 1173→1219，409 处矿坐标或密度改变；建筑记录 13→28。它是明显的平衡变体。 |
| Mayday Fixed Oils | mp31s2.map | 11537 条地形记录、全部 Overlay 类型/数据、253 处矿和两个起点均与原基图一致。非 Pack 部分的建筑、地物、光照和元数据存在差异，文件 SHA256 不同；属于保留全部基础几何的对象/显示修正版，不能称为官方未改字节。 |

比较用原图仅存于 Comparison，不加入本次五图运行 catalog。对照中“记录差异”以原字段为单位；对象重新编号也会影响按键比较，不能把每个不同键都解释成新增实体。

## JSON 与 C# 合同

入口是[地图目录](/storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Generated/Maps/catalog.json)和[数据接口](/storage/Users/currentUser/dev/redalert-godot-csharp/Simulation/ClassicMapData.cs)。

- ClassicMapCatalog.ResourcePath 指向 res://Assets/Generated/Maps/catalog.json。Parse(json) 返回含 Maps 的目录，Find(id) 返回 nullable ClassicMapInfo 引用。目录缺失、版本错误、数量不足或重复 ID 会失败，不回退为四个 seed 的同图。
- ClassicMapInfo 提供 Id、Name、DataPath、PreviewPath、Theater、MinPlayers、MaxPlayers、Width、Height、NavalSuitable、WaterFraction 和 SourceSha256。Seed 是对局随机数，不承担选图功能。
- ClassicMapDefinition.Parse(json) 提供 Width/Height、SourceOriginX/Y、SourceSize、LocalSize、PlayableBounds、RuntimeCells、Spawns、Bridges、Objects、MapRules 和 ShipyardsAllowed。
- RuntimeCells 只包含原地图有效记录；X/Y = SourceX/Y - SourceOriginX/Y，仅平移，绝不缩放到 96×96。矩阵缺口由 CellAt 返回 null，必须阻止行走/建造，不能填随机草地。模型允许负的 source origin，原虚空仍可留在矩阵包围框内。
- 原 TileIndex(int32)、SubTile、Level、IceGrowth、OverlayIndex、OverlayData 完整保存。TileSetId/Template 来自同版 theater INI 的累计 TilesInSet；TileIndex 不是现有生成 atlas 的索引。65535/-1 仅在 clear 图块查询时归一化，不修改保存的原字段。
- Ground/UnderlyingGround、LandPassable/WaterPassable、Buildable 是供当前模拟使用的派生值；原 TerrainType、RampType、TemplateHeight 保留以便核对。terrainType=9 为水，15 为岩壁，13 为平地，11/12 道路和 14 粗地保持可行走；真实墙和原地物阻挡另外保留。
- ResourceKind 0/1/2 表示无矿/矿石/宝石，ResourceDensity 是原 OverlayData 阶段值，ResourceValue 是原规则 Value（25/50）。阶段 0 不等于“没有覆盖层”；不得仅按 Density>0 丢弃矿格。矿量到经济容量的政策由模拟单独处理，不能因此重排矿区或强加两格留白。
- Objects 保存原 Terrain/Structures/Units/Infantry/Smudge/Aircraft 行；MapRules 保存原地图规则和元数据。它们是数据记录，导入器不执行原引擎行为。

每张预览 PNG 是原 PreviewPack 解出的原 RGB 像素。可直接按 PreviewPath 用 Godot 加载，由 Lead 统一导入资源；地图选择人数应取 MaxPlayers 和实际 Spawns，不硬塞八个起点。

## 地形、桥与坡面

[完整图块映射](/storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Generated/Maps/temperate-tiles.json)覆盖这五图用到的 331 个模板。183 个复用已验证原始文件，148 个新模板以范围请求取回完整原 TMP，共约 1.77 MB。每个模板保留原 TileSetId、文件名、原始路径与 hash，以及每个 SubTile 的原 header offset、offsetX/Y、terrain/ramp/height。effects 据此建立原像素 atlas，不再推造海岸 mask 来替代官方布局。

Level 始终是原 IsoMapPack5 字节。高架桥的桥头 Ovrps 道路单元已带甲板高度，例如战争岛的 Level=8；中跨 BRIDGE overlay 的底层原 Level=4。新增 SurfaceLevel 对中跨加上从原 Ovrps header 测得的 4 级高度，得到甲板 8；桥头不重复加高。镰刀锤子对应底面 1、甲板 5。陆行单位使用 SurfaceLevel，水面和桥下水层保留原 Level/UnderlyingGround。画原 TMP 时仍只按原 Level 投影一次 15px，不能又把 SurfaceLevel 或 TemplateHeight 加进原图。

typed Bridges 保留实际单元集合和 DeckLevel，不承诺包围矩形全部有桥。南太平洋原文件含零散 LOBRDG Data0/2 片段而没有完整 Data1 中心段；导入器保留原字节，不擅补整座木桥。高架中跨的原 SHP 由 effects 接入；已只读确认 ra2.mix/temperat.mix 中 bridge.tem 偏移 241699542、158344 字节，bridgb.tem 偏移 243768918、137928 字节。

坡面新增[角点接口](/storage/Users/currentUser/dev/redalert-godot-csharp/Simulation/ClassicMapRamps.cs)、[角点数据](/storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Generated/Maps/ramp-corners.json)和[数学证据](/storage/Users/currentUser/dev/redalert-godot-csharp/Reports/skirmish-parity/ramp-geometry.json)。ClassicMapRamps.Corners(type) 返回 NW=(0,0)、NE=(1,0)、SE=(1,1)、SW=(0,1) 的相对 level；加原 Level 后一律按原生 15px/级使用。13..16 包含二级坡形，不能压成四向枚举。

公开格式资料没有给出可验证的四角表；本表按原 slope01..20 非零材质像素轮廓、旋转族和官方地图共享边独立重建。所有基础 Z diamond 都与平地相同，逐行仅 29-y，因此不能把它当物理高度图。13 号面对相机视线呈边缘，仅四个非零材质像素，其相邻 1 级值来自 14..16 的旋转族推断，且本次五图没有使用 13。17/19 与 18/20 四角相同而内部鞍面不同；当前四角表不声称已证明内部三角剖分。原 RampType 和原像素继续保留，少量原地图坡边不连续也没有被“修平”。

## 独立格式实现与复现

仅复用原数据和原像素；LZO1X、LCW、MIX 索引读取、INI/CSF/PNG 等转换均使用项目自写工具或按公开格式独立实现，没有复制原版引擎实现。主要公开资料：

- [IsoMapPack5 格式说明](https://modenc2.markjfox.net/index.php?title=IsoMapPack5&oldid=12044)：Base64、分块、11 字节记录和末尾四零字节。
- [RA2 文件格式文字说明](https://raw.githubusercontent.com/tcrs/ra2ff/master/ra2ff.txt)：地图和 TMP 字段；其 rampType 语义标为未知，不能拿它作为四角表证据。
- [LZO 指令格式文字说明](https://docs.kernel.org/staging/lzo.html)与[历史版本文字说明](https://raw.githubusercontent.com/torvalds/linux/v4.15/Documentation/lzo.txt)：独立 LZO1X 指令解码。
- [Westwood LCW 格式说明](https://moddingwiki.shikadi.net/wiki/Westwood_LCW)：覆盖层分块解码。
- [ModEnc TMP 说明](https://modenc.renegadeprojects.com/TMP)：SlopeType 和深度层字段，不含数字四角表。
- [OpenRA 公开 tileset 数据](https://github.com/OpenRA/OpenRA/blob/7dc34b171a9dda58621393862ebdcda6008038e5/mods/ts/tilesets/temperate.yaml#L477)：仅用于交叉核对 slope01..20 对应 RampType1..20，未读取或复制其引擎实现。

```sh
python3 Tools/classic_maps/fetch.py --maps mp01t4.map mp02t2.map mp08t2.map mp23t4.map mp25t6.map
python3 Tools/classic_maps/fetch.py --metadata
python3 Tools/classic_maps/fetch.py --tiles
python3 Tools/classic_maps/convert.py
python3 Tools/classic_maps/validate.py
python3 Tools/classic_maps/compare_references.py
python3 Tools/classic_maps/infer_ramps.py
```

## 必要验证与集成边界

[最终数据验证](/storage/Users/currentUser/dev/redalert-godot-csharp/Reports/skirmish-parity/maps-validation.json)通过：160 个来源文件 hash、331 个完整 TMP hash；五图 90008 条 Iso 记录重新打包后与解码原字节 hash 一致；90008 处 Overlay 类型/数据逐坐标一致；18 个起点准确且位于可行走的原可玩区；3393 处原矿/宝石位置和阶段一致；五张预览 RGB 逐字节相同；三个截断/越界 LZO 样例被拒绝；typed bridge 的 SurfaceLevel 与 DeckLevel 一致。验证不把生成预览看起来相似当作地图布局保真的依据。

运行时还需 Lead 将真实 MapId、动态矩阵、有效起点数、原 Level/SurfaceLevel、passability、原矿与 MapRules 接到 World，并跳过随机地形、硬清起点及矿区重排。原中立对象的实际行为、海军桥下分层通行、单位脚点与原像素绘制属于核心模拟/美术集成验收；数据交付和 hash 验证不等于已经完成原生运行验证。本任务构建次数和启动次数均为 0。
