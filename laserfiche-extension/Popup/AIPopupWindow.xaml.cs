using LaserficheAIExtension.Infrastructure.Helpers;
using LaserficheAIExtension.Models;
using LaserficheAIExtension.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LaserficheAIExtension.Popup
{
    /// <summary>
    /// Modern floating popup window hosting WebView2 with GovSearch AI.
    /// </summary>
    public partial class AIPopupWindow : Window
    {
        private readonly IWebAppCommunicationService _communicationService;
        private readonly IConnectionMonitorService _connectionMonitor;
        private readonly IDocumentContextTracker _documentTracker;
        private readonly ICommandHandlerService _commandHandler;
        private readonly Models.ExtensionSettings _settings;
        private bool _isClosing;

        public AIPopupWindow(
            IWebAppCommunicationService communicationService,
            IConnectionMonitorService connectionMonitor,
            IDocumentContextTracker documentTracker,
            ICommandHandlerService commandHandler,
            Models.ExtensionSettings settings)
        {
            // --- Diagnostic logging: prove which EXE is running ---
            var asm = Assembly.GetExecutingAssembly();
            string diag = string.Format(
                "AIPopupWindow ctor running:\r\n" +
                "  Location: {0}\r\n" +
                "  FullName: {1}\r\n" +
                "  Version:  {2}\r\n",
                asm.Location,
                asm.FullName,
                asm.GetName().Version);
            File.AppendAllText(GetDiagnosticLogPath(), diag + "\r\n");

            try
            {
                InitializeComponent();
            }
            catch (Exception ex)
            {
                string fullDetails = FormatExceptionDetails(ex);
                string msg = "Failed to initialize AI popup window.\r\n\r\n" + fullDetails;
                File.AppendAllText(GetDiagnosticLogPath(), "InitializeComponent FAILED:\r\n" + msg + "\r\n\r\n");
                System.Diagnostics.Debug.WriteLine("AIPopupWindow.InitializeComponent failed:\r\n" + fullDetails);
                MessageBox.Show(
                    msg,
                    "GovSearch AI — Popup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
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
        }

        private static string GetDiagnosticLogPath()
        {
            string path = Path.Combine(Path.GetTempPath(), "GovSearchAI_Diagnostic.log");
            return path;
        }

        private static string FormatExceptionDetails(Exception ex)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Exception: " + ex.GetType().FullName);
            sb.AppendLine("Message: " + ex.Message);
            if (ex.InnerException != null)
            {
                sb.AppendLine();
                sb.AppendLine("Inner Exception:");
                sb.AppendLine("  Type:    " + ex.InnerException.GetType().FullName);
                sb.AppendLine("  Message: " + ex.InnerException.Message);
                if (ex.InnerException.InnerException != null)
                {
                    sb.AppendLine("  Inner:   " + ex.InnerException.InnerException.Message);
                }
            }
            sb.AppendLine();
            sb.AppendLine("StackTrace:");
            sb.AppendLine(ex.StackTrace);
            sb.AppendLine();
            sb.AppendLine("Source: " + (ex.Source ?? "(null)"));
            sb.AppendLine("TargetSite: " + (ex.TargetSite?.ToString() ?? "(null)"));
            return sb.ToString();
        }

        private async void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Apply saved position
                WindowPositionHelper.ApplyToWindow(this,
                    _settings.WindowLeft, _settings.WindowTop,
                    _settings.WindowWidth, _settings.WindowHeight,
                    _settings.IsMaximized);

                // Initialize WebView2
                await InitializeWebViewAsync();

                // Start monitoring connection
                _connectionMonitor.ConnectionStatusChanged += OnConnectionStatusChanged;
                await _connectionMonitor.StartMonitoringAsync(_settings.ServerUrl);

                // Track document selection changes
                _documentTracker.DocumentChanged += OnDocumentChanged;

                // Handle commands from web app
                _communicationService.CommandReceived += OnCommandReceived;
            }
            catch (Exception ex)
            {
                string fullDetails = FormatExceptionDetails(ex);
                System.Diagnostics.Debug.WriteLine("OnWindowLoaded failed:\r\n" + fullDetails);
                UpdateStatusOverlay(
                    "Popup initialization failed",
                    ex.Message,
                    "See Debug output for full details.");
            }
        }

        private async Task InitializeWebViewAsync()
        {
            try
            {
                var env = await CoreWebView2Environment.CreateAsync(null, GetWebViewDataPath());
                await WebView.EnsureCoreWebView2Async(env);

                // Configure WebView2
                WebView.CoreWebView2.Settings.IsScriptEnabled = true;
                WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                WebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
                WebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                WebView.CoreWebView2.Settings.IsZoomControlEnabled = true;

                // Set user agent to identify as Laserfiche extension
                WebView.CoreWebView2.Settings.UserAgent =
                    "LaserficheAIExtension/1.0 (Windows; WebView2) " + WebView.CoreWebView2.Settings.UserAgent;

                // Initialize communication bridge
                _communicationService.Initialize(WebView.CoreWebView2);
                _communicationService.ServerUrl = _settings.ServerUrl;

                // Subscribe to events
                WebView.CoreWebView2.NavigationCompleted += OnWebViewNavigationCompleted;
                WebView.CoreWebView2.SourceChanged += OnWebViewSourceChanged;

                // Start spinner animation
                var spinStoryboard = (Storyboard)FindResource("SpinAnimation");
                spinStoryboard.Begin();

                // Navigate to server
                UpdateStatusOverlay("Connecting to GovSearch AI...", _settings.ServerUrl, "Initializing WebView2...");
                WebView.Source = new Uri(_settings.ServerUrl);
            }
            catch (Exception ex)
            {
                string fullDetails = FormatExceptionDetails(ex);
                System.Diagnostics.Debug.WriteLine("InitializeWebViewAsync failed:\r\n" + fullDetails);
                UpdateStatusOverlay(
                    "Failed to initialize WebView2",
                    ex.Message,
                    "Check Edge WebView2 Runtime installation. Full details written to Debug output.");
            }
        }

        private void OnWebViewNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
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
            // Could track navigation history here if needed
        }

        private void OnConnectionStatusChanged(object sender, bool isOnline)
        {
            Dispatcher.Invoke(() =>
            {
                if (isOnline)
                {
                    ConnectionIndicator.Fill = new SolidColorBrush(Color.FromRgb(34, 197, 94)); // green
                    ConnectionStatusText.Text = "Connected";
                    ConnectionStatusText.Foreground = new SolidColorBrush(Color.FromRgb(34, 197, 94));

                    if (StatusOverlay.Visibility == Visibility.Visible && WebView.Source != null)
                    {
                        // Server came back online - reload
                        WebView.Reload();
                    }
                }
                else
                {
                    ConnectionIndicator.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // red
                    ConnectionStatusText.Text = "Disconnected";
                    ConnectionStatusText.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                    UpdateStatusOverlay("Waiting for Local AI...", _settings.ServerUrl, "Retrying connection...");
                }
            });
        }

        private async void OnDocumentChanged(object sender, DocumentContext context)
        {
            if (context == null) return;

            await Dispatcher.InvokeAsync(async () =>
            {
                DocumentStatusText.Text = $"Selected: {context.DocumentName}";

                if (_settings.SendSelectionOnChange && _communicationService.IsConnected)
                {
                    await _communicationService.SendDocumentContextAsync(context);
                }
            });
        }

        private async void OnCommandReceived(object sender, WebCommand command)
        {
            await _commandHandler.HandleCommandAsync(command);
        }

        private void UpdateStatusOverlay(string mainText, string subText, string retryText)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = mainText;
                StatusSubtext.Text = subText;
                RetryText.Text = retryText;
                StatusOverlay.Visibility = Visibility.Visible;
            });
        }

        private void OnWindowClosing(object sender, CancelEventArgs e)
        {
            if (_isClosing) return;
            _isClosing = true;

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

            // Cleanup
            _connectionMonitor?.StopMonitoring();
            WebView?.Dispose();
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
