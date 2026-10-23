# 八项反馈修复与原生验证

2026-10-22。最终版本已在 HarmonyOS 的真实 Godot 游戏窗口运行；DisplayServer 为 OpenHarmony，渲染器为 Mobile/Vulkan，保存的最终画面为 2090×1306。以下截图均直接取游戏 Viewport 的实际绘制结果。

| 反馈 | 最终处理与验证 |
| --- | --- |
| 鼠标到窗口边缘出现缩放图标 | 实测发现当前 OpenHarmony 后端不支持 MouseWarp，窗口禁缩放标志的 setter 为空、getter 恒为 false。此平台改用原生 Captured 锁定并隐藏系统指针，在实际内容区内绘制可见指针。指针保留 16 个物理像素的边距，生产侧栏仍可操作。桌面支持 MouseWarp 的后端继续使用 Confined 与位置约束。 |
| 四边移动相机，右边界错误 | 用真实战场矩形判断边缘；右边界位于战场与侧栏交界。四方向实际调用 UpdateCamera 后分别产生 ±65 屏幕像素位移，侧栏内部不卷屏。 |
| 菜单按钮露出背景按钮区域 | 原图按钮面为 126×25，外框为 150×43；移除负 3 像素的纹理扩展边距，按钮填满整个原图面，与背景共用 800×600 画布变换。 |
| 小地图应为矩形并显示真实视野框 | 去除梯形收缩及装饰 padding。地图、标记、点击、视野框共用线性的可逆 U/V 变换；四角与实际 Viewport 平面反投影相符。真实 GUI 点击雷达可以定位相机。 |
| 水岸接壤不正确 | 恢复原版完整 2×2 岸线模板，普通陆地、水面、低桥共用 level 0。相邻模板改为共享角点采样，明显断边指标从 205 降至 0；原生前后图确认硬三角与孤立湿边消失。矿点与水面保留两格岸距，防止矿图覆盖湿边，八个出生点完整 5×5 起始矿区全部保留。 |
| 桥梁不像原版 | 使用实际原版 LOBRDG 木桥条带、真实桥头及近侧木梁原像素遮挡层。每纵向单元只绘制一次整条三格宽桥面，不再绘制程序灰板和栏杆。原图顶面与 canonical deck 0 配准，坦克脚点贴住木板。 |
| 查找原版地图素材作为依据 | 实际查看 CnCNet 的 Malibu Cliffs LE 地图及预览，并从地图 Overlay 数据确认 X/Y 低桥的入口、中段、出口和帧号。素材图集包含 129 个模板、704 个图层，来源 hash 与原像素一致性检查通过。 |
| 坦克进入建筑下、建筑显示错误 | 根据原版基础数据统一阵营地基、物理阻挡、放置、绘制锚点，例如建设基地 4×4、精炼厂 4×3、战车工厂 5×3。车辆使用定点车身扫掠，不能从建筑角穿入；修复格内重规划切角卡住。建筑按实际地面前后约束排序，侧前方车辆显示在建筑前，后方允许屋顶遮挡；占领不改变原建筑地基及美术。 |

原生输入适配没有手动触发按钮信号。输入拦截器先累计并限制可见指针，再把事件通过 Viewport.PushInput 重投到 Godot 正常 GUI 路由，从而统一悬停、按钮、滚轮、小地图及战场命中。真实原生检查已确认维修按钮的 hover 和点击回调，以及小地图导航。Esc 菜单会释放系统指针，关闭菜单恢复约束，失去窗口焦点也会释放。窗口 API 的 `unresizable=false` 原始读数保留在审计中，没有把不支持的平台属性写入当作禁缩放已经成功的证据。

最终构建零警告、零错误，`git diff --check` 通过。只运行本次必要的短检查：

- 地形与矿区 7/7，四个种子，402 ms，零 World 步进；[结果记录](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/battle-polish/surface-check.json>)。
- 建筑占地与车辆绕行 3/3，130 次 World 步进；包括原版阵营尺寸、八个起始基地、完整工厂阻挡、车身擦角扫掠、实际绕路、准备队列、占领后保留地基；[结果记录](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/battle-polish/building-footprints.json>)。
- 原生 UI/相机检查 7/7，涵盖菜单覆盖、矩形地图与视野框、内容区指针、四边卷屏与侧栏边界、真实 GUI 操作、菜单释放/恢复、建筑三面视图；[原生验收记录](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/battle-polish/acceptance.json>)。
- 额外原生过桥短检查：实际单位 6 从 `(49.5,47.5)` 行驶到对岸 `(56.1445,47.5)`，47 次 World 步进后正常结束移动，表面高度全程为 0；[起步帧](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/battle-polish/bridge-moving-start.png>)、[到岸帧及木板上的车辆落脚](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/battle-polish/bridge-moving-end.png>)。最终当前运行日志没有新错误。
- 素材一致性 129 个来源 hash、704 个图层；[素材验证](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/terrain-reference/validation.json>)。

没有重跑旧的长时间网络、回放、八人压力套件。旧版梯形小地图断言已更新为矩形合同。本次规则版本为 `command-alpha-8`，使地基与地图变化进入内容兼容性版本。

![原生木桥、连续岸线、矩形地图和实际视野框](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/battle-polish/bridge-and-radar.png>)

![原生建筑前方：车辆处于基础之外并正确绘制](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/battle-polish/building-front.png>)

![原生建筑后方：合法的屋顶遮挡](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/battle-polish/building-back.png>)

其他图证：[菜单按钮](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/battle-polish/menu.png>)、[建筑侧前方](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/battle-polish/building-east.png>)、[共享角点修复前的原生岸线反例](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/battle-polish/building-front-before-shore.png>)。

原版地图与素材的来源、固定提交、帧号、锚点及真实地图预览见[原版素材核对报告](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/terrain-reference/README.md>)。岸线量化指标见[共享角点诊断](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/battle-polish/shore-shared-corners.json>)；该指标明确属于拓扑计算，原生视觉结论由以上实际截图补充。
