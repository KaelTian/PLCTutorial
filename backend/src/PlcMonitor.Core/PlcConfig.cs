namespace PlcMonitor.Core;

public class PlcPointConfig
{
    public string Id { get; init; } = string.Empty;
    public string TagPath { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
}

public class PlcConnectionConfig
{
    public string Name { get; init; } = string.Empty;
    public PlcProtocol Protocol { get; init; }
    public string EndpointUrl { get; init; } = string.Empty;
    public PlcPointConfig[] Points { get; init; } = [];
}
