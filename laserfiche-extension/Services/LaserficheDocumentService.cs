using LaserficheAIExtension.Infrastructure.Logging;
using LaserficheAIExtension.Models;
using LaserficheAIExtension.SDK;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LaserficheAIExtension.Services
{
    /// <summary>
    /// Implementation of document operations using the Laserfiche SDK wrapper.
    /// </summary>
    public class LaserficheDocumentService : ILaserficheDocumentService
    {
        private readonly ILaserficheSdkWrapper _sdk;
        private readonly ILogger<LaserficheDocumentService> _logger;

        public LaserficheDocumentService(ILaserficheSdkWrapper sdk, ILogger<LaserficheDocumentService> logger)
        {
            _sdk = sdk ?? throw new ArgumentNullException(nameof(sdk));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<DocumentContext> GetDocumentContextAsync(int entryId)
        {
            try
            {
                _logger.Debug("Getting document context for entry {EntryId}", entryId);
                return await _sdk.GetDocumentContextAsync(entryId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get document context for entry {EntryId}", entryId);
                throw;
            }
        }

        public async Task<Dictionary<string, object>> GetDocumentMetadataAsync(int entryId)
        {
            try
            {
                _logger.Debug("Getting metadata for entry {EntryId}", entryId);
                return await _sdk.GetDocumentMetadataAsync(entryId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get metadata for entry {EntryId}", entryId);
                throw;
            }
        }

        public async Task<bool> OpenDocumentAsync(int entryId)
        {
            try
            {
                _logger.Information("Opening document {EntryId}", entryId);
                return await _sdk.OpenDocumentAsync(entryId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to open document {EntryId}", entryId);
                return false;
            }
        }

        public async Task<bool> UpdateMetadataAsync(int entryId, Dictionary<string, object> metadata)
        {
            try
            {
                _logger.Information("Updating metadata for entry {EntryId}", entryId);
                return await _sdk.UpdateMetadataAsync(entryId, metadata);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to update metadata for entry {EntryId}", entryId);
                return false;
            }
        }

        public async Task<bool> MoveDocumentAsync(int entryId, string destinationPath)
        {
            try
            {
                _logger.Information("Moving entry {EntryId} to {Destination}", entryId, destinationPath);
                return await _sdk.MoveDocumentAsync(entryId, destinationPath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to move entry {EntryId}", entryId);
                return false;
            }
        }

        public async Task<byte[]> DownloadDocumentAsync(int entryId)
        {
            try
            {
                _logger.Debug("Downloading document {EntryId}", entryId);
                return await _sdk.DownloadDocumentAsync(entryId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to download document {EntryId}", entryId);
                throw;
            }
        }

        public async Task<string> RunOcrAsync(int entryId)
        {
            try
            {
                _logger.Information("Running OCR for entry {EntryId}", entryId);
                return await _sdk.RunOcrAsync(entryId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to run OCR for entry {EntryId}", entryId);
                throw;
            }
        }

        public async Task<string> GetDocumentPathAsync(int entryId)
        {
            try
            {
                return await _sdk.GetDocumentPathAsync(entryId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get path for entry {EntryId}", entryId);
                throw;
            }
        }

        public async Task<string> GetRepositoryNameAsync()
        {
            try
            {
                return await _sdk.GetRepositoryNameAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to get repository name");
                return "Default Repository";
            }
        }
    }
}
