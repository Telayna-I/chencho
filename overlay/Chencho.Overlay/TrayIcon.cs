using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Chencho.Core;
using Forms = System.Windows.Forms;

namespace Chencho.Overlay;

/// The notification-area icon. Windows shows no text beside the icon, so the count of sessions waiting
/// for you becomes a pixel badge on it, and the tooltip says it too.
internal sealed class TrayIcon
{
    private readonly AgentLink link;
    private readonly StripController strip;
    private readonly Forms.NotifyIcon icon = new() { Text = "Chencho", Visible = false };
    private int shownWaiting = -1;
    private Language shownLanguage;
    private bool shownLight;

    public TrayIcon(AgentLink link, StripController strip)
    {
        this.link = link;
        this.strip = strip;
        icon.MouseUp += (_, e) =>
        {
            if (e.Button is Forms.MouseButtons.Left or Forms.MouseButtons.Right) ShowMenu();
        };
        link.Changed += Update;
        Locale.Changed += Update;
        SystemEvents.UserPreferenceChanged += (_, _) => Application.Current.Dispatcher.BeginInvoke(() =>
        {
            MenuPalette.Apply(Application.Current.Resources);
            shownWaiting = -1;
            Update();
        });
        Update();
        icon.Visible = true;
        PromoteOnce();
    }

