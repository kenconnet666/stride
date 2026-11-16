// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stride.Core.Presentation.Avalonia.Windows;
using Stride.Core.Presentation.Windows;

namespace Stride.Core.Presentation.Avalonia.Tests.Windows;

/// <summary>
/// The message boxes drawn inside a view, for the platforms without windows: they cover the top level, answer with the
/// button used and close.
/// </summary>
public sealed class ViewMessageBoxTests
{
    private static readonly DialogButtonInfo[] Buttons =
    [
        new() { Content = "Reload", Result = 1, IsDefault = true },
        new() { Content = "Don't reload", Result = 2, IsCancel = true },
    ];

    [AvaloniaFact]
    public async Task AButtonClosesTheBoxWithItsResult()
    {
        var window = ShowHost();
        try
        {
            var result = ViewMessageBox.ShowAsync(window, "Game Studio", "Some files have been modified.", Buttons);
            Dispatcher.UIThread.RunJobs();

            var layer = OverlayLayer.GetOverlayLayer(window)!;
            var backdrop = Assert.Single(layer.Children);
            Assert.Equal(window.ClientSize.Width, backdrop.Bounds.Width);
            var button = layer.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "Reload"));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(1, await result);
            Assert.Empty(layer.Children);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task EscapeAnswersWithTheCancelButton()
    {
        var window = ShowHost();
        try
        {
            var result = ViewMessageBox.ShowAsync(window, "Game Studio", "Some files have been modified.", Buttons);
            Dispatcher.UIThread.RunJobs();

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

            Assert.Equal(2, await result);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task TheCheckBoxesGiveTheUsersChoices()
    {
        var window = ShowHost();
        try
        {
            var backup = new DialogCheckBoxInfo { Content = "Back up the files", IsChecked = false };
            var result = ViewMessageBox.ShowAsync(window, "Game Studio", "Upgrade the project?", Buttons, checkBoxes: [backup]);
            Dispatcher.UIThread.RunJobs();

            var layer = OverlayLayer.GetOverlayLayer(window)!;
            var checkBox = Assert.Single(layer.GetVisualDescendants().OfType<CheckBox>());
            Assert.Equal("Back up the files", checkBox.Content);
            checkBox.IsChecked = true;
            layer.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "Reload")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(1, await result);
            Assert.True(backup.IsChecked);
        }
        finally
        {
            window.Close();
        }
    }

    private static Window ShowHost()
    {
        var window = new Window { Width = 800, Height = 600, Content = new Border() };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }
}
