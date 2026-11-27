namespace Jellyfin.Plugin.WatchRecommendations.Model;

public sealed class UserRecommendationCache
{
    public Guid UserId { get; set; }

    public DateTime RefreshedAtUtc { get; set; }

    public List<ScoredRecommendation> Recommendations { get; set; } = new();
}