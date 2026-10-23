# 外部大模型／AI 控制接口

单机遭遇战的电脑等级新增 **AI**。选择该类型的席位不会运行简单／普通／困难电脑脚本，而是等待外部程序通过HTTP下达命令。矿车采集、单位寻路与自动战斗仍由游戏执行。游戏不内置大模型，也不保存模型API密钥。

## 启动与发现玩家

1. 主菜单 → 单人游戏 → 遭遇战，将任意非人类席位选为“AI”，设好国家和队伍后开始。
2. 只要对局包含AI席位，游戏就在本机启动 `http://127.0.0.1:19844/`。所有外部AI共用一个HTTP端口，通过URL中的玩家ID区分。
3. 请求 `GET /api/v1/players` 或 `GET /api/v1/match`，找 `controller: "external"` 的玩家。每人有独立 `id`；`setupSlot` 是部署页原始行号，从1计数。关闭席位会使对局ID连续压缩，因此请以API列出的ID为准。
4. `id` 在本局内稳定；每次新对局生成新 `matchId`，提交指令必须携带当前值，避免上一局的延迟指令控制新对局。

接口只在含外部AI的单机对局期间监听；返回部署或退出游戏时关闭。普通单机和局域网联机不启动该服务。端口占用时显示错误并保留在部署页，可通过启动参数 `--ai-api-port=其他端口` 更换端口。

无窗口运行示例（在项目根目录）：

```sh
godot --headless --path . -- --players=3 --external-ai=1,2 --ai-api-port=19844 --ticks=72000 --timeout=7200
```

这会建立3人单机对局，玩家1和2由API控制，玩家0无人工输入。可加 `--bot` 让玩家0也运行原有练习脚本；外部席位仍不会被该脚本接管。外部AI模式默认按正常时间推进，避免无窗口冒烟测试的快速模拟跑完后模型尚未响应；显式 `--fast` 仅用于测试。

## 接口

| 方法 | 路径 | 内容 |
|---|---|---|
| GET | `/api/v1` 或 `/api/v1/health` | 服务信息、当前matchId和端点目录 |
| GET | `/api/v1/match` | tick、20TPS、单机速度倍率、地图尺寸、公开玩家名单、胜负状态 |
| GET | `/api/v1/players` | 玩家ID、原部署行、名字、国家、队伍、控制类型和败北状态 |
| GET | `/api/v1/rules` | 全部单位、建筑、费用、属性、生产建筑、前置条件、国家限制、指令类型及容量限制 |
| GET | `/api/v1/players/{id}/state` | 该外部AI的资金、电力、技能状态、自有实体、当前可见实体和自有生产队列 |
| GET | `/api/v1/players/{id}/map` | 已探索地形、当前视野、当前可见矿石 |
| POST | `/api/v1/players/{id}/commands` | 向指定外部AI提交一批指令 |
| GET | `/api/v1/players/{id}/commands/{requestId}` | 查询该批指令的实际执行回执 |
| POST | `/api/v1/players/{id}/messages` | 向所有人或队友发文本消息 |
| GET | `/api/v1/players/{id}/messages?after=0&limit=50` | 按玩家可见范围和游标读取消息 |

状态按对应玩家的视野过滤：自有单位／建筑始终提供；其他实体只提供当前可见的部分。敌方生产队列、资金和不可见区域矿量不提供。静态地形在探索后保留；没有敌军历史情报记忆。地图行字符串按y排列、字符按x排列：`?`未探索，`.`草地，`,`泥地，`#`岩石，`~`水；视野行的`v`为可见，`e`为已探索但当前不可见。

坐标是地图格，左上为 `(0,0)`，x向右、y向下；建造坐标为建筑占地左上角。国家、单位、建筑和命令使用规则接口返回的枚举名，例如 `Russia`、`Conscript`、`WarFactory`、`AttackMove`。单位／建筑ID也只在本局有效。`state`不包含整张地图，避免每次推理重复传送大量格子；需要时再获取`map`。

消息接口支持 `all`／`team`，发送者、队伍和tick由游戏决定；HTTP201表示消息已经写入，重试返回200；它与战斗指令的202排队流程不同。具体消息体、游标、队伍隔离、限流以及真人快捷键见[对局聊天说明](BATTLE_CHAT.md)。

