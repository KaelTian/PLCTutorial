using Microsoft.Extensions.Options;
using PlcMonitor.Core;

namespace PlcMonitor.Api.Services;

public static class DependencyInjection
{
    public static IServiceCollection AddPlcMonitoring(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 绑定 PLC 连接配置
        var connections = configuration.GetSection("PlcConnections")
            .Get<List<PlcConnectionConfig>>() ?? [];

        if (connections.Count > 0)
        {
            services.AddSingleton(Options.Create(connections));
        }

        // 注册后台监控服务
        services.AddHostedService<PlcBackgroundService>();

        return services;
    }
}
