using System.Text.Json;
using Jellyfin.Plugin.WatchRecommendations.Model;

namespace Jellyfin.Plugin.WatchRecommendations.Services;

public sealed class RecommendationCacheStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _cacheFilePath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private IReadOnlyDictionary<Guid, UserRecommendationCache> _entries;

    public RecommendationCacheStore(string pluginsPath)
    {
        _cacheFilePath = Path.Combine(pluginsPath, "configurations", "Jellyfin.Plugin.WatchRecommendations", "recommendations-cache.json");
        _entries = Load();
    }

    public UserRecommendationCache? Get(Guid userId)
    {
        var entries = Volatile.Read(ref _entries);
        return entries.TryGetValue(userId, out var result) ? result : null;
    }

    public async Task ReplaceUserAsync(UserRecommendationCache result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.UserId == Guid.Empty)
        {
            throw new ArgumentException("A recommendation cache must belong to a Jellyfin user.", nameof(result));
        }

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var nextEntries = new Dictionary<Guid, UserRecommendationCache>(Volatile.Read(ref _entries))
            {
                [result.UserId] = result
            };

            await SaveAtomicallyAsync(nextEntries, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _entries, nextEntries);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private IReadOnlyDictionary<Guid, UserRecommendationCache> Load()
    {
        if (!File.Exists(_cacheFilePath))
        {
            return new Dictionary<Guid, UserRecommendationCache>();
        }

        try
        {
            var contents = File.ReadAllText(_cacheFilePath);
            return JsonSerializer.Deserialize<Dictionary<Guid, UserRecommendationCache>>(contents, JsonOptions)
                ?? new Dictionary<Guid, UserRecommendationCache>();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new Dictionary<Guid, UserRecommendationCache>();
        }
    }

    private async Task SaveAtomicallyAsync(
        IReadOnlyDictionary<Guid, UserRecommendationCache> entries,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_cacheFilePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{_cacheFilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var contents = JsonSerializer.Serialize(entries, JsonOptions);
            await File.WriteAllTextAsync(temporaryPath, contents, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, _cacheFilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}