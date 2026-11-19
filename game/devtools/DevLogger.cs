#if MS2_DEVTOOLS
using System.Collections.Generic;
using Godot;

namespace Ms2Godot;

/// <summary>
/// Collects the engine's log stream (print, GD.Print, push_error, push_warning and script errors) into a ring buffer for the
/// debug endpoint's log command. The engine calls it from any thread, so it only appends to the buffer under a lock.
/// </summary>
public partial class DevLogger : Logger
{
    public const int Capacity = 4000;

    readonly object gate = new();
    readonly LinkedList<(long seq, string kind, string text)> lines = new();
    long next = 1;

    void Add(string kind, string text)
    {
        lock (gate)
        {
            lines.AddLast((next++, kind, text));
            while (lines.Count > Capacity) lines.RemoveFirst();
        }
    }

    public override void _LogMessage(string message, bool error) => Add(error ? "stderr" : "out", message.TrimEnd('\n'));

    public override void _LogError(string function, string file, int line, string code, string rationale, bool editorNotify, int errorType,
        Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
    {
        string kind = errorType == (int)ErrorType.Warning ? "warning" : "error";
        Add(kind, $"{(string.IsNullOrEmpty(rationale) ? code : rationale)} ({function} {file}:{line})");
    }

    /// <summary>Entries after sequence number <paramref name="since"/> (0 = from the oldest kept), at most <paramref name="max"/>.</summary>
    public (long last, List<(long seq, string kind, string text)> items) Since(long since, int max)
    {
        var list = new List<(long, string, string)>();
        lock (gate)
        {
            foreach (var l in lines) { if (l.seq > since) list.Add(l); if (list.Count >= max) break; }
            return (next - 1, list);
        }
    }
}
#endif
