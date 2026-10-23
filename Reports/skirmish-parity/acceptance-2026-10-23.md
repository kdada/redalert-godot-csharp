# 原版遭遇战体验修正与最终验收

验收日期：2026-10-23（Asia/Shanghai）。目标为 vanilla Red Alert 2 v1.006，非尤里的复仇或模组默认规则。复用原始资产与数据，未复制原游戏引擎实现。当前规则版本 `command-alpha-13`，Protocol 3。

最终集中构建成功，0 warnings、0 errors；`git diff --check` 通过。最后一次实际游玩为正常开局对抗自主 AI，已自然产生胜负并实际检查 GPU 结算截图。下列 fixture 检查与正常开局分别标注，旧失败证据仍保留。

## 最后一次完整 AI 对战

[原始报告](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/final-ai-match-20261022-172752/report.json>) · [说明与完整截图索引](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/skirmish-parity/final-ai-match-verification.md>) · [保存的录像](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/final-ai-match-20261022-172752/match.replay.json>)

- 官方图 `mp08t2`，美国对俄罗斯，正常难度，双方各 10000 资金、仅 MCV 开局，初始作战单位为 0。
- 我方通过实际侧栏、部署键和普通军令操作，敌方保持自主 PracticeCommander。未直接生成实体、接管敌方或改写资金、血量及模拟状态。
- tick 3687 自然结束，winnerTeam=0；普通结算菜单真实可见，标题为“作战胜利”。我方获胜。32.535 秒为加速游玩的墙钟时间，不代表原版游戏节奏逐秒一致。
- 180 条命令被接受，0 条被拒绝；最终无堵塞生产者。双方均有发展、实际矿车卸货、正常产兵与有效命中。
- 我方观察到 9 栋建筑、26 个作战单位、31 次矿车卸货，卸货金额下限 14250；敌方观察到 10 栋建筑、31 个作战单位、17 次卸货，下限 17000。卸货统计按采样帧计算，不冒称完整经济流水。
- 敌方自主提交 Attack 25 次、AttackMove 7 次、Repair 8 次等指令；对我方造成 1154 伤害。我方损失 7 个单位。实体移除统计包含战败清理，不能全部解释为直接交火击杀。
- 记录最终 hash `bb734535b450d93a`。已保存完整录像，本轮没有再次重放整段最终录像。

![正常对抗 AI 后的自然胜利结算](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/final-ai-match-20261022-172752/04-natural-result.png>)

## 修改及验证结果

