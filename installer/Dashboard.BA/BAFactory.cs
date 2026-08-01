// BAFactory.cs
// WiX Burn Managed Bootstrapper Application factory.
//
// The [assembly: BootstrapperApplicationFactory] attribute is read by the WiX
// Burn managed host (WixManagedBootstrapperApplicationHost) to discover and
// instantiate the bootstrapper application when LFDashboard-Setup.exe starts.

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
            // Pass both engine and command: DashboardBA stores command itself
            // because the BootstrapperApplication base-class Command property
            // is private protected in WiX v4 and not accessible from here.
            return new DashboardBA(engine, command);
        }
    }
}
