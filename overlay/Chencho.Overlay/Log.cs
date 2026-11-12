using System;
using System.IO;
using Chencho.Core;

namespace Chencho.Overlay;

/// Appends to `logs\overlay.log`.
internal static class Log
{
    public static readonly string Directory = Path.Combine(AgentConfig.DataDirectory, "logs");
    private static readonly string FilePath = Path.Combine(Directory, "overlay.log");
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}";
        lock (Gate)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.AppendAllText(FilePath, line);
            }
            catch (IOException) { /* logging must never take the strip down */ }
            catch (UnauthorizedAccessException) { }
        }
    }
}
