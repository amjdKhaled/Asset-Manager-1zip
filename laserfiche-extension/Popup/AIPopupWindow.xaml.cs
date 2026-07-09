using LaserficheAIExtension.Infrastructure.Helpers;
using LaserficheAIExtension.Models;
using LaserficheAIExtension.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LaserficheAIExtension.Popup
{
    /// <summary>
    /// Production-quality floating popup window hosting WebView2.
    /// Hardened against freezes, deadlocks, memory leaks, and crashes.
    /// </summary>
    public partial class AIPopupWindow : Window
    {
        private readonly IWebAppCommunicationService _communicationService;
        private readonly IConnectionMonitorService _connectionMonitor;
        private readonly IDocumentContextTracker _documentTracker;
        private readonly ICommandHandlerService _commandHandler;
        private readonly Models.ExtensionSettings _settings;
        private bool _isClosing;
        private bool _isDisposed;

        public AIPopupWindow(
            IWebAppCommunicationService communicationService,
            IConnectionMonitorService connectionMonitor,
            IDocumentContextTracker documentTracker,
            ICommandHandlerService commandHandler,
            Models.ExtensionSettings settings)
        {
            Log("Constructor started");
            try
            {
                InitializeComponent();
            }
            catch (Exception ex)
            {
                Log("InitializeComponent FAILED: " + ex);
                throw;
            }

            _communicationService = communicationService ?? throw new ArgumentNullException(nameof(communicationService));
            _connectionMonitor = connectionMonitor ?? throw new ArgumentNullException(nameof(connectionMonitor));
            _documentTracker = documentTracker ?? throw new ArgumentNullException(nameof(documentTracker));
            _commandHandler = commandHandler ?? throw new ArgumentNullException(nameof(commandHandler));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            Loaded += OnWindowLoaded;
            Closing += OnWindowClosing;
            StateChanged += OnWindowStateChanged;
            LocationChanged += OnWindowLocationChanged;
            SizeChanged += OnWindowSizeChanged;
            Log("Constructor completed");
        }

        private static void Log(string message)
        {
            try
            {
                string path = Path.Combine(Path.GetTempPath(), "GovSearchAI_Extension.log");
                string line = string.Format("[{0:yyyy-MM-dd HH:mm:ss.fff}] [Popup] {1}", DateTime.Now, message);
                File.AppendAllText(path, line + "\r\n");
            }
            catch { }
        }

        private async void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            Log("OnWindowLoaded started");
            try
            {
                // Apply saved position
                WindowPositionHelper.ApplyToWindow(this,
                    _settings.WindowLeft, _settings.WindowTop,
                    _settings.WindowWidth, _settings.WindowHeight,
                    _settings.IsMaximized);

                // Initialize WebView2 asynchronously — never blocks UI thread
                await InitializeWebViewAsync();

                // Subscribe to events
                _connectionMonitor.ConnectionStatusChanged += OnConnectionStatusChanged;
                _documentTracker.DocumentChanged += OnDocumentChanged;
                _communicationService.CommandReceived += OnCommandReceived;

                // Start monitoring connection
                await _connectionMonitor.StartMonitoringAsync(_settings.ServerUrl);
                Log("OnWindowLoaded completed");
            }
            catch (Exception ex)
            {
                Log("OnWindowLoaded FAILED: " + ex);
                UpdateStatusOverlay(
                    "Popup initialization failed",
                    ex.Message,
                    "See log for full details.");
            }
        }

        private async Task InitializeWebViewAsync()
        {
            Log("InitializeWebViewAsync started");
            try
            {
                var env = await CoreWebView2Environment.CreateAsync(null, GetWebViewDataPath());
                await WebView.EnsureCoreWebView2Async(env);
                Log("WebView2 core initialized");

                // Configure WebView2
                WebView.CoreWebView2.Settings.IsScriptEnabled = true;
                WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                WebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
                WebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                WebView.CoreWebView2.Settings.IsZoomControlEnabled = true;
                WebView.CoreWebView2.Settings.UserAgent =
                    "LaserficheAIExtension/1.0 (Windows; WebView2) " + WebView.CoreWebView2.Settings.UserAgent;

                // Initialize communication bridge
                _communicationService.Initialize(WebView.CoreWebView2);
                _communicationService.ServerUrl = _settings.ServerUrl;
                Log("Communication bridge initialized");

                // Subscribe to events
                WebView.CoreWebView2.NavigationCompleted += OnWebViewNavigationCompleted;
                WebView.CoreWebView2.SourceChanged += OnWebViewSourceChanged;

                // Start spinner animation (null-safe)
                if (Resources.Contains("SpinAnimation"))
                {
                    var spinStoryboard = TryFindResource("SpinAnimation") as Storyboard;
                    spinStoryboard?.Begin();
                }

                // Navigate to server
                UpdateStatusOverlay("Connecting to GovSearch AI...", _settings.ServerUrl, "Initializing WebView2...");
                WebView.Source = new Uri(_settings.ServerUrl);
                Log("InitializeWebViewAsync completed");
            }
            catch (Exception ex)
            {
                Log("InitializeWebViewAsync FAILED: " + ex);
                UpdateStatusOverlay(
                    "Failed to initialize WebView2",
                    ex.Message,
                    "Check Edge WebView2 Runtime installation.");
            }
        }

        private void OnWebViewNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            Log("NavigationCompleted: IsSuccess=" + e.IsSuccess);
            if (e.IsSuccess)
            {
                StatusOverlay.Visibility = Visibility.Collapsed;
            }
            else
            {
                UpdateStatusOverlay("Waiting for Local AI...", _settings.ServerUrl, $"Error: {e.WebErrorStatus}");
            }
        }

        private void OnWebViewSourceChanged(object sender, CoreWebView2SourceChangedEventArgs e)
        {
            // Navigation history tracking placeholder
        }

        private void OnConnectionStatusChanged(object sender, bool isOnline)
        {
            // Use BeginInvoke (non-blocking) instead of Invoke to avoid deadlocks
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_isDisposed) return;

                if (isOnline)
                {
                    ConnectionIndicator.Fill = new SolidColorBrush(Color.FromRgb(34, 197, 94));
                    ConnectionStatusText.Text = "Connected";
                    ConnectionStatusText.Foreground = new SolidColorBrush(Color.FromRgb(34, 197, 94));

                    if (StatusOverlay.Visibility == Visibility.Visible && WebView?.Source != null)
                    {
                        try { WebView.Reload(); }
                        catch { /* WebView may be disposed */ }
                    }
                }
                else
                {
                    ConnectionIndicator.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                    ConnectionStatusText.Text = "Disconnected";
                    ConnectionStatusText.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                    UpdateStatusOverlay("Waiting for Local AI...", _settings.ServerUrl, "Retrying connection...");
                }
            }), DispatcherPriority.Background);
        }

        private async void OnDocumentChanged(object sender, DocumentContext context)
        {
            if (context == null) return;
            Log("DocumentChanged: " + context.DocumentName);

            await Dispatcher.InvokeAsync(async () =>
            {
                if (_isDisposed) return;
                DocumentStatusText.Text = $"Selected: {context.DocumentName}";

                if (_settings.SendSelectionOnChange && _communicationService.IsConnected)
                {
                    try { await _communicationService.SendDocumentContextAsync(context); }
                    catch (Exception ex) { Log("SendDocumentContextAsync failed: " + ex); }
                }
            });
        }

        private async void OnCommandReceived(object sender, WebCommand command)
        {
            try { await _commandHandler.HandleCommandAsync(command); }
            catch (Exception ex) { Log("HandleCommandAsync failed: " + ex); }
        }

        private void UpdateStatusOverlay(string mainText, string subText, string retryText)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_isDisposed) return;
                StatusText.Text = mainText;
                StatusSubtext.Text = subText;
                RetryText.Text = retryText;
                StatusOverlay.Visibility = Visibility.Visible;
            }), DispatcherPriority.Background);
        }

        private void OnWindowClosing(object sender, CancelEventArgs e)
        {
            if (_isClosing) return;
            _isClosing = true;
            Log("OnWindowClosing started");

            try
            {
                // Save position
                double left = 0, top = 0, width = 0, height = 0;
                bool isMaximized = false;
                WindowPositionHelper.CaptureFromWindow(this,
                    out left, out top,
                    out width, out height,
                    out isMaximized);
                _settings.WindowLeft = left;
                _settings.WindowTop = top;
                _settings.WindowWidth = width;
                _settings.WindowHeight = height;
                _settings.IsMaximized = isMaximized;
                _settings.Save();
            }
            catch (Exception ex) { Log("Save position failed: " + ex); }

            // Unsubscribe ALL events to prevent memory leaks
            try
            {
                Loaded -= OnWindowLoaded;
                Closing -= OnWindowClosing;
                StateChanged -= OnWindowStateChanged;
                LocationChanged -= OnWindowLocationChanged;
                SizeChanged -= OnWindowSizeChanged;
            }
            catch { }

            try
            {
                _connectionMonitor.ConnectionStatusChanged -= OnConnectionStatusChanged;
                _documentTracker.DocumentChanged -= OnDocumentChanged;
                _communicationService.CommandReceived -= OnCommandReceived;
            }
            catch { }

            try
            {
                if (WebView?.CoreWebView2 != null)
                {
                    WebView.CoreWebView2.NavigationCompleted -= OnWebViewNavigationCompleted;
                    WebView.CoreWebView2.SourceChanged -= OnWebViewSourceChanged;
                }
            }
            catch { }

            // Stop background services
            try { _connectionMonitor?.StopMonitoring(); }
            catch (Exception ex) { Log("StopMonitoring failed: " + ex); }

            // Dispose WebView2 asynchronously to avoid UI thread deadlock
            var webViewToDispose = WebView;
            if (webViewToDispose != null)
            {
                WebView = null; // Clear reference immediately
                Task.Run(() =>
                {
                    try
                    {
                        webViewToDispose.Dispose();
                        Log("WebView2 disposed on background thread");
                    }
                    catch (Exception ex) { Log("WebView2 dispose failed: " + ex); }
                });
            }

            _isDisposed = true;
            Log("OnWindowClosing completed");
        }

        private void OnWindowStateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                _settings.IsMinimized = true;
            }
            else if (WindowState == WindowState.Normal)
            {
                _settings.IsMinimized = false;
                _settings.IsMaximized = false;
            }
            else if (WindowState == WindowState.Maximized)
            {
                _settings.IsMaximized = true;
            }
        }

        private void OnWindowLocationChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Normal)
            {
                _settings.WindowLeft = Left;
                _settings.WindowTop = Top;
            }
        }

        private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (WindowState == WindowState.Normal)
            {
                _settings.WindowWidth = Width;
                _settings.WindowHeight = Height;
            }
        }

        // Title bar drag
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
            }
            else if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private static string GetWebViewDataPath()
        {
            return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LaserficheAIExtension",
                "WebViewData");
        }
    }
}
