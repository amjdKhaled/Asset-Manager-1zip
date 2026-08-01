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
    /// <para>
    /// <strong>Architecture note:</strong> this project targets x64 (matching the
    /// GovSearch AI Laserfiche extension which is the only other extension on this
    /// machine that uses WebView2 and is confirmed working). The x64 target ensures
    /// MSBuild promotes <c>runtimes\win-x64\native\WebView2Loader.dll</c> correctly.
    /// Using AnyCPU causes <c>0x8007000B / BadImageFormatException</c> because the
    /// NuGet build targets cannot determine which architecture loader to promote.
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
        /// Fully-qualified, non-empty URL, e.g.
        /// <c>https://localhost:5001/?repository=TestEmployee</c>.
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
            Icon            = LoadWindowIcon();

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
            string userDataFolder = Path.Combine(
                Path.GetTempPath(),
                "Dashboard_" + Guid.NewGuid().ToString("N"));

            ExtensionLogger.Log($"WebView2 user data: {userDataFolder}");

            // Probe runtime version before calling CreateAsync so we can emit a
            // clear "runtime missing" message rather than a generic failure.
            string runtimeVersion = "(unknown)";
            try
            {
                runtimeVersion = CoreWebView2Environment.GetAvailableBrowserVersionString();
                ExtensionLogger.Log($"WebView2 Runtime: {runtimeVersion}");
            }
            catch (Exception probeEx)
            {
                ExtensionLogger.Log($"WebView2 Runtime probe failed: {probeEx.Message}");
                // Continue — CreateAsync will also fail with a clearer message below.
            }

            try
            {
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
                ExtensionLogger.Log(
                    $"WebView2 init failed — {ex.GetType().Name} HRESULT=0x{(uint)ex.HResult:X8}: {ex.Message}");

                // Distinguish the error category so the user sees a meaningful message.
                string title, detail;

                if (ex is BadImageFormatException
                    || (uint)ex.HResult == 0x8007000B)   // ERROR_BAD_FORMAT
                {
                    // Architecture mismatch: the extension bitness does not match the
                    // WebView2Loader.dll that was deployed to the output folder.
                    // Resolution: rebuild with the correct explicit PlatformTarget.
                    title = "Dashboard — WebView2 Architecture Mismatch";
                    detail =
                        "The Dashboard extension or WebView2 native loader does not match " +
                        "the required Windows architecture.\n\n" +
                        $"Process 64-bit: {Environment.Is64BitProcess}  " +
                        $"(pointer size = {IntPtr.Size * 8} bit)\n\n" +
                        $"See the diagnostic log for loader paths:\n{GetLogPath()}";
                }
                else if ((uint)ex.HResult == 0x80070002   // ERROR_FILE_NOT_FOUND
                      || (uint)ex.HResult == 0x80070003   // ERROR_PATH_NOT_FOUND
                      || runtimeVersion == "(unknown)")
                {
                    title = "Dashboard — WebView2 Runtime Not Found";
                    detail =
                        "The Microsoft Edge WebView2 Runtime is not installed.\n\n" +
                        "Download the Evergreen Bootstrapper from:\n" +
                        "https://developer.microsoft.com/microsoft-edge/webview2/\n\n" +
                        $"Error: {ex.Message}";
                }
                else
                {
                    title = "Dashboard — WebView2 Initialisation Failed";
                    detail =
                        $"Failed to start the Dashboard window.\n\n" +
                        $"Runtime version detected: {runtimeVersion}\n\n" +
                        $"Error (0x{(uint)ex.HResult:X8}): {ex.Message}\n\n" +
                        $"See: {GetLogPath()}";
                }

                MessageBox.Show(detail, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
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
        /// Attempts to load <c>Resources\Dashboard.ico</c> from the EXE directory
        /// for the window title-bar icon. Returns <see langword="null"/> if absent.
        /// </summary>
        private static Icon? LoadWindowIcon()
        {
            try
            {
                var path = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Resources",
                    "Dashboard.ico");

                return File.Exists(path) ? new Icon(path) : null;
            }
            catch
            {
                return null;
            }
        }

        private static string GetLogPath() =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Dashboard", "logs", "extension.log");

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
