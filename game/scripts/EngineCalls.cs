using System;
using System.Collections.Concurrent;
using Godot;

namespace Ms2Godot;

/// <summary>
/// The log lines of code that can run on a worker thread (a pool task: the setup build, a failed send). GD.Print and
/// GD.PushWarning are engine calls, and one a worker makes after the quit reaches the engine's teardown (the exit crash
/// of the music decode, SIGSEGV). On the main thread a line goes straight to the engine; on another thread it waits in a
/// queue that <see cref="QuitFinalizers"/> empties each frame on the main thread. Once the quit closes the queue
/// (<see cref="QuitFinalizers"/>' _ExitTree, after every other node has left the tree) a worker's line goes to the
/// process's standard error instead, which makes no engine call.
/// </summary>
public static class EngineCalls
{
    static int mainThread = -1;
    static volatile bool closed;
    static readonly ConcurrentQueue<(bool Warning, string Text)> pending = new();

    /// <summary>Whether the caller is the engine's main thread (the one that opened the queue).</summary>
    public static bool OnMainThread => System.Environment.CurrentManagedThreadId == mainThread;

    public static void Print(string text) => Log(false, text);

    public static void Warn(string text) => Log(true, text);

    static void Log(bool warning, string text)
    {
        if (closed) { Console.Error.WriteLine(text); return; }
        if (OnMainThread) Write(warning, text);
        else pending.Enqueue((warning, text));
    }

    static void Write(bool warning, string text)
    {
        if (warning) GD.PushWarning(text); else GD.Print(text);
    }

    /// <summary>The main thread's start (the first autoload entering the tree).</summary>
    internal static void Open()
    {
        mainThread = System.Environment.CurrentManagedThreadId;
        closed = false;
    }

    /// <summary>Writes the queued lines (the main thread, each frame).</summary>
    internal static void Drain()
    {
        while (pending.TryDequeue(out var line)) Write(line.Warning, line.Text);
    }

    /// <summary>The quit: the queued lines are written, later ones go to standard error.</summary>
    internal static void Close()
    {
        Drain();
        closed = true;
    }
}
