# RA2 v1.006 遭遇战与生产界面：来源、差异和实施合同

目标是原版 Red Alert 2 v1.006；不将 Yuri's Revenge、CnCNet 扩展客户端或模组行为当作原版事实。联网核对日期：2026-10-22。本文记录独立 UI 实施，不复制其他游戏实现。

## 实际核对的来源

1. [原版英文手册 PDF](http://nyerguds.arsaneus-design.com/manuals/Red%20Alert%202/Red%20Alert%202%20-%20English.pdf)：研究子代理实际通过 HTTP 取到 PDF，并在内存中提取原文；印刷第 12–13 页列出 Game Speed、Credits、Unit Count、Short Game、Super Weapons、Build Near Ally、MCV Repacks、Crates Appear。第 16 页确认从左到右 Buildings / Armoury / Infantry / Vehicles；第 19 页明确坦克与舰船可在工厂/船坞同时生产，第 34–39 页将 Harrier、Nighthawk、Kirov 纳入 Vehicles，第 21 页将伞兵能力头像放入 Armoury。第 40–43 页确认九国专属能力。HTTPS 镜像和多个手册转录站本次不可用，不能把错误页视为证据。
2. [PlanetCNC 原版 RA2 FAQ](http://planetcnc.gamespy.com/Viewf7bc.html?view=GameInfo.Detail&id=34&game=5)：实际 HTTP 200，浏览器读取正文；页面更新于 2002-10-11，明确四个侧栏标签、Harrier/GI/基地并行生产、最多八位玩家，以及 America、France、Germany、Britain、Republic of Korea、Russia、Cuba、Libya、Iraq 九国。该站将 Yuri's Revenge 单列，适合区分原版目录。
3. [PlanetCNC 盟军单位](http://planetcnc.gamespy.com/Viewcc9a.html?view=GameInfo.Detail&id=35&game=5)、[盟军建筑](http://planetcnc.gamespy.com/View972c.html?view=GameInfo.Detail&id=36&game=5)、[苏军单位](http://planetcnc.gamespy.com/View1ef5.html?view=GameInfo.Detail&id=37&game=5)、[苏军建筑](http://planetcnc.gamespy.com/View2cb3.html?view=GameInfo.Detail&id=38&game=5)：本次均实际 HTTP 200。盟军页正文确认 Harrier、Rocketeer、Nighthawk、MCV、Aegis、Carrier、Transport、Destroyer、Dolphin 等原版角色。单位/建筑数值和前置优先使用 construction 从用户原始 rules.ini 核出的数据；网页描述不代替精确规则。
4. [原版规则数据镜像的入口说明](https://games-en-meer.webnode.nl/ra2-ini-editing/)明确区分 RA2 的 rules.ini 与 YR 的 rulesmd.ini；研究子代理实际 HTTP 200 获取并解码 [rules.ini 数据](https://github.com/hzhangxyz/rulesmd.ini/blob/master/rules.ini)，Git blob SHA `fbe3b3395cdf7089a4002ec91cd67267abb18f11`，541923 bytes。[MultiplayerDialogSettings] 实际是资金 5000–10000、默认 10000、步长 100，单位 0–10、默认 10，GameSpeed=0，Crates/ShortGame/MCVRedeploys=yes。construction 另从用户原始 rules 数据核出同值；精确 1.006 指纹以本地原数据溯源为准。镜像是原游戏规则数据，不是其他游戏实现。
5. ModEnc 具体字段页均实际 HTTP 200：[Money 默认值](https://modenc.renegadeprojects.com/Money/MultiplayerSetting)、[MinMoney](https://modenc.renegadeprojects.com/MinMoney)、[MaxMoney](https://modenc.renegadeprojects.com/MaxMoney)、[MoneyIncrement](https://modenc.renegadeprojects.com/MoneyIncrement)、[UnitCount](https://modenc.renegadeprojects.com/UnitCount)、[MinUnitCount](https://modenc.renegadeprojects.com/MinUnitCount)、[MaxUnitCount](https://modenc.renegadeprojects.com/MaxUnitCount)、[ShortGame](https://modenc.renegadeprojects.com/ShortGame)。部分具体页的引擎 fallback 值为资金下限 2500、单位 1–20，与实际 RA2 rules 数据不同；这些不能用作原版滑块上下限。UI 使用上一项实际 rules 值。集中 MultiplayerDialogSettings 总表声明仅保证 YR，不能作为原版补丁专属证据。

## 当前实现差异

检查了 [遭遇战页](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/Main.FrontSkirmish.cs)、[生产 UI](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/Main.Ui.cs)、[定义与设置](/storage/Users/currentUser/dev/redalert-godot-csharp/Simulation/Definitions.cs)、[开局世界](/storage/Users/currentUser/dev/redalert-godot-csharp/Simulation/World.cs) 和 [产线更新](/storage/Users/currentUser/dev/redalert-godot-csharp/Simulation/World.Economy.cs)。

| 项目 | 检查时状态 | 要落地的行为 |
| --- | --- | --- |
| 国家 | 仅美国、俄罗斯、法国、英国；其它旗帜都走英国绘制 | 九国由 Rules.CountryName/Side/Allowed 统一定义，菜单显示九国与对应旗帜 |
| 地图 | 四个种子、同类海岸平原；没有独立 MapId/容量 | 使用 terrain 的真实地图目录、MapId、MaxPlayers；人数不得超过槽位；预览、标题、启动参数一致 |
| 开局 | 预建基地、电厂、矿场、兵营，再给固定六单位 | 默认经典 MCV 开局；初始地面单位数可设；旧测试场景保留独立 Legacy fixture 路径 |
| 设置 | 初始资金 8000，5000–20000；单位滑块锁为 6；四个禁用假开关 | 绑定 MatchSettings 的已实现规则；正确呈现手册设置，不将本地控制与模拟规则混淆 |
| 标签 | 已有建筑/防御/步兵/机械四个原画标签 | 保留四标签；机械集合汇总 Vehicle、Aircraft、Naval，产线/运动域仍相互独立 |
| 生产者 | `Infantry ? Barracks : WarFactory` | 用 Rules API 返回生产者；盟军 Radar/GAAIRC 是空指部，不造重复 Airfield |
| 前置 | 少量 Radar/BattleLab 手工分支；缓存只关注五栋建筑 | 用定义的前置集合及实际拥有的已完成建筑集合刷新目录与提示 |
| 下单 | 正常 Main.Issue 路径已存在 | 所有新单位仍正常 Issue(Train, Actors=[合法producer])；不直接改世界或本地生成 |

## 已冻结并用于实现的模拟/UI 接口

construction 持有 Definitions、原始规则目录和科技树；Lead 持有 Main.Ui hooks、核心移动/战斗及设置语义。sidebar 不修改这些文件。

- Country 保留现有四个 ordinal，追加 Germany、Korea、Iraq、Cuba、Libya；避免旧配置/序列化悄然变成另一个国家。
- MovementDomain：Land / Air / Water；ProductionCategory：Infantry / Vehicle / Aircraft / Naval。二者不能互相推导。
- Rocketeer 是 Air + Infantry，在兵营生产；Kirov 是 Air + Vehicle，在工厂生产；Harrier/BlackEagle 是 Air + Aircraft，在盟军 Radar/GAAIRC 生产；舰船 Water + Naval，在 NavalYard 生产。
- 建筑提供 IsDefense；实际接口为 Rules.ProducerForUnit / Production / UnitRequirements / BuildingRequirements，World.CanSeeUnit / CanProduceUnit / CanTrainUnit。前置类型是 PrerequisiteDef(Building,Faction?)，全列表 AND。CanProduceUnit 不查钱/队列，CanTrainUnit 再核具体生产者、金额、队列和数量上限。
- [独立生产 UI](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/Main.ClassicProduction.cs) 已新增 `RefreshClassicProducts()`、`RefreshClassicProductProgress()`、`TrainClassicProduct(UnitType)`；Lead 将 Main.Ui 对应三个方法委托到这些入口。沿用 cameo、分页、维修出售、聊天与原画框架。
- 头像直接用 `Graphics.Cameo(def.Cameo)`，def 来自 Rules.Unit(type,country)/Building(type,country)，自然覆盖工程师/军犬/基地车/运输船的阵营变体；effects/Lead 持有原始 assets fallback，sidebar 不另写资源映射表。
- Armoury 提供可选 partial hooks `AppendClassicPowerProducts()` / `RefreshClassicPowerProducts()`，供 Lead 独立能力 UI 扩展；不增加第五或第六个标签。
- 国家限制与阵营限制分开：捕获外阵营工厂且 World.CanProduceUnit 成立时，UI 会显示其可生产单位；未解锁的偷科技单位由 CanSeeUnit 隐藏。缓存包含已完成建筑类型/原始阵营和两种偷科技标志。

## 设置语义合同

MatchSettings 已冻结：MapId=""、ClassicStart=true、StartingUnits=10、ShortGame=true、MCVRepacks=true、Crates=true、SuperWeapons=true、BuildNearAlly=true、GameSpeed=0。现有 StartingCredits 默认 10000。UI 对照原 rules：资金 5000–10000、步长 100；初始单位 0–10。

- ClassicStart：默认经典可部署 MCV，不显示手册未列出的 Bases 假开关；Practice() 为经典，旧自动化准备世界显式使用 LegacyFixture()。
- StartingUnits：额外初始地面单位的计数，与 MCV 分开；单位构成与生成由模拟负责。
- ShortGame：开时失去有效建筑且没有 MCV 即败；关时还须消灭全部有效单位。
- MCVRepacks：建造场能重新打包成可移动 MCV；部署依然须合法地形/空间。
- Crates：控制真实随机箱生成与拾取效果。
- SuperWeapons：控制相关建筑/能力的可用性；不能把该选项写成美国伞兵的总开关。
- BuildNearAlly：控制己方建筑能否依托盟友建筑邻接扩展。
- Game Speed：手册和原规则确有速度设置，但 Lead 冻结本轮只支持 GameSpeed=0 和固定 20 ticks/s。设置页不再显示一个没有规则执行支持的速度滑块；恢复完整速度选项仍是与原版的明确差异。

只有真正接到世界语义的设置才能开放为可操作控件。尚未落地的规则必须在交付状态中明确，不以一个能点的开关掩盖缺失功能。

## 最小完整实施与验收

1. 冻结国家/定义/产线接口，落地四标签数据驱动目录；列出所有原版可生产条目，正确显示国家限制、前置和生产者。
2. 刷新缓存来自真实已完成建筑类型集合；科技/生产者变化立即解锁/锁回。低资金/满队列由正常校验拒绝并给提示，不伪造成功。
3. 为机械、飞机、舰船均从 Rules 定义选择正确生产者，选中合法生产者优先，其次选最短队列；右键取消对应条目的最近排队项（若模拟取消API仍仅支持队尾，则遵循API语义并明确提示）。
4. 遭遇战页绑定九国、真实地图容量、实际 MatchSettings；离开地图页/切换 AI 席位保留身份和已选规则。
5. Lead 集中构建及必要短运行检查：每阵营/国家目录可达；Harrier 不在工厂、Kirov 不在机场、Rocketeer 不在机械 tab、舰船不在陆地工厂；变更选项进入新世界确实执行。

## 本次源码交付状态

- [遭遇战页](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/Main.FrontSkirmish.cs) 已实际改为九国、五个真实规则开关、正确资金/单位滑块；真实选项完整传入 MatchSettings，MapId 与 seed 独立，并传目录中的实际矩阵 Width/Height。
- 地图目录直接消费 terrain 的 ClassicMapCatalog 全列表，目前为五张原 multi.mix 官方地图：mp01t4、mp02t2、mp08t2、mp23t4、mp25t6。使用原 PreviewPack 解码 PNG；不生成替代预览。地图缺失/目录无效时禁用启动，地图容量之外的对手席位禁用且不会进入启动玩家列表。
- 网络已实现 LobbyMapId / SetLobbyMap(string) 和完整 LobbySettings；BuildSetupRail 保持原参数签名，显示使用 MapId；房间刷新已调用 `RefreshClassicSetupMap(network.LobbyMapId)`，旧 ShellMapPreview.Seed/seed-to-name 推断已移除。
- [独立生产 UI](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/Main.ClassicProduction.cs) 已实际实现科技/国家/生产者驱动目录、捕获工厂单位、偷科技隐藏/解锁、多个产线队列数量/进度、最短合法队列和正常 Issue 下单。右键只取消同类单位所在产线的队尾，避免误删其它类型。
- 公开检查接口：`GetSkirmishConfigurationAudit()` / `GetClassicProductionAudit()`；原有 sidebar 审计与分页继续可用。源码 whitespace 检查通过。未自行编译或运行，集中构建与世界规则验证仍由 Lead 执行，不能据此宣称所有规则已通过运行验收。

明确保留的差异：本轮无可调 GameSpeed；共享建筑 logical enum 的捕获敌方建造场混合建筑变体仍需核心进一步支持；规则文件精确 1.006 指纹待来源核对。完整世界规则、空海移动/战斗和支持能力执行属于 Lead 的集成工作，不能用 UI 按钮本身证明已完成。

## 网络大厅后续实施（task-24）

已修改 [传输与开局](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/NetworkSession.cs)、[大厅设置](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/NetworkSession.Lobby.cs)、[准备流程](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/NetworkSession.Preparation.cs) 和 [网络房间 UI](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/Main.FrontNetwork.cs)。

- `LobbyMapId` 默认 mp23t4；`SetLobbyMap(id)` 校验本地目录、真实矩阵尺寸和当前人数，返回 bool，不改变 seed。`LobbySettings` 保存全部规则；`SetLobbyRules(settings)` 更新资金、StartingUnits 和五个规则开关，保持权威玩家列表及地图。房主可改、来宾只读，UI 使用原规则范围。
- Lobby/Start 同步完整 MatchSettings、快照 SHA256 和 revision；接受包带相同快照身份。成员、身份、地图或规则变化都会撤销旧接受，旧版本接受包不能放行新设置。加载前、加载后与客户端 Start 都检查同一快照，Ready 同时校验初始世界 hash。
- 普通 wire 包包含本地地图目录 manifest（排序 ID、原始 SourceSha256、矩阵尺寸、人数范围）；选中地图 SourceSha256 单独随 Lobby/Start/Ready 发送。不一致、未知 ID、错误尺寸和超人数均拒绝。它验证来源元数据；派生地图实际加载仍交给核心 repository，初始世界一致性另由 Ready hash 检查。
- 地图容量在新来宾进入、改图、接收 Lobby/Start、Host Start 四处检查。原 ENet 最大连接容量保留，实际席位由当前官方地图限制；大厅列表不再把所有房间标成 /8。
- 房间只按 MapId 刷新原 TextureRect 预览。世界创建仍是原来的 `Task.Run(() => new World(snapshot))`，由 Lead 注册的统一地图 provider 执行，不在网络层再造地图加载器。
- 初始 ready 内容不一致会先向全员发送错误再终止。客户端加载失败会关闭连接；房主在加载阶段检测到断连会通知全员并终止，防止其它玩家等待一个不会 Ready 的连接。

独立只读审查未找到具体编译、正常快照接受顺序、人数或开局包顺序错误；审查指出的两条加载失败通知问题已在上面的路径修正。`git diff --check` 通过。没有执行构建或网络运行；协议号 3 / command-alpha-9 由 Lead 最终统一修改。

### 交给 Lead 的最小短检查

不跑旧网络长 suite。集中构建后可先在已有 Godot 检查入口执行一个纯序列化/验证片段：

```csharp
var players=new[]{new PlayerSetup("A",Country.America,0),new PlayerSetup("B",Country.Iraq,1)};
var settings=new MatchSettings(20261018,players,8300,199,199,MapId:"mp23t4",
    StartingUnits:3,ShortGame:false,MCVRepacks:false,Crates:false,
    SuperWeapons:false,BuildNearAlly:false);
var options=new System.Text.Json.JsonSerializerOptions
    {PropertyNamingPolicy=System.Text.Json.JsonNamingPolicy.CamelCase};
var copy=System.Text.Json.JsonSerializer.Deserialize<MatchSettings>(
    System.Text.Json.JsonSerializer.Serialize(settings,options),options)!;
// Assert: ID/dims/seed/8300/3 和五个 false 完整往返；快照 hash 相等。
var same=NetworkSession.LobbySnapshotHash(copy)==NetworkSession.LobbySnapshotHash(settings);
var valid=NetworkSession.ValidateLobbySnapshot(copy,true,out var error);
var overCapacity=copy with{MapId="mp02t2",Players=[..players,new("C",Country.Korea,2)]};
var rejected= !NetworkSession.ValidateLobbySnapshot(overCapacity,true,out _);
// Assert same && valid && rejected; mp02t2 的 199×199 矩阵不能接收三人。
// 再 assert：mp08t2 保持 199×199 时因实际矩阵应为 119×119 而拒绝；
// 未知 MapId、StartingUnits=11、GameSpeed=1、ClassicStart=false 均拒绝。
```

若还需要一项传输短检查，只建 host + guest：host 切 mp08t2、资金 8300、单位 3、五开关 false，轮询到 guest 的 LobbySettings 一致；guest 接受后 host 改一个规则，确认双方接受撤销且 `StartMatch(true)` 拒绝；重新接受后启动，比较双方世界 Settings/初始 hash。另发一个旧 revision 的接受包，host 不得视为已接受。故意制造初始 Ready hash 不一致时两端应都收到错误。所有检查完成即可，不重复旧截图/多客户端长流程。

本任务不自行构建、不启动或重启 native 游戏，不运行旧的大型 UI 回归。联网研究没有读取或复制任何开源 RA2 重实现。
