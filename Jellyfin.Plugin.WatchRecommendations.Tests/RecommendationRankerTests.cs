using Jellyfin.Plugin.WatchRecommendations.Model;
using Jellyfin.Plugin.WatchRecommendations.Services;
using Xunit;

namespace Jellyfin.Plugin.WatchRecommendations.Tests;

public class RecommendationRankerTests
{
    [Fact]
    public void RanksMetadataSimilarItemsAndExcludesStartedOrWatchedCandidates()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var profile = new RecommendationProfile
        {
            History =
            [
                new RecommendationTitle
                {
                    MediaType = "Movie",
                    Genres = ["Action", "Science Fiction"],
                    Completion = 1,
                    UserRating = 9,
                    LastPlayedUtc = now
                }
            ],
            Candidates =
            [
                Candidate("similar", "Movie", ["Action", "Science Fiction"]),
                Candidate("unrelated", "Movie", ["Romance"]),
                Candidate("started", "Series", ["Action"], isStarted: true),
                Candidate("watched", "Movie", ["Action"], isWatched: true)
            ]
        };

        var ranked = new RecommendationRanker().Rank(profile, nowUtc: now);

        Assert.Equal(new[] { "similar", "unrelated" }, ranked.Select(result => result.Item.Id));
        Assert.True(ranked[0].Score > ranked[1].Score);
    }

    [Fact]
    public void DeduplicatesByProviderAndMediaTypeWhileKeepingMoviesAndSeries()
    {
        var profile = new RecommendationProfile
        {
            Candidates =
            [
                Candidate("first", "Movie", ["Drama"], "123"),
                Candidate("duplicate", "Movie", ["Drama"], "123"),
                Candidate("series", "Series", ["Drama"], "123")
            ]
        };

        var ranked = new RecommendationRanker().Rank(profile);

        Assert.Equal(2, ranked.Count);
        Assert.Contains(ranked, result => result.Item.MediaType == "Movie");
        Assert.Contains(ranked, result => result.Item.MediaType == "Series");
    }

    [Fact]
    public void OptionalRatingsAndMetadataDoNotPreventRanking()
    {
        var profile = new RecommendationProfile
        {
            History = [new RecommendationTitle { MediaType = "Series", Genres = ["Anime"], Completion = 0.5 }],
            Candidates = [Candidate("anime", "Series", ["Anime"])]
        };

        var ranked = new RecommendationRanker().Rank(profile);

        Assert.Single(ranked);
        Assert.True(double.IsFinite(ranked[0].Score));
    }

    [Fact]
    public void ExcludesExternalCandidateMatchingWatchedTmdbIdentity()
    {
        var profile = new RecommendationProfile
        {
            History = [Candidate("watched-library-id", "Movie", ["Drama"], "123")],
            Candidates = [Candidate("external-tmdb-id", "Movie", ["Drama"], "123", isWatched: false)]
        };

        var ranked = new RecommendationRanker().Rank(profile);

        Assert.Empty(ranked);
    }

    private static RecommendationTitle Candidate(
        string id,
        string mediaType,
        List<string> genres,
        string? tmdbId = null,
        bool isStarted = false,
        bool isWatched = false) =>
        new()
        {
            Id = id,
            MediaType = mediaType,
            Name = id,
            Genres = genres,
            ProviderIds = tmdbId is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Tmdb"] = tmdbId },
            IsInLibrary = true,
            IsStarted = isStarted,
            IsWatched = isWatched
        };
}