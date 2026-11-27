using Jellyfin.Plugin.WatchRecommendations.ScheduledTasks;
using Jellyfin.Plugin.WatchRecommendations.Services;
using MediaBrowser.Controller;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.WatchRecommendations;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHttpClient(JellyfinEnhancedSeerrCandidateProvider.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false
            });
        serviceCollection.AddSingleton<RecommendationRanker>();
        serviceCollection.AddSingleton<RecommendationDataSource>();
        serviceCollection.AddSingleton<IRecommendationItemsProvider, RecommendationItemsProvider>();
        serviceCollection.AddSingleton<IRecommendationDataSource>(provider => provider.GetRequiredService<RecommendationDataSource>());
        serviceCollection.AddSingleton<ISeerrIntegrationConfigurationSource, JellyfinEnhancedSeerrConfigurationSource>();
        serviceCollection.AddSingleton<ISeerrRecommendationsFilter, JellyfinEnhancedSeerrRecommendationsFilter>();
        serviceCollection.AddSingleton<ISeerrCandidateProvider, JellyfinEnhancedSeerrCandidateProvider>();
        serviceCollection.AddSingleton(provider => new RecommendationCacheStore(provider.GetRequiredService<IApplicationPaths>().PluginsPath));
        serviceCollection.AddSingleton<RecommendationsRefreshService>();
        serviceCollection.AddTransient<RebuildRecommendationsTask>();
    }
}