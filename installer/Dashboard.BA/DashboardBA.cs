// DashboardBA.cs
// WiX Burn Managed Bootstrapper Application.
//
// WiX v4 API notes (confirmed from WixToolset.Mba.Core 4.0.5):
//
//   1. BootstrapperApplication(IEngine engine)  -- base constructor requires IEngine.
//      The base class stores it internally for event wiring; it does NOT expose
//      the engine as an accessible property.
//
//   2. There is a CLASS named WixToolset.Mba.Core.Engine in the assembly.
//      Writing `Engine.Plan(...)` resolves to that class (static), not to any
//      instance property, causing CS0120.  The fix is to store IEngine ourselves
//      as a private field (_engine) and call _engine.Plan(...) etc.
//
//   3. Command (IBootstrapperCommand) is not accessible via the base class from
//      a different assembly.  Store it ourselves.
//
//   4. ErrorEventArgs is ambiguous between WixToolset.Mba.Core and System.IO;
//      resolved with a using alias below.

using System;
using System.Windows.Forms;
using WixToolset.Mba.Core;

// Resolve ambiguity: WixToolset.Mba.Core.ErrorEventArgs vs System.IO.ErrorEventArgs.
using MbaErrorEventArgs = WixToolset.Mba.Core.ErrorEventArgs;

namespace Dashboard.BA
{
    public sealed class DashboardBA : BootstrapperApplication
    {
        // Store IEngine ourselves -- the base class provides no accessible property.
        // (There is a class Engine in WixToolset.Mba.Core; bare 'Engine.Xxx()' would
        //  resolve to that class, not to an instance method.)
        private readonly IEngine _engine;

        // Store IBootstrapperCommand ourselves (not accessible from base class
        // in an external assembly).
        private readonly IBootstrapperCommand _command;

        // The wizard form. Created on the UI thread in Run().
        private WizardForm? _form;

        // Window handle used for Detect / Apply calls.
        private IntPtr _hwnd;

        // ----------------------------------------------------------------
        // Constructor -- called by BAFactory.Create().
        // ----------------------------------------------------------------
        public DashboardBA(IEngine engine, IBootstrapperCommand command)
            : base(engine)       // required by BootstrapperApplication base ctor
        {
            _engine  = engine;
            _command = command;
        }

        // ----------------------------------------------------------------
        // Events raised on the UI thread (via BeginInvoke) so that
        // WizardForm can safely update controls without cross-thread exceptions.
        // ----------------------------------------------------------------

        // percent == -1: log message only, do not change the progress bar value.
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

            _engine.Quit(0);
        }

        // ----------------------------------------------------------------
        // Methods called by WizardForm to drive the Burn engine.
        // ----------------------------------------------------------------

        // Start Burn's built-in package-state detection.
        // Must be called before Plan/Apply.
        internal void StartDetect(IntPtr hwnd)
        {
            _hwnd = hwnd;
            _engine.Detect(hwnd);
        }

        // Push wizard values into Bundle Variables then begin planning.
        // OnPlanComplete will automatically call Apply.
        internal void StartInstall(InstallConfig config, IntPtr hwnd)
        {
            _hwnd = hwnd;

            _engine.SetVariableString("DashboardUrl",     config.DashboardUrl,     false);
            _engine.SetVariableString("LaserficheApiUrl", config.LaserficheApiUrl, false);
            _engine.SetVariableString("RepositoryId",     config.RepositoryId,     false);
            _engine.SetVariableString("DisplayName",      config.DisplayName,      false);
            _engine.SetVariableString("LFWebClientPath",  config.LFWebClientPath,  false);
            _engine.SetVariableString("DashboardPort",    config.DashboardPort,    false);
            _engine.SetVariableNumeric("InstallDesktopButton", config.InstallDesktopButton ? 1L : 0L);
            _engine.SetVariableNumeric("InstallWebButton",     config.InstallWebButton     ? 1L : 0L);

            // Plan using the action the Bundle was invoked with (Install / Repair / Uninstall).
            _engine.Plan(_command.Action);
        }

        internal void StartRepair(IntPtr hwnd)
        {
            _hwnd = hwnd;
            _engine.Plan(LaunchAction.Repair);
        }

        internal void StartUninstall(IntPtr hwnd)
        {
            _hwnd = hwnd;
            _engine.Plan(LaunchAction.Uninstall);
        }

        // ----------------------------------------------------------------
        // Burn engine event overrides.
        // All called on the engine thread -- UI updates MUST use BeginInvoke.
        // ----------------------------------------------------------------

        protected override void OnDetectComplete(DetectCompleteEventArgs e)
        {
            // Environment detection is done by DetectionService (BackgroundWorker).
            // Burn's own detection tracks package state (installed/absent) for Plan.
            // Nothing extra needed here.
        }

        protected override void OnPlanComplete(PlanCompleteEventArgs e)
        {
            if (e.Status >= 0) // S_OK (HRESULT >= 0 means success)
            {
                _engine.Apply(_hwnd);
            }
            else
            {
                string msg = string.Format("Setup planning failed (HRESULT 0x{0:X8}).", e.Status);
                SafeInvoke(() => InstallFinished?.Invoke(false, msg));
            }
        }

        protected override void OnApplyComplete(ApplyCompleteEventArgs e)
        {
            bool   ok  = e.Status >= 0;
            string msg = ok
                ? "Installation completed successfully."
                : string.Format("Installation failed (HRESULT 0x{0:X8}).", e.Status);
            SafeInvoke(() => InstallFinished?.Invoke(ok, msg));
        }

        protected override void OnExecuteProgress(ExecuteProgressEventArgs e)
        {
            SafeInvoke(() => ProgressUpdated?.Invoke(e.OverallPercentage, null));
        }

        protected override void OnExecutePackageBegin(ExecutePackageBeginEventArgs e)
        {
            string msg = string.Format("Installing: {0}...", e.PackageId);
            SafeInvoke(() => ProgressUpdated?.Invoke(-1, msg));
        }

        protected override void OnCacheAcquireProgress(CacheAcquireProgressEventArgs e)
        {
            if (e.OverallPercentage > 0)
            {
                string msg = string.Format("Preparing: {0}...", e.PackageOrContainerId);
                SafeInvoke(() => ProgressUpdated?.Invoke(e.OverallPercentage, msg));
            }
        }

        protected override void OnError(MbaErrorEventArgs e)
        {
            string msg = string.Format("Error [{0}]: {1}", e.ErrorCode, e.ErrorMessage);
            SafeInvoke(() => ProgressUpdated?.Invoke(-1, msg));
            // Suppress the MSI error dialog; the wizard shows the message in its log box.
            e.Result = Result.Ok;
        }

        // ----------------------------------------------------------------
        // Helper: marshal an action to the UI thread via BeginInvoke.
        // ----------------------------------------------------------------
        private void SafeInvoke(Action action)
        {
            try
            {
                if (_form == null || _form.IsDisposed) return;
                _form.BeginInvoke(action);
            }
            catch (ObjectDisposedException) { /* form was closed mid-install */ }
            catch (InvalidOperationException) { /* handle not yet created */ }
        }
    }
}
