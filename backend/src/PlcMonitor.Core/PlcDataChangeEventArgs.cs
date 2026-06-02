namespace PlcMonitor.Core;

public class PlcDataChangeEventArgs : EventArgs
{
    public string ReaderName { get; init; } = string.Empty;
    public PlcProtocol Protocol { get; init; }
    public IReadOnlyDictionary<string, PlcDataPoint> ChangedPoints { get; init; }
        = new Dictionary<string, PlcDataPoint>();
}
