using System;
using System.Linq;
using System.Threading;
using Godot;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/options_threads.tscn): <see cref="GameOptions.Language"/> asked by several threads at the
/// same moment, as the field prefetch (a pool thread) and the UI (the main thread) ask it at field load, reads the options
/// once, without an exception, and every thread gets the same language. Quits with 0 when it holds, else 1. Run it with
/// --options-file (a scratch path) so the shared user folder is not read or written.
/// </summary>
public partial class OptionsThreadsCheck : Node
{
    public override void _Ready()
    {
        const int n = 8;
        using var start = new Barrier(n);
        var langs = new int[n];
        var errors = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        var threads = Enumerable.Range(0, n).Select(i => new Thread(() =>
        {
            start.SignalAndWait();
            try { langs[i] = GameOptions.Language; }
            catch (Exception e) { errors.Add(e); }
        })).ToArray();
        foreach (var t in threads) t.Start();
        foreach (var t in threads) t.Join();
        bool ok = errors.IsEmpty && GameOptions.Loads == 1 && langs.All(l => l == langs[0]);
        GD.Print($"options threads: {n} threads, {GameOptions.Loads} loads, languages {string.Join(",", langs)}, {errors.Count} errors{(errors.IsEmpty ? "" : ": " + errors.First().Message)} -> {(ok ? "ok" : "FAIL")}");
        GetTree().Quit(ok ? 0 : 1);
    }
}
