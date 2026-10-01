using System.Reflection;
using LFPortal.Application.Interfaces;
using LFPortal.Domain.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using LFPortal.Application.DTOs;
using LFPortal.Domain.Entities;
using LFPortal.Infrastructure.Services;
using Xunit;

namespace LFPortal.Infrastructure.Tests;

public sealed class CachedLaserficheDashboardServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CachedSnapshot_RequiresLiveRepositorySession(bool deleted)
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var snapshot = new DashboardStatsDto { IsConnected = true, TotalDocuments = 73 };
        cache.Set("dashboard:v2:https://lf.test:repo:fallback:fallback", snapshot);
        var repo = DispatchProxy.Create<IRepositoryContext, Stub>();
        ((Stub)(object)repo).Call = _ => Task.FromResult(new RepositoryDescriptor("test", "https://lf.test", "repo", "repo"));
        var entries = DispatchProxy.Create<ILaserficheEntryService, Stub>();
        var reads = 0;
        ((Stub)(object)entries).Call = method => method.Name == "GetRootEntryIdAsync"
            ? Task.FromResult(1)
            : ++reads > 0 && deleted
                ? Task.FromException<LFEntry>(new UnauthorizedAccessException("Deleted repository session"))
                : Task.FromResult(new LFEntry { Id = 1, Name = "Root", EntryType = LFEntryType.Folder });
        var service = new CachedLaserficheDashboardService(null!, repo, new HttpContextAccessor(), cache,
            NullLogger<CachedLaserficheDashboardService>.Instance, entries);
        if (deleted)
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetDashboardStatsAsync());
        else
            Assert.Same(snapshot, await service.GetDashboardStatsAsync());
        Assert.Equal(1, reads);
    }

    public class Stub : DispatchProxy
    {
        public Func<MethodInfo, object?> Call { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Call(method!);
    }

    [Fact]
    public void Compact_ReleasesLargeEntryCollectionsButPreservesExactSummaries()
    {
        var activity = new[]
        {
            new DocumentActivityDayDto
            {
                Date = new DateOnly(2026, 9, 14),
                Created = 1_250,
                Modified = 340
            }
        };
        var users = new[]
        {
            new UserDocumentActivityDto
            {
                Name = "ADMIN",
                Created = 1_250,
                LastActivity = new DateTimeOffset(2026, 9, 14, 9, 30, 0, TimeSpan.Zero)
            }
        };
        var documents = Enumerable.Range(1, 150)
            .Select(id => new LFEntry { Id = id, Name = $"Document {id}", EntryType = LFEntryType.Document })
            .ToList();

        var compact = CachedLaserficheDashboardService.Compact(new DashboardStatsDto
        {
            IsConnected = true,
            TotalDocuments = 1_250,
            AllDocs = documents,
            AllFolders = [new LFEntry { Id = 500, Name = "Folder", EntryType = LFEntryType.Folder }],
            RecentDocs = documents,
            ModifiedDocs = documents,
            DocumentActivityByDay = activity,
            UserDocumentActivity = users
        });

        Assert.Empty(compact.AllDocs);
        Assert.Empty(compact.AllFolders);
        Assert.Equal(100, compact.RecentDocs.Count);
        Assert.Equal(100, compact.ModifiedDocs.Count);
        Assert.Equal(1_250, compact.TotalDocuments);
        Assert.Same(activity, compact.DocumentActivityByDay);
        Assert.Same(users, compact.UserDocumentActivity);
    }
}
