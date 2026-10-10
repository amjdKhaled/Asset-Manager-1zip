using System.Diagnostics;
using System.Text;
using System.Xml.Linq;
using Dashboard.SetupHelper;
using Dashboard.SafetyActions;

string fixture = Path.Combine(Path.GetTempPath(), "DashboardSafetyTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);
int passed = 0;
void Check(string name, Action test) {
    test();
    passed++;
    Console.WriteLine("PASS " + name);
}
void Require(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
void Refused(Action action) {
    try { action(); } catch (IOException) { return; }
    throw new Exception("Unsafe operation was accepted.");
}
void Junction(string link, string target) {
    using var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + target + "\"")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true })!;
    p.WaitForExit();
    if (p.ExitCode != 0) throw new Exception("Junction fixture failed: " + p.StandardError.ReadToEnd());
}
try {
    Check("cleanup preserves unknown files, subdirectories, logs and an outside sentinel", () => {
        string root = Path.Combine(fixture, "Dashboard");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "credentials"));
        Directory.CreateDirectory(Path.Combine(root, "nested"));
        Directory.CreateDirectory(Path.Combine(root, "logs"));
        foreach (string owned in RemoveDataAction.OwnedFiles) File.WriteAllText(Path.Combine(root, owned), "fixture");
        File.WriteAllText(Path.Combine(root, "unknown.txt"), "keep");
        File.WriteAllText(Path.Combine(root, "nested", "document.pdf"), "keep");
        File.WriteAllText(Path.Combine(root, "logs", "SetupHelper.log"), "keep");
        File.WriteAllText(Path.Combine(root, "credentials", "unknown.dpapi"), "keep");
        string outside = Path.Combine(fixture, "Laserfiche-document.txt");
        File.WriteAllText(outside, "Laserfiche sentinel");
        Require(RemoveDataAction.RemoveKnownFiles(root) == 0);
        foreach (string owned in RemoveDataAction.OwnedFiles) Require(!File.Exists(Path.Combine(root, owned)));
        foreach (string other in new[] { "unknown.txt", Path.Combine("nested", "document.pdf"),
            Path.Combine("logs", "SetupHelper.log"), Path.Combine("credentials", "unknown.dpapi") })
            Require(File.ReadAllText(Path.Combine(root, other)) == "keep");
        Require(File.ReadAllText(outside) == "Laserfiche sentinel");
    });
    Check("path containment rejects another application's directory", () =>
        Refused(() => InstallerFileSafety.RequireSamePath(Path.Combine(fixture, "Laserfiche"), Path.Combine(fixture, "Dashboard"))));
    Check("hard-linked files cannot redirect writes into a Laserfiche file", () => {
        string target = Path.Combine(fixture, "hardlink-outside.txt");
        string link = Path.Combine(fixture, "hardlink-dashboard.txt");
        File.WriteAllText(target, "Laserfiche hard-link sentinel");
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c mklink /H \"" + link + "\" \"" + target + "\"")
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true })!;
        process.WaitForExit();
        Require(process.ExitCode == 0);
        Refused(() => InstallerFileSafety.EnsureNoReparsePoints(link));
        Require(File.ReadAllText(target) == "Laserfiche hard-link sentinel");
        File.Delete(link);
    });
    Check("reparse point and ancestor junction are refused without changing outside files", () => {
        string outside = Path.Combine(fixture, "Laserfiche");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "laserfiche.config.json"), "keep");
        string link = Path.Combine(fixture, "DashboardLink");
        Junction(link, outside);
        try {
            Refused(() => InstallerFileSafety.EnsureNoReparsePoints(Path.Combine(link, "missing", "file.json")));
            Require(RemoveDataAction.RemoveKnownFiles(link) == 1);
            Require(File.ReadAllText(Path.Combine(outside, "laserfiche.config.json")) == "keep");
        } finally { Directory.Delete(link); }
    });
    Check("cleanup validates credential junction before deleting any settings", () => {
        string root = Path.Combine(fixture, "DashboardLinkedCredentials");
        Directory.CreateDirectory(root);
        string config = Path.Combine(root, "extension.config.json");
        File.WriteAllText(config, "keep");
        string link = Path.Combine(root, "credentials");
        Junction(link, Path.Combine(fixture, "Laserfiche"));
        try {
            Require(RemoveDataAction.RemoveKnownFiles(root) == 1);
            Require(File.ReadAllText(config) == "keep");
        } finally { Directory.Delete(link); }
    });
    foreach (Encoding encoding in new Encoding[] { new UTF8Encoding(false), new UTF8Encoding(true), Encoding.Unicode, Encoding.BigEndianUnicode }) {
        Check("Browse.aspx preserves encoding, BOM, Arabic and other content: " + encoding.WebName + "/" + encoding.GetPreamble().Length, () => {
            const string text = "<head>\r\n<script>other()</script></head><body>وثيقة</body>\r\n";
            byte[] original = encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();
            byte[] inserted = WebClientText.InsertTag(original);
            byte[] expected = encoding.GetPreamble().Concat(encoding.GetBytes(
                text.Replace("</head>", WebClientText.Tag + "</head>"))).ToArray();
            Require(inserted.SequenceEqual(expected));
            Require(WebClientText.InsertTag(inserted).SequenceEqual(inserted));
        });
    }
    Check("unrecognized page anchor refuses insertion", () =>
        Refused(() => WebClientText.InsertTag(Encoding.UTF8.GetBytes("<body>keep</body>"))));
    Check("unexpected preexisting script tag refuses rewriting", () =>
        Refused(() => WebClientText.InsertTag(Encoding.UTF8.GetBytes("<head><script src='lf-dashboard-button.js'></script></head>"))));
    string web = Path.Combine(fixture, "WebApp");
    Directory.CreateDirectory(web);
    File.WriteAllText(Path.Combine(web, "LFPortal.Web.dll"), "recognized payload fixture");
    string escaped = System.Security.SecurityElement.Escape(web)!;
    string ownedSite = "<site name='Dashboard'><application path='/' applicationPool='Dashboard'><virtualDirectory path='/' physicalPath='" + escaped + "'/></application></site>";
    XDocument Config(string sites, string pool = "") => XDocument.Parse("<configuration><system.applicationHost><sites>" + sites +
        "</sites><applicationPools>" + pool + "</applicationPools></system.applicationHost></configuration>");
    Check("dedicated Dashboard IIS resources are accepted", () => IisIsolation.Validate(Config(ownedSite, "<add name='Dashboard'/>"), web));
    Check("unrelated IIS sites are left alone", () => IisIsolation.Validate(Config("<site name='Laserfiche'><application path='/' applicationPool='Laserfiche'/></site>"), web));
    Check("shared Dashboard app pool is refused", () => Refused(() => IisIsolation.Validate(Config(ownedSite +
        "<site name='Laserfiche'><application path='/' applicationPool='Dashboard'/></site>"), web)));
    Check("foreign site with colliding Dashboard name is refused", () => Refused(() =>
        IisIsolation.Validate(Config(ownedSite.Replace(escaped, System.Security.SecurityElement.Escape(fixture))), web)));
    Check("extra application in Dashboard site is refused", () => Refused(() => IisIsolation.Validate(Config(
        ownedSite.Replace("</site>", "<application path='/Laserfiche' applicationPool='Laserfiche'/></site>")), web)));
    Check("unowned Dashboard app pool is refused", () => Refused(() =>
        IisIsolation.Validate(Config("", "<add name='Dashboard'/>"), Path.Combine(fixture, "Unknown"))));
    Console.WriteLine(passed + " installer safety checks passed.");
    return 0;
} finally {
    // Only the isolated, randomly named test fixture after junctions are unlinked.
    Directory.Delete(fixture, true);
}

