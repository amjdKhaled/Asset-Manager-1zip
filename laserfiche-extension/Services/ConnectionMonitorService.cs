using LaserficheAIExtension.Infrastructure.Logging;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace LaserficheAIExtension.Services
{
    /// <summary>
    /// Background service that pings the web app server and fires events on status change.
    /// </summary>
    public class ConnectionMonitorService : IConnectionMonitorService, IDisposable
    {
        private readonly ILogger<ConnectionMonitorService> _logger;
        private HttpClient _httpClient;
        private CancellationTokenSource _cts;
        private string _serverUrl;
        private bool _isReachable;
        private bool _isDisposed;

        public event EventHandler<bool> ConnectionStatusChanged;

        public bool IsServerReachable
        {
            get => _isReachable;
            private set
            {
                if (_isReachable != value)
                {
                    _isReachable = value;
                    ConnectionStatusChanged?.Invoke(this, value);
                    _logger.Information("Server connection status changed to {Status}", value ? "ONLINE" : "OFFLINE");
                }
            }
        }

        public ConnectionMonitorService(ILogger<ConnectionMonitorService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        }

        public async Task StartMonitoringAsync(string serverUrl)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(ConnectionMonitorService));
            _serverUrl = serverUrl;
            StopMonitoringInternal();
            _cts = new CancellationTokenSource();

            _logger.Information("Starting connection monitor for {Url}", serverUrl);

            try
            {
                await CheckConnectionAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            // Fire-and-forget background loop — safe because we hold CTS
            _ = Task.Run(async () =>
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(3000, _cts.Token);
                        await CheckConnectionAsync(_cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Connection check error");
                    }
                }
            }, _cts.Token);
        }

        public void StopMonitoring()
        {
            if (_isDisposed) return;
            StopMonitoringInternal();
            _logger.Information("Connection monitor stopped");
        }

        private void StopMonitoringInternal()
        {
            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;
            }
            catch { /* best effort */ }
        }

        private async Task CheckConnectionAsync(CancellationToken cancellationToken)
        {
            try
            {
                var response = await _httpClient.GetAsync(_serverUrl, cancellationToken);
                IsServerReachable = response.IsSuccessStatusCode;
            }
            catch
            {
                IsServerReachable = false;
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _logger.Information("ConnectionMonitorService disposing");
            StopMonitoringInternal();
            try { _httpClient?.Dispose(); }
            catch { }
            _httpClient = null;
        }
    }
}
