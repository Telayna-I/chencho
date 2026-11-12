using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Chencho.Overlay;

/// A borderless, transparent, always-on-top window that never takes focus from the app in use and stays
/// out of the taskbar and Alt-Tab.
internal class OverlayWindow : Window
{
    private readonly bool clickThrough;

    public OverlayWindow(bool clickThrough = false)
    {
        this.clickThrough = clickThrough;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        SnapsToDevicePixels = false;
        UseLayoutRounding = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
    }

    public IntPtr Handle => new WindowInteropHelper(this).EnsureHandle();

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var add = Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
        if (clickThrough) add |= Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED;
        Native.AddExtendedStyle(Handle, add, Native.WS_EX_APPWINDOW);
        HwndSource.FromHwnd(Handle)!.AddHook((IntPtr _, int message, IntPtr _, IntPtr _, ref bool handled) =>
        {
            if (message != Native.WM_MOUSEACTIVATE) return IntPtr.Zero;
            handled = true;
            return new IntPtr(Native.MA_NOACTIVATE);
        });
    }

    /// Shows the window without activating it and keeps it above later topmost windows.
    public void BringToFront()
    {
        if (!IsVisible) Show();
        Native.RaiseTopmost(Handle);
    }
}
