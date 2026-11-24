using Ms2.Formats.Text;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// CNetworkService::OnBuddyEmote mode 0's box (networkservice.cpp:1124-1127): a yes / no box (type 1) of common string 0xce5
/// with the asker's name and the emotion's name (CStringTable::FUN_141811980(4, emotion), the skill name), the tag
/// "coupleEmotion" (s_coupleEmotion), no listener; its arguments the emotion, the asker's character id and name; the timer
/// of the constant CoupleEmotionRequestDuration (SetTimer(n, 1, common 0x2b8 s_msg_popup_time)), not blocking
/// (FUN_141a02460(1)), modaless, not answered by Enter (FUN_141a024a0(0)) and shown now (FUN_140d73c60). Its buttons go to
/// <see cref="MessageBoxController"/>'s HandleCoupleEmotionButton.
/// </summary>
public static class CoupleEmotionInviteBox
{
    /// <summary>s_143af3d28; HandlePlainTag compares the whole tag (uimessagebox.cpp:1067).</summary>
    public const string Tag = "coupleEmotion";

    /// <summary>The constant's default when the table lacks it.</summary>
    public const int DefaultDuration = 20;

    public static MessageBoxData Show(int emotion, long characterId, string name, string emotionName, int duration)
    {
        var box = MessageBoxQueue.Create(1, ClientFormat.Format(Common(0xce5), name ?? "", emotionName ?? ""), null, Tag);
        box.Append(unchecked((uint)emotion));
        box.Append(characterId);
        box.AddStringArg(name ?? "");
        box.SetTimer(duration, 1, MessageBoxData.Common(0x2b8));
        box.NoInputBlock = true;
        box.Modaless = true;
        box.EnterAllowed = false;
        MessageBoxQueue.Show(box);
        return box;
    }

    static string Common(int id) => CommonStringIds.Key(id) is { } k ? MessageBoxData.CommonByKey(k) ?? "" : "";
}
