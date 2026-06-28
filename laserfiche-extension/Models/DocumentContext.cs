using Newtonsoft.Json;
using System.Collections.Generic;

namespace LaserficheAIExtension.Models
{
    /// <summary>
    /// Represents the currently selected document in Laserfiche.
    /// Sent to the web app whenever selection changes.
    /// </summary>
    public class DocumentContext
    {
        [JsonProperty("entryId")]
        public int EntryId { get; set; }

        [JsonProperty("documentId")]
        public string DocumentId { get; set; }

        [JsonProperty("documentName")]
        public string DocumentName { get; set; }

        [JsonProperty("templateName")]
        public string TemplateName { get; set; }

        [JsonProperty("metadata")]
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();

        [JsonProperty("folderPath")]
        public string FolderPath { get; set; }

        [JsonProperty("repositoryName")]
        public string RepositoryName { get; set; }

        [JsonProperty("volumeName")]
        public string VolumeName { get; set; }

        [JsonProperty("pageCount")]
        public int PageCount { get; set; }

        [JsonProperty("mimeType")]
        public string MimeType { get; set; }

        [JsonProperty("createdDate")]
        public string CreatedDate { get; set; }

        [JsonProperty("modifiedDate")]
        public string ModifiedDate { get; set; }

        [JsonProperty("creator")]
        public string Creator { get; set; }

        [JsonProperty("modifier")]
        public string Modifier { get; set; }

        [JsonProperty("isElectronicDocument")]
        public bool IsElectronicDocument { get; set; }

        [JsonProperty("isRecord")]
        public bool IsRecord { get; set; }

        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.Indented);
    }
}
