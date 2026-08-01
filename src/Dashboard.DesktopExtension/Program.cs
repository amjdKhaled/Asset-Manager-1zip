using System;
using System.IO;
using System.Windows.Forms;

namespace LFPortal.DesktopExtension
{
    /// <summary>
    /// Entry point for the Dashboard Desktop Extension.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This executable serves two distinct roles depending on the command-line arguments:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <term>Setup / Remove (run once by the installer or administrator)</term>
    ///     <description>
    ///       Uses the Laserfiche <c>ClientAutomation</c> SDK to register (or remove)
    ///       a toolbar button in the Laserfiche Desktop Client.
    ///       <code>Dashboard.DesktopExtension.exe --setup [--silent]</code>
    ///       <code>Dashboard.DesktopExtension.exe --remove [--silent]</code>
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <term>Button-click handler (invoked by the Laserfiche Desktop Client)</term>
    ///     <description>
    ///       Reads the portal URL from the extension config, constructs a URL with the
    ///       active repository appended as <c>?repository=</c>, and opens Dashboard in a
    ///       native WebView2 popup window. No external browser is launched.
    ///       <code>
    ///       Dashboard.DesktopExtension.exe -buttonclick
    ///           -connguid "%(ConnectionGUID)"
    ///           -hwnd "%(hwnd)"
    ///           -pid "%(PID)"
    ///           -databasename "%(DatabaseName)"
    ///       </code>
    ///       The <c>%(DatabaseName)</c> token is substituted by the Laserfiche Desktop
    ///       Client at click time with the name of the currently active repository.
    ///     </description>
    ///   </item>
    /// </list>
    /// <para>
    /// Running with no arguments is equivalent to <c>--setup</c>.
    /// </para>
    /// </remarks>
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var argList = new System.Collections.Generic.HashSet<string>(
                args, StringComparer.OrdinalIgnoreCase);

            bool silent = argList.Contains("--silent") || argList.Contains("-silent");

            // ----------------------------------------------------------------
            // Button-click handler
            // Invoked by Laserfiche Desktop Client when the toolbar button is
            // clicked. Laserfiche expands %(DatabaseName) to the name of the
            // repository currently active in the Desktop Client window.
            // We append it as ?repository=<name> so the portal's session
            // middleware can activate that repository without any configuration.
            //
            // Token reference (from Laserfiche SDK 10.4 CustomButtonManager docs):
            //   %(ConnectionGUID) — GUID of the active repository connection
            //   %(hwnd)           — HWND of the active Desktop Client window
            //   %(PID)            — process ID of the Desktop Client process
            //   %(DatabaseName)   — name of the currently open repository  ← used here
            // ----------------------------------------------------------------
            if (argList.Contains("-buttonclick"))
            {
                var databaseName = ParseNamedArg(args, "-databasename");

                // Log runtime diagnostics before attempting WebView2 initialization.
                // This information is critical for diagnosing architecture mismatches
                // (0x8007000B / BadImageFormatException) and missing runtime issues.
                ExtensionLogger.LogSection("DASHBOARD RUNTIME");
                ExtensionLogger.Log($"OS 64-bit:     {Environment.Is64BitOperatingSystem}");
                ExtensionLogger.Log($"Process 64-bit:{Environment.Is64BitProcess}");
                ExtensionLogger.Log($"Pointer size:  {IntPtr.Size} bytes ({IntPtr.Size * 8}-bit)");
                ExtensionLogger.Log($"Executable:    {Application.ExecutablePath}");
                ExtensionLogger.Log($"Raw args ({args.Length}): {string.Join(" ", args)}");
                ExtensionLogger.Log($"Repository:    {(string.IsNullOrWhiteSpace(databaseName) ? "(none — will use configured default)" : databaseName)}");

                // Log WebView2Loader.dll locations so architecture problems can be
                // diagnosed from the log without re-running the extension.
                LogWebView2LoaderPaths();

                OpenPortalInWindow(silent, databaseName);
                return;
            }

            // ----------------------------------------------------------------
            // Remove — unregisters the toolbar button.
            // ----------------------------------------------------------------
            if (argList.Contains("--remove"))
            {
                ToolbarRegistrar.Remove(silent);
                return;
            }

