using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using PlcMonitor.Core;

namespace PlcMonitor.ProtocolOpcUa;

public abstract class OpcUaReaderBase : IPlcReader
{
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Session? _session;
    private ApplicationConfiguration? _appConfig;
    private bool _disposed;

    protected OpcUaReaderBase(string name, string endpointUrl, ILogger logger)
    {
        Name = name;
        EndpointUrl = endpointUrl;
        _logger = logger;
    }

    public string Name { get; }
    public PlcProtocol Protocol => PlcProtocol.OpcUa;
    public bool IsConnected => _session?.Connected == true;
    protected string EndpointUrl { get; }
    protected virtual bool UseSecurity => false;
    protected virtual uint SessionTimeout => 60_000;

    public event EventHandler<PlcConnectionStateEventArgs>? ConnectionStateChanged;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (IsConnected) return;

        await _lock.WaitAsync(ct);
        try
        {
            if (IsConnected) return;

            _appConfig ??= await CreateApplicationConfigAsync();

            var endpoint = CoreClientUtils.SelectEndpoint(_appConfig, EndpointUrl, UseSecurity);
            var configuredEndpoint = new ConfiguredEndpoint(null, endpoint);

            _session = await Session.Create(
                _appConfig,
                configuredEndpoint,
                updateBeforeConnect: true,
                sessionName: $"PlcMonitor-{Name}",
                sessionTimeout: SessionTimeout,
                identity: null,
                preferredLocales: null,
                ct);

            _session.KeepAlive += OnKeepAlive;
            _session.SessionClosing += OnSessionClosing;

            NotifyConnectionState(true);
            _logger.LogInformation("Connected to {Endpoint}", EndpointUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to {Endpoint}", EndpointUrl);
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await CleanupSessionAsync();
            NotifyConnectionState(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<PlcDataPoint?> ReadAsync(string tagPath, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);

        var nodeId = new NodeId(tagPath);
        var readValue = new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value };

        try
        {
            var response = await _session!.ReadAsync(null, 0, TimestampsToReturn.Both, [readValue], ct);
            var dataValue = response.Results[0];
            return MapToDataPoint(tagPath, dataValue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read tag {TagPath}", tagPath);
            return null;
        }
    }

    public async Task<IReadOnlyDictionary<string, PlcDataPoint>> ReadAsync(
        IEnumerable<string> tagPaths, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);

        var nodesToRead = tagPaths.Select(p => new ReadValueId
        {
            NodeId = new NodeId(p),
            AttributeId = Attributes.Value,
        }).ToList();

        try
        {
            var response = await _session!.ReadAsync(null, 0, TimestampsToReturn.Both, new ReadValueIdCollection(nodesToRead), ct);
            var results = new Dictionary<string, PlcDataPoint>();
            var i = 0;
            foreach (var tagPath in tagPaths)
            {
                results[tagPath] = MapToDataPoint(tagPath, response.Results[i++]);
            }
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to batch read {Count} tags", tagPaths.Count());
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await CleanupSessionAsync();
        GC.SuppressFinalize(this);
    }

    // ── protected hooks for derived implementations ──

    protected virtual Task<ApplicationConfiguration> CreateApplicationConfigAsync()
    {
        var config = new ApplicationConfiguration
        {
            ApplicationName = $"PlcMonitor-{Name}",
            ApplicationType = ApplicationType.Client,
            TransportQuotas = new TransportQuotas
            {
                MaxMessageSize = 4 * 1024 * 1024,
                MaxByteStringLength = 4 * 1024 * 1024,
            },
            ClientConfiguration = new ClientConfiguration
            {
                DefaultSessionTimeout = (int)SessionTimeout,
            },
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier { StoreType = "Directory", StorePath = "OPC Foundation/CertificateStores/MachineDefault" },
                TrustedPeerCertificates = new CertificateTrustList { StoreType = "Directory", StorePath = "OPC Foundation/CertificateStores/MachineDefault" },
                RejectedCertificateStore = new CertificateStoreIdentifier { StoreType = "Directory", StorePath = "OPC Foundation/CertificateStores/MachineDefault" },
                AutoAcceptUntrustedCertificates = true,
            },
        };

        config.Validate(ApplicationType.Client);
        return Task.FromResult(config);
    }

    // ── private helpers ──

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (IsConnected) return;
        await ConnectAsync(ct);
    }

    private void OnKeepAlive(ISession session, KeepAliveEventArgs e)
    {
        if (e.Status != null && StatusCode.IsBad(e.Status.StatusCode))
        {
            _logger.LogWarning("KeepAlive failed for {Name}: {Status}", Name, e.Status);
            NotifyConnectionState(false);
            _ = ReconnectAsync();
        }
    }

    private void OnSessionClosing(object sender, EventArgs e)
    {
        _logger.LogInformation("Session closing for {Name}", Name);
        NotifyConnectionState(false);
    }

    private async Task ReconnectAsync()
    {
        _logger.LogInformation("Attempting reconnect for {Name}...", Name);
        try
        {
            await CleanupSessionAsync();
            await ConnectAsync();
            _logger.LogInformation("Reconnected to {Name}", Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reconnect failed for {Name}, retrying in 10s", Name);

            await Task.Delay(10_000);
            _ = ReconnectAsync();
        }
    }

    private async Task CleanupSessionAsync()
    {
        try
        {
            if (_session != null)
            {
                _session.KeepAlive -= OnKeepAlive;
                _session.SessionClosing -= OnSessionClosing;

                if (_session.Connected)
                    await _session.CloseAsync();

                _session.Dispose();
                _session = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during session cleanup for {Name}", Name);
        }
    }

    private void NotifyConnectionState(bool connected)
    {
        ConnectionStateChanged?.Invoke(this, new PlcConnectionStateEventArgs
        {
            ReaderName = Name,
            Protocol = Protocol,
            IsConnected = connected,
        });
    }

    private static PlcDataPoint MapToDataPoint(string tagPath, DataValue value)
    {
        return new PlcDataPoint
        {
            TagPath = tagPath,
            Value = value?.Value,
            Timestamp = value?.ServerTimestamp ?? DateTime.UtcNow,
            Quality = value?.StatusCode switch
            {
                null or { Code: StatusCodes.Good } => PlcDataQuality.Good,
                { Code: StatusCodes.Uncertain } => PlcDataQuality.Uncertain,
                _ => PlcDataQuality.Bad,
            },
        };
    }
}
