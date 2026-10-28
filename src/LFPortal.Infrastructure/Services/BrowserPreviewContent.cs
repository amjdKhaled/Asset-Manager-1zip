using ImageMagick;
using System.IO.Compression;
using LFPortal.Application.DTOs;

namespace LFPortal.Infrastructure.Services;

/// <summary>Identifies binary preview content without losing its leading bytes.</summary>
public static class BrowserPreviewContent
{
    public static async Task<(Stream Content, string ContentType)> InspectAsync(
        Stream source, string contentType, CancellationToken cancellationToken)
    {
        var prefix = new byte[16];
        var count = 0;
        while (count < prefix.Length)
        {
            var read = await source.ReadAsync(prefix.AsMemory(count), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            count += read;
        }

        // Prefer actual file signatures over a missing or generic HTTP header.
        var detected = DetectContentType(prefix.AsSpan(0, count)) ?? contentType;
        // OOXML is a ZIP container. Do not erase a useful Office MIME type
        // when Laserfiche omits the filename/extension from the download.
        if (detected == "application/zip" && OfficeDocumentPreview.CanConvert(null, contentType))
            detected = contentType;
        return (new PrefixStream(prefix.AsMemory(0, count), source), detected);
    }

    internal static string? DetectContentType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(new byte[] { 80, 75, 3, 4 })) return "application/zip";
        if (bytes.StartsWith("%PDF-"u8)) return "application/pdf";
        if (bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
        if (bytes.StartsWith(new byte[] { 255, 216, 255 })) return "image/jpeg";
        if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8)) return "image/gif";
        if (bytes.StartsWith("BM"u8)) return "image/bmp";
        if (bytes.StartsWith(new byte[] { 73, 73, 42, 0 }) ||
            bytes.StartsWith(new byte[] { 77, 77, 0, 42 }) ||
            bytes.StartsWith(new byte[] { 73, 73, 43, 0 }) ||
            bytes.StartsWith(new byte[] { 77, 77, 0, 43 })) return "image/tiff";
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) &&
            bytes.Slice(8, 4).SequenceEqual("WEBP"u8)) return "image/webp";
        return null;
    }

    /// <summary>Unpacks a single exported page and ensures it is a browser image.</summary>
    public static async Task<LaserficheEdocStream> PreparePageImageAsync(
        LaserficheEdocStream source, CancellationToken cancellationToken)
    {
        if (source.ContentType == "image/tiff")
            return await ConvertTiffAsync(source, cancellationToken).ConfigureAwait(false);
        if (source.ContentType != "application/zip") return source;

        using (source)
        {
            using var input = new MemoryStream();
            await source.Content.CopyToAsync(input, cancellationToken).ConfigureAwait(false);
            input.Position = 0;
            using var zip = new ZipArchive(input, ZipArchiveMode.Read);
            var files = zip.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)).ToArray();
            if (files.Length != 1)
                throw new InvalidDataException("Single-page export did not contain exactly one image.");
            using var file = files[0].Open();
            var output = new MemoryStream();
            try
            {
                await file.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                output.Position = 0;
                var inspected = await InspectAsync(output, "application/octet-stream", cancellationToken)
                    .ConfigureAwait(false);
                if (!inspected.ContentType.StartsWith("image/", StringComparison.Ordinal))
                    throw new InvalidDataException("Single-page export did not contain an image.");
                var result = new LaserficheEdocStream(inspected.Content, inspected.ContentType,
                    null, files[0].Name, Path.GetExtension(files[0].Name), output.Length, output);
                return inspected.ContentType == "image/tiff"
                    ? await ConvertTiffAsync(result, cancellationToken).ConfigureAwait(false)
                    : result;
            }
            catch
            {
                output.Dispose();
                throw;
            }
        }
    }

    /// <summary>Converts only the requested TIFF page locally; the original stays unchanged.</summary>
    public static async Task<LaserficheEdocStream> ConvertTiffAsync(
        LaserficheEdocStream source, CancellationToken cancellationToken)
    {
        using (source)
        {
            using var input = new MemoryStream();
            await source.Content.CopyToAsync(input, cancellationToken).ConfigureAwait(false);
            input.Position = 0;
            cancellationToken.ThrowIfCancellationRequested();
            var settings = new MagickReadSettings
            {
                Format = MagickFormat.Tiff,
                FrameIndex = 0,
                FrameCount = 1
            };
            using var image = new MagickImage(input, settings);
            var bytes = image.ToByteArray(MagickFormat.Png);
            cancellationToken.ThrowIfCancellationRequested();
            var output = new MemoryStream(bytes, writable: false);
            return new LaserficheEdocStream(output, "image/png", null,
                "preview.png", ".png", bytes.Length, output);
        }
    }

    /// <summary>Preserves every frame of an electronic multi-page TIFF in a PDF.</summary>
    public static async Task<LaserficheEdocStream> ConvertTiffDocumentAsync(
        LaserficheEdocStream source, CancellationToken cancellationToken)
    {
        using (source)
        {
            using var input = new MemoryStream();
            await source.Content.CopyToAsync(input, cancellationToken).ConfigureAwait(false);
            input.Position = 0;
            cancellationToken.ThrowIfCancellationRequested();
            using var pages = new MagickImageCollection(input);
            var bytes = pages.ToByteArray(MagickFormat.Pdf);
            cancellationToken.ThrowIfCancellationRequested();
            var output = new MemoryStream(bytes, writable: false);
            return new LaserficheEdocStream(output, "application/pdf", null, "preview.pdf", ".pdf", bytes.Length, output);
        }
    }

    /// <summary>Spools to a seekable file with automatic deletion after MVC finishes.</summary>
    public static async Task<(Stream Content, string ContentType)> BufferForPreviewAsync(
        LaserficheEdocStream source, CancellationToken cancellationToken)
    {
        using (source)
        {
            var output = new FileStream(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()),
                FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920,
                FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            try
            {
                await source.Content.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                if (output.Length == 0 || (source.ContentLength is > 0 && output.Length != source.ContentLength))
                    throw new IOException("Laserfiche returned an empty or incomplete preview file.");
                output.Position = 0;
                return (output, source.ContentType);
            }
            catch
            {
                await output.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
    }

    private sealed class PrefixStream(ReadOnlyMemory<byte> prefix, Stream source) : Stream
    {
        private int _position;
        public override bool CanRead => source.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            if (_position >= prefix.Length) return source.Read(buffer);
            var count = Math.Min(buffer.Length, prefix.Length - _position);
            prefix.Span.Slice(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position < prefix.Length) return Read(buffer.Span);
            return await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) source.Dispose();
            base.Dispose(disposing);
        }
    }
}
