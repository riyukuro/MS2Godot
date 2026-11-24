using System;

namespace Ms2.Ui;

/// <summary>
/// A per-char array that only holds the chars with a format of their own (a plain char leaves its slot default), grown as an
/// html is fed in pieces (DocView::AppendHtml, sfr_render_03b.cpp:277: the appended string alone goes through
/// StyledText::ParseHtml, :287). TextNode kept the styles of one feed in the array it was about to replace: growing it
/// copied only the chars of the earlier feeds, so the chars of this feed written before the array was full read back plain
/// (the head of a long help notice drew white and without its bold; Godot-free so it can be tested).
/// </summary>
public static class SparseRuns
{
    /// <summary>Stores <paramref name="value"/> at <paramref name="index"/>; a longer array (at least <paramref name="capacity"/>,
    /// at least 16) takes over every slot below <paramref name="index"/> when the array is full.</summary>
    public static void Set<T>(ref T[] runs, int index, T value, int capacity)
    {
        if (runs == null || index >= runs.Length)
        {
            var grown = new T[Math.Max(Math.Max(capacity, index + 1), 16)];
            if (runs != null) Array.Copy(runs, grown, Math.Min(index, runs.Length));
            runs = grown;
        }
        runs[index] = value;
    }
}
