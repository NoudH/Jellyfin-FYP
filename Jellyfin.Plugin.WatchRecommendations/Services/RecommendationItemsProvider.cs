using Jellyfin.Plugin.WatchRecommendations.Model;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.WatchRecommendations.Services;

public sealed class RecommendationItemsProvider : IRecommendationItemsProvider
{
    private const int MaximumLimit = 50;
    private const int DefaultLimit = MaximumLimit;

    private readonly RecommendationCacheStore _cacheStore;
    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IDtoService _dtoService;

    public RecommendationItemsProvider(
        RecommendationCacheStore cacheStore,
        IUserManager userManager,
        ILibraryManager libraryManager,
        IDtoService dtoService)
    {
        _cacheStore = cacheStore;
        _userManager = userManager;
        _libraryManager = libraryManager;
        _dtoService = dtoService;
    }

    public QueryResult<BaseItemDto>? GetRecommendations(Guid userId, int? limit)
    {
        var user = _userManager.GetUserById(userId);
        if (user is null)
        {
            return null;
        }

        var cache = _cacheStore.Get(userId);
        var items = SelectVisibleLibraryItems(
            cache,
            _libraryManager.GetItemById,
            item => item.IsVisibleStandalone(user));
        var limitedItems = items.Take(BoundLimit(limit)).ToList();
        var dtos = _dtoService.GetBaseItemDtos(limitedItems, new DtoOptions(), user);
        return new QueryResult<BaseItemDto>(dtos);
    }

    public static int BoundLimit(int? limit) => limit is > 0 ? Math.Min(limit.Value, MaximumLimit) : DefaultLimit;

    public static List<BaseItem> SelectVisibleLibraryItems(
        UserRecommendationCache? cache,
        Func<Guid, BaseItem?> resolveItem,
        Func<BaseItem, bool> isVisible)
    {
        ArgumentNullException.ThrowIfNull(resolveItem);
        ArgumentNullException.ThrowIfNull(isVisible);

        var selected = new List<BaseItem>();
        var seen = new HashSet<Guid>();
        foreach (var recommendation in cache?.Recommendations ?? [])
        {
            var title = recommendation.Item;
            if (title is null || !title.IsInLibrary || title.IsStarted || title.IsWatched
                || !Guid.TryParse(title.Id, out var itemId) || itemId == Guid.Empty)
            {
                continue;
            }

            var item = resolveItem(itemId);
            if (item is not (Movie or Series) || !seen.Add(item.Id))
            {
                continue;
            }

            try
            {
                if (isVisible(item))
                {
                    selected.Add(item);
                }
            }
            catch (Exception)
            {
                // A failed visibility check must not expose the item in the home-screen row.
            }
        }

        return selected;
    }
}