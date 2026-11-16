// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Stride.Core.Assets.Presentation.ViewModels;
using Stride.Core.Diagnostics;
using Stride.Core.Translation;

namespace Stride.Core.Assets.Editor.Components.Build;

/// <summary>
/// The deploy step of the browser games: the built browser head is served from this machine by the development server
/// of .NET (<c>dotnet run</c>), and the default browser opens its page.
/// </summary>
/// <remarks>
/// The server of the last game started keeps running until the next one starts, so that the page can be reloaded.
/// </remarks>
internal sealed class BrowserGameDeployer : IGameDeployer
{
    private const string AppUrlPrefix = "App url:";
    private static readonly TimeSpan ServerStartTimeout = TimeSpan.FromMinutes(2);
    private string configuration = "Debug";
    private Process? server;

    /// <inheritdoc/>
    public Task<GameProjectBuildRequest?> PrepareBuildAsync(ProjectViewModel project, GameProjectBuildRequest request, bool startProject, ILogger logger, CancellationToken cancellationToken)
    {
        // The server serves the build of this configuration
        configuration = request.Configuration;
        return Task.FromResult<GameProjectBuildRequest?>(request);
    }

    /// <inheritdoc/>
    public async Task<bool> DeployAsync(ProjectViewModel project, IGameProjectBuild build, ILogger logger, CancellationToken cancellationToken)
    {
        StopServer();
        var projectPath = project.ProjectPath.ToOSPath();
        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(projectPath),
        };
        foreach (var argument in new[] { "run", "--no-build", "--configuration", configuration, "--project", projectPath })
            startInfo.ArgumentList.Add(argument);
        startInfo.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{FreePort()}";

        var appUrl = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, line) =>
        {
            if (TryGetAppUrl(line.Data) is { } url) appUrl.TrySetResult(url);
        };
        process.ErrorDataReceived += (_, line) =>
        {
            if (!string.IsNullOrWhiteSpace(line.Data)) logger.Warning(line.Data);
        };
        process.Exited += (_, _) => appUrl.TrySetException(new InvalidOperationException(Tr._p("Message", "The development server of the browser game stopped before serving the page.")));
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        server = process;

        string url;
        try
        {
            url = await appUrl.Task.WaitAsync(ServerStartTimeout, cancellationToken);
        }
        catch (Exception e) when (e is TimeoutException or InvalidOperationException)
        {
            logger.Error(string.Format(Tr._p("Message", "Couldn't serve {0} in a browser: {1}"), project.Name, e.Message));
            StopServer();
            return false;
        }
        logger.Info(string.Format(Tr._p("Message", "{0} is served at {1}."), project.Name, url));
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        return true;
    }

    /// <summary>
    /// Reads the address of the page from a line of the development server, which prints it as <c>App url: http://...</c>.
    /// </summary>
    internal static string? TryGetAppUrl(string? line)
    {
        if (line is null) return null;
        var start = line.IndexOf(AppUrlPrefix, StringComparison.Ordinal);
        if (start < 0) return null;
        var url = line[(start + AppUrlPrefix.Length)..].Trim();
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? url : null;
    }

    private void StopServer()
    {
        if (server is null) return;
        try
        {
            if (!server.HasExited) server.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // It exited in the meantime
        }
        server.Dispose();
        server = null;
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
