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

        // Laserfiche repository identifier (case-sensitive, not display name).
        // Example: Documents
        public string RepositoryId { get; set; } = "";

        // Human-readable name shown in the Dashboard UI.
        // Optional: defaults to RepositoryId when blank.
        public string DisplayName { get; set; } = "";

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
    }

    // Results of the environment detection scan (page 2 of the wizard).
    internal sealed class DetectionResult
    {
        public bool   IisInstalled          { get; set; }
        public bool   AspNetCore8Installed  { get; set; }
        public string AspNetCore8Version    { get; set; } = "";
        public bool   WebView2Installed     { get; set; }
        public string WebView2Version       { get; set; } = "";
        public bool   DesktopClientFound    { get; set; }
        public string DesktopClientPath     { get; set; } = "";
        public bool   WebClientFound        { get; set; }
        public string WebClientPath         { get; set; } = "";

        // Pre-computed suggested Dashboard URL: http://MACHINENAME:5000
        public string SuggestedDashboardUrl { get; set; } = "";

        // True if all components required for the Dashboard web app are present.
        public bool AllRequiredPresent => IisInstalled && AspNetCore8Installed;
    }
}
