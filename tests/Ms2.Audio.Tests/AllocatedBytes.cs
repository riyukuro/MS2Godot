namespace Ms2.Audio.Tests;

/// <summary>
/// The bytes a run allocates on this thread, as the least over several runs. The thread's counter can jump once by under
/// 8 KB at a random point with no collection (seen at one frame of 20000 in about half of the runs on a loaded machine,
/// 624 to 7584 B); an allocation in the code under test is in every run.
/// </summary>
static class AllocatedBytes
{
    public static long LeastOf(int runs, Action run)
    {
        long least = long.MaxValue;
        for (int r = 0; r < runs; r++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            run();
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        return least;
    }
}
