# PLC 监控面板 — 技术架构

> GitHub: https://github.com/KaelTian/PLCTutorial

## 项目结构

```
PLCTutorial/
├── backend/                          # .NET 8 后端
│   └── src/
│       ├── PlcMonitor.Core/          # 核心模型与接口定义
│       │   ├── IPlcReader.cs         # PLC 读取器抽象接口
│       │   ├── PlcConfig.cs          # PLC 连接配置模型
│       │   ├── PlcDataPoint.cs       # 点位数据结构
│       │   ├── PlcDataQuality.cs     # 数据质量枚举 (Good/Uncertain/Bad)
│       │   ├── PlcProtocol.cs        # 协议类型枚举 (OpcUa/S7/Cip/ModbusTcp)
│       │   ├── PlcDataChangeEventArgs.cs
│       │   └── PlcConnectionStateEventArgs.cs
│       │
│       ├── PlcMonitor.ProtocolOpcUa/ # OPC UA 协议实现
│       │   ├── OpcUaReaderBase.cs    # OPC UA 读取器基类（批量读取、KeepAlive、自动重连）
│       │   ├── SiemensOpcUaReader.cs # 西门子厂商标识
│       │   ├── OmronOpcUaReader.cs   # 欧姆龙厂商标识
│       │   └── DependencyInjection.cs
│       │
│       ├── PlcMonitor.ProtocolS7/    # S7 协议实现
│       │   ├── S7Reader.cs           # S7 读取器（基于 S7.Net Plus 库）
│       │   └── PlcMonitor.ProtocolS7.csproj
│       │
│       └── PlcMonitor.Api/           # HTTP API + SignalR 服务
│           ├── Program.cs            # 入口：REST API + SignalR Hub 注册
│           ├── Services/
│           │   ├── PlcBackgroundService.cs # 后台轮询 + 缓存比较 + SignalR 推送
│           │   ├── PlcReaderFactory.cs     # 工厂：根据协议创建对应读取器
│           │   └── DependencyInjection.cs
│           ├── Hubs/PlcDataHub.cs    # SignalR Hub
│           ├── Models/ApiModels.cs   # REST API 响应模型
│           └── appsettings.json      # PLC 连接配置
│
└── frontend/                         # Vue 3 + Vite 前端
    └── plc-ui/
        ├── src/
        │   ├── components/
        │   │   ├── PlcDashboard.vue       # 主面板（数据源管理）
        │   │   ├── PlcConnectionCard.vue  # PLC 连接卡片组件
        │   │   └── PlcDataPointItem.vue   # 单个点位数据显示组件
        │   ├── composables/
        │   │   ├── usePlcApi.ts           # REST API 封装
        │   │   └── useSignalR.ts          # SignalR 实时通信封装
        │   ├── types/plc.ts               # TypeScript 类型定义
        │   └── App.vue                    # 根组件
        └── vite.config.ts                 # 开发代理配置
```

---

## 一、协议架构

### IPlcReader 接口

```csharp
public interface IPlcReader : IAsyncDisposable
{
    string Name { get; }
    PlcProtocol Protocol { get; }
    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();

    Task<PlcDataPoint?> ReadAsync(string tagPath, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, PlcDataPoint>> ReadAsync(
        IEnumerable<string> tagPaths, CancellationToken ct = default);

    event EventHandler<PlcConnectionStateEventArgs>? ConnectionStateChanged;
}
```

两种协议各自实现了这一接口，但内部机制差异较大：

### OPC UA 协议

```
OpcUaReaderBase (抽象基类)
    ├── SiemensOpcUaReader
    └── OmronOpcUaReader
```

| 特性 | 说明 |
|------|------|
| 库依赖 | `Opc.Ua` / `Opc.Ua.Client` (OPC Foundation) |
| 批量读取 | 支持 — 单次 RPC 读取多个 Node，避免 N+1 |
| 安全协商 | `SelectEndpoint` 自动协商安全策略 |
| 连接保活 | `KeepAlive` 机制检测断线 |
| 自动重连 | 断线后异步重试，间隔 10s |
| 线程安全 | `SemaphoreSlim` 保护会话操作 |
| 派生类 | 仅标记厂商，无额外逻辑 |

### S7 协议

```
S7Reader (直接实现 IPlcReader)
```

