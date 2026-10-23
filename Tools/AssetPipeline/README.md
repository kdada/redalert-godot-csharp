# SHP/PAL 素材转换工具

独立 .NET 10 控制台程序，适用于 HarmonyOS arm64；只用 BCL，不依赖 Godot、图形环境、外部命令、NuGet 包或第三方源码。支持 TS/RA2 的 SHP（帧编码 0、1、2、3）与 768 字节 PAL；PNG 由自写容器/CRC32 和 BCL `ZLibStream` 输出。输入为从 MIX 中提取的 SHP/PAL；本工具自身不下载素材。项目已取得的小体积原版样本及其来源见 `Assets/SOURCES.md`。

## 构建和自测

从仓库根目录执行（HarmonyOS 的 zsh，不依赖可执行位）：

```zsh
zsh Tools/AssetPipeline/build.zsh --self-test
```

脚本需要已设置 `XDG_CACHE_HOME`、`TMPDIR`，将 `obj`、二进制、NuGet 和 CLI 缓存全部置于 `XDG_CACHE_HOME/redalert-godot-csharp/`，不在项目中生成 `bin/obj`，不使用 `/tmp`。项目专用 `NuGet.Config` 清空源，构建还显式关闭 NuGet audit，因此 restore 不访问外源。仅依赖本地 .NET 10 SDK 的框架引用包。

不使用辅助脚本时，可等价构建：

```zsh
export DOTNET_CLI_HOME="${XDG_CACHE_HOME:?}/redalert-godot-csharp/dotnet-home"
export NUGET_PACKAGES="${XDG_CACHE_HOME:?}/redalert-godot-csharp/nuget"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_GENERATE_ASPNET_CERTIFICATE=false

dotnet build Tools/AssetPipeline/AssetPipeline.csproj \
  --artifacts-path "${XDG_CACHE_HOME:?}/redalert-godot-csharp/asset-pipeline" \
  --disable-build-servers \
  "-p:RestoreConfigFile=$PWD/Tools/AssetPipeline/NuGet.Config" \
  -p:NuGetAudit=false

dotnet "${XDG_CACHE_HOME:?}/redalert-godot-csharp/asset-pipeline/bin/AssetPipeline/debug/Ra2AssetPipeline.dll" --self-test
```

自测在 `TMPDIR` 下创建随机目录并在 `finally` 中核对路径、清理目录。fixture 完全由代码构造，覆盖原始/长度前缀/RLE 编码、奇数尺寸、非零偏移、空帧、队伍色范围、阴影配对、PAL6/PAL8、每帧/图集导出及元数据；用已知 CRC32 测试向量校验 CRC，再读回 PNG、核对块长度/CRC/zlib/每个有效索引像素。另有多 IDAT 块、越界指针、截断帧表/像素/压缩行、无效长度和 RLE、资源上限、CLI 参数及拒绝覆盖输出目录等检查。测试错误输入时会打印预期的 `error:` 行，最终 `SELF-TEST PASS` 且退出 0 才表示通过。

## CLI 用法

以下用 `tool` 代指：

```zsh
dotnet "${XDG_CACHE_HOME:?}/redalert-godot-csharp/asset-pipeline/bin/AssetPipeline/debug/Ra2AssetPipeline.dll"
```

仅检查 SHP（仍会完整校验并解码），或查看 PAL：

```text
tool inspect --shp unit.shp
tool inspect --pal unittem.pal
tool inspect --pal custom-rgb8.pal --palette-depth 8
```

导出图集，以及常见的“前半动画、后半阴影”布局：

```text
tool convert --shp unit.shp --pal unittem.pal --out exported-unit \
  --layout atlas --columns 8 --shadow-layout second-half --shadow-alpha 128
```

逐帧导出：

```text
tool convert --shp unit.shp --pal unittem.pal --out exported-frames --layout frames
```

也可直接让构建脚本在构建后运行命令：

```zsh
zsh Tools/AssetPipeline/build.zsh inspect --shp /absolute/path/unit.shp
```

参数：

