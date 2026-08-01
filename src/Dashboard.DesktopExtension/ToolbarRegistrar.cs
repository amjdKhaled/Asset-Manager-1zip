using Laserfiche.ClientAutomation;
using System;
using System.IO;
using System.Windows.Forms;

namespace LFPortal.DesktopExtension
{
    /// <summary>
    /// Registers (or removes) a custom toolbar button in the Laserfiche Desktop Client
    /// using the <c>Laserfiche.ClientAutomation</c> SDK.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class must only be called by the <em>setup mode</em> of the extension
    /// (<c>--setup</c> / <c>--remove</c> arguments). It requires the Laserfiche
    /// Desktop Client to be installed on the same machine and the
    /// <c>ClientAutomation.dll</c> SDK DLL to be present.
    /// </para>
    /// <para>
    /// The registration API is documented in the Laserfiche SDK 10.4 sample project
    /// <em>CustomButtonManager</em>. The button <c>Command</c> field is set to:
    /// <code>"path\to\Dashboard.DesktopExtension.exe" -buttonclick -connguid "%(ConnectionGUID)" -hwnd "%(hwnd)" -pid "%(PID)" -databasename "%(DatabaseName)"</code>
    /// Laserfiche replaces the <c>%(…)</c> tokens at runtime before invoking the process.
    /// The <c>%(DatabaseName)</c> token provides the name of the repository that is
    /// currently active in the Laserfiche Desktop Client window, allowing the portal
    /// to pre-select that repository without any SDK calls at click time.
    /// </para>
    /// </remarks>
    internal static class ToolbarRegistrar
    {
        private const string ToolbarName = "Dashboard";

        /// <summary>
        /// Adds the Dashboard toolbar button to the Laserfiche Desktop Client main window.
        /// If the toolbar already exists it is removed first to avoid duplicates.
        /// </summary>
        /// <param name="config">Extension configuration (button label, icon path).</param>
        /// <param name="silent">
        /// When <c>true</c> no dialog is shown on success. Errors always show a dialog.
        /// </param>
        public static void Register(ExtensionConfig config, bool silent = false)
        {
            try
            {
                // Remove any existing Dashboard toolbar/button first so re-running
                // setup after an update doesn't leave orphaned entries.
                RemoveInternal(silent: true);

                var exePath = Application.ExecutablePath;

                // Laserfiche token substitutions (evaluated by the Desktop Client at click time):
                //   %(ConnectionGUID) — GUID of the active repository connection
                //   %(hwnd)           — HWND of the active Desktop Client window
                //   %(PID)            — process ID of the Desktop Client process
                //   %(DatabaseName)   — name of the currently open repository
                var command =
                    $"\"{exePath}\" -buttonclick" +
                    " -connguid \"%(ConnectionGUID)\"" +
                    " -hwnd \"%(hwnd)\"" +
                    " -pid \"%(PID)\"" +
                    " -databasename \"%(DatabaseName)\"";

                // Resolve the icon path: prefer the built-in Resources\Dashboard.ico
                // that ships alongside the EXE; fall back to the config-specified path.
                var builtInIconPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Resources",
                    "Dashboard.ico");

                var resolvedIconPath = File.Exists(builtInIconPath)
                    ? builtInIconPath
                    : config.IconPath;

                using (var clientManager = new ClientManager())
                using (var toolbarMgr = clientManager.GetToolbarManager(ClientWindowType.Main))
                {
                    // Create the toolbar that contains our button.
                    toolbarMgr.AddToolbar(ToolbarName, ToolbarPosition.Top);

                    // Register the custom button definition.
                    var buttonInfo = new CustomButtonInfo
                    {
                        Description = config.ButtonLabel,
                        Command     = command,
                    };

                    // Only set the icon path when a valid file is resolved.
                    if (!string.IsNullOrWhiteSpace(resolvedIconPath)
                        && File.Exists(resolvedIconPath))
                    {
                        buttonInfo.IconPath = resolvedIconPath;
                    }

                    int buttonId = toolbarMgr.AddCustomToolbarButton(buttonInfo);

                    // Add the registered button to the toolbar.
                    var tbButton = new ToolbarButtonInfo
                    {
                        Id          = buttonId,
                        IsSeparator = false,
                    };
                    toolbarMgr.AddButton(ToolbarName, tbButton, -1);
                }

                if (!silent)
                {
                    MessageBox.Show(
                        $"Dashboard toolbar button \"{config.ButtonLabel}\" added " +
                        "to the Laserfiche Desktop Client.\n\n" +
                        $"Portal URL: {config.PortalUrl}\n\n" +
                        "When the button is clicked, the active Laserfiche repository " +
                        "will be passed automatically to the Dashboard.",
                        "Dashboard Extension — Setup Complete",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Failed to register the Dashboard toolbar button.\n\n" +
                    "Ensure the Laserfiche Desktop Client is installed and that this " +
                    "program is run as an administrator.\n\n" +
                    $"Details: {ex.Message}",
                    "Dashboard Extension — Setup Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Removes the Dashboard toolbar and all associated custom buttons from
        /// the Laserfiche Desktop Client.
        /// </summary>
        /// <param name="silent">
        /// When <c>true</c> no dialog is shown on success or when nothing is found.
        /// </param>
        public static void Remove(bool silent = false) => RemoveInternal(silent);

        private static void RemoveInternal(bool silent)
        {
            try
            {
                bool removed = false;

                using (var clientManager = new ClientManager())
                using (var toolbarMgr = clientManager.GetToolbarManager(ClientWindowType.Main))
                {
                    // Remove the named toolbar.
                    int count = toolbarMgr.GetToolbarCount();
                    for (int i = 0; i < count; i++)
                    {
                        if (string.Equals(
                                toolbarMgr.GetToolbarName(i),
                                ToolbarName,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            toolbarMgr.DeleteToolbar(ToolbarName);
                            removed = true;
                            break;
                        }
                    }

                    // Remove any custom button whose command references this executable.
                    var exeName = Path.GetFileName(Application.ExecutablePath);
                    int btnCount = toolbarMgr.GetCustomToolbarButtonCount();
                    for (int i = btnCount - 1; i >= 0; i--)
                    {
                        var info = toolbarMgr.GetCustomToolbarButton(i);
                        if (info.Command.IndexOf(exeName,
                                StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            toolbarMgr.RemoveCustomToolbarButton(i);
                            removed = true;
                        }
                    }
                }

                if (!silent)
                {
                    MessageBox.Show(
                        removed
                            ? "Dashboard toolbar button removed from the Laserfiche Desktop Client."
                            : "No Dashboard toolbar button found to remove.",
                        "Dashboard Extension — Remove",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    MessageBox.Show(
                        "Failed to remove the Dashboard toolbar button.\n\n" +
                        $"Details: {ex.Message}",
                        "Dashboard Extension — Remove Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
        }
    }
}