| 特性 | 说明 |
|------|------|
| 库依赖 | `S7.Net` (S7.Net Plus) |
| 批量读取 | 不支持 — 逐 tag 遍历调用 `ReadAsync` |
| 连接参数 | IP + CpuType + Rack + Slot |
| 连接保活 | 无 KeepAlive，通过读取失败判断断线 |
| 自动重连 | 下次轮询时自动调用 `EnsureConnectedAsync` |
| 特殊处理 | `NormalizeValue` 将 DBD 的 uint 重新解释为 float |

### 工厂模式

`PlcReaderFactory` 根据配置中的 `PlcProtocol` 创建对应读取器：

```
config.Protocol
    ├── OpcUa → 按名称/EndpointUrl 是否含 "Siemens" 判断
    │            ├── SiemensOpcUaReader
    │            └── OmronOpcUaReader
    └── S7    → S7Reader
```

---

## 二、OPC UA 连接与节点读取

### 连接流程

```
appsettings.json
  └── PlcConnections[]  →  DI 注册 IOptions<List<PlcConnectionConfig>>
                                └── PlcBackgroundService 读取配置
                                      └── CreateReader() 创建 IPlcReader
                                            └── ConnectAsync() → OPC UA Session.Create
                                                  ├── SelectEndpoint (安全协商)
                                                  ├── Session.Create (建立会话)
                                                  └── KeepAlive 监听 (断线检测)
```

### 批量读取（OPC UA 特有）

OPC UA 支持单次 RPC 批量读取多个 Node，这在大点位场景下性能优势明显：

```csharp
var nodesToRead = tagPaths.Select(p => new ReadValueId
{
    NodeId = new NodeId(p),
    AttributeId = Attributes.Value,
}).ToList();

var response = await _session.ReadAsync(null, 0, TimestampsToReturn.Both,
    new ReadValueIdCollection(nodesToRead), ct);
```

### S7 读取方式

S7 协议不支持批量读取，逐 tag 遍历，失败时记录日志但不会中断整体轮询：

```csharp
foreach (var tagPath in tagPaths)
{
    try { results[tagPath] = MapPoint(tagPath, await _plc!.ReadAsync(tagPath)); }
    catch (Exception ex) { _logger.LogError(ex, ...); }
}
```

### 数据类型修正（S7 特有）

Siemens S7 PLC 中 DBD（双字）地址在 `S7.Net` 库中返回 `uint`，但实际存储的是 REAL（浮点数），`S7Reader.NormalizeValue` 将其按位重新解释：

```csharp
private static object? NormalizeValue(object? value)
{
    if (value is uint uintVal)
        return BitConverter.ToSingle(BitConverter.GetBytes(uintVal));
    return value;
}
```

---

## 三、缓存比较与增量更新

### 核心流程

```
后台轮询循环（每 1 秒）
    │
    ├── 遍历所有已连接的 reader
    │       │
    │       ├── reader.ReadAsync (批量/逐 tag 读取)
    │       │       │
    │       │       └── DetectChanges (与 _previousValues 比较)
    │       │               │
    │       │               ├── 首次读取 → 全部视为变化 → 写入缓存
    │       │               ├── 非首次 → 逐点比较 Value 和 Quality
    │       │               │       └── 有变化 → 加入 changedPoints + 更新缓存
    │       │               └── 无变化 → 跳过
    │       │
    │       └── changedPoints.Count > 0 → PushDataChangedAsync via SignalR
    │
    └── Task.Delay(1s)
```

### 变更检测算法

比较维度：**Value**（对象值）和 **Quality**（数据质量）。Timestamp 每次读取必然不同，不参与比较。

```csharp
foreach (var kvp in currentValues)
{
    if (lastValues 中不存在该点位) → 新点位，推送
    else if (Value 变化 || Quality 变化) → 推送

    lastValues[kvp.Key] = kvp.Value  // 更新缓存
}
```

### 断线重连处理

| 场景 | 行为 |
|------|------|
| OPC UA 断线 | KeepAlive 检测 → `OnConnectionStateChanged` → 清除缓存 → 后台自动重连 (10s 间隔) |
| S7 断线 | 读取失败 → `EnsureConnectedAsync` → 下次轮询自动重连 |
| 缓存策略 | 断线时清除对应 PLC 的 `_previousValues`，重连后首次轮询全部重新推送 |

