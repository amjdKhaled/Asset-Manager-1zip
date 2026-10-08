using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace Dashboard.SetupHelper
{
    internal static class WebClientAction
    {
        public static int Deploy(Dictionary<string, string> opts)
        {
            string root = InstallerFileSafety.FullPath(PathUtil.SanitizeDir(Opt(opts, "path")));
            InstallerFileSafety.EnsureNoReparsePoints(root);
            string page = Path.Combine(root, "Browse.aspx");
            string js = Path.Combine(root, "assets", "custom", "lf-dashboard-button.js");
            InstallerFileSafety.EnsureNoReparsePoints(page);
            InstallerFileSafety.EnsureNoReparsePoints(js);
            if (!File.Exists(page)) throw new FileNotFoundException("Browse.aspx was not found.", page);

            Uri url;
            if (!Uri.TryCreate(Opt(opts, "url"), UriKind.Absolute, out url) ||
                (url.Scheme != "http" && url.Scheme != "https") || !string.IsNullOrEmpty(url.UserInfo))
                throw new IOException("A valid HTTP(S) Dashboard URL without credentials is required.");
            string source = FindSourceJs();
            string content = File.ReadAllText(source, Encoding.UTF8);
            string escapedUrl = url.AbsoluteUri.TrimEnd('/').Replace("\\", "\\\\").Replace("'", "\\'");
            string patched = Regex.Replace(content, @"(var DASHBOARD_BASE_URL\s*=\s*)'[^']*'",
                match => match.Groups[1].Value + "'" + escapedUrl + "'");
            if (patched == content && !content.Contains("var DASHBOARD_BASE_URL"))
                throw new IOException("The Dashboard URL placeholder was not found.");

            byte[] pageBefore = File.ReadAllBytes(page);
            byte[] pageAfter = WebClientText.InsertTag(pageBefore);
            byte[] jsBefore = File.Exists(js) ? File.ReadAllBytes(js) : null;
            byte[] jsAfter = new UTF8Encoding(false).GetBytes(patched);
            // Do not overwrite an unrelated pre-existing asset with the same name.
            if (jsBefore != null && !Encoding.UTF8.GetString(jsBefore).Contains("DASHBOARD_BASE_URL"))
                throw new IOException("The destination JavaScript is not recognized as a Dashboard asset.");

            string journal = JournalPath(opts);
            // Save exact preimages and postimages for THIS MSI transaction only.
            using (var stream = new FileStream(journal, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write("DashboardWebClientTransaction-v1");
                writer.Write(root);
                WriteBytes(writer, pageBefore);
                WriteBytes(writer, pageAfter);
                WriteBytes(writer, jsBefore);
                WriteBytes(writer, jsAfter);
            }

            // Recheck before every mutation. Atomic replacement also avoids writing
            // through hard links. No Laserfiche directory is ever deleted.
            if (!File.ReadAllBytes(page).SequenceEqual(pageBefore))
                throw new IOException("Browse.aspx changed during setup; deployment was refused.");
            Directory.CreateDirectory(Path.GetDirectoryName(js));
            InstallerFileSafety.EnsureNoReparsePoints(js);
            byte[] currentJs = File.Exists(js) ? File.ReadAllBytes(js) : null;
            if (!Equal(currentJs, jsBefore)) throw new IOException("The JavaScript changed during setup.");
            InstallerFileSafety.WriteBytesAtomic(js, jsAfter);
            InstallerFileSafety.WriteBytesAtomic(page, pageAfter);
            SetupLog.Info("Dashboard Web Client button deployed with a transaction-specific rollback journal.");
            return 0;
        }

        public static int Remove(Dictionary<string, string> opts)
        {
            // Uninstall deliberately makes NO filesystem changes in Laserfiche.
            // The optional Web Client integration can be managed independently.
            SetupLog.Info("Preserved all Laserfiche Web Client files during Dashboard removal.");
            return 0;
        }

        public static int Rollback(Dictionary<string, string> opts)
        {
            string journal = JournalPath(opts);
            if (!File.Exists(journal)) return 0; // Deploy never reached its mutation stage.
            string root = InstallerFileSafety.FullPath(PathUtil.SanitizeDir(Opt(opts, "path")));
            InstallerFileSafety.EnsureNoReparsePoints(root);
            using (var stream = File.OpenRead(journal))
            using (var reader = new BinaryReader(stream))
            {
                if (reader.ReadString() != "DashboardWebClientTransaction-v1" ||
                    !string.Equals(reader.ReadString(), root, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Rollback journal does not belong to this target.");
                byte[] pageBefore = ReadBytes(reader);
                byte[] pageAfter = ReadBytes(reader);
                byte[] jsBefore = ReadBytes(reader);
                byte[] jsAfter = ReadBytes(reader);
                RestoreIfUnchanged(Path.Combine(root, "Browse.aspx"), pageBefore, pageAfter);
                RestoreIfUnchanged(Path.Combine(root, "assets", "custom", "lf-dashboard-button.js"), jsBefore, jsAfter);
            }
            File.Delete(journal);
            return 0;
        }

        public static int Commit(Dictionary<string, string> opts)
        {
            string journal = JournalPath(opts);
            if (File.Exists(journal)) File.Delete(journal);
            return 0;
        }

        private static void RestoreIfUnchanged(string path, byte[] before, byte[] after)
        {
            InstallerFileSafety.EnsureNoReparsePoints(path);
            byte[] current = File.Exists(path) ? File.ReadAllBytes(path) : null;
            if (!Equal(current, after))
            {
                SetupLog.Warn("Preserved a file that changed after Dashboard deployment: " + path);
                return;
            }
            if (before == null) File.Delete(path); // Only the exact asset created by this transaction.
            else InstallerFileSafety.WriteBytesAtomic(path, before);
        }

        private static bool Equal(byte[] left, byte[] right)
        {
            return left == null ? right == null : right != null && left.SequenceEqual(right);
        }

        private static string JournalPath(Dictionary<string, string> opts)
        {
            Guid transaction;
            if (!Guid.TryParseExact(Opt(opts, "transaction"), "N", out transaction))
                throw new IOException("A transaction identifier is required; rollback never guesses a backup.");
            string directory = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData), "Dashboard", "installer-transactions");
            InstallerFileSafety.EnsureNoReparsePoints(directory);
            Directory.CreateDirectory(directory);
            // Journal contents govern rollback of another product's page. They must
            // not be editable by the Dashboard worker identity or normal users.
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
                security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null),
                    FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None, AccessControlType.Allow));
            Directory.SetAccessControl(directory, security);
            string path = Path.Combine(directory, transaction.ToString("N") + ".bin");
            InstallerFileSafety.EnsureNoReparsePoints(path);
            return path;
        }

        private static void WriteBytes(BinaryWriter writer, byte[] bytes)
        {
            writer.Write(bytes == null ? -1 : bytes.Length);
            if (bytes != null) writer.Write(bytes);
        }

        private static byte[] ReadBytes(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length == -1) return null;
            if (length < 0 || length > 32 * 1024 * 1024) throw new IOException("Invalid journal length.");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            return bytes;
        }

        private static string FindSourceJs()
        {
            string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            string installRoot = Path.GetDirectoryName(exeDir) ?? "";
            string path = Path.Combine(installRoot, "WebApp", "wwwroot", "js", "lf-webclient-button.js");
            InstallerFileSafety.EnsureNoReparsePoints(path);
            if (!File.Exists(path)) throw new FileNotFoundException("Dashboard button source is missing.", path);
            return path;
        }

        private static string Opt(Dictionary<string, string> opts, string key)
        {
            string value;
            return opts.TryGetValue(key, out value) ? value : "";
        }
    }
}
