using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Chencho.Core;
using Localization = Chencho.Core.Localization;

namespace Chencho.Overlay;

/// The menu brushes for the system's light or dark app theme.
internal static class MenuPalette
{
    public static void Apply(ResourceDictionary resources)
    {
        var light = ReadsLight("AppsUseLightTheme");
        resources["Menu.Background"] = Theme.Brush(light ? Color.FromRgb(0xF9, 0xF9, 0xF9) : Color.FromRgb(0x2C, 0x2C, 0x2C));
        resources["Menu.Border"] = Theme.Brush(light ? Color.FromRgb(0xE0, 0xE0, 0xE0) : Color.FromRgb(0x40, 0x40, 0x40));
        resources["Menu.Text"] = Theme.Brush(light ? Color.FromRgb(0x1B, 0x1B, 0x1B) : Color.FromRgb(0xFF, 0xFF, 0xFF));
        resources["Menu.Secondary"] = Theme.Brush(light ? Color.FromRgb(0x6B, 0x6B, 0x6B) : Color.FromRgb(0xA8, 0xA8, 0xA8));
        resources["Menu.Hover"] = Theme.Brush(light ? Color.FromArgb(0x0F, 0, 0, 0) : Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        resources["Menu.Disabled"] = Theme.Brush(light ? Color.FromRgb(0xA0, 0xA0, 0xA0) : Color.FromRgb(0x78, 0x78, 0x78));
        resources["Menu.Font"] = Theme.MenuFont;
        resources["Menu.FontSize"] = Theme.MenuSize;
    }

    /// `name` under Personalize: AppsUseLightTheme for windows and menus, SystemUsesLightTheme for the taskbar.
    public static bool ReadsLight(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue(name) is int value && value != 0;
    }
}

internal static class Menus
{
    public static MenuItem Header(string title) => new()
    {
        Header = title,
        Style = (Style)Application.Current.FindResource("MenuHeader"),
    };

    public static MenuItem Action(string title, Action handler, object? icon = null, bool isChecked = false)
    {
        var item = new MenuItem { Header = title, Icon = icon, IsChecked = isChecked };
        item.Click += (_, _) => handler();
        return item;
    }

    public static MenuItem Disabled(string title) => new() { Header = title, IsEnabled = false };

    public static MenuItem Submenu(string title, IEnumerable<MenuItem> items, object? icon = null)
    {
        var item = new MenuItem { Header = title, Icon = icon };
        foreach (var child in items) item.Items.Add(child);
        return item;
    }
}

internal static class CreatureMenu
{
    /// `open` brings the session's terminal to the front, as a click on the creature does.
    public static ContextMenu Make(PanelSession session, Action<ClientMessage> send, Action open)
    {
        var id = session.Id;
        var icon = session.Icon;
        var menu = new ContextMenu();
        menu.Items.Add(Menus.Header(session.Name));
        menu.Items.Add(Menus.Action(Locale.T("Open", "Abrir"), open));
        var pin = session.Pinned ? Locale.T("Unpin", "Desanclar") : Locale.T("Pin", "Anclar");
        menu.Items.Add(Menus.Action(pin, () => send(new ClientMessage.Pin(id, !session.Pinned))));
        menu.Items.Add(new Separator());

        var characters = new List<MenuItem>();
        foreach (var character in CharacterTemplate.All)
        {
            characters.Add(Menus.Action(
                character.LabelIn(Locale.Current),
                () => send(new ClientMessage.Icon(id, new SessionIcon(character.Id, icon.Color))),
                Preview(character.Id, icon.Tint),
                character.Id == CharacterTemplate.Named(icon.Character).Id));
        }
        menu.Items.Add(Menus.Submenu(Locale.T("Character", "Personaje"), characters, Preview(icon.Character, icon.Tint)));

        var colors = new List<MenuItem>();
        foreach (var (name, color) in SessionIcon.Palette)
        {
            colors.Add(Menus.Action(
                Localization.ColorName(name, Locale.Current),
                () => send(new ClientMessage.Icon(id, new SessionIcon(icon.Character, color))),
                Swatch(color),
                color == icon.Color.ToLowerInvariant()));
        }
        menu.Items.Add(Menus.Submenu(Locale.T("Color", "Color"), colors, Swatch(icon.Color)));
        return menu;
    }

    private const double PreviewPointsPerPixel = 2;

    /// The character at rest, trimmed, at 2 points per pixel, centred in a square as tall as its template.
    private static FrameworkElement Preview(string character, Rgb color)
    {
        var pixels = Sprite.Trimmed(Sprite.Frame(character, color, Mood.Idle, 1));
        var side = CharacterTemplate.Named(character).Body.Length * PreviewPointsPerPixel;
        var image = new Image
        {
            Source = SpriteImages.Render(pixels),
            Width = pixels.Width * PreviewPointsPerPixel,
            Height = pixels.Height * PreviewPointsPerPixel,
            Stretch = Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        return new Grid { Width = side, Height = side, Children = { image } };
    }

    private static FrameworkElement Swatch(string color)
    {
        var rgb = Rgb.FromHex(color) ?? SessionIcon.Default.Tint;
        return new System.Windows.Shapes.Ellipse
        {
            Width = 12,
            Height = 12,
            Margin = new Thickness(1),
            Fill = Theme.Brush(Theme.Of(rgb)),
            Stroke = Theme.Brush(Color.FromArgb(64, 0, 0, 0)),
            StrokeThickness = 1,
        };
    }
}

internal static class LibraryMenu
{
    /// Claude Code's sessions, most recent first; choosing one pins it, or unpins it when it already is.
    public static ContextMenu Make(IReadOnlyList<LibraryEntry> entries, DateTimeOffset now, Action<ClientMessage> send)
    {
        // Up to a hundred sessions: past the screen's height the menu scrolls.
        var menu = new ContextMenu { MaxHeight = SystemParameters.WorkArea.Height };
        menu.Items.Add(Menus.Header(Locale.T("Pin a session", "Anclar una sesión")));
        if (entries.Count == 0) menu.Items.Add(Menus.Disabled(Locale.T("No Claude Code sessions", "No hay sesiones de Claude Code")));
        foreach (var entry in entries)
        {
            var title = new TextBlock();
            title.Inlines.Add(new Run(entry.Name));
            title.Inlines.Add(new Run($"  {Status.Ago(entry.LastActivityAt, now, Locale.Current)}") { Foreground = (Brush)Application.Current.FindResource("Menu.Secondary") });
            var item = new MenuItem { Header = title, IsChecked = entry.Pinned, ToolTip = entry.Cwd };
            item.Click += (_, _) => send(new ClientMessage.Pin(entry.Id, !entry.Pinned));
            menu.Items.Add(item);
        }
        return menu;
    }
}
