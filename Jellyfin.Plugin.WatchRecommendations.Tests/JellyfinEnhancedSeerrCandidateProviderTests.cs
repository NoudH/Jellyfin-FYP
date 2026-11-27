using System.Net;
using System.Text;
using Jellyfin.Plugin.WatchRecommendations.Model;
using Jellyfin.Plugin.WatchRecommendations.Services;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.WatchRecommendations.Tests;

public class JellyfinEnhancedSeerrCandidateProviderTests
{
    [Fact]
    public async Task FetchesCandidatesUsingLinkedSeerrUserAndFiltersRecommendations()
    {
        var userId = Guid.NewGuid();
        var requests = new List<HttpRequestMessage>();
        var handler = new FakeHttpMessageHandler(request =>
        {
            requests.Add(CloneRequest(request));
            if (request.RequestUri!.AbsolutePath.EndsWith("/api/v1/user", StringComparison.Ordinal))
            {
                return JsonResponse($"{{\"results\":[{{\"id\":42,\"jellyfinUserId\":\"{userId:N}\"}}]}}");
            }

            return JsonResponse("""
                {"results":[{"id":202,"mediaType":"movie","title":"External title","overview":"Overview","genre_ids":[28,878],"release_date":"2025-01-02","vote_average":8.4}]}
                """);
        });
        var filter = new RecordingFilter();
        var provider = CreateProvider(
            new SeerrIntegrationConfiguration(true, "https://seerr.example", "secret-key", ""),
            new TestHttpClientFactory(handler),
            filter);

        var candidates = await provider.GetCandidatesAsync(userId, [HistorySeed("Movie", "101")], CancellationToken.None);

        var candidate = Assert.Single(candidates);
        Assert.Equal("202", candidate.Id);
        Assert.Equal("External title", candidate.Name);
        Assert.Equal("Movie", candidate.MediaType);
        Assert.Equal("202", candidate.ProviderIds["Tmdb"]);
        Assert.Contains("Action", candidate.Genres);
        Assert.Contains("Science Fiction", candidate.Genres);
        Assert.False(candidate.IsInLibrary);
        Assert.Equal(2, requests.Count);
        Assert.False(requests[0].Headers.Contains("X-Api-User"));
        Assert.Equal("42", requests[1].Headers.GetValues("X-Api-User").Single());
        Assert.Equal("secret-key", requests[1].Headers.GetValues("X-Api-Key").Single());
        Assert.Equal("/api/v1/movie/101/recommendations", requests[1].RequestUri!.AbsolutePath);
        Assert.Equal(userId, filter.UserId);
        Assert.Equal("/api/v1/movie/101/recommendations?page=1", filter.ApiPath);
    }

