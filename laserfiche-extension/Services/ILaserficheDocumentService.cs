using LaserficheAIExtension.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LaserficheAIExtension.Services
{
    /// <summary>
    /// Service for interacting with Laserfiche documents.
    /// </summary>
    public interface ILaserficheDocumentService
    {
        Task<DocumentContext> GetDocumentContextAsync(int entryId);
        Task<Dictionary<string, object>> GetDocumentMetadataAsync(int entryId);
        Task<bool> OpenDocumentAsync(int entryId);
        Task<bool> UpdateMetadataAsync(int entryId, Dictionary<string, object> metadata);
        Task<bool> MoveDocumentAsync(int entryId, string destinationPath);
        Task<byte[]> DownloadDocumentAsync(int entryId);
        Task<string> RunOcrAsync(int entryId);
        Task<string> GetDocumentPathAsync(int entryId);
        Task<string> GetRepositoryNameAsync();
    }
}
