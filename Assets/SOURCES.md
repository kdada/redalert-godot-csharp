# 原版 RA2 小体积参考资源与来源

本目录供本项目学习和格式研究使用。可追溯的原版游戏美术、音频、截图参考、解码转换结果与独立重建的界面装饰分别注明。建设面板 cameo、战场 SHP 精灵、VXL/HVA 模型和菜单影片分别保留来源与散列；截图派生的金属框及遮挡区域重建不冒充原始资源。

2026-10-18 后续音频修正：实际接入从 Frank Klepacki 官方原声页取得的音乐，以及同一固定版本原版资源镜像中的武器音效和单位／EVA 语音，原始文件名、容器区间、SHA256 与独立解码过程见 [原版音频记录](../Reports/AUDIO.md)。原先独立合成的音乐音效已退出活动映射，不标记为原版。盟军／苏军 Esc 菜单使用保留的原始 `pudlgbga.shp`／`pudlgbgs.shp` 及 `mnbttn.shp` 按钮像素，来源仍在菜单素材清单中。

取得日期：2026-10-18。首批实际保留的网络下载资源共 **1,502,680 字节**；连同从原始像素转换的 PNG 共 **1,874,120 字节**，118 个资源文件。工具、文档、Godot 自动产生的导入描述文件不计入美术资源数量。没有下载整个游戏或完整 MIX；主要资源通过 HTTP 206 Range 只读取索引与指定条目的字节。

完整逐文件清单、原始 URL、固定 Git commit、精确 HTTP Range、SHA256、尺寸、帧数、实际格式、用途和转换依赖见 [manifest.json](Reference/manifest.json)。这份清单覆盖首批资源；后续 36 个补充样本的精确区间和 SHA256 见 [manifest-extra.json](Reference/manifest-extra.json)。生成的运行时图集另外自带输入指纹与元数据。

## 原始来源

