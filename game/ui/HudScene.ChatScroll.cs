using Godot;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The <c>--uigen-open chatscroll</c> shot: the chat window with more lines than fit, then scrolled up with the wheel over the
/// log (TextField::OnMouseWheelEvent, sfr_gfx_03e.cpp:915) so the scroll bar thumb leaves the end. No server.
/// <c>--uigen-open chatresize</c> fills the log to its limit and drags the window's resize handle, printing per move what
/// the ActionScript handler (setSize and the redraws it asks for) and the display-list sync spent.
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
            return;
        }
        if (openShot == "chatend") return;      // the log as it stands at its end
        var h = m.Host;
        if (h.Child("m_textOutput") is not AsDisplayObject o) return;
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
            GD.Print($"CHATRESIZE moves={resizeMoves} as3={resizeAs3 / resizeMoves:F2} ms/move sync={resizeSync / resizeMoves:F2} ms/move objectsEnd-Start={Performance.GetMonitor(Performance.Monitor.ObjectCount) - resizeObjects0} nodesEnd-Start={Performance.GetMonitor(Performance.Monitor.ObjectNodeCount) - resizeNodes0}");
            foreach (var row in resizeRows) GD.Print("CHATRESIZE " + row);
        }
    }

    double resizeX, resizeY, resizeSyncPrev;
}