| 参数 | 默认值 | 含义 |
| --- | --- | --- |
| `--shp` | 必填 | TS/RA2 SHP；不支持 TD/RA1 SHP |
| `--pal` | convert 必填 | 恰好 768 字节的 RGB PAL |
| `--out` | convert 必填 | 新目录或空目录；拒绝覆盖已有输出 |
| `--layout` | `atlas` | `atlas` 或 `frames` |
| `--columns` | 自动向上取整平方根 | 图集列数，1 至帧数；frames 模式禁用 |
| `--palette-depth` | `6` | `6` 或 `8`；6 位按 `channel << 2` 扩展为 0–252，保留原 PAL 字节 |
| `--shadow-layout` | `unspecified` | `second-half` 将前后半帧配对，要求总帧数为偶数；默认不猜测 |
| `--shadow-alpha` | `128` | 阴影非透明像素 alpha，0–255 |
| `--transparent-index` | `0` | RGBA 预览中的透明索引，0–255；原始索引始终保留 |

成功退出 0；格式、边界、参数或 I/O 错误退出 1，并输出 `error:`。`--self-test` 失败会抛出异常并以非零退出。无参数或 `--help` 显示帮助。

## 输出与运行时接口

每个 image set 包含三张 PNG：

- `atlas.png` 或 `frame-00000.png`：RGBA8，按 PAL 展示；显式阴影帧的非透明像素为黑色和指定 alpha。
- `*.indices.png`：PNG grayscale8 的原始 SHP 索引样本，0–255 不变。此图不带 `tRNS`；索引 0 也作为数据保留。
- `*.team-mask.png`：PNG grayscale8；0 表示非队伍色，1–16 对应原始索引 16–31，`index = sample + 15`。使用 GPU 采样时按线性数据纹理读取并恢复 `round(sample * 255)`；避免 sRGB 转换、双线性插值、mipmap 和有损压缩。

同一目录的 `manifest.json`（schemaVersion 1）保存画布尺寸、原始帧裁剪框 `sourceRect`、原点偏移、压缩类型/数据指针/实际读取字节数、11 字节保留帧头、阴影角色与配对、PNG 裁剪坐标、每帧索引 SHA256、完整 256 色 RGB 表、原 PAL base64 以及输入 SHA256。字段定义与消费约定见仓库的 `Assets/Formats/README.md` 和 `Assets/Formats/shp-atlas.schema.json`。

SHP 的 X/Y 按无符号 16 位原值保存。裁剪尺寸不补成偶数；`centerOffset` 可有 0.5 像素，用于“裁剪中心减画布中心”。帧可超出名义画布，仍保留真实裁剪框。图集使用最大裁剪宽高的均匀格子，不增加边距；`imageRect` 排除格子填充。空帧 `hasData=false`，输出透明区域；宽或高为零时逐帧 PNG 用至少 1×1 透明占位，元数据继续保留原零尺寸。渲染器应跳过 `hasData=false` 或零尺寸的帧。

阴影并非 SHP 中可靠的显式标志。默认角色为 `unspecified`，始终保留所有原始索引；只有提供 `second-half` 时才把后半帧解释为阴影并写出角色和配对。特殊动画若使用其他阴影布局，可在消费端根据自身定义解释索引。

## 读取限制与实现依据

输入上限 256 MiB；每帧最多 16,777,216 像素、全部解码帧总计 67,108,864 像素；单张输出图最多 16,777,216 像素。图集过大时改用 frames。SHP 帧数据被下一个不同的帧指针截断，避免从相邻帧借字节；允许共享数据指针。RLE 非透明像素不能超过声明宽度，允许省略末尾透明像素；兼容部分 TS/RA2 文件中最终透明 run 恰好多跳过 1 像素的写法，仅裁掉这一个声明框之外的透明像素，不跨行写入。拒绝非行尾或超过 1 像素的透明越界、悬空零标记、零长度 run、错误行长度。格式 2 的单个前缀（含前缀自身的两个字节）定义所有行的固定存储宽度，剩余列为 0。

源码独立编写，仅参考以下公开格式行为，未引入或改编第三方源码：

- [OpenRA ShpTSLoader：TS/RA2 帧头及编码语义](https://raw.githubusercontent.com/OpenRA/OpenRA/bleed/OpenRA.Mods.Common/SpriteLoaders/ShpTSLoader.cs)
- [OpenRA RLEZerosCompression：零值游程语义](https://raw.githubusercontent.com/OpenRA/OpenRA/bleed/OpenRA.Mods.Common/FileFormats/RLEZerosCompression.cs)
- [OpenRA VxlReader：已调研 VXL 结构](https://raw.githubusercontent.com/OpenRA/OpenRA/bleed/OpenRA.Mods.Cnc/FileFormats/VxlReader.cs)；本工具当前不导入或渲染 VXL/HVA，优先保证 SHP/PAL 转换与校验。
