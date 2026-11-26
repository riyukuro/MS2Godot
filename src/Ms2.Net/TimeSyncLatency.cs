namespace Ms2.Net;

/// <summary>
/// Unk_NetworkLatency (CNetworkService::m_latency, networkservice.cpp:1229-1258): the round trips of the time sync requests.
/// Sent keeps each request's tick by its sequence; Received (the command 0 answer) turns it into a sample, keeps the last
/// five samples by sequence (the RB map's head, the lowest sequence, goes first) and sets the minimum, maximum and average
/// over them. Reset (command 1) clears everything. The average is what CMainSystem::SetServerTickBase keeps as m_i74
/// (FUN_141473460), the number the frame rate dialog shows in parentheses.
/// </summary>
public sealed class TimeSyncLatency
{
    /// <summary>The samples kept (samples.GetCount() > 5 removes the head).</summary>
    public const int MaxSamples = 5;

    readonly object gate = new();
    readonly Dictionary<uint, uint> sent = new();
    readonly SortedDictionary<uint, uint> samples = new();
    volatile int minimum, maximum, average;

    /// <summary>minimum, maximum, average in ms (unsigned in the client, read back as int by SetServerTickBase).</summary>
    public int Minimum => minimum;
    public int Maximum => maximum;
    public int Average => average;

    /// <summary>Reset @0x14153fb70: the statistics to 0, then Clear (both maps).</summary>
    public void Reset()
    {
        minimum = 0; maximum = 0; average = 0;
        lock (gate) { sent.Clear(); samples.Clear(); }
    }

    /// <summary>Sent @0x14153fc30: sent[sequence] = the tick in ms.</summary>
    public void Sent(uint sequence, uint nowMs)
    {
        lock (gate) sent[sequence] = nowMs;
    }

    /// <summary>
    /// Received @0x14153fca0: the request's tick is looked up and removed; with none (start 0) nothing changes. The round trip
    /// goes into the samples, the oldest sequence drops past five, and the statistics are taken over the copy.
    /// </summary>
    public void Received(uint sequence, uint nowMs)
    {
        uint min = uint.MaxValue, max = 0, total = 0;
        int count;
        lock (gate)
        {
            sent.TryGetValue(sequence, out uint start);
            sent.Remove(sequence);
            if (start == 0) return;
            samples[sequence] = nowMs - start;
            if (samples.Count > MaxSamples) samples.Remove(samples.Keys.First());
            count = samples.Count;
            foreach (uint v in samples.Values)
            {
                if (v >= max) max = v;
                if (v < min) min = v;
                total += v;
            }
        }
        if (count <= 0) return;
        minimum = (int)min; maximum = (int)max; average = (int)(total / (uint)count);
        // STILL-OPEN: FUN_140d86260()->vfunc_60(elapsed), the client platform's latency report, has no counterpart here.
    }
}
