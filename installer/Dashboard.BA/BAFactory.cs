// BAFactory.cs
// WiX Burn Managed Bootstrapper Application factory.
//
// The [assembly: BootstrapperApplicationFactory] attribute is read by the WiX
// Burn managed host (WixManagedBootstrapperApplicationHost) to discover and
// instantiate the bootstrapper application when LFDashboard-Setup.exe starts.
//
// One factory class per BA assembly is required by the WiX MBA contract.

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
            return new DashboardBA();
        }
    }
}
