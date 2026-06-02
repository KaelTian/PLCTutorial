using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using PlcMonitor.Api.Hubs;
using PlcMonitor.Core;
namespace PlcMonitor.Api.Services;

/// <summary>
/// 后台服务 — 定时轮询所有 PLC 点位，检测变化后通过 SignalR 推送
/// </summary>
public class PlcBackgroundService : BackgroundService
{
    private readonly IHubContext<PlcDataHub> _hubContext;
    private readonly ILogger<PlcBackgroundService> _logger;
    private readonly IReadOnlyList<PlcConnectionConfig> _connections;
    private readonly List<IPlcReader> _readers = [];
    private readonly ConcurrentDictionary<string, Dictionary<string, PlcDataPoint>> _previousValues = [];

    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(1);

    public PlcBackgroundService(
        IHubContext<PlcDataHub> hubContext,
        IOptions<List<PlcConnectionConfig>> connectionOptions,
        ILogger<PlcBackgroundService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
        _connections = connectionOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_connections.Count == 0)
        {
            _logger.LogWarning("No PLC connections configured");
            return;
        }

        // 创建并连接所有读取器
        foreach (var config in _connections)
        {
            var reader = CreateReader(config);
            if (reader == null) continue;

            reader.ConnectionStateChanged += OnConnectionStateChanged;
            _readers.Add(reader);
        }

        foreach (var reader in _readers)
        {
            try
            {
                await reader.ConnectAsync(stoppingToken);
                _logger.LogInformation("PLC {Name} connected", reader.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect PLC {Name}", reader.Name);
            }
        }

        // 轮询循环：定时批量读取，与缓存比较，只推送变化的值
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var reader in _readers)
            {
                if (!reader.IsConnected) continue;

                var config = _connections.First(c => c.Name == reader.Name);
                if (config.Points.Length == 0) continue;

                try
                {
                    var currentValues = await reader.ReadAsync(
                        config.Points.Select(p => p.TagPath), stoppingToken);

                    var changedPoints = DetectChanges(reader.Name, currentValues);
                    if (changedPoints.Count > 0)
                    {
                        await PushDataChangedAsync(reader.Name, changedPoints);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to poll PLC {Name}", reader.Name);
                }
            }

            await Task.Delay(PollingInterval, stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Shutting down PLC readers...");

        foreach (var reader in _readers)
        {
            reader.ConnectionStateChanged -= OnConnectionStateChanged;
            await reader.DisconnectAsync();
        }

        _readers.Clear();
        _previousValues.Clear();
        await base.StopAsync(cancellationToken);
    }

    /// <summary>
    /// 与上次缓存比较，返回真正变化的点位；首次读取时全部视为变化
    /// </summary>
    private Dictionary<string, PlcDataPoint> DetectChanges(
        string readerName, IReadOnlyDictionary<string, PlcDataPoint> currentValues)
    {
        var changed = new Dictionary<string, PlcDataPoint>();

        if (!_previousValues.TryGetValue(readerName, out var lastValues))
        {
            // 首次读取 — 全部推送
            lastValues = new Dictionary<string, PlcDataPoint>(currentValues.Count);
            foreach (var kvp in currentValues)
            {
                lastValues[kvp.Key] = kvp.Value;
                changed[kvp.Key] = kvp.Value;
            }
            _previousValues[readerName] = lastValues;
            return changed;
        }

        foreach (var kvp in currentValues)
        {
            if (!lastValues.TryGetValue(kvp.Key, out var lastVal))
            {
                changed[kvp.Key] = kvp.Value;
            }
            else if (!Equals(lastVal.Value, kvp.Value.Value) || lastVal.Quality != kvp.Value.Quality)
            {
                changed[kvp.Key] = kvp.Value;
            }

            lastValues[kvp.Key] = kvp.Value;
        }

        return changed;
    }

    private async Task PushDataChangedAsync(string readerName, Dictionary<string, PlcDataPoint> changedPoints)
    {
        var payload = new
        {
            readerName,
            protocol = PlcProtocol.OpcUa.ToString(),
            timestamp = DateTime.UtcNow,
            points = changedPoints.Select(p => new
            {
                tagPath = p.Key,
                value = p.Value.Value,
                quality = p.Value.Quality.ToString(),
                timestamp = p.Value.Timestamp,
            }),
        };

        await _hubContext.Clients.Group(readerName)
            .SendAsync("DataChanged", payload);
    }

    private async void OnConnectionStateChanged(object? sender, PlcConnectionStateEventArgs e)
    {
        try
        {
            var payload = new
            {
                readerName = e.ReaderName,
                protocol = e.Protocol.ToString(),
                isConnected = e.IsConnected,
                timestamp = DateTime.UtcNow,
            };

            await _hubContext.Clients.All
                .SendAsync("ConnectionStateChanged", payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to push connection state change for {Reader}", e.ReaderName);
        }

        // 断线时清除缓存，重连后下次轮询会全部重新推送
        if (!e.IsConnected)
        {
            _previousValues.TryRemove(e.ReaderName, out _);
        }
    }

    private static IPlcReader? CreateReader(PlcConnectionConfig config)
    {
        var loggerFactory = LoggerFactory.Create(b =>
            b.AddConsole().SetMinimumLevel(LogLevel.Information));

        return PlcReaderFactory.CreateReader(config, loggerFactory);
    }
}
