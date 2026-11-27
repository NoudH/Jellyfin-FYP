using Jellyfin.Plugin.WatchRecommendations.Model;

namespace Jellyfin.Plugin.WatchRecommendations.Services;

public interface ISeerrCandidateProvider
{
    Task<IReadOnlyList<RecommendationTitle>> GetCandidatesAsync(
        Guid userId,
        IReadOnlyList<RecommendationTitle> history,
        CancellationToken cancellationToken);
}