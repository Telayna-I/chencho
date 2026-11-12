using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace Chencho.Overlay;

/// Opens the context menus of the strip and the notification icon. A menu from a window that never takes the
/// foreground doesn't close on a click elsewhere, so while one is open an off-screen window of Chencho's holds the
/// foreground. A click anywhere else takes it away, which closes the menu; when nothing else took it, the app
/// that had it gets it back.
internal static class MenuHost
{
    private static readonly AnchorWindow Anchor = new();
    private static ContextMenu? open;

    static MenuHost()
    {
        Anchor.Deactivated += (_, _) => Close();
    }

    public static IntPtr AnchorHandle => Anchor.Handle;

    /// `target` places the menu; without one, the menu belongs to the anchor (from the notification icon).
    public static void Show(ContextMenu menu, UIElement? target, PlacementMode placement, double x = 0, double y = 0)
    {
        Close();
        var previous = Native.GetForegroundWindow();
        if (!Anchor.IsVisible) Anchor.Show();
        menu.PlacementTarget = target ?? Anchor;
        menu.Placement = placement;
        menu.HorizontalOffset = x;
        menu.VerticalOffset = y;
        menu.Closed += (_, _) =>
        {
            if (open == menu) open = null;
            if (Native.GetForegroundWindow() == Anchor.Handle && previous != IntPtr.Zero && previous != Anchor.Handle)
                Native.SetForegroundWindow(previous);
        };
        Anchor.Activate();
        open = menu;
        menu.IsOpen = true;
    }

    private static void Close()
    {
        if (open is not { } menu) return;
        open = null;
        menu.IsOpen = false;
    }

    private sealed class AnchorWindow : Window
    {
        public AnchorWindow()
        {
            WindowStyle = WindowStyle.None;
            ShowInTaskbar = false;
            ShowActivated = false;
            Width = 1;
            Height = 1;
            Left = -32000;
            Top = -32000;
        }

        public IntPtr Handle => new WindowInteropHelper(this).EnsureHandle();

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            Native.AddExtendedStyle(Handle, Native.WS_EX_TOOLWINDOW); // never in Alt-Tab
        }
    }
}
