// BAFactory.cs
// WiX Burn Managed Bootstrapper Application factory.
//
// The [assembly: BootstrapperApplicationFactory] attribute is read by
// WixToolset.Mba.Host.dll (the managed BA host auto-injected by
// bal:WixManagedBootstrapperApplicationHost) to locate this type.
//
// Loading sequence confirmed by inspection of WixToolset.Mba.Host 4.0.5 IL:
//
//   Native mbahost.dll
//     → activates CLR using <startup> from WixToolset.Mba.Host.config
//     → loads WixToolset.Mba.Host.dll (managed)
//         → ConfigurationManager.GetSection("wix.bootstrapper/host")
//         → Assembly.Load("Dashboard.BA")  ← this DLL
//         → GetCustomAttribute<BootstrapperApplicationFactoryAttribute>()
//         → Activator.CreateInstance(BAFactory)  ← BAFactory()
//         → IBootstrapperApplicationFactory.Create(pArgs, pResults)
//             → BaseBootstrapperApplicationFactory.Create(IntPtr, IntPtr)
//                 → InitializeFromCreateArgs(pArgs, …)  [mbanative.dll P/Invoke]
//                 → BAFactory.Create(IEngine, IBootstrapperCommand)  [below]
//                     → new DashboardBA(engine, command)
//                 → StoreBAInCreateResults(pResults, ba)  [mbanative.dll P/Invoke]
//
// StartupLogger writes to %TEMP%\LFDashboard-BA-startup.log and to
// %ProgramData%\LFDashboard\Logs\BA-startup.log on every launch.
// If BAFactory() is reached the log file will exist even if Create() never
// runs; if the log file is absent after launch, the failure is before
// Activator.CreateInstance(BAFactory) and is inside the managed host.

using System;
using WixToolset.Mba.Core;

[assembly: BootstrapperApplicationFactory(typeof(Dashboard.BA.BAFactory))]

namespace Dashboard.BA
{
    public sealed class BAFactory : BaseBootstrapperApplicationFactory
    {
        // ----------------------------------------------------------------
        // Constructor — called by Activator.CreateInstance(typeof(BAFactory))
        // inside WixToolset.Mba.Host.dll BEFORE Create() is ever called.
        //
        // This is the earliest point where managed code we control can log.
        // If the log file is absent after a launch the failure is in:
        //   • config parsing (ConfigurationManager.GetSection)
        //   • assembly loading (Assembly.Load("Dashboard.BA"))
        //   • attribute discovery (GetCustomAttribute)
        //   • factory instantiation (Activator.CreateInstance) — but that
        //     would have to throw from here, which we'd also log.
        // ----------------------------------------------------------------
        public BAFactory()
        {
            StartupLogger.Log("=== BAFactory constructor entered ===");
            StartupLogger.Log($"  Is64BitProcess   : {Environment.Is64BitProcess}");
            StartupLogger.Log($"  CLR version      : {Environment.Version}");
            StartupLogger.Log($"  AppDomain base   : {AppDomain.CurrentDomain.BaseDirectory}");
            StartupLogger.Log($"  CurrentDirectory : {Environment.CurrentDirectory}");
            try
            {
                var loc = typeof(BAFactory).Assembly.Location;
                StartupLogger.Log($"  BAFactory.dll at : {loc}");
            }
            catch { /* ignore */ }
        }

        // ----------------------------------------------------------------
        // Create — called by BaseBootstrapperApplicationFactory.Create(IntPtr,IntPtr)
        // after InitializeFromCreateArgs() gives us real IEngine + IBootstrapperCommand.
        // ----------------------------------------------------------------
        protected override IBootstrapperApplication Create(
            IEngine engine,
            IBootstrapperCommand command)
        {
            StartupLogger.Log("BAFactory.Create entered");
            StartupLogger.Log($"  engine type      : {engine.GetType().FullName}");
            StartupLogger.Log($"  command type     : {command.GetType().FullName}");
            StartupLogger.Log($"  command.Action   : {command.Action}");

            try
            {
                StartupLogger.Log("  Calling new DashboardBA(engine, command)...");
                var ba = new DashboardBA(engine, command);
                StartupLogger.Log("  DashboardBA constructed successfully");
                return ba;
            }
            catch (Exception ex)
            {
                // Log the full exception chain BEFORE rethrowing so Burn's
                // generic HRESULT message cannot be the only evidence.
                StartupLogger.LogException("BAFactory.Create — new DashboardBA() FAILED", ex);
                throw;
            }
        }
    }
}
