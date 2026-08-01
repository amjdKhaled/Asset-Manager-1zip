// DashboardBA.cs
// WiX Burn Managed Bootstrapper Application.
// Owns the WizardForm lifecycle and bridges between the wizard UI and the Burn engine.

using System;
using System.Windows.Forms;
using WixToolset.Mba.Core;

namespace Dashboard.BA
{
    public sealed class DashboardBA : BootstrapperApplication
    {
        // The wizard form. Created on the UI thread in Run().
        private WizardForm? _form;

        // Window handle for Detect/Apply calls.
        private IntPtr _hwnd;

        // ----------------------------------------------------------------
        // Events raised on the UI thread (via BeginInvoke) so that
        // WizardForm can safely update controls without cross-thread exceptions.
        // ----------------------------------------------------------------

        // (percent, message or null)
        // percent == -1: log message only, no progress bar change.
        public event Action<int, string?>? ProgressUpdated;

        // (success, detailMessage)
        public event Action<bool, string>? InstallFinished;

        // ----------------------------------------------------------------
        // Run: WiX Burn entry point -- called once by the BA host.
        // ----------------------------------------------------------------
        protected override void Run()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            _form = new WizardForm(this);
            Application.Run(_form);

            Engine.Quit(0);
        }

        // ----------------------------------------------------------------
        // Methods called by WizardForm to drive the Burn engine.
        // ----------------------------------------------------------------

        // Start Burn's built-in package-state detection.
        // Must be called before Plan/Apply.
        internal void StartDetect(IntPtr hwnd)
        {
            _hwnd = hwnd;
            Engine.Detect(hwnd);
        }

        // Set all Bundle Variables from wizard config, then begin planning.
        // OnPlanComplete will automatically call Apply.
        internal void StartInstall(InstallConfig config, IntPtr hwnd)
        {
            _hwnd = hwnd;

            // Push wizard values into Burn Variables.
            // Burn passes these to the MSI as MsiProperty elements.
            Engine.SetVariableString("DashboardUrl",         config.DashboardUrl,       false);
            Engine.SetVariableString("LaserficheApiUrl",     config.LaserficheApiUrl,   false);
            Engine.SetVariableString("RepositoryId",         config.RepositoryId,        false);
            Engine.SetVariableString("DisplayName",          config.DisplayName,         false);
            Engine.SetVariableString("LFWebClientPath",      config.LFWebClientPath,     false);
            Engine.SetVariableString("DashboardPort",        config.DashboardPort,       false);
            Engine.SetVariableNumeric("InstallDesktopButton", config.InstallDesktopButton ? 1L : 0L);
            Engine.SetVariableNumeric("InstallWebButton",     config.InstallWebButton     ? 1L : 0L);

            // Plan using the action requested by the command line.
            // For a fresh wizard session this is LaunchAction.Install.
            Engine.Plan(Command.Action);
        }

        internal void StartRepair(IntPtr hwnd)
        {
            _hwnd = hwnd;
            Engine.Plan(LaunchAction.Repair);
        }

        internal void StartUninstall(IntPtr hwnd)
        {
            _hwnd = hwnd;
            Engine.Plan(LaunchAction.Uninstall);
        }

        // ----------------------------------------------------------------
        // Burn engine event overrides (called on engine thread, not UI thread).
        // All UI updates MUST use BeginInvoke.
        // ----------------------------------------------------------------

        protected override void OnDetectComplete(DetectCompleteEventArgs e)
        {
            // Environment detection is handled by DetectionService on a
            // BackgroundWorker thread.  Burn's built-in detection determines
            // package state (installed / not installed) which Plan uses.
            // Nothing extra required here.
        }

        protected override void OnPlanComplete(PlanCompleteEventArgs e)
        {
            if (e.Status >= 0) // S_OK
            {
                Engine.Apply(_hwnd);
            }
            else
            {
                string msg = $"Setup planning failed (code 0x{e.Status:X8}).";
                SafeInvoke(() => InstallFinished?.Invoke(false, msg));
            }
        }

        protected override void OnApplyComplete(ApplyCompleteEventArgs e)
        {
            bool ok  = e.Status >= 0;
            string msg = ok
                ? "Installation completed successfully."
                : $"Installation failed (code 0x{e.Status:X8}).";
            SafeInvoke(() => InstallFinished?.Invoke(ok, msg));
        }

        protected override void OnExecuteProgress(ExecuteProgressEventArgs e)
        {
            SafeInvoke(() => ProgressUpdated?.Invoke(e.OverallPercentage, null));
        }

        protected override void OnExecutePackageBegin(ExecutePackageBeginEventArgs e)
        {
            string msg = $"Installing: {e.PackageId}...";
            SafeInvoke(() => ProgressUpdated?.Invoke(-1, msg));
        }

        protected override void OnCacheAcquireProgress(CacheAcquireProgressEventArgs e)
        {
            if (e.OverallPercentage > 0)
                SafeInvoke(() =>
                    ProgressUpdated?.Invoke(e.OverallPercentage,
                        $"Preparing: {e.PackageOrContainerId}..."));
        }

        protected override void OnError(ErrorEventArgs e)
        {
            string msg = $"Error [{e.ErrorCode}]: {e.ErrorMessage}";
            SafeInvoke(() => ProgressUpdated?.Invoke(-1, msg));
            e.Result = Result.Ok; // suppress MSI error dialog; wizard shows the message
        }

        // ----------------------------------------------------------------
        // Helper: marshal action to the UI thread via BeginInvoke.
        // ----------------------------------------------------------------
        private void SafeInvoke(Action action)
        {
            try
            {
                if (_form == null || _form.IsDisposed) return;
                _form.BeginInvoke(action);
            }
            catch (ObjectDisposedException) { /* form closed */ }
            catch (InvalidOperationException) { /* handle not created yet */ }
        }
    }
}
