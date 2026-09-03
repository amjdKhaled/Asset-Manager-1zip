// RemoveDataAction.cs
// Optional full cleanup requested explicitly from the uninstall confirmation.
// The target is fixed to %ProgramData%\Dashboard and can never be supplied by
// a command-line argument, preventing an MSI property from redirecting deletion.

using System;
using System.IO;

namespace Dashboard.SetupHelper
{
    internal static class RemoveDataAction
    {
        public static int Execute()
        {
            string programData = Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData);
            string dashboardDir = Path.GetFullPath(Path.Combine(programData, "Dashboard"));
            string expectedParent = Path.GetFullPath(programData)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            // Defense in depth: only the direct Dashboard child of the actual
            // CommonApplicationData directory may ever be deleted.
            if (!dashboardDir.StartsWith(expectedParent, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetFileName(dashboardDir), "Dashboard", StringComparison.OrdinalIgnoreCase))
            {
                SetupLog.Error("RemoveData refused unexpected path: " + dashboardDir);
                return 1;
            }

            if (!Directory.Exists(dashboardDir))
            {
                Console.WriteLine("[SetupHelper] No saved Dashboard data was found.");
                return 0;
            }

            SetupLog.Info("RemoveData: deleting saved configuration, credentials, and logs.");
            try
            {
                Directory.Delete(dashboardDir, recursive: true);
                Console.WriteLine("[SetupHelper] Removed saved Dashboard data from ProgramData.");
                return 0;
            }
            catch (Exception ex)
            {
                SetupLog.Error(ex);
                Console.Error.WriteLine("[SetupHelper] Could not remove saved Dashboard data: " + ex.Message);
                return 1;
            }
        }
    }
}
