# Vanilla 1.006 战斗核心交付与验收

主代理已集中重新构建并执行 `--classic-combat`：**9/9 PASS，396/900 个受限模拟步，583 ms**。全部行为断言保持原样通过；验收后 combat 仅补本报告，没有再次修改核心源码。运行与构建由主代理统一完成，combat 未另启编译、原生游戏或旧长回归。

检查入口是 [ClassicCombatChecks.cs](</storage/Users/currentUser/dev/redalert-godot-csharp/Tests/ClassicCombatChecks.cs#L46>) 的 `RedAlert.Tests.ClassicCombatChecks.Run(string[] args)`。检查使用 900 个 `World.Step` 的总硬上限，超限直接失败。主代理回传结果按实际检查输出字段记录如下：

```text
Classic combat: 9/9 passed; 396/900 capped world steps; 583 ms.
```

## 已通过的九项行为检查

| 检查 | 实际断言覆盖 | 结果 |
| --- | --- | --- |
| 目标平面与原版装甲列 | 灰熊不能打飞行火箭兵；神盾只打空中目标；驱逐舰检测并选择反潜武器；潜艇不能鱼雷打陆地坦克；停机飞机转为地面目标；AP/HollowPoint 按原装甲倍率计算；免辐射矿车不受辐射束伤害。 | PASS |
| GI 部署 | 部署使用 Para 的射程，超过未部署 M60 的射程仍能开火，保持原地，并可收起。 | PASS |
| 心灵控制 | 尤里建立真实所有权与控制关系；控制者死亡归还原所有者；免疫矿车不能被控制。 | PASS |
| 超时空抹除 | 目标冻结并积累抹除进度；攻击者死亡解除冻结且保留目标生命；持续照射最终完成抹除。 | PASS |
| 间谍、C4 与伊万 | 侵入苏军原型实验室解锁相应偷取科技；间谍消耗且实验室不易主；C4 使用 9 tick 延迟并保留谭雅；伊万使用 150 tick 引信并在到期后造成爆炸伤害。 | PASS |
| 寄生、辐射与自爆 | 恐怖机器人进入宿主并持续伤害，维修去虫解除双向关系；部署辐射杀伤步兵并豁免免疫矿车；自爆卡车造成真实范围伤害。 | PASS |
| 无畏导弹与防空拦截 | 发射两枚具有真实 ID、300 伤害和 DMISLWH 弹头的导弹；神盾在命中前击落导弹，被攻击建筑生命不变。 | PASS |
| 铁幕与两阶段超时空传送 | 铁幕保护车辆、杀伤有机目标并阻止真实来袭伤害；超时空发生器先选起点再选终点，传送车辆、消灭步兵并设置恢复时间。 | PASS |
| 天气与核弹 | 天气在延迟结束后产生雷击伤害；核弹经过飞行延迟后爆炸，并留下持续辐射。 | PASS |

## 交付内容与原始数据

- [World.Combat.cs](</storage/Users/currentUser/dev/redalert-godot-csharp/Simulation/World.Combat.cs>)：统一武器选择、定点射程、目标筛选、伤害入口和建筑防御；友军范围伤害保留，击杀晋级仅统计敌军。
- [World.ClassicCombat.cs](</storage/Users/currentUser/dev/redalert-godot-csharp/Simulation/World.ClassicCombat.cs>)：79 个原始弹头的 11 装甲列倍率、传感器与目标隐蔽、IFV 乘员武器选择、舰载机及导弹实体、真实开火表现事件。
- [World.ClassicAbilities.cs](</storage/Users/currentUser/dev/redalert-godot-csharp/Simulation/World.ClassicAbilities.cs>)：特殊能力、超级武器、所有权恢复、定时炸弹、抹除与寄生生命周期、辐射与天气状态，以及对应 Unit/Building/PlayerState partial 状态。

弹头倍率和单位目标特性来自本地原版 [rules.ini](</storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Reference/Visual/Metadata/rules.ini>)，没有复制原版引擎实现。原始数据 SHA-256 已记录在生成表旁：

```text
fd1e95cea0306ea78049dc81c8cd816e18c28c496872a1ff02edd50bd082062f
```

## 集成修正与验收边界

集中运行发现目录中的武器与弹头 ID 已统一大写，特殊分派仍使用区分大小写的比较。该根因已修正为原版 INI 所需的大小写无关语义，覆盖心灵控制、抹除、寄生、反潜与舰载发射等路径。测试中的调试生成单位会明确准备视野；生产逻辑的 `OrderSpecial` 可见性验证保留，未删除或弱化行为断言。

生命周期还补上了抹除者进入运输工具、目标切换或失去行动能力时释放照射，以及隐藏于运输工具的部署辐射兵停止刷新辐射。超级武器取消超时空终点选择使用既有命令的 `Target=-1`，不消耗充能且不要求有效目标坐标。

上述九项短检查证明所列行为在当前集成中通过。它们不是完整原版引擎等价性证明；舰载飞机与导弹采用当前模拟的定点二维飞行表示。原生音频、真实地图表现及其余环境工作由主代理另行验收，本报告仅确认战斗核心任务 23 的交付结果。
