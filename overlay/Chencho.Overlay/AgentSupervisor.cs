using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Threading;
using Chencho.Core;

namespace Chencho.Overlay;

/// Keeps the agent running: Windows has no per-user service manager that restarts a crashed console
/// program without a window or admin rights.
/// The agent outlives the strip: quitting Chencho leaves it tracking sessions.
internal sealed class AgentSupervisor
{
    private readonly string agentPath;
    private readonly bool lan;
    private readonly DispatcherTimer check;
    private Process? child;
    private DateTime launchedAt = DateTime.MinValue;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(1.5) };
    /// A fresh agent needs a moment before /health answers.
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(8);

    private AgentSupervisor(string agentPath, bool lan, Dispatcher dispatcher)
    {
        this.agentPath = agentPath;
        this.lan = lan;
        check = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) => _ = Ensure(), dispatcher);
    }

    /// Null when `chencho.exe` isn't beside the overlay, as in a development build.
    public static AgentSupervisor? Start(bool lan, Dispatcher dispatcher)
    {
        var directory = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var agent = Path.Combine(directory, "chencho.exe");
        if (!File.Exists(agent))
        {
            Log.Write($"no agent at {agent}; not supervising");
            return null;
        }
        var supervisor = new AgentSupervisor(agent, lan, dispatcher);
        _ = supervisor.Ensure();
        supervisor.check.Start();
        return supervisor;
    }

    private async Task Ensure()
    {
        if (child is { HasExited: false }) return;
        if (DateTime.UtcNow - launchedAt < Grace) return;
        if (await Healthy()) return; // started by setup, by hand, or by an earlier strip
        Launch();
    }

    private static async Task<bool> Healthy()
    {
        try
        {
            using var response = await Http.GetAsync($"http://127.0.0.1:{AgentConfig.PanelPort}/health");
            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void Launch()
    {
        Directory.CreateDirectory(Log.Directory);
        var log = Path.Combine(Log.Directory, "agent.log");
        var arguments = lan ? "agent --lan" : "agent";
        // cmd holds the log redirection, so the agent keeps logging after the strip quits.
        var command = $"/d /c \"\"{agentPath}\" {arguments} >> \"{log}\" 2>&1\"";
        try
        {
            child = Process.Start(new ProcessStartInfo("cmd.exe", command)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(agentPath)!,
            });
            launchedAt = DateTime.UtcNow;
            Log.Write($"started the agent (pid {child?.Id})");
        }
        catch (Exception error)
        {
            Log.Write($"could not start the agent: {error.Message}");
        }
    }
}