### 线程安全

`_previousValues` 使用 `ConcurrentDictionary` 保护：
- 轮询线程：`DetectChanges()` 中读写
- 事件线程：`OnConnectionStateChanged()` 中 `TryRemove`

---

## 四、SignalR 实时数据推送

### 整体链路

```
后端                             前端
────                            ────
PlcBackgroundService            PlcDashboard.vue
    │                                │
    ├── DetectChanges()              │ (被动接收)
    │     └── changedPoints          │
    │           │                    │
    ▼           ▼                    │
PushDataChangedAsync() ── SignalR ──► onDataChanged()
    │                    │           │
    ▼                    ▼           ▼
_hubContext.Clients          pointData (reactive)
    .Group(name)                  │
    .SendAsync("DataChanged")     │
                                  ▼
                          PlcConnectionCard
                              :point-data prop
                                  │
                                  ▼
                          PlcDataPointItem
                          (value, quality, timestamp)
```

### 连接管理

```
前端连接 SignalR Hub (/hubs/plc)
    │
    ├── JoinPlcGroup(readerName) → 加入指定 PLC 的频道
    │                               只接收该 PLC 的数据推送
    │
    └── 自动重连策略 [0, 2s, 5s, 10s, 30s]
           │
           └── reconnected → 自动重新 JoinGroup
```

### 推送协议格式

**DataChanged**（点位数据变更）：
```json
{
  "readerName": "Siemens-S7-1200",
  "protocol": "OpcUa",
  "timestamp": "2026-06-02T10:30:00Z",
  "points": [
    {
      "tagPath": "ns=3;s=\"Axis_Status\".\"Inject.ActPosition\"",
      "value": 1234.5,
      "quality": "Good",
      "timestamp": "2026-06-02T10:30:00Z"
    }
  ]
}
```

**ConnectionStateChanged**（连接状态变更）：
```json
{
  "readerName": "Omron-NJ101",
  "protocol": "OpcUa",
  "isConnected": true,
  "timestamp": "2026-06-02T10:30:00Z"
}
```

### 前端数据流

```
SignalR onDataChanged
    → pointData (reactive)  ← 数据源唯一
        → PlcConnectionCard :point-data prop
            → PlcDataPointItem props
                → 模板渲染
```

- 纯 props 下行数据，不通过 ref 调子组件方法
- 首次加载通过 REST API `/api/connections/{name}/read` 获取初始值
- 之后全部依赖 SignalR 增量推送

---

## 五、REST API

| 端点 | 方法 | 用途 |
|------|------|------|
| `/api/connections` | GET | 获取所有 PLC 连接配置（名称、协议、端点、点位列表） |
| `/api/connections/{name}/read` | GET | 一次性读取指定 PLC 的全部点位当前值 |

---

## 六、配置模型

`appsettings.json` 示例：

```json
{
  "PlcConnections": [
    {
      "name": "Siemens-S7-1200",
      "protocol": "OpcUa",
      "endpointUrl": "opc.tcp://192.168.1.100:4840",
      "points": [
        { "id": "1", "tagPath": "ns=3;s=\"Axis_Status\".\"Inject.ActPosition\"", "name": "注射实际位置" }
      ]
    }
  ]
}
```

S7 协议的 `endpointUrl` 填写 PLC 的 IP 地址，代码中通过 `S7Reader` 构造函数传入并配合 `CpuType` / `Rack` / `Slot` 参数连接到 S7 PLC。

---

## 七、可扩展方向

1. **支持更多协议** — `IPlcReader` 已预留 `S7`、`Cip`、`ModbusTcp` 枚举值，新增协议只需实现接口并在 `PlcReaderFactory` 中添加分支
2. **点位写操作** — 当前只有监控（只读），可扩展下发写操作
3. **历史数据存储** — 点位变更数据可以写入时序数据库
4. **报警规则引擎** — 基于值范围/变化率触发告警
5. **多用户认证** — 当前未实现鉴权
6. **点位分组/仪表盘自定义** — 前端可扩展更加灵活的展示布局
7. **OPC UA 安全** — 支持证书认证、加密通信
8. **在线配置热加载** — 当前启动时读取配置，可扩展运行时动态增删 PLC
