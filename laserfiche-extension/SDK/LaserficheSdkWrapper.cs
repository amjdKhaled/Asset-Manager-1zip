using LaserficheAIExtension.Infrastructure.Logging;
using LaserficheAIExtension.Models;
using Laserfiche.RepositoryAccess;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LaserficheAIExtension.SDK
{
    /// <summary>
    /// Real implementation of Laserfiche SDK operations.
    /// </summary>
    public class LaserficheSdkWrapper : ILaserficheSdkWrapper
    {
        private readonly ILogger<LaserficheSdkWrapper> _logger;
        private readonly Session _session;

        public bool IsConnected => _session?.IsConnected ?? false;

        public LaserficheSdkWrapper(ILogger<LaserficheSdkWrapper> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            try
            {
                // Attempt to connect using current Laserfiche Desktop Client session
                _session = new Session();
                _session.UseExistingConnection();
                _logger.Information("Connected to Laserfiche repository: {Repo}", _session.CurrentRepository?.Name ?? "Unknown");
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Could not connect to Laserfiche via existing session. Will retry on demand.");
                _session = null;
            }
        }

        public async Task<DocumentContext> GetDocumentContextAsync(int entryId)
        {
            return await Task.Run(() =>
            {
                EnsureSession();
                var entry = new EntryInfo(_session, entryId);

                var context = new DocumentContext
                {
                    EntryId = entryId,
                    DocumentId = entryId.ToString(),
                    DocumentName = entry.Name ?? "Unknown",
                    TemplateName = entry.Template?.Name ?? "None",
                    FolderPath = GetPathFromEntry(entry),
                    RepositoryName = _session.CurrentRepository?.Name ?? "Default",
                    VolumeName = entry.Volume?.Name ?? "",
                    PageCount = entry.PageCount,
                    MimeType = entry.MimeType?.Type ?? "",
                    CreatedDate = entry.CreationTime.ToString("O"),
                    ModifiedDate = entry.LastModifiedTime.ToString("O"),
                    Creator = entry.Creator?.Name ?? "",
                    Modifier = entry.LastModifier?.Name ?? "",
                    IsElectronicDocument = entry.IsElectronicDocument,
                    IsRecord = entry.IsRecord
                };

                // Fetch template fields
                try
                {
                    foreach (FieldInfo field in entry.Fields)
                    {
                        context.Metadata[field.Name] = field.Value ?? "";
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Could not read fields for entry {EntryId}", entryId);
                }

                return context;
            });
        }

        public async Task<Dictionary<string, object>> GetDocumentMetadataAsync(int entryId)
        {
            return await Task.Run(() =>
            {
                EnsureSession();
                var entry = new EntryInfo(_session, entryId);
                var metadata = new Dictionary<string, object>();

                try
                {
                    foreach (FieldInfo field in entry.Fields)
                    {
                        metadata[field.Name] = field.Value ?? "";
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Could not read metadata for entry {EntryId}", entryId);
                }

                return metadata;
            });
        }

        public async Task<bool> OpenDocumentAsync(int entryId)
        {
            return await Task.Run(() =>
            {
                try
                {
                    EnsureSession();
                    var entry = new EntryInfo(_session, entryId);
                    entry.OpenDocument();
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to open document {EntryId}", entryId);
                    return false;
                }
            });
        }

        public async Task<bool> UpdateMetadataAsync(int entryId, Dictionary<string, object> metadata)
        {
            return await Task.Run(() =>
            {
                try
                {
                    EnsureSession();
                    var entry = new EntryInfo(_session, entryId);

                    foreach (var kvp in metadata)
                    {
                        try
                        {
                            var field = entry.Fields[kvp.Key];
                            if (field != null)
                            {
                                field.Value = kvp.Value?.ToString();
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Warning(ex, "Could not update field {FieldName} for entry {EntryId}", kvp.Key, entryId);
                        }
                    }

                    entry.Commit();
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to update metadata for entry {EntryId}", entryId);
                    return false;
                }
            });
        }

        public async Task<bool> MoveDocumentAsync(int entryId, string destinationPath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    EnsureSession();
                    var entry = new EntryInfo(_session, entryId);
                    var destFolder = new FolderInfo(_session, destinationPath);
                    entry.MoveToFolder(destFolder);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to move entry {EntryId} to {Path}", entryId, destinationPath);
                    return false;
                }
            });
        }

        public async Task<byte[]> DownloadDocumentAsync(int entryId)
        {
            return await Task.Run(() =>
            {
                try
                {
                    EnsureSession();
                    var entry = new EntryInfo(_session, entryId);
                    using (var stream = entry.ReadDocument())
                    using (var memoryStream = new System.IO.MemoryStream())
                    {
                        stream.CopyTo(memoryStream);
                        return memoryStream.ToArray();
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to download document {EntryId}", entryId);
                    throw;
                }
            });
        }

        public async Task<string> RunOcrAsync(int entryId)
        {
            return await Task.Run(() =>
            {
                try
                {
                    EnsureSession();
                    var entry = new EntryInfo(_session, entryId);
                    // OCR implementation depends on Laserfiche DocumentServices
                    _logger.Information("OCR requested for entry {EntryId}", entryId);
                    return "OCR result placeholder";
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to run OCR for entry {EntryId}", entryId);
                    throw;
                }
            });
        }

        public async Task<string> GetDocumentPathAsync(int entryId)
        {
            return await Task.Run(() =>
            {
                try
                {
                    EnsureSession();
                    var entry = new EntryInfo(_session, entryId);
                    return GetPathFromEntry(entry);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to get path for entry {EntryId}", entryId);
                    throw;
                }
            });
        }

        public async Task<string> GetRepositoryNameAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    EnsureSession();
                    return _session.CurrentRepository?.Name ?? "Default Repository";
                }
                catch
                {
                    return "Default Repository";
                }
            });
        }

        private void EnsureSession()
        {
            if (_session == null || !_session.IsConnected)
            {
                try
                {
                    _session?.UseExistingConnection();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to establish Laserfiche session");
                    throw new InvalidOperationException("Not connected to Laserfiche", ex);
                }
            }
        }

        private string GetPathFromEntry(EntryInfo entry)
        {
            try
            {
                var parts = new List<string>();
                var current = entry;
                while (current != null && current.Parent != null)
                {
                    parts.Insert(0, current.Name);
                    current = current.Parent;
                }
                return "\\" + string.Join("\\", parts);
            }
            catch
            {
                return "\\" + entry.Name;
            }
        }
    }
}
