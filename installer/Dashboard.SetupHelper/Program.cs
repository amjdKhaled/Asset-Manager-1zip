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
// Exit codes: 0 = success, 1 = error.
// All diagnostic output goes to stdout/stderr (captured in MSI log).
//
// IMPORTANT: No passwords or secrets are ever passed as arguments.

using System;
using System.Collections.Generic;

namespace Dashboard.SetupHelper
{
    static class Program
    {
        static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.Error.WriteLine("Usage: Dashboard.SetupHelper.exe <command> [--key value ...]");
                Console.Error.WriteLine("Commands: --write-config, --deploy-webclient, --remove-webclient, --rollback-webclient");
                return 1;
            }

            string command = args[0].ToLowerInvariant();
            Dictionary<string, string> opts = ParseArgs(args, startIndex: 1);

            try
            {
                switch (command)
                {
                    case "--write-config":
                        return WriteConfigAction.Execute(opts);

                    case "--deploy-webclient":
                        return WebClientAction.Deploy(opts);

                    case "--remove-webclient":
                        return WebClientAction.Remove(opts);

                    case "--rollback-webclient":
                        return WebClientAction.Rollback(opts);

                    default:
                        Console.Error.WriteLine($"Unknown command: {command}");
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
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
