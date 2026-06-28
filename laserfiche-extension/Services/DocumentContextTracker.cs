using LaserficheAIExtension.Infrastructure.Logging;
using LaserficheAIExtension.Models;
using System;
using System.Threading.Tasks;

namespace LaserficheAIExtension.Services
{
    /// <summary>
    /// Tracks document selection and notifies subscribers.
    /// </summary>
    public class DocumentContextTracker : IDocumentContextTracker
    {
        private readonly ILaserficheDocumentService _documentService;
        private readonly ILogger<DocumentContextTracker> _logger;
        private DocumentContext _currentDocument;

        public event EventHandler<DocumentContext> DocumentChanged;

        public DocumentContext CurrentDocument => _currentDocument;

        public DocumentContextTracker(
            ILaserficheDocumentService documentService,
            ILogger<DocumentContextTracker> logger)
        {
            _documentService = documentService ?? throw new ArgumentNullException(nameof(documentService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task UpdateSelectionAsync(int entryId)
        {
            try
            {
                var context = await _documentService.GetDocumentContextAsync(entryId);
                if (context == null) return;

                _currentDocument = context;
                _logger.Information("Document selection changed: {DocumentName} (EntryId={EntryId})",
                    context.DocumentName, context.EntryId);

                DocumentChanged?.Invoke(this, context);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to update selection for entry {EntryId}", entryId);
            }
        }

        public void ClearSelection()
        {
            _currentDocument = null;
            _logger.Debug("Document selection cleared");
            DocumentChanged?.Invoke(this, null);
        }
    }
}
