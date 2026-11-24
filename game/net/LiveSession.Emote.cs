using Godot;
using Ms2.Net.Packets;
using Ms2.Ui;

namespace Ms2Godot.Net;

/// <summary>
/// The local player's emotions (CSkillSystem m_vEmotions) from the Emote packet (0x1F, CSceneStateGame::OnEmote,
/// scenestategame.cpp:2533-2570).
/// The favorite send (mode 4) comes from the dynamic action dialog's onFavorite (uidynamicactiondialog.cpp:369), which
/// also refreshes on NotifyLearnEmotion (the Learned event).
/// The use (mode 2) comes from the own emotion cast (StateSkill::FUN_1417eaa50, stateskill.cpp:1904-1910, <see cref="SendEmote"/>).
/// The learn (mode 1) comes from the learn-emotion message box on an emotion item (UIMessageBox::HandleLearnEmotionButton,
/// uimessagebox.cpp:1451, through the inventory). NotifyLearnEmotion also reaches CGuideManager::OnLearnEmotion
/// (registered at guideentity_1b.cpp:187, body :1057 @0x14138eb90).
/// </summary>
public partial class LiveSession
{
    /// <summary>The learned emotions; live only.</summary>
    public readonly EmotionList Emotions = new();
    /// <summary>A common string message box (FUN_140d73bc0(id, 0, 0, "")); the HUD sets it.</summary>
    public System.Action<int> CommonBoxSink;
    bool emotionsHooked;

    /// <summary>An own Emote send (mode 2, the emotion use). False offline.</summary>
    public bool SendEmote(Ms2.Net.Wire.IOutPacket p)
    {
        if (game == null) return false;
        Send(p);
        if (LogPackets) GD.Print($"live: Emote sent, {p}");
        return true;
    }

    /// <summary>OnEmote returns without a local player (:2537-2538); the MyPC exists from the own OnAddUser on.</summary>
    void OnEmote(EmoteMessage m)
    {
        if (!emotionsHooked)
        {
            emotionsHooked = true;
            Emotions.Notice += id => CommonBoxSink?.Invoke(id);
            Emotions.Learned += id => guide?.OnLearnEmotion(id);     // NotifyLearnEmotion -> CGuideManager::OnLearnEmotion
        }
        if (selfObject < 0) return;
        if (Emotions.Apply(m, GameSession?.ServerTime ?? 0))
            GD.Print($"live: Emote {m.GetType().Name} ({Emotions.All.Count} emotions)");
    }
}
