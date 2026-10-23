# Vanilla 1.006 出厂出口修复与验收

任务 41 已通过主代理统一构建的 alpha12 原生检查：**出口 5/5 PASS，11/40 个受限 World.Step，errors=[]**。同一份[原始验收结果](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/skirmish-parity/production-core-alpha12.json>)还记录了任务 42 的付款模型集成检查：**8/8 PASS，244 步，errors=[]**。combat 未另行构建、启动原生游戏或重跑长流程；本次收尾仅读取结果并补充文档，全部源码继续冻结。

## 修复结果

原实现以工厂右侧为起点，在最多 15 格半径内寻找任意空格，未验证真实出口及连通性。工厂被墙、建筑或悬崖堵住时，产品可能直接生成到另一侧。

[World.ProductionExit.cs](</storage/Users/currentUser/dev/redalert-godot-csharp/Simulation/World.ProductionExit.cs>)现以原始出口数据确定门外紧邻地基的出口，检查可通行性与内外沿地形衔接；船坞仅检查紧邻地基且水面衔接合法的水上出口。地面产品不再用远距离空格搜索绕过堵塞。

| 受限检查 | 已验证的行为 | 结果 |
| --- | --- | --- |
| 车辆出口堵塞与恢复 | 墙堵住门口时保留订单、满进度及已付款；拆墙后只生成一辆，位置位于合法出口。 | PASS |
| 苏军兵营结构堵塞 | 本单准确退款一次，不生成原单位，也不向复制中心排入复制品。 | PASS |
| 单位暂时堵门 | 等待期间不退款、不复制；清除堵门单位后生成一个原单位及一个待复制订单。 | PASS |
| 悬崖与相邻墙体 | 不跨越出口高差；邻墙没有占住实际门口时，合法出口继续可用。 | PASS |
| 俘获工厂与坐标精度 | 原始 384/96 lepton 偏移保留；美军玩家控制苏军原型工厂时仍生产具有俄罗斯 OriginCountry 的犀牛，FoundationCountry 不变。 | PASS |

检查入口为 [Main.ProductionExitChecks.cs](</storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/Main.ProductionExitChecks.cs>) 的 `RunProductionExitChecks()`。结果明确标注 `fixtureOnly=true`、`ordinaryBattleModified=false`；上述 11 步来自隔离夹具，并非完整对局时长。

## 原始依据与集成边界

- [原始规则的盟军战车工厂出口](</storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Reference/Visual/Metadata/rules.ini#L8448>)与苏军战车工厂均为 `ExitCoord=384,96,0`。按 256 leptons/cell 转为 Scale=1024 的定点坐标，不把 96 擅改为格中心。
- [盟军兵营规则](</storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Reference/Visual/Metadata/rules.ini#L9070>)为 `ExitCoord=-64,64,0`；[苏军兵营规则](</storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Reference/Visual/Metadata/rules.ini#L9004>)为零偏移。当前零偏移的苏军兵营、复制中心采用模拟层的 +X 门面约定。
- [原始矿场美术数据](</storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Reference/Details/art.ini#L1164>)的 `QueueingCell=4,1`用于矿场门外点；[船坞数据](</storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Reference/Details/art.ini#L1752>)的 `DockingOffset0=384,-128,0`仅用于排列相邻水上出口的优先顺序。
- [官方累计补丁转录](https://cncnz.com/features/technical-support-help-guides/red-alert-2-v1001-v1006-patch-history/)的 v1.005 明确写道：“If walls or other buildings block the Soviet Barracks exit, no unit is built even if the user has a Cloning Vat. The cost is refunded to the player.” 退款规则只适用于相应苏军兵营结构堵塞，不扩展到单位拥堵、车辆或地形阻塞。

任务 42 集成后的[生产完成路径](</storage/Users/currentUser/dev/redalert-godot-csharp/Simulation/World.Economy.cs#L53>)按 `ProductionPaid` 退款并重置付款状态；已付款的结构堵塞退款先于复制中心积压容量限制处理。原始验收结果中的 8 项付款检查覆盖了该顺序、单次退款、原单位与复制品只花费一次，以及俘获后退回旧主已付款的行为。

门口适配器未新增单位出厂阶段或动画状态，也未修改任务 36 已验收的机场停机逻辑。**完整门内行军、SHP 门动画与实体出门同步尚未实现**；`doorInteriorAnimationImplemented=false` 已写入真实检查结果，不能将本修复表述为完整原版出厂动画还原。

主代理另外回传：本轮正常盟军流程 7 阶段 PASS、4289 ticks、0 rejected commands；正常海军生产与双方实际对射 PASS、7068 ticks，两舰生命均由 600 降到 500，最后开火 tick 分别为 7035/7053。此处按主代理回传记录，不将隔离夹具结果冒充完整对局证据。
