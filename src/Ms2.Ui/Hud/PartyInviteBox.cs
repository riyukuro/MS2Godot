using Ms2.Formats.Text;
using Ms2.Net.Packets;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// CPartyManager::OnPartyInvite @0x14162c8b0 (partymanager.cpp:968) after its reads: the refuse option answers 0x14, a PvP
/// point 0x15; otherwise a yes / no box (type 1) of common string 0x2b7 (s_msg_party_invite) with the inviter's name, the tag
/// "partyinvite_{kind}", no listener, made modaless, not blocking (FUN_141a02460) and not answered by Enter (FUN_141a024a0),
/// shown now (FUN_140d73c60). Its buttons go to <see cref="MessageBoxController"/>'s HandlePartyinviteButton.
/// STILL-OPEN: the box's 20 s timer (SetTimer(0x14, 1, common 0x2b8 s_msg_popup_time)) and the timed-out answer 12 wait
/// for the box timer port.
/// </summary>
public static class PartyInviteBox
{
    /// <summary>DAT_143ae2c58 (s_143af3c28 in uimessagebox.cpp): the tag's prefix; HandlePlainTag matches it at the start.</summary>
    public const string Tag = "partyinvite";

    /// <summary>The invite's answer or its box; <paramref name="refuseParty"/> is the community option's 4th flag
    /// (Unk_PartyOptionPrefix::refuseParty, disablePartyRequest), <paramref name="pvpPoint"/> the local player's
    /// GetPvPInfo()->HasPoint(), <paramref name="answer"/> SendPartyMode2. Null when the invite was answered at once.</summary>
    public static MessageBoxData OnInvite(PartyInvite invite, bool refuseParty, bool pvpPoint, Action<int> answer)
    {
        if (refuseParty)
        {
            answer(PartyInviteAnswer.Refused);
            return null;
        }
        if (pvpPoint)
        {
            answer(PartyInviteAnswer.Busy);
            return null;
        }
        string key = $"{Tag}_{unchecked((uint)invite.Kind)}";
        var box = MessageBoxQueue.Create(1, ClientFormat.Format(Common(0x2b7), invite.PartyName ?? ""), null, key);
        box.NoInputBlock = true;
        box.Modaless = true;
        box.EnterAllowed = false;
        MessageBoxQueue.Show(box);
        return box;
    }

    /// <summary>GetStringCommon(id) (CommonStringIds, then the box's common string table).</summary>
    static string Common(int id) => CommonStringIds.Key(id) is { } k ? MessageBoxData.CommonByKey(k) ?? "" : "";
}
