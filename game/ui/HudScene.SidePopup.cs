using Godot;
using Ms2.Quest;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The side popup talk (UISidePopupTalk, 0x4f57, uisidepopuptalk_1.cpp): the Trigger type 0x13 packet's popup
/// (CTriggerService::OnSidePopupTalk, triggerservice.cpp:683-705: the text through CServerString's trigger group), whose movie
/// loads with the first popup and stays for the HUD's life.
/// </summary>
public partial class HudScene
{
    SidePopupTalkController sidePopup;     // kept across relayouts, as the client's object
    MovieDialog sidePopupMovie;
    Action unwatchSidePopup;

    void SidePopupPane()
    {
        sidePopup ??= new SidePopupTalkController
        {
            LoadRequested = LoadSidePopup,
            ReplaceObjectAndValueWords = SidePopupWords(),
            SoundState = () => (GameOptions.Current.SoundOption.MuteAll, GameOptions.Current.SoundOption.MovieVolume),
            MinimapAndQuestGuideVisible = SetMinimapAndQuestGuideVisible,
            PlayVoice = voice =>
            {
                // CEffectService::PlayVoiceAt at the local player (pPlayer->vfunc_18, its position)
                if (fieldScene?.GetNodeOrNull<Node3D>("Player") is { } me) World.NpcTalk.PlayVoiceAt(voice, me.GlobalPosition);
                else NotApplied.Print($"side popup: voice {voice} has no player to play at");
                return 0;
            },
            SystemSound = key => { if (FieldAudio.Instance?.PlayUi(key) != true) NotApplied.Print($"side popup: system sound {key} is not played"); },
            ServerTime = () => Net.LiveSession.Instance?.ServerTime ?? 0,
            Unapplied = name => NotApplied.Print($"side popup: {name} is not applied (the field war and wedding hall sends are not ported)"),
        };
        unwatchSidePopup?.Invoke();
        unwatchSidePopup = null;
        if (Net.LiveSession.Instance is { } live)
        {
            Action<Ms2.Net.Packets.TriggerSidePopup> on = p => sidePopup.ShowPopup(new SidePopupTalkParam(p.Kind, p.Name, p.Icon,
                p.NpcOrDuration, p.Sound, p.Script, hudModel.Tables.TriggerStrings.GetString(p.Text)));
            live.SidePopupReceived += on;
            unwatchSidePopup = () => live.SidePopupReceived -= on;
        }
    }

    /// <summary>CReservedWordTable::ReplaceObjectAndValueWords over the quest table's object words and the local player's name.</summary>
    Func<string, string> SidePopupWords()
    {
        var langs = new Ms2.Formats.World.GameStrings(assets.Packs, assets.Language).Languages;
        var words = (questTable ??= QuestTable.Load(assets.Packs, langs, data.Locale, f => !data.FeaturesOff.Contains(f))).Words;
        var reserved = hudModel.Tables.ReservedWords;
        return t => reserved.ReplaceValueWords(words.ReplaceObjectWords(t), hudModel.HasLocalPlayer ? hudModel.Player.Name : null);
    }

    /// <summary>UIHeadUpDisplay::vfunc_2: the movie loaded (its load complete adds the waiting popups).</summary>
    void LoadSidePopup()
    {
        if (sidePopupMovie != null) return;
        sidePopupMovie = new MovieDialog(assets, sidePopup, hudModel, ScreenMode.PixelSize(GetWindow()));
        uiRoot.AddChild(sidePopupMovie);
    }

    /// <summary>SetMinimapAndQuestGuideVisible (@0x140e886f0): the minimap (0x4e29) and quest guide (0x4e43) entities' visible
    /// (UISwfEntity::vfunc_8), each set only when it differs.</summary>
    void SetMinimapAndQuestGuideVisible(bool visible)
    {
        foreach (var m in new[] { minimap, questGuide })
        {
            if (m == null || !IsInstanceValid(m) || m.Host.Entity == null) continue;
            if (Ms2.Avm2.Rt.ToBoolean(m.Host.Get(m.Host.Entity, "visible")) != visible) m.Host.SetVisible(m.Host.Entity, visible);
        }
    }

    void SidePopupUnwatch()
    {
        unwatchSidePopup?.Invoke();
        unwatchSidePopup = null;
        sidePopup?.Unload();
    }
}
