using Jellyfin.Plugin.WatchRecommendations.Model;
using Jellyfin.Plugin.WatchRecommendations.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using Xunit;

namespace Jellyfin.Plugin.WatchRecommendations.Tests;

public class RecommendationItemsProviderTests
{
    [Theory]
    [InlineData(null, 50)]
    [InlineData(0, 50)]
    [InlineData(-5, 50)]
    [InlineData(16, 16)]
    [InlineData(500, 50)]
    public void BoundsRequestedLimit(int? requested, int expected)
    {
        Assert.Equal(expected, RecommendationItemsProvider.BoundLimit(requested));
    }

    [Fact]
    public void SelectsVisibleLibraryRecommendationsInRankedOrderAndExcludesExternalOrDuplicates()
    {
        var firstId = Guid.NewGuid();
        var externalId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var hiddenId = Guid.NewGuid();
        var cache = Cache(
            Title(firstId, isInLibrary: true),
            Title(externalId, isInLibrary: false),
            Title(secondId, isInLibrary: true),
            Title(firstId, isInLibrary: true),
            Title(hiddenId, isInLibrary: true));
        var items = new Dictionary<Guid, BaseItem>
        {
            [firstId] = new Movie { Id = firstId },
            [secondId] = new Series { Id = secondId },
            [hiddenId] = new Movie { Id = hiddenId }
        };

        var selected = RecommendationItemsProvider.SelectVisibleLibraryItems(
            cache,
            id => items.GetValueOrDefault(id),
            item => item.Id != hiddenId);

        Assert.Equal([firstId, secondId], selected.Select(item => item.Id));
    }

    [Fact]
    public void SkipsStartedWatchedMalformedAndUnresolvableCacheEntries()
    {
        var startedId = Guid.NewGuid();
        var watchedId = Guid.NewGuid();
        var item = new Movie { Id = Guid.NewGuid() };
        var cache = Cache(
            Title(startedId, isInLibrary: true, isStarted: true),
            Title(watchedId, isInLibrary: true, isWatched: true),
            new RecommendationTitle { Id = "not-a-guid", IsInLibrary = true },
            Title(Guid.NewGuid(), isInLibrary: true),
            Title(item.Id, isInLibrary: true));

        var selected = RecommendationItemsProvider.SelectVisibleLibraryItems(
            cache,
            id => id == item.Id ? item : null,
            _ => true);

        Assert.Equal([item.Id], selected.Select(selectedItem => selectedItem.Id));
    }

    [Fact]
    public void FailsClosedWhenVisibilityCheckThrows()
    {
        var itemId = Guid.NewGuid();
        var item = new Movie { Id = itemId };

        var selected = RecommendationItemsProvider.SelectVisibleLibraryItems(
            Cache(Title(itemId, isInLibrary: true)),
            _ => item,
            _ => throw new InvalidOperationException("Visibility unavailable."));

        Assert.Empty(selected);
    }

    private static UserRecommendationCache Cache(params RecommendationTitle[] titles) =>
        new()
        {
            UserId = Guid.NewGuid(),
            Recommendations = titles.Select(title => new ScoredRecommendation { Item = title }).ToList()
        };

    private static RecommendationTitle Title(
        Guid id,
        bool isInLibrary,
        bool isStarted = false,
        bool isWatched = false) =>
        new()
        {
            Id = id.ToString("N"),
            IsInLibrary = isInLibrary,
            IsStarted = isStarted,
            IsWatched = isWatched
        };
}