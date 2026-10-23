# 原图树木与桥梁可攻击破坏

Task38 已完成。Root 的 alpha13 集中构建为 0 warning、0 error，随后实际 Native 验收通过：438次 World.Step、11张真实GPU捕获、error为空、missingConditions=[]、coverageComplete=true，结束后回普通部署菜单。结果见[Native验收报告](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/terrain-destruction/20261022-170543-369038/acceptance.json>)；本分支仅执行隔离纯 Simulation 短检查，没有操作 Native 或集中构建。运行时源码保持冻结。

## 命令、武器与破坏状态

[地形战斗](</storage/Users/currentUser/dev/redalert-godot-csharp/Simulation/World.TerrainCombat.cs>)使用追加的 AttackGround 命令/单位命令状态，保留原有协议枚举值。目标使用稳定负ID，不创建中立建筑或可驻军实体；X/Y 是实际点击格，单位的固定点 GroundTargetX/Y 与寻路目标分开保存。普通右键可攻击可见且有合法武器的树，A 攻击模式或 Ctrl+左键可攻击桥面/空地，桥上的普通右键仍是移动。

实际武器能力决定可否发射，使用原射程、ROF、burst、warhead verses；IFV载员与GI部署选择沿用现有规则，维修/心控/寄生/水下武器不会当作普通地面破坏枪。桥梁按原规则 Wall/Wood 标志与爆破动作允许伤害；V3/Dread 等 launcher 的 SPECIAL 是发射占位弹头，选择时解析实际 V3WH/DMISLWH，落点仍由既有可拦截 ClassicProjectile 结算。Tanya/C4 与 Ivan 使用对应延时爆破，C4 为既有绝对摧毁动作，Ivan 为原400伤害及 IvanWH。

[地图状态](</storage/Users/currentUser/dev/redalert-godot-csharp/Simulation/BattleMap.Destructibles.cs>)为原 placement/span 保存HP与存活状态，按稳定原对象顺序与桥ID建表。普通树死亡重建所有存活原对象的碰撞 OR-mask，保留重叠建筑的阻挡；破坏事件同时使导航版本失效，显示端不再排入该树的深度/前景队列。矿钻 TIBTRE01–03 保留原 Immune=yes，不会作为可毁树移除。

首次 Native 指出了导入数据把原 [Terrain] 对象同时烘入 LandPassable/Buildable 的问题。现在仅对已知树的原 footprint 区分基础TMP支撑与对象mask：底层必须是原可通行TMP类型及草地/泥地，未知overlay保原阻挡；建造按原importer的11/12/13/14类型，且仍检查坡面、矿石、水、桥、建筑与重叠对象。活树继续由原对象mask阻挡，毁树才实际释放。该策略版本为 map-terrain-combat-2 并参与fingerprint/hash；原地图定义未改写。

每个桥 span 使用一份HP。坍塌清除实际 runtime Bridges，恢复地下层 Elevation、清除运行时坡标记，并使陆军通行与水域查询读取坍塌状态。完整桥面的支撑优先于其下方 Rock 岩岸分类，避免原窄车道被水下地形错误切断；水域查询保持原判定，岩岸仍不能下船，塌后也不能通过。原地图定义、BridgeIds、原 source/hash 与 span provenance 保留。原 overlay 桥取原 raw Level；native OVRPS 桥头还需扣除原 TMP TemplateHeight，避免清了图像但 canonical 表面仍停留在桥面。

桥面陆军随支撑面丢失死亡，水域单位保留。地形炮弹/导弹/延时炸弹记录发射/放置时的 impactSurfaceLevelFixed；爆炸先按实际演员支撑层结算，再更新破坏地形。因此同XY桥面坦克会被轰击与坍塌影响，下方船不会被错误当作同层单位打死。该额外弹道/炸弹状态也参与hash。

[确定性检查](</storage/Users/currentUser/dev/redalert-godot-csharp/Simulation/World.ClassicHash.cs>)包含新增规则、全部地形HP/碰撞/坍塌状态、单位固定点瞄准、延时炸弹与弹道高度。Replay 通过正常命令复现，Root 统一更新 Rules.Revision。

## 原数据证据与明确的实现参数

原[rules.ini](</storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Reference/Visual/Metadata/rules.ini>)明确 BridgeStrength=1500；TREE01..TREE30 有 IsFlammable=no，TIBTRE01..03 有 Immune=yes。普通 TREE 的 Strength/Armor 并未在该数据中列出。ModEnc 正文/API访问均403，web_search401，不能把未核实值说成原引擎默认。

