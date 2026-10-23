# RA2 TerrainPipeline

独立纯 C# / .NET 10 console 工具，读取 RA2 TMP 地表模板，导出每个非空子块的 **60×30 基础 diamond RGBA PNG**、原始索引/Z 数据及 JSON manifest。有 extra image 时默认额外导出独立图层，不把悬崖、突出物等 extra 压进基础 diamond。

只使用 .NET BCL，不引用 Godot SDK，不需要 NuGet 包。项目通过 `Compile Link` 复用相邻 AssetPipeline 的 `PngWriter.cs` / `Palette.cs` 源文件，不修改原文件，也不引用 AssetPipeline 项目。`SharedGuard.cs` 只为这两个文件提供原有的 `ShpTs.Require` 校验入口，没有引入 SHP 解析代码。

## 构建与自测

在仓库根目录执行：

```sh
zsh Tools/TerrainPipeline/build.zsh
zsh Tools/TerrainPipeline/build.zsh --self-test
```

构建脚本需要有效且可写的 `XDG_CACHE_HOME` 与 `TMPDIR`。它实际调用 `dotnet build`，显式使用本目录的无源 NuGet 配置，默认产物在：

```text
$XDG_CACHE_HOME/redalert-godot-csharp/terrain-pipeline/bin/TerrainPipeline/debug/Ra2TerrainPipeline.dll
```

也可直接执行：

```sh
dotnet build Tools/TerrainPipeline/TerrainPipeline.csproj \
  --disable-build-servers -p:NuGetAudit=false \
  -p:RestoreConfigFile="$PWD/Tools/TerrainPipeline/NuGet.Config"

dotnet "$XDG_CACHE_HOME/redalert-godot-csharp/terrain-pipeline/bin/TerrainPipeline/debug/Ra2TerrainPipeline.dll" --self-test
```

`--self-test` 在 `TMPDIR` 创建唯一测试目录，并在 `finally` 中校验完整目标路径后清理。它包括：52 字节字段布局、900 索引排列与空白末行、空表项和全空文件、乱序/复用表项、每个被截断的合成 fixture 前缀、非法尺寸/相对及绝对偏移/跨块读取/预算、PAL 深度、原索引 SHA256、单独 extra/extra-Z、PNG CRC 与实际 zlib 解压像素、manifest 字段和非空输出目录保护。自测是独立生成的小型二进制 fixture，不包含游戏素材。

## 转换

目标命令：

```sh
dotnet "$XDG_CACHE_HOME/redalert-godot-csharp/terrain-pipeline/bin/TerrainPipeline/debug/Ra2TerrainPipeline.dll" \
  convert --tmp /path/to/clear01.tem --pal /path/to/isotem.pal --out /path/to/new-or-emptydir
```

也可通过构建脚本运行同一命令：

```sh
zsh Tools/TerrainPipeline/build.zsh convert \
  --tmp /path/to/clear01.tem --pal /path/to/isotem.pal --out /path/to/new-or-emptydir
```

参数：

- `--tmp`：单个 TMP 文件。文件扩展名不参与判定，`.tem/.sno/.urb/.des/.lun/.ubn` 等同名扩展也可能是 SHP；本工具只接受 TMP 60×30 布局。
- `--pal`：匹配 theater 的等距地表 PAL，例如 `isotem.pal`。768 字节、256 RGB 颜色。默认 6-bit VGA 分量，展开为 `channel << 2`，63 对应 252。错误的 theater PAL 可能产生错误颜色，工具无法从 TMP 内自动识别 theater。
- `--out`：不存在或完全空的目录，拒绝覆写文件；也拒绝把直接符号链接用作输出目录。全部输入先解析与校验，然后才创建输出。
- `--palette-depth 8`：显式允许 RGB8 PAL；默认 6。不会自动猜测。
- `--no-extra`：不输出 extra 图像/二进制，但 manifest 仍保留其尺寸、坐标、索引 hash 和 Z hash。

转换/输入错误返回退出码 1；成功返回 0。I/O 在输出过程中失败时，目录可能留下已完成的部分文件，应选另一个空目录重试。CLI 不递归清理用户输出目录。

## 输出与 manifest

以表项 0 为例：

| 输出 | 内容 |
| --- | --- |
| `tile-0000.diamond.png` | 固定 60×30、RGBA8、最近邻原像素。diamond 外和最后一行透明黑；900 个实际编码像素全部不透明，包括源索引 0。 |
| `tile-0000.diamond.indices.bin` | 从 TMP 原样提取的 900 个索引，保持源文件顺序，无填充、无调色板转换。 |
| `tile-0000.diamond.z.bin` | 原样提取的 900 个基础 Z 字节，保持 diamond 编码顺序。 |
| `tile-0000.extra.png` | 有 extra 时输出其原尺寸矩形 RGBA8；索引 0 透明，其他索引不透明。与基础 diamond 明确分层。 |
| `tile-0000.extra.indices.bin` | extra 原始 `width*height` 个索引。 |
| `tile-0000.extra.z.bin` | 确定或可推断存在的 extra Z 矩形；推断会在 warnings 标记。 |
| `manifest.json` | source/palette SHA256、模板尺寸、空表项、全部非空帧、格式来源和坐标/透明度约定。 |

表项编号是 **0 开始的 TMP 子块索引**，不是 theater 的全局 tile-set ID；空表项保存在 `template.emptyIndices`，不会输出 PNG。相同文件偏移的合法复用表项仍按其各自表索引导出，解析数据只分配一次。

