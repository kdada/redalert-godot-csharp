# 正常开局对抗自主 AI：自然终局验收

任务 43 原生验收 **PASS**。combat 已完整读取[原始报告](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/final-ai-match-20261022-172752/report.json>)，并通过图像读取工具实际查看[最终 GPU 截图](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/final-ai-match-20261022-172752/04-natural-result.png>)，确认普通结算菜单明确显示“作战胜利”。本次仅补文档，未构建、运行原生程序或修改源码。

本局为 `command-alpha-13`、官方地图 `mp08t2`、美国对俄罗斯；双方各 10000 初始资金、仅 MCV 开局，普通难度。敌方保持项目现有 `PracticeCommander` 自主决策，我方通过实际侧栏和普通军令操作。运行至 **tick 3687** 自然结束：`finished=true`、`naturalResult=true`、`winnerTeam=0`、`localPlayerWon=true`、`localDefeated=false`、`resultUiVisible=true`，结算标题为“作战胜利”。

墙钟耗时为 **32.535 秒**，来自加速原生运行，不代表原版常速对局时长；完整运行上限为 60000 ticks / 900 秒。本局接受 **180** 条命令、拒绝 **0** 条，最终 `blockedProducers=[]`、`error=""`。

| 实际观测 | 美国，我方 | 俄罗斯，自主 AI |
| --- | ---: | ---: |
| 曾观测到的建筑 | 9 | 10 |
| 曾观测到的战斗单位 | 26 | 31 |
| 矿车卸货次数 | 31 | 17 |
| 观测卸下 cargo 的累计下限 | 14250 | 17000 |
| 对对方有效命中 | 1195 | 74 |
| 对对方伤害 | 14345 | 1154 |
| 军队数量峰值 | 19 | 10 |
| 结束时是否战败 | 否 | 是 |

cargo 为逐帧观测的卸货下限，不是精确总采矿收益。我方观测到 7 个非 MCV 单位移除；敌方 10 座建筑和 32 个非 MCV 单位移除。移除统计包含自然战败后的资产清理，不能全部表述为交火击杀。

敌方自主命令包括 `Deploy=1`、`QueueBuilding=12`、`Build=9`、`Train=35`、`Repair=8`、`Attack=25`、`AttackMove=7`，证明该对手实际完成发展、生产、维修与反击。报告的 `bothSidesDevelopedMinedProducedAndHit=true`，并非首次伤害后即停止。

GPU 阶段记录为：[正常开局](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/final-ai-match-20261022-172752/00-normal-start.png>) tick 0、[双方已发展并采矿](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/final-ai-match-20261022-172752/01-both-developed-and-mining.png>) tick 1084、[双方实际交火](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/final-ai-match-20261022-172752/02-mutual-live-combat.png>) tick 1639、[正常视野发现敌方基地](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/final-ai-match-20261022-172752/03-enemy-developed-visible.png>) tick 2634，以及 tick 3687 的最终结算。此文作者实际打开了最终截图；中盘与敌方基地截图由主代理继续人工查看。

报告记录 `directEntitySpawns=0`、`directEnemyOrders=0`、`simulationStateWrites=0`。未由测试脚本生成军队、改写资金或生命、接管敌方、触发投降或强制设置胜利。完整[对局复盘文件](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/final-ai-match-20261022-172752/match.replay.json>)已保存，最终世界状态 hash 为 `bb734535b450d93a`；本次未额外重播验证。

**验收边界：**本局证明当前规则实现下，正常侧栏建设、采矿、付款生产、双方战斗及自然胜负 UI 可以贯通。敌方策略仍是项目的 `PracticeCommander`；这不能证明原版全部 AI taskforces、基地计划或难度策略已逐项复现。
