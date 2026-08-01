using System;
using System.Diagnostics;
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
    ///       Reads the portal URL from the extension config and opens it in the default
    ///       browser. The Laserfiche token <c>%(DatabaseName)</c> is appended as a
    ///       <c>?repository=</c> query parameter so the portal can automatically activate
    ///       the repository that is open in the Desktop Client.
    ///       <code>Dashboard.DesktopExtension.exe -buttonclick -connguid "{guid}" -hwnd "{hwnd}" -pid "{pid}" -databasename "{DatabaseName}"</code>
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
            // ----------------------------------------------------------------
            if (argList.Contains("-buttonclick"))
            {
                var databaseName = ParseNamedArg(args, "-databasename");
                OpenPortalInBrowser(silent, databaseName);
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
        // Button-click: open the portal URL in the default browser           //
        // ------------------------------------------------------------------ //

        private static void OpenPortalInBrowser(bool silent, string databaseName)
        {
            var config = ExtensionConfig.Load();
            var url = config.PortalUrl?.Trim();

            if (string.IsNullOrEmpty(url))
            {
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

            // Append ?repository=<DatabaseName> when provided so the portal
            // activates the same repository that is open in the Desktop Client.
            if (!string.IsNullOrWhiteSpace(databaseName))
            {
                var separator = url.Contains('?') ? "&" : "?";
                url = $"{url}{separator}repository={Uri.EscapeDataString(databaseName)}";
            }

            try
            {
                // UseShellExecute = true delegates to the OS default browser handler.
                Process.Start(new ProcessStartInfo(url)
                {
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Could not open the Dashboard portal.\n\nURL: {url}\n\nError: {ex.Message}",
                    "Dashboard Extension — Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
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
