using System;
using System.IO;

namespace LFPortal.DesktopExtension
{
    /// <summary>
    /// Simple append-only file logger that writes diagnostic entries to
    /// <c>%ProgramData%\Dashboard\logs\extension.log</c>.
    /// </summary>
    /// <remarks>
    /// All methods are deliberately non-throwing: if logging fails (e.g. permission
    /// denied) the extension continues normally. Never log credentials, bearer tokens,
    /// or any user-identifiable information beyond the repository name.
    /// </remarks>
    internal static class ExtensionLogger
    {
        private static readonly string LogDir =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Dashboard", "logs");

        private static readonly string LogPath = Path.Combine(LogDir, "extension.log");

        // ------------------------------------------------------------------ //
        // Public API                                                          //
        // ------------------------------------------------------------------ //

        /// <summary>Writes a single line with a UTC timestamp prefix.</summary>
        internal static void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(LogDir);
                var line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z] {message}";
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
            catch
            {
                // Never crash the extension due to a logging failure.
            }
        }

        /// <summary>
        /// Writes a blank line followed by a titled section header, useful for
        /// separating distinct extension invocations in the log file.
        /// </summary>
        internal static void LogSection(string title)
        {
            try
            {
                Directory.CreateDirectory(LogDir);
                var header =
                    Environment.NewLine +
                    $"===== {title} =====" + Environment.NewLine +
                    $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z] ";
                File.AppendAllText(LogPath, header);
            }
            catch { }
        }
    }
}