## 提交指令与回执

请求头必须是 `Content-Type: application/json`。例如：

```json
{
  "matchId": "从match或players接口取得的值",
  "requestId": "agent-1-plan-0001",
  "commands": [
    {"type": "Train", "actors": [101], "unit": "Conscript"},
    {"type": "AttackMove", "actors": [102, 103], "x": 40, "y": 40}
  ]
}
```

上例的ID仅示意，必须替换为该玩家实际拥有的兵营／单位。`player`、`tick`、`sequence`不能从请求体指定，分别由URL和游戏主线程决定。未知JSON字段、数字枚举及缺少必要参数会被拒绝。

POST成功返回HTTP202，`status: "queued"`，以及 `receiptUrl`。它表示已进入指令队列，**还不表示规则允许或已经执行**。在下一次逻辑步调用真实游戏规则后，回执变为：

```json
{
  "matchId": "...",
  "playerId": 1,
  "requestId": "agent-1-plan-0001",
  "status": "applied",
  "queuedAtTick": 200,
  "commandCount": 2,
  "results": [
    {"index": 0, "type": "Train", "sequence": 1, "tick": 200, "accepted": true, "error": null},
    {"index": 1, "type": "AttackMove", "sequence": 2, "tick": 200, "accepted": false, "error": "game_rules_rejected"}
  ],
  "receiptUrl": "/api/v1/players/1/commands/agent-1-plan-0001"
}
```

`applied`表示逐条尝试完毕，以每条`accepted`为准。资金、前置建筑、生产队列、占地、目标和所有权继续由游戏校验；不绕过费用、建造时间或视野。同一批指令按顺序尝试，前面成功、后面失败时不会回滚前面的操作。结构／所有权检查失败则整批不排队。失败的实际规则回执目前给通用`game_rules_rejected`，可结合新状态判断是否缺钱、前置不满足或位置不可用。

`requestId`为1–64个ASCII字母、数字、`-`、`_`，在每个玩家和match内唯一。同ID同内容重试返回已有回执，不重复执行；同ID不同内容返回409。响应中断时，应使用相同ID重试或查询回执。新决策使用新ID。每局最多16,384份回执，不会悄悄丢弃旧ID并重新执行。

| 指令 | 必填参数 |
|---|---|
| Move／AttackMove | actors、x、y |
| Attack | actors、target（当前可见实体ID） |
| Stop | actors |
| QueueBuilding | building；先预付费用，在右侧准备建筑 |
| Build | building、x、y；只放置已经准备就绪的同类建筑，不再扣费 |
| CancelBuilding | 无其他参数；取消当前建筑准备（包括已就绪），全额退款 |
| Train | actors（一个生产建筑）、unit |
| Rally | actors（一个建筑）、x、y |
| Repair／Sell／CancelQueue | actors（一个建筑） |
| Support | x、y；当前为美国空降支援，仍需兵营、冷却与视野 |
| Surrender | 无其他参数；仅投降URL指定的外部AI玩家 |

每批1–32条指令，每条最多256个actor，正文最多65,536字节，全局待执行指令最多256条。达到容量返回429；HTTP请求繁忙或超时返回503。主线程尚未开始处理就超时的HTTP请求不会日后突然执行；已处理但响应丢失的情况由`requestId`解决。

建筑流程已改为两阶段：提交 `QueueBuilding`（只需 `building`），查询 state 中的 `player.buildingProduction`、`buildingProductionProgress` 与 `buildingReady`；就绪后提交 `Build`（`building/x/y`）。非法位置不会消耗就绪项；`CancelBuilding` 在准备中或就绪后均全额退款。放置动画标准为 20 tick，准备时间仍来自建筑定义，缺电会放慢。战败／投降当步清除本方所有资产与准备项；之后 state/map 提供全局观战视野，命令仍返回 `player_inactive`。仅剩一队时 `finished=true`。

## Python接入示例

以下只依赖标准库，会查询玩家并让第一位外部AI在兵营尝试生产一名本国基础步兵，然后等待真实回执。先运行上述对局：

