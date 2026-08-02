// RuntimeLog.cs
// BA runtime diagnostic log proving process / BA / WizardForm identity.
//
// Purpose: diagnose "second wizard window" reports by making every UI
// lifecycle event attributable to a specific process, BA instance, and
// form instance:
//
//   [ts] PID=1234 START=... BA=<guid> FORM=<guid|-> EVENT=<name> <detail>
//
// Written to %ProgramData%\Dashboard\Logs\BA-runtime.log (appended, never
// truncated, so multiple processes interleave and remain distinguishable
// by PID).  Mirrors StartupLogger's design constraints: no static ctor,
// swallow every exception, System.IO only.

using System;
using System.IO;
using System.Text;

namespace Dashboard.BA
{
    internal static class RuntimeLog
    {
        private static readonly object _lock = new object();

        // One GUID per loaded BA assembly instance (i.e. per process, since
        // Burn loads the BA once per process).
        internal static readonly string BaInstanceId = Guid.NewGuid().ToString("N");

        private static string _path = "";
        private static bool _ready = false;

        private static void EnsureReady()
        {
            if (_ready) return;
            _ready = true;
            try
            {
                var programData = Environment.GetFolderPath(
                    Environment.SpecialFolder.CommonApplicationData);
                var dir = Path.Combine(programData, "Dashboard", "Logs");
                Directory.CreateDirectory(dir);
                _path = Path.Combine(dir, "BA-runtime.log");
            }
            catch { _path = ""; }
        }

        /// <summary>Logs an event with no form association.</summary>
        public static void Log(string evt, string detail = "") => Log(null, evt, detail);

        /// <summary>Logs an event attributed to a specific WizardForm instance GUID.</summary>
        public static void Log(string? formId, string evt, string detail = "")
        {
            try
            {
                EnsureReady();
                if (_path.Length == 0) return;

                var p = System.Diagnostics.Process.GetCurrentProcess();
                var sb = new StringBuilder();
                sb.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append("] ");
                sb.Append("PID=").Append(p.Id).Append(' ');
                try { sb.Append("START=").Append(p.StartTime.ToString("HH:mm:ss.fff")).Append(' '); }
                catch { /* access denied is possible; skip */ }
                sb.Append("BA=").Append(BaInstanceId).Append(' ');
                sb.Append("FORM=").Append(string.IsNullOrEmpty(formId) ? "-" : formId).Append(' ');
                sb.Append("EVENT=").Append(evt);
                if (!string.IsNullOrEmpty(detail)) sb.Append(' ').Append(detail);
                sb.Append(Environment.NewLine);

                lock (_lock) { File.AppendAllText(_path, sb.ToString(), Encoding.UTF8); }
            }
            catch { /* never propagate */ }
        }
    }
}
