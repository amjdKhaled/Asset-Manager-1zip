using Laserfiche.ClientAutomation;
using LaserficheAIExtension.Infrastructure.DependencyInjection;
using LaserficheAIExtension.Models;
using LaserficheAIExtension.Popup;
using LaserficheAIExtension.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Drawing;

namespace LaserficheAIExtension.ClientAutomation
{
    /// <summary>
    /// Real Laserfiche Desktop Client integration using the official ClientAutomation SDK.
    /// Registers a custom toolbar button that opens the GovSearch AI Assistant popup.
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
        /// Initializes the integration by registering a toolbar button in Laserfiche
        /// using ToolbarManager and CustomButtonInfo from the ClientAutomation SDK.
        /// </summary>
        public void Initialize()
        {
            var clientManager = ClientManager.Instance;
            if (clientManager == null)
                throw new InvalidOperationException("Laserfiche Desktop Client is not running.");

            var mainWindow = clientManager.MainWindow;
            if (mainWindow == null)
                throw new InvalidOperationException("Cannot access Laserfiche main window.");

            var toolbarManager = mainWindow.ToolbarManager;
            if (toolbarManager == null)
                throw new InvalidOperationException("Cannot access Laserfiche toolbar manager.");

            var button = new CustomButtonInfo("GovSearchAI", "AI Assistant")
            {
                Tooltip = "Open GovSearch AI Assistant",
                Icon = CreateIcon()
            };

            button.Click += OnToolbarButtonClick;
            toolbarManager.AddButton(button);
        }

        private void OnToolbarButtonClick(object sender, EventArgs e)
        {
            ShowOrActivatePopup();
        }

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

        private static Image CreateIcon()
        {
            var bitmap = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.FromArgb(26, 86, 219));
                using (var brush = new SolidBrush(Color.White))
                {
                    g.DrawString("AI", new Font("Segoe UI", 6, FontStyle.Bold), brush, 1, 2);
                }
            }
            return bitmap;
        }

        public void Dispose()
        {
            _popup?.Close();
            _popup = null;
        }
    }
}
