using Laserfiche.ClientAutomation;
using LaserficheAIExtension.Infrastructure.DependencyInjection;
using LaserficheAIExtension.Models;
using LaserficheAIExtension.Popup;
using LaserficheAIExtension.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Reflection;
using System.Windows;

namespace LaserficheAIExtension
{
    /// <summary>
    /// WPF application entry point that mirrors the SDK 10.4 CustomButtonManager sample.
    /// Dual-mode:
    ///   * /register  — adds the "AI Assistant" toolbar button to Laserfiche Desktop Client
    ///   * ai         — shows the floating WPF popup with embedded WebView2
    /// </summary>
    public partial class App : Application
    {
        private AIPopupWindow _popup;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            string[] args = e.Args;

            // Registration mode: install the toolbar button into Laserfiche
            if (args.Contains("/register") || args.Contains("-register"))
            {
                RegisterToolbarButton();
                Shutdown();
                return;
            }

            // Popup mode (default): show the AI assistant floating window
            ShowAIPopup(args);
        }

        /// <summary>
        /// Registers the "AI Assistant" custom toolbar button in Laserfiche Desktop Client.
        /// Mirrors the CustomButtonManager SDK sample exactly.
        /// </summary>
        private static void RegisterToolbarButton()
        {
            // Obtain the running Laserfiche Desktop Client instance
            ClientManager clientManager = ClientManager.Instance;
            if (clientManager == null)
            {
                MessageBox.Show("Laserfiche Desktop Client is not running.", "GovSearch AI",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ClientInstance clientInstance = clientManager.ClientInstance;
            if (clientInstance == null)
            {
                MessageBox.Show("Cannot access Laserfiche client instance.", "GovSearch AI",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            MainWindow mainWindow = clientInstance.MainWindow;
            if (mainWindow == null)
            {
                MessageBox.Show("Cannot access Laserfiche main window.", "GovSearch AI",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ToolbarManager toolbarManager = mainWindow.ToolbarManager;
            if (toolbarManager == null)
            {
                MessageBox.Show("Cannot access Laserfiche toolbar manager.", "GovSearch AI",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string exePath = Assembly.GetExecutingAssembly().Location;
            string command = string.Format("\"{0}\" ai", exePath);

            CustomButtonInfo button = new CustomButtonInfo("GovSearchAI", "AI Assistant")
            {
                Tooltip = "Open GovSearch AI Assistant",
                Command = command
            };

            toolbarManager.AddButton(button);

            MessageBox.Show("AI Assistant toolbar button registered successfully.", "GovSearch AI",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Shows the floating WPF popup with embedded WebView2.
        /// Parses document context from command-line arguments passed by Laserfiche.
        /// </summary>
        private void ShowAIPopup(string[] args)
        {
            // Build DI container exactly like the original extension
            var services = new ServiceCollection();
            services.AddLaserficheAIExtension();
            var serviceProvider = services.BuildServiceProvider();

            var communication = serviceProvider.GetRequiredService<IWebAppCommunicationService>();
            var monitor = serviceProvider.GetRequiredService<IConnectionMonitorService>();
            var tracker = serviceProvider.GetRequiredService<IDocumentContextTracker>();
            var handler = serviceProvider.GetRequiredService<ICommandHandlerService>();
            var settings = serviceProvider.GetRequiredService<ExtensionSettings>();

            _popup = new AIPopupWindow(communication, monitor, tracker, handler, settings);
            _popup.Closed += (s, e) =>
            {
                _popup = null;
                Shutdown();
            };

            _popup.Show();

            // Parse entry ID from Laserfiche command-line arguments
            // Expected format from ClientAutomation: ai --entryId 12345 --repo "MyRepo"
            ParseLaserficheArguments(args, tracker);
        }

        private static void ParseLaserficheArguments(string[] args, IDocumentContextTracker tracker)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if ((args[i] == "--entryId" || args[i] == "-entryId") && i + 1 < args.Length)
                {
                    if (int.TryParse(args[i + 1], out int entryId))
                    {
                        tracker.UpdateSelectionAsync(entryId).ConfigureAwait(false);
                    }
                }
            }
        }
    }
}
