using System.Reflection;

namespace Jellyfin.Plugin.WatchRecommendations.Services;

public sealed class JellyfinEnhancedSeerrRecommendationsFilter : ISeerrRecommendationsFilter
{
    private const string JellyfinEnhancedAssemblyName = "Jellyfin.Plugin.JellyfinEnhanced";
    private const string ParentalFilterTypeName = "Jellyfin.Plugin.JellyfinEnhanced.Services.SeerrParentalFilter";
    private readonly IServiceProvider _serviceProvider;

    public JellyfinEnhancedSeerrRecommendationsFilter(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<string?> FilterAsync(
        string json,
        string apiPath,
        Guid jellyfinUserId,
        CancellationToken cancellationToken)
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(candidate => string.Equals(
                candidate.GetName().Name,
                JellyfinEnhancedAssemblyName,
                StringComparison.OrdinalIgnoreCase));
        var filterType = assembly?.GetType(ParentalFilterTypeName, throwOnError: false)
            ?? throw new InvalidOperationException("JE's Seerr parental filter is unavailable.");
        var filter = _serviceProvider.GetService(filterType);
        if (filter is null)
        {
            return json;
        }

        var applyMethod = filterType.GetMethod(
            "ApplyAsync",
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: [typeof(string), typeof(string), typeof(string), typeof(CancellationToken)],
            modifiers: null)
            ?? throw new InvalidOperationException("JE's Seerr parental filter API is incompatible.");
        var task = applyMethod.Invoke(filter, [json, apiPath, jellyfinUserId.ToString("D"), cancellationToken]) as Task
            ?? throw new InvalidOperationException("JE's Seerr parental filter returned an unexpected result.");

        await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        var result = task.GetType().GetProperty("Result", BindingFlags.Public | BindingFlags.Instance)?.GetValue(task)
            ?? throw new InvalidOperationException("JE's Seerr parental filter returned no result.");
        if (ReadBoolean(result, "RetryLater"))
        {
            throw new InvalidOperationException("JE could not verify the parental ratings for Seerr recommendations yet.");
        }

        return ReadBoolean(result, "Block") ? null : ReadString(result, "Body");
    }

    private static bool ReadBoolean(object instance, string propertyName) =>
        instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance) is true;

    private static string? ReadString(object instance, string propertyName) =>
        instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance) as string;
}