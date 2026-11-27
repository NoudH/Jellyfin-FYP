using System.Reflection;
using System.Reflection.Emit;
using Jellyfin.Plugin.WatchRecommendations.Services;
using Xunit;

namespace Jellyfin.Plugin.WatchRecommendations.Tests;

public class JellyfinEnhancedSeerrRecommendationsFilterTests
{
    [Fact]
    public async Task ReturnsUnfilteredRecommendationsWhenParentalFilterIsNotRegistered()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("Jellyfin.Plugin.JellyfinEnhanced"),
            AssemblyBuilderAccess.Run);
        assembly.DefineDynamicModule("JellyfinEnhanced")
            .DefineType(
                "Jellyfin.Plugin.JellyfinEnhanced.Services.SeerrParentalFilter",
                TypeAttributes.Public | TypeAttributes.Class)
            .CreateType();

        const string json = "{\"results\":[{\"title\":\"External recommendation\"}]}";
        var filter = new JellyfinEnhancedSeerrRecommendationsFilter(new EmptyServiceProvider());

        var result = await filter.FilterAsync(json, "/api/v1/movie/101/recommendations?page=1", Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(json, result);
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}