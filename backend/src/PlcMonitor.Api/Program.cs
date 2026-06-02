using PlcMonitor.Api.Hubs;
using PlcMonitor.Api.Models;
using PlcMonitor.Api.Services;
using PlcMonitor.Core;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// SignalR
builder.Services.AddSignalR();

// CORS — 允许 Vue 前端跨域访问
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()
              .SetIsOriginAllowed(_ => true);
    });
});

// PLC 后台监控服务
builder.Services.AddPlcMonitoring(builder.Configuration);

var app = builder.Build();

app.UseCors();
app.MapHub<PlcDataHub>("/hubs/plc");

// REST API: 获取所有 PLC 连接信息
app.MapGet("/api/connections", (
    IOptions<List<PlcConnectionConfig>> configOptions) =>
{
    return configOptions.Value.Select(c => new PlcConnectionInfo(
        c.Name,
        c.Protocol.ToString(),
        c.EndpointUrl,
        false, // 实际连接状态通过 SignalR 推送
        c.Points.Select(p => new PlcPointInfo(
            p.Id, p.TagPath, p.Name, p.Description
        )).ToArray()
    ));
})
.WithName("GetConnections");

// REST API: 读取指定 PLC 的全部点位
app.MapGet("/api/connections/{name}/read", async (
    string name,
    IOptions<List<PlcConnectionConfig>> configOptions,
    IServiceProvider sp,
    CancellationToken ct) =>
{
    var configs = configOptions.Value;
    var config = configs.FirstOrDefault(c =>
        c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    if (config == null)
        return Results.NotFound(new { error = $"PLC '{name}' not found" });

    var loggerFactory = LoggerFactory.Create(b =>
        b.AddConsole().SetMinimumLevel(LogLevel.Information));

    var reader = PlcReaderFactory.CreateReader(config, loggerFactory);
    if (reader == null)
        return Results.BadRequest(new { error = $"Protocol '{config.Protocol}' not supported yet" });

    await using var _ = reader;
    await reader.ConnectAsync(ct);

    var results = await reader.ReadAsync(
        config.Points.Select(p => p.TagPath), ct);

    var data = config.Points.Select(p =>
    {
        var hasValue = results.TryGetValue(p.TagPath, out var point);
        return new
        {
            p.Id,
            p.Name,
            p.TagPath,
            value = hasValue ? point.Value : null,
            quality = point?.Quality.ToString() ?? "Disconnected",
            timestamp = point?.Timestamp,
        };
    });

    return Results.Ok(data);
})
.WithName("ReadPlc");

app.Run();