    [Fact]
    public async Task DoesNotQuerySeerrWhenDisabledOrUserIsBlocked()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("Seerr should not be called."));
        var userId = Guid.NewGuid();

        var disabledCandidates = await CreateProvider(
                new SeerrIntegrationConfiguration(false, "https://seerr.example", "key", ""),
                new TestHttpClientFactory(handler),
                new RecordingFilter())
            .GetCandidatesAsync(userId, [HistorySeed("Movie", "101")], CancellationToken.None);
        var blockedCandidates = await CreateProvider(
                new SeerrIntegrationConfiguration(true, "https://seerr.example", "key", userId.ToString("D")),
                new TestHttpClientFactory(handler),
                new RecordingFilter())
            .GetCandidatesAsync(userId, [HistorySeed("Movie", "101")], CancellationToken.None);

        Assert.Empty(disabledCandidates);
        Assert.Empty(blockedCandidates);
    }

    [Fact]
    public async Task UsesTheSeerrInstanceWhereTheJellyfinUserIsLinked()
    {
        var userId = Guid.NewGuid();
        var requests = new List<HttpRequestMessage>();
        var handler = new FakeHttpMessageHandler(request =>
        {
            requests.Add(CloneRequest(request));
            if (request.RequestUri!.AbsolutePath.EndsWith("/api/v1/user", StringComparison.Ordinal))
            {
                var matchingUser = request.RequestUri.Host == "seerr-two.example"
                    ? $"{{\"id\":42,\"jellyfinUserId\":\"{userId:N}\"}}"
                    : "{\"id\":7,\"jellyfinUserId\":\"unrelated-user\"}";
                return JsonResponse($"{{\"results\":[{matchingUser}]}}");
            }

            Assert.Equal("seerr-two.example", request.RequestUri.Host);
            return JsonResponse("{\"results\":[{\"id\":202,\"mediaType\":\"movie\",\"title\":\"External title\"}]}");
        });
        var provider = CreateProvider(
            new SeerrIntegrationConfiguration(
                true,
                "https://seerr-one.example\nhttps://seerr-two.example",
                "key",
                ""),
            new TestHttpClientFactory(handler),
            new RecordingFilter());

        var candidates = await provider.GetCandidatesAsync(userId, [HistorySeed("Movie", "101")], CancellationToken.None);

        Assert.Single(candidates);
        Assert.Equal(3, requests.Count);
        Assert.Equal("seerr-two.example", requests[2].RequestUri!.Host);
        Assert.Equal("42", requests[2].Headers.GetValues("X-Api-User").Single());
    }

    [Fact]
    public async Task FetchesExternalSeriesFromTheSeerrTvRecommendationsRoute()
    {
        var userId = Guid.NewGuid();
        var requests = new List<HttpRequestMessage>();
        var handler = new FakeHttpMessageHandler(request =>
        {
            requests.Add(CloneRequest(request));
            if (request.RequestUri!.AbsolutePath.EndsWith("/api/v1/user", StringComparison.Ordinal))
            {
                return JsonResponse($"{{\"results\":[{{\"id\":42,\"jellyfinUserId\":\"{userId:N}\"}}]}}");
            }

            return JsonResponse("{\"results\":[{\"id\":404,\"mediaType\":\"tv\",\"name\":\"Anime series\",\"genre_ids\":[10765]}]}");
        });
        var provider = CreateProvider(
            new SeerrIntegrationConfiguration(true, "https://seerr.example", "key", ""),
            new TestHttpClientFactory(handler),
            new RecordingFilter());

        var candidates = await provider.GetCandidatesAsync(userId, [HistorySeed("Series", "303")], CancellationToken.None);

        var candidate = Assert.Single(candidates);
        Assert.Equal("Series", candidate.MediaType);
        Assert.Equal("Anime series", candidate.Name);
        Assert.Contains("Sci-Fi & Fantasy", candidate.Genres);
        Assert.Equal("/api/v1/tv/303/recommendations", requests[1].RequestUri!.AbsolutePath);
        Assert.Equal("42", requests[1].Headers.GetValues("X-Api-User").Single());
    }

    [Fact]
    public async Task UnlinkedUserDoesNotFetchRecommendations()
    {
        var requestCount = 0;
        var handler = new FakeHttpMessageHandler(_ =>
        {
            requestCount++;
            return JsonResponse("{\"results\":[]}");
        });
        var provider = CreateProvider(
            new SeerrIntegrationConfiguration(true, "https://seerr.example", "key", ""),
            new TestHttpClientFactory(handler),
            new RecordingFilter());

        var candidates = await provider.GetCandidatesAsync(Guid.NewGuid(), [HistorySeed("Series", "101")], CancellationToken.None);

        Assert.Empty(candidates);
        Assert.Equal(1, requestCount);
    }

    [Fact]
    public void ConfigurationSourceReturnsNullWhenJEIsNotLoaded()
    {
        Assert.Null(new JellyfinEnhancedSeerrConfigurationSource().GetConfiguration());
    }

    private static JellyfinEnhancedSeerrCandidateProvider CreateProvider(
        SeerrIntegrationConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ISeerrRecommendationsFilter filter) =>
        new(
            new TestConfigurationSource(configuration),
            httpClientFactory,
            filter,
            NullLogger<JellyfinEnhancedSeerrCandidateProvider>.Instance);

    private static RecommendationTitle HistorySeed(string mediaType, string tmdbId) => new()
    {
        Id = $"seed-{tmdbId}",
        MediaType = mediaType,
        Name = "Watched title",
        ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Tmdb"] = tmdbId },
        LastPlayedUtc = DateTime.UtcNow
    };

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }

    private sealed class TestConfigurationSource : ISeerrIntegrationConfigurationSource
    {
        private readonly SeerrIntegrationConfiguration _configuration;

        public TestConfigurationSource(SeerrIntegrationConfiguration configuration)
        {
            _configuration = configuration;
        }

        public SeerrIntegrationConfiguration GetConfiguration() => _configuration;
    }

    private sealed class RecordingFilter : ISeerrRecommendationsFilter
    {
        public Guid UserId { get; private set; }
        public string? ApiPath { get; private set; }

        public Task<string?> FilterAsync(string json, string apiPath, Guid jellyfinUserId, CancellationToken cancellationToken)
        {
            UserId = jellyfinUserId;
            ApiPath = apiPath;
            return Task.FromResult<string?>(json);
        }
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _httpClient;

        public TestHttpClientFactory(HttpMessageHandler handler)
        {
            _httpClient = new HttpClient(handler);
        }

        public HttpClient CreateClient(string name) => _httpClient;
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responseFactory(request));
    }
}