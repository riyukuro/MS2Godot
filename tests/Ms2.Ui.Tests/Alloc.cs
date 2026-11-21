namespace Ms2.Ui.Tests;

/// <summary>The measurement for the "N calls allocate under 512 B" tests.</summary>
static class Alloc
{
    /// <summary>
    /// The least bytes <paramref name="calls"/> allocates over up to 5 windows (a window of 0 ends early). The runtime's tiering
    /// (call counting, the tier-1 rejit, OSR) can land one-time allocations in any window, more so on a slow 2-core runner, so
    /// the second window alone is not enough; a real per-call allocation shows up in every window, so the least still finds it.
    /// </summary>
    public static long Least(Action calls)
    {
        long least = long.MaxValue;
        for (int i = 0; i < 5 && least > 0; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            calls();
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        return least;
    }

    /// <summary><see cref="Least(Action)"/> for a window that measures itself and returns its bytes.</summary>
    public static long Least(Func<long> window)
    {
        long least = long.MaxValue;
        for (int i = 0; i < 5 && least > 0; i++) least = Math.Min(least, window());
        return least;
    }
}
