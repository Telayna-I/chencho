using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Chencho.Core;
using Localization = Chencho.Core.Localization;
using Size = System.Windows.Size;
using Rect = System.Windows.Rect;
using CoreRect = Chencho.Core.Rect;

namespace Chencho.Overlay;

internal sealed class StripController
{
    /// What the pointer can be over: a session's creature, the add button after the last one, or the tab.
    private abstract record Cell
    {
        public sealed record Session(string Id) : Cell;
        public sealed record Add : Cell;
        public sealed record Tab : Cell;
    }

    private readonly AgentLink link;
    private readonly StripView view = new();
    private readonly HoverLabel label = new();
    private StripLayout layout = StripLayout.Fitting(0, 0);
    /// The sessions the current layout has room for.
    private IReadOnlyList<PanelSession> shown = Array.Empty<PanelSession>();
    private Cell? hovered;
    private DispatcherTimer? clock;
    private bool collapsed;
    private bool hidden;
    private ScreenEdge edge;
    /// The session whose terminal is in front, found locally (the agent can't see windows).
    private string? focusedId;
    private IntPtr lastForeground;

    public bool Hidden
    {
        get => hidden;
        set { hidden = value; Relayout(); }
    }

    public ScreenEdge Edge
    {
        get => edge;
        set
        {
            edge = value;
            Preferences.Edge = value;
            Relayout();
        }
    }

    public StripController(AgentLink link)
    {
        this.link = link;
        edge = Preferences.Edge;
        collapsed = Preferences.Collapsed;
        view.OnHover = Hover;
        view.OnClick = Click;
        view.MenuFor = Menu;
        view.ScreenChanged += Relayout;
        Locale.Changed += Render;
        link.Changed += Relayout;
        SystemEvents.DisplaySettingsChanged += (_, _) => view.Dispatcher.BeginInvoke(Relayout);
        Relayout();
    }

    private void Relayout()
    {
        var now = DateTimeOffset.Now;
        shown = link.List.Shown(now);
        TerminalFocus.Forget(link.List.Sessions.Values.Where(s => s.Pid.HasValue).Select(s => s.Pid!.Value));
        if (hidden)
        {
            view.Hide();
            label.Hide();
            StopClock();
            hovered = null; // no MouseLeave arrives for a hidden window
            return;
        }
        // The primary screen, without the taskbar.
        var visible = SystemParameters.WorkArea;
        layout = StripLayout.Fitting(shown.Count + 1, visible.Height);
        var frame = layout.Frame(new CoreRect(visible.X, visible.Y, visible.Width, visible.Height), edge);
        if (view.Left != frame.X || view.Top != frame.Y || view.Width != frame.Width || view.Height != frame.Height)
        {
            view.Left = frame.X;
            view.Top = frame.Y;
            view.Width = frame.Width;
            view.Height = frame.Height;
        }
        view.Arrange(layout, edge, collapsed, new Size(frame.Width, frame.Height));
        view.BringToFront();
        StartClock();
        Render();
    }

    private void Render()
    {
        var now = DateTimeOffset.Now;
        var current = link.List.Shown(now);
        // A finished session that dozes off leaves the strip, which then needs a new layout.
        if (!current.Select(s => s.Id).SequenceEqual(shown.Select(s => s.Id)))
        {
            Relayout();
            return;
        }
        shown = current;
        TrackForeground();

        var frame = Clock.FrameAt(now);
        if (!collapsed)
        {
            var appearances = shown
                .Select(s => CreatureAppearance.For(s, s.Id == focusedId, hovered == new Cell.Session(s.Id), frame, now))
                .Append(CreatureAppearance.AddButton(hovered is Cell.Add))
                .ToList();
            view.Show(appearances);
        }
        view.ShowTab(CreatureAppearance.Tab(shown, collapsed, edge, hovered is Cell.Tab, frame, now));

        var notice = link.CurrentNotice;
        Cell? noticeCell = notice is null ? null : notice.SessionId is { } id ? new Cell.Session(id) : new Cell.Add();
        var labelled = hovered ?? noticeCell;
        if (labelled is null || (collapsed && labelled is not Cell.Tab)
            || LabelContent(labelled, noticeCell == labelled && notice is not null ? Localization.Phrase(notice.Message, Locale.Current) : null, now) is not { } content)
        {
            label.Hide();
            return;
        }
        label.Show(content);
    }

    /// Follows the window in front: it moves the white ring, and a new topmost window may cover the strip.
    private void TrackForeground()
    {
        var foreground = Native.GetForegroundWindow();
        // While a menu is open Chencho holds the foreground; the ring stays where it was.
        if (foreground == MenuHost.AnchorHandle) return;
        if (foreground != lastForeground)
        {
            lastForeground = foreground;
            if (view.IsVisible) Native.RaiseTopmost(view.Handle);
        }
        focusedId = TerminalFocus.FocusedSessionId(shown);
    }

