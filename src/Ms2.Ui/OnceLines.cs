namespace Ms2.Ui;

/// <summary>
/// Console lines for a part this port reads and does not apply or play (a packet kind, a system sound, a screen effect, a
/// missing effect nif): each distinct line goes to the sink once per run, as LiveSession's ReadNotApplied does per packet and
/// mode, so a packet the server repeats does not print a line each time. The line is its own key: what varies in it (the kind,
/// the sound, the effect) tells the cases apart. Thread-safe.
/// </summary>
public sealed class OnceLines
{
    readonly HashSet<string> seen = new(StringComparer.Ordinal);
    readonly Action<string> sink;

    public OnceLines(Action<string> sink) => this.sink = sink;

    /// <summary>Sends <paramref name="line"/> to the sink when it has not been sent before; true when it was sent.</summary>
    public bool Print(string line)
    {
        if (line == null) return false;
        lock (seen)
            if (!seen.Add(line)) return false;
        sink?.Invoke(line);
        return true;
    }
}
