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

            // Background loop on a dedicated thread to avoid thread-pool starvation.
            // The loop body blocks with GetAwaiter().GetResult() because ThreadStart
            // must be synchronous; every async point is wrapped in try/catch.
            var capturedCts = _cts;  // Capture so Dispose() nulling _cts does not race.
            var thread = new Thread(() =>
            {
                while (!capturedCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        Task.Delay(3000, capturedCts.Token).GetAwaiter().GetResult();
                        CheckConnectionAsync(capturedCts.Token).GetAwaiter().GetResult();
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
            })
            { IsBackground = true, Name = "ConnectionMonitor" };
            thread.Start();
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
            // Capture local reference so Dispose() nulling _httpClient does not race.
            var client = _httpClient;
            if (client == null) return;

            try
            {
                var response = await client.GetAsync(_serverUrl, cancellationToken);
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
