# 红警 2 v1.006 遭遇战：实现与验收

目标为原版 Red Alert 2 v1.006，未引入《尤里的复仇》内容。原版规则、地图和素材作为数据使用；模拟和渲染由当前工程实现。本轮实现是可游玩的接近版本，仍有下列明确差异，不能称为原引擎的完整复刻。

## 已接入

- 9 个原版国家；47 个逻辑单位类型、27 个建筑类型；国家专属原型及盟军/苏军美术变体。步兵、战车、飞机、海军使用各自生产分类、前置、费用和队列。
- MCV 开局与展开/收车、供电、建造、矿车采集及卸货、机场停机容量和补弹、运输和卸载、两栖移动、主要特殊兵种与四种超级武器；美国 8 GI 伞兵支援。AI 通过相同命令接口展开、建设和生产。
- 5 张原版地图：South Pacific、Isle of War、Little Big Lake、Hammer and Sickle、DEFCON 6。保留原地块、可玩范围、18 个出生点、矿石分布、高差、道路、悬崖与桥梁。DEFCON 6 保留原地图禁止船坞的规则。
- 1645 个原地图物件全部解析：1525 地形物件、60 中立/城区建筑、5 辆民用车辆、55 处弹坑/焦痕，共 91 种。使用原 Foundation 处理静态碰撞、寻路与建造限制。
- 原 overlay 的 10 种岩石及 RUBBLE 别名，共 448 格；岩石与演员按深度排序，弹坑和 rubble 在地面阶段绘制。
- 原版箭头、修理、出售等 450 帧光标及热点、动画；137 个音频文件，实际开火事件映射和宝箱拾取触发。

## 鼠标修正及测量边界

捕获模式的 ScreenRelative 是原始硬件位移，桌面绝对指针已经过系统加速；原问题同时涉及这一区别和 viewport 缩放。当前逐报告应用对应 OpenHarmony 加速曲线，再对 viewport 变换做一次逆变换；捕获时关闭输入合并，离开时恢复，失焦/离窗释放。采用显示器实际物理 PPI，未把 densityDpi 当物理 PPI。

7 项输入检查通过，生成了 5 张实际 GPU 光标画面。该检查将原始/本地输入送入真实 viewport，但没有测量实体鼠标在桌面与游戏上的两条硬件轨迹。系统 GetPointerSpeed 在此设备返回 -1，因此按系统默认速度 10 回退，并提供速度与灵敏度设置；未把回退值称为已读取的用户偏好。

## 集中验收

| 检查 | 实际结果 | 模拟步预算 |
| --- | --- | --- |
| 战斗：装甲/武器、AA/潜艇、C4/控制/辐射等 | 9/9 PASS | 396/900 |
| 经济：产能/机场、矿车、间谍等 | 8/8 PASS | 321 |
| 移动：两栖、运输装卸、旧 ID 乘客清理、越障飞行、收车开关 | 5/5 PASS | 97/200 |
| 回放与完整规则/地图状态指纹 | 6/6 PASS | 90/100 |
| 五图全部 MCV 原地展开，零起始部队 | 5/5 图、18/18 基地 PASS | 5/5 |

DEFCON 6 的首轮 17/18 失败保留了真正的原岩石阻挡。统一修正偶数地基的展开支点，收车逆映射和 AI 预检同步；重跑硬断言 18/18 通过，未清除岩石或移动地图出生点。回放检查额外验证收车后 MCV 回到原格，避免反复操作漂移。

最终游戏构建为 0 warnings、0 errors。没有启动旧的长 UI、网络、八人长局套件。当前规则修订为 command-alpha-10，内容指纹包括原地图物件的静态规则。

## 最终原生画面与真实游戏循环

[最终八组 GPU 截图记录](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/skirmish-parity/visual-20261021-192200/report.json>)覆盖两张城区、森林、高桥、海军、苏军建筑、国家专属单位和大型民房侧面遮挡，均为 2090×1306 实际画面：

- 91 种物件和 10 种岩石素材加载成功；物件/岩石绘制失败均为 0。
- 森林画面实际绘制 69 个原物件和 9 个岩石，高桥绘制 14 段；城区保留原房屋、路灯、污迹和街道。
- TMP 原透明索引 0 曾被画成蓝色。本轮仅将其 24191 个像素 alpha 从 255 修正为 0，原 RGB、indices、depth、偏移和 atlas regions 不变。最终八张画面的战场区域蓝块特征像素均为 0。
- 独立只读复核发现大型中立建筑未参与地基遮挡图，随后将其纳入相同排序约束。新增 CANWY26 东侧坦克原生场景：实际绘制顺序检查通过，14 条地基约束、排序回退 0；该修复后的再次集中构建为 0 warnings、0 errors。

[最终正常游戏循环记录](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/skirmish-parity/gameplay-20261021-191346/report.json>)通过真实 Main._Process、Issue、World.Step 和 AudioCaptureTick 运行 52 ticks：玩家与 AI 均展开基地，接受命令 4、拒绝 0；13 次真实原版武器事件，缺失音效映射 0；实际宝箱拾取 1、播放 1，累计播放 27 个 cue。137 个音频文件加载成功，音乐正在播放。创建音效计数在本场景为 0，没有将未触发事件当成通过。

[最终游戏截图](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/skirmish-parity/gameplay-20261021-191346/native-gameplay.png>) · [森林与岩石](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/skirmish-parity/visual-20261021-192200/mp23t4-forest-0.png>) · [城区](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/skirmish-parity/visual-20261021-192200/mp02t2-city-1.png>) · [高桥](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/skirmish-parity/visual-20261021-192200/mp23t4-highbridge-0.png>) · [海军](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/skirmish-parity/visual-20261021-192200/mp01t4-naval-0.png>)。

## 仍与原版有差异

- 中立城区建筑、科技建筑、民用车辆目前是原美术及静态碰撞物件，未完成完整的驻军、攻占收益、破坏和中立 AI 联动。
- 移速、建造时长的换算及部分坡面内部插值采用当前模拟近似；维修厂在周边维修，伞兵支援仍为即时交付，未还原完整运输机/降落伞过程。
- 部分弹道、特效、宝石外观和宝箱奖励仍为近似或子集；原版所有地图、全部游戏速度和长期对战平衡未覆盖。
- 原生正常循环属于短场景验收，未宣称完成整局对战、全部产品逐个 UI 生产和长时网络同步验证。

## 资料与细项证据

原资料参考：[官方英文手册](http://nyerguds.arsaneus-design.com/manuals/Red%20Alert%202/Red%20Alert%202%20-%20English.pdf)、[Planet C&C 原版资料](https://planetcnc.gamespy.com/Viewcc9a.html?view=GameInfo.Detail&id=35&game=5)，以及记录 SHA 的原 rules/art、地图、TMP、SHP、VXL 数据。

[规则与经济核对](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/skirmish-parity/rules-research.md>) · [战斗](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/skirmish-parity/combat-core-verification.md>) · [地图物件素材](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/skirmish-parity/map-objects.md>) · [物件导航与 18 个出生点](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/skirmish-parity/map-object-navigation.md>) · [透明与岩石](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/skirmish-parity/terrain-rock-transparency.md>) · [真实音频事件](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/skirmish-parity/audio-classic-cues.md>)。
