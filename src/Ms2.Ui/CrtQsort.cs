namespace Ms2.Ui;

/// <summary>
/// The CRT's qsort (ucrt qsort.cpp, the median-of-three quicksort with the selection "shortsort" at 8 elements or
/// fewer), for a client sort whose comparison never answers 0 for distinct keys or is otherwise not a total order: the
/// order of ties then depends on this algorithm, which a stable or introspective sort does not reproduce.
/// </summary>
public static class CrtQsort
{
    const int Cutoff = 8;

    /// <summary>qsort(base, list.Count, sizeof(T), cmp).</summary>
    public static void Sort<T>(IList<T> a, Comparison<T> cmp)
    {
        int num = a.Count;
        if (num < 2) return;
        var lostk = new Stack<int>();
        var histk = new Stack<int>();
        int lo = 0, hi = num - 1;
        void Swap(int x, int y) { if (x != y) (a[x], a[y]) = (a[y], a[x]); }
        for (;;)
        {
            int size = hi - lo + 1;
            if (size <= Cutoff) ShortSort(a, lo, hi, cmp);
            else
            {
                int mid = lo + size / 2;
                if (cmp(a[lo], a[mid]) > 0) Swap(lo, mid);
                if (cmp(a[lo], a[hi]) > 0) Swap(lo, hi);
                if (cmp(a[mid], a[hi]) > 0) Swap(mid, hi);
                int loguy = lo, higuy = hi;
                for (;;)
                {
                    if (mid > loguy)
                        do loguy++; while (loguy < mid && cmp(a[loguy], a[mid]) <= 0);
                    if (mid <= loguy)
                        do loguy++; while (loguy <= hi && cmp(a[loguy], a[mid]) <= 0);
                    do higuy--; while (higuy > mid && cmp(a[higuy], a[mid]) > 0);
                    if (higuy < loguy) break;
                    Swap(loguy, higuy);
                    if (mid == higuy) mid = loguy;
                }
                higuy++;
                if (mid < higuy)
                    do higuy--; while (higuy > mid && cmp(a[higuy], a[mid]) == 0);
                if (mid >= higuy)
                    do higuy--; while (higuy > lo && cmp(a[higuy], a[mid]) == 0);
                if (higuy - lo >= hi - loguy)
                {
                    if (lo < higuy) { lostk.Push(lo); histk.Push(higuy); }
                    if (loguy < hi) { lo = loguy; continue; }
                }
                else
                {
                    if (loguy < hi) { lostk.Push(loguy); histk.Push(hi); }
                    if (lo < higuy) { hi = higuy; continue; }
                }
            }
            if (lostk.Count == 0) return;
            lo = lostk.Pop();
            hi = histk.Pop();
        }
    }

    /// <summary>shortsort: the largest of lo..hi (the first of equal largest ones) swapped to hi, hi moved down.</summary>
    static void ShortSort<T>(IList<T> a, int lo, int hi, Comparison<T> cmp)
    {
        while (hi > lo)
        {
            int max = lo;
            for (int p = lo + 1; p <= hi; p++)
                if (cmp(a[p], a[max]) > 0) max = p;
            if (max != hi) (a[max], a[hi]) = (a[hi], a[max]);
            hi--;
        }
    }
}
