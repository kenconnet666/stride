// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
#if STRIDE_PLATFORM_BROWSER
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using Stride.Games;
using Stride.Graphics;
using Stride.Input;

namespace Stride.Engine;

/// <summary>
/// Runs a game in a browser page: loads its content, gives it a canvas and the input of the page, and draws a frame
/// for each animation frame of the browser.
/// </summary>
/// <remarks>
/// The page needs a canvas and the .NET runtime, nothing of Stride: the script of this class is in the assembly.
/// It publishes the state of the game as <c>globalThis.strideGame</c> (<c>status</c>, <c>frames</c>, <c>frameMs</c>,
/// <c>error</c>) and raises the <c>stride-game-failed</c> event on the window when a frame throws.
/// </remarks>
[SupportedOSPlatform("browser")]
public static partial class BrowserGame
{
    private const string Module = "stride-browser-game";
    private static GameContextBrowser context;
    private static InputSourceBrowser input;

    /// <summary>The game that runs in the page, or null.</summary>
    public static Game Current { get; private set; }

    /// <summary>Starts a game in the page. The task ends when the game runs: the browser then drives its frames.</summary>
    /// <param name="createGame">Creates the game, once its content is loaded and the graphics device is ready.</param>
    public static async Task<Game> RunAsync(Func<Game> createGame, BrowserGameOptions options = null)
    {
        ArgumentNullException.ThrowIfNull(createGame);
        if (Current != null) throw new InvalidOperationException("A game already runs in this page.");
        options ??= new BrowserGameOptions();
        // Nothing in the head of a game names its scripts: its assemblies are loaded here, and their module initializers
        // register the scripts and their serializers.
        foreach (var assembly in options.GameAssemblies)
            RuntimeHelpers.RunModuleConstructor(Assembly.Load(assembly).ManifestModule.ModuleHandle);

        await JSHost.ImportAsync(Module, ModuleUrl());
        using (var http = new HttpClient())
            await BrowserContent.PreloadAsync(http, new Uri(new Uri(BaseUrl()), options.ContentManifest));
        await GraphicsAdapterFactory.InitializeWebGPUAsync();

        var game = createGame();
        context = new GameContextBrowser(options.Canvas, CanvasWidth(options.Canvas), CanvasHeight(options.Canvas));
        Current = game;
        try
        {
            game.Run(context);
            input = game.Input.Sources.OfType<InputSourceBrowser>().Single();
            input.TextInputActiveChanged += SetTextInput;
            var manager = game.GraphicsDeviceManager;
            await Start(options.Canvas, options.UsePreferredBackBufferSize ? manager.PreferredBackBufferWidth : 0, options.UsePreferredBackBufferSize ? manager.PreferredBackBufferHeight : 0);
        }
        catch
        {
            await StopAsync();
            throw;
        }
        return game;
    }

    /// <summary>Stops the game of the page and releases its graphics device.</summary>
    public static async Task StopAsync()
    {
        if (Current == null) return;
        Stop();
        if (input != null) input.TextInputActiveChanged -= SetTextInput;
        await Current.DisposeAsync();
        Current = null;
        context = null;
        input = null;
        GraphicsAdapterFactory.Dispose();
    }

    // The two scripts make one module: the game script uses the input adapter, and a module loaded from a data address
    // cannot import a file next to it.
    private static string ModuleUrl()
    {
        var script = new StringBuilder();
        foreach (var name in new[] { "browser-input.js", "browser-game.js" })
        {
            using var stream = typeof(BrowserGame).Assembly.GetManifestResourceStream("Stride.Engine.Browser." + name)
                ?? throw new InvalidOperationException($"The script {name} is not in Stride.Engine.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            script.AppendLine(reader.ReadToEnd());
        }
        return "data:text/javascript;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(script.ToString()));
    }

    [JSImport("baseUrl", Module)] private static partial string BaseUrl();
    [JSImport("canvasWidth", Module)] private static partial int CanvasWidth(string canvas);
    [JSImport("canvasHeight", Module)] private static partial int CanvasHeight(string canvas);
    [JSImport("start", Module)] private static partial Task Start(string canvas, int width, int height);
    [JSImport("stop", Module)] private static partial void Stop();
    [JSImport("setTextInput", Module)] private static partial void SetTextInput(bool active);
    [JSImport("managedError", Module)] private static partial void ReportError(string error);

    // What the script of the page calls: one frame, and the events of the canvas.

    [JSExport]
    internal static void Frame(int width, int height, bool focused, bool visible)
    {
        try
        {
            context.AdvanceFrame(width, height, focused, visible);
        }
        catch (Exception error)
        {
            // The exception that reaches the script has the message only
            ReportError(error.ToString());
            throw;
        }
    }

    [JSExport] internal static bool Key(string code, bool down) => input.SubmitKey(code, down);
    [JSExport] internal static void Text(string text) => input.SubmitText(text);
    [JSExport] internal static void Composition(string text, int selectionStart, int selectionLength) => input.SubmitComposition(text, selectionStart, selectionLength);
    [JSExport] internal static void Move(float x, float y) => input.SubmitMouseMove(x, y);
    [JSExport] internal static void Button(int button, bool down) => input.SubmitMouseButton(button, down);
    [JSExport] internal static void Wheel(float ticks) => input.SubmitWheel(ticks);
    [JSExport] internal static void CancelMouse() => input.CancelMouse();
    [JSExport] internal static void Pointer(int id, int type, float x, float y) => input.SubmitPointer(id, (PointerEventType)type, x, y);
    [JSExport] internal static void Surface(int width, int height) => input.SetSurfaceSize(width, height);
    [JSExport] internal static void Pause() => input.Pause();
    [JSExport] internal static void GamePadRemoved(int index) => input.RemoveGamePad(index);

    [JSExport]
    internal static void GamePad(int index, string name, int buttons, float leftX, float leftY, float rightX, float rightY, float leftTrigger, float rightTrigger)
        => input.SubmitGamePad(index, name, buttons, leftX, leftY, rightX, rightY, leftTrigger, rightTrigger);
}

/// <summary>What <see cref="BrowserGame.RunAsync"/> needs to know about the page and the game.</summary>
public sealed class BrowserGameOptions
{
    /// <summary>The CSS selector of the canvas the game draws on.</summary>
    public string Canvas { get; set; } = "#game";

    /// <summary>The address of the content manifest, relative to the page.</summary>
    public string ContentManifest { get; set; } = "content/manifest.json";

    /// <summary>The names of the assemblies of the game: the ones with its scripts.</summary>
    public string[] GameAssemblies { get; set; } = [];

    /// <summary>
    /// Whether the canvas takes the back buffer size of the game settings. Otherwise it keeps the size the page gave it.
    /// </summary>
    public bool UsePreferredBackBufferSize { get; set; } = true;
}
#endif