| 来源 | 实际取得的内容 | 可追溯证据 |
| --- | --- | --- |
| [toarrntnz/ts-redalert2-assets](https://github.com/toarrntnz/ts-redalert2-assets/tree/acfa5b834899e7fab4c8499ccb4b49c402927b44) | 从原版 `ra2.mix` 分片中的 `conquer.mix`、`local.mix`、`generic.mix`，以及 `language.mix` 中的 `cameo.mix`，按文件名 CRC 查索引并提取指定条目 | 固定版本 `acfa5b834899e7fab4c8499ccb4b49c402927b44`；[README](https://github.com/toarrntnz/ts-redalert2-assets/blob/acfa5b834899e7fab4c8499ccb4b49c402927b44/README.md) 说明原版资源及分片方式；每个样本的字节区间在清单中 |
| [pedrus16/ra2-models](https://github.com/pedrus16/ra2-models/tree/b565f9ecdc27f628d824e00a33d5df713c308c9e/doc/icons) | 27 个已有的 60×48 建筑 cameo PNG 副本；没有下载其重制 Blender 模型或材质 | 固定版本 `b565f9ecdc27f628d824e00a33d5df713c308c9e`；[README](https://github.com/pedrus16/ra2-models/blob/b565f9ecdc27f628d824e00a33d5df713c308c9e/README.md) 逐一把图标对应到建筑名称；这组属于第三方 PNG 副本，不声称是零售 SHP 原始字节 |
| [DeDf/ShpTSPlayer](https://github.com/DeDf/ShpTSPlayer/tree/2f6aa5ad73747758bf1c6e410562db5e9f9345a6) | [unittem.pal](Reference/Formats/unittem.pal) 与 [ntclonmk.shp](Reference/Formats/ntclonmk.shp) | 固定版本 `2f6aa5ad73747758bf1c6e410562db5e9f9345a6`；后者是尤里复仇克隆缸建造动画，214×174、50 帧，属于扩展包样本 |
| [TheMi1k/Ra2EasyShp](https://github.com/TheMi1k/Ra2EasyShp/tree/140aa5aa82c788b65553b50b18bcc6e872d52bf3/Res/Palette) | [cameo.pal](Reference/Formats/cameo.pal)，768 字节原版建设图标调色板 | 固定版本 `140aa5aa82c788b65553b50b18bcc6e872d52bf3`；组件范围 0–63 |

游戏美术属于 EA/Westwood 等原权利人。来源仓库的程序或文档许可证不改变游戏美术的归属。

## 可直接交给 Godot 的 PNG

以下为原始 SHP 像素和对应 PAL 的格式转换，未使用生成式图片。PNG 导入时建议关闭过滤和有损压缩，按需要使用最近邻缩放。

### 两阵营核心建筑

这些 PNG 只导出各原始 SHP 的第 0 帧，保留完整画布尺寸和内部偏移，索引 0 透明。原版建筑通常还需要叠加旗帜、发光、地基和屋顶等图层；对应已取得的图层也分别导出，没有宣称静态主图层等于完整游戏动画。

| 建筑 | 战场 PNG | 原始 SHP |
| --- | --- | --- |
| 盟军电厂 | [ggpowr.png](Reference/Exported/Buildings/ggpowr.png)，142×110 | [ggpowr.shp](Reference/Battlefield/Buildings/ggpowr.shp)，6 帧；[发光层](Reference/Battlefield/Buildings/ggpowr_a.shp)，32 帧 |
| 盟军兵营 | [ggpile.png](Reference/Exported/Buildings/ggpile.png)，250×148 | [ggpile.shp](Reference/Battlefield/Buildings/ggpile.shp)，6 帧；[旗帜层](Reference/Battlefield/Buildings/ggpile_a.shp)，64 帧 |
| 盟军矿场 | [ggrefn.png](Reference/Exported/Buildings/ggrefn.png)，210×196 | [ggrefn.shp](Reference/Battlefield/Buildings/ggrefn.shp)；[地基层](Reference/Battlefield/Buildings/ggrefnbb.shp) |
| 盟军战车工厂 | [ggweap.png](Reference/Exported/Buildings/ggweap.png)，266×224 | [ggweap.shp](Reference/Battlefield/Buildings/ggweap.shp)；[地基](Reference/Battlefield/Buildings/ggweapbb.shp)、[坡道](Reference/Battlefield/Buildings/ggweap_1.shp)、[顶部](Reference/Battlefield/Buildings/ggweap_2.shp) |
| 苏军电厂 | [ngpowr.png](Reference/Exported/Buildings/ngpowr.png)，210×140 | [ngpowr.shp](Reference/Battlefield/Buildings/ngpowr.shp)，6 帧 |
| 苏军兵营 | [nghand.png](Reference/Exported/Buildings/nghand.png)，254×230 | [nghand.shp](Reference/Battlefield/Buildings/nghand.shp)，6 帧 |
| 苏军矿场 | [ngrefn.png](Reference/Exported/Buildings/ngrefn.png)，210×184 | [ngrefn.shp](Reference/Battlefield/Buildings/ngrefn.shp)；[地基层](Reference/Battlefield/Buildings/ngrefnbb.shp) |
| 苏军战车工厂 | [ngweap.png](Reference/Exported/Buildings/ngweap.png)，282×224 | [ngweap.shp](Reference/Battlefield/Buildings/ngweap.shp)；[地基层](Reference/Battlefield/Buildings/ngweapbb.shp) |

其他同名图层的第 0 帧 PNG 位于 [导出目录](Reference/Exported/Buildings/)。普通 6 帧建筑 SHP 常见布局为正常/受损/重损和后半的阴影帧；应以实际序列定义为准。这里的 `gg`/`ng` 是温带、城市通用图形名称，雪地另用 `ga`/`na` 等版本；本批没有收集完整雪地图集。

### 步兵战场图集

每张图集 **16 列**，按原版帧索引从左至右、从上至下排列，空余格透明。截取第 `i` 帧：`x=(i%16)*frameWidth`、`y=(i/16)*frameHeight`。原版 SHP 的后半组包含阴影帧，不能把全部帧依次作为行走动画播放。

| 单位 | PNG 图集 | 单格尺寸 | 总帧数 | 原始 SHP |
| --- | --- | --- | --- | --- |
| GI | [gi.png](Reference/Exported/Infantry/gi.png)，1248×3102 | 78×66 | 744 | [gi.shp](Reference/Battlefield/Infantry/gi.shp) |
| 动员兵 | [cons.png](Reference/Exported/Infantry/cons.png)，1216×3648 | 76×96 | 602 | [cons.shp](Reference/Battlefield/Infantry/cons.shp) |
| 英国狙击手 | [snipe.png](Reference/Exported/Infantry/snipe.png)，1792×3116 | 112×82 | 602 | [snipe.shp](Reference/Battlefield/Infantry/snipe.shp) |

可以先用 0–7 帧作为八方向站立，行走用 `8+方向索引*6+步数`，普通步兵射击用 `164+方向索引*6+步数`。GI 部署后的站立从 292 开始，部署动作从 300 开始。索引均从 0 计。序列依据 [OpenRA 默认步兵定义](https://github.com/OpenRA/ra2/blob/master/mods/ra2/sequences/defaults.yaml) 和 [盟军步兵定义](https://github.com/OpenRA/ra2/blob/master/mods/ra2/sequences/allied-infantry.yaml)，这里只引用格式知识和名称，没有把这些项目的生成素材当原版。

调色板采用 [unittem.pal](Reference/Formats/unittem.pal)，按 6 位色分量左移 2 位转换；没有模拟游戏 VPL 光照。索引 0 设为透明，索引 1 设为半透明；原始调色板及像素索引仍保存在原文件。运行时已根据规范图集保留的 16–31 号索引进行队伍色重映射。

## 建设面板图标

[Cameos 目录](Reference/Cameos/) 有 **40 个 60×48 PNG cameo**。它们适用于建设、训练按钮或单位说明面板，**不是战场 sprites**。

| 用途 | PNG |
| --- | --- |
| 盟军电厂 / 兵营 / 矿场 / 战车工厂 | [powricon](Reference/Cameos/powricon.png) / [brrkicon](Reference/Cameos/brrkicon.png) / [reficon](Reference/Cameos/reficon.png) / [gwepicon](Reference/Cameos/gwepicon.png) |
| 苏军电厂 / 兵营 / 矿场 / 战车工厂 | [npwricon](Reference/Cameos/npwricon.png) / [handicon](Reference/Cameos/handicon.png) / [nreficon](Reference/Cameos/nreficon.png) / [nwepicon](Reference/Cameos/nwepicon.png) |
| GI / 灰熊 / 超时空矿车 | [giicon](Reference/Cameos/giicon.png) / [gtnkicon](Reference/Cameos/gtnkicon.png) / [ahrvicon](Reference/Cameos/ahrvicon.png) |
| 动员兵 / 犀牛 / 武装矿车 | [e2icon](Reference/Cameos/e2icon.png) / [htnkicon](Reference/Cameos/htnkicon.png) / [harvicon](Reference/Cameos/harvicon.png) |
| 美国空降兵 / 英国狙击手 / 德国坦克杀手 / 法国巨炮 | [aparicon](Reference/Cameos/aparicon.png) / [snipicon](Reference/Cameos/snipicon.png) / [tnkdicon](Reference/Cameos/tnkdicon.png) / [gcanicon](Reference/Cameos/gcanicon.png) |
| 韩国黑鹰 / 苏军磁能坦克 / 超时空军团兵 | [beagicon](Reference/Cameos/beagicon.png) / [ttnkicon](Reference/Cameos/ttnkicon.png) / [clegicon](Reference/Cameos/clegicon.png) |
| 普通伞兵能力 | [paraicon](Reference/Cameos/paraicon.png) |
| 建造厂 | [mcvicon](Reference/Cameos/mcvicon.png) / [smcvicon](Reference/Cameos/smcvicon.png) |
| 基础炮塔 / 防空 / 高级防御 | [pillicon](Reference/Cameos/pillicon.png) / [plticon](Reference/Cameos/plticon.png) / [samicon](Reference/Cameos/samicon.png) / [flakicon](Reference/Cameos/flakicon.png) / [prisicon](Reference/Cameos/prisicon.png) / [tslaicon](Reference/Cameos/tslaicon.png) |
| 空指部 / 雷达 / 实验室 | [heliicon](Reference/Cameos/heliicon.png) / [nradicon](Reference/Cameos/nradicon.png) / [techicon](Reference/Cameos/techicon.png) / [ntchicon](Reference/Cameos/ntchicon.png) |
| 船厂 / 维修厂 / 围墙 | [ayaricon](Reference/Cameos/ayaricon.png) / [yardicon](Reference/Cameos/yardicon.png) / [fixicon](Reference/Cameos/fixicon.png) / [rfixicon](Reference/Cameos/rfixicon.png) / [wallicon](Reference/Cameos/wallicon.png) / [nwalicon](Reference/Cameos/nwalicon.png) |

13 个由本次原始 SHP 解码的单位/能力图标，也保留 [原始 cameo SHP](Reference/Formats/Cameos/) 及转换依赖，可从清单区分它们与第三方 PNG 副本。

## 坦克与矿车是真正的 VXL/HVA

首批 [Voxels 目录](Reference/Battlefield/Voxels/) 样本含 12 个 VXL 和 12 个配套 HVA，补充样本又增加 12 对。HVA 保存部件变换，VXL 保存体素、颜色索引和法线。现在已使用独立 C# headless 体素烘焙器生成方向图，游戏消费生成后的 PNG、重映射掩码和元数据。

| 车辆 | 实际部件 |
| --- | --- |
| 灰熊 | [gtnk.vxl](Reference/Battlefield/Voxels/gtnk.vxl) + [gtnk.hva](Reference/Battlefield/Voxels/gtnk.hva)；[炮塔](Reference/Battlefield/Voxels/gtnktur.vxl) + [HVA](Reference/Battlefield/Voxels/gtnktur.hva)；[炮管](Reference/Battlefield/Voxels/gtnkbarl.vxl) + [HVA](Reference/Battlefield/Voxels/gtnkbarl.hva) |
| 犀牛 | [htnk.vxl](Reference/Battlefield/Voxels/htnk.vxl) + [htnk.hva](Reference/Battlefield/Voxels/htnk.hva)；[炮塔](Reference/Battlefield/Voxels/htnktur.vxl) + [HVA](Reference/Battlefield/Voxels/htnktur.hva)；[炮管](Reference/Battlefield/Voxels/htnkbarl.vxl) + [HVA](Reference/Battlefield/Voxels/htnkbarl.hva) |
| 超时空矿车 | [cmin.vxl](Reference/Battlefield/Voxels/cmin.vxl) + [cmin.hva](Reference/Battlefield/Voxels/cmin.hva) |
| 武装矿车 | [harv.vxl](Reference/Battlefield/Voxels/harv.vxl) + [harv.hva](Reference/Battlefield/Voxels/harv.hva)；[炮塔](Reference/Battlefield/Voxels/harvtur.vxl) + [HVA](Reference/Battlefield/Voxels/harvtur.hva) |
| 德国坦克杀手 | [tnkd.vxl](Reference/Battlefield/Voxels/tnkd.vxl) + [tnkd.hva](Reference/Battlefield/Voxels/tnkd.hva) |
| 苏军磁能坦克 | [ttnk.vxl](Reference/Battlefield/Voxels/ttnk.vxl) + [ttnk.hva](Reference/Battlefield/Voxels/ttnk.hva)；[炮塔](Reference/Battlefield/Voxels/ttnktur.vxl) + [HVA](Reference/Battlefield/Voxels/ttnktur.hva) |

实际使用的车辆战场图集位于 [Generated/Vehicles](Generated/Vehicles/)，每个方向均由原始三维体素几何重新投影，支持独立车体、炮塔和炮管。

## Headless 复现与验证

- [fetch_ra2_samples.py](Reference/Tools/fetch_ra2_samples.py)：Python 标准库 + Node 的 OpenSSL legacy Blowfish，读取 MIX 索引、按名称 CRC 查条目并通过精确 Range 提取。每次请求必须返回符合区间的 HTTP 206；累计传输上限 19 MB，不接受服务器退回整个档案。只写本目录的显式允许样本。
- [convert_shp_samples.py](Reference/Tools/convert_shp_samples.py)：独立编写的 SHP/PAL→PNG 转换脚本，仅解码原图像素。支持原始压缩模式及零值行程编码，处理零售素材行尾越出一格的透明填充。没有启动编辑器或 GUI。
- 已检查 SHP 魔数、尺寸、帧头、所有导出帧的解码边界、VXL 签名、HVA 长度、PAL 的 768 字节与 6 位分量范围，以及每项资源的 SHA256；最终另核验 PNG chunk CRC、解压后尺寸和清单覆盖完整性。
- 当前执行模型不支持图片输入；调用 `read_image` 被工具明确拒绝，因此没有声称完成人工视觉验收。PNG 转换由格式解码和文件结构验证确认，后续可由支持图片的会话或渲染测试补充视觉检查。

## 已接入游戏的规范图集

- [Sprites](Generated/Sprites/)：27 组独立 C# 解码器输出，包含 GI、动员兵、狙击手、工程师、盟苏军犬，以及核心建筑、建造厂、雷达、实验室、巨炮底座和磁暴线圈。每组提供原索引、RGBA、队伍色掩码和逐帧元数据；前述 `Reference/Exported` 的整画布预览保持原样，运行时使用裁剪后的规范图集。
- [Vehicles](Generated/Vehicles/)：10 组 32 朝向图集，覆盖灰熊、犀牛、超时空矿车、武装矿车、磁能坦克、多功能步兵车、防空履带车、光棱坦克、天启及巨炮炮塔。每个部件保留原索引、法线索引、队伍色、透明覆盖与输入 SHA256。
- 补充资源由 [fetch_more_art.py](../Tools/fetch_more_art.py) 按首批相同的固定源版本与精确 Range 提取，总计 36 个条目；本次补充请求实际传输 865,671 字节。
- 建筑像素锚点参考公开序列的数值 Offset，记录于 [layout.json](Generated/Sprites/layout.json)，包含源 URL 与源内容 SHA256。只提取坐标事实，未引入其它游戏实现代码。
- 已接入原版建筑主图层、地基及下述附加动画和施工序列；完整原版光照仍待后续完善。headless 验证不等于视觉验收。

## 原版界面与温带地表补充

针对 UI 和地图与原版风格差异较大的反馈，补取同一固定版本资源仓库中的 `sidec01.mix`、`sidec02.mix`、`isotemp.mix`、`temperat.mix` 和调色板条目。没有使用 OpenRA 的重排 chrome 拼图或游戏实现代码。

- 精确 URL、HTTP Range、原名与 SHA256 见 [manifest-visual.json](Reference/manifest-visual.json) 和 [manifest-visual-variants.json](Reference/manifest-visual-variants.json)，共 115 个补充条目，相关请求合计 2,779,008 字节（包含索引请求）。
- 两阵营界面使用原版 radar、side1/2/3、tab00–03、repair/sell；保持原画布与按钮帧，原版调色板转换后见 [界面资源目录](Generated/Interface/catalog.json)。矿石、树木和岩石虽使用 `.tem` 扩展，实际是 SHP TS 格式，单独通过已有 SHP 解码器转换。
- 地面使用原始 TMP 60×30 菱形和 `isotem.pal`。保留每个原始 900 字节索引及 Z 数据，C# 转换器输出后将 746 个平地子块无重绘地拼入 [地表图集](Generated/Terrain/ground.png)，元数据及来源帧散列见 [地表目录](Generated/Terrain/catalog.json)。8 种 clear、ruff、sandy、green 变体及 LAT 系列、完整 42 种 shore 模板均保留。
- 第二轮核对原版 [temperat.ini](Reference/Visual/Metadata/temperat.ini) 后，纠正了 `rough01..10` 是 **Farm Crops 农田**、不是天然粗糙地面的误认。天然草丛使用 ruff/clat，疏草裸地使用 sandy/dlat。地表四边关系由原始 CLAT 索引分布测量，16 种关系全部唯一。后续修正了岸线按水面占比任取子窗口的问题：现在按水陆方向使用完整直岸/转角模板，联合校验重复段与四角接缝，不再把弯岸片段和大面积水块当直岸。原岸线干侧为 green 沙地，增加 green/glat 沙地过渡带，并独立验证15种实际使用的 GLAT 方向。布局证据在 [layout.json](Generated/Terrain/layout.json)，生成器为 [derive_terrain_layout.py](../Tools/derive_terrain_layout.py)。本轮不改变模拟地形和通行性，岸线匹配覆盖现有海洋边界，尚非完整原版地图编辑器的所有高度/悬崖/岸线组合。
- 复现入口：[资源提取](../Tools/fetch_visual_art.py)、[变体与装饰提取](../Tools/fetch_visual_variants.py)、[转换与拼图](../Tools/prepare_classic_art.py)。[TMP 工具说明](../Tools/TerrainPipeline/README.md) 列出格式依据与边界；转换原像素不改变原权利归属。

## 建筑施工、附加动画与原版队伍色

- [manifest-details.json](Reference/manifest-details.json) 记录 42 个源条目（含4个复用的建筑资源），包含原版 art.ini、17 种建筑施工与其它附加层。41 个新图集保留全部 1500 帧及空帧元数据，位于 [Details](Generated/Details/)，逐项结果见 [verification.json](Generated/Details/verification.json)。
- 原版 art.ini 从 `local.mix` 精确提取，SHA256 `b477f861063a9509e87b7836b78190204c73bea5ea5595a842c456f5920ea223`。序列起止、是否需要电力等使用原数据；叠层锚点数值来自已记录的公开数据事实。动画时钟、施工帧映射与图层合成为独立实现，没有使用游戏实现代码。
- SHP 的第二半阴影与原队伍色索引完整保留。树石与矿石的61个阴影帧均逐像素验证只含原始0/1二值索引，再按原画布位置叠加。VXL 车辆尚未恢复真实投影阴影。
- 八种队伍色改用原版 [rules.ini](Reference/Visual/Metadata/rules.ini) 的 `[Colors]` HSV 数值：Gold、DarkRed、DarkBlue、DarkGreen、Orange、DarkSky、Purple、Magenta；只改变显示，不改变玩家身份或网络状态。
- [fetch_terrain_details.py](../Tools/fetch_terrain_details.py) 与 [manifest-terrain-details.json](Reference/manifest-terrain-details.json) 记录追加地表的135条精确Range源。包括先前已取得的岸线重复验证条目；唯一源文件总数以完整性报告的sourceFiles为准。

## 启动、设置与局域网前端

- [菜单源清单](Reference/manifest-menu.json) 记录90份原始素材和100条精确HTTP206范围，新增载荷2,188,075字节；[菜单目录](Generated/Menu/catalog.json) 保留76个PCX及6组SHP、原始尺寸和调色板依据。
- 第一版曾使用 dbak6440 与 shellbttnoff/on/press；经看图确认它们不足以证明发行版外观，当前已替换。历史素材及其[转换校验](Generated/Menu/validation.json) 保留供格式研究，不再作为主菜单还原依据。
- [发行版参考事实](Reference/FrontEndResearch/FACTS.md) 和[截图来源清单](Reference/FrontEndResearch/provenance.json) 保留原始手册、Version 1.0 主菜单、2001年选项截图、1.006 原生网络大厅及遭遇战设置参考，明确版本与置信度。
- [原版影片源清单](Reference/manifest-authentic-shell.json) 记录固定版本 language.mix 中 ra2ts_l.bik 的精确范围，原片7,184,828字节，632×570、431帧、15fps、28.733秒、无音轨。没有下载整个 MIX 或游戏实现代码。[生成清单](Generated/AuthenticShell/manifest.json) 保留原始帧、PNG、完整 Ogg/Theora 循环及解码器验证。
- 当前主菜单实际使用该原片的标志、红色雷达与银色 CRT 边框；右侧按钮使用原始 mnbttn SHP 帧，复选灯、滑条手柄与下拉箭头使用原 PCX。SHP 三帧的普通／悬停／按下映射是独立实现选择，尚未从原程序点击录像逐帧核实。
- [界面装饰生成器](../Tools/derive_shell_chrome.gd) 从明确记录的原版截图提取金属框、控制台和地图装饰；移除截图上的旧控件与文字，独立 Godot 控件承接全部点击和数值。被旧控件遮挡的少量底纹为重建区域，不能称为原始逐像素背景。字体未追求原版一致。样式、实际操作和边界见[当前前端记录](../Reports/AUTHENTIC_FRONT_END.md)。菜单音乐尚未补入。

格式资料：[tcrs/ra2ff](https://github.com/tcrs/ra2ff)；[OpenRA SHP TS 读取器](https://github.com/OpenRA/OpenRA/blob/bleed/OpenRA.Mods.Common/SpriteLoaders/ShpTSLoader.cs)；[OpenRA RA2 建筑序列](https://github.com/OpenRA/ra2/blob/master/mods/ra2/sequences/allied-structures.yaml)；[OpenRA RA2 VXL 部件映射](https://github.com/OpenRA/ra2/blob/master/mods/ra2/sequences/voxels.yaml)。
