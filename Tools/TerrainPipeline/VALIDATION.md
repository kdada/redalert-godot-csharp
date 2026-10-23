# TerrainPipeline 本机验证

环境：HarmonyOS aarch64，.NET SDK 10.0.401 / Microsoft.NETCore.App 10.0.12。仅使用纯 net10 console，不加载 Godot SDK。

执行于仓库根目录：

```sh
zsh Tools/TerrainPipeline/build.zsh --self-test
```

实际结果：

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
PASS: 1975 format/export checks (including every truncated fixture prefix).
```

构建产物：

```text
/data/storage/el2/base/haps/entry/cache/redalert-godot-csharp/terrain-pipeline/bin/TerrainPipeline/debug/Ra2TerrainPipeline.dll
```

自测在 `$TMPDIR/terrain-selftest-<随机标识>/converted` 完成合成 TMP/PAL 的真实导出，再读取输出 PNG 并解压验证。临时目录已按验证过的绝对路径清理。主要断言包括原900字节索引的逐字节一致性及SHA256、PNG RGBA像素/CRC、extra独立图层、manifest地形索引与空表项、不覆写已有目录、每个截断前缀均被拒绝。合成样本不代表对全部游戏素材变体的覆盖。

另外通过实际 CLI 验证：未知参数、`--palette-depth 7`、以真实 SHP `Assets/Reference/Formats/ntclonmk.shp` 作为 TMP 输入均返回退出码 1；非法输入不会创建输出目录。SHP 的错误为 `Invalid TMP block grid 14024704x3276974; supported total is 1..65536.`，没有尝试把它当 TMP 继续读取。

另有一份保留在正常权限缓存内的独立合成端到端导出，方便直接查看实际文件（无游戏素材）：

```text
输入目录：/data/storage/el2/base/haps/entry/cache/redalert-godot-csharp/terrain-pipeline/validation/synthetic-cee4d0d6da
输出目录：/data/storage/el2/base/haps/entry/cache/redalert-godot-csharp/terrain-pipeline/validation/synthetic-cee4d0d6da/converted
结果：Converted 1 nonempty diamonds and 0 extra layers.
```

该目录包含 `tile-0000.diamond.png`、900-byte 原索引、900-byte Z 和 `manifest.json`。manifest 已读取核对：60×30、3 个空表项、`terrainType: 7`、索引 SHA256 `dce130eabd16e25c516e6373552d815ce95e2f0b2625795dcbdd83914419f9c6`、无警告。图像像素通过程序化 PNG 解码自测；当前模型无图像输入能力，没有声称人工目视检查。

真实 TMP/PAL 样本验证等待主代理提供样本路径；尚不把合成自测视为真实游戏样本验证。CLI 已可执行：

```sh
dotnet /data/storage/el2/base/haps/entry/cache/redalert-godot-csharp/terrain-pipeline/bin/TerrainPipeline/debug/Ra2TerrainPipeline.dll \
  convert --tmp /path/to/sample.tem --pal /path/to/isotem.pal --out /path/to/new-or-emptydir
```
