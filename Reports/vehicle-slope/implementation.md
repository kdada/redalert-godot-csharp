# 车辆坡面真实 VXL 姿态

本次改动针对坦克在原地图斜坡上被压扁的显示问题。C# 与生成模型已交 Root SOURCE/DATA STABLE；terrain 未启动集中 build、Godot Native 或既有长测试，后续原生结果由 Root 记录。

## 根因与显示修正

旧方案将整张已经投影的车身、炮塔、炮管、阴影图片乘以地面仿射矩阵。其竖轴 `by=1−(hx+hy)/30` 在原图梯度 `(1,1)`、每级15像素时为零；矩阵不仅缩短地面接触面，也把本应有体积的车身高度压成线。其他方向的仿射也会错误改变车身高度。

现在[坡面采样](</storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/Battlefield.VehiclePose.cs>)只确定四个接触点、平均支撑高度与坡面梯度。接触跨越不可通行悬崖时延续当前格自身的支撑平面，继续避免向崖下倾倒。桥面仍取权威 SurfaceHeight，原图地面高度只投影一次，不修改原 TMP zdata、地形、碰撞或单位模拟状态。

[真实姿态渲染器](</storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/ArtLibrary.VehiclePose.cs>)在原 VXL/HVA 暴露面上完成三维刚体旋转，再按既有30°摄像机投影和重新栅格化：

- 保持原 model origin、HVA、footer scale 与 Y 反射；围绕车身底部接触高度旋转。
- 车身 yaw 与炮塔/炮管 yaw 分开处理，随后应用同一支撑面旋转；各部件共用深度缓冲以决定遮挡。
- 从地图 `(30,15),(-30,15)` 地面投影推导坡面物理法向，兼容原图15像素/级与旧地图8像素/级。
- 地面阴影单独投影到支撑平面。支撑平面侧对摄像机时阴影可以收成细线，三维车身仍保留体积。
- 平地与海空单位沿用既有原图集；SHP 本身没有 VXL 体积，不对其施加整图仿射。

[绘制接入](</storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/Battlefield.cs>)仅调整 DrawUnit 的姿态/阴影调用；[资源管理](</storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/ArtLibrary.cs>)增加退出时销毁姿态缓存。没有修改 Simulation。

## 模型来源与数据核对

[生成器](</storage/Users/currentUser/dev/redalert-godot-csharp/Tools/VehiclePose/prepare_models.py>)移植本仓库已有、独立实现的原 VXL/HVA 格式解析与暴露面构建算法。它按 basename+SHA256 定位保留的原数据，逐面流式输出 gzip；无需 dotnet、原版引擎代码或第三方渲染器。

[模型目录](</storage/Users/currentUser/dev/redalert-godot-csharp/Assets/Generated/VehiclePoses/catalog.json>)包含45个模型、63部件、730920个暴露面；压缩总计6743096字节（6.43MiB），展开总计44622075字节（42.55MiB）。含两个 MCV、IFV 三种炮塔、棱镜三种充能炮塔及全部带 body 的既有模型；四个仅含炮塔的建筑不需要车辆坡面模型。

生成子任务完成的实际检查：49份 manifest 的185条源 hash 引用全部匹配；221份保留的 VXL/HVA/PAL 源全部复核；65个 limb 的原尺寸、bounds、scale、normal type、体素数与原 manifest 一致；45个 gzip 全量读回 CRC、61字节面记录、长度及 SHA256 均通过。[九项 Python 解析/变换检查](</storage/Users/currentUser/dev/redalert-godot-csharp/Tools/VehiclePose/test_prepare_models.py>)通过。生成与完整读回用时72.453秒，这是离线数据生成耗时，不是游戏运行时耗时。

## 有界缓存与性能观察

运行时 LRU 最多192张姿态图（16MiB计费上限）、12个模型（24MiB计费上限），等待队列最多256项。朝向保留32档，支撑梯度按1/8级/格量化；不随地图位置或单位ID复制图像。每帧最多处理两个缺失姿态，累计6ms后不再启动新生成；FIFO 避免画面前部持续移动的车辆饿死其他请求。转向或梯度变化等待期间优先使用同车/同朝向的最近三维姿态，首次尚无缓存时短暂使用既有原图集。

单次模型解压和栅格化不可中断，因此6ms是启动预算，不是单次硬时限。Root 应观察 `VehiclePoseMaximumRenderMilliseconds` 与首次/暖缓存帧，特别是大型 MCV、密集车群持续转向。若实测首次 miss 过重，优先把 gzip 解压与模型 float 读取改为一次缓冲批读，再评估按帧渐进读取或预热常用车辆；不要以避开斜坡、无限预烘组合或放开缓存上限掩盖问题。

