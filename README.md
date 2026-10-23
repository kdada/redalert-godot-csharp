# Red Alert Command · Godot + C#

> 当前验收版本：`command-alpha-13`（2026-10-23）。已接入原版地图及海陆空生产交战，修正坡面车辆、方向与部署光标、矿物配色和余量、迷雾，以及树桥破坏和毁后通行。最后一局正常开局对抗自主 AI 已自然结算为“作战胜利”，180 条指令接受、0 条拒绝。完整结果及尚未复原的细节见[最新验收汇总](Reports/skirmish-parity/acceptance-2026-10-23.md)与[完整 AI 对战验收](Reports/skirmish-parity/final-ai-match-verification.md)。
>
> 下文保留初版搭建过程与命令说明，其中早期功能清单和限制以以上最新验收记录为准。

独立实现的红警 2 地面 RTS 学习原型。游戏逻辑、Godot 前端、ENet 联机和 SHP/VXL 转换器均为新写的 C# 代码；参考公开格式和行为，不依赖 OpenRA、原版游戏程序或现成 RTS 游戏逻辑库。原版美术通过可追溯的小体积样本转换使用。

支持 2–8 位玩家；普通主机计入 8 人上限，也可运行独立服务端加 8 个客户端。模拟、联机和回放验收首先通过 headless 完成；后续已安装 DSH 控制插件，在鸿蒙图形 Godot Editor App 中运行四国训练场并保存真实 GPU 截图，见[原生图形验证](Reports/NATIVE_RENDERING.md)。随后针对 UI 与地图风格进行了两轮还原，最新的地表连接、岸线、建筑动画与侧栏操作，以及实机画面和动图见[第二轮还原记录](Reports/RESTORATION.md)；随后纠正海岸错误接缝与沙地衔接，最新结果见[海岸拼接修复](Reports/COAST_FIX.md)。启动菜单已接入原版红色雷达与银色 CRT 动画；设置、单人游戏和网络大厅按发行版截图、原始手册重新编写，见[发行版菜单重做与实机截图](Reports/AUTHENTIC_FRONT_END.md)。

## 当前可玩内容

| 国家 | 阵营 | 特色 |
| --- | --- | --- |
| 美国 | 盟军 | 空降 6 名 GI，带冷却时间 |
| 俄罗斯 | 苏军 | 磁能坦克 |
| 法国 | 盟军 | 巨炮 |
| 英国 | 盟军 | 狙击手 |

- 96×96 等角地图、8 个出生点、地面阻挡、矿区和队伍共享迷雾。
- 采矿和卸矿、超时空矿车返矿、电力、建造、生产队列、取消退款、维修、出售和集结点。
- 步兵、军犬、工程师、矿车、基础坦克及部分高级战车；编队移动、攻击、进攻移动、自动索敌、溅射和工程师占领。
- 框选、同类选择、编组、小地图、视角平移和缩放；建设面板使用原版 cameo 图标。
- 原版步兵 SHP 动画、盟苏建筑 SHP、离线烘焙的 32 朝向 VXL 车辆和分离的车体/炮塔；按原队伍色索引换色。
- 原版 60×30 温带 TMP 地面、连续草丛/裸地 LAT 过渡、岸线、矿石和树石阴影；原版队伍色与选择血条。
- 17 种建筑施工序列及电厂、兵营、建造厂、矿场、工厂附加动画，支持原版阴影、受损段和电力状态。
- 盟苏原版侧栏皮肤、雷达与分类页签；精确75行距双列 cameo、竖向电力条、整行滚动与紧凑选项菜单。
- ENet 房间、队伍与国家选择、房主开始、加载屏障、命令归属验证、掉线投降、周期状态校验、录像保存与离线重演校验。
- 四国训练场带有简单对手，适合验证经济和操作。

这是首个可运行版本。数值与部分行为为原型实现；尚未实现完整原版单位/科技树、战役、空军/海军、超武、建筑驻军/地形高差、联网重连或录像观看界面。地面已接入原始 TMP 像素与 LAT/岸线连接；地图布局仍由种子生成，完整高差/悬崖/桥梁/道路与岸线编辑组合尚未实现。部分战斗特效、VXL投影阴影和原版光照尚未还原。图形资源与绘制回调已通过 headless 检查；原生窗口已验证 Mobile / Vulkan 渲染、中文排版、主要布局边界和截图输出。逐像素美术审查、持续 GPU 性能和八人图形联机仍未验收。

