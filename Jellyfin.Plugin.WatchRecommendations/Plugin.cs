using Jellyfin.Plugin.WatchRecommendations.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.WatchRecommendations;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
    }

    public override string Name => "Watch Recommendations";

    public override Guid Id => Guid.Parse("2e8de290-51e4-4a2d-b148-73f43e48a72a");

    public IEnumerable<PluginPageInfo> GetPages() => Array.Empty<PluginPageInfo>();

    public IEnumerable<PluginPageInfo> GetViews() =>
    [
        new PluginPageInfo
        {
            Name = "watchRecommendationsPage",
            EmbeddedResourcePath = $"{GetType().Namespace}.PluginPages.RecommendationsPage.html"
        }
    ];
}