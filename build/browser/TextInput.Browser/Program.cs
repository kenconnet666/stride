// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Games;
using Stride.Graphics;
using Stride.Graphics.Font;
using Stride.Input;
using Stride.Rendering;
using Stride.Rendering.Compositing;
using Stride.Rendering.UI;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

/// <summary>
/// A Stride UI page with an <see cref="EditText"/>, to type into with a keyboard, an input method or an on-screen keyboard.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class Program
{
    private const string FontName = "Text input font";

    private static Game? game;
    private static GameContextBrowser? context;
    private static EditText? edit;
    private static TextBlock? echo;

    private static InputSourceBrowser Source => (InputSourceBrowser)game!.Input.Sources.Single();

    public static void Main()
    {
        GlobalLogger.GlobalMessageLogged += message => ReportLog($"[{message.Module}]: {message.Type}: {message.Text}", (int)message.Type);
    }

    [JSExport]
    public static async Task Start(string manifestUrl, string fontUrl, int width, int height)
    {
        if (game != null) throw new InvalidOperationException("The text input page is already started.");
        using var http = new HttpClient();
        await BrowserContent.PreloadAsync(http, new Uri(manifestUrl));
        // FreeType reads the font from a file, also later when it generates new glyphs.
        var fontPath = Path.GetTempFileName();
        await File.WriteAllBytesAsync(fontPath, await http.GetByteArrayAsync(fontUrl));

        await GraphicsAdapterFactory.InitializeWebGPUAsync();
        game = new Game();
        game.GraphicsDeviceManager.PreferredBackBufferWidth = width;
        game.GraphicsDeviceManager.PreferredBackBufferHeight = height;
        context = new GameContextBrowser("#game", width, height);
        game.Run(context);

        var fonts = (FontSystem)game.Font;
        fonts.RuntimeFonts.RegisterFont(FontName, fontPath);
        var font = fonts.LoadRuntimeFont(FontName, 32) ?? throw new InvalidOperationException("The font did not load.");

        var camera = new CameraComponent();
        var compositor = GraphicsCompositorHelper.CreateDefault(false, camera: camera, clearColor: new Color4(0.08f, 0.1f, 0.14f, 1));
        compositor.RenderFeatures.Add(new UIRenderFeature
        {
            RenderStageSelectors =
            {
                new SimpleGroupToRenderStageSelector { RenderStage = compositor.RenderStages.Single(stage => stage.Name == "Transparent"), EffectName = "Test" },
            },
        });
        game.SceneSystem.GraphicsCompositor = compositor;

        edit = new EditText
        {
            Font = font,
            TextSize = 32,
            Width = width - 80,
            Height = 64,
            BackgroundColor = new Color(40, 48, 64),
            Margin = new Thickness(0, 16, 0, 16),
        };
        echo = new TextBlock { Font = font, TextSize = 24, TextColor = Color.LightGreen, WrapText = true, Width = width - 80 };
        var page = new UIPage
        {
            RootElement = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(40, 40, 40, 40),
                Children =
                {
                    new TextBlock { Font = font, TextSize = 28, TextColor = Color.White, Text = "Stride 文本输入：点击下面的输入框" },
                    edit,
                    echo,
                },
            },
        };
        var scene = game.SceneSystem.SceneInstance.RootScene;
        scene.Entities.Add(new Entity("Camera") { camera });
        scene.Entities.Add(new Entity("UI") { new UIComponent { Page = page, Resolution = new Vector3(width, height, 1000) } });

        Source.TextInputActiveChanged += SetTextInput;
    }

    [JSExport]
    public static void Frame(int width, int height, bool focused, bool visible)
    {
        try
        {
            echo!.Text = edit!.Composition.Length > 0 ? $"组字中：{edit.Composition}" : $"内容（{edit.Text.Length} 字）：{edit.Text}";
            context!.AdvanceFrame(width, height, focused, visible);
        }
        catch (Exception error)
        {
            ReportError(error.ToString());
            throw;
        }
    }

    /// <summary>The text, the composition, whether text input is active and the frame count, separated by U+001F.</summary>
    [JSExport]
    public static string State() => string.Join('\u001f', edit?.Text, edit?.Composition, Source.IsTextInputActive, game?.DrawTime.FrameCount ?? 0);

    [JSExport] public static bool Key(string code, bool down) => Source.SubmitKey(code, down);
    [JSExport] public static void Move(float x, float y) => Source.SubmitMouseMove(x, y);
    [JSExport] public static void Button(int button, bool down) => Source.SubmitMouseButton(button, down);
    [JSExport] public static void Wheel(float ticks) => Source.SubmitWheel(ticks);
    [JSExport] public static void Pointer(int id, int type, float x, float y) => Source.SubmitPointer(id, (PointerEventType)type, x, y);
    [JSExport] public static void Surface(int width, int height) => Source.SetSurfaceSize(width, height);
    [JSExport] public static void CancelMouse() => Source.CancelMouse();
    [JSExport] public static void Pause() => Source.Pause();
    [JSExport] public static void Text(string text) => Source.SubmitText(text);
    [JSExport] public static void Composition(string text, int selectionStart, int selectionLength) => Source.SubmitComposition(text, selectionStart, selectionLength);

    [JSImport("managedError", "text-input")] private static partial void ReportError(string error);
    [JSImport("log", "text-input")] private static partial void ReportLog(string message, int level);
    [JSImport("setTextInput", "text-input")] private static partial void SetTextInput(bool active);
}
