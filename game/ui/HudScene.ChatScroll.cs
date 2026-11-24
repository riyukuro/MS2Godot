using Godot;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The <c>--uigen-open chatscroll</c> shot: the chat window with more lines than fit, then scrolled up with the wheel over the
/// log (TextField::OnMouseWheelEvent, sfr_gfx_03e.cpp:915) so the scroll bar thumb leaves the end. No server.
/// <c>--uigen-open chatend</c> is the log at its end with a super chat line in it: the shot prints, after the view synced, the script's
/// scroll range and where the super chat line is against the block TextAreaEx.updateBackgroundColor made for it.
/// <c>--uigen-open chatresize</c> fills the log to its limit and drags the window's resize handle, printing per move what
/// the ActionScript handler (setSize and the redraws it asks for) and the display-list sync spent.
/// <c>--uigen-open chatappend</c> fills the log to its limit, then adds one line a frame and prints what a frame cost in
/// main-thread allocation and time against the quiet frames after it (what one appended line costs).
/// <c>--uigen-open chathistory</c> sends three lines with real Return key events (the line's text set between them), then Up and Down
/// key events, and prints the input's text after each (the input history, FUN_141a13c20).
/// </summary>
public partial class HudScene
{
    /// <summary>Frame 200 fills the log; frame 215 puts the pointer over it and turns the wheel up.</summary>
    void ChatScrollShot(int step)
    {
        if (messengers.Count == 0) return;
        var (m, _) = messengers[0];
        if (step == 0)
        {
            // every third line is long enough to wrap in the window, so the script's and the label's wraps can differ
            for (int i = 1; i <= 60; i++) ChatLog.AddSystemLine("Chat line " + i + " of 60, long enough to be a line of its own in the log." + (i % 3 == 0 ? " It goes on and on, so that it needs a second and a third line in the window." : ""), TalkKind.SystemNotice);
            // a user line, a super chat (type 0x10: TEXTFORMAT indent/tabStops, whose line TextAreaEx.updateBackgroundColor colours from
            // getTextFormat) and a user line again
            ChatLog.FeatureEnabled = _ => false;      // the KMS2 data's chat strings are the ones with the gender argument
            foreach (int type in new[] { 0, 0x10, 0 })
                ChatLog.OnUserChat(new Ms2.Net.Packets.ChatMessage(0, 0, "Hero", false, 0, type == 0x10 ? "Super chat line" : "Plain line", type, false, 0, "", 0, 11000001, 0));
            ChatLog.FeatureEnabled = null;
            foreach (var l in ChatLog.Logs.TakeLast(4)) GD.Print("chatlog tail kind " + l.Kind + ": " + l.Msg);
            return;
        }
        var h = m.Host;
        if (h.Child("m_textOutput") is not AsDisplayObject o) return;
        var tfld = h.Get(o, "textField");
        if (step == 2)
        {
            // after the view synced: the script's range is the one drawn, and the block TextAreaEx.updateBackgroundColor made sits on the
            // super chat line (its place below the first shown line, in the field's view)
            var bg = h.Child("m_textBgColor") as AsContainer;
            int nl = Rt.ToInt32(h.Get(tfld, "numLines")), superLine = -1;
            for (int i = 0; i < nl && superLine < 0; i++)
            {
                int off = Rt.ToInt32(h.Invoke(tfld, "getLineOffset", i)), len = Rt.ToInt32(h.Invoke(tfld, "getLineLength", i));
                if (h.Invoke(tfld, "getTextFormat", off, off + len - 1) is { } tf && Rt.ToNumber(h.Get(tf, "indent")) > 0 && h.Get(tf, "tabStops") != null) superLine = i;
            }
            string row = "none";
            if (superLine >= 0)
            {
                var cb = h.Invoke(tfld, "getCharBoundaries", Rt.ToInt32(h.Invoke(tfld, "getLineOffset", superLine)));
                var first = h.Invoke(tfld, "getCharBoundaries", Rt.ToInt32(h.Invoke(tfld, "getLineOffset", Rt.ToInt32(h.Get(tfld, "scrollV")) - 1)));
                row = $"line {superLine} y={Rt.ToNumber(h.Get(cb, "y")) - Rt.ToNumber(h.Get(first, "y")) + 2:F1} h={Rt.ToString(h.Get(h.Invoke(tfld, "getLineMetrics", superLine), "height"))}";
            }
            string blocks = bg == null ? "none" : string.Join("; ", bg.Children.Take(4).Select(k => { var kb = k.GetBounds(bg); return $"y={kb.Y:F1} h={kb.H:F1} w={kb.W:F1}"; }));
            GD.Print($"CHATSUPER scrollV={Rt.ToString(h.Get(tfld, "scrollV"))} maxScrollV={Rt.ToString(h.Get(tfld, "maxScrollV"))} bottomScrollV={Rt.ToString(h.Get(tfld, "bottomScrollV"))} numLines={nl} superRow [{row}] blocks {bg?.Children.Count ?? 0} [{blocks}]");
            return;
        }
        // the super chat consumer: TextAreaEx.updateBackgroundColor fills m_textBgColor with a tinted block per super chat line
        if (openShot is "chatscroll" or "chatend") h.Invoke(o, "updateBackgroundColor");
        if (openShot == "chatend") return;      // the log as it stands at its end
        var b = o.GetBounds(o);
        var (x, y) = o.LocalToGlobal(b.X + b.W / 2, b.Y + b.H / 2);
        h.Player.MouseMove(x, y);
        for (int i = 0; i < 5; i++) h.Player.Wheel(3);
    }

