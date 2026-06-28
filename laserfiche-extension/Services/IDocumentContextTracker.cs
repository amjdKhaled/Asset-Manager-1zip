using LaserficheAIExtension.Models;
using System;
using System.Threading.Tasks;

namespace LaserficheAIExtension.Services
{
    /// <summary>
    /// Tracks the currently selected document in Laserfiche and broadcasts changes.
    /// </summary>
    public interface IDocumentContextTracker
    {
        event EventHandler<DocumentContext> DocumentChanged;
        DocumentContext CurrentDocument { get; }
        Task UpdateSelectionAsync(int entryId);
        void ClearSelection();
    }
}
