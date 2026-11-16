using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Stride.Engine;
using Stride.Core.Diagnostics;
using Stride.Shaders.Compiler;
using Stride.UI;
using Stride.UI.Controls;

[SupportedOSPlatform("browser")]
public static partial class Program
{
    private static Game? game;
    public static void Main()
    {
        GlobalLogger.GlobalMessageLogged += message => ReportLog($"[{message.Module}]: {message.Type}: {message.Text}", (int)message.Type);
    }

    [JSExport]
    public static async Task Start()
    {
        if (game != null) throw new InvalidOperationException("Space Escape is already started.");
        // The page sizes its canvas for the portrait game: the game takes that size
        var options = new BrowserGameOptions { GameAssemblies = [typeof(SpaceEscape.GameScript).Assembly.GetName().Name!], UsePreferredBackBufferSize = false };
        game = await BrowserGame.RunAsync(() => new Game(), options);
        game.EffectSystem.Compiler = new EffectRequestRecorder((EffectCompilerBase)game.EffectSystem.Compiler, ReportEffectRequest);
    }
    [JSImport("log", "space-escape")] private static partial void ReportLog(string message, int level);
    [JSImport("effectRequest", "space-escape")] private static partial void ReportEffectRequest(string hash, string payload);
    [JSExport] public static int Entities() => game?.SceneSystem.SceneInstance?.RootScene?.Entities.Count ?? 0;
    private static SpaceEscape.CharacterScript? Character => game?.SceneSystem.SceneInstance?.RootScene?.Entities
        .SelectMany(entity => entity.Components).OfType<SpaceEscape.CharacterScript>().FirstOrDefault();
    [JSExport] public static int Lane() => Character?.CurLane ?? -1;
    [JSExport] public static bool Dead() => Character?.IsDead ?? false;
    [JSExport] public static float CharacterX() => Character?.Entity.Transform.Position.X ?? 0;
    [JSExport] public static float HitboxHeight()
    {
        var bounds = Character?.CalculateCurrentBoundingBox();
        return bounds.HasValue ? bounds.Value.Maximum.Y - bounds.Value.Minimum.Y : 0;
    }
    [JSExport] public static string UiText()
    {
        var root = game?.SceneSystem.SceneInstance?.RootScene;
        if (root == null) return string.Empty;
        return string.Join("|", root.Entities.SelectMany(entity => entity.Components).OfType<UIComponent>()
            .Where(component => component.Page?.RootElement != null)
            .SelectMany(component => Texts(component.Page.RootElement)));
        static IEnumerable<string> Texts(UIElement element)
        {
            if (element is TextBlock text) yield return text.Text;
            foreach (var child in element.VisualChildren)
                foreach (var value in Texts(child)) yield return value;
        }
    }
    private static System.Threading.Channels.ChannelReader<ProfilingEvent>? profile;
    private static int profileStartFrame;
    /// Turns the engine profiler on. ProfileReport then gives the time per frame of each profiling key since this call.
    [JSExport] public static void StartProfile()
    {
        Profiler.EnableAll();
        profile ??= Profiler.Subscribe();
        while (profile.TryRead(out _)) { }
        profileStartFrame = game!.DrawTime.FrameCount;
    }
    /// One line per profiling key, the slowest first: name, milliseconds per frame, events per frame.
    [JSExport] public static string ProfileReport()
    {
        if (profile == null) return string.Empty;
        var totals = new Dictionary<string, (double Ms, int Count)>();
        while (profile.TryRead(out var e))
        {
            if (e.Type != ProfilingMessageType.End) continue;
            var total = totals.GetValueOrDefault(e.Key.Name);
            totals[e.Key.Name] = (total.Ms + e.ElapsedTime.TotalMilliseconds, total.Count + 1);
        }
        var frames = Math.Max(1, game!.DrawTime.FrameCount - profileStartFrame);
        return string.Join('\n', totals.OrderByDescending(x => x.Value.Ms)
            .Select(x => FormattableString.Invariant($"{x.Key}\t{x.Value.Ms / frames:0.000}\t{(double)x.Value.Count / frames:0.0}")));
    }
    [JSExport] public static double Frames() => game?.DrawTime.FrameCount ?? 0;
    [JSExport]
    public static async Task Stop()
    {
        await BrowserGame.StopAsync();
        game = null;
    }
}
