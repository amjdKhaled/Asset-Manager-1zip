// StartupLogger.cs
// Self-contained startup diagnostic logger for the Dashboard managed BA.
//
// Writes to %TEMP%\LFDashboard-BA-startup.log using only System.IO.
// All public methods silently swallow every exception — the logger MUST
// never be the reason the bootstrapper application fails.
//
// Design constraints:
//   • No static constructor   (a faulting static ctor → TypeInitializationException,
//                              which is exactly what we are trying to diagnose)
//   • No DI, no WiX engine, no UI, no third-party libs
//   • Thread-safe via a simple object lock around file writes

using System;
using System.IO;
using System.Text;

namespace Dashboard.BA
{
    internal static class StartupLogger
    {
        // ---------------------------------------------------------------- state
        // No static constructor — field initialisers below cannot throw.
        private static readonly object _lock    = new object();
        private static          string _logPath = "";       // "" ⇒ disabled
        private static          bool   _ready   = false;

        // ---------------------------------------------------------------- init
        // Called lazily on first use.  Safe to call multiple times; idempotent.
        private static void EnsureReady()
        {
            if (_ready) return;
            _ready = true;                          // set first so a failure disables the logger

            try
            {
                _logPath = Path.Combine(Path.GetTempPath(), "LFDashboard-BA-startup.log");

                var sb = new StringBuilder();
                sb.AppendLine("====================================================");
                sb.AppendLine("  LFDashboard Bootstrapper Application – startup log");
                sb.AppendLine($"  {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                sb.AppendLine("----------------------------------------------------");
                try { sb.AppendLine($"  Process bit-ness : {(IntPtr.Size == 8 ? "64-bit" : "32-bit")}"); } catch { /* ignore */ }
                try { sb.AppendLine($"  CLR version      : {Environment.Version}"); } catch { /* ignore */ }
                try { sb.AppendLine($"  OS               : {Environment.OSVersion}"); } catch { /* ignore */ }
                try { sb.AppendLine($"  AppDomain base   : {AppDomain.CurrentDomain.BaseDirectory}"); } catch { /* ignore */ }
                try { sb.AppendLine($"  Temp path        : {Path.GetTempPath()}"); } catch { /* ignore */ }
                sb.AppendLine("====================================================");

                // Overwrite any previous log so we always start fresh
                lock (_lock) { File.WriteAllText(_logPath, sb.ToString(), Encoding.UTF8); }
            }
            catch
            {
                _logPath = "";   // disable — cannot write
            }
        }

        // ---------------------------------------------------------------- public API

        /// <summary>Append a plain informational line to the log.</summary>
        public static void Log(string message)
        {
            try
            {
                EnsureReady();
                if (_logPath.Length == 0) return;

                var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}";
                lock (_lock) { File.AppendAllText(_logPath, line, Encoding.UTF8); }
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
                if (_logPath.Length == 0) return;

                var sb = new StringBuilder();
                sb.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] *** EXCEPTION in {context} ***");
                AppendException(sb, ex, depth: 0);
                sb.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] *** END EXCEPTION ***");

                lock (_lock) { File.AppendAllText(_logPath, sb.ToString(), Encoding.UTF8); }
            }
            catch { /* never propagate */ }
        }

        // ---------------------------------------------------------------- helpers

        private static void AppendException(StringBuilder sb, Exception? ex, int depth)
        {
            if (ex == null || depth > 10) return;

            string pad = new string(' ', depth * 2);
            sb.AppendLine($"{pad}Type    : {ex.GetType().FullName}");
            sb.AppendLine($"{pad}Message : {ex.Message}");
            sb.AppendLine($"{pad}HResult : 0x{ex.HResult:X8}");

            if (!string.IsNullOrEmpty(ex.StackTrace))
            {
                foreach (var line in ex.StackTrace.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
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
