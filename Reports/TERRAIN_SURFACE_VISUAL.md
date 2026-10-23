# 地形层级、桥梁与车辆坡地姿态修复

验证日期：2026-10-21（Asia/Shanghai）。本轮已进入实际原生游戏，直接读取运行中 Viewport 的 GPU 图像，并用 read_image 查看画面。截图不是重画的示意图，也不是 headless 结果。

## 已修复

- 地面整块与单位按中心深度混排，导致坦克处在同一格后半部时被地面盖住。现在先完成地面、桥面与矿石绘制，再绘制单位；较高前景与真实垂直面通过几何裁剪保留合理遮挡。
- 旧地图把任意一级台阶当作可通行坡道。现在使用平坦高地、明确坡向、固定点连续高度和共享边匹配；未标记的悬崖不能通行，坡地不能建造。
- 旧桥梁只是道路碰到水后的逐格标记。现在生成三个有轴向、长度、宽度、统一桥面高度和两端陆地入口的完整桥跨，并绘制连续护栏、立柱和支撑。
- 地面贴图边缘透明采样产生明显黑色格缝。现用原图不透明像素的平均色在同一共享多边形下填底，保留原始纹理像素与 UV。
- 车辆以车前、车后和左右支点的实际表面高度计算俯仰与侧倾，影子、车体、炮塔和炮管采用同一支撑变换；跨越不可通行悬崖的邻格不作为虚假支撑。

## 原生环境与复查结果

- Godot 4.7.2 stable custom build，OpenHarmony 显示服务，Mobile / Vulkan，实际帧缓冲 2090 × 1306。
- 当前工程会话：`redalert-godot-csharp@5ee76591db9aaaec`；最终验证运行：`r1782110-3`。
- 原生编辑器由用户打开后，Lead 通过现有编辑器运行游戏。终端 headless launcher 的图形启动失败不计入成功验证。
- 检查入口只用于调试：静态取景时冻结模拟并揭示地图；运动取景仍使用正常 Move 命令、寻路和 World.Step，不使用重建的动画。
- 最终构建 0 warnings、0 errors；最终原生运行日志只有调试辅助节点注册信息，没有游戏错误。结束检查时游戏已停止，编辑器处于 ready；再次运行进入正常主菜单。
- 环境、通行 tick、姿态和最终状态汇总见 [原生运行记录](terrain-visual/native-runtime.json)。

| 检查点 | 实际结果 | 原生证据 |
| --- | --- | --- |
| 平地原错误位置 | 坦克中心 `(18.21973,52.21973)`，位于本格后半部，完整履带、车体和影子可见 | [最终画面](terrain-visual/flat-ground-final.png) · [帧数据](terrain-visual/flat-ground-final.json) |
| 桥面 | 坦克高度 1，水面高度 0，桥面与两岸同高；护栏、立柱与支撑可见 | [桥面画面](terrain-visual/bridge.png) · [帧数据](terrain-visual/bridge.json) |
| 实际过桥 | 从 `(49.5,47.5)` 经过桥面，到 `(56.14453,47.5)` 的对岸单元；47 ticks 后停止，高度始终为 1 | [桥中运动帧](terrain-visual/bridge-motion-03.png) · [桥末运动帧](terrain-visual/bridge-motion-06.png) · [到岸数据](terrain-visual/bridge-arrival.json) |
| 实际上坡 | 低地高度 1 → 连续坡面 → 平台高度 3；中途高度 `1.6611328125`，俯仰约 `14.93°` | [上坡运动帧](terrain-visual/ramp-up-motion-01.png) · [中途数据](terrain-visual/ramp-up-motion-01.json) · [到达数据](terrain-visual/ramp-up-arrival.json) |
| 实际下坡 | 平台高度 3 → 连续坡面 → 低地高度 1；中途高度 `2.3388671875` | [下坡运动帧](terrain-visual/ramp-down-motion-01.png) · [中途数据](terrain-visual/ramp-down-motion-01.json) · [到达数据](terrain-visual/ramp-down-arrival.json) |
| 横向停在坡面 | 高度 1.5，侧倾约 `14.92°`，俯仰 0；履带和车体完整 | [侧倾画面](terrain-visual/ramp-cross.png) · [帧数据](terrain-visual/ramp-cross.json) |
| 高地平台 | 高度 3，车辆俯仰和侧倾均为 0，平台表面连续，边缘有垂直高差 | [平台画面](terrain-visual/plateau.png) · [帧数据](terrain-visual/plateau.json) |

过桥连续保存了 7 帧；上坡与下坡分别保存了 4 帧。三次短通行累计执行 85 个模拟 ticks，未运行旧的八人长局、长网络或长回放回归。

黑缝修复前的中间画面保留为 [早期坡地截图](terrain-visual/ramp-up-initial.png)。该图已经包含坡地与遮挡修复，但还没有最终填底和桥梁细节，不能作为原始版本的完整前后对照。

## 必要短检查

```sh
dotnet build Tests/RedAlert.Tests.csproj --no-restore -v:minimal
dotnet run --project Tests/RedAlert.Tests.csproj --no-build -- --terrain-surface
dotnet build RedAlert.csproj --no-restore -v:minimal
git diff --check -- Scripts Simulation Tests NuGet.Config
```

[表面检查记录](terrain-visual/surface-check.json)：5/5 通过，覆盖 4 个种子、全部四个坡向、固定点高度与法线、悬崖和坡侧阻挡、建造限制、16 个坡地单元、3 个完整桥跨、八个出生点资源通路及新地形数据散列。检查本身不推进 World；原生通行记录单独保存。

地形规则修订为 `command-alpha-7`，坡向、桥格编号和桥跨全部元数据进入确定性状态散列。

## 表现范围

车辆坡地姿态采用已有 VXL 朝向图集的二维仿射近似，没有重新生成带俯仰和侧倾的三维 voxel 视角。桥梁和悬崖的结构几何为独立实现，地面和车辆继续复用允许使用的原版美术数据。河面仍能看到少量很淡的斜纹，本轮已消除明显黑色格缝；单张画面无法将淡纹完全归因于原纹理或采样边界。
