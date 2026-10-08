using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Dashboard.SetupHelper;
using WixToolset.Dtf.WindowsInstaller;

namespace Dashboard.SafetyActions
{
    public static class SafetyActions
    {
        [CustomAction]
        public static ActionResult ValidateDashboardIsolation(Session session)
        {
            try
            {
                string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Dashboard");
                string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Dashboard");
                string menu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Dashboard");
                var expected = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    { "INSTALLFOLDER", root }, { "PROGRAMDATAFOLDER", data }, { "STARTMENUFOLDER", menu }
                };
                var directories = new Dictionary<string, Tuple<string, string>>();
                using (var view = session.Database.OpenView("SELECT `Directory`, `Directory_Parent`, `DefaultDir` FROM `Directory`"))
                {
                    view.Execute();
                    Record record;
                    while ((record = view.Fetch()) != null)
                        using (record) directories[record.GetString(1)] =
                            Tuple.Create(record.GetString(2), record.GetString(3));
                }
                bool added;
                do
                {
                    added = false;
                    foreach (var pair in directories)
                    {
                        if (expected.ContainsKey(pair.Key) || !expected.ContainsKey(pair.Value.Item1)) continue;
                        string name = pair.Value.Item2.Split(':')[0];
                        if (name.Contains("|")) name = name.Substring(name.IndexOf('|') + 1);
                        expected[pair.Key] = name == "." ? expected[pair.Value.Item1] :
                            Path.Combine(expected[pair.Value.Item1], name);
                        added = true;
                    }
                } while (added);

                foreach (var pair in expected)
                    InstallerFileSafety.RequireSamePath(session.GetTargetPath(pair.Key), pair.Value);

                // Check every harvested file too: an existing file can itself be
                // a symlink even when its parent directories are ordinary folders.
                using (var view = session.Database.OpenView(
                    "SELECT `File`.`FileName`, `Component`.`Directory_` FROM `File`, `Component` WHERE `File`.`Component_` = `Component`.`Component`"))
                {
                    view.Execute();
                    Record record;
                    while ((record = view.Fetch()) != null)
                    {
                        using (record)
                        {
                            string directory = record.GetString(2);
                            if (!expected.ContainsKey(directory))
                                throw new IOException("An MSI payload file is outside the Dashboard directories.");
                            string name = record.GetString(1);
                            if (name.Contains("|")) name = name.Substring(name.IndexOf('|') + 1);
                            InstallerFileSafety.EnsureNoReparsePoints(Path.Combine(expected[directory], name));
                        }
                    }
                }

                // Removal must also work after IIS or ANCM have been removed.
                string config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "System32", "inetsrv", "config", "applicationHost.config");
                InstallerFileSafety.EnsureNoReparsePoints(config);
                XDocument? iis = File.Exists(config) ? XDocument.Load(config) : null;
                if (iis != null) IisIsolation.Validate(iis, Path.Combine(root, "WebApp"));
                if (!session["REMOVE"].Split(',').Any(x => string.Equals(x, "ALL", StringComparison.OrdinalIgnoreCase)))
                {
                    // Resolve before IIS is re-authored, so direct repair preserves
                    // the currently installed port instead of silently resetting it.
                    string portText = session["DASHBOARD_PORT"];
                    if (string.IsNullOrWhiteSpace(portText) && iis != null)
                    {
                        var site = iis.Descendants("site").FirstOrDefault(x =>
                            string.Equals((string)x.Attribute("name"), "Dashboard", StringComparison.OrdinalIgnoreCase));
                        var binding = site?.Descendants("binding").FirstOrDefault(x => (string)x.Attribute("protocol") == "http");
                        string bindingInfo = (string?)binding?.Attribute("bindingInformation") ?? "";
                        int lastColon = bindingInfo.LastIndexOf(':');
                        int portColon = lastColon > 0 ? bindingInfo.LastIndexOf(':', lastColon - 1) : -1;
                        if (portColon >= 0) portText = bindingInfo.Substring(portColon + 1, lastColon - portColon - 1);
                    }
                    if (string.IsNullOrWhiteSpace(portText)) portText = "5000";
                    int port;
                    if (!int.TryParse(portText, out port) || port < 1 || port > 65535)
                        throw new IOException("Dashboard port must be an integer between 1 and 65535.");
                    session["DASHBOARD_PORT"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                session["DASHBOARD_TRANSACTION_ID"] = Guid.NewGuid().ToString("N");
                session.Log("Dashboard isolation validated before InstallInitialize; no machine changes made.");
                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                session.Log("Dashboard isolation refused: " + ex.Message);
                using (var message = new Record(1))
                {
                    message.SetString(0, "Dashboard setup stopped to protect other applications. " + ex.Message);
                    session.Message(InstallMessage.Error, message);
                }
                return ActionResult.Failure;
            }
        }

    }
}

