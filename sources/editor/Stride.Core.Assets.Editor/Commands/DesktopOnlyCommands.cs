// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System.Collections.Frozen;

namespace Stride.Core.Assets.Editor.Commands;

/// <summary>
/// The commands of the editor that need the desktop Game Studio: they start a process, build a project, or read the files
/// and the tools of the machine. A plugin marks its own with <see cref="CommandDescriptor.DesktopOnly"/>.
/// </summary>
public static class DesktopOnlyCommands
{
    private static readonly FrozenSet<string> Ids = new[]
    {
        // Other programs of the machine
        "Stride.Project.OpenInIDE",
        "Stride.Asset.OpenFile",
        "Stride.Asset.OpenSourceFile",
        "Stride.Asset.OpenWithTextEditor",
        "Stride.Asset.ShowInExplorer",
        "Stride.Asset.ShowSourceInExplorer",
        // MSBuild, NuGet and the compiled game
        "Stride.Project.Build",
        "Stride.Project.CancelBuild",
        "Stride.Project.Start",
        "Stride.Project.ReloadGameAssemblies",
        "Stride.Project.NewProject",
        "Stride.Project.AddExistingProject",
        "Stride.Project.AddDependency",
        // Importers that are native tools
        "Stride.Asset.UpdateSelectedFromSource",
        "Stride.Asset.UpdateAllFromSource",
        // A browser tab is closed by the browser
        "Stride.File.Exit",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Whether a command of the editor needs the desktop Game Studio.
    /// </summary>
    public static bool Contains(string id) => Ids.Contains(id);
}
