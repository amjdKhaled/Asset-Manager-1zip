// Program.cs  --  Dashboard.SetupHelper
// Entry point for the MSI custom action helper EXE.
//
// This console application is called by MSI ExeCommand custom actions with
// elevated privileges (Impersonate="no").  It performs operations that require
// write access to %ProgramData% and the Laserfiche Web Client directory.
//
// COMMAND SYNTAX (all arguments are required unless documented as optional):
//
//   --write-config
//       --url          <dashboard-url>
//       --lf-api       <laserfiche-api-url>
//       --repo-id      <repository-id>
//       --display-name <display-name>         (optional; defaults to repo-id)
//       --port         <tcp-port>             (optional; default 5000)
//       --webapp-path  <webappfolder-path>    (optional; patches appsettings.json Urls)
//
//   --deploy-webclient
//       --url   <dashboard-url>
//       --path  <web-client-physical-path>
//
//   --remove-webclient
//       --path  <web-client-physical-path>
//
//   --rollback-webclient
//       --path  <web-client-physical-path>
//
//   --prepare-tls
//       --lf-api           <laserfiche-api-url>
//       --trust-selfsigned <0|1>   (operator consent from the wizard checkbox)
//       Certificate/TLS preparation stage: inspects the certificate the LF
//       API endpoint presents, and (only for a valid self-signed certificate
//       matching the host, with consent) installs the PUBLIC certificate
//       into LocalMachine\Root. Never bypasses TLS validation. Always
//       exits 0 (best-effort; all outcomes logged with [TLS SETUP]).
//
// Exit codes: 0 = success, 1 = error.
// All diagnostic output goes to stdout/stderr (captured in MSI log).
//
// IMPORTANT: No passwords or secrets are ever passed as arguments.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Dashboard.SetupHelper
{
    static class Program
    {
        static int Main(string[] args)
        {
            // Persistent diagnostics: %ProgramData%\Dashboard\Logs\SetupHelper.log.
            // The MSI log only shows "returned actual error code 1"; this log
            // captures the full command, environment, and any exception chain.
            SetupLog.Init();
            SetupLog.Info("============================================================");
            SetupLog.Info($"Invoked: {string.Join(" ", args.Select(a => a.Length == 0 ? "<EMPTY>" : a))}");
            SetupLog.Info($"Process bitness: {(Environment.Is64BitProcess ? "x64" : "x86")}");
            try { SetupLog.Info($"Current directory: {Environment.CurrentDirectory}"); } catch { }

            int rc;
            if (args.Length == 0)
            {
                Console.Error.WriteLine("Usage: Dashboard.SetupHelper.exe <command> [--key value ...]");
                Console.Error.WriteLine("Commands: --write-config, --deploy-webclient, --remove-webclient, --rollback-webclient");
                SetupLog.Error("No command supplied.");
                SetupLog.Info("Final exit code: 1");
                return 1;
            }

            string command = args[0].ToLowerInvariant();
            Dictionary<string, string> opts = ParseArgs(args, startIndex: 1);

            try
            {
                switch (command)
                {
                    case "--write-config":
                        rc = WriteConfigAction.Execute(opts);
                        break;

                    case "--deploy-webclient":
                        rc = WebClientAction.Deploy(opts);
                        break;

                    case "--remove-webclient":
                        rc = WebClientAction.Remove(opts);
                        break;

                    case "--rollback-webclient":
                        rc = WebClientAction.Rollback(opts);
                        break;

                    case "--prepare-tls":
                        rc = TlsSetupAction.Execute(opts);
                        break;

                    default:
                        Console.Error.WriteLine($"Unknown command: {command}");
                        SetupLog.Error($"Unknown command: {command}");
                        rc = 1;
                        break;
                }
            }
            catch (Exception ex)
            {
                // Log the COMPLETE exception chain before returning non-zero.
                SetupLog.Error(ex);
                Console.Error.WriteLine($"[ERROR] {ex.GetType().Name}: {ex.Message}");
                rc = 1;
            }

            SetupLog.Info($"Final exit code: {rc}");
            return rc;
        }

        // Parses "--key value" pairs from args starting at startIndex.
        // Skips lone flags that are not followed by a value.
        internal static Dictionary<string, string> ParseArgs(string[] args, int startIndex)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int i = startIndex;
            while (i < args.Length)
            {
                string token = args[i];
                if (token.StartsWith("--") && i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                {
                    string key = token.Substring(2); // strip leading "--"
                    string val = args[i + 1];
                    result[key] = val;
                    i += 2;
                }
                else
                {
                    i++;
                }
            }
            return result;
        }
    }
}
