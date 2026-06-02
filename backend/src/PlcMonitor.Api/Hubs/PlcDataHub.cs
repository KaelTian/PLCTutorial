using Microsoft.AspNetCore.SignalR;

namespace PlcMonitor.Api.Hubs;

/// <summary>
/// SignalR Hub — 向前端推送 PLC 点位变化和连接状态
/// </summary>
public class PlcDataHub : Hub
{
    private readonly ILogger<PlcDataHub> _logger;

    public PlcDataHub(ILogger<PlcDataHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>客户端加入指定 PLC 的数据频道</summary>
    public async Task JoinPlcGroup(string readerName)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, readerName);
        _logger.LogDebug("Client {Id} joined group {Group}", Context.ConnectionId, readerName);
    }

    /// <summary>客户端离开指定 PLC 的数据频道</summary>
    public async Task LeavePlcGroup(string readerName)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, readerName);
    }
}
