using Godot;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The <c>--uigen-open chatscroll</c> shot: the chat window with more lines than fit, then scrolled up with the wheel over the
/// log (TextField::OnMouseWheelEvent, sfr_gfx_03e.cpp:915) so the scroll bar thumb leaves the end. No server.
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
            for (int i = 1; i <= 60; i++) ChatLog.AddSystemLine("Chat line " + i + " of 60, long enough to be a line of its own in the log.", TalkKind.SystemNotice);
            return;
        }
        var h = m.Host;
        if (h.Child("m_textOutput") is not AsDisplayObject o) return;
        var b = o.GetBounds(o);
        var (x, y) = o.LocalToGlobal(b.X + b.W / 2, b.Y + b.H / 2);
        h.Player.MouseMove(x, y);
        for (int i = 0; i < 5; i++) h.Player.Wheel(3);
    }
}
