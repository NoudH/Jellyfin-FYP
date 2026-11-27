namespace Jellyfin.Plugin.WatchRecommendations.Model;

public sealed class ScoredRecommendation
{
    public RecommendationTitle Item { get; set; } = new();

    public double Score { get; set; }
}