using System.ComponentModel.DataAnnotations;

namespace LFPortal.Web.Options;

/// <summary>Controls the optional, password-authenticated external Dashboard surface.</summary>
public sealed class ExternalShareOptions
{
    public const string SectionName = "ExternalShare";

    public bool Enabled { get; set; }

    [Required]
    public string AccessKey { get; set; } = string.Empty;

    public bool ReadOnly { get; set; } = true;

    /// <summary>
    /// Optional allow-list. When empty, the configured Laserfiche repository is the
    /// sole permitted repository.
    /// </summary>
    public string[] Repositories { get; set; } = [];
}
