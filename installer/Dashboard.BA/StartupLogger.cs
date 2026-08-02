// StartupLogger.cs
// Self-contained startup diagnostic logger for the Dashboard managed BA.
//
// Writes to TWO locations for redundancy:
//   %TEMP%\LFDashboard-BA-startup.log
//   %ProgramData%\LFDashboard\Logs\BA-startup.log
//
// Uses only System.IO.  All public methods silently swallow every exception —
// the logger MUST never be the reason the bootstrapper application fails.
//
// Design constraints:
//   • No static constructor   (a faulting static ctor → TypeInitializationException,
//                              which is exactly what we are trying to diagnose)
//   • No DI, no WiX engine, no UI, no third-party libs
//   • Thread-safe via a simple object lock around file writes
//   • Writes to both %TEMP% and %ProgramData% so that if one location fails
//     or is inaccessible (e.g. elevation, temp dir per-session isolation)
//     the other survives.

using System;
using System.IO;
using System.Text;

namespace Dashboard.BA
{
    internal static class StartupLogger
    {
        // ---------------------------------------------------------------- state
        // No static constructor — field initialisers below cannot throw.
        private static readonly object _lock   = new object();
        private static string _tempPath        = "";   // "" ⇒ disabled
        private static string _commonPath      = "";   // "" ⇒ disabled
        private static bool   _ready           = false;

        // ---------------------------------------------------------------- init

        // Called lazily on first use.  Safe to call multiple times; idempotent.
        private static void EnsureReady()
        {
            if (_ready) return;
            _ready = true;   // set first so a fault here disables the logger cleanly

            var header = BuildHeader();

            // Location 1: %TEMP%\LFDashboard-BA-startup.log
            try
            {
                _tempPath = Path.Combine(Path.GetTempPath(), "LFDashboard-BA-startup.log");
                lock (_lock) { File.WriteAllText(_tempPath, header, Encoding.UTF8); }
            }
            catch { _tempPath = ""; }

            // Location 2: %ProgramData%\LFDashboard\Logs\BA-startup.log
            try
            {
                var programData = Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonApplicationData);
                var logDir = Path.Combine(programData, "LFDashboard", "Logs");
                Directory.CreateDirectory(logDir);
                _commonPath = Path.Combine(logDir, "BA-startup.log");
                lock (_lock) { File.WriteAllText(_commonPath, header, Encoding.UTF8); }
            }
            catch { _commonPath = ""; }
        }

        private static string BuildHeader()
        {
            var sb = new StringBuilder();
            sb.AppendLine("====================================================");
            sb.AppendLine("  LFDashboard Bootstrapper Application – startup log");
            try { sb.AppendLine($"  {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}"); } catch { /* ignore */ }
            sb.AppendLine("----------------------------------------------------");
            try { sb.AppendLine($"  Process bit-ness : {(IntPtr.Size == 8 ? "64-bit" : "32-bit")}"); } catch { /* ignore */ }
            try { sb.AppendLine($"  CLR version      : {Environment.Version}"); } catch { /* ignore */ }
            try { sb.AppendLine($"  OS               : {Environment.OSVersion}"); } catch { /* ignore */ }
            try { sb.AppendLine($"  AppDomain base   : {AppDomain.CurrentDomain.BaseDirectory}"); } catch { /* ignore */ }
            try { sb.AppendLine($"  Temp path        : {Path.GetTempPath()}"); } catch { /* ignore */ }
            try
            {
                var pd = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                sb.AppendLine($"  ProgramData      : {pd}");
            }
            catch { /* ignore */ }
            sb.AppendLine("====================================================");
            return sb.ToString();
        }

        // ---------------------------------------------------------------- public API

        /// <summary>Append a plain informational line to the log.</summary>
        public static void Log(string message)
        {
            try
            {
                EnsureReady();
                var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}";
                lock (_lock)
                {
                    if (_tempPath.Length > 0)   File.AppendAllText(_tempPath,   line, Encoding.UTF8);
                    if (_commonPath.Length > 0) File.AppendAllText(_commonPath, line, Encoding.UTF8);
                }
            }
            catch { /* never propagate */ }
        }

        /// <summary>
        /// Append the full exception chain to the log.
        /// Captures Type, Message, HResult, and StackTrace for each nested exception.
        /// </summary>
        public static void LogException(string context, Exception? ex)
        {
            try
            {
                EnsureReady();
                var sb = new StringBuilder();
                sb.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] *** EXCEPTION in {context} ***");
                AppendException(sb, ex, depth: 0);
                sb.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] *** END EXCEPTION ***");
                var text = sb.ToString();
                lock (_lock)
                {
                    if (_tempPath.Length > 0)   File.AppendAllText(_tempPath,   text, Encoding.UTF8);
                    if (_commonPath.Length > 0) File.AppendAllText(_commonPath, text, Encoding.UTF8);
                }
            }
            catch { /* never propagate */ }
        }

        // ---------------------------------------------------------------- helpers

        private static void AppendException(StringBuilder sb, Exception? ex, int depth)
        {
            if (ex == null || depth > 10) return;

            var pad = new string(' ', depth * 2);
            sb.AppendLine($"{pad}Type    : {ex.GetType().FullName}");
            sb.AppendLine($"{pad}Message : {ex.Message}");
            sb.AppendLine($"{pad}HResult : 0x{ex.HResult:X8}");

            if (!string.IsNullOrEmpty(ex.StackTrace))
            {
                foreach (var line in ex.StackTrace.Split(new[] { "\r\n", "\n" },
                                                          StringSplitOptions.None))
                {
                    if (line.Length > 0)
                        sb.AppendLine($"{pad}  {line.TrimStart()}");
                }
            }

            if (ex.InnerException != null)
            {
                sb.AppendLine($"{pad}--- InnerException ({ex.InnerException.GetType().Name}) ---");
                AppendException(sb, ex.InnerException, depth + 1);
            }
        }
    }
}