## 工具链与构建

当前实际验证环境：HarmonyOS arm64，Godot `4.7.2.stable.mono`，`Godot.NET.Sdk/4.7.2-ohos.3`，.NET SDK `10.0.401` / `net10.0`。

在项目根目录执行：

```zsh
dotnet build RedAlert.csproj
```

[NuGet.Config](NuGet.Config) 使用这台机器自带的本地 Godot NuGet feed，构建不依赖在线包源。[Directory.Build.props](Directory.Build.props) 将普通 .NET 项目的构建产物放进 `XDG_CACHE_HOME`；Godot 元数据也通过被忽略的 `.godot` 缓存目录保存。其它平台需要将 Godot SDK 版本与 NuGet feed 改为该平台所装的 Godot .NET 版本；尚未验证其它平台的构建和联网一致性。

源码和生成后的美术均已保留，普通构建不必重新下载或转换素材。

## Headless 验证

完整验收入口（构建、headless 导入、规则/输入/生命周期和 2/8 人普通及弱网对战）：

```zsh
python3 Tools/verify_headless.py
```

验收还逐项检查 154 个来源文件、242 个 PNG 的完整性，以及 3,264 个 SHP 帧的原索引裁剪散列。`--quick` 使用 2 人比赛和 800 单位压力场景，`--with-assets` 额外重建并自测两个格式工具。正确性通过与性能达标分别记录；压力结果里的 P95 需要与 20 Hz 的 50 ms 预算比较。

规则集成测试与大部队逻辑性能测试：

```zsh
dotnet run --project Tests/RedAlert.Tests.csproj -- --report=Reports/core-tests.json --stress=1600
```

Godot 实际场景启动、原版资源加载、绘制回调和鼠标/键盘操作链路：

```zsh
godot --headless --path . -- --ui-test --report=Reports/ui-input.json
```

真实进程间的 ENet 对战；每个进程独立模拟并独立重演自己的录像：

```zsh
python3 Tools/headless_match.py --players=8 --ticks=3600
python3 Tools/headless_match.py --players=8 --ticks=1200 --loss=0.02 --jitter-ms=40
python3 Tools/headless_match.py --players=8 --ticks=800 --dedicated
```

弱网参数作用于真实 UDP 报文：代理按设定概率丢包，并加入 0 至指定上限的随机单程延迟，能触发 ENet 的可靠传输与重排处理。各客户端使用不同进程帧率。测试驱动先复制已构建程序集到独立缓存快照，之后的开发构建不会修改正在测试的程序集。

测试要求所有存活参与者达到同一逻辑步、最终状态相同、录像重演相同，并以非零退出码报告失败。输出保留于 [Reports](Reports/) 中各次带时间戳的目录。当前实测结果和适用范围见 [验证记录](Reports/VALIDATION.md)。这些是同机多进程和可控 UDP 代理测试，不代表已经完成跨平台或公网多地实测。

录像单独校验：

```zsh
godot --headless --path . -- --verify-replay=/absolute/path/to/match.replay.json
```

## 启动与联机入口

从Godot编辑器运行项目，或在支持图形的Godot .NET环境执行：

```zsh
godot --path .
```

主菜单包含单人游戏、互联网、局域网、影片与制作人员、选项、退出游戏六项。“单人游戏 → 遭遇战”设置2–8个席位、电脑难度、四国、队伍、地图变体、初始资金和单机游戏速度，再点“开始游戏”。未实现的战役与存档入口明确禁用。

“局域网”先进入玩家／游戏列表，点“新建”直接建立房间，或选中已发现的游戏再点“加入”。UDP `19843` 自动搜索兼容房间，默认游戏端口为 UDP `19842`；另提供 IP 直连。房间支持实际国家／队伍调整、聊天和共享地图／资金。至少两个不同队伍、所有来宾接受当前设置后，房主可开始；修改设置会重置来宾接受状态。跨设备与公网 NAT 穿透尚未实测或实现。大厅公共聊天尚未实现，开战前房间聊天和对局内所有人／队友聊天均可用。

