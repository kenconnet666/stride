// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Browser;

[assembly: SupportedOSPlatform("browser")]

namespace Stride.GameStudio.Avalonia.Browser;

internal static class Program
{
    private const string ProjectDirectory = "/project";

    // The first argument may be the address of a project archive (a .zip of a package and its assets) to open.
    private static async Task Main(string[] args)
    {
        if (args.Length > 0 && Uri.TryCreate(args[0], UriKind.Absolute, out var archive) && archive.Scheme is "http" or "https")
            args = [await DownloadProjectAsync(archive), .. args[1..]];
        App.Options = StartupOptions.Parse(args);
        await BuildAvaloniaApp().StartBrowserAppAsync("out");
    }

    /// <summary>
    /// Downloads a project archive into the file system of the tab, and returns the path of its package file.
    /// </summary>
    private static async Task<string> DownloadProjectAsync(Uri archive)
    {
        using var http = new HttpClient();
        await using var stream = new MemoryStream(await http.GetByteArrayAsync(archive));
        if (Directory.Exists(ProjectDirectory))
            Directory.Delete(ProjectDirectory, recursive: true);
        System.IO.Compression.ZipFile.ExtractToDirectory(stream, ProjectDirectory);
        return Directory.EnumerateFiles(ProjectDirectory, "*.sdpkg", SearchOption.AllDirectories).Order(StringComparer.Ordinal).FirstOrDefault()
            ?? throw new InvalidOperationException($"The archive [{archive}] has no package file (.sdpkg).");
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .WithInterFont();
}
