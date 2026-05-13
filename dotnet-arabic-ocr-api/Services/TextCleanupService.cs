using System.Text.RegularExpressions;
using ArabicPdfExtraction.Api.Contracts;

namespace ArabicPdfExtraction.Api.Services;

public sealed class TextCleanupService : ITextCleanupService
{
    public string Clean(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var cleaned = text.Replace("\u200f", " ").Replace("\u200e", " ");
        cleaned = Regex.Replace(cleaned, "[ \t]+", " ");
        cleaned = Regex.Replace(cleaned, "\n{3,}", "\n\n");
        return cleaned.Trim();
    }
}
