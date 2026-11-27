using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.WatchRecommendations.Model;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WatchRecommendations.Services;

public sealed class JellyfinEnhancedSeerrCandidateProvider : ISeerrCandidateProvider
{
    public const string HttpClientName = "WatchRecommendationsSeerr";

    private const int MaximumSeedCount = 8;
    private const int MaximumCandidatesPerSeed = 20;
    private const int MaximumSeerrInstances = 5;
    private const int MaximumResponseBytes = 8 * 1024 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private static readonly IReadOnlyDictionary<int, string> MovieGenres = new Dictionary<int, string>
    {
        [12] = "Adventure", [14] = "Fantasy", [16] = "Animation", [18] = "Drama",
        [27] = "Horror", [28] = "Action", [35] = "Comedy", [36] = "History",
        [37] = "Western", [53] = "Thriller", [80] = "Crime", [99] = "Documentary",
        [878] = "Science Fiction", [9648] = "Mystery", [10402] = "Music",
        [10749] = "Romance", [10752] = "War", [10770] = "TV Movie"
    };
    private static readonly IReadOnlyDictionary<int, string> TvGenres = new Dictionary<int, string>
    {
        [16] = "Animation", [18] = "Drama", [35] = "Comedy", [37] = "Western",
        [80] = "Crime", [99] = "Documentary", [10751] = "Family", [10759] = "Action & Adventure",
        [10762] = "Kids", [10763] = "News", [10764] = "Reality", [10765] = "Sci-Fi & Fantasy",
        [10766] = "Soap", [10767] = "Talk", [10768] = "War & Politics", [9648] = "Mystery"
    };

    private readonly ISeerrIntegrationConfigurationSource _configurationSource;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISeerrRecommendationsFilter _recommendationsFilter;
    private readonly ILogger<JellyfinEnhancedSeerrCandidateProvider> _logger;

