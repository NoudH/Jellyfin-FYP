using Jellyfin.Data.Enums;
using Jellyfin.Plugin.WatchRecommendations.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.WatchRecommendations.Services;

public sealed class RecommendationDataSource : IRecommendationDataSource
{
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserDataManager _userDataManager;

    public RecommendationDataSource(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IUserDataManager userDataManager)
    {
        _userManager = userManager;
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
    }

    public IReadOnlyList<Guid> GetUserIds() => _userManager.GetUsers().Select(user => user.Id).ToList();

    public RecommendationProfile LoadProfile(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = _userManager.GetUserById(userId)
            ?? throw new InvalidOperationException($"Jellyfin user {userId} no longer exists.");

        var libraryItems = _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
            IsVirtualItem = false,
            Recursive = true,
            GroupByPresentationUniqueKey = false
        }).ToList();
        cancellationToken.ThrowIfCancellationRequested();

        var itemData = _userDataManager.GetUserDataBatch(libraryItems, user);
        var episodes = _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            IncludeItemTypes = new[] { BaseItemKind.Episode },
            IsVirtualItem = false,
            Recursive = true,
            GroupByPresentationUniqueKey = false
        }).OfType<Episode>().ToList();
        cancellationToken.ThrowIfCancellationRequested();

        var episodeData = _userDataManager.GetUserDataBatch(episodes, user);
        var episodesBySeries = episodes
            .Where(episode => episode.SeriesId != Guid.Empty)
            .GroupBy(episode => episode.SeriesId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var history = new List<RecommendationTitle>();
        var candidates = new List<RecommendationTitle>();
        foreach (var item in libraryItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            itemData.TryGetValue(item.Id, out var userData);

            if (item is Movie movie)
            {
                var positionTicks = userData?.PlaybackPositionTicks ?? 0;
                var started = userData?.Played == true || positionTicks > 0;
                var runtimeTicks = movie.RunTimeTicks.GetValueOrDefault();
                var completion = userData?.Played == true
                    ? 1
                    : runtimeTicks > 0 ? Math.Clamp((double)positionTicks / runtimeTicks, 0, 1) : 0;
                var title = CreateTitle(movie, userData?.Rating, completion, userData?.LastPlayedDate, started, userData?.Played == true);
                if (started)
                {
                    history.Add(title);
                }
                else
                {
                    candidates.Add(title);
                }

                continue;
            }

            if (item is not Series series)
            {
                continue;
            }

            episodesBySeries.TryGetValue(series.Id, out var seriesEpisodes);
            seriesEpisodes ??= new List<Episode>();
            var completionTotal = 0d;
            var playedEpisodes = 0;
            var startedEpisodes = 0;
            DateTime? lastPlayed = null;
            foreach (var episode in seriesEpisodes)
            {
                episodeData.TryGetValue(episode.Id, out var episodeUserData);
                var runtimeTicks = episode.RunTimeTicks.GetValueOrDefault();
                var played = episodeUserData?.Played == true;
                var positionTicks = episodeUserData?.PlaybackPositionTicks ?? 0;
                if (played)
                {
                    playedEpisodes++;
                    completionTotal += 1;
                }
                else if (positionTicks > 0)
                {
                    completionTotal += runtimeTicks > 0
                        ? Math.Clamp((double)positionTicks / runtimeTicks, 0, 1)
                        : 0.5;
                }

                if (played || positionTicks > 0)
                {
                    startedEpisodes++;
                    if (episodeUserData?.LastPlayedDate is DateTime lastPlayedDate
                        && (!lastPlayed.HasValue || lastPlayedDate > lastPlayed.Value))
                    {
                        lastPlayed = lastPlayedDate;
                    }
                }
            }

            var seriesCompletion = seriesEpisodes.Count > 0
                ? completionTotal / seriesEpisodes.Count
                : userData?.Played == true ? 1 : (userData?.PlaybackPositionTicks ?? 0) > 0 ? 0.5 : 0;
            var seriesWatched = userData?.Played == true
                || seriesEpisodes.Count > 0 && playedEpisodes == seriesEpisodes.Count;
            var seriesStarted = startedEpisodes > 0
                || userData?.Played == true
                || (userData?.PlaybackPositionTicks ?? 0) > 0;
            lastPlayed ??= userData?.LastPlayedDate;
            var seriesTitle = CreateTitle(series, userData?.Rating, seriesCompletion, lastPlayed, seriesStarted, seriesWatched);
            if (seriesStarted)
            {
                history.Add(seriesTitle);
            }
            else
            {
                candidates.Add(seriesTitle);
            }
        }

        return new RecommendationProfile { History = history, Candidates = candidates };
    }

    private static RecommendationTitle CreateTitle(
        BaseItem item,
        double? userRating,
        double completion,
        DateTime? lastPlayed,
        bool isStarted,
        bool isWatched) =>
        new()
        {
            Id = item.Id.ToString("N"),
            MediaType = item is Movie ? "Movie" : "Series",
            Name = item.Name ?? string.Empty,
            Overview = item.Overview,
            Genres = item.Genres?.Where(genre => !string.IsNullOrWhiteSpace(genre)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>(),
            Tags = item.Tags?.Where(tag => !string.IsNullOrWhiteSpace(tag)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>(),
            ProviderIds = item.ProviderIds is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : item.ProviderIds.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            PremiereDate = item.PremiereDate,
            DateAddedUtc = item.DateCreated,
            CommunityRating = item.CommunityRating,
            UserRating = userRating,
            Completion = completion,
            LastPlayedUtc = lastPlayed,
            IsInLibrary = true,
            IsStarted = isStarted,
            IsWatched = isWatched
        };
}