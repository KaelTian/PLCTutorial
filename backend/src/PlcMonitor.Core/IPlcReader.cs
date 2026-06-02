namespace PlcMonitor.Core;

/// <summary>
/// PLC 读取器公共接口 — 所有协议组件的统一抽象
/// </summary>
public interface IPlcReader : IAsyncDisposable
{
    string Name { get; }
    PlcProtocol Protocol { get; }
    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();

    /// <summary>读取单个点位</summary>
    Task<PlcDataPoint?> ReadAsync(string tagPath, CancellationToken ct = default);

    /// <summary>批量读取多个点位</summary>
    Task<IReadOnlyDictionary<string, PlcDataPoint>> ReadAsync(
        IEnumerable<string> tagPaths, CancellationToken ct = default);

    /// <summary>连接状态变化时触发</summary>
    event EventHandler<PlcConnectionStateEventArgs>? ConnectionStateChanged;
}
