using ImageMagick;
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
        return (new PrefixStream(prefix.AsMemory(0, count), source), detected);
    }

    internal static string? DetectContentType(ReadOnlySpan<byte> bytes)
    {
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
