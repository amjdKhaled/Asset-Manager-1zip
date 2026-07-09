using LaserficheAIExtension.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LaserficheAIExtension.SDK
{
    /// <summary>
    /// Abstracts all Laserfiche SDK interactions for testability and clean separation.
    /// </summary>
    public interface ILaserficheSdkWrapper : System.IDisposable
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
        bool IsConnected { get; }
    }
}
