using Jellyfin.Plugin.WatchRecommendations.Model;
using Jellyfin.Plugin.WatchRecommendations.Services;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.WatchRecommendations.Controllers;

[ApiController]
[Authorize]
[Route("WatchRecommendations")]
public class RecommendationsController : ControllerBase
{
    private const string JellyfinUserIdClaim = "Jellyfin-UserId";
    private readonly RecommendationCacheStore _cacheStore;
    private readonly IRecommendationItemsProvider _itemsProvider;

    public RecommendationsController(
        RecommendationCacheStore cacheStore,
        IRecommendationItemsProvider itemsProvider)
    {
        _cacheStore = cacheStore;
        _itemsProvider = itemsProvider;
    }

    [HttpGet("recommendations")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult GetRecommendations()
    {
        var claimValue = User.Claims
            .FirstOrDefault(claim => string.Equals(claim.Type, JellyfinUserIdClaim, StringComparison.OrdinalIgnoreCase))?.Value;
        if (!Guid.TryParse(claimValue, out var userId) || userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var cached = _cacheStore.Get(userId);
        return Ok(new RecommendationsResponse
        {
            RefreshedAtUtc = cached?.RefreshedAtUtc,
            Recommendations = cached?.Recommendations ?? new List<ScoredRecommendation>()
        });
    }

    [HttpGet("recommendations/items")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [Produces("application/json")]
    public ActionResult<QueryResult<BaseItemDto>> GetRecommendationItems([FromQuery] int? limit)
    {
        var claimValue = User.Claims
            .FirstOrDefault(claim => string.Equals(claim.Type, JellyfinUserIdClaim, StringComparison.OrdinalIgnoreCase))?.Value;
        if (!Guid.TryParse(claimValue, out var userId) || userId == Guid.Empty)
        {
            return Unauthorized();
        }

        var result = _itemsProvider.GetRecommendations(userId, limit);
        return result is null ? Forbid() : Ok(result);
    }
}

public sealed class RecommendationsResponse
{
    public DateTime? RefreshedAtUtc { get; set; }

    public List<ScoredRecommendation> Recommendations { get; set; } = new();
}