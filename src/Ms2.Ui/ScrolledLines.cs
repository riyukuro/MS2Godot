using System;
using System.Collections.Generic;

namespace Ms2.Ui;

/// <summary>
/// Which line a label draws from for a script text field scrolled to a line. The script's line count is the AVM's own wrap and
/// the label's is the renderer's, so the script's first line is found again in the label's lines by the character it starts at,
/// and at the end of the scroll range the label draws the last lines that fit, so the newest line is always the last one drawn
/// (Godot-free so it can be tested; TextNode feeds it the label's lines).
/// </summary>
public static class ScrolledLines
{
    /// <summary>How many of <paramref name="count"/> lines from <paramref name="first"/> fit <paramref name="room"/>
    /// (LineBuffer::IsLineVisible, sfr_render_04d.cpp:365): the first always, each other one when its bottom (the advances before
    /// it plus its height) is within the room. <paramref name="metrics"/> gives a line's height and advance.</summary>
    public static int Fit(int count, Func<int, (double Height, double Advance)> metrics, double room, int first)
    {
        double top = 0;
        int n = 0;
        for (int i = first; i < count; i++)
        {
            var (height, advance) = metrics(i);
            if (i > first && top + height > room) break;
            n++;
            top += advance;
        }
        return n;
    }

    /// <summary>The smallest first line from which every line to the last one fits. Only the lines at the end are measured.</summary>
    public static int LastPosition(int count, Func<int, (double Height, double Advance)> metrics, double room)
    {
        var memo = new Dictionary<int, (double, double)>();
        (double, double) M(int i) { if (!memo.TryGetValue(i, out var m)) memo[i] = m = metrics(i); return m; }
        int pinned = Math.Max(0, count - 1);
        while (pinned > 0 && Fit(count, M, room, pinned - 1) == count - (pinned - 1)) pinned--;
        return pinned;
    }

    /// <summary>The line to draw from. <paramref name="starts"/> are the lines' first character indexes; <paramref name="start"/>
    /// is the character the script's first line starts at. Not below the line holding it and not past the last position; at the
    /// end of the script's range exactly the last position.</summary>
    public static int FirstLine(IReadOnlyList<int> starts, Func<int, (double Height, double Advance)> metrics, double room,
        int start, bool atEnd)
    {
        if (starts.Count == 0) return 0;
        int pinned = LastPosition(starts.Count, metrics, room);
        if (atEnd) return pinned;
        int line = 0;
        for (int i = 0; i < starts.Count && starts[i] <= start; i++) line = i;
        return Math.Min(line, pinned);
    }
}
