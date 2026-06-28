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
    public class ConnectionMonitorService : IConnectionMonitorService
    {
        private readonly ILogger<ConnectionMonitorService> _logger;
        private readonly HttpClient _httpClient;
        private CancellationTokenSource _cts;
        private string _serverUrl;
        private bool _isReachable;

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
            _serverUrl = serverUrl;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            _logger.Information("Starting connection monitor for {Url}", serverUrl);

            try
            {
                // Immediate check
                await CheckConnectionAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

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
            _cts?.Cancel();
            _logger.Information("Connection monitor stopped");
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
    }
}
