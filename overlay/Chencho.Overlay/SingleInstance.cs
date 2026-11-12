using System.Threading;

namespace Chencho.Overlay;

internal static class SingleInstance
{
    private static Mutex? mutex;

    /// The mutex lasts as long as the process: Windows releases it on exit, even after a crash.
    public static bool Claim()
    {
        mutex = new Mutex(initiallyOwned: true, @"Local\chencho-overlay", out var created);
        return created;
    }
}
