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
            // NOTE: StartupLogger.Log cannot be called BEFORE base(engine) because
            // C# requires the base initialiser to run first.  We log immediately after
            // the base constructor returns so any failure in base(engine) is caught
            // by the try/catch in BAFactory.Create() which wraps this constructor call.
            StartupLogger.Log("DashboardBA constructor: base(engine) completed");

            _engine  = engine;
            _command = command;

            // Diagnostic identity + launch context: proves which process/BA
            // instance did what, and WHY Burn launched this BA (a related
            // bundle executed during an upgrade arrives with Display!=Full
            // and/or Relation!=None -- that combination must NEVER show UI).
            RuntimeLog.Log("DashboardBA.Constructor",
                $"Action={command.Action} Display={command.Display} " +
                $"Resume={command.Resume} Relation={command.Relation}");

            StartupLogger.Log(
                $"DashboardBA constructor: completed  Action={command.Action} " +
                $"Display={command.Display} Resume={command.Resume} Relation={command.Relation}");
        }

        // True when this BA execution must show NO UI.  This is the root-cause
        // fix for the "second Dashboard Configuration window at ~50%" bug:
        // during a same-version upgrade Burn executes the previously installed
        // RELATED bundle mid-Apply (Relation=Upgrade, Display=Embedded), and
        // its BA used to open a full wizard on top of the active installer.
        //
        // Criteria are deliberately NARROW so direct user launches keep the
        // exact previous behavior:
        //   - Relation != None  : this process is a related-bundle execution
        //                         driven by another Burn engine.  Never UI.
        //   - Display == Embedded: this process is hosted by another Burn
        //                         engine.  Never UI.
        // Passive/None/Unknown DIRECT launches remain interactive as before --
        // a headless first-time install would produce a broken configuration
        // because required wizard values would never be collected.
        private bool IsSilentExecution =>
            _command.Relation != RelationType.None ||
            _command.Display  == Display.Embedded;

        // Signalled when the headless (silent) chain finishes.
        private System.Threading.ManualResetEvent? _silentDone;
        private int _silentExitCode;

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
            StartupLogger.Log("DashboardBA.Run entered");
            RuntimeLog.Log("Run.Entry",
                $"Silent={IsSilentExecution} Action={_command.Action} Display={_command.Display} Relation={_command.Relation}");
            try
            {
                if (IsSilentExecution)
                {
                    // HARD INVARIANT: no WizardForm, no message loop, no UI of
                    // any kind for embedded/passive/quiet/related executions.
                    RuntimeLog.Log("Run.SilentMode",
                        "Running headless Detect->Plan->Apply; no UI will be created.");
                    _silentDone = new System.Threading.ManualResetEvent(false);
                    try
                    {
                        _engine.Detect(IntPtr.Zero);
                    }
                    catch (Exception ex)
                    {
                        RuntimeLog.Log("Run.SilentDetectFailed", ex.Message);
                        _silentExitCode = ex.HResult != 0 ? ex.HResult : unchecked((int)0x80004005);
                        _silentDone.Set();
                    }
                    // Fail-safe timeout: a related-bundle uninstall/upgrade must
                    // never hang the parent installer forever if a completion
                    // callback is skipped.  60 minutes is far beyond any
                    // legitimate chain execution.
                    if (!_silentDone.WaitOne(TimeSpan.FromMinutes(60)))
                    {
                        RuntimeLog.Log("Run.SilentTimeout",
                            "Headless chain did not complete within 60 minutes.");
                        _silentExitCode = unchecked((int)0x800705B4); // ERROR_TIMEOUT
                    }
                    RuntimeLog.Log("Run.SilentComplete", $"ExitCode=0x{_silentExitCode:X8}");
                    _engine.Quit(_silentExitCode);
                    return;
                }

                StartupLogger.Log("  Calling Application.EnableVisualStyles()");
                Application.EnableVisualStyles();

                StartupLogger.Log("  Calling Application.SetCompatibleTextRenderingDefault(false)");
                Application.SetCompatibleTextRenderingDefault(false);

                StartupLogger.Log("  Creating WizardForm...");
                _form = new WizardForm(this);
                StartupLogger.Log("  WizardForm created successfully");

                StartupLogger.Log("  Calling Application.Run(_form)...");
                Application.Run(_form);
                StartupLogger.Log("  Application.Run returned");

                _engine.Quit(0);
            }
            catch (Exception ex)
            {
                StartupLogger.LogException("DashboardBA.Run FAILED", ex);
                throw;
            }
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
            // RepositoryId / DisplayName intentionally not set: repository is
            // runtime session context, not installation configuration.
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

        protected override void OnDetectBegin(DetectBeginEventArgs e)
        {
            RuntimeLog.Log("Detect.Begin");
            base.OnDetectBegin(e);
        }

        protected override void OnDetectComplete(DetectCompleteEventArgs e)
        {
            RuntimeLog.Log("Detect.Complete", $"Status=0x{e.Status:X8}");

            if (IsSilentExecution)
            {
                if (e.Status >= 0)
                {
                    try
                    {
                        // Headless chain: plan the action Burn asked for.
                        _engine.Plan(_command.Action);
                    }
                    catch (Exception ex)
                    {
                        RuntimeLog.Log("Detect.SilentPlanFailed", ex.Message);
                        _silentExitCode = ex.HResult != 0 ? ex.HResult : unchecked((int)0x80004005);
                        _silentDone?.Set();
                    }
                }
                else
                {
                    _silentExitCode = e.Status;
                    _silentDone?.Set();
                }
                return;
            }
            // Interactive: environment detection is done by DetectionService
            // (BackgroundWorker).  Burn's own detection tracks package state
            // (installed/absent) for Plan.  Nothing extra needed here.
        }

        protected override void OnPlanBegin(PlanBeginEventArgs e)
        {
            RuntimeLog.Log("Plan.Begin");
            base.OnPlanBegin(e);
        }

        protected override void OnPlanComplete(PlanCompleteEventArgs e)
        {
            RuntimeLog.Log("Plan.Complete", $"Status=0x{e.Status:X8}");
            if (e.Status >= 0) // S_OK (HRESULT >= 0 means success)
            {
                try
                {
                    _engine.Apply(_hwnd);
                }
                catch (Exception ex) when (IsSilentExecution)
                {
                    RuntimeLog.Log("Plan.SilentApplyFailed", ex.Message);
                    _silentExitCode = ex.HResult != 0 ? ex.HResult : unchecked((int)0x80004005);
                    _silentDone?.Set();
                }
            }
            else if (IsSilentExecution)
            {
                _silentExitCode = e.Status;
                _silentDone?.Set();
            }
            else
            {
                string msg = string.Format("Setup planning failed (HRESULT 0x{0:X8}).", e.Status);
                SafeInvoke(() => InstallFinished?.Invoke(false, msg));
            }
        }

        protected override void OnApplyBegin(ApplyBeginEventArgs e)
        {
            RuntimeLog.Log("Apply.Begin");
            base.OnApplyBegin(e);
        }

        protected override void OnApplyComplete(ApplyCompleteEventArgs e)
        {
            RuntimeLog.Log("Apply.Complete", $"Status=0x{e.Status:X8}");

            if (IsSilentExecution)
            {
                _silentExitCode = e.Status >= 0 ? 0 : e.Status;
                _silentDone?.Set();
                return;
            }

            bool   ok  = e.Status >= 0;
            string msg = ok
                ? "Installation completed successfully."
                : string.Format("Installation failed (HRESULT 0x{0:X8}).", e.Status);
            SafeInvoke(() => InstallFinished?.Invoke(ok, msg));
        }

        protected override void OnShutdown(ShutdownEventArgs e)
        {
            RuntimeLog.Log("Shutdown");
            // Last-resort unblock for the headless chain: if the engine shuts
            // down without ApplyComplete having fired, never leave Run() waiting.
            _silentDone?.Set();
            base.OnShutdown(e);
        }

        protected override void OnExecuteProgress(ExecuteProgressEventArgs e)
        {
            SafeInvoke(() => ProgressUpdated?.Invoke(e.OverallPercentage, null));
        }

        protected override void OnExecutePackageBegin(ExecutePackageBeginEventArgs e)
        {
            RuntimeLog.Log("ExecutePackage.Begin", $"Package={e.PackageId}");
            string msg = string.Format("Installing: {0}...", e.PackageId);
            SafeInvoke(() => ProgressUpdated?.Invoke(-1, msg));
        }

        protected override void OnExecutePackageComplete(ExecutePackageCompleteEventArgs e)
        {
            RuntimeLog.Log("ExecutePackage.Complete",
                $"Package={e.PackageId} Status=0x{e.Status:X8}");
            base.OnExecutePackageComplete(e);
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
