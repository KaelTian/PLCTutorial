using Microsoft.Extensions.Logging;
using PlcMonitor.Core;

namespace PlcMonitor.ProtocolOpcUa;

/// <summary>
/// Siemens S7 PLC via OPC UA — 默认使用西门子典型端点 opc.tcp://192.168.0.1:4840
/// </summary>
public class SiemensOpcUaReader : OpcUaReaderBase
{
    public SiemensOpcUaReader(string name, string endpointUrl, ILogger<SiemensOpcUaReader> logger)
        : base(name, endpointUrl, logger)
    {
    }

    protected override bool UseSecurity => false; // Siemens PLC 开发环境常禁用安全
}
