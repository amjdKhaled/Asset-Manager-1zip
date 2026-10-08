using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Dashboard.SetupHelper;
namespace Dashboard.SafetyActions
{
    internal static class IisIsolation
    {
        internal static void Validate(XDocument document, string expectedWebApp)
        {
            var sites = document.Descendants("site").ToArray();
            var site = sites.FirstOrDefault(x => string.Equals((string)x.Attribute("name"), "Dashboard", StringComparison.OrdinalIgnoreCase));
            bool ownedPayload = File.Exists(Path.Combine(expectedWebApp, "LFPortal.Web.dll"));
            if (site != null)
            {
                if (!ownedPayload) throw new IOException("An existing IIS site named Dashboard has no recognized Dashboard payload.");
                var applications = site.Elements("application").ToArray();
                if (applications.Length != 1 || (string)applications[0].Attribute("path") != "/" ||
                    (string)applications[0].Attribute("applicationPool") != "Dashboard")
                    throw new IOException("The Dashboard IIS site contains an unexpected or shared application.");
                var vdirs = applications[0].Elements("virtualDirectory").ToArray();
                if (vdirs.Length != 1 || (string)vdirs[0].Attribute("path") != "/")
                    throw new IOException("The Dashboard IIS site contains another virtual directory.");
                string physical = Environment.ExpandEnvironmentVariables((string)vdirs[0].Attribute("physicalPath") ?? "");
                InstallerFileSafety.RequireSamePath(physical, expectedWebApp);
            }

            foreach (var other in sites)
                foreach (var app in other.Elements("application"))
                    if (string.Equals((string)app.Attribute("applicationPool"), "Dashboard", StringComparison.OrdinalIgnoreCase) &&
                        !ReferenceEquals(other, site))
                        throw new IOException("The Dashboard app pool is used by another IIS site. Setup will not stop, alter or delete it.");
            bool poolExists = document.Descendants("applicationPools").Elements("add")
                .Any(x => string.Equals((string)x.Attribute("name"), "Dashboard", StringComparison.OrdinalIgnoreCase));
            if (poolExists && !ownedPayload)
                throw new IOException("An existing app pool named Dashboard cannot be proven to belong to this application.");
        }
    }
}
