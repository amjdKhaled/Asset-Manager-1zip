using LaserficheAIExtension.Models;
using System;
using System.Threading.Tasks;

namespace LaserficheAIExtension.Services
{
    /// <summary>
    /// Service for bidirectional communication with the embedded web app.
    /// </summary>
    public interface IWebAppCommunicationService : IDisposable
    {
        event EventHandler<WebCommand> CommandReceived;
        event EventHandler<bool> ConnectionStateChanged;

        void Initialize(object webView);
        Task SendDocumentContextAsync(DocumentContext context);
        Task SendCommandAsync(string command, object payload);
        Task<bool> PingAsync();
        bool IsConnected { get; }
        string ServerUrl { get; set; }
    }
}
