using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Automation;
using Chencho.Core;

namespace Chencho.Overlay;

/// Finds the terminal window that hosts a Claude session, brings it (and its Windows Terminal tab) to the
/// front, and tells which session is in front.
///
/// Claude Code titles its terminal tab with a spinner glyph and the session's name ("✳ fix-login"), the
/// same name the agent reports, and Windows Terminal titles its window after the selected tab.
internal static class TerminalFocus
{
    private const string WindowsTerminalClass = "CASCADIA_HOSTING_WINDOW_CLASS";
    private static readonly TimeSpan HostLifetime = TimeSpan.FromSeconds(10);
    private static readonly Dictionary<int, Host> Hosts = new();
    /// Kept alive for as long as the process: the console APIs call it from another thread.
    private static readonly Native.ConsoleCtrlHandler IgnoreConsoleEvents = _ => true;

    private sealed record Host(IntPtr Window, string ConsoleTitle, DateTime ResolvedAt);

    /// While briefly attached to a session's console, a Ctrl+C in that terminal must not end the strip.
    public static void Prepare() => Native.SetConsoleCtrlHandler(IgnoreConsoleEvents, true);

    /// Must run on the thread that received the click: Windows lets it set the foreground window.
    public static bool Focus(PanelSession session)
    {
        if (session.Pid is not { } pid) return false;
        var host = Resolve(pid, refresh: true);
        if (host.Window == IntPtr.Zero) return false;
        if (Native.IsIconic(host.Window)) Native.ShowWindow(host.Window, Native.SW_RESTORE);
        Native.SetForegroundWindow(host.Window);
        if (Native.ClassName(host.Window) == WindowsTerminalClass)
        {
            var window = host.Window;
            var titles = new[] { Bare(host.ConsoleTitle), Bare(session.Name) };
            Task.Run(() => SelectTab(window, titles));
        }
        return true;
    }

    /// The live session whose terminal is the foreground window, or null.
    public static string? FocusedSessionId(IEnumerable<PanelSession> sessions)
    {
        var foreground = Native.GetForegroundWindow();
        if (foreground == IntPtr.Zero) return null;
        var candidates = sessions
            .Where(s => s.Live && s.Pid is { } pid && Resolve(pid, refresh: false).Window == foreground)
            .ToList();
        if (candidates.Count <= 1) return candidates.FirstOrDefault()?.Id;
        // Several sessions in one Windows Terminal window: the window carries the selected tab's title.
        var selected = Bare(Native.WindowText(foreground));
        return candidates.FirstOrDefault(s => Bare(s.Name) == selected)?.Id
            ?? candidates.FirstOrDefault(s => Bare(Hosts[s.Pid!.Value].ConsoleTitle) == selected)?.Id;
    }

    /// Drops what's cached about processes that are gone.
    public static void Forget(IEnumerable<int> livePids)
    {
        var keep = livePids.ToHashSet();
        foreach (var pid in Hosts.Keys.Where(pid => !keep.Contains(pid)).ToList()) Hosts.Remove(pid);
    }

    private static Host Resolve(int pid, bool refresh)
    {
        if (Hosts.TryGetValue(pid, out var cached) && !refresh
            && DateTime.UtcNow - cached.ResolvedAt < HostLifetime
            && (cached.Window == IntPtr.Zero || Native.IsWindow(cached.Window)))
            return cached;
        var host = FromConsole(pid) ?? new Host(FromAncestors(pid), "", DateTime.UtcNow);
        Hosts[pid] = host;
        return host;
    }

    /// Windows Terminal and conhost own the session's console window, even when the terminal was opened
    /// as the default terminal and isn't the shell's parent process.
    private static Host? FromConsole(int pid)
    {
        if (!Native.AttachConsole(pid)) return null;
        try
        {
            var console = Native.GetConsoleWindow();
            var title = new StringBuilder(1024);
            Native.GetConsoleTitle(title, title.Capacity);
            var owner = console == IntPtr.Zero ? IntPtr.Zero : Native.GetAncestor(console, Native.GA_ROOTOWNER);
            // A pseudo console that no window adopted (VS Code's terminal) stays hidden.
            if (owner == IntPtr.Zero || !Native.IsWindowVisible(owner)) return new Host(FromAncestors(pid), title.ToString(), DateTime.UtcNow);
            return new Host(owner, title.ToString(), DateTime.UtcNow);
        }
        finally
        {
            Native.FreeConsole();
        }
    }

    /// The nearest ancestor process with a visible top-level window, e.g. VS Code for its terminal.
    private static IntPtr FromAncestors(int pid)
    {
        var parents = ProcessParents();
        var current = pid;
        for (var depth = 0; depth < 8 && parents.TryGetValue(current, out var parent) && parent > 0; depth++)
        {
            current = parent;
            var window = MainWindow(current);
            if (window != IntPtr.Zero) return window;
        }
        return IntPtr.Zero;
    }

    private static IntPtr MainWindow(int processId)
    {
        var found = IntPtr.Zero;
        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd) || Native.GetWindow(hwnd, 4 /* GW_OWNER */) != IntPtr.Zero) return true;
            Native.GetWindowThreadProcessId(hwnd, out var owner);
            if (owner != processId || Native.GetWindowTextLength(hwnd) == 0) return true;
            found = hwnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private static Dictionary<int, int> ProcessParents()
    {
        var parents = new Dictionary<int, int>();
        var snapshot = Native.CreateToolhelp32Snapshot(Native.TH32CS_SNAPPROCESS, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1)) return parents;
        try
        {
            var entry = new Native.PROCESSENTRY32 { dwSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.PROCESSENTRY32>() };
            for (var more = Native.Process32First(snapshot, ref entry); more; more = Native.Process32Next(snapshot, ref entry))
                parents[entry.th32ProcessID] = entry.th32ParentProcessID;
        }
        finally
        {
            Native.CloseHandle(snapshot);
        }
        return parents;
    }

    /// Selects the Windows Terminal tab titled like the session, through UI Automation.
    private static void SelectTab(IntPtr window, string[] titles)
    {
        try
        {
            var tabs = AutomationElement.FromHandle(window).FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem));
            foreach (var title in titles.Where(t => t.Length > 0))
            {
                foreach (AutomationElement tab in tabs)
                {
                    if (Bare(tab.Current.Name) != title) continue;
                    if (tab.GetCurrentPattern(SelectionItemPattern.Pattern) is SelectionItemPattern item && !item.Current.IsSelected)
                        item.Select();
                    return;
                }
            }
        }
        catch (Exception error)
        {
            Log.Write($"could not select the terminal tab: {error.Message}");
        }
    }

    /// The title without Claude Code's leading status glyph ("◐ fix-login" → "fix-login").
    public static string Bare(string title)
    {
        title = title.Trim();
        var space = title.IndexOf(' ');
        if (space is 1 or 2 && !title[..space].Any(char.IsLetterOrDigit)) return title[(space + 1)..].Trim();
        return title;
    }
}
