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

    [Fact]
    public void PagedArchiveQueries_UseEfficientLaserficheEntryTypeFilters()
    {
        Assert.Equal("{LF:Name=\"*\",Type=\"D\"}",
            LaserficheArchiveQuery.Build("documents", null, null, null, null, null, 0, 1).Expression);
        Assert.Equal("{LF:Name=\"*\",Type=\"B\"}",
            LaserficheArchiveQuery.Build("without-template", null, null, null, null, null, 0, 1).Expression);
        Assert.Contains("Type=\"F\"", LaserficheArchiveQuery.Build(
            "folders", null, null, null, null, null, 0, 1).Expression);
        Assert.Equal("{LF:TemplateName=\"General (2)\"}", LaserficheArchiveQuery.Build(
            "template", "General (2)", null, null, null, null, 0, 1).Expression);
    }

    [Fact]
    public void DashboardCounts_KeepArchivePagingAccurateWhenApiOmitsODataCount()
    {
        var stats = Stats() with
        {
            TotalDocuments = 1_234,
            TotalFolders = 321,
            DocsWithTemplate = 800,
            DocsWithoutTemplate = 434,
            UserDocumentActivity =
            [
                new UserDocumentActivityDto { Name = "ADMIN", Created = 700 }
            ],
            DocumentActivityByDay =
            [
                new DocumentActivityDayDto
                {
                    Date = new DateOnly(2026, 9, 14), Created = 25, Modified = 12
                }
            ]
        };

        Assert.Equal(1_234, ArchiveController.ResolveDashboardCount(
            stats, "documents", null, null, null, null, 0));
        Assert.Equal(321, ArchiveController.ResolveDashboardCount(
            stats, "folders", null, null, null, null, 0));
        Assert.Equal(1, ArchiveController.ResolveDashboardCount(
            stats, "template", "Invoices", null, null, null, 0));
        Assert.Equal(700, ArchiveController.ResolveDashboardCount(
            stats, "creator", null, "admin", null, null, 0));
        Assert.Equal(12, ArchiveController.ResolveDashboardCount(
            stats, "activity", null, null, "2026-09-14", "modified", 0));
    }

    [Fact]
    public void FolderAndActivityQueries_PreserveTheExactDashboardFilter()
    {
        var folder = LaserficheArchiveQuery.Build(
            "root-folder", null, "Policies", null, null, null, 42, 1);
        Assert.Contains("LookIn=\"42\"", folder.Expression);
        Assert.Contains("Subfolders=y", folder.Expression);

        var activity = LaserficheArchiveQuery.Build(
            "activity", null, null, null, "2026-09-14", "modified", 0, 1);
        Assert.Contains("Modified>=\"09/14/2026\"", activity.Expression);
        Assert.Contains("Modified<\"09/15/2026\"", activity.Expression);
    }

    [Fact]
    public void QueryValues_AreEscapedBeforeTheyEnterLaserficheSyntax()
    {
        var query = LaserficheArchiveQuery.Build(
            "creator", null, null, "DOMAIN\\a\"user", null, null, 0, 1);
        Assert.Contains("DOMAIN\\\\a\\\"user", query.Expression);
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
