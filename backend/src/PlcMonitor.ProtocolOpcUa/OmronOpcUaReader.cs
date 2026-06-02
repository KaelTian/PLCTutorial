using Microsoft.Extensions.Logging;
using PlcMonitor.Core;

namespace PlcMonitor.ProtocolOpcUa;

/// <summary>
/// Omron NJ/NX PLC via OPC UA — 默认使用欧姆龙典型端点 opc.tcp://192.168.250.1:4840
/// </summary>
public class OmronOpcUaReader : OpcUaReaderBase
{
    public OmronOpcUaReader(string name, string endpointUrl, ILogger<OmronOpcUaReader> logger)
        : base(name, endpointUrl, logger)
    {
    }

    protected override bool UseSecurity => false;
}
