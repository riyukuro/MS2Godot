namespace Ms2Godot.Tests;

/// <summary>
/// The headless checks' "allocates nothing" window, as the unit tests' Alloc.Least (tests/Shared/Alloc.cs): a single window
/// around a long loop also counts the JIT's one-time compile of the loop on this thread (on-stack replacement), which can
/// allocate kilobytes while other threads load. The least of up to 5 windows is kept (a window of 0 ends early); a real
/// per-call allocation shows up in every one.
/// </summary>
static class CheckAlloc
{
    /// <summary>The least bytes <paramref name="calls"/> allocates over up to 5 windows; it must be able to run again.</summary>
    public static long Least(System.Action calls)
    {
        long least = long.MaxValue;
        for (int i = 0; i < 5 && least > 0; i++)
        {
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            calls();
            least = System.Math.Min(least, System.GC.GetAllocatedBytesForCurrentThread() - before);
        }
        return least;
    }
}
