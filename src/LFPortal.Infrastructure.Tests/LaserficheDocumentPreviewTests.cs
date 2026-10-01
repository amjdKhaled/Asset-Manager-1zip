using System.Net;
using System.Net.Http.Headers;
using System.Text;
using LFPortal.Application.DTOs;
using LFPortal.Application.Interfaces;
using LFPortal.Infrastructure.Adapters;
using LFPortal.Infrastructure.Options;
using LFPortal.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using ImageMagick;

namespace LFPortal.Infrastructure.Tests;

public sealed class LaserficheDocumentPreviewTests
{
    [Fact]
    public async Task ElectronicDocument_V2UsesDocumentedExportFlow()
    {
        var export = Json("{\"value\":\"https://lf.test/download/document.pdf\"}");
        var pdf = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([0x25, 0x50, 0x44, 0x46])
        };
        pdf.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        pdf.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = "document.pdf" };

        var handler = new QueueHandler(export, pdf);
        var service = CreateService(handler);

        using var result = await service.StreamEdocAsync(42);

        Assert.Equal("application/pdf", result.ContentType);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.EndsWith("/Entries/42/Export", handler.Requests[0].Url);
        Assert.Equal("https://lf.test/download/document.pdf", handler.Requests[1].Url);
    }

    [Fact]
    public async Task ElectronicDocument_KeepsHttpClientAliveUntilReturnedStreamIsDisposed()
    {
        var export = Json("{\"value\":\"https://lf.test/download/document.pdf\"}");
        var pdf = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([0x25, 0x50, 0x44, 0x46])
        };
        pdf.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");

        var factory = new ClientFactory(new QueueHandler(export, pdf));
        var service = CreateService(factory);

        var result = await service.StreamEdocAsync(42);

        Assert.NotNull(factory.LastClient);
        Assert.False(factory.LastClient.WasDisposed);

        using var copy = new MemoryStream();
        await result.Content.CopyToAsync(copy);
        Assert.Equal([0x25, 0x50, 0x44, 0x46], copy.ToArray());

        result.Dispose();
        Assert.True(factory.LastClient.WasDisposed);
    }

    [Fact]
    public async Task TiffPage_IsExportedAsBrowserSafePngOnV2()
    {
        var tiff = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([0x49, 0x49, 0x2A, 0x00])
        };
        tiff.Content.Headers.ContentType = new MediaTypeHeaderValue("image/tiff");
        tiff.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline") { FileName = "page.tif" };

        var export = Json("{\"value\":\"https://lf.test/download/page.png\"}");
        var png = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47])
        };
        png.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");

        var handler = new QueueHandler(tiff, export, png);
        var service = CreateService(handler);

        using var result = await service.GetPageImageAsync(42, 1);

        Assert.Equal("image/png", result.ContentType);
        Assert.Equal(3, handler.Requests.Count);
        Assert.EndsWith("/Entries/42/Document/Pages/1/Image", handler.Requests[0].Url);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        Assert.EndsWith("/Entries/42/Export?pageRange=1", handler.Requests[1].Url);
        Assert.Equal("https://lf.test/download/page.png", handler.Requests[2].Url);
    }

    [Theory]
    [InlineData("application/octet-stream", "scan.pdf", "application/pdf")]
    [InlineData(null, "scan.jpeg", "image/jpeg")]
    [InlineData("image/png", null, "image/png")]
    public void ContentType_IsRecoveredFromFilenameWhenHeaderIsGeneric(
        string? header,
        string? fileName,
        string expected) =>
        Assert.Equal(expected, LaserficheDocumentService.NormalizeContentType(header, fileName));

    [Theory]
    [InlineData("{\"value\":\"https://lf.test/file\"}")]
    [InlineData("\"https://lf.test/file\"")]
    public void ExportLinkParser_AcceptsSupportedResponses(string body) =>
        Assert.Equal("https://lf.test/file", LaserficheDocumentService.ParseExportDownloadLink(body));

    [Fact]
    public async Task V1Edoc_UsesConfirmedRouteAndAcceptAndRetainsEveryByte()
    {
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.7\nA complete document body with more than sixteen bytes.");
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var handler = new QueueHandler(response);
        using var result = await CreateService(handler, "v1").StreamEdocAsync(619);
        Assert.Equal("https://lf.test/LFRepositoryAPI/v1/Repositories/Documents/Entries/619/Laserfiche.Repository.Document/edoc", handler.Requests[0].Url);
        Assert.Equal("application/octet-stream", handler.Requests[0].Accept);
        Assert.Equal("application/pdf", result.ContentType);
        using var copy = new MemoryStream();
        await result.Content.CopyToAsync(copy);
        Assert.Equal(bytes, copy.ToArray());
    }

    [Fact]
    public async Task V1TiffPage_IsConvertedLocallyToRealPng()
    {
        using var original = new MagickImage(MagickColors.White, 8, 12);
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(original.ToByteArray(MagickFormat.Tiff))
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var handler = new QueueHandler(response);
        using var result = await CreateService(handler, "v1").GetPageImageAsync(42, 3);
        Assert.Single(handler.Requests);
        Assert.EndsWith("/Entries/42/pages/3/image", handler.Requests[0].Url);
        Assert.Equal("image/png", result.ContentType);
        using var rendered = new MagickImage(result.Content);
        Assert.Equal(MagickFormat.Png, rendered.Format);
        Assert.Equal(8u, rendered.Width);
        Assert.Equal(12u, rendered.Height);
    }

    [Fact]
    public async Task EdocTiff_DownloadRemainsOriginalTiff()
    {
        using var original = new MagickImage(MagickColors.White, 8, 12);
        var bytes = original.ToByteArray(MagickFormat.Tiff);
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        var handler = new QueueHandler(response);
        using var result = await CreateService(handler, "v1").StreamEdocAsync(619);
        Assert.Equal("image/tiff", result.ContentType);
        using var copy = new MemoryStream();
        await result.Content.CopyToAsync(copy);
        Assert.Equal(bytes, copy.ToArray());
    }

    [Fact]
    public async Task Inspect_NonSeekableShortReadsRetainEveryByte()
    {
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.7 document body");
        using var source = new OneByteStream(bytes);
        var inspected = await BrowserPreviewContent.InspectAsync(source, "application/octet-stream", default);
        using var content = inspected.Content;
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy);
        Assert.Equal("application/pdf", inspected.ContentType);
        Assert.Equal(bytes, copy.ToArray());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task V1MissingPageRoute_TriesDocumentedV2OnSameServer(HttpStatusCode status)
    {
        var png = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([137, 80, 78, 71, 13, 10, 26, 10, 1])
        };
        var handler = new QueueHandler(new HttpResponseMessage(status), png);
        using var result = await CreateService(handler, "v1").GetPageImageAsync(42, 1);
        Assert.Equal("image/png", result.ContentType);
        Assert.EndsWith("/v2/Repositories/Documents/Entries/42/Document/Pages/1/Image", handler.Requests[1].Url);
    }

    [Fact]
    public async Task TextOnlyPages_AreExcludedFromImageList()
    {
        var handler = new QueueHandler(Json("{\"value\":[{\"pageNumber\":1,\"hasImage\":false},{\"pageNumber\":2,\"hasImage\":true}]}"));
        var pages = await CreateService(handler).GetDocumentPagesAsync(42);
        Assert.Equal(2, Assert.Single(pages).PageNumber);
    }

    private sealed class OneByteStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, 1)], cancellationToken);
    }

    private static LaserficheDocumentService CreateService(QueueHandler handler, string version = "v2") =>
        CreateService(new ClientFactory(handler), version);

    private static LaserficheDocumentService CreateService(ClientFactory factory, string version = "v2")
    {
        var options = new LaserficheOptions
        {
            ServerUrl = "https://lf.test",
            ApiBasePath = "/LFRepositoryAPI",
            ApiVersion = version
        };
        var adapter = new LaserficheApiAdapter(new StaticOptionsMonitor(options));
        return new LaserficheDocumentService(
            factory,
            new RepositoryContext(),
            null!,
            adapter,
            NullLogger<LaserficheDocumentService>.Instance);
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class QueueHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public List<(HttpMethod Method, string Url, string Accept)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri!.AbsoluteUri, request.Headers.Accept.ToString()));
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public TrackingHttpClient? LastClient { get; private set; }

        public HttpClient CreateClient(string name) =>
            LastClient = new TrackingHttpClient(handler);
    }

    private sealed class TrackingHttpClient(HttpMessageHandler handler)
        : HttpClient(handler, disposeHandler: false)
    {
        public bool WasDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing) WasDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class RepositoryContext : IRepositoryContext
    {
        private static readonly RepositoryDescriptor Repository =
            new("test", "https://lf.test", "Documents", "Documents");

        public Task<RepositoryDescriptor> GetActiveRepositoryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Repository);

        public Task<IReadOnlyList<RepositoryDescriptor>> GetAllRepositoriesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RepositoryDescriptor>>([Repository]);
    }

    private sealed class StaticOptionsMonitor(LaserficheOptions value) : IOptionsMonitor<LaserficheOptions>
    {
        public LaserficheOptions CurrentValue => value;
        public LaserficheOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<LaserficheOptions, string?> listener) => null;
    }
}
