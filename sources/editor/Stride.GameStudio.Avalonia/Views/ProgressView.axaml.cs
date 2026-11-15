// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia.Controls;
using Avalonia.Interactivity;
using Stride.Core.Presentation.Avalonia.Windows;

namespace Stride.GameStudio.Avalonia.Views;

/// <summary>
/// What the progress dialog shows: the progress of a work, its log, and the buttons that cancel it or close the dialog.
/// </summary>
internal sealed partial class ProgressView : UserControl
{
    public ProgressView()
    {
        InitializeComponent();
    }

    private void OnClick_Close(object? sender, RoutedEventArgs args)
    {
        ViewDialog.Close(this);
    }
}
