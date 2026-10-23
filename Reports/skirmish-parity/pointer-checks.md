# 原生指针与五种原版光标：七项短验收入口

入口已写入独立 [Main.PointerParityInspection.cs](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/Main.PointerParityInspection.cs)。没有修改既有 Main、PointerLayer 或偏好设置，没有启动、编译或执行测试；由 Root 在统一资产与原生构建完成后调用。

```csharp
RunPointerParityChecks();
GetPointerParityAudit(); // running / completed / passed / phase / folder / checks / captures / diagnostics / error
```

该入口为 async void，使用 running 防重入。Root 可轮询审计，等 completed=true 后读取结果；所有图像均完成保存之后才完成流程。运行目录名包含 UTC 时间和 ticks，避免覆盖上一轮结果。

## 七项检查

1. 主菜单使用 Hidden，未捕获、未限制桌面指针。菜单指针等于 Viewport 查询到的原生绝对位置；注入带 raw 字段的菜单 motion 不增加 DesktopPointerMotion 的报告计数，证明菜单不重复应用 gain。
2. 在 LegacyFixture 建筑检查场景中，确认 Captured 与 UseAccumulatedInput=false。对三个不同方向/幅值的原始单位增量，先独立计算 OS profile 输出及当前 PointerSensitivity，再通过实际 Viewport.PushInput 路由。指针从安全矩形中心开始，显式拒绝会被裁剪的样本；将实际虚拟位移乘 final transform 的 basis，要求回到预期屏幕位移，误差小于 0.02 像素。
3. ScreenRelative=0 的局部合成事件保留原 logical Relative，且不进入 OS gain helper，防止旧测试受到物理转换。
4. 调用实际 FocusExited handler，立即检查 Visible、软件光标隐藏及 accumulation 恢复；调用 FocusEntered handler，检查重新 Captured 和关闭 accumulation。审计明确标注这是 handler 验证，没有伪称实际切换外部应用焦点。
5. 原版 arrow：实际绘制 frame 0，hotspot=(0,0)，完整 atlas 帧数 450，并保存真实 Viewport PNG。
6. 原版 repair / repair_invalid：使用自己的、存活、完成且已受伤的建造厂 ground footprint center。确认有效维修；满血、未完成、敌方受伤完成建筑分别验证无效。保存两种实际 PNG，检查正确 frame range 与 hotspot。
7. 原版 sell / sell_invalid：同一建筑分别为己方和敌方所有，验证出售有效/无效，保存两种实际 PNG，检查正确 frame range 与 hotspot。

流程只临时修改检查场景建筑的 Health、Owner、RemainingBuildTicks 和 Repairing，finally 恢复这些字段并执行 ReturnToDeployment。最终要求 Battle=null、terrainInspection=false、terrainInspectionMotion=false、softwareBattlePointer=false；普通菜单仅可为 Hidden 或 Visible，不能遗留捕获、限制或冻结检查场景。finally 还验证 accumulation 恢复。

## 真实图像与审计

运行时生成：

```text
Reports/skirmish-parity/pointer-<UTC时间>-<ticks>/
  arrow.png          arrow.json
  repair.png         repair.json
  repair_invalid.png repair_invalid.json
  sell.png           sell.json
  sell_invalid.png   sell_invalid.json
  acceptance.json
```

每张图像先等待 ProcessFrame，再等待真正的 RenderingServer.FramePostDraw，随后从 Viewport texture 读取图像并检查 SavePng 返回值。没有调用 async void CaptureBattlePolishFrame 来冒充已完成保存，也没有重构截图或单独拼贴光标。等待绘制有 8 秒超时，并在 finally 解除信号订阅。

独立图像 JSON 保存实际图像尺寸、draw frame、当前 cursor audit、预期 frame 范围、hotspot、pointer、drawTopLeft 和窗口输入审计。汇总结果只有在七项完成、五张 PNG 完成、菜单清理成功且报告写入成功时才 passed=true。

| 光标 | 原版帧范围 | Hotspot |
|---|---:|---:|
| arrow | 0 | (0,0) |
| repair | 150–169 | (27,21) |
| repair_invalid | 190 | (27,21) |
| sell | 129–138 | (27,21) |
| sell_invalid | 149 | (27,21) |

这些固定验收值来自 [原版光标目录](/storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Generated/Cursors/catalog.json:9019)；实际绘制值读取 [ClassicCursorLibrary](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/ClassicCursorLibrary.cs:32) 的当前帧审计。

## 证据范围

增量检查是按真实 native 单位构造的 PushInput 探针，覆盖真实捕获路由和当前 Viewport transform；它不是用户鼠标的真实物理行程测量。菜单检查调用真实 client-entry handler，未调用缺失的 native warp。Focus 检查也明确记录 handler 范围。五张 PNG 则来自当前原生窗口真实 GPU 渲染。

没有运行旧长套件；未生成虚假的成功结果或占位 PNG。本文件记录入口与验收契约，实际通过状态以 Root 调用后生成的 acceptance.json 为准。
