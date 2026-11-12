using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace Chencho.Overlay;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var gallery = Array.IndexOf(args, "--gallery");
        if (gallery >= 0) return Gallery.Run(args.ElementAtOrDefault(gallery + 1) ?? "gallery");
        var focus = Array.IndexOf(args, "--focus");
        if (focus >= 0 && int.TryParse(args.ElementAtOrDefault(focus + 1), out var pid)) return FocusCheck(pid, args.ElementAtOrDefault(focus + 2) ?? "");

        if (!SingleInstance.Claim())
        {
            Console.Error.WriteLine("chencho-overlay is already running");
            return 0;
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Load(app);
        app.DispatcherUnhandledException += (_, e) =>
        {
            Log.Write($"unhandled: {e.Exception}");
            e.Handled = true;
        };
        TerminalFocus.Prepare();

        var link = new AgentLink(app.Dispatcher);
        var strip = new StripController(link);
        var tray = new TrayIcon(link, strip);
        AgentSupervisor.Start(lan: args.Contains("--lan"), app.Dispatcher);
        link.Start();
        Log.Write("started");
        app.Run();
        tray.Dispose();
        return 0;
    }

    /// Development aid: what a click on the creature of session `name` (Claude process `pid`) does.
    private static int FocusCheck(int pid, string name)
    {
        TerminalFocus.Prepare();
        var session = new Chencho.Core.PanelSession("check", name, "", Chencho.Core.SessionState.Working, null, true,
            DateTimeOffset.Now, false, null, Chencho.Core.SessionIcon.Default, pid);
        var found = TerminalFocus.Focus(session);
        System.Threading.Thread.Sleep(2000); // the tab is selected in the background
        Console.WriteLine($"focused={found} foreground=\"{Native.WindowText(Native.GetForegroundWindow())}\" in-front={TerminalFocus.FocusedSessionId(new[] { session }) ?? "none"}");
        return found ? 0 : 1;
    }

    public static void Load(Application app)
    {
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/chencho-overlay;component/MenuTheme.xaml"),
        });
        MenuPalette.Apply(app.Resources);
    }
}
