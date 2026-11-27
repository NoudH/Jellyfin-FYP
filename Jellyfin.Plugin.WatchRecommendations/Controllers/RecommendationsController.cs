using Jellyfin.Plugin.WatchRecommendations.Model;
using Jellyfin.Plugin.WatchRecommendations.Services;
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

    public RecommendationsController(RecommendationCacheStore cacheStore)
    {
        _cacheStore = cacheStore;
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
}

public sealed class RecommendationsResponse
{
    public DateTime? RefreshedAtUtc { get; set; }

    public List<ScoredRecommendation> Recommendations { get; set; } = new();
}