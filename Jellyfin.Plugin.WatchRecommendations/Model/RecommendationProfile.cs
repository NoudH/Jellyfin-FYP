namespace Jellyfin.Plugin.WatchRecommendations.Model;

public sealed class RecommendationProfile
{
    public List<RecommendationTitle> History { get; set; } = new();

    public List<RecommendationTitle> Candidates { get; set; } = new();
}