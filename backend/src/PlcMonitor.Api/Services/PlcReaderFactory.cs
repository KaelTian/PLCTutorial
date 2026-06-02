using PlcMonitor.Core;
using PlcMonitor.ProtocolOpcUa;
using PlcMonitor.ProtocolS7;

namespace PlcMonitor.Api.Services;

public static class PlcReaderFactory
{
    /// <summary>
    /// 根据协议和厂商标识创建对应的读取器实例
    /// </summary>
    public static IPlcReader? CreateReader(PlcConnectionConfig config, ILoggerFactory loggerFactory)
    {
        switch (config.Protocol)
        {
            case PlcProtocol.OpcUa:
                var isSiemens = config.Name.Contains("Siemens", StringComparison.OrdinalIgnoreCase)
                                || config.EndpointUrl.Contains("siemens", StringComparison.OrdinalIgnoreCase);
                return isSiemens
                    ? new SiemensOpcUaReader(config.Name, config.EndpointUrl,
                        loggerFactory.CreateLogger<SiemensOpcUaReader>())
                    : new OmronOpcUaReader(config.Name, config.EndpointUrl,
                        loggerFactory.CreateLogger<OmronOpcUaReader>());

            case PlcProtocol.S7:
                return new S7Reader(config.Name, config.EndpointUrl,
                    loggerFactory.CreateLogger<S7Reader>());

            default:
                return null;
        }
    }
}
