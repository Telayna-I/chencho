using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Chencho.Core;
using CoreRect = Chencho.Core.Rect;
using CorePoint = Chencho.Core.Point;
using Size = System.Windows.Size;

namespace Chencho.Overlay;

/// The strip's window. Changes only the elements whose appearance differs from the last frame, so an
/// unchanged strip costs WPF nothing.
///
/// `StripLayout` uses bottom-left coordinates (y up); this view flips them into WPF's top-left ones.
internal sealed class StripView : OverlayWindow
{
    public Action<StripHit?> OnHover = _ => { };
    public Action<StripHit> OnClick = _ => { };
    public Func<int, ContextMenu?> MenuFor = _ => null;

    private StripLayout layout = StripLayout.Fitting(0, 0);
    private ScreenEdge edge = ScreenEdge.Right;
    private bool collapsed;
    private Size bounds;
    private readonly Canvas root = new() { Background = Brushes.Transparent, ClipToBounds = true };
    /// Holds the strip and its tab, and slides toward the edge to collapse them; the root clips it.
    private readonly Canvas drawer = new();
    private readonly TranslateTransform slide = new();
    private readonly LayerBox backdrop = new();
    private readonly LayerBox tabBackdrop = new();
    private readonly CellLayers tab;
    private readonly List<CellLayers> creatures = new();
    private StripHit? pressed;
    private double slideTarget = double.NaN;
    private static readonly Duration SlideDuration = new(TimeSpan.FromSeconds(0.25));

    public StripView()
    {
        foreach (var panel in new[] { backdrop, tabBackdrop })
        {
            panel.Paint(Theme.Backdrop, Theme.Line, 1);
            drawer.Children.Add(panel);
        }
        tab = new CellLayers(drawer);
        drawer.RenderTransform = slide;
        root.Children.Add(drawer);
        Content = root;

        root.MouseMove += (_, e) => OnHover(Hit(e));
        root.MouseLeave += (_, _) => { if (!root.IsMouseCaptured) OnHover(null); };
        root.MouseLeftButtonDown += (_, e) =>
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { ShowMenu(e); return; }
            pressed = Hit(e);
            root.CaptureMouse();
        };
        root.MouseLeftButtonUp += (_, e) =>
        {
            var hit = Hit(e);
            var down = pressed;
            pressed = null;
            root.ReleaseMouseCapture();
            if (hit is not null && hit == down) OnClick(hit);
        };
        root.MouseRightButtonDown += (_, e) => ShowMenu(e);
    }

    public void Arrange(StripLayout layout, ScreenEdge edge, bool collapsed, Size bounds)
    {
        this.layout = layout;
        this.edge = edge;
        this.collapsed = collapsed;
        this.bounds = bounds;
        root.Width = bounds.Width;
        root.Height = bounds.Height;
        drawer.Width = bounds.Width;
        drawer.Height = bounds.Height;
        // A slide in progress keeps going when the strip is rearranged.
        if (slideTarget != DrawerOffset)
        {
            slide.BeginAnimation(TranslateTransform.XProperty, null);
            slide.X = DrawerOffset;
            slideTarget = DrawerOffset;
        }

        var inner = edge == ScreenEdge.Right ? Corners.TopLeft | Corners.BottomLeft : Corners.TopRight | Corners.BottomRight;
        var frame = new CoreRect(0, 0, bounds.Width, bounds.Height);
        var strip = layout.StripRect(frame, edge);
        Place(backdrop, strip);
        backdrop.Shape(10, inner);

        var tabRect = layout.TabRect(frame, edge, false);
        Place(tabBackdrop, tabRect);
        tabBackdrop.Shape(3 * layout.Scale, inner);
        tab.Place(Flip(tabRect), Flip(tabRect), 3 * layout.Scale, inner);

        while (creatures.Count < layout.Count) creatures.Add(new CellLayers(drawer));
        while (creatures.Count > layout.Count)
        {
            creatures[^1].Remove();
            creatures.RemoveAt(creatures.Count - 1);
        }
        for (var index = 0; index < creatures.Count; index++)
        {
            var cell = layout.CellFrame(index, strip);
            creatures[index].Place(Flip(layout.TileRect(cell)), Flip(layout.SpriteRect(cell)), 3 * layout.Scale, Corners.All);
        }
    }

    /// Slides the strip past the edge, leaving its tab, or back.
    public void Slide(bool collapsed)
    {
        this.collapsed = collapsed;
        slideTarget = DrawerOffset;
        var curve = collapsed ? new KeySpline(0.42, 0, 1, 1) : new KeySpline(0, 0, 0.58, 1);
        var animation = new DoubleAnimationUsingKeyFrames { Duration = SlideDuration, FillBehavior = FillBehavior.HoldEnd };
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(DrawerOffset, KeyTime.FromTimeSpan(SlideDuration.TimeSpan), curve));
        slide.BeginAnimation(TranslateTransform.XProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private double DrawerOffset => collapsed ? layout.CollapseOffset(edge) : 0;

    /// Opens `menu` with its top-left corner on cell `index`'s, outside any mouse event.
    public void PopUp(ContextMenu menu, int index)
    {
        var cell = Flip(layout.CellFrame(index, layout.StripRect(new CoreRect(0, 0, bounds.Width, bounds.Height), edge)));
        MenuHost.Show(menu, root, System.Windows.Controls.Primitives.PlacementMode.Relative, cell.X, cell.Y);
    }

    public void Show(IReadOnlyList<CreatureAppearance> appearances)
    {
        for (var i = 0; i < Math.Min(creatures.Count, appearances.Count); i++) creatures[i].Show(appearances[i]);
    }

    public void ShowTab(CreatureAppearance appearance) => tab.Show(appearance);

    /// The cell's rect in screen DIPs, for the hover label.
    public System.Windows.Rect OnScreen(CoreRect local) {
        var flipped = Flip(local);
        return new System.Windows.Rect(Left + flipped.X, Top + flipped.Y, flipped.Width, flipped.Height);
    }

    private void ShowMenu(MouseButtonEventArgs e)
    {
        if (Hit(e) is not StripHit.Cell cell || MenuFor(cell.Index) is not { } menu) return;
        MenuHost.Show(menu, root, System.Windows.Controls.Primitives.PlacementMode.MousePoint);
        e.Handled = true;
    }

    private StripHit? Hit(MouseEventArgs e)
    {
        var p = e.GetPosition(root);
        return layout.Hit(new CorePoint(p.X, bounds.Height - p.Y), new CoreRect(0, 0, bounds.Width, bounds.Height), edge, collapsed);
    }

    private System.Windows.Rect Flip(CoreRect r) => new(r.X, bounds.Height - r.Y - r.Height, r.Width, r.Height);

    private void Place(FrameworkElement element, CoreRect rect) => CellLayers.Position(element, Flip(rect));

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource.FromHwnd(Handle)!.AddHook(WndProc);
    }

    public event Action? ScreenChanged;

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (message)
        {
            case Native.WM_SETTINGCHANGE when wParam.ToInt32() == Native.SPI_SETWORKAREA:
            case Native.WM_DISPLAYCHANGE:
            case Native.WM_DPICHANGED:
                Dispatcher.BeginInvoke(() => ScreenChanged?.Invoke());
                break;
        }
        return IntPtr.Zero;
    }
}

