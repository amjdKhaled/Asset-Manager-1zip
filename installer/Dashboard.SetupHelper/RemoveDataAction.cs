// RemoveDataAction.cs
// Optional full cleanup requested explicitly from the uninstall confirmation.
// The target is fixed to %ProgramData%\Dashboard and can never be supplied by
// a command-line argument, preventing an MSI property from redirecting deletion.

using System;
using System.IO;
using System.Threading;

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
            Exception? lastError = null;
            for (int attempt = 1; attempt <= 5; attempt++)
            {
                try
                {
                    Directory.Delete(dashboardDir, recursive: true);
                    Console.WriteLine("[SetupHelper] Removed saved Dashboard data from ProgramData.");
                    return 0;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    SetupLog.Warn("RemoveData attempt " + attempt + " failed: " + ex.Message);
                    if (attempt < 5) Thread.Sleep(500);
                }
            }

            if (lastError != null) SetupLog.Error(lastError);
            Console.Error.WriteLine("[SetupHelper] Could not remove saved Dashboard data after stopping IIS.");
            return 1;
        }
    }
}
