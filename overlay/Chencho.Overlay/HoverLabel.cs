using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Chencho.Core;
using CoreRect = Chencho.Core.Rect;
using CoreSize = Chencho.Core.Size;

namespace Chencho.Overlay;

internal sealed class HoverLabel
{
    /// Rects are in screen DIPs.
    public sealed record Content(string Name, string Status, Color StatusColor, System.Windows.Rect FrameBeside, System.Windows.Rect Outside, ScreenEdge Edge);

    private readonly OverlayWindow panel = new(clickThrough: true);
    private readonly Canvas canvas = new();
    private readonly LayerBox box = new();
    private readonly TextBlock name = Field(Theme.NameFont, Theme.NameSize, FontWeights.SemiBold);
    private readonly TextBlock status = Field(Theme.StatusFont, Theme.StatusSize, FontWeights.Normal);
    private Content? shown;
    private const double MaxTextWidth = 300;
    private const double PadX = 10;
    private const double PadY = 6;
    private const double LineGap = 2;

    public HoverLabel()
    {
        box.Paint(Theme.Backdrop, Theme.Line, 1);
        box.Shape(8, Corners.All);
        name.Foreground = Theme.Brush(Theme.Text);
        canvas.Children.Add(box);
        canvas.Children.Add(name);
        canvas.Children.Add(status);
        panel.Content = canvas;
    }

    private static TextBlock Field(FontFamily font, double size, FontWeight weight) => new()
    {
        FontFamily = font,
        FontSize = size,
        FontWeight = weight,
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextWrapping = TextWrapping.NoWrap,
        IsHitTestVisible = false,
    };

    public void Show(Content content)
    {
        if (content == shown) return;
        shown = content;
        name.Text = content.Name;
        status.Text = content.Status;
        status.Foreground = Theme.Brush(content.StatusColor);
        var alignment = content.Edge == ScreenEdge.Right ? TextAlignment.Right : TextAlignment.Left;
        name.TextAlignment = alignment;
        status.TextAlignment = alignment;

        var nameSize = Fitting(name);
        var statusSize = Fitting(status);
        var width = Math.Min(Math.Ceiling(Math.Max(nameSize.Width, statusSize.Width)), MaxTextWidth);
        // Name on top, status below.
        Place(name, PadX, PadY, width, nameSize.Height);
        Place(status, PadX, PadY + nameSize.Height + LineGap, width, statusSize.Height);
        var size = new System.Windows.Size(width + 2 * PadX, PadY + nameSize.Height + LineGap + statusSize.Height + PadY);
        Place(box, 0, 0, size.Width, size.Height);
        canvas.Width = size.Width;
        canvas.Height = size.Height;

        var frame = StripLayout.LabelFrame(
            new CoreSize(size.Width, size.Height),
            new CoreRect(content.FrameBeside.X, content.FrameBeside.Y, content.FrameBeside.Width, content.FrameBeside.Height),
            new CoreRect(content.Outside.X, content.Outside.Y, content.Outside.Width, content.Outside.Height),
            content.Edge);
        // The frame is centred on the item vertically, so it reads the same with a top-left origin.
        panel.Left = frame.X;
        panel.Top = frame.Y;
        panel.Width = frame.Width;
        panel.Height = frame.Height;
        panel.BringToFront();
    }

    public void Hide()
    {
        shown = null;
        panel.Hide();
    }

    private static System.Windows.Size Fitting(TextBlock field)
    {
        field.Width = double.NaN;
        field.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        return field.DesiredSize;
    }

    private static void Place(FrameworkElement element, double x, double y, double width, double height)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        element.Width = width;
        element.Height = height;
    }
}
