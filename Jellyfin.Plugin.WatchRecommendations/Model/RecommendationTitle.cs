namespace Jellyfin.Plugin.WatchRecommendations.Model;

public sealed class RecommendationTitle
{
    public string Id { get; set; } = string.Empty;

    public string MediaType { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Overview { get; set; }

    public List<string> Genres { get; set; } = new();

    public List<string> Tags { get; set; } = new();

    public Dictionary<string, string> ProviderIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public DateTime? PremiereDate { get; set; }

    public DateTime? DateAddedUtc { get; set; }

    public double? CommunityRating { get; set; }

    public double? UserRating { get; set; }

    public double Completion { get; set; }

    public DateTime? LastPlayedUtc { get; set; }

    public bool IsInLibrary { get; set; }

    public bool IsStarted { get; set; }

    public bool IsWatched { get; set; }
}