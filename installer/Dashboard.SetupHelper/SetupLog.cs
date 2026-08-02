// SetupLog.cs
// Persistent diagnostic log for Dashboard.SetupHelper.
//
// The MSI log only records "returned actual error code 1" for ExeCommand
// custom actions -- stdout/stderr of a deferred elevated EXE is NOT reliably
// captured.  This logger writes every invocation to:
//
//   %ProgramData%\Dashboard\Logs\SetupHelper.log
//
// so install failures can be diagnosed after the fact.
//
// RULES:
//   - The logger must NEVER throw: every operation is wrapped in try/catch.
//     A logging failure must not fail the installation.
//   - No passwords, tokens, credentials, or secrets are ever logged.
//     (The helper never receives any -- see Program.cs header.)

using System;
using System.IO;
using System.Text;

namespace Dashboard.SetupHelper
{
    internal static class SetupLog
    {
        private static string? _logPath;

        // Resolves the log path and creates the directory. Never throws.
        public static void Init()
        {
            try
            {
                string programData = Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonApplicationData);
                string logDir = Path.Combine(programData, "Dashboard", "Logs");
                Directory.CreateDirectory(logDir);
                _logPath = Path.Combine(logDir, "SetupHelper.log");
            }
            catch
            {
                _logPath = null; // logging disabled; helper continues
            }
        }

        public static void Info(string message)  => Write("INFO ", message);
        public static void Warn(string message)  => Write("WARN ", message);
        public static void Error(string message) => Write("ERROR", message);

        // Logs the complete exception chain: type, message, HRESULT,
        // stack trace, and all InnerExceptions.
        public static void Error(Exception ex)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("EXCEPTION:");
                Exception? cur = ex;
                int depth = 0;
                while (cur != null)
                {
                    string prefix = depth == 0 ? "  " : $"  [Inner {depth}] ";
                    sb.AppendLine($"{prefix}Type    : {cur.GetType().FullName}");
                    sb.AppendLine($"{prefix}Message : {cur.Message}");
                    sb.AppendLine($"{prefix}HRESULT : 0x{cur.HResult:X8}");
                    sb.AppendLine($"{prefix}Stack   : {cur.StackTrace}");
                    cur = cur.InnerException;
                    depth++;
                }
                Write("ERROR", sb.ToString());
            }
            catch { /* never throw from the logger */ }
        }

        private static void Write(string level, string message)
        {
            // Always echo to console too (visible when run interactively /
            // in the build smoke test).
            try
            {
                if (level == "INFO ") Console.WriteLine($"[SetupHelper] {message}");
                else Console.Error.WriteLine($"[SetupHelper] {level.Trim()}: {message}");
            }
            catch { }

            if (_logPath == null) return;
            try
            {
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(_logPath, line, Encoding.UTF8);
            }
            catch { /* never throw from the logger */ }
        }
    }
}
