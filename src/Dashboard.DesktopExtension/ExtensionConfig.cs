using Newtonsoft.Json;
using System;
using System.IO;

namespace LFPortal.DesktopExtension
{
    /// <summary>
    /// Persisted configuration for the Dashboard Desktop Extension.
    /// Stored at <c>%ProgramData%\Dashboard\extension.config.json</c>.
    /// Falls back to the legacy <c>%ProgramData%\LFPortal\extension.config.json</c>
    /// path for backward compatibility with earlier installations.
    /// </summary>
    internal sealed class ExtensionConfig
    {
        private static readonly string ProgramData =
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        /// <summary>Primary config file path (current product name).</summary>
        internal static readonly string ConfigPath =
            Path.Combine(ProgramData, "Dashboard", "extension.config.json");

        /// <summary>Legacy config file path (kept for backward compatibility).</summary>
        private static readonly string LegacyConfigPath =
            Path.Combine(ProgramData, "LFPortal", "extension.config.json");

        /// <summary>URL of the Dashboard portal to open when the button is clicked.</summary>
        [JsonProperty("portalUrl")]
        public string PortalUrl { get; set; } = "http://localhost:5000";

        /// <summary>Label shown on the Laserfiche toolbar button.</summary>
        [JsonProperty("buttonLabel")]
        public string ButtonLabel { get; set; } = "Dashboard";

        /// <summary>
        /// Optional: absolute path to a 16×16 or 32×32 .ico file used as the toolbar icon.
        /// Leave empty to use the Laserfiche client default icon.
        /// </summary>
        [JsonProperty("iconPath")]
        public string IconPath { get; set; } = string.Empty;

        // ------------------------------------------------------------------ //
        // Persistence                                                          //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Loads the configuration from disk. Returns a default configuration when
        /// no file exists. Reads from the primary Dashboard path; if absent, falls
        /// back to the legacy LFPortal path.
        /// </summary>
        public static ExtensionConfig Load()
        {
            // Primary path
            if (File.Exists(ConfigPath))
                return ReadFile(ConfigPath);

            // Backward-compat fallback
            if (File.Exists(LegacyConfigPath))
                return ReadFile(LegacyConfigPath);

            return new ExtensionConfig();
        }

        /// <summary>Writes the configuration to the primary Dashboard path.</summary>
        public void Save()
        {
            var dir = Path.GetDirectoryName(ConfigPath)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(ConfigPath,
                JsonConvert.SerializeObject(this, Formatting.Indented));
        }

        private static ExtensionConfig ReadFile(string path)
        {
            try
            {
                var json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<ExtensionConfig>(json)
                    ?? new ExtensionConfig();
            }
            catch
            {
                return new ExtensionConfig();
            }
        }
    }
}
