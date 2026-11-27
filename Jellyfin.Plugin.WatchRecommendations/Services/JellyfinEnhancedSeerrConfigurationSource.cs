using System.Reflection;

namespace Jellyfin.Plugin.WatchRecommendations.Services;

public sealed class JellyfinEnhancedSeerrConfigurationSource : ISeerrIntegrationConfigurationSource
{
    private const string JellyfinEnhancedAssemblyName = "Jellyfin.Plugin.JellyfinEnhanced";
    private const string JellyfinEnhancedPluginTypeName = "Jellyfin.Plugin.JellyfinEnhanced.JellyfinEnhanced";

    public SeerrIntegrationConfiguration? GetConfiguration()
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(candidate => string.Equals(
                candidate.GetName().Name,
                JellyfinEnhancedAssemblyName,
                StringComparison.OrdinalIgnoreCase));
        var pluginType = assembly?.GetType(JellyfinEnhancedPluginTypeName, throwOnError: false);
        var instance = pluginType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        var configuration = instance?.GetType().GetProperty("Configuration", BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance);
        if (configuration is null)
        {
            return null;
        }

        var configurationType = configuration.GetType();
        if (configurationType.GetProperty("JellyseerrEnabled") is null
            || configurationType.GetProperty("JellyseerrUrls") is null
            || configurationType.GetProperty("JellyseerrApiKey") is null
            || configurationType.GetProperty("JellyseerrImportBlockedUsers") is null)
        {
            return null;
        }

        return new SeerrIntegrationConfiguration(
            ReadProperty<bool>(configuration, "JellyseerrEnabled"),
            ReadProperty<string>(configuration, "JellyseerrUrls"),
            ReadProperty<string>(configuration, "JellyseerrApiKey"),
            ReadProperty<string>(configuration, "JellyseerrImportBlockedUsers"));
    }

    private static T? ReadProperty<T>(object instance, string name)
    {
        var value = instance.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance);
        return value is T typedValue ? typedValue : default;
    }
}