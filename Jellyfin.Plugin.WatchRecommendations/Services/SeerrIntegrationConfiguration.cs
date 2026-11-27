namespace Jellyfin.Plugin.WatchRecommendations.Services;

public sealed record SeerrIntegrationConfiguration(
    bool Enabled,
    string? BaseUrls,
    string? ApiKey,
    string? BlockedUserIds);

public interface ISeerrIntegrationConfigurationSource
{
    SeerrIntegrationConfiguration? GetConfiguration();
}