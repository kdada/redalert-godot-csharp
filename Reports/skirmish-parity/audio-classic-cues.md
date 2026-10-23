# 宝箱与新增兵种：原版音效接线

本轮增加 96 个真实原录音，现 41 个文件继续保留，运行时音频表合计 **137** 个文件。本轮数据映射复用其中 23 个旧录音；覆盖 47 个 UnitType、112 个原武器 ID 与 130 个 sound.ini Sound.Id。没有合成、归一化、重采样或借用非原版替代音。

## 文件与来源

- [GameAudio.cs](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/GameAudio.cs) 合并原音频表，保留现有有界混音池、菜单/战斗音乐、EVA 就绪播报与无头禁用规则。
- [GameAudio.ClassicCues.cs](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/GameAudio.ClassicCues.cs) 消费真实武器和宝箱事件，处理原单位选择/移动/CreateSound。
- [GameAudio.ClassicCueCatalog.cs](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/GameAudio.ClassicCueCatalog.cs) 是从原 INI 生成的纯数据映射，未读取或改变模拟随机数。
- [独立提取脚本](/storage/Users/currentUser/dev/redalert-godot-csharp/Tools/extract_skirmish_audio.py) 校验已保留的原 rules.ini、sound.ini、audio.idx SHA256，再提取指定 BAG 样本。
- [原数据选择表](/storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Reference/Audio/SkirmishCues/selection.json) 与 [来源及转换清单](/storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Reference/Audio/SkirmishCues/manifest.json) 保存每个原 Sound.Id、样本名、原字段、精确 archive offset、HTTP Content-Range、原始及 PCM 文件 SHA256。

