// InstallConfig.cs
// Data models populated by the setup wizard and passed to the WiX Burn engine.
// All string fields are plain ASCII-safe; no Unicode escapes needed.

namespace Dashboard.BA
{
    // Wizard-collected installation settings.
    // An instance is built up across wizard pages, then written to Bundle
    // Variables in DashboardBA.StartInstall().  Burn passes them to the MSI
    // as MsiProperty elements.
    internal sealed class InstallConfig
    {
        // Full URL users' browsers use to reach the Dashboard server.
        // Suggested default: http://COMPUTERNAME:5000  (never localhost in production).
        public string DashboardUrl { get; set; } = "";

        // Laserfiche Repository API endpoint.
        // Example: https://lf-server.company.local/LFRepositoryAPI
        public string LaserficheApiUrl { get; set; } = "";

        // Laserfiche Repository API version: "Auto" (default — the web app
        // probes v2 then v1 at runtime and remembers the result), "v1", or "v2".
        public string LaserficheApiVersion { get; set; } = "Auto";

        // NOTE: Repository ID and Display Name were intentionally REMOVED.
        // The repository is runtime session context (passed by the Desktop /
        // Web Client via ?repository=, or chosen at login) — never a permanent
        // installation setting.  The installer configures infrastructure only.

        // Physical path to the Laserfiche Web Client (Web Files) directory.
        // Empty string means: skip web client integration.
        // Example: C:\Program Files\Laserfiche\Web Access\Web Files
        public string LFWebClientPath { get; set; } = "";

        // True: register the Desktop Client Extension after installation.
        public bool InstallDesktopButton { get; set; } = true;

        // True: deploy lf-dashboard-button.js and patch Browse.aspx.
        public bool InstallWebButton { get; set; } = false;

        // IIS HTTP binding port.  Must match the port in DashboardUrl.
        public string DashboardPort { get; set; } = "5000";

        // True: the operator consented (wizard checkbox) to trusting the
        // detected SELF-SIGNED Laserfiche API certificate on this machine
        // (public certificate into LocalMachine\Root, performed by the
        // elevated SetupHelper --prepare-tls custom action, which re-checks
        // every safety rule itself; this flag is consent only, not policy).
        public bool TrustSelfSignedCert { get; set; } = false;
    }

    // Results of the environment detection scan (page 2 of the wizard).
    internal sealed class DetectionResult
    {
        public bool   IisInstalled          { get; set; }

        // ANCM V2 (aspnetcorev2.dll) — required for IIS to host the self-contained
        // ASP.NET Core web app.  Dashboard no longer requires a globally installed
        // .NET 8 runtime; it carries its own runtime in its install folder.
        public bool   AncmInstalled         { get; set; }
        public string AncmPath              { get; set; } = "";

        public bool   WebView2Installed     { get; set; }
        public string WebView2Version       { get; set; } = "";
        public bool   DesktopClientFound    { get; set; }
        public string DesktopClientPath     { get; set; } = "";
        public bool   WebClientFound        { get; set; }
        public string WebClientPath         { get; set; } = "";

        // Pre-computed suggested Dashboard URL: http://MACHINENAME:5000
        public string SuggestedDashboardUrl { get; set; } = "";

        // Certificate-valid Laserfiche API URL detected from the IIS
        // /LFRepositoryAPI application's HTTPS binding and certificate SAN.
        // Empty when no certificate-valid host could be determined.
        public string LaserficheApiUrl { get; set; } = "";

        // Non-fatal warning about the API certificate (untrusted chain,
        // expired, no hostname match, ...) to surface in the wizard/log.
        public string LaserficheApiWarning { get; set; } = "";

        // ApiVersion found in an existing %ProgramData%\Dashboard\
        // laserfiche.config.json ("Auto", "v1", "v2"). Empty on fresh machines.
        // Used to preselect the wizard's API Version combo so an upgrade never
        // silently changes a pinned version to Auto Detect.
        public string ExistingApiVersion { get; set; } = "";

        // Certificate presented by the detected /LFRepositoryAPI HTTPS
        // binding: shown on the config page and used to decide whether the
        // "trust self-signed certificate" checkbox may be offered.
        public string LaserficheCertSubject    { get; set; } = "";
        public string LaserficheCertThumbprint { get; set; } = "";
        public bool   LaserficheCertSelfSigned { get; set; }
        public bool   LaserficheCertTrusted    { get; set; }

        // True if all components required for the Dashboard web app are present.
        // Dashboard is self-contained; only IIS + ANCM V2 are required on the machine.
        public bool AllRequiredPresent => IisInstalled && AncmInstalled;
    }
}
