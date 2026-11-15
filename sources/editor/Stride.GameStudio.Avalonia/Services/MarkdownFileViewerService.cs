// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using Stride.Core.Presentation.Avalonia.Windows;
using Stride.GameStudio.Avalonia.Views;

namespace Stride.GameStudio.Avalonia.Services;

public static class MarkdownFileViewerService
{
    private static Window? MainWindow =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public static async Task ShowFileAsync(string filePath, string title = "Markdown Viewer")
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    await ShowErrorAsync($"We are facing some technical challenges to fetch the content: {filePath}", title);
                    return;
                }

                string markdownContent = await File.ReadAllTextAsync(filePath);

                if (ViewDialog.MainView is { } view)
                {
                    var viewer = new ScrollViewer { MaxWidth = 800, MaxHeight = 600, Content = new MarkView.Avalonia.MarkdownViewer { Markdown = markdownContent } };
                    await ViewDialog.Show(view, viewer, title).Closed;
                    return;
                }

                var window = new MarkdownViewerWindow(markdownContent, title);

                if (MainWindow != null)
                {
                    await window.ShowDialog(MainWindow);
                }
                else
                {
                    window.Show(); // fallback if MainWindow is unavailable
                }
            }
            catch (Exception ex)
            {
                await ShowErrorAsync($"Failed to open markdown file:\n{ex.Message}", title);
            }
        });
    }

    private static async Task ShowErrorAsync(string message, string title)
    {
        if (ViewDialog.MainView is { } view)
        {
            await ViewDialog.Show(view, new TextBlock { Text = message, MaxWidth = 400, TextWrapping = TextWrapping.Wrap }, title).Closed;
            return;
        }

        var errorWindow = new Window
        {
            Title = title,
            Width = 400,
            Height = 200,
            Content = new TextBlock
            {
                Text = message,
                Margin = new Thickness(20),
                TextWrapping = TextWrapping.Wrap
            },
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };

        if (MainWindow != null)
        {
            await errorWindow.ShowDialog(MainWindow);
        }
        else
        {
            errorWindow.Show();
        }
    }
}
