using Stride.Core;
using Stride.Core.Threading;
using Stride.Data;

namespace Stride.BepuPhysics.Navigation.Definitions;
[DataContract("RecastNavigationConfiguration")]
[Display("Recast Navigation")]
public class RecastNavigationConfiguration : Configuration
{
    [Display("Build Settings", Expand = ExpandRule.Never)]
    public BuildSettings BuildSettings { get; set; } = new();

    [Display("Pathfinding Settings", Expand = ExpandRule.Never)]
    public PathfindingSettings PathfindingSettings { get; set; } = new();

    /// <summary>
    /// Total thread count to use for pathfinding, or 0 for half of those of the machine: all of them make the game stutter.
    /// </summary>
    public int UsableThreadCount { get; set; }

    internal int ThreadCount => UsableThreadCount > 0 ? UsableThreadCount : Math.Max(1, Dispatcher.MaxDegreeOfParallelism / 2);
}
