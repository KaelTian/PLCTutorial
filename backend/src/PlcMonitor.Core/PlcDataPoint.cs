namespace PlcMonitor.Core;

public class PlcDataPoint
{
    public string TagPath { get; init; } = string.Empty;
    public string? Name { get; init; }
    public object? Value { get; init; }
    public DateTime Timestamp { get; init; }
    public PlcDataQuality Quality { get; init; } = PlcDataQuality.Good;
}
