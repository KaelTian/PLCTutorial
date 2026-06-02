# PLC 监控面板 — 技术总结

## 项目结构

```
PLCTutorial/
├── backend/                          # .NET 8 后端
│   └── src/
│       ├── PlcMonitor.Core/          # 核心模型与接口定义
│       │   ├── IPlcReader.cs         # PLC 读取器抽象接口
│       │   ├── PlcConfig.cs          # PLC 连接配置模型 (JSON 反序列化)
│       │   ├── PlcDataPoint.cs       # 点位数据结构
│       │   ├── PlcDataQuality.cs     # 数据质量枚举 (Good/Uncertain/Bad)
│       │   ├── PlcProtocol.cs        # 协议类型枚举 (OpcUa/S7/Cip/ModbusTcp)
│       │   ├── PlcDataChangeEventArgs.cs    # (备用) 数据变更事件参数
│       │   └── PlcConnectionStateEventArgs.cs # 连接状态事件参数
│       ├── PlcMonitor.ProtocolOpcUa/ # OPC UA 协议实现
│       │   ├── OpcUaReaderBase.cs    # OPC UA 读取器基类（核心实现）
│       │   ├── SiemensOpcUaReader.cs # Siemens 厂商标识类
│       │   ├── OmronOpcUaReader.cs   # Omron 厂商标识类
│       │   └── DependencyInjection.cs # DI 注册扩展
│       └── PlcMonitor.Api/           # HTTP API + SignalR 服务
│           ├── Program.cs            # 入口：REST API 端点定义
│           ├── Services/
│           │   ├── PlcBackgroundService.cs # 后台轮询 + 变更检测 + SignalR 推送
│           │   └── DependencyInjection.cs  # DI 注册
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

## 一、OPC UA 连接与节点读取

### 架构层次

```
IPlcReader (接口抽象)
    └── OpcUaReaderBase (OPC UA 协议实现)
            ├── SiemensOpcUaReader (西门子标识)
            └── OmronOpcUaReader (欧姆龙标识)
```

- `IPlcReader` 定义连接/读取/断开的标准接口，与具体协议解耦
- `OpcUaReaderBase` 基于 **OPC UA .NET Standard 库** (`Opc.Ua`, `Opc.Ua.Client`) 实现
- 派生类仅做厂商标识，无额外逻辑；后续可按需扩展差异化行为

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

### 批量读取

`ReadAsync(IEnumerable<string> tagPaths)` 使用 OPC UA 的 **批量 Read 服务**（单次请求读取多个 Node），避免逐节点读取的 N+1 开销：

```csharp
// 拼装批量请求
var nodesToRead = tagPaths.Select(p => new ReadValueId
{
    NodeId = new NodeId(p),
    AttributeId = Attributes.Value,
}).ToList();

// 一次 RPC 返回全部结果
var response = await _session.ReadAsync(null, 0, TimestampsToReturn.Both,
    new ReadValueIdCollection(nodesToRead), ct);
```

---

## 二、缓存比较与增量更新

### 核心流程

```
后台轮询循环（每 1 秒）
    │
    ├── ReadAsync (批量读取全部点位)
    │       │
    │       └── DetectChanges (与 _previousValues 比较)
    │               │
    │               ├── 首次读取 → 全部视为变化 → 写入缓存
    │               ├── 非首次 → 逐点比较 Value 和 Quality
    │               │       └── 有变化 → 加入 changedPoints + 更新缓存
    │               └── 无变化 → 跳过
    │
    └── changedPoints.Count > 0 → PushDataChangedAsync via SignalR
```

### 变更检测算法

比较维度：**Value**（对象值）和 **Quality**（数据质量）。Timestamp 每次读取必然不同，不参与比较。

```csharp
// 伪代码
foreach (var kvp in currentValues)
{
    if (lastValues 中不存在该点位)
        → 新点位，推送
    else if (Value 变化 || Quality 变化)
        → 推送
    lastValues[kvp.Key] = kvp.Value  // 更新缓存
}
```

### 断线重连处理

- PLC 断线时 `OnConnectionStateChanged` 触发，**清除**对应 PLC 的缓存
- 重连成功后下次轮询为**首次读取**，全部点位重新推送
- 确保前端不会因断线期间的数据丢失而显示过期值

### 线程安全

`_previousValues` 使用 `ConcurrentDictionary` 保护：
- 轮询线程：`DetectChanges()` 中读写
- 事件线程：`OnConnectionStateChanged()` 中 `TryRemove`
- 内外两层字典分离，外层并发安全，内层（每个 PLC 的缓存）单线程访问

---

## 三、SignalR 实时数据推送

### 整体链路

```
后端                                             前端
────                                            ────
PlcBackgroundService                            PlcDashboard.vue
    │                                                │
    ├── DetectChanges()                              │ (被动接收)
    │     └── changedPoints                          │
    │           │                                    │
    ▼           ▼                                    │
PushDataChangedAsync()  ─── SignalR ──►  onDataChanged()
    │                          │                     │
    │                   ┌──────┘                     │
    ▼                   ▼                            ▼
_hubContext.Clients                          pointData (reactive)
    .Group(name)                                  │
    .SendAsync("DataChanged")                     │
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
    ├── JoinPlcGroup(readerName)  → 加入指定 PLC 的频道
    │                                  │
    │                                  └── 只接收该 PLC 的数据推送
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

### 前端数据流（单向响应式）

```
SignalR onDataChanged
    → pointData (reactive)  ← 数据源唯一，数据驱动
        → PlcConnectionCard :point-data prop
            → PlcDataPointItem props
                → 模板渲染
```

- 不通过 ref 调子组件方法，纯 props 下行数据
- 首次加载通过 REST API `/api/connections/{name}/read` 获取初始值
- 之后全部依赖 SignalR 增量推送

---

## 四、REST API

| 端点 | 方法 | 用途 |
|------|------|------|
| `/api/connections` | GET | 获取所有 PLC 连接配置（名称、协议、端点、点位列表） |
| `/api/connections/{name}/read` | GET | 一次性读取指定 PLC 的全部点位当前值 |

---

## 五、可扩展方向

1. **支持更多协议** — `IPlcReader` 已预留 `S7`、`Cip`、`ModbusTcp` 枚举值，新增协议只需实现该接口并在 `PlcBackgroundService.CreateReader` 中添加分支
2. **点位写操作** — 前端可以下发写操作（如设定参数），当前只有监控（只读）
3. **历史数据存储** — 点位变更数据可以写入时序数据库
4. **报警规则引擎** — 基于值范围/变化率触发告警
5. **多用户认证** — 当前未实现鉴权，直接广播给所有连接客户端
6. **点位分组/仪表盘自定义** — 前端可扩展更加灵活的展示布局
7. **OPC UA 安全** — 支持证书认证、加密通信，当前开发环境禁用了安全
8. **在线配置热加载** — 当前配置在启动时读取 `appsettings.json`，可扩展运行时动态增删 PLC
