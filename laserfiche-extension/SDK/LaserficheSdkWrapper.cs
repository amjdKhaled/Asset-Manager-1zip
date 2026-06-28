using LaserficheAIExtension.Infrastructure.Logging;
using LaserficheAIExtension.Models;
using Laserfiche.DocumentServices;
using Laserfiche.RepositoryAccess;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace LaserficheAIExtension.SDK
{
    /// <summary>
    /// Real implementation of Laserfiche SDK operations using documented
    /// Laserfiche.RepositoryAccess and Laserfiche.DocumentServices APIs only.
    /// </summary>
    public class LaserficheSdkWrapper : ILaserficheSdkWrapper, IDisposable
    {
        private readonly ILogger<LaserficheSdkWrapper> _logger;
        private readonly ExtensionSettings _settings;
        private Session _session;
        private readonly object _sessionLock = new object();

        public bool IsConnected
        {
            get
            {
                lock (_sessionLock)
                {
                    return _session != null && _session.IsConnected;
                }
            }
        }

        public LaserficheSdkWrapper(ILogger<LaserficheSdkWrapper> logger, ExtensionSettings settings)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>
        /// Lazily establishes a session using stored Laserfiche connection settings.
        /// Falls back to Windows pass-through authentication when no username is provided.
        /// </summary>
        private void EnsureSession()
        {
            lock (_sessionLock)
            {
                if (_session != null && _session.IsConnected)
                    return;

                try
                {
                    if (_session != null)
                    {
                        try { _session.Dispose(); }
                        catch { /* best-effort cleanup */ }
                        _session = null;
                    }

                    if (string.IsNullOrWhiteSpace(_settings.LaserficheServer)
                        || string.IsNullOrWhiteSpace(_settings.LaserficheRepository))
                    {
                        throw new InvalidOperationException(
                            "Laserfiche server and repository are not configured in extension settings.");
                    }

                    var registration = new RepositoryRegistration(
                        _settings.LaserficheServer,
                        _settings.LaserficheRepository);

                    _session = new Session();
                    _session.Connect(registration);

                    if (!string.IsNullOrEmpty(_settings.LaserficheUsername))
                    {
                        _session.LogIn(
                            _settings.LaserficheUsername,
                            _settings.LaserfichePassword ?? string.Empty);
                    }
                    else
                    {
                        // Windows pass-through / Kerberos authentication
                        _session.LogIn(registration);
                    }

                    _logger.Information(
                        "Connected to Laserfiche server={Server} repository={Repo}",
                        _settings.LaserficheServer,
                        _settings.LaserficheRepository);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to establish Laserfiche session");
                    throw new InvalidOperationException(
                        "Not connected to Laserfiche. Verify server, repository, and credentials in settings.", ex);
                }
            }
        }

        public async Task<DocumentContext> GetDocumentContextAsync(int entryId)
        {
            return await Task.Run(() =>
            {
                EnsureSession();

                using (EntryInfo entry = Entry.GetEntryInfo(entryId, _session))
                {
                    var context = new DocumentContext
                    {
                        EntryId = entryId,
                        DocumentId = entryId.ToString(),
                        DocumentName = entry.Name ?? "Unknown",
                        TemplateName = entry.TemplateName ?? "None",
                        FolderPath = entry.Path ?? "\\",
                        RepositoryName = _session.RepositoryName ?? "Default",
                        Creator = entry.Owner ?? "",
                        Modifier = "",
                        CreatedDate = "",
                        ModifiedDate = "",
                        PageCount = 0,
                        MimeType = "",
                        IsElectronicDocument = false,
                        IsRecord = false
                    };

                    // Populate document-specific properties when the entry is a document
                    if (entry.EntryType == EntryType.Document)
                    {
                        using (DocumentInfo docInfo = Document.GetDocumentInfo(entryId, _session))
                        {
                            context.PageCount = docInfo.PageCount;
                            context.MimeType = docInfo.Extension ?? "";
                            context.IsElectronicDocument = true;
                        }
                    }

                    // Read template field values
                    try
                    {
                        FieldValueCollection fields = entry.GetFieldValues();
                        for (int i = 0; i < fields.Count; i++)
                        {
                            string fieldName = fields.PositionToName(i);
                            if (!string.IsNullOrEmpty(fieldName))
                            {
                                context.Metadata[fieldName] = fields[i] ?? "";
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Could not read field values for entry {EntryId}", entryId);
                    }

                    return context;
                }
            });
        }

        public async Task<Dictionary<string, object>> GetDocumentMetadataAsync(int entryId)
        {
            return await Task.Run(() =>
            {
                EnsureSession();
                var metadata = new Dictionary<string, object>();

                using (EntryInfo entry = Entry.GetEntryInfo(entryId, _session))
                {
                    try
                    {
                        FieldValueCollection fields = entry.GetFieldValues();
                        for (int i = 0; i < fields.Count; i++)
                        {
                            string fieldName = fields.PositionToName(i);
                            if (!string.IsNullOrEmpty(fieldName))
                            {
                                metadata[fieldName] = fields[i] ?? "";
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Could not read metadata for entry {EntryId}", entryId);
                    }
                }

                return metadata;
            });
        }

        /// <summary>
        /// Opens a document in the Laserfiche Desktop Client viewer.
        /// This operation requires the Laserfiche.ClientAutomation SDK and cannot
        /// be performed through Laserfiche.RepositoryAccess alone.
        /// </summary>
        public async Task<bool> OpenDocumentAsync(int entryId)
        {
            return await Task.Run(() =>
            {
                _logger.Information(
                    "OpenDocument requested for entry {EntryId}. " +
                    "Opening documents in the viewer requires the Laserfiche Desktop Client SDK (Laserfiche.ClientAutomation).",
                    entryId);
                return false;
            });
        }

        public async Task<bool> UpdateMetadataAsync(int entryId, Dictionary<string, object> metadata)
        {
            return await Task.Run(() =>
            {
                try
                {
                    EnsureSession();

                    using (EntryInfo entry = Entry.GetEntryInfo(entryId, _session))
                    {
                        FieldValueCollection fields = entry.GetFieldValues();

                        entry.Lock(LockType.Exclusive);
                        try
                        {
                            foreach (var kvp in metadata)
                            {
                                try
                                {
                                    fields[kvp.Key] = kvp.Value?.ToString() ?? string.Empty;
                                }
                                catch (Exception ex)
                                {
                                    _logger.Warning(
                                        ex,
                                        "Could not update field '{FieldName}' for entry {EntryId}",
                                        kvp.Key, entryId);
                                }
                            }

                            entry.SetFieldValues(fields);
                            entry.Save();
                        }
                        finally
                        {
                            entry.Unlock();
                        }
                    }

                    _logger.Information("Metadata updated for entry {EntryId}", entryId);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to update metadata for entry {EntryId}", entryId);
                    return false;
                }
            });
        }

        /// <summary>
        /// Moves a document to a different folder.
        /// Laserfiche.RepositoryAccess does not expose a direct entry-move API.
        /// This requires the Laserfiche Server API or Desktop Client SDK.
        /// </summary>
        public async Task<bool> MoveDocumentAsync(int entryId, string destinationPath)
        {
            return await Task.Run(() =>
            {
                _logger.Information(
                    "MoveDocument requested for entry {EntryId} to '{Path}'. " +
                    "Move is not supported directly by Laserfiche.RepositoryAccess. " +
                    "Use the Laserfiche Server API or Desktop Client SDK instead.",
                    entryId, destinationPath);
                return false;
            });
        }

        public async Task<byte[]> DownloadDocumentAsync(int entryId)
        {
            return await Task.Run(() =>
            {
                try
                {
                    EnsureSession();

                    using (DocumentInfo docInfo = Document.GetDocumentInfo(entryId, _session))
                    {
                        using (var memoryStream = new MemoryStream())
                        {
                            var exporter = new DocumentExporter();
                            exporter.ExportElecDoc(docInfo, memoryStream);
                            return memoryStream.ToArray();
                        }
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

                    using (DocumentInfo docInfo = Document.GetDocumentInfo(entryId, _session))
                    {
                        _logger.Information(
                            "OCR requested for entry {EntryId}. " +
                            "Full OCR requires Laserfiche DocumentServices OcrEngine configuration.",
                            entryId);

                        return $"OCR placeholder: Document '{docInfo.Name}', {docInfo.PageCount} page(s). " +
                               "Configure OcrEngine for production use.";
                    }
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

                    using (EntryInfo entry = Entry.GetEntryInfo(entryId, _session))
                    {
                        return entry.Path ?? "\\";
                    }
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
                    return _session.RepositoryName ?? "Default Repository";
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to get repository name");
                    return "Default Repository";
                }
            });
        }

        public void Dispose()
        {
            lock (_sessionLock)
            {
                if (_session != null)
                {
                    try
                    {
                        _session.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Error disposing Laserfiche session");
                    }
                    _session = null;
                }
            }
        }
    }
}
