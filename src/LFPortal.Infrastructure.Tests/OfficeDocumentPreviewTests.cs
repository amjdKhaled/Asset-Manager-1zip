using System.IO.Compression;
using System.Text;
using ImageMagick;
using LFPortal.Application.DTOs;
using LFPortal.Infrastructure.Services;
using Xunit;

namespace LFPortal.Infrastructure.Tests;

public sealed class OfficeDocumentPreviewTests
{
    [Theory]
    [InlineData("report.DOCX", null, ".docx")]
    [InlineData(null, "application/msword", ".doc")]
    [InlineData(null, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ".xlsx")]
    [InlineData("slides.pptx", "application/octet-stream", ".pptx")]
    [InlineData("file.exe", "application/octet-stream", null)]
    [InlineData("archive.zip", "application/zip", null)]
    public void FormatSelection(string? name, string? mime, string? expected) =>
        Assert.Equal(expected, OfficeDocumentPreview.GetExtension(name, mime));

    [Theory]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("application/vnd.openxmlformats-officedocument.presentationml.presentation")]
    [InlineData("application/vnd.ms-word.document.macroenabled.12")]
    public async Task OoxmlWithoutFilename_PreservesTheOfficeMimeType(string mime)
    {
        byte[] bytes = [80, 75, 3, 4, 1, 2, 3, 4];
        using var input = new MemoryStream(bytes);
        var inspected = await BrowserPreviewContent.InspectAsync(input, mime, default);
        using var content = inspected.Content;
        Assert.Equal(mime, inspected.ContentType);
        Assert.True(OfficeDocumentPreview.CanConvert(null, inspected.ContentType));
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy);
        Assert.Equal(bytes, copy.ToArray());
    }

    [Fact]
    public async Task MissingRuntime_DisposesOriginalAndReportsRepair()
    {
        var input = new MemoryStream("{\\rtf1 test}"u8.ToArray());
        var source = new LaserficheEdocStream(input, "application/rtf", null, "test.rtf", ".rtf", input.Length, input);
        var error = await Assert.ThrowsAsync<IOException>(() => OfficeDocumentPreview.ConvertAsync(source, default, "missing-converter.exe"));
        Assert.Contains("Repair", error.Message);
        Assert.False(input.CanRead);
    }

    [Fact]
    public async Task OversizedOfficeFile_IsRejectedBeforeStartingAConverter()
    {
        var input = new MemoryStream([1, 2, 3]);
        var source = new LaserficheEdocStream(input, "application/msword", null, "large.doc", ".doc", 101L * 1024 * 1024, input);
        var error = await Assert.ThrowsAsync<IOException>(() => OfficeDocumentPreview.ConvertAsync(source, default));
        Assert.Contains("100 MB", error.Message);
        Assert.False(input.CanRead);
    }

    [Fact]
    public async Task CancelledOfficeRequest_DisposesInputWithoutStartingProcess()
    {
        var executable = Path.GetTempFileName();
        try
        {
            var input = new MemoryStream([1, 2, 3]);
            var source = new LaserficheEdocStream(input, "application/msword", null, "file.doc", ".doc", input.Length, input);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => OfficeDocumentPreview.ConvertAsync(source, cancellation.Token, executable));
            Assert.False(input.CanRead);
        }
        finally { File.Delete(executable); }
    }

    [Fact]
    public async Task BundledOffice_EmptyFileDoesNotBecomeAnEmptyPdfResponse()
    {
        var executable = Environment.GetEnvironmentVariable("DASHBOARD_OFFICE_TEST_PATH");
        if (string.IsNullOrEmpty(executable)) return;
        var input = new MemoryStream();
        var source = new LaserficheEdocStream(input, "application/msword", null, "empty.docx", ".docx", 0, input);
        await Assert.ThrowsAsync<IOException>(() => OfficeDocumentPreview.ConvertAsync(source, default, executable));
        Assert.False(input.CanRead);
    }

    [Fact]
    public async Task ElectronicTiff_PreservesAllFramesInPdf()
    {
        using var pages = new MagickImageCollection();
        pages.Add(new MagickImage(MagickColors.White, 8, 12));
        pages.Add(new MagickImage(MagickColors.Blue, 8, 12));
        var input = new MemoryStream(pages.ToByteArray(MagickFormat.Tiff));
        using var source = new LaserficheEdocStream(input, "image/tiff", null, "scan.tiff", ".tiff", input.Length, input);
        using var output = await BrowserPreviewContent.ConvertTiffDocumentAsync(source, default);
        using var bytes = new MemoryStream();
        await output.Content.CopyToAsync(bytes);
        Assert.Equal("application/pdf", output.ContentType);
        var pdf = Encoding.ASCII.GetString(bytes.ToArray());
        Assert.StartsWith("%PDF-", pdf);
        Assert.Matches(@"/Count\s+2\b", pdf);
    }

    // Run explicitly by Test-OfflinePreview.ps1 against the staged runtime.
    [Theory]
    [InlineData("rtf")]
    [InlineData("docx")]
    [InlineData("xlsx")]
    [InlineData("pptx")]
    public async Task BundledOffice_ProducesPdfWithoutOfficeInstalled(string format)
    {
        var executable = Environment.GetEnvironmentVariable("DASHBOARD_OFFICE_TEST_PATH");
        if (string.IsNullOrEmpty(executable)) return; // Explicit deployment integration gate sets this.
        var bytes = Fixture(format);
        var input = new MemoryStream(bytes);
        using var source = new LaserficheEdocStream(input, "application/octet-stream", null, "document." + format, "." + format, bytes.Length, input);
        using var pdf = await OfficeDocumentPreview.ConvertAsync(source, default, executable);
        using var copy = new MemoryStream();
        await pdf.Content.CopyToAsync(copy);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(copy.ToArray()));
        Assert.True(copy.Length > 100);
    }

    private static byte[] Fixture(string format)
    {
        if (format == "rtf") return Encoding.ASCII.GetBytes(@"{\rtf1\ansi Dashboard offline preview}");
        var (part, mime, xml) = format switch
        {
            "docx" => ("word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml",
                "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Offline Word preview</w:t></w:r></w:p></w:body></w:document>"),
            "xlsx" => ("xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml",
                "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Offline\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>"),
            _ => ("ppt/presentation.xml", "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml",
                "<p:presentation xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><p:sldIdLst><p:sldId id=\"256\" r:id=\"rId1\"/></p:sldIdLst><p:sldSz cx=\"9144000\" cy=\"6858000\"/><p:notesSz cx=\"6858000\" cy=\"9144000\"/></p:presentation>")
        };
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string body) { using var writer = new StreamWriter(zip.CreateEntry(path).Open()); writer.Write(body); }
            var extra = format == "xlsx" ? "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" :
                format == "pptx" ? "<Override PartName=\"/ppt/slides/slide1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slide+xml\"/>" : "";
            Add("[Content_Types].xml", $"<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/{part}\" ContentType=\"{mime}\"/>{extra}</Types>");
            Add("_rels/.rels", $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"{part}\"/></Relationships>");
            Add(part, xml);
            if (format == "xlsx")
            {
                Add("xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
                Add("xl/worksheets/sheet1.xml", "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>Offline Excel preview</t></is></c></row></sheetData></worksheet>");
            }
            if (format == "pptx")
            {
                Add("ppt/_rels/presentation.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide\" Target=\"slides/slide1.xml\"/></Relationships>");
                Add("ppt/slides/slide1.xml", "<p:sld xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><p:cSld><p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/><p:sp><p:nvSpPr><p:cNvPr id=\"2\" name=\"Text\"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr/><p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:t>Offline PowerPoint preview</a:t></a:r></a:p></p:txBody></p:sp></p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>");
            }
        }
        return output.ToArray();
    }
}
