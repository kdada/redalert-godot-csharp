# 对局文本聊天：所有人／队友／外部 AI

单机与局域网对局均支持文本聊天。单机中，真人和外部AI共用本局消息存储；局域网中由主机授权并可靠转发。原有开战前房间聊天继续保留。

## 真人操作

- `Enter`：打开所有人频道；`Shift+Enter`：打开队友频道。
- 战场左上“交谈”按钮也可打开输入框，使用“所有人／队友”下拉选择接收范围。
- 输入后按Enter或点“发送”；Esc取消草稿。点击战场会关闭草稿，该次点击不下达战斗命令。
- 输入时不触发战斗快捷键，方向键用于文字编辑，不卷动镜头。Esc取消输入时保留原有部队选择。
- 最近消息显示约12秒；重新打开输入框可查看最近100条可见记录。频道、发送者名字／玩家ID和逻辑时间一起显示。
- 文本按字面显示，不解析BBCode或链接。支持中文和emoji；最多280个Unicode码点，空消息拒绝，换行／制表符变空格，其他控制字符与方向控制标记过滤。
- 每个玩家每20个逻辑tick最多4条。战败或对局结束后不能继续发送，仍可读已有消息。

`all`发给本局所有玩家；`team`仅发给发送者所在队伍，同队包括自己。没有队友时，队友频道只会自己看到。队伍取开局时的真实玩家配置，发送方不能指定其他队伍。

## 外部 AI API

沿用本机HTTP服务，默认 `http://127.0.0.1:19844`。只向 `controller: "external"` 的玩家ID开放；启用方式和完整战斗指令见[外部AI说明](EXTERNAL_AI_API.md)。

发送：

```http
POST /api/v1/players/1/messages
Content-Type: application/json
```

```json
{
  "matchId": "从players或state接口读取当前值",
  "requestId": "agent1-chat-0001",
  "channel": "team",
  "text": "我准备从东侧进攻，请你防守基地。"
}
```

HTTP201表示主线程已接收并写入消息历史，返回：

```json
{
  "matchId": "...",
  "playerId": 1,
  "requestId": "agent1-chat-0001",
  "message": {
    "id": 17,
    "tick": 240,
    "senderId": 1,
    "senderName": "AI 玩家 1",
    "senderTeam": 0,
    "channel": "team",
    "text": "我准备从东侧进攻，请你防守基地。"
  }
}
```

发送者ID由URL确定，名字、队伍、消息ID和tick由游戏确定。请求体不能添加`senderId`、`senderTeam`、`tick`等字段。与战斗命令的202排队回执不同，聊天不等待下一逻辑步。

同一玩家、本局内，同`requestId`及相同频道／规范化文本重试返回HTTP200和原消息，不重复发送；同ID不同内容409。聊天与战斗命令使用独立requestId空间。成功的消息请求回执每局最多16,384份，达到容量返回429。

读取：

```http
GET /api/v1/players/1/messages?after=0&limit=50&matchId=当前matchId
```

```json
{
  "matchId": "...",
  "playerId": 1,
  "tick": 260,
  "messages": [],
  "nextCursor": 17,
  "hasMore": false,
  "historyTruncated": false
}
```

- `after`是上一页的`nextCursor`，初次为0。`limit`默认50，范围1–100。
- 只返回该玩家能看到的消息：所有人消息、本队消息及自己发送的消息。对方队友消息不会进入结果。
- ID按本局全局递增，过滤后可能不连续。不要自己将ID加1，使用返回的`nextCursor`；无新消息时游标保持不变。
- `hasMore=true`时继续读取下一页，直到false，再进入正常轮询。建议每0.5–2秒查询一次。
- 历史最多保留最近512条；游标落后于已删除历史时`historyTruncated=true`，返回目前仍保留的可见消息。需要长对话记录时，由控制程序自行保存。
- 游标只在本局有效。建议一直携带`matchId`，跨局查询返回409；发现新matchId后将游标重置为0。
- 非法频道、未知JSON字段、非法游标、空／过长文字返回400；人类／原有电脑席位403；旧局标识或玩家已结束作战409；超频／容量限制429。

与原有接口一致，这是可信本机控制程序入口，没有增加按模型账户鉴权。控制程序应将模型工具固定绑定到分配的player ID；各端点按该ID过滤消息。调用方本身若具有选择任意外部AI ID的权限，仍能查询那个ID对应的视角。

## 局域网隔离与同步

主机从实际ENet连接查找玩家，不信任客户端提交的sender、名字、队伍、tick或消息ID。队友消息在发送前按真实队伍筛选远端连接，绝不先广播再让客户端隐藏。主机作为权威会保留完整历史，但本机界面也经过玩家可见性过滤。客户端只接受主机发送的当前match授权消息，并再次校验队伍、名字、频道、文本与消息ID。

局域网线协议升为2，联机各端需使用本次更新后的构建；旧构建会在握手时被明确拒绝。非法／过期聊天包只拒绝消息，不终止正在进行的对局。聊天独立于战斗命令、状态hash和战斗录像，消息不会改变确定性模拟。聊天历史目前不写进战斗录像；返回部署、关闭对局或新开对局时清空。

## 验证

自动检查入口：

```sh
dotnet run --project Tests/RedAlert.Tests.csproj -- --battle-chat --report=Reports/battle-chat-http-tests.json
godot --headless --path . -- --battle-chat-test --timeout=120 --report=Reports/battle-chat-network-ui-tests.json
```

2026-10-18联调结果：

- [HTTP聊天检查](battle-chat-http-tests.json)：通过。实际HttpClient／TCP服务覆盖真人与AI共用存储、队伍过滤、分页游标、重试去重、伪造字段、跨局、限流、Unicode和512条保留上限。
- [三人ENet及界面检查](battle-chat-network-ui-tests.json)：通过。创建3个真实ENet peer，确认队友消息未进入对手连接、主机生成真实身份、拒绝旧局及重复包，且各端战斗hash／指令录像保持不变。通过实际键鼠事件验证聊天输入、频道、发送／取消、快捷键隔离、纯文本、淡出后新消息重新显示、布局范围及重新开局清空。
- 实际Godot进程经[HTTP联调驱动](../Tools/verify_external_ai.py)完成两名AI的all／team发送、查询过滤、游标与重复提交；战斗指令仍为3条接受、0条拒绝，240tick后录像验证通过。战斗循环报告见[Godot结果](external-ai-godot-smoke.json)。
- [原有AI指令回归](battle-chat-ai-regression.json)通过，包括八人世界中的七个并发外部AI；[菜单／房间回归](battle-chat-front-regression.json)通过，包括原有房间聊天、真实两peer开战和五人单机配置。
- [核心模拟回归](battle-chat-core-regression.json)：25/25通过，包括八人确定性指令流和序列化录像。
- 项目编译0错误、0警告。

本轮原生窗口截图未生成：当前命令行开发包在启动图形编辑器时提示Vulkan版本不满足，且未编入OpenGL驱动。上述游戏循环、网络和实际键鼠事件检查使用无窗口Godot进程完成；没有修改其他已打开的Godot项目。