    public JellyfinEnhancedSeerrCandidateProvider(
        ISeerrIntegrationConfigurationSource configurationSource,
        IHttpClientFactory httpClientFactory,
        ISeerrRecommendationsFilter recommendationsFilter,
        ILogger<JellyfinEnhancedSeerrCandidateProvider> logger)
    {
        _configurationSource = configurationSource;
        _httpClientFactory = httpClientFactory;
        _recommendationsFilter = recommendationsFilter;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RecommendationTitle>> GetCandidatesAsync(
        Guid userId,
        IReadOnlyList<RecommendationTitle> history,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (userId == Guid.Empty)
        {
            return Array.Empty<RecommendationTitle>();
        }

        var seeds = GetSeeds(history);
        if (seeds.Count == 0)
        {
            return Array.Empty<RecommendationTitle>();
        }

        var configuration = _configurationSource.GetConfiguration();
        if (configuration is not { Enabled: true }
            || string.IsNullOrWhiteSpace(configuration.ApiKey)
            || IsUserBlocked(configuration.BlockedUserIds, userId))
        {
            return Array.Empty<RecommendationTitle>();
        }

        var urls = ParseBaseUrls(configuration.BaseUrls);
        if (urls.Count == 0)
        {
            return Array.Empty<RecommendationTitle>();
        }

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        var linkedAccount = await FindLinkedUserAsync(urls, configuration.ApiKey, userId, httpClient, cancellationToken)
            .ConfigureAwait(false);
        if (linkedAccount is null)
        {
            return Array.Empty<RecommendationTitle>();
        }

        var candidates = new List<RecommendationTitle>();
        foreach (var seed in seeds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var apiPath = $"/api/v1/{seed.SeerrMediaType}/{seed.TmdbId}/recommendations?page=1";
            var uri = new Uri(linkedAccount.BaseUrl + apiPath, UriKind.Absolute);
            var json = await SendGetAsync(
                    httpClient,
                    uri,
                    configuration.ApiKey,
                    linkedAccount.SeerrUserId,
                    cancellationToken)
                .ConfigureAwait(false);
            var filteredJson = await _recommendationsFilter
                .FilterAsync(json, apiPath, userId, cancellationToken)
                .ConfigureAwait(false);
            if (filteredJson is null)
            {
                continue;
            }

            using var document = JsonDocument.Parse(filteredJson);
            var results = ReadResults(document.RootElement);
            foreach (var result in results.Take(MaximumCandidatesPerSeed))
            {
                var candidate = CreateCandidate(result, seed.SeerrMediaType);
                if (candidate is not null)
                {
                    candidates.Add(candidate);
                }
            }
        }

        return candidates;
    }

    private async Task<LinkedSeerrAccount?> FindLinkedUserAsync(
        IReadOnlyList<string> urls,
        string apiKey,
        Guid userId,
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        var receivedValidResponse = false;
        foreach (var baseUrl in urls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var uri = new Uri(baseUrl + "/api/v1/user?take=1000", UriKind.Absolute);
                var json = await SendGetAsync(httpClient, uri, apiKey, null, cancellationToken).ConfigureAwait(false);
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (!TryGetProperty(root, "results", out var users) || users.ValueKind != JsonValueKind.Array)
                {
                    throw new JsonException("Seerr user lookup did not contain a results array.");
                }

                receivedValidResponse = true;
                foreach (var user in users.EnumerateArray())
                {
                    if (!string.Equals(
                            NormalizeUserId(ReadString(user, "jellyfinUserId")),
                            userId.ToString("N"),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var seerrUserId = ReadString(user, "id");
                    if (!string.IsNullOrWhiteSpace(seerrUserId))
                    {
                        return new LinkedSeerrAccount(baseUrl, seerrUserId);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                lastError = exception;
                _logger.LogWarning(exception, "Could not resolve a linked Seerr account from a configured JE instance.");
            }
        }

        if (!receivedValidResponse && lastError is not null)
        {
            throw new HttpRequestException("Unable to resolve the Jellyfin user's Seerr account.", lastError);
        }

        return null;
    }

    private static async Task<string> SendGetAsync(
        HttpClient httpClient,
        Uri uri,
        string apiKey,
        string? seerrUserId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("JellyfinWatchRecommendations/1.0");
        request.Headers.Add("X-Api-Key", apiKey);
        if (!string.IsNullOrWhiteSpace(seerrUserId))
        {
            request.Headers.Add("X-Api-User", seerrUserId);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            using var response = await httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Seerr returned HTTP {(int)response.StatusCode}.");
            }

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType is null
                || !(contentType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
                    || contentType.StartsWith("application/", StringComparison.OrdinalIgnoreCase)
                        && contentType.EndsWith("+json", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException("Seerr returned a non-JSON response.");
            }

            if (response.Content.Headers.ContentLength > MaximumResponseBytes)
            {
                throw new InvalidDataException("Seerr response exceeded the configured size limit.");
            }

            return await ReadBoundedResponseAsync(response, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The Seerr request timed out.");
        }
    }

    private static async Task<string> ReadBoundedResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaximumResponseBytes)
            {
                throw new InvalidDataException("Seerr response exceeded the configured size limit.");
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static List<RecommendationSeed> GetSeeds(IReadOnlyList<RecommendationTitle> history)
    {
        return history
            .Select(item => TryGetTmdbId(item, out var tmdbId) && TryGetSeerrMediaType(item.MediaType, out var mediaType)
                ? new RecommendationSeed(item, tmdbId, mediaType)
                : null)
            .Where(seed => seed is not null)
            .Select(seed => seed!)
            .OrderByDescending(seed => seed.Title.LastPlayedUtc ?? DateTime.MinValue)
            .ThenByDescending(seed => seed.Title.Completion)
            .GroupBy(seed => $"{seed.SeerrMediaType}:{seed.TmdbId}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(MaximumSeedCount)
            .ToList();
    }

    private static bool TryGetTmdbId(RecommendationTitle item, out string tmdbId)
    {
        foreach (var pair in item.ProviderIds)
        {
            if (pair.Key.Equals("Tmdb", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(pair.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                && parsed > 0)
            {
                tmdbId = parsed.ToString(CultureInfo.InvariantCulture);
                return true;
            }
        }

        tmdbId = string.Empty;
        return false;
    }

    private static bool TryGetSeerrMediaType(string mediaType, out string seerrMediaType)
    {
        if (mediaType.Equals("Movie", StringComparison.OrdinalIgnoreCase))
        {
            seerrMediaType = "movie";
            return true;
        }

        if (mediaType.Equals("Series", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("TV", StringComparison.OrdinalIgnoreCase))
        {
            seerrMediaType = "tv";
            return true;
        }

        seerrMediaType = string.Empty;
        return false;
    }

    private static bool IsUserBlocked(string? blockedUsers, Guid userId)
    {
        var normalizedUserId = userId.ToString("N");
        return (blockedUsers ?? string.Empty)
            .Split(new[] { ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim().Replace("-", string.Empty, StringComparison.Ordinal))
            .Any(value => string.Equals(value, normalizedUserId, StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> ParseBaseUrls(string? configuredUrls)
    {
        return (configuredUrls ?? string.Empty)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Where(value => Uri.TryCreate(value, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                && string.IsNullOrEmpty(uri.UserInfo)
                && string.IsNullOrEmpty(uri.Query)
                && string.IsNullOrEmpty(uri.Fragment))
            .Select(value => new Uri(value, UriKind.Absolute).GetLeftPart(UriPartial.Path).TrimEnd('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximumSeerrInstances)
            .ToList();
    }

    private static IReadOnlyList<JsonElement> ReadResults(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray().ToList();
        }

        return TryGetProperty(root, "results", out var results) && results.ValueKind == JsonValueKind.Array
            ? results.EnumerateArray().ToList()
            : Array.Empty<JsonElement>();
    }

    private static RecommendationTitle? CreateCandidate(JsonElement result, string seedMediaType)
    {
        if (!int.TryParse(ReadString(result, "id"), NumberStyles.None, CultureInfo.InvariantCulture, out var tmdbId)
            || tmdbId <= 0)
        {
            return null;
        }

        var responseMediaType = ReadString(result, "mediaType");
        var mediaType = responseMediaType?.ToLowerInvariant() switch
        {
            "movie" => "Movie",
            "tv" or "series" => "Series",
            null or "" => seedMediaType == "movie" ? "Movie" : "Series",
            _ => null
        };
        if (mediaType is null)
        {
            return null;
        }

        var genres = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddGenres(result, "genres", genres, mediaType);
        AddGenres(result, "genreIds", genres, mediaType);
        AddGenres(result, "genre_ids", genres, mediaType);

        var title = ReadString(result, "title") ?? ReadString(result, "name");
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        return new RecommendationTitle
        {
            Id = tmdbId.ToString(CultureInfo.InvariantCulture),
            MediaType = mediaType,
            Name = title,
            Overview = ReadString(result, "overview"),
            Genres = genres.ToList(),
            ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Tmdb"] = tmdbId.ToString(CultureInfo.InvariantCulture)
            },
            PremiereDate = ReadDate(result, "releaseDate", "release_date", "firstAirDate", "first_air_date"),
            CommunityRating = ReadDouble(result, "voteAverage", "vote_average"),
            IsInLibrary = false
        };
    }

    private static void AddGenres(JsonElement item, string propertyName, ISet<string> genres, string mediaType)
    {
        if (!TryGetProperty(item, propertyName, out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var genreMap = mediaType == "Movie" ? MovieGenres : TvGenres;
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                var genre = value.GetString();
                if (!string.IsNullOrWhiteSpace(genre))
                {
                    genres.Add(genre);
                }
            }
            else if (value.ValueKind == JsonValueKind.Object)
            {
                var genre = ReadString(value, "name");
                if (!string.IsNullOrWhiteSpace(genre))
                {
                    genres.Add(genre);
                }
            }
            else if (value.TryGetInt32(out var genreId) && genreMap.TryGetValue(genreId, out var genre))
            {
                genres.Add(genre);
            }
        }
    }

    private static DateTime? ReadDate(JsonElement element, params string[] propertyNames)
    {
        var value = ReadString(element, propertyNames);
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date.ToUniversalTime()
            : null;
    }

    private static double? ReadDouble(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetProperty(element, propertyName, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String
                && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            {
                return number;
            }
        }

        return null;
    }

    private static string? ReadString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetProperty(element, propertyName, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }

            if (value.ValueKind == JsonValueKind.Number)
            {
                return value.GetRawText();
            }
        }

        return null;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string NormalizeUserId(string? value) => (value ?? string.Empty).Replace("-", string.Empty, StringComparison.Ordinal);

    private sealed record RecommendationSeed(RecommendationTitle Title, string TmdbId, string SeerrMediaType);

    private sealed record LinkedSeerrAccount(string BaseUrl, string SeerrUserId);
}