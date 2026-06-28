using System;
using System.Threading.Tasks;

namespace LaserficheAIExtension.Services
{
    /// <summary>
    /// Monitors the connection to the web app server and auto-reconnects.
    /// </summary>
    public interface IConnectionMonitorService
    {
        event EventHandler<bool> ConnectionStatusChanged;
        bool IsServerReachable { get; }
        Task StartMonitoringAsync(string serverUrl);
        void StopMonitoring();
    }
}