    double resizeAs3, resizeSync; int resizeMoves; double resizeObjects0, resizeNodes0;
    readonly List<string> resizeRows = new();

    /// <summary>One frame of the drag: 215 presses the handle, 216-275 move it out and back, 280 prints the totals.</summary>
    void ChatResizeShot(int frames)
    {
        if (messengers.Count == 0) return;
        var (m, _) = messengers[0];
        var h = m.Host;
        if (frames is >= 150 and < 170)      // the log's 500 lines, 25 a frame
        {
            for (int i = 1; i <= 25; i++) ChatLog.AddSystemLine("Chat line " + ((frames - 150) * 25 + i) + " of 500, long enough to be a line of its own in the log.", TalkKind.SystemNotice);
            return;
        }
        if (frames < 215) return;
        if (h.Child("m_resize") is not AsDisplayObject r) return;
        if (frames == 215)
        {
            var b = r.GetBounds(r);
            var (x, y) = r.LocalToGlobal(b.X + b.W / 2, b.Y + b.H / 2);
            resizeX = x; resizeY = y;
            h.Player.MouseMove(x, y);
            h.Player.MouseButton(true);
            return;
        }
        if (frames is >= 216 and <= 275)
        {
            int k = frames - 216;
            int d = k < 30 ? k : 60 - k;
            double syncFrame = As3View.SyncMs - resizeSyncPrev;      // the previous frame's display-list sync
            resizeSyncPrev = As3View.SyncMs;
            var sw = System.Diagnostics.Stopwatch.GetTimestamp();
            h.Player.MouseMove(resizeX + d * 4, resizeY - d * 2);
            double as3 = System.Diagnostics.Stopwatch.GetElapsedTime(sw).TotalMilliseconds;
            if (k == 0) { resizeObjects0 = Performance.GetMonitor(Performance.Monitor.ObjectCount); resizeNodes0 = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount); }
            else { resizeAs3 += as3; resizeSync += syncFrame; resizeMoves++; }
            resizeRows.Add($"move {k}: as3 {as3:F2} ms, sync of the frame before {syncFrame:F2} ms");
            return;
        }
        if (frames == 280)
        {
            h.Player.MouseButton(false);
            GD.Print($"CHATRESIZE moves={resizeMoves} as3={resizeAs3 / Math.Max(1, resizeMoves):F2} ms/move sync={resizeSync / Math.Max(1, resizeMoves):F2} ms/move objectsEnd-Start={Performance.GetMonitor(Performance.Monitor.ObjectCount) - resizeObjects0} nodesEnd-Start={Performance.GetMonitor(Performance.Monitor.ObjectNodeCount) - resizeNodes0}");
            foreach (var row in resizeRows) GD.Print("CHATRESIZE " + row);
        }
    }

    double resizeX, resizeY, resizeSyncPrev;

    /// <summary>One key press and release through the viewport, as the keyboard sends it.</summary>
    void ChatKeyEvent(Key key)
    {
        foreach (bool down in new[] { true, false })
            Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = down });
    }

    /// <summary><c>--uigen-open chattab:N</c>: frame 160 Return focuses the input, from 165 every 5 frames a real Tab key event
    /// (N of them) and two frames later the print of the selected target row; the frame 300 shot shows the last one.</summary>
    void ChatTabShot(int frames, int tabs)
    {
        if (messengers.Count == 0) return;
        var (m, c) = messengers[0];
        var h = m.Host;
        if (h.Child("m_subject") is not AsDisplayObject subject) return;
        void Print(string what)
        {
            int sel = (int)Rt.ToNumber(h.Get(subject, "selectedIndex"));
            var row = h.Get(h.Get(subject, "dataProvider"), sel.ToString()) as AsObject;
            GD.Print($"CHATTAB {what}: selectedIndex={sel} label=\"{(row == null ? "" : Rt.ToString(h.Get(row, "label")))}\" focused={c.InputActive}");
        }
        if (frames == 150) Print("start");
        if (frames == 160) { ChatKeyEvent(Key.Enter); return; }
        if (frames == 163) { Print("focused"); return; }
        if (frames < 165 || (frames - 165) % 5 != 0 && (frames - 167) % 5 != 0) return;
        int n = (frames - 165) / 5;
        if ((frames - 165) % 5 == 0) { if (n < tabs) ChatKeyEvent(Key.Tab); }
        else if (n < tabs) Print($"tab {n + 1}");
    }

    /// <summary><c>--uigen-open chathistory</c>: frames 160, 170 and 180 (+4) send "alpha", "beta" and "gamma" (Return focuses the input,
    /// the text is set, Return sends it); from 200 Up, Up, Up, Down, Down, Down and a focus out with Up print the input's text.</summary>
    void ChatHistoryShot(int frames)
    {
        if (messengers.Count == 0) return;
        var (m, c) = messengers[0];
        var h = m.Host;
        if (h.Child("m_textInput") is not AsDisplayObject input) return;
        void Print(string what) => GD.Print($"CHATHISTORY {what}: text=\"{Rt.ToString(h.Get(input, "text"))}\" focused={Rt.ToString(h.Get(input, "focused"))} history={string.Join("|", c.Browser.History.Select(e => e.Text))} sent={ChatLog.Logs.Count}");
        // the key events reach the scene on the next frame, so a text is set and a state read two frames after the key
        if (frames is 160 or 170 or 180) { ChatKeyEvent(Key.Enter); return; }
        if (frames is 162 or 172 or 182) { h.Set(h.Get(input, "textField"), "text", frames == 162 ? "alpha" : frames == 172 ? "beta" : "gamma"); return; }
        if (frames is 164 or 174 or 184) { ChatKeyEvent(Key.Enter); return; }
        if (frames is 166 or 176 or 186) { Print("sent"); return; }
        if (frames == 200) { ChatKeyEvent(Key.Enter); return; }
        if (frames == 202) { Print("focused"); return; }
        if (frames is 205 or 210 or 215) { ChatKeyEvent(Key.Up); return; }
        if (frames is 207 or 212 or 217) { Print("up"); return; }
        if (frames is 220 or 225 or 230) { ChatKeyEvent(Key.Down); return; }
        if (frames is 222 or 227 or 232) { Print("down"); return; }
        if (frames == 235) { c.OnCallback("eventFocusOut", System.Array.Empty<object>()); ChatKeyEvent(Key.Up); return; }
        if (frames == 237) Print("focus out, up");
    }

    /// <summary>
    /// <c>--uigen-open chatlog:top|mid|bot</c>: the log filled to its 500 lines (wrapping lines, user lines and a super chat line among
    /// them), scrolled to its top, middle or end by the script's scrollV; frame 280 prints what the field and its label show (the
    /// script's range and the lines the label draws, as a hash of their text), the shot is the frame after.
    /// </summary>
    void ChatLogShot(int frames)
    {
        if (messengers.Count == 0) return;
        var (m, _) = messengers[0];
        if (frames is >= 150 and < 170)
        {
            for (int i = 1; i <= 25; i++)
            {
                int n = (frames - 150) * 25 + i;
                if (n % 97 == 0)
                {
                    ChatLog.FeatureEnabled = _ => false;
                    ChatLog.OnUserChat(new Ms2.Net.Packets.ChatMessage(0, 0, "Hero", false, 0, n == 291 ? "Super chat line" : "Plain line " + n, n == 291 ? 0x10 : 0, false, 0, "", 0, 11000001, 0));
                    ChatLog.FeatureEnabled = null;
                }
                else ChatLog.AddSystemLine("Chat line " + n + " of 500, long enough to be a line of its own in the log." + (n % 3 == 0 ? " It goes on and on, so that it needs a second and a third line in the window." : ""), TalkKind.SystemNotice);
            }
            return;
        }
        var h = m.Host;
        if (h.Child("m_textOutput") is not AsDisplayObject o) return;
        var tfld = h.Get(o, "textField");
        if (frames == 200)
        {
            int max = Rt.ToInt32(h.Get(tfld, "maxScrollV"));
            string pos = openShot.Split(':').ElementAtOrDefault(1) ?? "bot";
            h.Set(tfld, "scrollV", pos == "top" ? 1 : pos == "mid" ? max / 2 : max);
            return;
        }
        if (frames == 280)
        {
            var log = LargestText(this);
            var shown = log?.ShownLines();
            string text = shown == null ? "" : string.Join("|", shown.Select(l => l.Text.TrimEnd('\n')));
            GD.Print($"CHATLOG {openShot} scrollV={Rt.ToString(h.Get(tfld, "scrollV"))} maxScrollV={Rt.ToString(h.Get(tfld, "maxScrollV"))} bottomScrollV={Rt.ToString(h.Get(tfld, "bottomScrollV"))} numLines={Rt.ToString(h.Get(tfld, "numLines"))} firstLine={log?.FirstLine} shown={shown?.Count} hash={text.Aggregate(17u, (a, c) => a * 31 + c):X} [{text.Substring(0, Math.Min(text.Length, 400))}]");
            if (log != null) GD.Print($"CHATLOG layoutDigest={log.LayoutDigest(false)}");
        }
    }

    /// <summary>
    /// <c>--uigen-open chatgm[:lines above the end[:log lines before]]</c>: a log of 500 (or the given number of) system lines, then the server's command help as one notice (its "\n"-joined,
    /// html-encoded lines with "word:" in &lt;b&gt;), scrolled to the end; frame 280 prints the colour of the text node's runs
    /// at each help line (the script's getTextFormat) and the label's.
    /// </summary>
    /// <summary>The live driver's "@chatup:N": the first chat window's log scrolled N lines up from the bottom.</summary>
    public void ScrollChatUp(int up)
    {
        if (messengers.Count == 0) return;
        var h = messengers[0].Side.Host;
        if (h.Child("m_textOutput") is not AsDisplayObject o) return;
        var tfld = h.Get(o, "textField");
        h.Set(tfld, "scrollV", Math.Max(1, Rt.ToInt32(h.Get(tfld, "maxScrollV")) - up));
    }

    void ChatGmShot(int frames)
    {
        if (messengers.Count == 0) return;
        var (m, _) = messengers[0];
        int total = int.TryParse(openShot.Split(':').ElementAtOrDefault(2), out var tl) ? tl : 500;
        if (frames is >= 150 and < 170)
        {
            for (int i = 1; i <= 25; i++)
                if ((frames - 150) * 25 + i <= total)
                    ChatLog.AddSystemLine("Chat line " + ((frames - 150) * 25 + i) + " of " + total + ", long enough to be a line of its own in the log.",
                        new[] { TalkKind.SystemNotice, TalkKind.Meso, TalkKind.Item, TalkKind.Merat, TalkKind.Token, TalkKind.Friend, TalkKind.SystemEvent, TalkKind.TopLayer }[(frames - 150) * 25 + i & 7]);   // each kind's own colour
            return;
        }
        var h = m.Host;
        if (h.Child("m_textOutput") is not AsDisplayObject o) return;
        var tfld = h.Get(o, "textField");
        if (frames == 170)
        {
            string[] names = { "perm", "alert", "ban", "baninfo", "unban", "banword", "buff", "movefield", "addfield", "clearinven",
                "toggleinvisiblefromnpc", "toggledeathpower", "heal", "level", "item", "kick", "mute", "notice", "warp", "spawn", "killall", "gold", "exp", "skill" };
            // GameConsole.GameOutputStreamWriter: HtmlEncode, "word:" in <b>, runs of spaces folded to two
            var sb = new System.Text.StringBuilder("Commands:\n");
            foreach (var n in names) sb.Append($"  {n}  Do the {n} thing (the client's GM window). Usage: {n} <character> [-t type]\n");
            string enc = System.Net.WebUtility.HtmlEncode(sb.ToString().TrimEnd('\n'));
            enc = System.Text.RegularExpressions.Regex.Replace(enc, "(\\w+:)", "<b>$1</b>");
            enc = System.Text.RegularExpressions.Regex.Replace(enc, " {2,}", "  ");
            string wire = System.Environment.GetEnvironmentVariable("MS2_CHATGM_TEXT");   // a captured notice text, sent as it came
            if (!string.IsNullOrEmpty(wire) && System.IO.File.Exists(wire)) enc = System.IO.File.ReadAllText(wire);
            ChatLog.AddSystemLine(enc, TalkKind.SystemNotice);
            return;
        }
        if (frames == 200) { int up = int.TryParse(openShot.Split(':').ElementAtOrDefault(1), out var u) ? u : 0; h.Set(tfld, "scrollV", Math.Max(1, Rt.ToInt32(h.Get(tfld, "maxScrollV")) - up)); return; }
        if (frames == 280)
        {
            string plain = Rt.ToString(h.Get(tfld, "text"));
            int at = 0, idx = 0;
            var cols = new List<string>();
            foreach (string para in plain.Split('\r', '\n'))
            {
                if (idx >= total && para.Length > 0)
                    cols.Add(Rt.ToString(h.Get(h.Invoke(tfld, "getTextFormat", at, at + 1), "color")) + ":" + para.Substring(0, Math.Min(12, para.Length)));
                at += para.Length + 1;
                idx++;
            }
            if (LargestText(this) is { } tn)
            {
                var sb2 = new System.Text.StringBuilder();
                string tt = tn.Text; int pi = 0, cp0 = 0;
                foreach (string para in tt.Split('\n'))
                {
                    if (pi < 14 && para.Length > 0) { var st = tn.StyleAt(cp0 + Math.Min(3, para.Length - 1)); sb2.Append($"{(st.Color is Color c ? c.ToHtml(false) : "-")}{(st.Bold ? "b" : "")}:{para.Substring(0, Math.Min(8, para.Length))} | "); }
                    cp0 += para.Length + 1; pi++;
                }
                GD.Print($"CHATGM node whole={tn.SameAsWholeParse()} {sb2}");
            }
            GD.Print($"CHATGM scrollV={Rt.ToString(h.Get(tfld, "scrollV"))} max={Rt.ToString(h.Get(tfld, "maxScrollV"))} runs=[{string.Join(" | ", cols)}]");
        }
    }

    /// <summary>The text node under <paramref name="n"/> with the longest text (the chat log).</summary>
    static TextNode LargestText(Node n)
    {
        TextNode best = n as TextNode;
        for (int i = 0; i < n.GetChildCount(); i++)
        {
            var c = LargestText(n.GetChild(i));
            if (c != null && (best == null || c.Text.Length > best.Text.Length)) best = c;
        }
        return best;
    }

    long appendBytes0; ulong appendUsec0;
    readonly List<(bool Append, long Bytes, double Ms)> appendFrames = new();

    /// <summary>One frame of the append run: 150-169 fill the log, 200-239 add a line a frame (the log is at its limit, so each
    /// also drops the oldest), 250-289 add none, 290 prints the means. A frame's cost is the next call's reading less this one's.</summary>
    void ChatAppendShot(int frames)
    {
        if (messengers.Count == 0) return;
        if (frames is >= 150 and < 170)
        {
            for (int i = 1; i <= 25; i++) ChatLog.AddSystemLine("Chat line " + ((frames - 150) * 25 + i) + " of 500, long enough to be a line of its own in the log.", TalkKind.SystemNotice);
            return;
        }
        if (frames < 200) return;
        long bytes = GC.GetAllocatedBytesForCurrentThread(); ulong usec = Time.GetTicksUsec();
        if (frames is >= 201 and <= 240 or >= 251 and <= 290)
            appendFrames.Add((frames <= 240, bytes - appendBytes0, (usec - appendUsec0) / 1000.0));
        appendBytes0 = bytes; appendUsec0 = usec;
        if (frames is >= 200 and < 240)
            ChatLog.AddSystemLine("Chat line " + (501 + frames - 200) + " of 500+, one more line appended to a full log, long enough to wrap in the window.", TalkKind.SystemNotice);
        if (frames is 245 or 246)      // the lines as laid out from the kept paragraphs, and laid out anew
        {
            var log = LargestText(this);
            if (log != null) GD.Print($"CHATAPPEND digest {(frames == 245 ? "kept" : "fresh")}={log.LayoutDigest(frames == 246)} incrementalText={(frames == 245 ? log.SameAsWholeParse() : "-")}");
        }
        if (frames == 290)
        {
            var a = appendFrames.Where(f => f.Append).ToList(); var q = appendFrames.Where(f => !f.Append).ToList();
            double aB = a.Average(f => (double)f.Bytes), qB = q.Average(f => (double)f.Bytes), aM = a.Average(f => f.Ms), qM = q.Average(f => f.Ms);
            GD.Print($"CHATAPPEND appended frames={a.Count} bytes/frame={aB:F0} ms/frame={aM:F2}; quiet frames={q.Count} bytes/frame={qB:F0} ms/frame={qM:F2}; per appended line bytes={aB - qB:F0} ms={aM - qM:F2}");
            foreach (var f in a) GD.Print($"CHATAPPEND frame bytes={f.Bytes} ms={f.Ms:F2}");
        }
    }
}
