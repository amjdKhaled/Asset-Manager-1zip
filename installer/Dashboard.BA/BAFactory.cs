// BAFactory.cs
// WiX Burn Managed Bootstrapper Application factory.
//
// The [assembly: BootstrapperApplicationFactory] attribute is read by the WiX
// Burn managed host (WixManagedBootstrapperApplicationHost) to discover and
// instantiate the bootstrapper application when LFDashboard-Setup.exe starts.

using System;
using WixToolset.Mba.Core;

[assembly: BootstrapperApplicationFactory(typeof(Dashboard.BA.BAFactory))]

namespace Dashboard.BA
{
    public sealed class BAFactory : BaseBootstrapperApplicationFactory
    {
        protected override IBootstrapperApplication Create(
            IEngine engine,
            IBootstrapperCommand command)
        {
            // ---- diagnostic: write header + environment info ----
            StartupLogger.Log("BAFactory.Create entered");
            StartupLogger.Log($"  Is64BitProcess   : {Environment.Is64BitProcess}");
            StartupLogger.Log($"  CLR version      : {Environment.Version}");
            StartupLogger.Log($"  AppDomain base   : {AppDomain.CurrentDomain.BaseDirectory}");
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
                // Log the full exception chain BEFORE rethrowing so Burn cannot swallow it.
                StartupLogger.LogException("BAFactory.Create — new DashboardBA() FAILED", ex);
                throw;
            }
        }
    }
}
