using LaserficheAIExtension.Models;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using System;
using System.Threading.Tasks;

namespace LaserficheAIExtension.Communication
{
    /// <summary>
    /// Low-level bridge for injecting JavaScript and handling WebView2 messages.
    /// </summary>
    public class WebViewBridge : IDisposable
    {
        private readonly CoreWebView2 _webView;

        public event EventHandler<WebCommand> MessageReceived;

        public WebViewBridge(CoreWebView2 webView)
        {
            _webView = webView ?? throw new ArgumentNullException(nameof(webView));
            _webView.WebMessageReceived += OnWebMessageReceived;
        }

        private void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var json = e.TryGetWebMessageAsString();
                if (string.IsNullOrWhiteSpace(json)) return;

                var command = JsonConvert.DeserializeObject<WebCommand>(json);
                if (command != null)
                {
                    MessageReceived?.Invoke(this, command);
                }
            }
            catch { /* Ignore malformed messages */ }
        }

        public async Task ExecuteScriptAsync(string script)
        {
            if (_webView == null) return;
            await _webView.ExecuteScriptAsync(script);
        }

        public async Task PostMessageAsync(string json)
        {
            if (_webView == null) return;
            var script = $"window.postMessage({json}, '*');";
            await _webView.ExecuteScriptAsync(script);
        }

        public async Task DispatchEventAsync(string eventName, object detail)
        {
            var json = JsonConvert.SerializeObject(detail);
            var script = $@"
                (function() {{
                    var event = new CustomEvent('{eventName}', {{ detail: {json} }});
                    window.dispatchEvent(event);
                }})();
            ";
            await _webView.ExecuteScriptAsync(script);
        }

        public void Dispose()
        {
            if (_webView != null)
            {
                _webView.WebMessageReceived -= OnWebMessageReceived;
            }
        }
    }
}
