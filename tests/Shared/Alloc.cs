namespace Ms2Godot.Tests;

/// <summary>
/// The measurement for the "N calls allocate nothing" (or under a budget) tests, shared by every test project.
/// A single window around a loop counts more than the calls: the JIT runs on the test thread inside it. A loop of a
/// thousand or more turns makes the runtime compile the method that holds it again mid-run (on-stack replacement), and a
/// method called for the first time in the window is compiled there too. That compile can allocate on the thread: a few
/// bytes (the 24 B a Release run reads) or, with other tests running in parallel, kilobytes (a FieldReactorsTests window
/// of 100000 polls read 872, 7488 and 7944 B in 3 of 6 parallel Release runs, at the compile of its own loop, and 0 B
/// alone). It happens once, so a window run again reads only the calls; a real per-call allocation shows up in every
/// window. So the least of up to 5 windows is kept (a window of 0 ends early), and the counter is read inside, around the
/// calls alone, never with a constraint or a message built between the reads.
/// </summary>
static class Alloc
{
    /// <summary>The least bytes <paramref name="calls"/> allocates over up to 5 windows (a window of 0 ends early).
    /// <paramref name="calls"/> must leave the state so that it can run again.</summary>
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

    /// <summary><see cref="Least(Action)"/> for a window that measures itself and returns its bytes (one that must set
    /// something up outside the count each time).</summary>
    public static long Least(Func<long> window)
    {
        long least = long.MaxValue;
        for (int i = 0; i < 5 && least > 0; i++) least = Math.Min(least, window());
        return least;
    }
}
