using LFPortal.Domain.Entities;
using LFPortal.Application.DTOs;
using LFPortal.Application.Interfaces;
using LFPortal.Infrastructure.Adapters;
using LFPortal.Infrastructure.Options;
using LFPortal.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;
using Xunit;

namespace LFPortal.Infrastructure.Tests;

public sealed class LaserficheDashboardTraversalTests
{
    [Fact]
    public async Task RecursiveScan_CountsThreeRootFoldersNestedDocumentsAndDocumentTemplatesOnly()
    {
        var roots = new[] { Folder(10, "A"), Folder(20, "B"), Folder(30, "C") };
        var children = new Dictionary<int, IReadOnlyList<LFEntry>>
        {
            [10] = [Folder(11, "A1"), Document(101, "templated", templateId: 7)],
            [11] = [Document(102, "plain"), Document(103, "named", templateName: "Invoices")],
            [20] = [Folder(21, "B1")],
            [21] = [Document(201, "deep")],
            [30] = [Document(301, "last")]
        };

        var result = await LaserficheDashboardService.ScanRootFoldersAsync(
            roots,
            (id, _) => Task.FromResult(children.GetValueOrDefault(id, Array.Empty<LFEntry>())),
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Equal(3, result.Count);
        Assert.Equal(5, result.Sum(x => x.Documents));
        Assert.Equal(2, result.Sum(x => x.Folders));
        Assert.Equal(3, result.Single(x => x.Name == "A").Documents);
        Assert.Equal(2, result.SelectMany(x => x.TemplateCounts).Sum(x => x.Value));
        Assert.Contains(result.SelectMany(x => x.TemplateCounts), x => x.Key == "Template #7");
        Assert.Contains(result.SelectMany(x => x.TemplateCounts), x => x.Key == "Invoices");
    }

    [Fact]
    public async Task FolderChildren_FollowsODataAndPlainNextLinksAndReadsEveryValueItem()
    {
        var handler = new QueueHandler(
            Json("""{"value":[{"id":10,"name":"A","entryType":"Folder"},{"id":101,"name":"D1","entryType":"Document"}],"@odata.nextLink":"/page-2"}"""),
            Json("""{"value":[{"id":102,"name":"D2","entryType":"Document"}],"nextLink":"https://lf.test/page-3"}"""),
            Json("""{"value":[{"id":103,"name":"D3","entryType":"Document"}]}"""));
        var options = new LaserficheOptions { ServerUrl = "https://lf.test", ApiBasePath = "/LFRepositoryAPI", ApiVersion = "v2" };
        var service = new LaserficheEntryService(
            new ClientFactory(handler),
            new RepositoryContext(),
            new LaserficheApiAdapter(new OptionsMonitor(options)),
            NullLogger<LaserficheEntryService>.Instance);

        var entries = await service.GetAllFolderChildrenAsync(1);

        Assert.Equal(4, entries.Count);
        Assert.Equal(new[] { 10, 101, 102, 103 }, entries.Select(x => x.Id));
        Assert.Equal(3, handler.RequestCount);
    }

    private static LFEntry Folder(int id, string name) =>
        new() { Id = id, Name = name, EntryType = LFEntryType.Folder };

    private static LFEntry Document(int id, string name, int? templateId = null, string? templateName = null) =>
        new() { Id = id, Name = name, EntryType = LFEntryType.Document, TemplateId = templateId, TemplateName = templateName };

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class QueueHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public int RequestCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RepositoryContext : IRepositoryContext
    {
        private static readonly RepositoryDescriptor Repository = new("test", "https://lf.test", "TestEmployee", "TestEmployee");
        public Task<RepositoryDescriptor> GetActiveRepositoryAsync(CancellationToken cancellationToken = default) => Task.FromResult(Repository);
        public Task<IReadOnlyList<RepositoryDescriptor>> GetAllRepositoriesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RepositoryDescriptor>>([Repository]);
    }

    private sealed class OptionsMonitor(LaserficheOptions value) : IOptionsMonitor<LaserficheOptions>
    {
        public LaserficheOptions CurrentValue => value;
        public LaserficheOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<LaserficheOptions, string?> listener) => null;
    }
}