/// A tile with a sprite on it: a creature, the add button or the tab.
internal sealed class CellLayers
{
    private readonly Canvas parent;
    private readonly LayerBox tile = new();
    private readonly System.Windows.Controls.Image sprite = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private CreatureAppearance? shown;

    public CellLayers(Canvas parent)
    {
        this.parent = parent;
        RenderOptions.SetBitmapScalingMode(sprite, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(sprite, EdgeMode.Aliased);
        parent.Children.Add(tile);
        parent.Children.Add(sprite);
    }

    public void Place(System.Windows.Rect tileFrame, System.Windows.Rect spriteFrame, double cornerRadius, Corners corners)
    {
        Position(tile, tileFrame);
        tile.Shape(cornerRadius, corners);
        Position(sprite, spriteFrame);
    }

    public void Show(CreatureAppearance appearance)
    {
        if (appearance == shown) return;
        if (shown is null || appearance.Pixels != shown.Pixels) sprite.Source = SpriteImages.For(appearance.Pixels);
        if (shown is null || appearance.Tile != shown.Tile)
        {
            var t = appearance.Tile;
            tile.Paint(t.Fill is { } fill ? Theme.Of(fill) : null, t.Border is { } border ? Theme.Of(border) : null, t.Border is null ? 0 : t.BorderWidth);
        }
        if (shown is null || appearance.Opacity != shown.Opacity) sprite.Opacity = appearance.Opacity;
        shown = appearance;
    }

    public void Remove()
    {
        parent.Children.Remove(tile);
        parent.Children.Remove(sprite);
    }

    public static void Position(FrameworkElement element, System.Windows.Rect rect)
    {
        Canvas.SetLeft(element, rect.X);
        Canvas.SetTop(element, rect.Y);
        element.Width = rect.Width;
        element.Height = rect.Height;
    }
}
