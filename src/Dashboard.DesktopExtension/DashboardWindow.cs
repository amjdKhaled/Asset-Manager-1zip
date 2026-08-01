using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace LFPortal.DesktopExtension
{
    /// <summary>
    /// Native popup window that hosts the Dashboard web application inside a
    /// Microsoft Edge WebView2 control. No browser chrome (address bar, tabs,
    /// toolbar) is shown — the Dashboard fills the entire window.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each instance uses an isolated temporary WebView2 user-data folder
    /// (a GUID-named subdirectory under <c>%TEMP%</c>) so that its session
    /// cookie is completely independent from other open Dashboard windows.
    /// This guarantees that clicking the Laserfiche toolbar button with
    /// repository <em>TestEmployee</em> always opens a window that is
    /// pre-scoped to <em>TestEmployee</em>, even if another window for
    /// <em>LFNewRepoWF</em> is already open.
    /// </para>
    /// <para>
    /// Construct the window with the fully-qualified portal URL (including the
    /// <c>?repository=</c> parameter when a repository is known) and call
    /// <c>Application.Run(window)</c>. The window disposes its WebView2 and
    /// cleans up automatically when closed.
    /// </para>
    /// </remarks>
    internal sealed class DashboardWindow : Form
    {
        // ------------------------------------------------------------------ //
        // Fields                                                              //
        // ------------------------------------------------------------------ //

        private readonly string   _url;
        private readonly WebView2 _webView;

        // ------------------------------------------------------------------ //
        // Construction                                                        //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Initialises the window with the Dashboard URL to navigate to.
        /// </summary>
        /// <param name="url">
        /// Fully-qualified URL, e.g.
        /// <c>https://localhost:5001/?repository=TestEmployee</c>.
        /// Must not be null or empty.
        /// </param>
        internal DashboardWindow(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentNullException(nameof(url));

            _url = url;

            // ---- Window chrome ----
            Text            = "Dashboard";
            ClientSize      = new Size(1400, 850);
            MinimumSize     = new Size(1000, 650);
            StartPosition   = FormStartPosition.CenterScreen;
            Icon            = LoadIcon();

            // ---- WebView2 ----
            _webView = new WebView2
            {
                Dock = DockStyle.Fill,
            };
            Controls.Add(_webView);

            Load += OnFormLoad;
        }

        // ------------------------------------------------------------------ //
        // WebView2 initialisation                                             //
        // ------------------------------------------------------------------ //

        private async void OnFormLoad(object sender, EventArgs e)
        {
            try
            {
                // Each window gets its own isolated user-data folder so its
                // session cookie is never shared with other Dashboard windows.
                var userDataFolder = Path.Combine(
                    Path.GetTempPath(),
                    "Dashboard_" + Guid.NewGuid().ToString("N"));

                ExtensionLogger.Log($"WebView2 user data: {userDataFolder}");

                var env = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder:          userDataFolder);

                await _webView.EnsureCoreWebView2Async(env);

                // ---- Suppress browser-UI chrome ----
                var settings = _webView.CoreWebView2.Settings;
                settings.AreDevToolsEnabled            = false;
                settings.AreDefaultContextMenusEnabled = true;   // Allow copy/paste
                settings.IsStatusBarEnabled            = false;

                // ---- Keep navigation inside the same window ----
                _webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;

                ExtensionLogger.Log($"Navigating to: {_url}");
                _webView.CoreWebView2.Navigate(_url);
            }
            catch (Exception ex)
            {
                ExtensionLogger.Log($"WebView2 init failed: {ex.Message}");

                MessageBox.Show(
                    "Failed to initialise the Dashboard window.\n\n" +
                    "Ensure the Microsoft Edge WebView2 Runtime is installed on this machine.\n\n" +
                    $"Error: {ex.Message}\n\n" +
                    "Download WebView2 Runtime from:\n" +
                    "https://developer.microsoft.com/microsoft-edge/webview2/",
                    "Dashboard — WebView2 Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                Close();
            }
        }

        // ------------------------------------------------------------------ //
        // Event handlers                                                      //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// When Dashboard opens a link with <c>target="_blank"</c> or similar,
        /// navigate within this same window rather than spawning a browser tab.
        /// </summary>
        private void OnNewWindowRequested(
            object sender,
            CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            try
            {
                _webView.CoreWebView2.Navigate(e.Uri);
            }
            catch (Exception ex)
            {
                ExtensionLogger.Log($"NewWindowRequested navigation failed: {ex.Message}");
            }
        }

        // ------------------------------------------------------------------ //
        // Helpers                                                             //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Attempts to load <c>Resources\Dashboard.ico</c> from the EXE directory.
        /// Returns <see langword="null"/> if the file is absent (the OS default is used).
        /// </summary>
        private static Icon LoadIcon()
        {
            try
            {
                var path = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Resources",
                    "Dashboard.ico");

                return File.Exists(path) ? new Icon(path) : null!;
            }
            catch
            {
                return null!;
            }
        }

        // ------------------------------------------------------------------ //
        // Disposal                                                            //
        // ------------------------------------------------------------------ //

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _webView?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
