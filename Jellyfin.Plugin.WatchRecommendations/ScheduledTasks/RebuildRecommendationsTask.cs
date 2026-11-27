using MediaBrowser.Model.Tasks;
using Jellyfin.Plugin.WatchRecommendations.Services;

namespace Jellyfin.Plugin.WatchRecommendations.ScheduledTasks;

public class RebuildRecommendationsTask : IScheduledTask, IConfigurableScheduledTask
{
    private readonly RecommendationsRefreshService _refreshService;

    public RebuildRecommendationsTask(RecommendationsRefreshService refreshService)
    {
        _refreshService = refreshService;
    }

    public string Name => "Rebuild Watch Recommendations";

    public string Key => "WatchRecommendationsRebuild";

    public string Description => "Recalculates the personalized watch recommendations for Jellyfin users.";

    public string Category => "Watch Recommendations";

    public bool IsHidden => false;

    public bool IsEnabled => true;

    public bool IsLogged => true;

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() =>
    [
        new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromHours(24).Ticks
        }
    ];

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken) =>
        _refreshService.RefreshAllUsersAsync(progress, cancellationToken);
}