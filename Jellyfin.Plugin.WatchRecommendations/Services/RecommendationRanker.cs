using Jellyfin.Plugin.WatchRecommendations.Model;

namespace Jellyfin.Plugin.WatchRecommendations.Services;

public sealed class RecommendationRanker
{
    private const double SimilarityWeight = 0.75;
    private const double RatingWeight = 0.15;
    private const double RecencyWeight = 0.10;

    public IReadOnlyList<ScoredRecommendation> Rank(
        RecommendationProfile profile,
        int maximumResults = 50,
        DateTime? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (maximumResults <= 0)
        {
            return Array.Empty<ScoredRecommendation>();
        }

        var now = nowUtc ?? DateTime.UtcNow;
        var history = profile.History ?? new List<RecommendationTitle>();
        var historyIdentities = history
            .Select(GetIdentity)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = profile.Candidates
            .Where(item => !item.IsStarted && !item.IsWatched && !historyIdentities.Contains(GetIdentity(item)))
            .GroupBy(GetIdentity, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(item => item.IsInLibrary)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .First());

        return candidates
            .Select(item => new ScoredRecommendation { Item = item, Score = Score(item, history, now) })
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => GetIdentity(result.Item), StringComparer.OrdinalIgnoreCase)
            .Take(maximumResults)
            .ToList();
    }

    public static string GetIdentity(RecommendationTitle item)
    {
        if (TryGetProviderId(item, "Tmdb", out var tmdbId))
        {
            return $"{item.MediaType}:tmdb:{tmdbId}";
        }

        if (TryGetProviderId(item, "Imdb", out var imdbId))
        {
            return $"{item.MediaType}:imdb:{imdbId}";
        }

        return $"{item.MediaType}:jellyfin:{item.Id}";
    }

    private static double Score(RecommendationTitle candidate, IReadOnlyList<RecommendationTitle> history, DateTime now)
    {
        var similarity = history.Count == 0
            ? 0
            : history.Max(seed => MetadataSimilarity(candidate, seed) * HistoryWeight(seed, now));

        var rating = candidate.CommunityRating ?? candidate.UserRating;
        var hasRating = rating.HasValue;
        var releaseDate = candidate.PremiereDate ?? candidate.DateAddedUtc;
        var hasRecency = releaseDate.HasValue;

        var totalWeight = (history.Count == 0 ? 0 : SimilarityWeight)
            + (hasRating ? RatingWeight : 0)
            + (hasRecency ? RecencyWeight : 0);
        if (totalWeight == 0)
        {
            return 0;
        }

        var score = history.Count == 0 ? 0 : similarity * SimilarityWeight;
        if (rating.HasValue)
        {
            score += Math.Clamp(rating.Value / 10d, 0, 1) * RatingWeight;
        }

        if (releaseDate.HasValue)
        {
            var ageDays = Math.Max(0, (now - releaseDate.Value.ToUniversalTime()).TotalDays);
            score += Math.Exp(-ageDays / (365.25 * 12)) * RecencyWeight;
        }

        return score / totalWeight;
    }

    private static double MetadataSimilarity(RecommendationTitle candidate, RecommendationTitle seed)
    {
        var genres = Jaccard(candidate.Genres, seed.Genres);
        var tags = Jaccard(candidate.Tags, seed.Tags);
        return (genres * 0.65) + (tags * 0.35);
    }

    private static double Jaccard(IEnumerable<string>? left, IEnumerable<string>? right)
    {
        var leftSet = new HashSet<string>(left ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var rightSet = new HashSet<string>(right ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        leftSet.RemoveWhere(string.IsNullOrWhiteSpace);
        rightSet.RemoveWhere(string.IsNullOrWhiteSpace);
        if (leftSet.Count == 0 && rightSet.Count == 0)
        {
            return 0;
        }

        var intersection = leftSet.Count(rightSet.Contains);
        var union = leftSet.Count + rightSet.Count - intersection;
        return union == 0 ? 0 : (double)intersection / union;
    }

    private static double HistoryWeight(RecommendationTitle item, DateTime now)
    {
        var completion = Math.Clamp(item.Completion, 0, 1);
        var completionWeight = 0.6 + (0.4 * completion);
        var recencyWeight = item.LastPlayedUtc.HasValue
            ? Math.Exp(-Math.Max(0, (now - item.LastPlayedUtc.Value.ToUniversalTime()).TotalDays) / 730)
            : 0.5;
        var ratingWeight = item.UserRating.HasValue
            ? 0.35 + (0.65 * Math.Clamp(item.UserRating.Value / 10d, 0, 1))
            : 1;

        return completionWeight * recencyWeight * ratingWeight;
    }

    private static bool TryGetProviderId(RecommendationTitle item, string provider, out string value)
    {
        foreach (var pair in item.ProviderIds)
        {
            if (string.Equals(pair.Key, provider, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(pair.Value))
            {
                value = pair.Value.Trim();
                return true;
            }
        }

        value = string.Empty;
        return false;
    }
}