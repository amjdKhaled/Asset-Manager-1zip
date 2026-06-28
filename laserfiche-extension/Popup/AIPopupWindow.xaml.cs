using LaserficheAIExtension.Infrastructure.Helpers;
using LaserficheAIExtension.Models;
using LaserficheAIExtension.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.ComponentModel;
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
            InitializeComponent();

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

        private async void OnWindowLoaded(object sender, RoutedEventArgs e)
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
                UpdateStatusOverlay("Failed to initialize WebView2", ex.Message, "Check Edge WebView2 Runtime installation");
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
            WindowPositionHelper.CaptureFromWindow(this,
                out _settings.WindowLeft, out _settings.WindowTop,
                out _settings.WindowWidth, out _settings.WindowHeight,
                out _settings.IsMaximized);
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
