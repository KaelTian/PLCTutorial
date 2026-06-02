namespace PlcMonitor.Core;

public class PlcConnectionStateEventArgs : EventArgs
{
    public string ReaderName { get; init; } = string.Empty;
    public PlcProtocol Protocol { get; init; }
    public bool IsConnected { get; init; }
}
