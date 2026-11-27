namespace Jellyfin.Plugin.WatchRecommendations.Services;

public interface ISeerrRecommendationsFilter
{
    Task<string?> FilterAsync(string json, string apiPath, Guid jellyfinUserId, CancellationToken cancellationToken);
}