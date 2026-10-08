using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Dashboard.SetupHelper
{
    internal static class InstallerJournal
    {
        internal static string PathFor(Dictionary<string, string> opts, string kind)
        {
            string value;
            Guid id;
            if (!opts.TryGetValue("transaction", out value) || !Guid.TryParseExact(value, "N", out id))
                throw new IOException("An exact installer transaction identifier is required.");
            string directory = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData), "Dashboard", "installer-transactions");
            InstallerFileSafety.EnsureNoReparsePoints(directory);
            Directory.CreateDirectory(directory);
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
                security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null),
                    FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None, AccessControlType.Allow));
            Directory.SetAccessControl(directory, security);
            string path = Path.Combine(directory, id.ToString("N") + "." + kind + ".bin");
            InstallerFileSafety.EnsureNoReparsePoints(path);
            return path;
        }
    }
}

