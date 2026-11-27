using Jellyfin.Plugin.WatchRecommendations.Model;
using Jellyfin.Plugin.WatchRecommendations.Services;
using Xunit;

namespace Jellyfin.Plugin.WatchRecommendations.Tests;

public class RecommendationCacheStoreTests
{
    [Fact]
    public async Task PersistsAndReloadsPerUserResultsWithoutCrossUserLeakage()
    {
        var root = Path.Combine(AppContext.BaseDirectory, $"recommendations-cache-test-{Guid.NewGuid():N}");
        var firstUserId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();
        try
        {
            var store = new RecommendationCacheStore(root);
            await store.ReplaceUserAsync(Cache(firstUserId, "first-user-title"), CancellationToken.None);
            await store.ReplaceUserAsync(Cache(secondUserId, "second-user-title"), CancellationToken.None);

            var reloadedStore = new RecommendationCacheStore(root);

            Assert.Equal("first-user-title", reloadedStore.Get(firstUserId)?.Recommendations.Single().Item.Name);
            Assert.Equal("second-user-title", reloadedStore.Get(secondUserId)?.Recommendations.Single().Item.Name);
            Assert.Null(reloadedStore.Get(Guid.NewGuid()));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static UserRecommendationCache Cache(Guid userId, string title) =>
        new()
        {
            UserId = userId,
            RefreshedAtUtc = DateTime.UtcNow,
            Recommendations =
            [
                new ScoredRecommendation
                {
                    Item = new RecommendationTitle { Id = title, Name = title, MediaType = "Movie" },
                    Score = 0.75
                }
            ]
        };
}