| 内容 | 实际结果与证据 |
|---|---|
| 边缘方向、受限滚动与 MCV 部署光标 | [最终 17 项验收](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/direction-deploy-20261022-170037-62463/acceptance.json>)通过；8 个方向、侧栏交界、无效部署、普通点击展开及 D 键重部署均覆盖。 |
| 车辆坡面姿态 | 使用原 VXL/HVA 的车体、炮塔与地面阴影姿态；[坡面验收](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/vehicle-slope/20261022-154040-23043/acceptance.json>)保存 34 张真实截图，覆盖官方地图可用坡向及桥面。该模块此后未改。 |
| 矿石、水晶、实时余量 | 换用准确的原 TEMPERAT 调色盘，原 SHP/索引/锚点/alpha 保留。[新配色 Native 报告](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/skirmish-parity/ore-visibility-20261022-170133/report.json>)8 张捕获通过，0 fallback、隐藏数量无泄漏；真实采矿 400→200→150，悬停显示 150 资金/6 包。 |
| 空资源提示框 | 只隐藏空文字的 PopupPanel。[最终海战截图](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/full-playthrough-20261022-172539-naval/05-sea-combat.png>)已实际检查，船旁空黑黄框消失。 |
| 开图、Gap、卫星与迷雾边缘 | [最终 21 项验收](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/shroud-parity-20261022-172458-36707/acceptance.json>)通过，5 张真实 GPU 捕获；旧内黑带、亮牙、未知格灰墙已修。未知资源、目标地形及竖直面的队列泄漏计数均为 0。 |
| 树木与桥面攻击、破坏、通行 | [最终原生报告](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/terrain-destruction/20261022-170543-369038/acceptance.json>)438 步/11 张 GPU 捕获通过，必测缺项为空。树毁后实际移动入格；高桥坍塌后原 19 格通路失效，改走 224 格合法绕路；桥面坦克死亡、同位置桥下船保持 600 HP，海面无旧黑洞。 |
| 渐进扣款与队列 | [Core 检查](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/skirmish-parity/production-core-alpha13.json>)含 8 项资金 fixture 和 5 项出口 fixture；[真实侧栏检查截图](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/production-funding-20261022-172012/sidebar-funding.png>)覆盖低资金开工、暂停/继续及二次右键退款，12 起始资金、实付 10 后退款恢复 12，6 条接受/0 拒绝。 |
| 正常盟军陆空流程 | [报告](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/full-playthrough-20261022-170845-land-air/report.json>)通过，4288 ticks、4 次伤害事件；正常部署、建设、采矿入库、造兵、交战及飞机返场补弹。实际检查返场飞机 GPU 图像。 |
| 正常苏军陆空流程 | [报告](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/full-playthrough-20261022-171634-land-air/report.json>)通过，5937 ticks、6 次伤害事件；正常生产与基洛夫轰炸、地面交战。实际检查空战 GPU 图像。 |
| 正常双方海军流程 | [最终报告](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/full-playthrough-20261022-172539-naval/report.json>)通过，7085 ticks、3 次双向伤害事件；正常建设船厂、生产驱逐舰与潜艇、航行至双方可见水域并互相命中。该流程的敌方普通指令由操作者给出，不能与最后自主 AI 对战混称。 |

侧栏放置操作者增加有限次数真实点击重试：合法格可能在一帧之间被矿车临时占用。没有绕过普通放置规则；原先单次点击超时的报告仍保留。最终海军流程通过，0 次额外重试、0 次拒绝。

## 原版依据

[原版遭遇战研究记录](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/original-skirmish-research/2026-10-22.md>)与[规则研究及生产差距落实记录](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/skirmish-parity/rules-research.md>)保存了来源与判断。

- [EA 原版手册 OCR](https://archive.org/download/Command_Conquer_Red_Alert_2/Command__Conquer_Red_Alert_2_djvu.txt)：低资金等待并随收入恢复，右键暂停、再次右键取消并退还已支付资金，以及 Ctrl 强制攻击。
- [v1.001–v1.006 补丁历史](https://cncnz.com/features/technical-support-help-guides/red-alert-2-v1001-v1006-patch-history/)：核对最终版本桥梁武器、苏军兵营结构性堵门退款等行为；v1.006 原始规则覆盖老手册数值。
- 光标、矿物、坡面与迷雾均有原始文件校验和及资产链；未把 YR/模组默认规则作为 vanilla 事实。

## 当前仍明确保留的边界

- 对手策略仍是项目 PracticeCommander。完整对局证明经济、生产、战斗及自然胜负流程可用，没有证明原版全部 AI taskforces、baseplans、海空扩张与难度策略都已复刻。
- 迷雾已恢复原始帧的外向羽化及正确透明方向；高度断层仍有少量保守黑侧面/小缺口。原始 alpha 数值归一化、原版低电行为的逐项 Native 对照尚未全部证明。不能称像素完全一致。
- 树木缺省 100 HP/wood 尚无原生默认值的确证；TIB02 密度帧循环顺序仍有推断。具体假设在各专项报告保留。
- 桥毁后使用真实原 TMP 底层素材；精确废墟/断桥头帧映射尚未确证，桥梁维修小屋闭环未实现。
- 工厂出口和堵塞规则已验证，完整出厂门动画与内部行进尚未实现；多工厂同类生产速度/队列的完全一致性尚未建立。
- 官方图未提供某些正对角坡型的实际 GPU 样本，该部分只完成数学检查。

正常对局、受控 fixture、原始素材验证各自提供证据；通过当前验收不等于已证明整个原版游戏逐行为、逐像素一致。
