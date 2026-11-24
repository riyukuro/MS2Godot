using System;
using System.Collections.Generic;

namespace Ms2.Ui;

/// <summary>
/// A space never wraps a line: the renderer's overflow test skips a space (ParagraphFormatter::CheckWordWrap,
/// sfr_render_03b.cpp:1918, `!isSpace && NewLineWidth + ... > TextRectWidth`), so the space after a line's last word hangs past
/// the text rect and the word still fits. The engine's line breaker counts that space, so a line that ends exactly within
/// a space's width of the rect was broken one word early, and a field drew a line more than the script's numLines (the box
/// built from textHeight clipped the last line). This moves a first word back onto the line before it when it fits without
/// the space that follows it (Godot-free so it can be tested; TextNode feeds it the shaped paragraph's breaks).
/// </summary>
public static class HangingSpaces
{
    /// <param name="breaks">The paragraph's soft line breaks as the engine returns them: start, end of each line, in the shaped
    /// text's own indices; a line ends after its trailing spaces.</param>
    /// <param name="isSpace">Whether the char at an index is a space.</param>
    /// <param name="width">The shaped width of the chars from index a up to b.</param>
    /// <param name="limit">The text rect's width.</param>
    public static int[] Fit(int[] breaks, Func<int, bool> isSpace, Func<int, int, double> width, double limit)
    {
        if (breaks.Length < 4) return breaks;
        var s = new List<int>(breaks.Length / 2);
        var e = new List<int>(breaks.Length / 2);
        for (int i = 0; i + 1 < breaks.Length; i += 2) { s.Add(breaks[i]); e.Add(breaks[i + 1]); }
        bool changed = false;
        for (int i = 0; i + 1 < s.Count;)
        {
            // the engine's line ends after its spaces, or before them when it trims the edge spaces (a gap of spaces to the next line)
            bool gap = e[i] < s[i + 1];
            bool spaces = e[i] > s[i] && (gap ? AllSpaces(isSpace, e[i], s[i + 1]) : isSpace(e[i] - 1) && e[i] == s[i + 1]);
            if (spaces)
            {
                int p = s[i + 1];
                while (p < e[i + 1] && !isSpace(p)) p++;
                if (p > s[i + 1] && width(s[i], p) <= limit)
                {
                    int q = p;
                    while (q < e[i + 1] && isSpace(q)) q++;
                    e[i] = gap ? p : q;
                    changed = true;
                    if (q >= e[i + 1]) { s.RemoveAt(i + 1); e.RemoveAt(i + 1); }
                    else s[i + 1] = q;
                    continue;
                }
            }
            i++;
        }
        if (!changed) return breaks;
        var res = new int[s.Count * 2];
        for (int i = 0; i < s.Count; i++) { res[i * 2] = s[i]; res[i * 2 + 1] = e[i]; }
        return res;
    }

    static bool AllSpaces(Func<int, bool> isSpace, int a, int b)
    {
        for (int i = a; i < b; i++) if (!isSpace(i)) return false;
        return true;
    }
}
