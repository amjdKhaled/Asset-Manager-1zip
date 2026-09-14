using LFPortal.Application.DTOs;
using LFPortal.Domain.Entities;
using LFPortal.Web.Controllers;
using Xunit;

namespace LFPortal.Web.Tests;

public sealed class ArchiveDrillDownTests
{
    private static readonly DateTimeOffset Day = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

    private static DashboardStatsDto Stats() => new()
    {
        IsConnected = true,
        AllDocs =
        [
            Doc(1, "A", "Invoices", "ADMIN", Day, Day.AddHours(1)),
            Doc(2, "B", null, "ADMIN", Day, Day),
            Doc(3, "C", "HR", "USER", Day.AddDays(-1), Day)
        ],
        ModifiedDocs =
        [
            Doc(1, "A", "Invoices", "ADMIN", Day, Day.AddHours(1)),
            Doc(3, "C", "HR", "USER", Day.AddDays(-1), Day)
        ],
        AllFolders =
        [
            new LFEntry { Id = 10, Name = "Root A", EntryType = LFEntryType.Folder },
            new LFEntry { Id = 11, Name = "Child", EntryType = LFEntryType.Folder }
        ],
        RootFolders =
        [
            new RootFolderStatDto { EntryId = 10, Name = "Root A", Documents = 2, DocumentIds = [1, 2] }
        ],
        TemplateStats =
        [
            new TemplateStatDto { Name = "Invoices", Count = 1 },
            new TemplateStatDto { Name = "HR", Count = 1 }
        ],
        TemplateDefinitions =
        [
            new LFTemplateDefinition { Id = 7, Name = "Invoices" },
            new LFTemplateDefinition { Id = 8, Name = "Unused" }
        ]
    };

    [Fact]
    public void KpiScopes_ReturnTheSameAuthoritativeSets()
    {
        var stats = Stats();

        Assert.Equal(3, ArchiveDrillDown.Apply(stats, "documents").Entries.Count);
        Assert.Equal(2, ArchiveDrillDown.Apply(stats, "folders").Entries.Count);
        Assert.Equal(2, ArchiveDrillDown.Apply(stats, "with-template").Entries.Count);
        Assert.Single(ArchiveDrillDown.Apply(stats, "without-template").Entries);
        Assert.Equal(2, ArchiveDrillDown.Apply(stats, "templates").Templates.Count);
    }

    [Fact]
    public void ChartAndTableScopes_ReturnExactDocuments()
    {
        var stats = Stats();

        Assert.Equal(new[] { 1, 2 }, ArchiveDrillDown.Apply(stats, "root-folder", folder: "Wrong duplicate name", entryId: 10).Entries.Select(x => x.Id).OrderBy(x => x));
        Assert.Equal(1, Assert.Single(ArchiveDrillDown.Apply(stats, "template", template: "Invoices").Entries).Id);
        Assert.Equal(2, ArchiveDrillDown.Apply(stats, "creator", creator: "ADMIN").Entries.Count);
        Assert.Equal(new[] { 1, 2 }, ArchiveDrillDown.Apply(stats, "activity", date: "2026-09-14", activity: "created").Entries.Select(x => x.Id).OrderBy(x => x));
        Assert.Equal(new[] { 1, 3 }, ArchiveDrillDown.Apply(stats, "activity", date: "2026-09-14", activity: "modified").Entries.Select(x => x.Id).OrderBy(x => x));
    }

    [Fact]
    public void DocumentScope_OpensOnlyTheSelectedDocument()
    {
        var result = ArchiveDrillDown.Apply(Stats(), "document", entryId: 3);

        Assert.Equal(3, Assert.Single(result.Entries).Id);
        Assert.Equal(3, result.OpenEntryId);
    }

    private static LFEntry Doc(
        int id,
        string name,
        string? template,
        string creator,
        DateTimeOffset created,
        DateTimeOffset modified) => new()
    {
        Id = id,
        Name = name,
        EntryType = LFEntryType.Document,
        TemplateName = template,
        TemplateId = template is null ? null : 1,
        Creator = creator,
        CreationTime = created,
        LastModifiedTime = modified
    };
}
