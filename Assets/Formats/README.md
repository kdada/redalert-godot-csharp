# 素材导入格式契约

`Tools/AssetPipeline` 是独立的 .NET 10 SHP(TS/RA2)+PAL 离线转换工具。这里定义它生成的 `manifest.json` schemaVersion 1。可机读 JSON Schema 见同目录 `shp-atlas.schema.json`。转换器的单元自测使用代码构造的数据；项目中另有来源可追溯的原版样本，见 `Assets/SOURCES.md`。

## 坐标与帧

所有坐标以像素为单位，X 向右，Y 向下。`canvasWidth` / `canvasHeight` 是 SHP 全局名义画布尺寸。每个 `frames[i]` 保留：

| 字段 | 约定 |
| --- | --- |
| `index` | SHP 中的原始顺序，0 起 |
| `sourceRect` | SHP 帧头 X/Y/Width/Height，无符号原值；相对名义画布左上角；可以超出画布 |
| `centerOffset` | `(x + width/2 - canvasWidth/2, y + height/2 - canvasHeight/2)`，允许半像素；用于中心为原点的裁剪精灵 |
| `compression` | SHP 编码 0、1、2、3 |
| `dataOffset` | 原始文件绝对字节偏移；0 表示无数据 |
| `encodedLength` | 本次解码实际读取的字节数；不包括未知填充 |
| `hasData` | 是否有非零数据指针；false 时应跳过渲染 |
| `reservedHeaderBase64` | 压缩标记字节之后、数据偏移之前的 11 字节，原样保留 |
| `role` | `unspecified`、`color` 或 `shadow` |
| `pairedFrame` | 明确提供 second-half 布局时对应的颜色/阴影帧序号；否则 null |
| `imageSet` | 顶层 `images` 数组中的索引 |
| `imageRect` | 该 image set 三张 PNG 中完全相同的裁剪框；不含格子填充；保留零尺寸 |
| `indicesSha256` | 逐行、无补齐的原始解码索引缓冲区 SHA256（小写十六进制）；空数据指针对应零缓冲区 |

裁剪尺寸保持原样，不因奇数宽高而补齐。空帧的 PNG 可为至少 1×1 的透明占位；必须以 `hasData` 和 `imageRect` 判断是否绘制。atlas 采用最大裁剪尺寸的固定格子；frames 为每帧一组图。不要把 atlas 格子尺寸当成帧本身尺寸。

在 Godot 中，可用 `imageRect` 创建 `AtlasTexture`，以 `centerOffset` 对齐到画布中心；每个 PNG 使用相同裁剪框即可同步颜色、索引和队伍色数据。实际世界坐标、单位脚点和方向/动画分组应由游戏自身素材定义提供；SHP 本身没有这些语义。

## 颜色、队伍色、阴影

`images[n].color` 指向 RGBA8 PNG。`indices` 和 `teamMask` 均为 PNG color type 0 / bit depth 8，必须作为数据纹理读取，禁止 sRGB 转换、线性插值、mipmap 和有损压缩。PNG 文件均不写入 gAMA/sRGB/tRNS 块。

- `indices` 的原始 8 位样本就是 SHP 调色板索引。
- `teamMask` 的 0 为非重着色像素；1–16 对应 16–31，原索引等于样本加 15。GPU 归一化采样先执行 `round(value * 255)`。
- `transparentIndex` 是 RGBA 输出采用的透明索引，默认 0。索引图保留该索引数值，队伍色 mask 仍按原始索引生成。
- `palette.componentBits=6` 的通道以左移两位展开（63→252）；8 位则原值。`palette.rawRgbBase64` 保存恰好 768 字节原始 PAL；`colors` 保存 256 项展开后的 `{r,g,b}`。
- SHP 无可靠阴影标志。`shadowLayout=unspecified` 时所有角色未知，颜色图按 PAL 展示；所有索引仍保留。`second-half` 时后半帧为 `shadow`，前后半一一配对；阴影非透明像素在 RGBA 中为黑色和 `shadowAlpha`，索引图保持原始数据。

若运行时需要自己的队伍色与阴影算法，可完全从索引图和元数据重建；RGBA 图仅是一次明确参数下的展示。阴影配对不是额外帧删除：原始 SHP 的全部帧都留在输出中。

## 可追溯性与兼容边界

顶层 `sourceSha256`、`palette.sha256` 标记原输入，文件名仅保存 basename。帧头保留字节、帧尺寸/偏移、调色板和原始索引均保留；本格式不保证 SHP 二进制逐字节往返，未保存帧间填充或未知尾部。schemaVersion 发生不兼容变更时递增。本 SHP 导出契约不覆盖 VXL/HVA、MIX、SHP(TD/RA1) 或 Godot 自动导入插件。VXL/HVA 离线烘焙由项目的独立 VoxelBaker 工具处理。
