using LFPortal.Application.DTOs;
using LFPortal.Application.Interfaces;
using LFPortal.Domain.Entities;
using LFPortal.Domain.Exceptions;
using LFPortal.Infrastructure.Services;
using LFPortal.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LFPortal.Web.Tests;

public sealed class DocumentPreviewTests
{
    private static LFEntry Entry(int? pageCount = 0) => new()
    { Id = 619, Name = "Document", EntryType = LFEntryType.Document, PageCount = pageCount };

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    public async Task MissingElectronicFile_StillLoadsScannedPages(int status)
    {
        var service = new Documents { EdocError = new LaserficheException("No edoc", status),
            Pages = [new LFDocumentPage { PageNumber = 3 }] };
        var model = await DocumentPreviewResolver.ResolveAsync(service, Entry(1), NullLogger.Instance, default);
        Assert.False(model.HasElectronicDocument);
        Assert.Equal(3, Assert.Single(model.Pages).PageNumber);
        Assert.Null(model.ErrorMessage);
        Assert.Null(model.PreviewError);
    }

    [Fact]
    public async Task EmptyDocument_DoesNotShowAnHttpError()
    {
        var service = new Documents { EdocError = new LaserficheException("No edoc", 400) };
        var model = await DocumentPreviewResolver.ResolveAsync(service, Entry(), NullLogger.Instance, default);
        Assert.Empty(model.Pages);
        Assert.False(model.HasElectronicDocument);
        Assert.Null(model.PreviewError);
        Assert.Null(model.ErrorMessage);
    }

    [Fact]
    public async Task KnownElectronicFile_WithBadRequest_IsNotMislabelledAsEmpty()
    {
        var service = new Documents { EdocError = new LaserficheException("Bad request", 400) };
        var model = await DocumentPreviewResolver.ResolveAsync(service,
            Entry() with { FileSizeBytes = 4096 }, NullLogger.Instance, default);
        Assert.NotNull(model.PreviewError);
        Assert.Null(model.ErrorMessage);
    }

    [Fact]
    public async Task FailedPageMetadata_UsesKnownPageCount()
    {
        var service = new Documents { EdocError = new LaserficheException("No edoc", 400),
            PagesError = new LaserficheException("Unavailable", 404) };
        var model = await DocumentPreviewResolver.ResolveAsync(service, Entry(2), NullLogger.Instance, default);
        Assert.Equal(new[] { 1, 2 }, model.Pages.Select(p => p.PageNumber));
        Assert.Null(model.PreviewError);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(500)]
    public async Task AccessAndServerFailures_AreNotReportedAsEmptyDocuments(int status)
    {
        var service = new Documents { EdocError = new LaserficheException("Failed", status) };
        var model = await DocumentPreviewResolver.ResolveAsync(service, Entry(), NullLogger.Instance, default);
        Assert.NotNull(model.PreviewError);
        Assert.Null(model.ErrorMessage); // Metadata remains available.
    }

    [Fact]
    public async Task TextOnlyPages_DoNotInventImagePreviews()
    {
        var service = new Documents { EdocError = new LaserficheException("No edoc", 404) };
        var model = await DocumentPreviewResolver.ResolveAsync(service, Entry(4), NullLogger.Instance, default);
        Assert.Empty(model.Pages); // Successful page API returned no images.
        Assert.Null(model.PreviewError);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PdfMvcResponse_PreservesBytesAndSupportsRange(bool range)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\n0123456789abcdef\n%%EOF");
        var input = new MemoryStream(bytes);
        using var source = new LaserficheEdocStream(input, "application/pdf", null, "test.pdf", ".pdf", bytes.Length, input);
        var buffered = await BrowserPreviewContent.BufferForPreviewAsync(source, default);
        var services = new ServiceCollection();
        services.AddLogging(); services.AddControllers();
        using var provider = services.BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = provider };
        http.Request.Method = "GET";
        if (range) http.Request.Headers.Range = "bytes=5-12";
        http.Response.Body = new MemoryStream();
        var context = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var result = new FileStreamResult(buffered.Content, buffered.ContentType) { EnableRangeProcessing = true };
        await result.ExecuteResultAsync(context);
        Assert.Equal(range ? 206 : 200, http.Response.StatusCode);
        Assert.Equal(range ? bytes[5..13] : bytes, ((MemoryStream)http.Response.Body).ToArray());
        Assert.Equal(range ? 8 : bytes.Length, http.Response.ContentLength);
        Assert.Equal("application/pdf", http.Response.ContentType);
    }

    [Fact]
    public async Task TruncatedPdf_IsRejectedBeforeReturningAFileResponse()
    {
        var input = new MemoryStream([1, 2, 3]);
        using var source = new LaserficheEdocStream(input, "application/pdf", null, null, null, 100, input);
        await Assert.ThrowsAsync<IOException>(() => BrowserPreviewContent.BufferForPreviewAsync(source, default));
        Assert.False(input.CanRead);
    }

    private sealed class Documents : ILaserficheDocumentService
    {
        public Exception? EdocError { get; init; }
        public Exception? PagesError { get; init; }
        public IReadOnlyList<LFDocumentPage> Pages { get; init; } = [];
        public Task<LaserficheEdocStream> StreamEdocAsync(int entryId, CancellationToken cancellationToken = default) =>
            Task.FromException<LaserficheEdocStream>(EdocError ?? new LaserficheException("No edoc", 404));
        public Task<IReadOnlyList<LFDocumentPage>> GetDocumentPagesAsync(int entryId, CancellationToken cancellationToken = default) =>
            PagesError is null ? Task.FromResult(Pages) : Task.FromException<IReadOnlyList<LFDocumentPage>>(PagesError);
        public Task<LaserficheEdocStream> GetPageImageAsync(int entryId, int pageNumber, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<LFEntry> GetDocumentMetadataAsync(int entryId, CancellationToken cancellationToken = default) => Task.FromResult(Entry());
    }
}