    private HoverLabel.Content? LabelContent(Cell cell, string? failure, DateTimeOffset now)
    {
        var failed = Theme.Of(Paint.Danger);
        var bounds = new CoreRect(0, 0, view.Width, view.Height);
        var strip = layout.StripRect(bounds, edge);
        var window = new System.Windows.Rect(view.Left, view.Top, view.Width, view.Height);
        HoverLabel.Content Content(string name, string status, Color color, CoreRect beside, CoreRect? outside = null) =>
            new(name, status, color, view.OnScreen(beside), outside is { } o ? view.OnScreen(o) : window, edge);

        switch (cell)
        {
            case Cell.Session(var id):
                var index = shown.ToList().FindIndex(s => s.Id == id);
                if (index < 0) return null;
                var session = shown[index];
                var color = failure is not null ? failed : session.NeedsAttention ? Theme.Of(Paint.Accent) : Theme.Muted;
                return Content(session.Name, failure ?? session.StatusLine(now, Locale.Current), color, layout.CellFrame(index, strip));
            case Cell.Add:
                return Content(Locale.T("Add a session", "Agregar una sesión"),
                    failure ?? Locale.T("pin any Claude Code session", "anclar cualquier sesión de Claude Code"),
                    failure is not null ? failed : Theme.Muted,
                    layout.CellFrame(shown.Count, strip));
            case Cell.Tab:
                var tab = layout.TabRect(bounds, edge, collapsed);
                var tabColor = shown.Any(s => s.NeedsAttention) ? Theme.Of(Paint.Accent) : Theme.Muted;
                var title = collapsed ? Locale.T("Expand", "Expandir") : Locale.T("Collapse", "Contraer");
                return Content(title, Status.Summary(shown, Locale.Current), tabColor, tab, collapsed ? tab : null);
            default:
                return null;
        }
    }

    private Cell? CellAt(int index)
    {
        if (index >= 0 && index < shown.Count) return new Cell.Session(shown[index].Id);
        return index == shown.Count ? new Cell.Add() : null;
    }

    private void StartClock()
    {
        if (clock is not null) return;
        clock = new DispatcherTimer(DispatcherPriority.Render, view.Dispatcher);
        clock.Tick += (_, _) =>
        {
            clock!.Interval = UntilNextFrame();
            Render();
        };
        clock.Interval = UntilNextFrame();
        clock.Start();
    }

    /// Ticks land just after each frame boundary, so no frame is skipped or drawn twice.
    private static TimeSpan UntilNextFrame()
    {
        var periodMs = 1000.0 / Clock.FramesPerSecond;
        var nowMs = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        var next = (Math.Floor(nowMs / periodMs) + 1) * periodMs + 5;
        return TimeSpan.FromMilliseconds(next - nowMs);
    }

    private void StopClock()
    {
        clock?.Stop();
        clock = null;
    }

    private void Hover(StripHit? hit)
    {
        Cell? cell = hit switch
        {
            StripHit.Cell c => CellAt(c.Index),
            StripHit.Tab => new Cell.Tab(),
            _ => null,
        };
        if (cell == hovered) return;
        hovered = cell;
        Render();
    }

    private ContextMenu? Menu(int index)
    {
        if (CellAt(index) is not Cell.Session(var id) || shown.FirstOrDefault(s => s.Id == id) is not { } session) return null;
        Hover(null); // the label would sit under the menu
        return CreatureMenu.Make(session, link.Send, () => Open(session));
    }

    private void Click(StripHit hit)
    {
        if (hit is not StripHit.Cell cell)
        {
            ToggleCollapsed();
            return;
        }
        switch (CellAt(cell.Index))
        {
            case Cell.Session(var id) when shown.FirstOrDefault(s => s.Id == id) is { } session:
                Open(session);
                break;
            case Cell.Add:
                Hover(null);
                link.RequestLibrary(entries =>
                    // Not from inside the receive loop: the menu holds the mouse until it closes.
                    view.Dispatcher.BeginInvoke(() => ShowLibrary(entries)));
                break;
        }
    }

    /// A running session's terminal comes to the front; any other session resumes in a new terminal tab.
    private void Open(PanelSession session)
    {
        if (session.Live && session.Pid.HasValue)
        {
            if (TerminalFocus.Focus(session)) focusedId = session.Id;
            else link.Show(new AgentLink.Notice(session.Id, "can't find its terminal"));
            Render();
            return;
        }
        // The agent opens Windows Terminal, which may then come to the front.
        Native.AllowSetForegroundWindow(Native.ASFW_ANY);
        link.Send(new ClientMessage.Open(session.Id));
    }

    private void ToggleCollapsed()
    {
        collapsed = !collapsed;
        Preferences.Collapsed = collapsed;
        hovered = null; // the tab slides out from under the pointer
        Render();
        view.Slide(collapsed);
    }

    private void ShowLibrary(IReadOnlyList<LibraryEntry> entries)
    {
        var menu = LibraryMenu.Make(entries, DateTimeOffset.Now, link.Send);
        view.PopUp(menu, shown.Count);
    }
}
