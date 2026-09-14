using System.Globalization;
using LFPortal.Application.DTOs;
using LFPortal.Domain.Entities;

namespace LFPortal.Web.Controllers;

/// <summary>
/// Applies dashboard drill-down filters to the exact authoritative entry sets
/// used to calculate the dashboard. This keeps every displayed result count in
/// sync with the statistic that opened it.
/// </summary>
public static class ArchiveDrillDown
{
    public static ArchiveDrillDownResult Apply(
        DashboardStatsDto stats,
        string scope,
        string? template = null,
        string? folder = null,
        string? creator = null,
        string? date = null,
        string? activity = null,
        int entryId = 0)
    {
        ArgumentNullException.ThrowIfNull(stats);

        var normalizedScope = (scope ?? string.Empty).Trim().ToLowerInvariant();
        IReadOnlyList<LFEntry> entries;
        IReadOnlyList<ArchiveTemplateResult> templates = [];
        string title;
        string description;
        var openEntryId = 0;

        switch (normalizedScope)
        {
            case "documents":
                entries = SortDocuments(stats.AllDocs);
                title = "All documents";
                description = "Documents counted by the dashboard";
                break;

            case "folders":
                entries = stats.AllFolders
                    .OrderBy(e => e.FullPath, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                    .AsReadOnly();
                title = "All folders";
                description = "Folders counted by the dashboard";
                break;

            case "templates":
                entries = [];
                var counts = stats.TemplateStats.ToDictionary(
                    item => item.Name,
                    item => item.Count,
                    StringComparer.OrdinalIgnoreCase);
                templates = stats.TemplateDefinitions
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(item => new ArchiveTemplateResult
                    {
                        Id = item.Id,
                        Name = item.Name,
                        Description = item.Description,
                        DocumentCount = counts.GetValueOrDefault(item.Name)
                    })
                    .ToList()
                    .AsReadOnly();
                title = "Template definitions";
                description = "Templates returned by the active repository";
                break;

            case "with-template":
                entries = SortDocuments(stats.AllDocs.Where(HasTemplate));
                title = "Documents with a template";
                description = "Documents counted in the template-assigned KPI";
                break;

            case "without-template":
                entries = SortDocuments(stats.AllDocs.Where(entry => !HasTemplate(entry)));
                title = "Documents without a template";
                description = "Documents counted in the no-template KPI";
                break;

            case "template":
                var templateName = (template ?? string.Empty).Trim();
                entries = SortDocuments(stats.AllDocs.Where(entry =>
                    string.Equals(GetTemplateKey(entry), templateName, StringComparison.OrdinalIgnoreCase)));
                title = string.IsNullOrWhiteSpace(templateName)
                    ? "Template documents"
                    : $"Template: {templateName}";
                description = "Documents counted for this template";
                break;

            case "root-folder":
                var folderName = (folder ?? string.Empty).Trim();
                var folderStat = entryId > 0
                    ? stats.RootFolders.FirstOrDefault(item => item.EntryId == entryId)
                    : stats.RootFolders.FirstOrDefault(item =>
                        string.Equals(item.Name, folderName, StringComparison.OrdinalIgnoreCase));
                var ids = folderStat?.DocumentIds.ToHashSet() ?? [];
                entries = SortDocuments(stats.AllDocs.Where(entry => ids.Contains(entry.Id)));
                var resolvedFolderName = folderStat?.Name ?? folderName;
                title = string.IsNullOrWhiteSpace(resolvedFolderName)
                    ? "Folder documents"
                    : $"Folder: {resolvedFolderName}";
                description = "Documents counted below this top-level folder";
                break;

            case "creator":
                var creatorName = (creator ?? string.Empty).Trim();
                entries = SortDocuments(stats.AllDocs.Where(entry => CreatorMatches(entry, creatorName)));
                title = string.IsNullOrWhiteSpace(creatorName)
                    ? "Documents by user"
                    : $"Created by: {creatorName}";
                description = "Documents counted for this creator";
                break;

            case "activity":
                var day = ParseDate(date);
                var isModified = string.Equals(activity, "modified", StringComparison.OrdinalIgnoreCase);
                var source = isModified ? stats.ModifiedDocs : stats.AllDocs;
                entries = day is null
                    ? []
                    : SortDocuments(source.Where(entry =>
                        GetActivityDate(entry, isModified) == day.Value));
                title = day is null
                    ? "Document activity"
                    : $"{(isModified ? "Modified" : "Created")} documents — {day.Value:yyyy-MM-dd}";
                description = "Documents represented by the selected activity bar";
                break;

            case "document":
                var match = stats.AllDocs.FirstOrDefault(entry => entry.Id == entryId);
                entries = match is null ? [] : [match];
                title = match?.Name ?? "Document";
                description = "Selected dashboard document";
                openEntryId = match?.Id ?? 0;
                break;

            default:
                entries = [];
                title = "Dashboard results";
                description = "The requested dashboard filter is not supported.";
                break;
        }

        return new ArchiveDrillDownResult
        {
            Scope = normalizedScope,
            Title = title,
            Description = description,
            Entries = entries,
            Templates = templates,
            OpenEntryId = openEntryId
        };
    }

    private static IReadOnlyList<LFEntry> SortDocuments(IEnumerable<LFEntry> entries) =>
        entries
            .OrderByDescending(entry => entry.CreationTime ?? DateTimeOffset.MinValue)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();

    private static bool HasTemplate(LFEntry entry) =>
        entry.EntryType == LFEntryType.Document &&
        (entry.TemplateId is > 0 || !string.IsNullOrWhiteSpace(entry.TemplateName));

    private static string GetTemplateKey(LFEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.TemplateName)
            ? entry.TemplateName.Trim()
            : entry.TemplateId is > 0
                ? $"Template #{entry.TemplateId}"
                : string.Empty;

    private static bool CreatorMatches(LFEntry entry, string creator) =>
        string.Equals(
            string.IsNullOrWhiteSpace(entry.Creator) ? "Unknown" : entry.Creator.Trim(),
            string.IsNullOrWhiteSpace(creator) ? "Unknown" : creator,
            StringComparison.OrdinalIgnoreCase);

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
                ? parsed
                : null;

    private static DateOnly? GetActivityDate(LFEntry entry, bool modified)
    {
        var value = modified ? entry.LastModifiedTime : entry.CreationTime;
        return value.HasValue ? DateOnly.FromDateTime(value.Value.LocalDateTime) : null;
    }
}

public sealed record ArchiveDrillDownResult
{
    public string Scope { get; init; } = string.Empty;
    public string Title { get; init; } = "Dashboard results";
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<LFEntry> Entries { get; init; } = [];
    public IReadOnlyList<ArchiveTemplateResult> Templates { get; init; } = [];
    public int OpenEntryId { get; init; }
}

public sealed record ArchiveTemplateResult
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int DocumentCount { get; init; }
}

