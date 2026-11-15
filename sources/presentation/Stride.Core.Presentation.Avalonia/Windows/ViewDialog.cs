// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Stride.Core.Presentation.Avalonia.Windows;

/// <summary>
/// A modal dialog drawn inside a view, for the platforms that have no windows (a browser tab, a phone).
/// </summary>
/// <remarks>
/// The dialog covers its top level with a backdrop that takes the input, and shows its content in the middle.
/// </remarks>
public sealed class ViewDialog
{
    private readonly TaskCompletionSource closed = new();
    private readonly OverlayLayer layer;
    private readonly Border backdrop;

    private ViewDialog(OverlayLayer layer, Control content, string? title, bool canCancel)
    {
        this.layer = layer;

        var panel = new DockPanel();
        if (!string.IsNullOrEmpty(title) || canCancel)
        {
            var titleBar = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            DockPanel.SetDock(titleBar, Dock.Top);
            if (canCancel)
            {
                var closeButton = new Button { Content = "×", FontSize = 18, Padding = new Thickness(8, 0), Background = Brushes.Transparent, BorderThickness = default };
                closeButton.Click += (_, _) => Close();
                DockPanel.SetDock(closeButton, Dock.Right);
                titleBar.Children.Add(closeButton);
            }
            titleBar.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(titleBar);
        }
        panel.Children.Add(content);

        var frame = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(24),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Child = panel,
        };
        frame.Bind(Border.BackgroundProperty, frame.GetResourceObservable("SystemRegionBrush"));
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("SystemControlForegroundBaseMediumLowBrush"));

        backdrop = new Border
        {
            Background = new SolidColorBrush(Colors.Black, 0.5),
            Focusable = true,
            Child = frame,
        };
        KeyboardNavigation.SetTabNavigation(backdrop, KeyboardNavigationMode.Cycle);
        if (canCancel)
        {
            backdrop.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Escape) return;
                e.Handled = true;
                Close();
            };
        }

        layer.SizeChanged += OnLayerSizeChanged;
        FitLayer();
        layer.Children.Add(backdrop);
        if (!content.Focus())
            backdrop.Focus();
    }

    /// <summary>
    /// The main view of an application that has no windows, or null in one that has them.
    /// </summary>
    public static Control? MainView => (Application.Current?.ApplicationLifetime as ISingleViewApplicationLifetime)?.MainView;

    /// <summary>
    /// Completes when the dialog is closed.
    /// </summary>
    public Task Closed => closed.Task;

    /// <summary>
    /// Shows a dialog over the top level of a visual.
    /// </summary>
    /// <param name="owner">A visual of the top level to cover.</param>
    /// <param name="content">What the dialog shows.</param>
    /// <param name="title">The title, or null for none.</param>
    /// <param name="canCancel">Whether Escape and a close button close the dialog.</param>
    public static ViewDialog Show(Visual owner, Control content, string? title = null, bool canCancel = true)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(content);
        var layer = OverlayLayer.GetOverlayLayer(owner)
            ?? throw new InvalidOperationException("The owner of the dialog is not in a top level.");
        return new ViewDialog(layer, content, title, canCancel);
    }

    /// <summary>
    /// Closes the dialog; does nothing when it is closed already.
    /// </summary>
    public void Close()
    {
        if (!closed.TrySetResult()) return;
        layer.SizeChanged -= OnLayerSizeChanged;
        layer.Children.Remove(backdrop);
    }

    private void OnLayerSizeChanged(object? sender, SizeChangedEventArgs e) => FitLayer();

    // The overlay layer is a canvas: it does not stretch its children
    private void FitLayer()
    {
        backdrop.Width = layer.Bounds.Width;
        backdrop.Height = layer.Bounds.Height;
    }
}