```python
import json
import time
import uuid
import urllib.request

BASE = "http://127.0.0.1:19844"

def get(path):
    with urllib.request.urlopen(BASE + path, timeout=10) as r:
        return json.load(r)

def post(path, value):
    req = urllib.request.Request(
        BASE + path,
        json.dumps(value).encode(),
        {"Content-Type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(req, timeout=10) as r:
        return json.load(r)

roster = get("/api/v1/players")
ai = next(p for p in roster["players"] if p["controller"] == "external")
player_id = ai["id"]
state = get(f"/api/v1/players/{player_id}/state")
barracks = next(b for b in state["buildings"]
                if b["own"] and b["type"] == "Barracks" and b["complete"])
request_id = uuid.uuid4().hex
receipt = post(f"/api/v1/players/{player_id}/commands", {
    "matchId": state["matchId"],
    "requestId": request_id,
    "commands": [{
        "type": "Train",
        "actors": [barracks["id"]],
        "unit": "Conscript" if ai["country"] == "Russia" else "Gi",
    }],
})
deadline = time.monotonic() + 10
while receipt["status"] == "queued" and time.monotonic() < deadline:
    time.sleep(0.1)
    receipt = get(receipt["receiptUrl"])
print(json.dumps(receipt, ensure_ascii=False, indent=2))
```

接大模型时，由同机控制程序获取`rules`、某玩家`state`和所需地图信息，交给模型生成上述`commands`数组，再调用POST并检查回执。每位模型保留自己的player ID、matchId和requestId计数。建议先以1–3秒为一次高层决策周期；单位的移动、采矿和开火不需要模型逐帧指挥。游戏不会为等待推理暂停，模型未接入时该席位不发新的战略命令。

默认服务只监听本机，没有为各模型设置独立身份凭据；同机调用方可指定任意外部AI ID。人类和原有电脑席位不能通过命令／状态接口接管。未提供CORS，带Origin的浏览器请求被拒绝；请用本机Python／Node／工具进程接入。远程模型服务本身不能直接访问本机127.0.0.1，应由你的本机控制程序转交模型输入输出。本轮没有开放公网访问、模型服务或互联网对局。

## 实现与验证

[HTTP传输](../Scripts/ExternalAiHttpServer.cs)后台只接收请求；[控制器](../Scripts/ExternalAiController.cs)在游戏主线程读取快照、排队指令、记录回执；[单机集成](../Scripts/Main.ExternalAi.cs)管理席位、端口和生命周期。HTTP指令进入原有模拟校验流程，并写入本地录像；回放不需要模型或HTTP服务。

已完成实际验证：

- [HTTP联调](external-ai-http-tests.json)：真实loopback请求、多玩家ID路由、迷雾过滤、越权／过期match拒绝、真实扣费与国家限制、请求去重、实体移除后重试、回执、生产完成、录像一致、64KiB限制及关闭重用端口；八人模拟中七名外部AI同时提交成功。
- [Godot游戏循环联调](external-ai-godot-smoke.json)：三人局、两名外部AI、240tick，3条HTTP指令实际接受、0拒绝，未混入原有电脑脚本；录像重演一致，hash `591d51448a64f301`。可用 [验证脚本](../Tools/verify_external_ai.py) 重跑。
- [核心回归](external-ai-core-tests.json)：25/25通过，内容规则指纹仍为 `d1493b9340acc921`。
- [菜单与局域网回归](external-ai-front-tests.json)：原菜单、设置保存、实际UDP发现／双ENet进程房间、国家队伍、接受状态、地图资金同步及五人遭遇战均通过。
- 原生2090×1306窗口实际选中两席“AI”，所有部署控件位于视口内；[部署截图](external-ai-native/setup.png)。进入游戏后，通过19844端口分别为玩家1／2下达Train，均得到实际接受回执；[运行记录](external-ai-native/runtime.json)、[战斗截图](external-ai-native/battle.png)。

复现自动HTTP与核心检查：

```sh
dotnet run --project Tests/RedAlert.Tests.csproj -- --external-ai --report=Reports/external-ai-http-tests.json
dotnet run --project Tests/RedAlert.Tests.csproj -- --report=Reports/external-ai-core-tests.json
python3 Tools/verify_external_ai.py
```

