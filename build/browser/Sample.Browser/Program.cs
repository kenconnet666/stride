using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Stride.Core.Diagnostics;
using Stride.Engine;
using Stride.Games;
using Stride.Graphics;
using Stride.Input;
using Stride.Shaders.Compiler;
using Stride.UI;
using Stride.UI.Controls;

// Runs the game of a sample (Sample.Game, built with -p:Sample=...) in a browser page.
[SupportedOSPlatform("browser")]
public static partial class Program
{
    private static Game? game;
    private static GameContextBrowser? context;
    private static InputSourceBrowser Source => (InputSourceBrowser)game!.Input.Sources.Single();

    public static void Main()
    {
        GlobalLogger.GlobalMessageLogged += message => ReportLog($"[{message.Module}]: {message.Type}: {message.Text}", (int)message.Type);
    }

    /// The name of the sample this host was built for.
    [JSExport]
    public static string SampleName() => typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(x => x.Key == "Sample").Value!;

    [JSExport]
    public static async Task Start(string manifestUrl, int width, int height)
    {
        if (game != null) throw new InvalidOperationException("The sample is already started.");
        // The module initializer of the game assembly registers its scripts and their serializers
        try { RuntimeHelpers.RunModuleConstructor(Assembly.Load($"{SampleName()}.Game").ManifestModule.ModuleHandle); }
        catch (TypeInitializationException error) when (error.InnerException != null)
        {
            // The exception of an initializer says nothing itself: the page gets its cause
            ReportError(error.InnerException.ToString());
            throw;
        }
        using var http = new HttpClient();
        await BrowserContent.PreloadAsync(http, new Uri(manifestUrl));
        await GraphicsAdapterFactory.InitializeWebGPUAsync();
        game = new Game();
        context = new GameContextBrowser("#game", width, height);
        try { game.Run(context); }
        catch { await Stop(); throw; }
        game.EffectSystem.Compiler = new EffectRequestRecorder((EffectCompilerBase)game.EffectSystem.Compiler, ReportEffectRequest);
    }

    [JSExport]
    public static void Frame(int width, int height, bool focused, bool visible)
    {
        try { context!.AdvanceFrame(width, height, focused, visible); }
        catch (Exception error) { ReportError(error.ToString()); throw; }
    }

    [JSImport("managedError", "sample")] private static partial void ReportError(string error);
    [JSImport("log", "sample")] private static partial void ReportLog(string message, int level);
    [JSImport("effectRequest", "sample")] private static partial void ReportEffectRequest(string hash, string payload);

    /// The back buffer size the game settings of the sample ask for: the page gives it to its canvas.
    [JSExport] public static int PreferredWidth() => game!.GraphicsDeviceManager.PreferredBackBufferWidth;
    [JSExport] public static int PreferredHeight() => game!.GraphicsDeviceManager.PreferredBackBufferHeight;

    [JSExport] public static double Frames() => game?.DrawTime.FrameCount ?? 0;
    /// The gamepads the game sees, one per line: index, buttons, thumbsticks and triggers.
    [JSExport]
    public static string GamePads() => string.Join('\n', game!.Input.GamePads.Select(pad =>
        FormattableString.Invariant($"{pad.Index}: {pad.State.Buttons} L{pad.State.LeftThumb.X:0.##},{pad.State.LeftThumb.Y:0.##} R{pad.State.RightThumb.X:0.##},{pad.State.RightThumb.Y:0.##} T{pad.State.LeftTrigger:0.##},{pad.State.RightTrigger:0.##}")));

    [JSExport] public static int Entities() => game?.SceneSystem.SceneInstance?.RootScene?.Entities.Count ?? 0;

    /// The texts of the UI pages of the scene, separated by '|': what a test reads to know where the game is.
    [JSExport]
    public static string UiText()
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

    /// The state of the game, for a page or a test that wants to know why nothing shows.
    [JSExport]
    public static string Diagnostics()
    {
        if (game == null) return "not started";
        var scene = game.SceneSystem.SceneInstance?.RootScene;
        var sceneUrl = game.SceneSystem.InitialSceneUrl;
        return $"settings={(game.Settings == null ? "none" : "loaded")} initialScene={sceneUrl ?? "none"} "
            + $"sceneInContent={sceneUrl != null && game.Content.Exists(sceneUrl)} sceneInstance={game.SceneSystem.SceneInstance != null} "
            + $"entities={scene?.Entities.Count ?? -1} children={scene?.Children.Count ?? -1} compositor={game.SceneSystem.GraphicsCompositor != null} "
            + $"gameRenderer={game.SceneSystem.GraphicsCompositor?.Game?.GetType().Name ?? "none"} "
            + $"running={game.IsRunning} active={game.IsActive} drawFrames={game.DrawTime.FrameCount} "
            + $"backBuffer={game.GraphicsDevice.Presenter?.BackBuffer?.Width}x{game.GraphicsDevice.Presenter?.BackBuffer?.Height}";
    }

    [JSExport] public static bool Key(string code, bool down) => Source.SubmitKey(code, down);
    [JSExport] public static void Move(float x, float y) => Source.SubmitMouseMove(x, y);
    [JSExport] public static void Button(int button, bool down) => Source.SubmitMouseButton(button, down);
    [JSExport] public static void Wheel(float ticks) => Source.SubmitWheel(ticks);
    [JSExport] public static void Pointer(int id, int type, float x, float y) => Source.SubmitPointer(id, (PointerEventType)type, x, y);
    [JSExport] public static void Surface(int width, int height) => Source.SetSurfaceSize(width, height);
    [JSExport] public static void CancelMouse() => Source.CancelMouse();
    [JSExport]
    public static void GamePad(int index, string name, int buttons, float leftX, float leftY, float rightX, float rightY, float leftTrigger, float rightTrigger)
        => Source.SubmitGamePad(index, name, buttons, leftX, leftY, rightX, rightY, leftTrigger, rightTrigger);
    [JSExport] public static void GamePadRemoved(int index) => Source.RemoveGamePad(index);
    [JSExport] public static void Pause() => Source.Pause();

    [JSExport]
    public static async Task Stop()
    {
        if (game == null) return;
        await game.DisposeAsync();
        game = null;
        context = null;
        GraphicsAdapterFactory.Dispose();
    }
}
