using Ms2.Formats.Text;
using Ms2.Net.Packets;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// CGuildManager::FUN_1413bdbb0 (guildmanager.cpp:1587) after its checks: a yes / no box (type 1) of common string 0x38c with
/// the inviter's name and the guild name, the tag "guildinvite" (s_strGuildInvite), no listener, a 20 s timer
/// (SetTimer(0x14, 1, common 0x2b8 s_msg_popup_time)), not blocking (FUN_141a02460(1)), modaless, not answered by Enter
/// (FUN_141a024a0(0)) and shown now (FUN_140d73c60). Its buttons go to <see cref="MessageBoxController"/>'s
/// HandleGuildinviteButton. The refuse option, the PvP point and the sound after the box are GuildBook's.
/// </summary>
public static class GuildInviteBox
{
    /// <summary>s_strGuildInvite (@0x1400f4830); HandlePlainTag compares the whole tag.</summary>
    public const string Tag = "guildinvite";

    /// <summary>Shows the invite's box and returns it.</summary>
    public static MessageBoxData Show(GuildInviteInfo invite)
    {
        var box = MessageBoxQueue.Create(1, ClientFormat.Format(Common(0x38c), invite.Inviter ?? "", invite.GuildName ?? ""), null, Tag);
        box.SetTimer(0x14, 1, MessageBoxData.Common(0x2b8));    // 20 s, the countdown line s_msg_popup_time
        box.NoInputBlock = true;
        box.Modaless = true;
        box.EnterAllowed = false;
        MessageBoxQueue.Show(box);
        return box;
    }

    /// <summary>GetStringCommon(id) (CommonStringIds, then the box's common string table).</summary>
    static string Common(int id) => CommonStringIds.Key(id) is { } k ? MessageBoxData.CommonByKey(k) ?? "" : "";
}
