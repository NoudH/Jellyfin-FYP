using Jellyfin.Plugin.WatchRecommendations.Model;

namespace Jellyfin.Plugin.WatchRecommendations.Services;

public interface IRecommendationDataSource
{
    IReadOnlyList<Guid> GetUserIds();

    RecommendationProfile LoadProfile(Guid userId, CancellationToken cancellationToken);
}