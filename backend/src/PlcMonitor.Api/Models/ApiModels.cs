namespace PlcMonitor.Api.Models;

public record PlcConnectionInfo(
    string Name,
    string Protocol,
    string EndpointUrl,
    bool IsConnected,
    PlcPointInfo[] Points);

public record PlcPointInfo(
    string Id,
    string TagPath,
    string Name,
    string? Description);
