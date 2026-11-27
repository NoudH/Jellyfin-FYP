using System.Security.Claims;
using Jellyfin.Plugin.WatchRecommendations.Controllers;
using Jellyfin.Plugin.WatchRecommendations.Model;
using Jellyfin.Plugin.WatchRecommendations.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Jellyfin.Plugin.WatchRecommendations.Tests;

public class RecommendationsControllerTests
{
    [Fact]
    public async Task ReturnsOnlyTheAuthenticatedUsersCacheAndIgnoresUserIdQuery()
    {
        var root = Path.Combine(AppContext.BaseDirectory, $"recommendations-controller-test-{Guid.NewGuid():N}");
        var authenticatedUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        try
        {
            var cache = new RecommendationCacheStore(root);
            await cache.ReplaceUserAsync(Cache(authenticatedUserId, "authenticated-title"), CancellationToken.None);
            await cache.ReplaceUserAsync(Cache(otherUserId, "other-title"), CancellationToken.None);
            var controller = CreateController(cache, authenticatedUserId, $"?userId={otherUserId}");

            var response = Assert.IsType<OkObjectResult>(controller.GetRecommendations());
            var result = Assert.IsType<RecommendationsResponse>(response.Value);

            Assert.Equal("authenticated-title", Assert.Single(result.Recommendations).Item.Name);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void RejectsRequestsWithoutAValidJellyfinUserClaim()
    {
        var root = Path.Combine(AppContext.BaseDirectory, $"recommendations-controller-auth-test-{Guid.NewGuid():N}");
        try
        {
            var controller = CreateController(new RecommendationCacheStore(root), null, null);

            Assert.IsType<UnauthorizedResult>(controller.GetRecommendations());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static RecommendationsController CreateController(
        RecommendationCacheStore cache,
        Guid? userId,
        string? query)
    {
        var controller = new RecommendationsController(cache);
        var context = new DefaultHttpContext();
        if (userId.HasValue)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("Jellyfin-UserId", userId.Value.ToString())],
                "TestAuthentication"));
        }

        if (query is not null)
        {
            context.Request.QueryString = new QueryString(query);
        }

        controller.ControllerContext = new ControllerContext { HttpContext = context };
        return controller;
    }

    private static UserRecommendationCache Cache(Guid userId, string title) =>
        new()
        {
            UserId = userId,
            RefreshedAtUtc = DateTime.UtcNow,
            Recommendations = [new ScoredRecommendation { Item = new RecommendationTitle { Name = title }, Score = 1 }]
        };
}