/// <summary>Builds the same Laserfiche search used by an Archive drill-down and Web Client URL.</summary>
public sealed record LaserficheArchiveQuery
{
    public string Scope { get; init; } = string.Empty;
    public string Title { get; init; } = "Dashboard results";
    public string Description { get; init; } = string.Empty;
    public string Expression { get; init; } = "{LF:Name=\"*\",Type=\"D\"}";
    public bool IsTemplateCatalog { get; init; }
    public int OpenEntryId { get; init; }

    public static LaserficheArchiveQuery Build(
        string scope,
        string? template,
        string? folder,
        string? creator,
        string? date,
        string? activity,
        int entryId,
        int rootEntryId)
    {
        const string documents = "{LF:Name=\"*\",Type=\"D\"}";
        var normalized = (scope ?? string.Empty).Trim().ToLowerInvariant();
        var day = DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed) ? parsed : (DateOnly?)null;

        return normalized switch
        {
            "documents" => Create(normalized, "All documents", "Documents counted by the dashboard", documents),
            "folders" => Create(normalized, "All folders", "Folders counted by the dashboard",
                rootEntryId > 0
                    ? $"{{LF:Name=\"*\",Type=\"F\"}} - {{LF:ID={rootEntryId}}}"
                    : "{LF:Name=\"*\",Type=\"F\"}"),
            "templates" => new LaserficheArchiveQuery
            {
                Scope = normalized, Title = "Template definitions",
                Description = "Templates returned by the active repository", IsTemplateCatalog = true
            },
            "with-template" => Create(normalized, "Documents with a template",
                "Documents counted in the template-assigned KPI",
                $"{documents} - {{LF:Name=\"*\",Type=\"B\"}}"),
            "without-template" => Create(normalized, "Documents without a template",
                "Documents counted in the no-template KPI", "{LF:Name=\"*\",Type=\"B\"}"),
            "template" => Create(normalized, $"Template: {(template ?? string.Empty).Trim()}",
                "Documents counted for this template",
                $"{{LF:Template=\"{Escape(template)}\"}}"),
            "root-folder" when entryId > 0 => Create(normalized,
                $"Folder: {(folder ?? string.Empty).Trim()}", "Documents counted below this top-level folder",
                $"{{LF:LookIn=\"{entryId}\",Subfolders=y}} & {documents}"),
            "creator" => Create(normalized, $"Created by: {(creator ?? string.Empty).Trim()}",
                "Documents counted for this creator",
                $"{documents} & {{LF:Creator=\"{Escape(creator)}\"}}"),
            "activity" when day.HasValue => BuildActivity(normalized, day.Value, activity, documents),
            "document" when entryId > 0 => new LaserficheArchiveQuery
            {
                Scope = normalized, Title = "Document", Description = "Selected dashboard document",
                Expression = $"{{LF:ID={entryId}}}", OpenEntryId = entryId
            },
            _ => Create(normalized, "Dashboard results", "The requested dashboard filter is not supported.",
                "{LF:ID=0}")
        };
    }

    private static LaserficheArchiveQuery BuildActivity(
        string scope, DateOnly day, string? activity, string documents)
    {
        var modified = string.Equals(activity, "modified", StringComparison.OrdinalIgnoreCase);
        var field = modified ? "Modified" : "Created";
        var start = day.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
        var end = day.AddDays(1).ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
        return Create(scope, $"{field} documents — {day:yyyy-MM-dd}",
            "Documents represented by the selected activity bar",
            $"{documents} & {{LF:{field}>=\"{start}\"}} & {{LF:{field}<\"{end}\"}}");
    }

    private static LaserficheArchiveQuery Create(string scope, string title, string description, string expression) =>
        new() { Scope = scope, Title = title, Description = description, Expression = expression };

    private static string Escape(string? value) =>
        (value ?? string.Empty).Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
}
