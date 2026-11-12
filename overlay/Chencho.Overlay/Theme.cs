using System.Windows.Media;
using Chencho.Core;

namespace Chencho.Overlay;

/// The overlay's colors and fonts, in one place so they're easy to swap.
internal static class Theme
{
    public static readonly Color Backdrop = Color.FromArgb(199, 13, 13, 13);
    public static readonly Color Line = Color.FromArgb(26, 255, 255, 255);
    public static readonly Color Text = Color.FromRgb(245, 245, 245);
    public static readonly Color Muted = Color.FromRgb(153, 153, 153);

    /// The hover label's session name.
    public static readonly FontFamily NameFont = new("Segoe UI Variable Text, Segoe UI");
    public const double NameSize = 12;
    /// The hover label's status.
    public static readonly FontFamily StatusFont = new("Cascadia Mono, Consolas");
    public const double StatusSize = 11;
    /// Menus follow Windows' own font.
    public static readonly FontFamily MenuFont = new("Segoe UI Variable Text, Segoe UI");
    public const double MenuSize = 12;

    public static Color Of(Paint paint) =>
        Color.FromArgb((byte)System.Math.Round(paint.Alpha * 255), paint.Rgb.R, paint.Rgb.G, paint.Rgb.B);

    public static Color Of(Rgb rgb) => Color.FromRgb(rgb.R, rgb.G, rgb.B);

    public static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
