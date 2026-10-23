# 第一版前端拆分（历史记录）

本页记录的是第一版页面拆分与当时的测试。用户随后指出视觉只像结构；经实际看图，确认其中使用了历史遗留外壳素材，因此本页不作为当前发行版 UI 样式的验收依据。后续重新联网核对截图、解码原版主菜单影片并重做交互，见[发行版菜单重做与实机验收](AUTHENTIC_FRONT_END.md)。

按用户“启动页面，设置页面，局域网联机页面能做成原版风格吗”的要求，将原先混在一页的部署表单拆为独立的主菜单、单人游戏、选项和局域网页面。

## 界面

- 主菜单：单人游戏、局域网、选项、退出游戏。使用红色标题、金色文字、原始外壳按钮的普通/悬停/按下状态与金属边框。
- 单人游戏：选择国家和呼号后进入四国训练场，实际启动1名玩家与3名简单电脑对手的对战。
- 选项：全屏模式、帧率上限、卷屏速度、边缘卷屏、应用、取消与恢复默认；支持保存并在重新进入页面及下次启动时恢复。垂直同步在支持的窗口系统上可切换；当前OpenHarmony实机无论请求关闭与否均保持启用，因此该平台明确显示不可编辑的“系统控制”。
- 局域网：八个成员席位、国家与队伍、呼号、主机IP、端口、建房、加入、离开与房主开始作战。未满两人或不足两个不同队伍时禁用开始。入房后锁定身份字段，避免界面显示与网络已登记的身份不一致。

固定4:3内容画布按整体比例居中，适配宽屏；按钮与边框按原素材分段缩放。布局与中文标题为独立实现，没有把无法证明用途的资源冒充原版主菜单布局。

## 美术来源

[原始菜单资源清单](../Assets/Reference/manifest-menu.json) 保留固定源版本、SHA256、100条精确HTTP206范围及Content-Range证据，总载荷2,188,075字节，没有下载原程序、整包或影片。

实际前端使用[原始底纹](../Assets/Generated/Menu/Pcx/dbak6440.png)以及[普通按钮](../Assets/Generated/Menu/Pcx/shellbttnoff.png)、[悬停按钮](../Assets/Generated/Menu/Pcx/shellbttnon.png)、[按下按钮](../Assets/Generated/Menu/Pcx/shellbttnpress.png)。这些PCX自带RGB8调色板，转换逐像素保留其颜色。[资源目录](../Assets/Generated/Menu/catalog.json) 另保留76个PCX和6组SHP供后续使用。

[菜单转换验证](../Assets/Generated/Menu/validation.json) 通过：90份原始源散列、8个SHP帧的原始像素字节、170个PNG的CRC/结构/像素。SHP候选调色板及未确认用途见[资源审计](../Assets/Generated/Menu/resource-audit.json)。原始资源包含有历史遗留素材，文件名不能证明其就是发行版主菜单底图；原始UI INI也不包含菜单坐标。因此本次恢复的是原版风格和可用流程，未声称逐像素复制发行版菜单。动态开场背景和菜单音乐尚未接入。

## 设置与功能

[客户端配置](../Scripts/ClientPreferences.cs) 保存于Godot私有`user://client.cfg`；鸿蒙原生实测目录位于应用的长期配置区，不放工作区HOME。保存内容为显示、镜头及呼号/国家/最近主机/端口，不进入模拟规则或网络协议。

[菜单实现](../Scripts/Main.FrontEnd.cs)、[4:3画布与边框](../Scripts/ClassicMenuCanvas.cs) 和[原素材缓存](../Scripts/ArtLibrary.Menu.cs) 与战场侧栏分开。切入战场时恢复战场主题，返回时恢复主菜单；没有重绘战场美术。

## 验证

[前端操作回归](classic-front-end-tests.json) 通过实际Viewport鼠标事件验证：

1. 主菜单与独立页面切换，以及控件处于可见画布内。
2. 设置应用、写盘、重新载入、取消未应用修改和恢复默认。
3. 空地址与错误端口拒绝连接，八人席位与入房身份锁定。
4. 同一Godot进程内两个真实ENet Peer，通过界面建房、来宾加入、房间列表更新、房主点击开始并双端进入对战；没有伪造成员。
5. 单人游戏保留已选国家和呼号，并进入四人训练场。

[25项核心回归](classic-front-core-tests.json) 全通过。[战场鼠标/键盘与回放回归](classic-front-battle-regression.json) 通过：240ticks、18接受、0拒绝，散列仍为`c6e9e1d5f7e48c65`，内容指纹仍为`d1493b9340acc921`。前端修改未改变既有确定性结果。

## 原生截图

原生Godot/Maleoon935，帧缓冲2090×1306。通过实机按钮导航捕获：

- [启动主菜单](classic-front-native/main.png)
- [选项页](classic-front-native/settings.png)
- [局域网建房后](classic-front-native/network.png)
- [局域网空房间](classic-front-native/network-empty.png)
- [单人游戏页](classic-front-native/single-player.png)

[原生运行证据](classic-front-native/runtime.json) 记录实际设置保存、主菜单按钮和建房状态。[尺寸检查](classic-front-native/layout-checks.json) 验证1024×768与1920×1080的控件均位于可见画布内，再恢复1440×900。实机另验证全屏模式从0切换到3再恢复、帧率上限从60改为90再恢复。垂直同步关闭请求被当前平台保持为启用，因而做了上文的平台适配。

本历史版本当时未完成图像直接检查；后续已支持图像输入并重新看图实现，参见页首新版记录。以上旧验证覆盖原始像素、布局、实际输入路径、设置状态及帧缓冲，没有完成当时的手工美术验收。跨设备局域网实测仍需另行完成；本次新增的是本机双Peer的真实UI联机验收。
