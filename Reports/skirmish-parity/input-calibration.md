# 原生桌面鼠标速度：已实现的 OS 曲线转换与证据

调查日期：2026-10-22。已实现独立 [DesktopPointerMotion.cs](/storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/DesktopPointerMotion.cs)，未修改 Main、PointerLayer 或偏好设置；未构建、启动或触发原生游戏 UI。安装版引擎按提交 `2a654488474da3c874e15cdbf49e7d764057db73` 的 Git 对象核对。

## 交付 API

```csharp
var desktopMotion = new DesktopPointerMotion(); // fallbackPointerSpeed 默认为源代码默认值 10。
Vector2 screenPixels = desktopMotion.ToScreenPixels(originalMotion.ScreenRelative);
int speed = desktopMotion.PointerSpeed;
var audit = desktopMotion.GetAudit();
desktopMotion.RefreshNativeSettings(); // 可在焦点恢复/系统偏好变化时只读重查。
```

输入必须是 API 26 原生 Captured 的硬件 counts，输出为屏幕物理像素增量。调用方再通过 Viewport final transform 的逆 **basis** 变换一次，得到游戏坐标增量；不要重复乘显示密度。GUI 合成事件和旧测试的局部目标应由调用方绕过此转换。helper 不改鼠标模式、全局输入设置或系统偏好。

**捕获期间需要调用方设 `Input.UseAccumulatedInput=false`，退出后恢复原值。** OS 曲线对每个报告的增量幅值非线性计算；Godot 若先合并多条 counts，再计算曲线，就会产生 FPS 相关的过度加速。helper 的审计保留此开关状态，但不修改它。

## 本机只读查询的实际结果

独立 Python 子进程调用已核对签名的 native getter，退出码 0；没有调用 setter。

| 查询 | 返回码 | 实际值 |
|---|---:|---:|
| 默认屏幕宽度/高度 | 0 / 0 | 3120 × 2080 像素 |
| DensityPixels | 0 | 1.899999976158142 px/vp |
| DensityDpi | 0 | 304 |
| DensityXdpi / DensityYdpi | 0 / 0 | 264.1600036621094 / 264.1600036621094 |
| MMI InputManager GetInstance | 指针非空 | 可取得实例 |
| MMI GetPointerSpeed | **-1** | out 未得到有效速度；没有声称已测得 5 或 10 |

动态曲线要求 physical PPI，不能使用 304 这个 density DPI。helper 用 native X/Y dpi 和同一默认屏幕的像素宽高还原物理对角线，再计算 `pixelDiagonal / inchDiagonal`。本机物理 PPI ≈264.16，对角线约 14.195 英寸，整数对角线 14，ScreenFactor=1。

签名见公开 SDK 的 [显示管理头文件](/storage/Users/currentUser/.oheco/packages/command-line-tools/26.0.0.201/sdk/default/openharmony/native/sysroot/usr/include/window_manager/oh_display_manager.h:142)。现有引擎 [ScreenGetDpi 包装](/storage/Users/currentUser/dev/ohos/godot-editor/platform/openharmony/wrapper_openharmony.cpp:93) 返回的是 DensityDpi；这两个值不能混用。

实际 [系统鼠标曲线](/system/etc/multimodalinput/pointer_motion_acceleration_config.json:572) 可读，MouseDynamic 参数与固定官方源快照相同：

```text
speeds       = [0.1, 0.2, …, 2.0]，共 20 项
slow_gains   = [1.0341957429588597, 0.7126536301164397,
                2.5813850030026853, 0.02004352388383787]
fast_gains   = [1.3556224243118067, 2.5417186406523578,
                6.127531547651636, 3.2322785269552803]
standard_ppi = 264.16
```

helper 首先读取系统配置；读取或验证失败时使用同源内置参数，并在审计明确区分。physical PPI 无法取得时，采用公开 OS 的 PCMouse 分段曲线分支，而不把 density DPI 冒充 physical PPI。非 OpenHarmony 或 headless 环境返回原样增量并跳过 native 查询。

