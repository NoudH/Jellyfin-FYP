using Jellyfin.Plugin.WatchRecommendations.Model;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WatchRecommendations.Services;

public sealed class RecommendationsRefreshService
{
    private readonly IRecommendationDataSource _dataSource;
    private readonly ISeerrCandidateProvider _seerrCandidateProvider;
    private readonly RecommendationRanker _ranker;
    private readonly RecommendationCacheStore _cacheStore;
    private readonly ILogger<RecommendationsRefreshService> _logger;

    public RecommendationsRefreshService(
        IRecommendationDataSource dataSource,
        ISeerrCandidateProvider seerrCandidateProvider,
        RecommendationRanker ranker,
        RecommendationCacheStore cacheStore,
        ILogger<RecommendationsRefreshService> logger)
    {
        _dataSource = dataSource;
        _seerrCandidateProvider = seerrCandidateProvider;
        _ranker = ranker;
        _cacheStore = cacheStore;
        _logger = logger;
    }

    public async Task RefreshAllUsersAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var userIds = _dataSource.GetUserIds();
        if (userIds.Count == 0)
        {
            progress.Report(100);
            return;
        }

        for (var index = 0; index < userIds.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var userId = userIds[index];
            try
            {
                var profile = _dataSource.LoadProfile(userId, cancellationToken);
                try
                {
                    var externalCandidates = await _seerrCandidateProvider
                        .GetCandidatesAsync(userId, profile.History, cancellationToken)
                        .ConfigureAwait(false);
                    profile.Candidates.AddRange(externalCandidates);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(
                        exception,
                        "Failed to fetch Seerr recommendations for Jellyfin user {UserId}; continuing without external recommendations.",
                        userId);
                }

                var now = DateTime.UtcNow;
                var result = new UserRecommendationCache
                {
                    UserId = userId,
                    RefreshedAtUtc = now,
                    Recommendations = _ranker.Rank(profile, nowUtc: now).ToList()
                };
                await _cacheStore.ReplaceUserAsync(result, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to refresh recommendations for Jellyfin user {UserId}; keeping the previous cache.", userId);
            }

            progress.Report((index + 1d) / userIds.Count * 100);
        }
    }
}