淘汰纹理延迟两个渲染帧释放，避免已提交的 Canvas 命令引用失效 RID；每帧最多两次新增使退休纹理数也保持有界。当前计费指标包括模型、活动纹理及待释放纹理，瞬时栅格工作数组另计。真实最小覆盖像素数/高度在生成后检查，空图或线状车身不会被当成成功姿态。

## Root 原生入口与覆盖边界

[原生审计入口](</storage/Users/currentUser/dev/redalert-godot-csharp/Scripts/Main.VehicleSlopeInspection.cs>)：

```csharp
Main.RunVehicleSlopeChecks();
Main.GetVehicleSlopeAudit();
```

入口仅在实际 Main/Godot Native 下运行。它搜索全部已注册官方地图的可通行真实坡面，按坡向拍 uphill/downhill/cross 的原 Grizzly/Rhino，另含 flat/bridge 控制；同一真实 Battlefield 与 World 绘制，使用 PointerParityFrame/FramePostDraw 后保存 viewport PNG 与逐格 JSON。运动检查最多48次 World.Step（上坡24、下坡24），原图数据保持不变。运行后回到正常部署菜单。

所有五张官方图全体原记录均无 RampType13，因此没有正对角 `(1,1)` 的真实原图格。该条件明确列为 coverageNotes，`positiveDiagonalUnavailable=true`，`coverageComplete=false`；实际四向/控制/运动条件仍需正常通过，`passed` 不因一个不存在的可选原图类型单独失败。独立 `(1,1)` 几何压力对照明确标为数学案例，不将其伪装成第六张官方地图或改写原地形。

最终 Native 验收应查看实际坡面车身接地、炮塔炮管遮挡、阴影、悬崖边界与桥面；几何对照和模型 hash 核对不能代替原生截图。当前报告不预先声称 Root 原生测试通过。

## 独立几何压力对照结果

[有界对照脚本](</storage/Users/currentUser/dev/redalert-godot-csharp/Tools/VehiclePose/validate_projection.py>)实际运行通过（exit0），只读45模型中的原 Grizzly 数据并写本报告目录，不启动 C#/Godot/Native。8个输入为 flat、东西南北、正/负对角与交叉坡；所有旋转基正交、行列式1，且所有三维栅格结果都有非零像素体积。

最强退化案例 `(1,1)` 中，旧整图仿射行列式0，输出仅48像素、1像素高；真实 VXL 三维旋转输出637像素、18像素高，宽度均48像素。结果见[几何检查 JSON](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/vehicle-slope/pose-projection-checks.json>)。

![上行旧仿射数值对照、下行真实三维VXL，列顺序为flat/E/W/S/N/正对角/负对角/cross](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/vehicle-slope/voxel-pose-comparison.png>)

这是离线原几何对照图。上行用前向像素映射演示旧仿射及其奇异条件，放大方向存在像素采样空隙；下行重新光栅化原三维面。它证明车身体积不再跟地面Jacobian一起归零，不表示这些图是原生游戏截图。

## 首轮 Native 接入检查与修复

Root 首轮[原生审计结果](</storage/Users/currentUser/dev/redalert-godot-csharp/Reports/vehicle-slope/20261022-151545-88542/acceptance.json>)在第一处真实坡面触发“未使用姿态 renderer/cache”，captureCount=0。地图搜索已完成，发现八种实际可用坡向；这轮没有产生可当成通过证据的截图。

只读核对定位为模型别名解析：扩展目录中的 `gtnk` 复用实际 manifest 的 `grizzly`，`htnk` 复用 `rhino`，其余核心车辆同理。旧 atlas 加载器按 manifest 路径读取，所以原平地素材正常；首版坡面路径直接按扩展ID拼模型文件名，实际数据文件却严格使用 manifest.name，导致回退到平地图集。kind 字段正确为 `voxel`，模型资源本身完整。

已只在运行时姿态 partial 增加与既有 loader 一致的 canonical manifest.name 解析，并缓存最多128个别名；没有复制资源或添加无依据的同名替代。全部22个陆地 VXL 及炮塔/no-spawn 分支到实际模型文件的独立映射核对通过（exit0），涵盖九种旧 atlas 别名、IFV/棱镜变体与两个 MCV。修复已交回 SOURCE STABLE，Root 后续集中重建与原生复验结果待补。
