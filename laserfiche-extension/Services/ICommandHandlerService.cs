using LaserficheAIExtension.Models;
using System.Threading.Tasks;

namespace LaserficheAIExtension.Services
{
    /// <summary>
    /// Handles commands received from the web app and routes them to Laserfiche operations.
    /// </summary>
    public interface ICommandHandlerService
    {
        Task HandleCommandAsync(WebCommand command);
    }
}
