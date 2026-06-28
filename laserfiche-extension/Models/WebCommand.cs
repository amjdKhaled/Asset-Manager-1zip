using Newtonsoft.Json;
using System.Collections.Generic;

namespace LaserficheAIExtension.Models
{
    /// <summary>
    /// Commands sent from the web app back to the Laserfiche extension.
    /// </summary>
    public class WebCommand
    {
        [JsonProperty("command")]
        public string Command { get; set; }

        [JsonProperty("payload")]
        public Dictionary<string, object> Payload { get; set; } = new Dictionary<string, object>();

        [JsonProperty("requestId")]
        public string RequestId { get; set; }

        [JsonProperty("timestamp")]
        public long Timestamp { get; set; }

        public static WebCommand FromJson(string json) => JsonConvert.DeserializeObject<WebCommand>(json);
    }

    public static class WebCommandTypes
    {
        public const string OpenDocument = "OpenDocument";
        public const string UpdateMetadata = "UpdateMetadata";
        public const string MoveDocument = "MoveDocument";
        public const string RefreshMetadata = "RefreshMetadata";
        public const string DownloadDocument = "DownloadDocument";
        public const string RunOcr = "RunOcr";
        public const string RunAi = "RunAi";
        public const string SearchRepository = "SearchRepository";
        public const string GetDocumentFields = "GetDocumentFields";
        public const string Ping = "Ping";
        public const string SetTheme = "SetTheme";
    }
}
