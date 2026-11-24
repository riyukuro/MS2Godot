using Ms2.Formats.Text;
using Ms2.Net.Packets;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// The client's recall invitation: CSceneStateGame::OnRecallUser (@0x141731c10, scenestategame.cpp:4733) and the confirm box's
/// answer, UIMessageBox::HandleRecallconfirmButton (@0x1419fc640, uimessagebox.cpp:1470). The invite id is DAT_143ab2904, the
/// one pending invite; a second invite while one is pending is not shown but answered with c2s mode 7 (the name, flag 4).
/// Modes 5 to 7 are chat lines of kind 0x14 (FUN_1412751b0). One instance per session.
/// </summary>
public sealed class RecallUserBox
{
    /// <summary>s_143af3c30 (uimessagebox.cpp:915): the box's tag, compared whole.</summary>
    public const string Tag = "recallconfirm";

    /// <summary>DAT_143ab2904: the pending invite's id; 0 when none.</summary>
    public int Pending { get; private set; }

    /// <summary>The chat line (common string text) of a mode 5 to 7 packet.</summary>
    public Action<string> Notice;
    /// <summary>The sends: SendRecallUserText (the second invite) and SendRecallUserReply (the box's answer).</summary>
    public Action<Ms2.Net.Wire.IOutPacket> Send;

    /// <summary>OnRecallUser's body for one read packet; returns the confirm box a mode 4 opened, else null.</summary>
    public MessageBoxData Apply(RecallUserPacket packet)
    {
        switch (packet)
        {
            case RecallUserPacket.Invite invite:
                if (Pending != 0)
                {
                    Send?.Invoke(new RecallUserText(invite.Name, 4));
                    return null;
                }
                Pending = invite.InviteId;
                var box = MessageBoxQueue.Create(1, ClientFormat.Format(Common(0x902), invite.Name ?? ""), null, Tag);
                box.SetTimer(0x14, 1, MessageBoxData.Common(0x2b8));    // 20 s, the countdown line s_msg_popup_time
                box.NoInputBlock = true;
                box.Modaless = true;
                box.EnterAllowed = false;
                MessageBoxQueue.Show(box);
                return box;
            default:
                if (packet.NoticeStringId is var id and not 0)
                    Notice?.Invoke(ClientFormat.Format(Common(id), packet.Who ?? ""));
                return null;
        }
    }

    /// <summary>SendRecallUserReply: nothing when no invite is pending; else the pending id is cleared and sent with the answer
    /// (button 0, yes, accepts).</summary>
    public void Answer(bool accept)
    {
        if (Pending == 0) return;
        int id = Pending;
        Pending = 0;
        Send?.Invoke(new RecallUserReply(id, accept));
    }

    /// <summary>GetStringCommon(id) (CommonStringIds, then the box's common string table).</summary>
    static string Common(int id) => CommonStringIds.Key(id) is { } k ? MessageBoxData.CommonByKey(k) ?? "" : "";
}
