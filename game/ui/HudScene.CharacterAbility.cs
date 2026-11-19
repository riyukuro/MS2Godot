using Godot;
using Ms2.Formats.World;
using Ms2.Quest;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The character abilities: UICharacterAbilityDialog (0x4f0c, movie uicharacterabilitydialog) over CCharacterAbilityTable
/// (table/characterabilitycategory.xml and characterability.xml) and CCharacterAbilityManager's data. Live, the data is the
/// session's (the CharacterAbility packets, 0xDF) and the learn and reset requests go out (CharacterAbility 0x89); offline,
/// no ability is learned and the sends are logged. The C++ side lives with the HUD, as the client's dialog exists from the
/// game start: its level listener and the game menu's count work before the first open.
/// </summary>
public partial class HudScene
{
    CharacterAbilityController characterAbility;    // its C++ side, from the HUD's build
    MovieDialog characterAbilityMovie;              // the movie, from open until OnEndClose
    static CharacterAbilityTable characterAbilityTable;
    static readonly CharacterAbilityData offlineAbilities = new();
    Action unwatchCharacterAbility;

    bool CharacterAbilityOpen => characterAbilityMovie != null && characterAbility?.IsClosed == false;

    /// <summary>CCharacterAbilityTable, read once.</summary>
    CharacterAbilityTable CharacterAbilityTable() =>
        characterAbilityTable ??= Ms2.Ui.Hud.CharacterAbilityTable.Load(assets.Table("table/characterabilitycategory.xml"),
            assets.Table("table/characterability.xml"), e => FeatureElements.IsAvailable(assets.Packs, e),
            key => hudModel.Tables.Common(key));

    /// <summary>The quest tables for the dialog's disabled text (GetQuestData, GetQuestName, GetChapterName).</summary>
    QuestTable AbilityQuestTable() =>
        questTable ??= QuestTable.Load(assets.Packs, new GameStrings(assets.Packs, assets.Language).Languages, data.Locale,
            f => !data.FeaturesOff.Contains(f));

    /// <summary>
    /// The dialog's C++ side and its listeners, at the HUD's build: the level change (NotifyChangeMyLevel, the dialog's
    /// OnChangeMyLevel and the game menu's vfunc_oa0_9) and, live, the CharacterAbility packets: a load rebuilds the open
    /// dialog's table, a learn plays its LearnAbility (both also redo the game menu's mark, FUN_141938850).
    /// </summary>
    void CharacterAbility()
    {
        unwatchCharacterAbility?.Invoke();
        var live = Ms2Godot.Net.LiveSession.Instance;
        var table = CharacterAbilityTable();
        if (live != null) live.CharacterAbilityTable = table;
        var abilities = live?.CharacterAbility ?? offlineAbilities;
        Func<int, bool> rewarded = id => live?.Quests.Get(id)?.State == 2;
        if (characterAbility == null)
        {
            var c = characterAbility = new CharacterAbilityController
            {
                Table = table,
                Data = abilities,
                IsQuestRewarded = rewarded,
                ServerTime = () => live?.GameSession?.ServerTime ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Quest = id => AbilityQuestTable().Quest(id),
                QuestName = id => AbilityQuestTable().QuestName(id),
                ChapterName = ch => AbilityQuestTable().ChapterText(ch)?.Name ?? "",
            };
            c.SendLearn += id =>
            {
                if (live != null) live.SendTalk(new Ms2.Net.Packets.CharacterAbilityLearnRequest(id));
                else GD.Print($"character ability: learn {id} not sent (offline)");
            };
            c.SendReset += () =>
            {
                if (live != null) live.SendTalk(new Ms2.Net.Packets.CharacterAbilityResetRequest());
                else GD.Print("character ability: reset not sent (offline)");
            };
            c.SystemNotice += key => SystemNoticeText(hudModel.Tables.Common(key));
            c.MenuNotifyCount += n => gameMenu?.UpdateNotifyCount(GameMenuController.CharacterAbilityUiId, n);
            c.Unloaded += () =>
            {
                characterAbilityMovie?.Host.Unload();
                characterAbilityMovie?.QueueFree();
                characterAbilityMovie = null;
            };
        }
        if (gameMenu != null)
        {
            gameMenu.AbilityTable = table;
            gameMenu.Abilities = abilities;
            gameMenu.IsQuestRewarded = rewarded;
        }
        Action<StatusEvent> onStatus = e =>
        {
            if (e != StatusEvent.Level) return;
            characterAbility.OnChangeMyLevel((ushort)hudModel.Player.Level);
            gameMenu?.OnChangeMyLevel();
        };
        hudModel.Changed += onStatus;
        Action<Ms2.Net.Packets.CharacterAbilityMessage> onPacket = m =>
        {
            // the dialog's methods need its movie (m_30), which only an open dialog has here
            if (m is Ms2.Net.Packets.CharacterAbilityMessage.Loaded) { if (CharacterAbilityOpen) characterAbility.BuildAbilityTable(); }
            else if (m is Ms2.Net.Packets.CharacterAbilityMessage.Learned l && table.GetAbility(l.Id) is { } a && CharacterAbilityOpen)
                characterAbility.OnAbilityLearned(a);
            gameMenu?.UpdateCharacterAbilityNotify();
        };
        if (live != null) live.CharacterAbilityReceived += onPacket;
        unwatchCharacterAbility = () =>
        {
            hudModel.Changed -= onStatus;
            if (live != null) live.CharacterAbilityReceived -= onPacket;
        };
    }

    /// <summary>CUIService::ToggleDialog(0x4f0c): open when closed, else vfunc_67.</summary>
    void ToggleCharacterAbility()
    {
        if (CharacterAbilityOpen) characterAbility.Close();
        else OpenCharacterAbility();
    }

    void OpenCharacterAbility()
    {
        if (characterAbility == null) CharacterAbility();
        if (characterAbilityMovie != null) { characterAbilityMovie.Host.Unload(); characterAbilityMovie.QueueFree(); characterAbilityMovie = null; }
        characterAbilityMovie = new MovieDialog(assets, characterAbility, hudModel, GetWindow().Size);
        uiRoot.AddChild(characterAbilityMovie);
    }
}
