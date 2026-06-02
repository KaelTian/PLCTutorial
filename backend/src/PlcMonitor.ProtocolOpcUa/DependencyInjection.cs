using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlcMonitor.Core;

namespace PlcMonitor.ProtocolOpcUa;

public static class DependencyInjection
{
    /// <summary>
    /// 注册 OPC UA 类型的 PLC 读取器到 DI 容器。
    /// 通过配置驱动，支持 Siemens / Omron 等多种 OPC UA 设备。
    /// </summary>
    public static IServiceCollection AddOpcUaReaders(
        this IServiceCollection services,
        IEnumerable<PlcConnectionConfig> connections)
    {
        foreach (var config in connections)
        {
            if (config.Protocol != PlcProtocol.OpcUa) continue;

            // 通过 Name 区分不同 OPC UA 设备，注册为 IPlcReader 服务
            services.AddKeyedSingleton<IPlcReader>(config.Name, (sp, key) =>
            {
                var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                var endpoint = config.EndpointUrl;

                // 根据名称约定或配置自动选择厂商标识
                // 也可通过扩展配置字段明确指定，此处简化处理
                var isSiemens = config.Name.Contains("Siemens", StringComparison.OrdinalIgnoreCase)
                    || endpoint.Contains("siemens", StringComparison.OrdinalIgnoreCase);

                return isSiemens
                    ? new SiemensOpcUaReader(config.Name, endpoint,
                        loggerFactory.CreateLogger<SiemensOpcUaReader>())
                    : new OmronOpcUaReader(config.Name, endpoint,
                        loggerFactory.CreateLogger<OmronOpcUaReader>());
            });
        }

        return services;
    }
}