固定源版本为 [acfa5b834899e7fab4c8499ccb4b49c402927b44](https://github.com/toarrntnz/ts-redalert2-assets/tree/acfa5b834899e7fab4c8499ccb4b49c402927b44)，原录音位于 language.mix/audio.mix/audio.bag。游戏音频权利仍属 EA/Westwood 等原权利人；镜像元数据许可不改变游戏音频权利。

最终 96 个样本共读取 **1,379,698 字节**，每次必须为精确 HTTP206 byte range，脚本预算 4,000,000 字节；拒绝整包响应。补齐 CreateSound/载机 launch 前后两次提取共传输 2,624,328 字节，同样未达到该预算。没有下载整个 MIX、BAG 或游戏程序。

原始样本保留在 Reference/Audio/SkirmishCues/Original；可导入的派生 PCM 位于 Generated/Audio/SkirmishCues，独立于现有 41 个音源和 ClassicExpansion 视觉数据。Reference/Audio 的现有 .gdignore 不会把派生 PCM 排除出导入/导出。

## 真实事件与播放时机

已读现有 Main 与 NetworkSession 接线：

- [本地 Step 回调](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/Main.cs:156) 每次 Step 后立即 AudioCaptureTick，位于下一次 Step 之前。
- [网络 StateAdvanced 回调](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/Main.cs:112) 同样立即 AudioCaptureTick；[客户端](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/NetworkSession.cs:299) 和 [主机](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/NetworkSession.cs:345) 每次 Step 均单独触发 StateAdvanced。
- [模拟 Step](/storage/Users/currentUser/dev/redalert-godot-csharp/Simulation/World.cs:105) 在 Tick++ 之前调用 UpdateClassicWorldState 与 UpdateClassicEconomy。因此音频仅接受 event.Tick == world.Tick-1，再由现有 lastTick guard 保证一次 Observe，不会在无新事件或结束后的保留列表上重播。

武器声音现在只取 [ClassicWeaponEvent](/storage/Users/currentUser/dev/redalert-godot-csharp/Simulation/World.ClassicCombat.cs:16) 的真实 WeaponId；该事件在实际 Fire/扣弹时产生。Primary、secondary、IFV 载员武器、部署武器以及舰载机/导弹 launch 均据原 Weapon.Report 解析，不再把命中伤害事件误当开火，也不会随着 AoE 次伤重复播放。Damage 事件仅负责实际 hit/death，零伤且非爆炸的间谍等表现标记保持无声。

载机和导弹的原 launcher 武器没有 Report。对应原子对象 AuxSound1 是起飞/发射音，因此按以下原字段接线，直接在 launch 事件播放：

| 实际武器 ID | 原对象字段 | 原 Sound.Id | 原样本 |
|---|---|---|---|
| HornetLauncher | HORNET.AuxSound1 | HornetTakeoff | vhortaka |
| ASWLauncher | ASW.AuxSound1 | OspreyTakeOff | vospstaa |
| DredLauncher | DMISL.AuxSound1 | DreadnoughtAttack | vdreatta |
| V3Launcher | V3ROCKET.AuxSound1 | V3Attack | vv3latta |

敌方隐藏的开火源继续静音，距离衰减按开火事件保存的真实源坐标计算。有界池仍为 12 Effects/3 Voice，每 tick 最多五个开火 cue；pitch 变化只由音频读取事件字段计算，不回写单位、世界、Replay 或模拟 RNG。

## 宝箱四种原音

[ClassicCratePickups](/storage/Users/currentUser/dev/redalert-godot-csharp/Simulation/World.ClassicEconomy.cs:39) 必须在真实奖励发生后消费，未通过显示箱体、鼠标悬停或推测种类提前播放。

| 奖励 | 原 Sound.Id | 原样本 |
|---|---|---|
| Credits | CrateMoney | ubonus |
| Repair | HealCrate | gcraheal |
| Reveal | CrateReveal | bspyon |
| Veteran | CratePromoted | gcraprom |

四种原数据 Type=global、MinVolume=80；本轮使用收集者本人的全局 Effects cue，不让隐藏敌方奖励通过声音泄露。观察者不接收玩家私人奖励 cue。

## 原单位选择与就绪

VoiceSelect/VoiceMove 按实际原单位规则映射，复用已有 GI、Conscript、通用车辆、狙击手与军犬录音。新增步兵、机器人、飞机、海军采用各自原 Sound.Id；Harrier/BlackEagle 的无线电音也按原 INI 保留，没有补造台词。

CreateSound 对 Tanya、ChronoCommando、Kirov、DemolitionTruck 使用对应原创建提示，实际出现新的己方单位 ID 时才播放。既有 Allied/Soviet EVA_UnitReady 继续覆盖普通生产完成，因此没有给没有 CreateSound 的单位臆造就绪台词。

默认 Selection 参数与现 Main 保持兼容。若 Lead 要修正占领工厂生产的 Engineer/MCV/Transport 语音阵营，已有 `SelectionFor(Unit selected,bool order=false)` 可直接按 unit.OriginCountry 选择，而不是按当前 owner 国家；本轮按要求没有修改 Main。

可用于原生审计的新增计数：ObservedOriginalWeaponEvents、MissingOriginalWeaponCues、IntentionalSilentWeaponEvents、ObservedCratePickups、PlayedCrateCues、PlayedOriginalCreatedCues。现 Main.GetAudioAudit.loadedClips 在所有文件成功加载时应为 137；Lead 可自行将这些计数添加到其 Main 审计入口。

## 格式与验证

原 BAG 的大多数样本为 flags=12 的单声道 IMA ADPCM；CratePromoted 等 flags=6 的录音已经是原始单声道 PCM16，直接包成 WAV，PCM 数据字节不变。格式标记仅参考 [OpenRA 原 BAG 格式说明实现](https://github.com/OpenRA/OpenRA/pull/7573)，使用既有独立标准 IMA 解码器与标准 WAV 写入，未复制其游戏代码。

原 sound.ini 的 Dummy 与 OspreyCollision 明确 Volume=0 且无 Sounds，保持故意静音；没有用通用枪声补齐空 Report。原 `[CowShot];[PrismShot]` 行按 Westwood 的第一个闭方括号解释，保留原注释语义。

可重复验证命令：

```text
python3 Tools/extract_skirmish_audio.py --plan
python3 Tools/extract_skirmish_audio.py --validate
```

离线验证已通过：原 metadata/source/runtime SHA256、精确 HTTP206 range 证据、PCM16 mono header、原 PCM 字节保持、非静音信号、所有 cue 绑定可解析、武器键无大小写冲突。音频成员没有自编译或 native launch，没有运行旧长套件。

Lead 随后报告其最新统一构建为 0 warnings/errors，原生游戏已启动，GetAudioAudit 实测 loadedClips=137、musicPlaying=true。这确认全部运行时音源成功加载且音乐实际播放；该原生结果由 Lead 提供，音频成员未重复启动。

真实开火与宝箱播放计数仍待 Lead 的 mutable core / 原生 play 验证。当前 Stopped inspection 不执行 World.Step，观察到的开火事件计数为 0 符合预期，不能作为实际触发通过证据。后续应在真实 Step 后检查 event.Tick == world.Tick-1，并读取 ObservedOriginalWeaponEvents、MissingOriginalWeaponCues、PlayedCrateCues；同一 world.Tick 重复 Observe 不应重播。

后续正常原生 gameplay 验收已通过，以上真实触发待验项已有实测证据。音频成员已读取 Lead 生成的 [52 tick 验收记录](/storage/Users/currentUser/dev/redalert-godot-csharp/Reports/skirmish-parity/gameplay-20261021-185126/report.json)，其中 completed=true、passed=true。本轮通过正常 `_Process`、`Issue` 和 `AudioCaptureTick` 推进游戏；Lead 只放置真实宝箱，由 `UpdateClassicEconomy` 实际拾取，没有直接伪造音频触发。

| 实测项 | 结果 |
|---|---:|
| 正常推进 tick | 52 |
| 本地 / AI 基地车展开 | true / true |
| accepted / rejected commands | 4 / 0 |
| loadedClips / musicPlaying | 137 / true |
| observedOriginalWeaponEvents / weaponEventsDelta | 13 / 13 |
| missingOriginalWeaponCues / intentionalSilentWeaponEvents | 0 / 0 |
| observedCratePickups / playedCrateCues / crateCuesDelta | 1 / 1 / 1 |
| playedCues | 27 |
| playedOriginalCreatedCues | 0 |

该记录确认正常原生模拟中的真实开火事件能解析原音效，真实宝箱奖励进入播放管线；本轮没有产生带专属 CreateSound 的生产事件。此次只追加验收证据，未改源码、未另行构建或启动原生游戏。
