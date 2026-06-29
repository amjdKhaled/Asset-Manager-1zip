using LaserficheAIExtension.Infrastructure.Logging;
using LaserficheAIExtension.Models;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using System;
using System.Threading.Tasks;

namespace LaserficheAIExtension.Services
{
    /// <summary>
    /// Handles all communication between the WPF host and the embedded web application.
    /// Uses WebView2's WebMessage API for bidirectional JSON messaging.
    /// </summary>
    public class WebAppCommunicationService : IWebAppCommunicationService
    {
        private CoreWebView2 _webView;
        private readonly ILogger<WebAppCommunicationService> _logger;
        private bool _isConnected;

        public event EventHandler<WebCommand> CommandReceived;
        public event EventHandler<bool> ConnectionStateChanged;

        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (_isConnected != value)
                {
                    _isConnected = value;
                    ConnectionStateChanged?.Invoke(this, value);
                }
            }
        }

        public string ServerUrl { get; set; } = "http://localhost:5000";

        public WebAppCommunicationService(ILogger<WebAppCommunicationService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void Initialize(object webView)
        {
            if (webView is CoreWebView2 coreWebView)
            {
                _webView = coreWebView;
                _webView.WebMessageReceived += OnWebMessageReceived;
                _webView.NavigationCompleted += OnNavigationCompleted;
                _webView.ProcessFailed += OnProcessFailed;

                // Inject a bridge script that lets the web app send messages back
                _webView.DOMContentLoaded += async (s, e) =>
                {
                    await InjectBridgeScriptAsync();
                };

                _logger.Information("WebView2 communication initialized");
            }
            else
            {
                throw new ArgumentException("Expected CoreWebView2 instance", nameof(webView));
            }
        }

        private async Task InjectBridgeScriptAsync()
        {
            const string bridgeScript = @"
                window.LaserficheBridge = {
                    sendCommand: function(command, payload) {
                        const message = JSON.stringify({
                            command: command,
                            payload: payload || {},
                            timestamp: Date.now(),
                            requestId: Math.random().toString(36).substr(2, 9)
                        });
                        window.chrome.webview.postMessage(message);
                    },
                    ping: function() {
                        this.sendCommand('Ping', {});
                    }
                };
                window.dispatchEvent(new CustomEvent('laserfiche-bridge-ready'));
            ";

            try
            {
                await _webView.ExecuteScriptAsync(bridgeScript);
                _logger.Debug("Bridge script injected into web app");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to inject bridge script");
            }
        }

        private void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var message = e.TryGetWebMessageAsString();
                if (string.IsNullOrEmpty(message)) return;

                _logger.Debug("Message received from web app: {Message}", message.Substring(0, Math.Min(200, message.Length)));

                var command = WebCommand.FromJson(message);
                if (command != null)
                {
                    CommandReceived?.Invoke(this, command);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to process web message");
            }
        }

        private void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            IsConnected = e.IsSuccess;
            if (e.IsSuccess)
            {
                _logger.Information("Navigation completed successfully to {Url}", _webView?.Source);
            }
            else
            {
                _logger.Warning("Navigation failed with HRESULT: {HResult}", e.WebErrorStatus);
            }
        }

        private void OnProcessFailed(object sender, CoreWebView2ProcessFailedEventArgs e)
        {
            _logger.Error($"WebView2 process failed: {e.ProcessFailedKind}");
            IsConnected = false;
        }

        public async Task SendDocumentContextAsync(DocumentContext context)
        {
            if (_webView == null || !IsConnected) return;

            try
            {
                var json = context.ToJson();
                var script = $"window.dispatchEvent(new CustomEvent('laserfiche-document-changed', {{ detail: {json} }}));";
                await _webView.ExecuteScriptAsync(script);
                _logger.Debug("Document context sent to web app for entry {EntryId}", context.EntryId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to send document context");
            }
        }

        public async Task SendCommandAsync(string command, object payload)
        {
            if (_webView == null || !IsConnected) return;

            try
            {
                var message = JsonConvert.SerializeObject(new { command, payload, timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
                var script = $"window.dispatchEvent(new CustomEvent('laserfiche-command', {{ detail: {message} }}));";
                await _webView.ExecuteScriptAsync(script);
                _logger.Debug("Command '{Command}' sent to web app", command);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to send command '{Command}'", command);
            }
        }

        public async Task<bool> PingAsync()
        {
            if (_webView == null) return false;

            try
            {
                await _webView.ExecuteScriptAsync("window.LaserficheBridge && window.LaserficheBridge.ping();");
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