“选项”在同一页展示显示、游戏、界面和音频四组控制，右侧进入键盘及网络设置；返回主菜单时保存。分辨率、视觉特效、电脑难度、目标线、隐藏单位标记、提示、卷屏速度、音频总线音量和实际按键分配均已接入。原版菜单动画无音轨，尚未补入原版菜单音乐；互联网入口说明旧在线服务不可用。样式对照、素材来源、支持边界和当前截图见[前端记录](Reports/AUTHENTIC_FRONT_END.md)。基于 oheco broker 的互联网 ENet 接入目前仅完成[可行性分析](Reports/INTERNET_ENET_FEASIBILITY.md)，尚未实现。

遭遇战的电脑等级新增“AI”，该席位由外部程序通过本机 HTTP 控制，默认 `http://127.0.0.1:19844/`；每名 AI 有独立玩家 ID，可查询其视野内状态、规则与地图，并提交带回执和去重 ID 的指令。外部 AI 不运行原有练习脚本，所有操作仍经过费用、前置条件、所有权和视野校验；指令可随录像重演。完整端点、JSON 契约和 Python 示例见[外部 AI 接口](Reports/EXTERNAL_AI_API.md)。

单机和局域网对局均可按 Enter 向所有人交谈、Shift+Enter 向队友交谈；战场左上“交谈”按钮可打开输入框并切换频道。外部 AI 可通过 HTTP 消息端点发送并按游标读取，队伍消息在服务端按真实队伍筛选。操作、接口和验证见[对局聊天](Reports/BATTLE_CHAT.md)。

也可直接运行无窗口服务端和客户端：

```zsh
# 普通房主加七位客户端；--players 包含房主
# --bot 仅为测试生成输入，实际人类客户端不必带此参数
godot --headless --path . -- --server --players=8 --port=19842 --bot --ticks=3600

godot --headless --path . -- --join=127.0.0.1 --port=19842 --country=Russia --name=Commander --bot

# 独立服务端等待八位客户端，服务器自身没有玩家席位
godot --headless --path . -- --server --dedicated --players=8 --port=19842 --ticks=3600
```

Headless 服务端会等到 `--players` 指定的人数后自动开始；`--ticks` 到达时进行最终确认并退出，默认超时为 120 秒，可用 `--timeout=300` 调整。交互房间由房主手动开始。

## 操作

| 输入 | 行为 |
| --- | --- |
| 左键 / 拖框 | 选择己方单位或建筑 |
| Shift + 选择 | 加入/移出当前选择 |
| 双击单位 | 选择附近同类单位 |
| 右键地面 / 敌军 / 矿区 | 移动 / 攻击 / 矿车采矿；选中生产建筑时设置集结点 |
| A 后左键 | 进攻移动 |
| S | 停止 |
| Ctrl + 0–9 / 0–9 | 建立 / 选择编组 |
| 方向键 / 中键拖动 | 平移视角 |
| 滚轮 | 缩放 |
| Space | 返回基地附近 |
| 右键 / Esc | 取消放置、支援或进攻移动模式 |

点击建设条目后在有效地面放置；同一玩家同时进行一项建筑施工。点击兵种条目会加入所选或首个对应兵营/战车工厂的队列。维修/出售作用于选中的己方建筑。“保存录像”写入 Godot 的 `user://Replays`；“返回部署”退出当前对战。

## 代码与素材入口

- [Simulation](Simulation/)：不依赖 Godot 的整数模拟、规则、地图、寻路、经济、战斗、迷雾、校验和录像。
- [Scripts](Scripts/)：Godot C# 前端、输入、HUD、原版图集消费和 ENet。
- [Tests](Tests/)：实际规则场景与确定性/性能回归测试，无外部测试包。
- [网络与模拟说明](ARCHITECTURE.md)：固定步长、命令时序、校验、缓存边界和限制。
- [素材来源](Assets/SOURCES.md)：来源版本、HTTP 区间、SHA256、用途和格式资料。
- [SHP/PAL 转换器](Tools/AssetPipeline/README.md) 与 [VXL/HVA 烘焙器](Tools/VoxelBaker/README.md)：独立 C# CLI、自测和离线接口。

美术转换重建：

```zsh
zsh Tools/AssetPipeline/build.zsh --self-test
python3 Tools/prepare_sprites.py
zsh Tools/VoxelBaker/bake-samples.zsh
zsh Tools/bake_extra.zsh
```

下载和测试编排的少量 Python 脚本只用于开发工具；游戏运行和素材解码/体素烘焙核心均为 C#。
