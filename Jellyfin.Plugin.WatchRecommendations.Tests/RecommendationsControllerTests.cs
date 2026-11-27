using System.Security.Claims;
using Jellyfin.Plugin.WatchRecommendations.Controllers;
using Jellyfin.Plugin.WatchRecommendations.Model;
using Jellyfin.Plugin.WatchRecommendations.Services;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
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
            var controller = CreateController(cache, new StubRecommendationItemsProvider(), authenticatedUserId, $"?userId={otherUserId}");

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
            var controller = CreateController(new RecommendationCacheStore(root), new StubRecommendationItemsProvider(), null, null);

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

    [Fact]
    public void HomeScreenItemsUseAuthenticatedUserAndReturnJellyfinQueryResult()
    {
        var root = Path.Combine(AppContext.BaseDirectory, $"recommendations-controller-items-test-{Guid.NewGuid():N}");
        var authenticatedUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        try
        {
            var provider = new StubRecommendationItemsProvider();
            var controller = CreateController(
                new RecommendationCacheStore(root),
                provider,
                authenticatedUserId,
                $"?UserId={otherUserId}&Limit=12");

            var response = Assert.IsType<OkObjectResult>(controller.GetRecommendationItems(12).Result);
            var result = Assert.IsType<QueryResult<BaseItemDto>>(response.Value);

            Assert.Equal(authenticatedUserId, provider.UserId);
            Assert.Equal(12, provider.Limit);
            Assert.Empty(result.Items);
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
    public void HomeScreenItemsRejectRequestsWithoutAValidJellyfinUserClaim()
    {
        var root = Path.Combine(AppContext.BaseDirectory, $"recommendations-controller-items-auth-test-{Guid.NewGuid():N}");
        var provider = new StubRecommendationItemsProvider();
        try
        {
            var controller = CreateController(new RecommendationCacheStore(root), provider, null, null);

            Assert.IsType<UnauthorizedResult>(controller.GetRecommendationItems(10).Result);
            Assert.Null(provider.UserId);
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
        IRecommendationItemsProvider itemsProvider,
        Guid? userId,
        string? query)
    {
        var controller = new RecommendationsController(cache, itemsProvider);
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

    private sealed class StubRecommendationItemsProvider : IRecommendationItemsProvider
    {
        public Guid? UserId { get; private set; }

        public int? Limit { get; private set; }

        public QueryResult<BaseItemDto>? GetRecommendations(Guid userId, int? limit)
        {
            UserId = userId;
            Limit = limit;
            return new QueryResult<BaseItemDto>(Array.Empty<BaseItemDto>());
        }
    }
}