            // ----------------------------------------------------------------
            // Setup (default) — registers the toolbar button.
            // ----------------------------------------------------------------
            var config = ExtensionConfig.Load();
            ToolbarRegistrar.Register(config, silent);
        }

        // ------------------------------------------------------------------ //
        // Button-click: open Dashboard in a native WebView2 popup window     //
        // ------------------------------------------------------------------ //

        private static void OpenPortalInWindow(bool silent, string databaseName)
        {
            var config = ExtensionConfig.Load();

            // Validate PortalUrl before doing anything else.
            string? rawUrl = config.PortalUrl?.Trim();
            if (string.IsNullOrEmpty(rawUrl))
            {
                ExtensionLogger.Log("ERROR: PortalUrl is not configured.");

                if (!silent)
                {
                    MessageBox.Show(
                        "The Dashboard portal URL is not configured.\n\n" +
                        $"Edit the configuration file at:\n{ExtensionConfig.ConfigPath}\n\n" +
                        "Set the \"portalUrl\" field to the URL of your Dashboard portal " +
                        "and run this program again to re-register the button.",
                        "Dashboard Extension — Configuration Missing",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                return;
            }

            // rawUrl is non-null/non-empty after the guard above.
            // Assign to a non-nullable local so the compiler knows DashboardWindow
            // will receive a valid string (fixes CS8604).
            string url = rawUrl;

            // Append ?repository=<DatabaseName> so the portal's session middleware
            // activates the same repository that is currently open in the Desktop Client.
            // Priority (enforced by RepositorySessionMiddleware on the server):
            //   1. This parameter — Desktop Client active repository (always wins)
            //   2. Existing session  — from a previous navigation in this same window
            //   3. Configured default — from Settings > Default Repository (Fallback)
            if (!string.IsNullOrWhiteSpace(databaseName))
            {
                string separator = url.Contains('?') ? "&" : "?";
                url = $"{url}{separator}repository={Uri.EscapeDataString(databaseName)}";
            }

            ExtensionLogger.Log($"PortalUrl (from config): {config.PortalUrl}");
            ExtensionLogger.Log($"Final URL: {url}");

            // Open Dashboard in a native WebView2 popup. No external browser is used.
            // Each window gets an isolated WebView2 session so switching between
            // repositories in Laserfiche always opens a correctly-scoped window.
            try
            {
                Application.Run(new DashboardWindow(url));
            }
            catch (Exception ex)
            {
                ExtensionLogger.Log($"DashboardWindow launch failed: {ex.GetType().Name}: {ex.Message}");

                MessageBox.Show(
                    $"Could not open the Dashboard portal.\n\nURL: {url}\n\nError: {ex.Message}",
                    "Dashboard Extension — Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        // ------------------------------------------------------------------ //
        // Diagnostic helpers                                                  //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Logs the presence/absence of WebView2Loader.dll at the three candidate
        /// locations the runtime searches.  Helps diagnose architecture mismatches
        /// (0x8007000B) and missing runtime deployments before WebView2 is initialised.
        /// </summary>
        private static void LogWebView2LoaderPaths()
        {
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;

            var flat  = Path.Combine(exeDir, "WebView2Loader.dll");
            var x64   = Path.Combine(exeDir, "runtimes", "win-x64", "native", "WebView2Loader.dll");
            var x86   = Path.Combine(exeDir, "runtimes", "win-x86", "native", "WebView2Loader.dll");

            ExtensionLogger.Log($"WebView2Loader (flat):     {flat}  exists={File.Exists(flat)}");
            ExtensionLogger.Log($"WebView2Loader (win-x64):  {x64}  exists={File.Exists(x64)}");
            ExtensionLogger.Log($"WebView2Loader (win-x86):  {x86}  exists={File.Exists(x86)}");
        }

        // ------------------------------------------------------------------ //
        // Argument parsing helpers                                            //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Extracts the value of a named argument from the command-line array.
        /// Looks for <paramref name="name"/> and returns the next element.
        /// Returns an empty string when the argument is not present.
        /// </summary>
        private static string ParseNamedArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return string.Empty;
        }
    }
}
