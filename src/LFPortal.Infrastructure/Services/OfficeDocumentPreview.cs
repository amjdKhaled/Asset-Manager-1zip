using System.Diagnostics;
using LFPortal.Application.DTOs;

namespace LFPortal.Infrastructure.Services;

/// <summary>Converts Office documents locally using the runtime shipped with Dashboard.</summary>
public static class OfficeDocumentPreview
{
    private static readonly SemaphoreSlim Slots = new(2);
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    { ".doc", ".docx", ".docm", ".dot", ".dotx", ".dotm", ".rtf", ".odt", ".ott", ".xls", ".xlsx",
      ".xlsm", ".xlsb", ".xlt", ".xltx", ".xltm", ".ods", ".ots", ".csv", ".tsv", ".ppt", ".pptx", ".pps", ".ppsx",
      ".pptm", ".potx", ".potm", ".ppsm", ".odp", ".otp", ".odg", ".vsd", ".vsdx", ".txt" };

    public static string? GetExtension(string? fileName, string? contentType)
    {
        var extension = Path.GetExtension(fileName ?? "");
        if (Extensions.Contains(extension)) return extension.ToLowerInvariant();
        return contentType?.ToLowerInvariant() switch
        {
            "application/msword" => ".doc",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
            "application/vnd.ms-excel" => ".xls",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
            "application/vnd.ms-powerpoint" => ".ppt",
            "application/vnd.openxmlformats-officedocument.presentationml.presentation" => ".pptx",
            "application/vnd.oasis.opendocument.text" => ".odt",
            "application/vnd.oasis.opendocument.spreadsheet" => ".ods",
            "application/vnd.oasis.opendocument.presentation" => ".odp",
            "application/rtf" or "text/rtf" => ".rtf",
            "text/plain" => ".txt",
            "text/csv" => ".csv",
            _ => null
        };
    }

    public static bool CanConvert(string? fileName, string? contentType) => GetExtension(fileName, contentType) is not null;

    public static async Task<LaserficheEdocStream> ConvertAsync(
        LaserficheEdocStream source, CancellationToken cancellationToken, string? executable = null)
    {
        using (source)
        {
            var extension = GetExtension(source.FileName, source.ContentType)
                ?? throw new NotSupportedException("No local converter supports this file type.");
            executable ??= Path.Combine(AppContext.BaseDirectory, "tools", "LibreOffice", "program", "soffice.com");
            if (!File.Exists(executable))
                throw new IOException("The bundled Office preview converter is missing. Repair the Dashboard installation.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            await Slots.WaitAsync(timeout.Token).ConfigureAwait(false);
            var directory = Path.Combine(Path.GetTempPath(), "Dashboard-preview-" + Guid.NewGuid().ToString("N"));
            Process? process = null;
            try
            {
                Directory.CreateDirectory(directory);
                var input = Path.Combine(directory, "document" + extension);
                await using (var file = File.Create(input))
                {
                    var buffer = new byte[81920];
                    long total = 0;
                    int count;
                    while ((count = await source.Content.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
                    {
                        total += count;
                        if (total > 100 * 1024 * 1024)
                            throw new IOException("Office preview exceeds the 100 MB conversion limit. Download the original file.");
                        await file.WriteAsync(buffer.AsMemory(0, count), timeout.Token).ConfigureAwait(false);
                    }
                }

                // Every conversion gets a fresh profile, so parallel jobs and an
                // interactive LibreOffice instance cannot capture each other's work.
                var profile = Path.Combine(directory, "profile");
                Directory.CreateDirectory(Path.Combine(profile, "user"));
                await File.WriteAllTextAsync(Path.Combine(profile, "user", "registrymodifications.xcu"),
                    """
                    <?xml version="1.0" encoding="UTF-8"?>
                    <oor:items xmlns:oor="http://openoffice.org/2001/registry">
                      <item oor:path="/org.openoffice.Office.Common/Security/Scripting"><prop oor:name="MacroSecurityLevel" oor:op="fuse"><value>3</value></prop></item>
                    </oor:items>
                    """, timeout.Token).ConfigureAwait(false);
                var start = new ProcessStartInfo(executable)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var argument in new[] { "-env:UserInstallation=" + new Uri(profile + Path.DirectorySeparatorChar).AbsoluteUri,
                    "--headless", "--nologo", "--nodefault", "--norestore", "--convert-to", "pdf", "--outdir", directory, input })
                    start.ArgumentList.Add(argument);
                process = Process.Start(start) ?? throw new IOException("Could not start the Office preview converter.");
                var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
                var pdf = Path.Combine(directory, "document.pdf");
                if (process.ExitCode != 0 || !File.Exists(pdf))
                    throw new IOException("Office preview failed. The file may be corrupt, encrypted, or unsupported.");
                var bytes = await File.ReadAllBytesAsync(pdf, timeout.Token).ConfigureAwait(false);
                if (!bytes.AsSpan().StartsWith("%PDF-"u8))
                    throw new IOException("The local converter did not produce a valid PDF.");
                var output = new MemoryStream(bytes, writable: false);
                return new LaserficheEdocStream(output, "application/pdf", null, "preview.pdf", ".pdf", bytes.Length, output);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Office preview conversion timed out. Download the original file or retry.");
            }
            finally
            {
                if (process is not null)
                {
                    try
                    {
                        if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync().ConfigureAwait(false); }
                    }
                    catch (InvalidOperationException) { /* Process exited between the check and kill. */ }
                    process.Dispose();
                }
                try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
                catch (IOException) { /* OS/antivirus may briefly retain a handle; the temp folder is isolated. */ }
                catch (UnauthorizedAccessException) { /* Do not replace a conversion result with a cleanup error. */ }
                finally { Slots.Release(); }
            }
        }
    }
}
