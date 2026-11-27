using Jellyfin.Plugin.WatchRecommendations.Model;
using Jellyfin.Plugin.WatchRecommendations.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.WatchRecommendations.Tests;

public class RecommendationsRefreshServiceTests
{
    [Fact]
    public async Task FailedUserRefreshKeepsPriorCacheAndContinuesWithOtherUsers()
    {
        var root = Path.Combine(AppContext.BaseDirectory, $"recommendations-refresh-test-{Guid.NewGuid():N}");
        var failedUserId = Guid.NewGuid();
        var successfulUserId = Guid.NewGuid();
        try
        {
            var cache = new RecommendationCacheStore(root);
            await cache.ReplaceUserAsync(new UserRecommendationCache
            {
                UserId = failedUserId,
                RefreshedAtUtc = DateTime.UtcNow.AddDays(-1),
                Recommendations = [new ScoredRecommendation { Item = new RecommendationTitle { Name = "previous" }, Score = 1 }]
            }, CancellationToken.None);

            var source = new FakeDataSource(failedUserId, successfulUserId);
            var seerrProvider = new FakeSeerrCandidateProvider(
                [new RecommendationTitle { Id = "external", Name = "external", MediaType = "Movie", IsInLibrary = false }]);
            var service = new RecommendationsRefreshService(
                source,
                seerrProvider,
                new RecommendationRanker(),
                cache,
                NullLogger<RecommendationsRefreshService>.Instance);

            await service.RefreshAllUsersAsync(new NoOpProgress(), CancellationToken.None);

            Assert.Equal("previous", cache.Get(failedUserId)?.Recommendations.Single().Item.Name);
            Assert.Contains(cache.Get(successfulUserId)!.Recommendations, item => item.Item.Name == "external");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CancellationStopsRefreshBeforeReplacingAnyUserCache()
    {
        var root = Path.Combine(AppContext.BaseDirectory, $"recommendations-cancel-test-{Guid.NewGuid():N}");
        try
        {
            var cache = new RecommendationCacheStore(root);
            var service = new RecommendationsRefreshService(
                new FakeDataSource(Guid.NewGuid()),
                new FakeSeerrCandidateProvider(),
                new RecommendationRanker(),
                cache,
                NullLogger<RecommendationsRefreshService>.Instance);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.RefreshAllUsersAsync(new NoOpProgress(), cancellation.Token));
            Assert.Empty(Directory.Exists(root) ? Directory.GetFiles(root, "*.json", SearchOption.AllDirectories) : Array.Empty<string>());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SeerrProviderFailurePreservesPriorCache()
    {
        var root = Path.Combine(AppContext.BaseDirectory, $"recommendations-seerr-failure-test-{Guid.NewGuid():N}");
        var userId = Guid.NewGuid();
        try
        {
            var cache = new RecommendationCacheStore(root);
            await cache.ReplaceUserAsync(new UserRecommendationCache
            {
                UserId = userId,
                RefreshedAtUtc = DateTime.UtcNow.AddDays(-1),
                Recommendations = [new ScoredRecommendation { Item = new RecommendationTitle { Name = "previous" }, Score = 1 }]
            }, CancellationToken.None);
            var service = new RecommendationsRefreshService(
                new FakeDataSource(userId),
                new FakeSeerrCandidateProvider(exception: new InvalidOperationException("Provider unavailable")),
                new RecommendationRanker(),
                cache,
                NullLogger<RecommendationsRefreshService>.Instance);

            await service.RefreshAllUsersAsync(new NoOpProgress(), CancellationToken.None);

            Assert.Equal("previous", cache.Get(userId)?.Recommendations.Single().Item.Name);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class FakeDataSource : IRecommendationDataSource
    {
        private readonly Guid[] _userIds;

        public FakeDataSource(params Guid[] userIds)
        {
            _userIds = userIds;
        }

        public IReadOnlyList<Guid> GetUserIds() => _userIds;

        public RecommendationProfile LoadProfile(Guid userId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (userId == _userIds[0] && _userIds.Length > 1)
            {
                throw new InvalidOperationException("Fixture failure");
            }

            return new RecommendationProfile
            {
                Candidates = [new RecommendationTitle { Id = "new", Name = "new", MediaType = "Movie", IsInLibrary = true }]
            };
        }
    }

    private sealed class NoOpProgress : IProgress<double>
    {
        public void Report(double value)
        {
        }
    }

    private sealed class FakeSeerrCandidateProvider : ISeerrCandidateProvider
    {
        private readonly IReadOnlyList<RecommendationTitle> _candidates;
        private readonly Exception? _exception;

        public FakeSeerrCandidateProvider(
            IReadOnlyList<RecommendationTitle>? candidates = null,
            Exception? exception = null)
        {
            _candidates = candidates ?? Array.Empty<RecommendationTitle>();
            _exception = exception;
        }

        public Task<IReadOnlyList<RecommendationTitle>> GetCandidatesAsync(
            Guid userId,
            IReadOnlyList<RecommendationTitle> history,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(_candidates);
        }
    }
}