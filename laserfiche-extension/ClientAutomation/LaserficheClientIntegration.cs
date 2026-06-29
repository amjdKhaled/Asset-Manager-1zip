using Laserfiche.ClientAutomation;
using LaserficheAIExtension.Infrastructure.DependencyInjection;
using LaserficheAIExtension.Models;
using LaserficheAIExtension.Popup;
using LaserficheAIExtension.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace LaserficheAIExtension.ClientAutomation
{
    /// <summary>
    /// Real Laserfiche Desktop Client integration using the official SDK 10.4 ClientAutomation API.
    /// Mirrors the CustomButtonManager sample from SDK 10.4 Samples/ClientAutomationSamples/CSharp.
    /// </summary>
    public class LaserficheClientIntegration : IDisposable
    {
        private readonly IServiceProvider _serviceProvider;
        private AIPopupWindow _popup;

        public LaserficheClientIntegration()
        {
            var services = new ServiceCollection();
            services.AddLaserficheAIExtension();
            _serviceProvider = services.BuildServiceProvider();
        }

        /// <summary>
        /// Registers the "AI Assistant" custom toolbar button in Laserfiche Desktop Client.
        /// Called once when the extension loads.
        /// </summary>
        public void Initialize()
        {
            // Obtain the running Laserfiche Desktop Client instance via ClientManager singleton.
            ClientManager clientManager = ClientManager.Instance;
            if (clientManager == null)
                throw new InvalidOperationException("Laserfiche Desktop Client is not running.");

            // Access the main application window and its toolbar manager.
            MainWindow mainWindow = clientManager.MainWindow;
            if (mainWindow == null)
                throw new InvalidOperationException("Cannot access Laserfiche main window.");

            ToolbarManager toolbarManager = mainWindow.ToolbarManager;
            if (toolbarManager == null)
                throw new InvalidOperationException("Cannot access Laserfiche toolbar manager.");

            // Create a custom button using the official CustomButtonInfo class (SDK sample pattern).
            CustomButtonInfo button = new CustomButtonInfo("GovSearchAI", "AI Assistant")
            {
                Tooltip = "Open GovSearch AI Assistant"
            };

            // Wire the click event to open the WPF popup (embedded WebView2, no external browser).
            button.Click += OnToolbarButtonClick;

            // Register the button with the Laserfiche toolbar.
            toolbarManager.AddButton(button);
        }

        private void OnToolbarButtonClick(object sender, EventArgs e)
        {
            ShowOrActivatePopup();
        }

        /// <summary>
        /// Creates the AI popup if not already open, or brings it to the foreground.
        /// </summary>
        private void ShowOrActivatePopup()
        {
            if (_popup == null || !_popup.IsLoaded)
            {
                var communication = _serviceProvider.GetRequiredService<IWebAppCommunicationService>();
                var monitor = _serviceProvider.GetRequiredService<IConnectionMonitorService>();
                var tracker = _serviceProvider.GetRequiredService<IDocumentContextTracker>();
                var handler = _serviceProvider.GetRequiredService<ICommandHandlerService>();
                var settings = _serviceProvider.GetRequiredService<ExtensionSettings>();

                _popup = new AIPopupWindow(communication, monitor, tracker, handler, settings);
                _popup.Closed += (s, args) => _popup = null;
                _popup.Show();
            }
            else
            {
                _popup.Activate();
                if (_popup.WindowState == System.Windows.WindowState.Minimized)
                {
                    _popup.WindowState = System.Windows.WindowState.Normal;
                }
            }
        }

        public void Dispose()
        {
            _popup?.Close();
            _popup = null;
        }
    }
}
