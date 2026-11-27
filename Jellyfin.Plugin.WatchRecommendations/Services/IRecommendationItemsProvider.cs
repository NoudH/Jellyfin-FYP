using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.WatchRecommendations.Services;

public interface IRecommendationItemsProvider
{
    QueryResult<BaseItemDto>? GetRecommendations(Guid userId, int? limit);
}