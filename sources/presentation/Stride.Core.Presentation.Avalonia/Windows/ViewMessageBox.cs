// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MarkView.Avalonia;
using Stride.Core.Presentation.Services;
using Stride.Core.Presentation.Windows;

namespace Stride.Core.Presentation.Avalonia.Windows;

/// <summary>
/// The message boxes of <see cref="MessageBox"/>, <see cref="CheckedMessageBox"/> and <see cref="MultiCheckedMessageBox"/>
/// as a <see cref="ViewDialog"/>, where there are no windows.
/// </summary>
public static class ViewMessageBox
{
    /// <summary>
    /// Shows a message with buttons, and optionally check boxes whose <see cref="DialogCheckBoxInfo.IsChecked"/> follows
    /// the user's choices.
    /// </summary>
    /// <returns>The result of the button that closed the box, or of its cancel button when Escape did; 0 without one.</returns>
    public static async Task<int> ShowAsync(Visual owner, string caption, string message, IReadOnlyCollection<DialogButtonInfo> buttons,
        MessageBoxImage image = MessageBoxImage.None, IReadOnlyCollection<DialogCheckBoxInfo>? checkBoxes = null)
    {
        ArgumentNullException.ThrowIfNull(buttons);
        var result = buttons.FirstOrDefault(x => x.IsCancel)?.Result ?? 0;
        ViewDialog? dialog = null;

        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        DockPanel.SetDock(buttonPanel, Dock.Bottom);
        var content = new DockPanel { MinWidth = 320, MaxWidth = 720 };
        foreach (var info in buttons)
        {
            var button = new Button
            {
                Content = info.Content,
                // Escape closes the dialog with the result of the cancel button: only Enter needs a button
                IsDefault = info.IsDefault,
                Margin = new Thickness(4, 0),
                Padding = new Thickness(16, 4),
                MinWidth = 80,
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            button.Click += (_, _) => CloseWith(info.Result);
            buttonPanel.Children.Add(button);
            if (!string.IsNullOrEmpty(info.Key))
                content.KeyBindings.Add(new KeyBinding { Gesture = KeyGesture.Parse(info.Key), Command = new ResultCommand(() => CloseWith(info.Result)) });
        }
        content.Children.Add(buttonPanel);

        if (checkBoxes is { Count: > 0 })
        {
            var checkBoxPanel = new StackPanel { Margin = new Thickness(0, 12, 0, 0), Spacing = 4 };
            DockPanel.SetDock(checkBoxPanel, Dock.Bottom);
            foreach (var info in checkBoxes)
            {
                var checkBox = new CheckBox { DataContext = info };
                checkBox.Bind(ContentControl.ContentProperty, new Binding(nameof(DialogCheckBoxInfo.Content)));
                checkBox.Bind(CheckBox.IsCheckedProperty, new Binding(nameof(DialogCheckBoxInfo.IsChecked)) { Mode = BindingMode.TwoWay });
                checkBox.Bind(InputElement.IsEnabledProperty, new Binding(nameof(DialogCheckBoxInfo.IsEnabled)));
                checkBoxPanel.Children.Add(checkBox);
            }
            content.Children.Add(checkBoxPanel);
        }

        if (GetGeometry(image) is { } geometry)
        {
            var icon = new global::Avalonia.Controls.Shapes.Path
            {
                Data = geometry, Width = 32, Height = 32, Margin = new Thickness(0, 0, 12, 0), Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Top,
            };
            icon.Bind(global::Avalonia.Controls.Shapes.Shape.FillProperty, icon.GetResourceObservable("SystemAccentColor", x => x is Color color ? new SolidColorBrush(color) : null));
            DockPanel.SetDock(icon, Dock.Left);
            content.Children.Add(icon);
        }
        content.Children.Add(new MarkdownViewer { Markdown = message, VerticalAlignment = VerticalAlignment.Center });

        dialog = ViewDialog.Show(owner, content, caption, canCancel: buttons.Any(x => x.IsCancel));
        await dialog.Closed;
        return result;

        void CloseWith(int buttonResult)
        {
            result = buttonResult;
            dialog?.Close();
        }
    }

    private static Geometry? GetGeometry(MessageBoxImage image)
    {
        var key = image switch
        {
            MessageBoxImage.None => null,
            MessageBoxImage.Error => "GeometryError",
            MessageBoxImage.Question => "GeometryQuestion",
            MessageBoxImage.Warning => "GeometryWarning",
            MessageBoxImage.Information => "GeometryInformation",
            _ => throw new ArgumentOutOfRangeException(nameof(image), image, null),
        };
        if (key is null) return null;
        var resources = new global::Avalonia.Markup.Xaml.Styling.ResourceInclude(new Uri("avares://Stride.Core.Presentation.Avalonia/"))
        {
            Source = new Uri("avares://Stride.Core.Presentation.Avalonia/Windows/GeometryResources.axaml"),
        };
        return resources.TryGetResource(key, null, out var geometry) ? geometry as Geometry : null;
    }

    private sealed class ResultCommand(Action execute) : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => execute();
    }
}
