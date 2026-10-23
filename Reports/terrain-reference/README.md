# 原版地图、水岸和桥梁素材核对

本次只提取用户已授权使用的原版数据和像素，没有下载、复制或依赖原版游戏实现；未构建、启动游戏。以下实际查看的是公开地图分发包中的预览和本地解出的原始图像，不是本项目运行效果。

## 先看真实地图

已下载并实际查看 CnCNet 分发的温带 [Malibu Cliffs LE 地图预览](</storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Reference/Terrain/Maps/2_malibu_cliffs_le.png>) 和 [地图数据](</storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Reference/Terrain/Maps/2_malibu_cliffs_le.map>)。该地图 `[Map]` 明确是 `Theater=TEMPERATE`，尺寸 `101×128`，可视区域 `2,4,97,118`。预览中能直接看到两座低木桥、普通沙草湿岸，以及仅在真正高地边缘出现的岩壁。

![实际 CnCNet Malibu Cliffs LE 预览](</storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Reference/Terrain/Maps/2_malibu_cliffs_le.png>)

另保留 [May Day 地图预览](</storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Reference/Terrain/Maps/2_mayday_oils_fixed.png>) 和 [地图数据](</storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Reference/Terrain/Maps/2_mayday_oils_fixed.map>) 作为雪地水岸对照。它不能作为温带调色板依据。