## 动态鼠标公式及来源

[官方 PointerMotionAcceleration，固定提交](https://github.com/openharmony/multimodalinput_input/blob/7f674c11dee34d4c158827ca8f0e8db3b12a1d4b/service/mouse_event_normalize/src/pointer_motion_acceleration.cpp#L430)：

```text
vin = hypot(rawX, rawY)

若 vin < 13.5:
    t = slow[0] * ln(slow[1] * vin + slow[2]) + slow[3]
否则:
    t = fast[0] * ln(fast[1] * ln(vin) - fast[2]) + fast[3]

gain = t * speeds[pointerSpeed - 1] * physicalPPI / standardPPI
screenDelta = rawCounts * gain * ScreenFactor(integerDiagonalInches)
```

[DynamicAccelerateMouse](https://github.com/openharmony/multimodalinput_input/blob/7f674c11dee34d4c158827ca8f0e8db3b12a1d4b/service/mouse_event_normalize/src/pointer_motion_acceleration.cpp#L92) 虽然接收 deltaTime 参数，所核对实现的鼠标 gain **不使用该参数**，没有添加时间 bin 或 FPS 系数。

[MouseTransformProcessor](https://github.com/openharmony/multimodalinput_input/blob/7f674c11dee34d4c158827ca8f0e8db3b12a1d4b/service/mouse_event_normalize/src/mouse_transform_processor.cpp#L291) 在物理屏幕信息有效时优先走动态分支，使用物理尺寸推导 PPI；否则用设备静态曲线。其 [ScreenFactor](https://github.com/openharmony/multimodalinput_input/blob/7f674c11dee34d4c158827ca8f0e8db3b12a1d4b/service/mouse_event_normalize/src/mouse_transform_processor.cpp#L145) 如下：

| 整数物理对角线（英寸） | 系数 |
|---|---:|
| ≤0 | 1 |
| 1–7 | 0.7 |
| 8–17 | 1 |
| 18–26 | 1.2 |
| 27–54 | 1.6 |
| ≥55 | 2.4 |

静态 PCMouse 采用 `vin=max(abs(x),abs(y))+min(abs(x),abs(y))/2`，在当前速度对应的曲线中选第一个 `vin≤threshold` 段，gain 为 `(slope*vin+offset)/vin`；超过最后阈值沿用最后段。超出曲线速度行数时使用最后一行，与公开源的 MatchCurve 一致。

C# 文件保留公开 OS 源的 Apache-2.0 许可与归属；公式和参数来自系统公开源及本机系统配置，不来自原游戏实现。

## Native pointer speed 查询和回退

[InputManager 的公开源签名](https://github.com/openharmony/multimodalinput_input/blob/7f674c11dee34d4c158827ca8f0e8db3b12a1d4b/frameworks/proxy/events/src/input_manager.cpp#L567) 为 `int32_t GetPointerSpeed(int32_t&)`；GetInstance 返回原始指针。helper 只读调用已在本机验证可装载的系统 client 库，失败时记录结果和异常类型。

[MousePreferenceAccessor](https://github.com/openharmony/multimodalinput_input/blob/7f674c11dee34d4c158827ca8f0e8db3b12a1d4b/service/mouse_event_normalize/src/mouse_preference_accessor.cpp#L33) 的 `DEFAULT_SPEED` 是 **10**。因此默认 fallback=10 是源默认值，**不是实际系统设置的测量值**。构造函数可传入明确的调用方回退值；native 成功读到有效速度时仍以实际 native 值优先。

[ArkTS pointer 系统接口](https://github.com/openharmony/docs/blob/master/zh-cn/application-dev/reference/apis-input-kit/js-apis-pointer-sys.md) 的 getPointerSpeed 是系统接口，不应把普通 SDK 未声明的方法当作公开稳定应用 API。C++ getter 同样属于内部 ABI，因此 helper 捕获装载/入口错误，并在审计显示回退。DSH 进程返回 -1 并未提供具体失败原因；仍需在真实 Godot 进程检查 `pointerSpeedSource` 和 `nativeSpeedResult`，不能推断已有实际值。

## 为什么当前 Visible 事件不能自动校准 raw→desktop

本机 SDK [rawDelta 契约](/storage/Users/currentUser/.oheco/packages/command-line-tools/26.0.0.201/sdk/default/openharmony/ets/component/common.d.ts:8991) 明确：API 26 返回原始硬件移动数据，既不是物理像素也不是逻辑像素。API 26 前才曾被显示比例除过。

[ArkTS host](/storage/Users/currentUser/dev/ohos/GodotEditor.11/entry/src/main/ets/pages/Index.ets:378) 对绝对位置 vp2px；416–417 行的 rawDelta 原样传递。精确 `.11` [native bridge](/storage/Users/currentUser/dev/ohos/godot-editor/platform/openharmony/bridge_openharmony.cpp:127) 行为为：

```cpp
ev->set_position(Vector2(event.x, event.y));
ev->set_global_position(ev->get_position());
Vector2 relative = has_mouse_position
    ? Vector2(event.x - last_mouse_event.x, event.y - last_mouse_event.y)
    : Vector2();
if (event.has_relative && mode == MOUSE_MODE_CAPTURED) {
    relative = Vector2(event.relative_x, event.relative_y);
}
ev->set_relative(relative);
ev->set_relative_screen_position(relative);
```

因此 Visible/Confined 的两个 relative 字段都是加速后的绝对像素位置之差，硬件 raw 被丢弃；无法在同一次 Visible 事件中配对 raw 与桌面位移。Captured 的 Position/GlobalPosition 在 bridge 没有强制置中，但原生锁使绝对指针静止，无法用它推导未锁定时的桌面移动。

[Viewport 本地化](/storage/Users/currentUser/dev/ohos/godot-editor/scene/main/viewport.cpp:1467) 对位置/Relative 应用 final transform 逆变换；[motion 变换](/storage/Users/currentUser/dev/ohos/godot-editor/core/input/input_event.cpp:976) 保留 ScreenRelative。因此当前 Captured 下 `Relative=A⁻¹ rawCounts`，软件渲染再乘 A，最终位移只有 rawCounts；缺的是桌面 gain，而非再补一次 Viewport 缩放。

## 菜单原版光标

精确 `.11` OpenHarmony 后端没有 `cursor_set_custom_image` override，调用继承的 [默认实现](/storage/Users/currentUser/dev/ohos/godot-editor/servers/display/display_server.cpp:1150)，只警告“Custom cursor shape not supported by this display server”。原版纹理需要菜单软件光标或将来补 host 功能。

菜单用隐藏的正常桌面指针位置驱动软件纹理时，已经具有系统桌面速度，不应再把菜单 Position/Relative 送入上述 raw-count 转换。该模式也可作为原生对照。

## 实测仍需检查的限制

- 真实 Godot 进程的速度 getter 是否成功；若仍失败，审计会明确显示 system_default_fallback。不能把 fallback=10 当成已经匹配用户自定义设置。
- 该类移植的是鼠标动态/PC 分支；触摸板有单独设备参数及曲线，当前 host 不传足以自动区分全部设备的数据。
- OS 的加速开关没有通过此公开 SDK 查询到；当前假定开启并在审计标明。Vendor 的其他前置报告滤波、产品配置覆盖、屏幕旋转及多屏特殊逻辑，未声称完全重现。
- 原始报告不得在 Godot 侧先合并；核对 GetAudit 的 useAccumulatedInput。
- Native 手动验收应比较菜单和战斗的慢/快直线、斜线及端点，并检查侧栏点击、滚轮、雷达拖动、边缘滚屏、Esc 和焦点恢复。若端点相近但观感拖后，要调查渲染延迟，不应继续增加 gain。

本轮完成源码移植、源公式/签名核对和上述只读 native 数据查询；按 Lead 要求没有 build 或启动。最终编译与原生交互由统一 Root 轮次验证。
