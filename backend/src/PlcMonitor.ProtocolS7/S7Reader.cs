using Microsoft.Extensions.Logging;
using PlcMonitor.Core;
using S7.Net;

namespace PlcMonitor.ProtocolS7;

public class S7Reader : IPlcReader
{
    private readonly string _name;
    private readonly string _ip;
    private readonly short _rack;
    private readonly short _slot;
    private readonly CpuType _cpuType;
    private readonly ILogger _logger;
    private Plc? _plc;
    private bool _lastKnownConnected;
    private bool _disposed;

    public S7Reader(string name, string endpointUrl, ILogger logger,
        CpuType cpuType = CpuType.S71500, short rack = 0, short slot = 0)
    {
        _name = name;
        _ip = endpointUrl;
        _cpuType = cpuType;
        _rack = rack;
        _slot = slot;
        _logger = logger;
    }

    public string Name => _name;
    public PlcProtocol Protocol => PlcProtocol.S7;
    public bool IsConnected => _plc?.IsConnected ?? false;

    public event EventHandler<PlcConnectionStateEventArgs>? ConnectionStateChanged;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (IsConnected) return;

        _plc = new Plc(_cpuType, _ip, _rack, _slot);
        try
        {
            await _plc.OpenAsync(ct);
            UpdateConnectionState(true);
            _logger.LogInformation("Connected to S7 PLC {Name} at {IP}", _name, _ip);
        }
        catch
        {
            _plc?.Close();
            _plc = null;
            throw;
        }
    }

    public Task DisconnectAsync()
    {
        if (_plc != null)
        {
            _plc.Close();
            _plc = null;
        }
        UpdateConnectionState(false);
        return Task.CompletedTask;
    }

    public async Task<PlcDataPoint?> ReadAsync(string tagPath, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);

        try
        {
            var value = await _plc!.ReadAsync(tagPath);
            return new PlcDataPoint
            {
                TagPath = tagPath,
                Value = NormalizeValue(value),
                Timestamp = DateTime.UtcNow,
                Quality = PlcDataQuality.Good,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read S7 tag {TagPath} on {Name}", tagPath, _name);

            if (!IsConnected)
                UpdateConnectionState(false);

            return null;
        }
    }

    public async Task<IReadOnlyDictionary<string, PlcDataPoint>> ReadAsync(
        IEnumerable<string> tagPaths, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);

        var results = new Dictionary<string, PlcDataPoint>();

        foreach (var tagPath in tagPaths)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var value = await _plc!.ReadAsync(tagPath);
                results[tagPath] = new PlcDataPoint
                {
                    TagPath = tagPath,
                    Value = NormalizeValue(value),
                    Timestamp = DateTime.UtcNow,
                    Quality = PlcDataQuality.Good,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read S7 tag {TagPath} on {Name}", tagPath, _name);
            }
        }

        return results;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await DisconnectAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// S7NetPlus 对 DBD（双字）返回 uint，但 Siemens PLC 中 DBD 通常存储的是 REAL（浮点数）。
    /// 将 uint 按位重新解释为 float，使数值显示正常。
    /// </summary>
    private static object? NormalizeValue(object? value)
    {
        if (value is uint uintVal)
            return BitConverter.ToSingle(BitConverter.GetBytes(uintVal));
        return value;
    }

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (IsConnected) return;

        // Clean up stale instance if any
        if (_plc != null)
        {
            _plc.Close();
            _plc = null;
        }

        await ConnectAsync(ct);
    }

    private void UpdateConnectionState(bool connected)
    {
        if (_lastKnownConnected == connected) return;
        _lastKnownConnected = connected;

        ConnectionStateChanged?.Invoke(this, new PlcConnectionStateEventArgs
        {
            ReaderName = Name,
            Protocol = Protocol,
            IsConnected = connected,
        });
    }
}