    /// Windows 11 tucks new icons into the overflow, out of sight. So the first time Explorer lists
    /// Chencho's icon, before the user chose either way, it's pinned to the taskbar.
    private static void PromoteOnce()
    {
        var path = Environment.ProcessPath;
        if (path is null) return;
        var attempts = 0;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            if (++attempts > 20 || TryPromote(path)) timer.Stop();
        };
        timer.Start();
    }

    private static bool TryPromote(string path)
    {
        try
        {
            using var settings = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings");
            if (settings is null) return true;
            foreach (var name in settings.GetSubKeyNames())
            {
                using var entry = settings.OpenSubKey(name, writable: true);
                if (entry?.GetValue("ExecutablePath") is not string executable
                    || !string.Equals(executable, path, StringComparison.OrdinalIgnoreCase)) continue;
                if (entry.GetValue("IsPromoted") is null) entry.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                return true;
            }
        }
        catch (Exception error)
        {
            Log.Write($"could not show the notification icon: {error.Message}");
            return true;
        }
        return false; // Explorer hasn't listed the icon yet
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
    }

    private void Update()
    {
        var waiting = link.List.Shown(DateTimeOffset.Now).Count(s => s.NeedsAttention);
        var light = MenuPalette.ReadsLight("SystemUsesLightTheme");
        if (waiting == shownWaiting && light == shownLight && Locale.Current == shownLanguage) return;
        shownWaiting = waiting;
        shownLight = light;
        shownLanguage = Locale.Current;
        var previous = icon.Icon;
        icon.Icon = Draw(waiting, light);
        if (previous is not null) DestroyIcon(previous.Handle);
        icon.Text = waiting switch
        {
            0 => "Chencho",
            1 => Locale.T("Chencho · 1 needs you", "Chencho · 1 te necesita"),
            _ => Locale.T($"Chencho · {waiting} need you", $"Chencho · {waiting} te necesitan"),
        };
    }

    private void ShowMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Menus.Disabled(StatusText));
        menu.Items.Add(new Separator());
        var toggle = strip.Hidden ? Locale.T("Show Strip", "Mostrar franja") : Locale.T("Hide Strip", "Ocultar franja");
        menu.Items.Add(Menus.Action(toggle, () => strip.Hidden = !strip.Hidden));
        menu.Items.Add(Menus.Submenu(Locale.T("Edge", "Borde"), new[]
        {
            Menus.Action(Locale.T("Left", "Izquierdo"), () => strip.Edge = ScreenEdge.Left, isChecked: strip.Edge == ScreenEdge.Left),
            Menus.Action(Locale.T("Right", "Derecho"), () => strip.Edge = ScreenEdge.Right, isChecked: strip.Edge == ScreenEdge.Right),
        }));
        // Each language is named in itself, so either can be found from the other.
        menu.Items.Add(Menus.Submenu(Locale.T("Language", "Idioma"), new[]
        {
            Menus.Action(Locale.T("Automatic", "Automático"), () => Locale.Preference = "auto", isChecked: Locale.Preference == "auto"),
            Menus.Action("English", () => Locale.Preference = "en", isChecked: Locale.Preference == "en"),
            Menus.Action("Español", () => Locale.Preference = "es", isChecked: Locale.Preference == "es"),
        }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Menus.Action(Locale.T("Quit Chencho", "Salir de Chencho"), Quit));
        MenuHost.Show(menu, null, System.Windows.Controls.Primitives.PlacementMode.MousePoint);
    }

    private string StatusText
    {
        get
        {
            var count = link.List.Shown(DateTimeOffset.Now).Count;
            return link.Status switch
            {
                AgentLink.LinkStatus.Online => count == 1
                    ? Locale.T("Connected · 1 session", "Conectado · 1 sesión")
                    : Locale.T($"Connected · {count} sessions", $"Conectado · {count} sesiones"),
                AgentLink.LinkStatus.Connecting => Locale.T("Connecting to the agent…", "Conectando con el agente…"),
                AgentLink.LinkStatus.Offline => Locale.T("Agent not running · retrying", "El agente no está corriendo · reintentando"),
                _ => Locale.T("Agent not set up · retrying", "El agente no está instalado · reintentando"),
            };
        }
    }

    private void Quit()
    {
        Dispose();
        Application.Current.Shutdown();
    }

    /// The default character's silhouette with its eyes punched out, plus a badge.
    internal static Icon Draw(int waiting, bool lightTaskbar)
    {
        const int grid = 16;
        var size = Forms.SystemInformation.SmallIconSize.Width;
        var scale = Math.Max(1, size / grid);
        using var bitmap = new Bitmap(grid * scale, grid * scale, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var ink = lightTaskbar ? System.Drawing.Color.Black : System.Drawing.Color.White;

        void Fill(int x, int y, System.Drawing.Color color)
        {
            for (var dy = 0; dy < scale; dy++)
                for (var dx = 0; dx < scale; dx++)
                    bitmap.SetPixel(x * scale + dx, y * scale + dy, color);
        }

        var character = CharacterTemplate.Named("default");
        var eyes = character.Eyes;
        bool IsEye(int x, int y) =>
            (y == eyes.Y || y == eyes.Y + 1) && (x == eyes.Left || x == eyes.Left + 1 || x == eyes.Right || x == eyes.Right + 1);
        for (var y = 0; y < character.Body.Length; y++)
            for (var x = 0; x < character.Body[y].Length; x++)
                if (character.Body[y][x] != '.' && !IsEye(x, y)) Fill(x, y, ink);

        if (waiting > 0)
        {
            var accent = System.Drawing.Color.FromArgb(Paint.Accent.Rgb.R, Paint.Accent.Rgb.G, Paint.Accent.Rgb.B);
            for (var y = 9; y < 16; y++)
                for (var x = 9; x < 16; x++)
                    if (!((x == 9 || x == 15) && (y == 9 || y == 15))) Fill(x, y, accent);
            var glyph = waiting > 9 ? Digits[10] : Digits[waiting];
            for (var row = 0; row < 5; row++)
                for (var column = 0; column < 3; column++)
                    if (glyph[row][column] == 'X') Fill(11 + column, 10 + row, System.Drawing.Color.White);
        }
        return System.Drawing.Icon.FromHandle(bitmap.GetHicon());
    }

    /// A 3×5 pixel font: 0–9, then "+" for more than nine.
    private static readonly string[][] Digits =
    {
        new[] { "XXX", "X.X", "X.X", "X.X", "XXX" },
        new[] { ".X.", "XX.", ".X.", ".X.", "XXX" },
        new[] { "XXX", "..X", "XXX", "X..", "XXX" },
        new[] { "XXX", "..X", "XXX", "..X", "XXX" },
        new[] { "X.X", "X.X", "XXX", "..X", "..X" },
        new[] { "XXX", "X..", "XXX", "..X", "XXX" },
        new[] { "XXX", "X..", "XXX", "X.X", "XXX" },
        new[] { "XXX", "..X", "..X", "..X", "..X" },
        new[] { "XXX", "X.X", "XXX", "X.X", "XXX" },
        new[] { "XXX", "X.X", "XXX", "..X", "XXX" },
        new[] { "...", ".X.", "XXX", ".X.", "..." },
    };

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

}
