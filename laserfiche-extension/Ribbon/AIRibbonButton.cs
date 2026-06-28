using LaserficheAIExtension.Models;
using LaserficheAIExtension.Popup;
using LaserficheAIExtension.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Windows.Forms;

namespace LaserficheAIExtension.Ribbon
{
    /// <summary>
    /// Ribbon button that opens the AI Assistant popup.
    /// Implements Laserfiche's IRibbonButton interface (or acts as a standalone button).
    /// </summary>
    [Export(typeof(IRibbonButton))]
    public class AIRibbonButton : IRibbonButton
    {
        private readonly IServiceProvider _serviceProvider;
        private AIPopupWindow _popup;

        public AIRibbonButton()
        {
            // Build DI container
            var services = new ServiceCollection();
            services.AddLaserficheAIExtension();
            _serviceProvider = services.BuildServiceProvider();
        }

        public string Id => "GovSearchAIAssistant";
        public string Label => "AI Assistant";
        public string Tooltip => "Open GovSearch AI Assistant";
        public Image Icon => CreateDefaultIcon();
        public bool IsEnabled => true;

        public void Execute()
        {
            if (_popup == null || !_popup.IsLoaded)
            {
                var communication = _serviceProvider.GetRequiredService<IWebAppCommunicationService>();
                var monitor = _serviceProvider.GetRequiredService<IConnectionMonitorService>();
                var tracker = _serviceProvider.GetRequiredService<IDocumentContextTracker>();
                var handler = _serviceProvider.GetRequiredService<ICommandHandlerService>();
                var settings = _serviceProvider.GetRequiredService<ExtensionSettings>();

                _popup = new AIPopupWindow(communication, monitor, tracker, handler, settings);
                _popup.Closed += (s, e) => _popup = null;
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

        public void OnSelectionChanged(int entryId)
        {
            var tracker = _serviceProvider.GetRequiredService<IDocumentContextTracker>();
            tracker.UpdateSelectionAsync(entryId).ConfigureAwait(false);
        }

        private static Image CreateDefaultIcon()
        {
            var bitmap = new System.Drawing.Bitmap(32, 32);
            using (var g = System.Drawing.Graphics.FromImage(bitmap))
            {
                g.Clear(System.Drawing.Color.FromArgb(26, 86, 219));
                using (var brush = new System.Drawing.SolidBrush(System.Drawing.Color.White))
                {
                    g.DrawString("AI", new System.Drawing.Font("Segoe UI", 10, System.Drawing.FontStyle.Bold), brush, 4, 6);
                }
            }
            return bitmap;
        }
    }

    /// <summary>
    /// Minimal interface for Laserfiche ribbon integration.
    /// </summary>
    public interface IRibbonButton
    {
        string Id { get; }
        string Label { get; }
        string Tooltip { get; }
        Image Icon { get; }
        bool IsEnabled { get; }
        void Execute();
        void OnSelectionChanged(int entryId);
    }
}