因此[共享地形规则](</storage/Users/currentUser/dev/redalert-godot-csharp/Simulation/ClassicTerrainCombatRules.cs>)明确将 TreeStrength=100、TreeArmor=wood 标为 fallback，将 BridgeArmor=concrete 标为当前模拟策略；这些参数均参与content fingerprint。树不额外添加无依据的持续燃烧机制。

Audio任务独立读取的原RA2英文手册写明“Force Fire Hold CTRL key, guide cursor over target, left-click”，并说明可强制射击友军或中立目标。原版补丁记录也确认 Dreadnaught 导弹可以破桥，以及 Crazy Ivan/Tanya 炸弹可以作用全部桥梁；参见[CNCNZ转录的官方补丁历史](https://cncnz.com/features/technical-support-help-guides/red-alert-2-v1001-v1006-patch-history/)。未读取或移植原引擎代码。

## 显示与美术边界

[绘制与命中](</storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/Battlefield.TerrainTargets.cs>)使用实际可见树的原美术 bounds/depth 和真实升高桥面的投影四边形；点击与服务端命令都检查当前可见性，长桥允许点击当前可见桥格，不要求整个桥 origin 同时可见。

保留的高/低桥 state 元数据可证明轴向/拼接/原空帧，却不能证明任意某一索引就是倒塌破损状态。因此没有猜一个帧替换。显示端优先选择同底层类型、同实际高度的原 TMP substrate；无同类平坦Rock底图时，改用同水/陆层的原平坦TMP放在实际underlying level，排除bridge、OVRPS、ramp、extra和带高度的TMP。逐格记录原 tile/subtile/sourceGround/sourceLevel 与实际底层高度，不修改Map通行性。首次 Native 发现的纯黑缺口由这一真实TMP回退修复；[最终桥下穿GPU帧](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/terrain-destruction/20261022-170543-369038/bridge-underpass-after.png>)显示连续海面、存活船与移除的完整桥面。精确断头、废墟与碎片轮廓仍未建立可靠对应关系。

Root依据已证原鼠标表补入Attack53..57、OutRange58..62，interval4、hotspot27,21；保留450帧原atlas像素不重写。Battlefield.TryGetTerrainHoverTarget 与只读 TerrainAttackInRange 供原光标查询使用，原生验收中树/桥两张target-cursor捕获均通过。

## 已实际通过的隔离短检查

隔离项目位于cache目录，不引用 Godot、不写集中游戏 bin/obj，TreatWarningsAsErrors=true，最终运行 exit0：

- mp01t4：Grizzly 真实武器三次伤害摧毁树，55tick内完成从(110,101)真正Move入(111,101)；通行与1x1可建检查均由false变true，55个逐tick序列化命令回放hash全部一致。
- mp23t4：高桥4/target-560，实际岸点(70,73)→(89,73)的19格最短路穿越真实水上桥面；四台V3在415tick内以15次实际伤害毁桥。桥面坦克精确随坍塌死亡，(86,73)同XY下方船保持600HP，支撑由5120降为1024。该桥通路断开，地图仍有不走毁桥的224格合法绕路。mp02t2虽有已证明的水下控制点，但其右岸是自然Rock，未将其当作合法跨岸起终点。
- 标准合法起步：AttackGround 正常接受，25tick后 Replay.Play 的最终hash与原 World 一致。

详见[最终canonical短检查](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/terrain-destruction/canonical-support-checks.json>)；[初次武器短检查](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/terrain-destruction/source-checks.json>)单独保留。

## Root Native 入口

[实际Main检查](</storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/Main.TerrainDestructionChecks.cs>)：

```csharp
Main.RunTerrainDestructionChecks();
Main.GetTerrainDestructionAudit();
```

入口搜索所有已注册官方图，实际 ShowBattle，真实 Viewport.PushInput 发出树右键、A左键、Ctrl左键命令；演员可由fixture生成/放置，目标HP绝不直接赋零，也不直接调用 DamageTerrain。检查真实weapon events、导弹发射/命中、毁树后移动、桥 A*前后与桥上/桥下单位，保存Native postdraw before/flight/after/underpass PNG及JSON。

总上限1600次World.Step、单目标640、90秒；报告保留必测缺失条件和真实美术限制，并在成功或异常后回普通部署菜单。Root已以alpha13集中重建的新DLL运行，实际438步、约7.18秒、11张GPU帧完成，两例及所有required控制通过：树从100→0且真Move抵达、桥从1500→0且19格原桥路线改为224格绕行、桥上坦克死亡、桥下船保留、两种原攻击光标、Ctrl空格攻击、只读/fog/source保留检查均成功。直接目标HP写入和直接DamageTerrain调用均为0。已读取最终JSON及桥下穿after PNG，无纯黑缺口；仍明确不声称精确broken rubble状态已考证。
