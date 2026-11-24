using Godot;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;

namespace Ms2Godot.Net;

/// <summary>
/// RecallUser: CSceneStateGame::OnRecallUser @0x141731c10 (scenestategame.cpp:4733) and the confirm box's answer
/// (UIMessageBox::HandleRecallconfirmButton, uimessagebox.cpp:1470). The state is <see cref="RecallUserBox"/>.
/// </summary>
public partial class LiveSession
{
    RecallUserBox recall;

    /// <summary>The one pending recall invite and its sends; the notices are chat lines of kind 0x14 (FUN_1412751b0).</summary>
    public RecallUserBox Recall => recall ??= new RecallUserBox
    {
        Send = Send,
        Notice = text => { if (!string.IsNullOrEmpty(text)) Ms2Godot.Ui.HudScene.ChatLog.AddSystemLine(text, TalkKind.SystemNotice); },
    };

    void OnRecallUser(RecallUserPacket packet)
    {
        GD.Print($"live: recall user {packet}");
        Recall.Apply(packet);
    }

    /// <summary>The box's answer (button 0 accepts); nothing is sent when no invite is pending.</summary>
    public void AnswerRecallUser(bool accept) => Recall.Answer(accept);
}
