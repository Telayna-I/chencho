using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chencho.Core;
using Size = System.Windows.Size;
using Rect = System.Windows.Rect;

namespace Chencho.Overlay;

/// Development aid: renders the README's eight states (images/creatures.gif) with the overlay's own
/// drawing code, one PNG per animation frame, so the two can be compared side by side.
internal static class Gallery
{
    /// The README's capture: a screenshot of the strip at 200% display scaling, so 4 pixels per sprite pixel.
    private const double Scale = 4;
    private const double Column = 122;
    private const double FirstTile = 22;
    private const int Frames = 48; // the least common multiple of every animation cycle

    public static int Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Program.Load(app);
        var now = DateTimeOffset.Now;
        var states = new (string Label, PanelSession Session)[]
        {
            ("working", Session("default", "#d97757", SessionState.Working, now)),
            ("needs permission", Session("robot", "#d97757", SessionState.NeedsPermission, now)),
            ("asking you", Session("ghost", "#4f7cff", SessionState.NeedsInput, now)),
            ("compacting", Session("cat", "#a66cff", SessionState.Compacting, now)),
            ("done", Session("default", "#4cd964", SessionState.Done, now)),
            ("asleep", Session("robot", "#3ccfe6", SessionState.Done, now.AddMinutes(-10))),
            ("error", Session("ghost", "#d97757", SessionState.Error, now)),
            ("closed", Session("cat", "#d97757", SessionState.Ended, now, live: false)),
        };

        var cell = 28 * Scale;
        for (var frame = 0; frame < Frames; frame++)
        {
            var canvas = new Canvas { Width = 1002, Height = 178, Background = Theme.Brush(Color.FromRgb(0x12, 0x12, 0x14)) };
            for (var i = 0; i < states.Length; i++)
            {
                var (label, session) = states[i];
                var appearance = CreatureAppearance.For(session, focused: false, hovered: false, frame, now);
                var left = FirstTile - Scale + i * Column;
                var tile = new LayerBox();
                tile.Shape(3 * Scale, Corners.All);
                var t = appearance.Tile;
                tile.Paint(t.Fill is { } fill ? Theme.Of(fill) : null, t.Border is { } border ? Theme.Of(border) : null, t.Border is null ? 0 : t.BorderWidth * Scale / 2);
                CellLayers.Position(tile, new Rect(left + Scale, FirstTile, cell - 2 * Scale, cell - 2 * Scale));
                var sprite = new Image { Source = SpriteImages.Render(appearance.Pixels), Stretch = Stretch.Fill, Opacity = appearance.Opacity };
                RenderOptions.SetBitmapScalingMode(sprite, BitmapScalingMode.NearestNeighbor);
                CellLayers.Position(sprite, new Rect(left + 2 * Scale, FirstTile + Scale, 24 * Scale, 24 * Scale));
                var caption = new TextBlock
                {
                    Text = label,
                    Width = Column,
                    TextAlignment = TextAlignment.Center,
                    Foreground = Theme.Brush(Color.FromRgb(0xC8, 0xC8, 0xC8)),
                    FontFamily = Theme.NameFont,
                    FontSize = 13,
                };
                Canvas.SetLeft(caption, left + cell / 2 - Column / 2);
                Canvas.SetTop(caption, 136);
                canvas.Children.Add(tile);
                canvas.Children.Add(sprite);
                canvas.Children.Add(caption);
            }
            canvas.Measure(new Size(canvas.Width, canvas.Height));
            canvas.Arrange(new Rect(0, 0, canvas.Width, canvas.Height));
            canvas.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)canvas.Width, (int)canvas.Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(canvas);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(directory, $"frame-{frame:00}.png"));
            encoder.Save(file);
        }
        foreach (var (waiting, light) in new[] { (0, false), (3, false), (12, false), (0, true), (2, true) })
        {
            using var icon = TrayIcon.Draw(waiting, light);
            using var bitmap = icon.ToBitmap();
            using var large = new System.Drawing.Bitmap(bitmap.Width * 8, bitmap.Height * 8);
            using (var g = System.Drawing.Graphics.FromImage(large))
            {
                g.Clear(light ? System.Drawing.Color.FromArgb(243, 243, 243) : System.Drawing.Color.FromArgb(32, 32, 32));
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.DrawImage(bitmap, 0, 0, large.Width, large.Height);
            }
            large.Save(Path.Combine(directory, $"tray-{waiting}-{(light ? "light" : "dark")}.png"));
        }
        Console.WriteLine($"wrote {Frames} frames to {Path.GetFullPath(directory)}");
        return 0;
    }

    private static PanelSession Session(string character, string color, SessionState state, DateTimeOffset since, bool live = true) =>
        new(
            Id: Guid.NewGuid().ToString(),
            Name: character,
            Cwd: "C:\\",
            State: state,
            Activity: null,
            Live: live,
            StateSince: since,
            Pinned: true,
            Position: 0,
            Icon: new SessionIcon(character, color),
            Pid: null);
}
