using LaserficheAIExtension.Infrastructure.Logging;
using LaserficheAIExtension.Models;
using System;
using System.Threading.Tasks;

namespace LaserficheAIExtension.Services
{
    /// <summary>
    /// Routes web app commands to the appropriate Laserfiche document operations.
    /// </summary>
    public class CommandHandlerService : ICommandHandlerService
    {
        private readonly ILaserficheDocumentService _documentService;
        private readonly IWebAppCommunicationService _communicationService;
        private readonly ILogger<CommandHandlerService> _logger;

        public CommandHandlerService(
            ILaserficheDocumentService documentService,
            IWebAppCommunicationService communicationService,
            ILogger<CommandHandlerService> logger)
        {
            _documentService = documentService ?? throw new ArgumentNullException(nameof(documentService));
            _communicationService = communicationService ?? throw new ArgumentNullException(nameof(communicationService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task HandleCommandAsync(WebCommand command)
        {
            if (command?.Payload == null)
            {
                _logger.Warning("Received null or empty command");
                return;
            }

            _logger.Information("Handling command: {Command}", command.Command);

            try
            {
                switch (command.Command)
                {
                    case WebCommandTypes.OpenDocument:
                        await HandleOpenDocumentAsync(command.Payload);
                        break;
                    case WebCommandTypes.UpdateMetadata:
                        await HandleUpdateMetadataAsync(command.Payload);
                        break;
                    case WebCommandTypes.MoveDocument:
                        await HandleMoveDocumentAsync(command.Payload);
                        break;
                    case WebCommandTypes.RefreshMetadata:
                        await HandleRefreshMetadataAsync(command.Payload);
                        break;
                    case WebCommandTypes.DownloadDocument:
                        await HandleDownloadDocumentAsync(command.Payload);
                        break;
                    case WebCommandTypes.RunOcr:
                        await HandleRunOcrAsync(command.Payload);
                        break;
                    case WebCommandTypes.RunAi:
                        await HandleRunAiAsync(command.Payload);
                        break;
                    case WebCommandTypes.SearchRepository:
                        await HandleSearchRepositoryAsync(command.Payload);
                        break;
                    case WebCommandTypes.GetDocumentFields:
                        await HandleGetDocumentFieldsAsync(command.Payload);
                        break;
                    case WebCommandTypes.Ping:
                        _logger.Debug("Ping received from web app");
                        break;
                    case WebCommandTypes.SetTheme:
                        _logger.Information("Theme change requested: {Theme}", command.Payload["theme"]);
                        break;
                    default:
                        _logger.Warning("Unknown command received: {Command}", command.Command);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error handling command {Command}", command.Command);
            }
        }

        private async Task HandleOpenDocumentAsync(System.Collections.Generic.Dictionary<string, object> payload)
        {
            if (payload.TryGetValue("entryId", out var entryIdValue) && entryIdValue is long entryId)
            {
                var success = await _documentService.OpenDocumentAsync((int)entryId);
                await SendResultAsync("OpenDocumentResult", new { success, entryId });
            }
        }

        private async Task HandleUpdateMetadataAsync(System.Collections.Generic.Dictionary<string, object> payload)
        {
            if (payload.TryGetValue("entryId", out var entryIdValue) && entryIdValue is long entryId
                && payload.TryGetValue("metadata", out var metadataValue) && metadataValue is System.Collections.Generic.Dictionary<string, object> metadata)
            {
                var success = await _documentService.UpdateMetadataAsync((int)entryId, metadata);
                await SendResultAsync("UpdateMetadataResult", new { success, entryId });
            }
        }

        private async Task HandleMoveDocumentAsync(System.Collections.Generic.Dictionary<string, object> payload)
        {
            if (payload.TryGetValue("entryId", out var entryIdValue) && entryIdValue is long entryId
                && payload.TryGetValue("destinationPath", out var pathValue) && pathValue is string destinationPath)
            {
                var success = await _documentService.MoveDocumentAsync((int)entryId, destinationPath);
                await SendResultAsync("MoveDocumentResult", new { success, entryId, destinationPath });
            }
        }

        private async Task HandleRefreshMetadataAsync(System.Collections.Generic.Dictionary<string, object> payload)
        {
            if (payload.TryGetValue("entryId", out var entryIdValue) && entryIdValue is long entryId)
            {
                var metadata = await _documentService.GetDocumentMetadataAsync((int)entryId);
                await SendResultAsync("RefreshMetadataResult", new { entryId, metadata });
            }
        }

        private async Task HandleDownloadDocumentAsync(System.Collections.Generic.Dictionary<string, object> payload)
        {
            if (payload.TryGetValue("entryId", out var entryIdValue) && entryIdValue is long entryId)
            {
                var data = await _documentService.DownloadDocumentAsync((int)entryId);
                var base64 = Convert.ToBase64String(data);
                await SendResultAsync("DownloadDocumentResult", new { entryId, base64, size = data.Length });
            }
        }

        private async Task HandleRunOcrAsync(System.Collections.Generic.Dictionary<string, object> payload)
        {
            if (payload.TryGetValue("entryId", out var entryIdValue) && entryIdValue is long entryId)
            {
                var text = await _documentService.RunOcrAsync((int)entryId);
                await SendResultAsync("RunOcrResult", new { entryId, text });
            }
        }

        private async Task HandleRunAiAsync(System.Collections.Generic.Dictionary<string, object> payload)
        {
            _logger.Information("RunAI command received with payload: {Payload}", payload);
            await SendResultAsync("RunAiResult", new { status = "executed" });
        }

        private async Task HandleSearchRepositoryAsync(System.Collections.Generic.Dictionary<string, object> payload)
        {
            _logger.Information("Search repository command: {Query}", payload["query"]);
            await SendResultAsync("SearchRepositoryResult", new { status = "not_implemented" });
        }

        private async Task HandleGetDocumentFieldsAsync(System.Collections.Generic.Dictionary<string, object> payload)
        {
            if (payload.TryGetValue("entryId", out var entryIdValue) && entryIdValue is long entryId)
            {
                var metadata = await _documentService.GetDocumentMetadataAsync((int)entryId);
                await SendResultAsync("GetDocumentFieldsResult", new { entryId, metadata });
            }
        }

        private async Task SendResultAsync(string command, object result)
        {
            await _communicationService.SendCommandAsync(command, result);
        }
    }
}
