using System;
using System.IO;

namespace Dashboard.SetupHelper
{
    internal static class RemoveDataAction
    {
        // Exact application-owned names only. No wildcards, recursive deletion,
        // directory removal, or caller-supplied target.
        internal static readonly string[] OwnedFiles =
        {
            "extension.config.json", "laserfiche.config.json", "laserfiche.runtime.json",
            Path.Combine("credentials", "37a8eec1ce19687d132fe29051dca629d164e2c4958ba141d5f4133a33f0688f.dpapi")
        };

        public static int Execute()
        {
            string root = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData), "Dashboard");
            return RemoveKnownFiles(root);
        }

        internal static int RemoveKnownFiles(string root)
        {
            try
            {
                InstallerFileSafety.EnsureNoReparsePoints(root);
                // Validate ALL targets before deleting the first one.
                foreach (string name in OwnedFiles)
                    InstallerFileSafety.EnsureNoReparsePoints(Path.Combine(root, name));
                foreach (string name in OwnedFiles)
                {
                    string path = Path.Combine(root, name);
                    InstallerFileSafety.EnsureNoReparsePoints(path);
                    if (File.Exists(path)) File.Delete(path);
                }
                Console.WriteLine("[SetupHelper] Removed known Dashboard configuration and credentials. Logs and other files were preserved.");
                return 0;
            }
            catch (Exception ex)
            {
                SetupLog.Error(ex);
                Console.Error.WriteLine("[SetupHelper] Cleanup was refused or incomplete; preserved remaining files.");
                return 1;
            }
        }
    }
}