`frames[]` 的主要字段：

- `index`、`cellX`、`cellY`、`sourceOffset`：源偏移表索引、二维表格位置、源文件绝对块头偏移。
- `offset`：块头原始有符号 `(x,y)`；相对整个 TMP 模板的图像左上角，不做中心偏移、缩放或裁剪。
- `gridOffset`：偏移表格点隐含的等距像素坐标 `((cellX-cellY)*30, (cellX+cellY)*15)`，单独提供以免与原始字段混淆。
- `heightAdjustedOffset`：ra2ff 文档提供的模板摆放算法，保持 `x`，`y += (template.maxHeight - height)*15`。仅作为独立元数据，PNG 未被重采样。完整游戏地形渲染仍需要 theater 与地图规则。
- `height`、`terrainType`、`rampType`：块头的原始字节，不猜测中文地形名或路径/坡度语义。`terrainType` 就是单个块提供的地形类型索引；tile-set ID、terrain 名和 INI 规则映射不在独立 TMP 文件中。
- `flags` / `flagsHex`：保留完整 32-bit 标记。仅解释 bit 0（extra 存在），其余已知/未知位不用于推断数组大小。
- `extraOffset`、`zOffset`、`extraZOffset`：保留原始相对偏移字段。
- `diamond` / `extra`：`kind`、`size.width/height`、原始 `offset`、相对 diamond 的 `offsetFromDiamond`、输出文件名、`indicesSha256`、`indexCount`、`zSha256`。hash 都是小写十六进制 SHA256，针对原始压紧数据，而非 RGBA、PNG 或填满 60×30 的索引矩形。
- `radarLeft` / `radarRight` / `reserved`：保留原始字节；JSON 的 byte 数组按 BCL 默认编码为 Base64。
- `warnings`：逐块说明 extra 指针推断、缺少 extra-Z 或损伤变体未解析等不确定性。

基础 diamond 的索引 0 按不透明黑/调色板颜色保留，确保 PNG 的有效区域完整表示全部 900 个地面样本；extra 使用常见的索引 0 透明约定。若以后渲染器需要为基础层的索引 0 另设透明规则，可用保留下来的原索引转换，而不丢失信息。

## 实现边界与格式资料

参考公开格式说明，独立实现边界检查、几何映射、manifest 和 CLI；没有复制第三方游戏读取器代码，也没有下载整游戏。

- [tcrs/ra2ff 格式文档（固定提交）](https://github.com/tcrs/ra2ff/blob/3800e3636c43d2a8a7331b2d7dbcd888c215d6af/ra2ff.txt)：TMP 头/52-byte 块头、900 索引、extra 位置、height 调整、PAL6 规则。
- [OpenRA TMP TS 文件读取参考（固定提交）](https://github.com/OpenRA/OpenRA/blob/e180e3225f3930272dd451917467631618f8976f/OpenRA.Mods.Cnc/SpriteLoaders/TmpTSLoader.cs)：交叉核对 52-byte 块头、diamond 扫描线、逐块基础图/Z/extra/extra-Z 存放顺序。作为格式行为参考，不作为代码依赖。

检查公开资料时网络使用 `curl --proxy socks5h://127.0.0.1:10808`。以上 URL 同时写进每个 manifest。

已确定的读取布局：文件头为 little-endian `int32 blocksX, blocksY, tileWidth, tileHeight`，紧接 `uint32 offsets[blocksX*blocksY]`，0 表示空块。块头 52 字节；颜色 diamond 从块内偏移 52 开始，Z diamond 从 952 开始，两者各 900 字节。行宽是 `4,8,...,60,...,8,4,0`，最后一行空白。ra2ff 文档的扫描线伪码在中心转折处存在 off-by-one，若照抄会越过 60 宽；实现采用总量恰为 900 的对称几何并由自测校验。

不确定或受限部分明确保留：

- ra2ff 文档对 extra 指针及 extra-Z 语义有问号，两个参考读取器主要按连续布局读取。本工具将**非零 extra/extra-Z 指针按块头相对偏移**读，并验证不得与已知层重叠或跨越下一块；extra 指针为 0 时使用连续偏移 1852，同时输出 warning。
- extra-Z 指针为 0 时，若 extra 颜色矩形后至少还有一个完整同尺寸矩形，则保留为推断的 extra-Z 并 warning；没有后续数据时允许只有颜色层，并明确说明 depth 不可用；若只有不足一个矩形的后续字节则以 extra-Z 截断拒绝。不会把这些原始 Z 值当成已验证的世界高度。
- 只支持参考资料确认的 `zOffset == 952` 的 RA2 60×30 布局；其他布局清晰拒绝，不按猜测继续。受损图像变体不解码，flag bit 2 存在时 warning；未知高位原样保留。
- 每文件上限 256 MiB、偏移表最多 65,536 格、每个 extra 最多 16,777,216 像素。先验证偏移表，再按下一个不同非零块偏移限制当前块；拒绝整数溢出、越界尺寸、跨块/越界偏移和截断声明数据。不会根据未设置 extra 标记的无用尺寸/指针分配内存。
- 本工具不解析 MIX、地图包、theater INI、光照、调色板动画或道路连接规则，不会仅靠一个 TMP 猜出这些信息。

本机验证命令和真实样本转换结果见 `VALIDATION.md`。