原始地图和图像来自 [CnCNet/cncnet-yr-client-package 固定提交](https://github.com/CnCNet/cncnet-yr-client-package/tree/5beccf00ef46b75ca198a5f06ecbdaf260ea778a/package/Maps)。各文件 URL、字节数和 SHA256 见 [地图来源记录](</storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Reference/Terrain/Maps/sources.json>)。没有把预览误称为实际游戏截图，也没有把尚未解码的 IsoMapPack5 当成已读取的单元高度。

## 原始素材给出的区别

| 类型 | 原始数据及实际图像 | 展示含义 |
| --- | --- | --- |
| 普通水岸 | Shore01…42，完整 2×2 模板，检查的模板每格 `height=0,rampType=0`，原水面也在 level 0 | 湿岸和干岸像素属于同一平面过渡，不应因 Grass/Water 枚举变化就加一面竖墙。 |
| 浅滩 | WaterBridge 对应 `wbrdge01/02`，分别 2×4 / 4×2，所有 8 格 `height=0,rampType=0` | 实际图像是沙岸间水中铺石的涉水通道，不是带护栏的木桥。 |
| 低木桥 | `lobrdg01…28.tem` 实为 SHP，来自 isotemp.mix；180×120 原生 canvas | 木板、横梁、边梁已经在原图中。一张图是跨三格宽的一条桥面，不能给三条车道重复画三次。 |
| 高架桥 | Ovrps01…16；例如 Ovrps07 同时有 `height=4` 甲板和低处图块、较大 extra 图层 | 带高桥墩、桥头和原始高度差，不能直接贴到当前 level-0 低桥上。 |
| 真正高地岩壁 | Cliff/WCliff TMP 及其 extra 层 | 仅在真实地形高度差处使用；不能拿它代替全部普通岸线。 |

![原版浅滩，不是护栏低桥](</storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Generated/Terrain/OriginalStructures/Templates/wbrdge01.png>)

![原版高架桥段，不能强贴到低桥](</storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Generated/Terrain/OriginalStructures/Templates/ovrps07.png>)

![原版完整低木桥条带](</storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Generated/Terrain/OriginalStructures/Templates/lobrdg01-001.png>)

![原版另一方向的完整低木桥条带](</storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Generated/Terrain/OriginalStructures/Templates/lobrdg12-001.png>)

本项目统一基准已由 Lead 决定为干地 0、水面 0、低桥甲板/桥岸 0、高地 2、两排坡 0→1→2。此素材任务没有修改 Simulation。普通水岸共面符合原模板的使用方式；本次不声称整个地图的所有海岸单元必须是绝对高度 0。

## 可直接读取的素材接口

运行时读取 [图集清单](</storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Generated/Terrain/OriginalStructures/catalog.json>) 和 [单张原像素图集](</storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Generated/Terrain/OriginalStructures/atlas.png>)。目前 129 个模板、704 个图层（含 12 个近侧木材原像素遮挡 mask），图集 2048×1384；没有旋转、缩放、生成或重绘源图像。

`templates[]` 以 `id`（如 `shore18`、`lobrdg01`）查找，包含 `sourcePath`、`sourceSha256`、`manifestPath`、`format` 和 `frames[]`。

TMP 的每帧保留：

- `index`、`cellX/Y`、原始有符号 `offset`、`gridOffset`、`heightAdjustedOffset`。
- 原始 `height`、`rampType`、`terrainType`、flags 及 extra/Z 偏移。
- `layers[]` 分开保存 `diamond` 和 `extra`，每层有原始 `size`、`offset`、`offsetFromDiamond`、原索引和 Z 文件路径、主图集 `region`。
- 60×30 diamond 未压入 extra。原始 Z 字节保留，没有被冒充为已验证的世界高度。
- 整模板原生拼接预览的 `assembledBounds` 保留含负数的边界；`originInAssembled=(-minX,-minY)`。
- 对世界 tile 左上顶点 `Project(x,y)`，`drawOffsetFromTileOrigin=(minX-30,minY)`。原图高度单位每层 15 像素，没有偷偷折成项目原先的 8 像素。

SHP 的每帧保留：

- 原生 `canvasWidth/Height`，`sourceRect`、`centerOffset`、`dataOffset`、原像素索引 hash 和主图集 `region`。
- `layers[].kind="sprite"`，`offset` 是原 sourceRect 左上，完全独立于图集装箱位置。
- 非选择图保留 canvas 中心约定 `(canvasWidth/2,canvasHeight/2-15)`。对已选择的低桥，原约定 `(90,45)` 另存 `nativeCanvasCenterAnchor`；`pixelFootAnchorFromTileOrigin` 采用实图顶面配准的派生值 `(90,35)`。例如 X 轴原图顶面四角约 `(120,20),(150,35),(60,80),(30,65)`，减去 `(90,35)` 后恰与 level-0 三格宽、一格长的世界甲板投影吻合。该配准避免单位脚点比木桥面低约 10 像素；它没有被冒称为原版引擎偏移。`deckPlaneCanvasQuad` 和 `anchorConvention` 明确保留依据，仍需 Lead 的实际原生画面核验桥头。绘制位置使用 `Project(tileOrigin)+sourceRect.Position-anchor`。
- LOBRDG01…26 通常只有 frame 1 有图，LOBRDG24 是 frame 2，27/28 全空；LOBRDGE1…4 只有 frame 0。空帧不能用作默认帧。
- 原图中的桥边梁保留；断桥、橙绿入口占位图没有被误标成完整桥面。12 个选择图的 `foregroundLayer` 仅包含近侧边梁及桥面以下木材的原像素，供单位绘制后按深度遮挡，排除整块 road-top，避免恢复“地面盖住坦克”问题。源整条 sprite 仍原样保留；遮挡 mask 没有生成新栏杆。

## 真实地图覆盖层确认的选择

Effects teammate 独立解码 Malibu Cliffs 的 `OverlayPack` 和 `OverlayDataPack`，每包 32 块、解出 262144 字节。`OverlayTypes` 必须按 INI 项出现顺序从零编号，不能拿 INI 左侧数字键直接当资源 ID。结果固化在 [可复现素材选择表](</storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Reference/Terrain/selection.json>)，并进入 `catalog.selection`。

| 地图中的桥 | 真实覆盖层运行段 | 入口 | 完好中段变体 | 出口 |
| --- | --- | --- | --- | --- |
| X 轴，三格 Y 宽 | x=44…53，y=86…88 | LOBRDG21，Overlay ID 94 | LOBRDG01…04 | LOBRDG19，ID 92 |
| Y 轴，三格 X 宽 | x=149…151，y=118…128 | LOBRDG23，Overlay ID 96 | LOBRDG10…13 | LOBRDG25，ID 98 |

两桥的三个 crosslane 分别存 OverlayData 0、1、2；只在中间 crosslane 对应的 frame 1 有原图，因此必须每个纵向单元只画一条完整 180×120 原 canvas。X 放置在 `tile(x,bridgeOriginY+1)`；Y 放置在 `tile(bridgeOriginX+1,y)`。不能把一张三格宽图再逐车道重复，也不能默认空的 frame 0。该地图中另有只带 Data=0 的残留片段，没有当成有效整桥参考。

`catalog.selection.lowBridge.X/Y` 提供 `start`、`middle[]`、`end` 的模板与帧号、`crossLaneCount=3`、`drawCrossLane=1`，供 renderer 直接选择。原图桥头原生厚度、木材边梁和源锚点仍由图片决定，没有用数学灰板替代。

## 可复现与来源

[素材提取脚本](</storage/Users/currentUser/dev/redalert-godot-csharp/Tools/prepare_terrain_reference.py>) 用精确 HTTP Range 从项目此前允许的固定资产源提取相关文件。第一组 54 份 TMP 合计网络范围字节 1,054,396；第二组 32 份低桥图像及新增目录索引合计 134,622。不下载完整游戏包。

原素材保存在 [原始文件目录](</storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Reference/Terrain/Original>)；逐文件 SHA256、原始档案/嵌套档案、绝对偏移和 HTTP Range 见 [素材来源清单](</storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Reference/Terrain/sources.json>)。

TMP 复用项目已有的独立转换器二进制，直接调用转换命令，没有重新构建。低桥 SHP 的零 offset 空帧里有 compression=4 的无像素头，原严格转换器会拒绝；本脚本仅跳过空头的解压步骤，原输入不修改，有图 payload 均按 format-3 解码并保留索引 hash。该处理没有借用原版游戏实现。

[地图引用提取脚本](</storage/Users/currentUser/dev/redalert-godot-csharp/Tools/prepare_terrain_map_reference.py>) 只下载少量 `.map`/预览 PNG 和来源元数据。普通岸、低桥、高桥的用途先由原图实际确认，再提供给 renderer；没有仅凭资源名猜测用途。

最终只运行必要的资产一致性检查：129 个模板来源 SHA256 与 704 个图层的全部非透明 RGBA/alpha 均与源图精确匹配，12 个前景 mask 保留原像素并排除桥面内部。结果见 [检查记录](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/terrain-reference/validation.json>)。构建次数和游戏启动次数均为 0；原生游戏展示验证由 Lead 执行。
