#if MS2_DEVTOOLS
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Ms2.Devtools;

/// <summary>
/// Hand-off between the socket threads and the main thread. A socket thread <see cref="Submit"/>s a request and waits; the
/// main thread calls <see cref="Drain"/> from its frame callback, which runs the handler on that thread only, within a time
/// budget, and wakes the waiter with the response line. Nothing here touches the engine.
/// </summary>
public sealed class RequestQueue
{
    sealed class Item { public Request Req; public string Response; public readonly ManualResetEventSlim Done = new(false); }

    readonly ConcurrentQueue<Item> items = new();
    int pending;

    /// <summary>True when nothing is waiting: the per-frame check on the main thread is this one read.</summary>
    public bool Idle => Volatile.Read(ref pending) == 0;

    /// <summary>Queues the request and blocks the calling (socket) thread until it is answered or <paramref name="timeoutMs"/> passes.</summary>
    public string Submit(Request req, int timeoutMs)
    {
        var item = new Item { Req = req };
        Interlocked.Increment(ref pending);
        items.Enqueue(item);
        if (item.Done.Wait(timeoutMs)) return item.Response;
        req.Abandoned = true;
        return Response.Error(req.Id, "timed out after " + timeoutMs + " ms waiting for the main thread");
    }

    /// <summary>
    /// Runs queued requests with <paramref name="handler"/> until the queue is empty or <paramref name="budgetMs"/> is used up
    /// (at least one request runs when any is waiting). Must be called from the main thread, never from inside a signal or
    /// script callback. Returns the number of requests answered.
    /// </summary>
    public int Drain(Func<Request, JsonNode> handler, double budgetMs)
    {
        if (Idle) return 0;
        long t0 = Stopwatch.GetTimestamp();
        int n = 0;
        while (items.TryDequeue(out var item))
        {
            Interlocked.Decrement(ref pending);
            n++;
            if (!item.Req.Abandoned) item.Response = Run(handler, item.Req);
            item.Done.Set();
            if (Stopwatch.GetElapsedTime(t0).TotalMilliseconds >= budgetMs) break;
        }
        return n;
    }

    /// <summary>Answers everything still queued with an error (shutdown).</summary>
    public void FailAll(string why)
    {
        while (items.TryDequeue(out var item))
        {
            Interlocked.Decrement(ref pending);
            item.Response = Response.Error(item.Req.Id, why);
            item.Done.Set();
        }
    }

    static string Run(Func<Request, JsonNode> handler, Request req)
    {
        try { return Response.Ok(req.Id, handler(req)); }
        catch (Exception e) { return Response.Error(req.Id, e.GetType().Name + ": " + e.Message); }
    }
}
#endif
