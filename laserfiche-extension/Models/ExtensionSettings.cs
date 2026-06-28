using Newtonsoft.Json;
using System;
using System.IO;

namespace LaserficheAIExtension.Models
{
    /// <summary>
    /// Persisted settings for the AI extension popup.
    /// </summary>
    public class ExtensionSettings
    {
        [JsonProperty("windowWidth")]
        public double WindowWidth { get; set; } = 1200;

        [JsonProperty("windowHeight")]
        public double WindowHeight { get; set; } = 800;

        [JsonProperty("windowLeft")]
        public double WindowLeft { get; set; } = 100;

        [JsonProperty("windowTop")]
        public double WindowTop { get; set; } = 100;

        [JsonProperty("isMaximized")]
        public bool IsMaximized { get; set; } = false;

        [JsonProperty("isMinimized")]
        public bool IsMinimized { get; set; } = false;

        [JsonProperty("darkMode")]
        public bool DarkMode { get; set; } = false;

        [JsonProperty("serverUrl")]
        public string ServerUrl { get; set; } = "http://localhost:5000";

        [JsonProperty("autoConnect")]
        public bool AutoConnect { get; set; } = true;

        [JsonProperty("reconnectIntervalMs")]
        public int ReconnectIntervalMs { get; set; } = 3000;

        [JsonProperty("sendSelectionOnChange")]
        public bool SendSelectionOnChange { get; set; } = true;

        [JsonProperty("laserficheServer")]
        public string LaserficheServer { get; set; } = "";

        [JsonProperty("laserficheRepository")]
        public string LaserficheRepository { get; set; } = "";

        [JsonProperty("laserficheUsername")]
        public string LaserficheUsername { get; set; } = "";

        [JsonProperty("laserfichePassword")]
        public string LaserfichePassword { get; set; } = "";

        [JsonProperty("lastUpdated")]
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LaserficheAIExtension",
            "settings.json");

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                LastUpdated = DateTime.UtcNow;
                File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }

        public static ExtensionSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    return JsonConvert.DeserializeObject<ExtensionSettings>(json) ?? new ExtensionSettings();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
            }
            return new ExtensionSettings();
        }
    }
}
