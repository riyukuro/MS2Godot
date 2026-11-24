using System;
using Godot;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The HUD's part of the exp, level and adventure level packets (CSceneStateGame::OnExpUp, OnLevelUp, OnPrestige,
/// OnAdventurerBar, OnJob's learnt skill toasts): the chat lines, the reward notices, the system texts and the game menu's
/// counts. Subscribed with the prestige watch (<see cref="WatchPrestige"/>).
/// </summary>
public partial class HudScene
{
    Action WatchLevel(Ms2Godot.Net.LiveSession live)
    {
        Action<Ms2.Net.Packets.ExpUp, bool> onExp = (e, rested) => ExpLines(e.Gained, e.Type, rested, e.Flag, live.FieldId ?? 0);
        Action<int, int> onLevel = OwnLevelUp;
        Action<int, int> onAdventure = OwnAdventureLevelUp;
        Action onBar = () => gameMenu?.UpdateAdventureBarCount();
        Action<int> onJob = mode => JobSkillToasts(live, mode);
        live.ExpLogged += onExp;
        live.OwnLevelChanged += onLevel;
        live.OwnAdventureLevelChanged += onAdventure;
        live.AdventurerBarChanged += onBar;
        live.SkillBookChanged += onJob;
        if (Ms2Godot.Net.LiveSession.LiveCheck) GD.Print("live: level watch on");
        return () =>
        {
            live.ExpLogged -= onExp; live.OwnLevelChanged -= onLevel; live.OwnAdventureLevelChanged -= onAdventure;
            live.AdventurerBarChanged -= onBar; live.SkillBookChanged -= onJob;
        };
    }

    /// <summary>FUN_1413606c0 (uiworldshareinfoeventlistener.cpp:133), see <see cref="ExpChatLines.Compose"/>: the reward
    /// notice FUN_141abf230(amount, 0) first when the type asks for it, then the line (kind 8) and the second line (kind
    /// 0x14, none for an empty text).</summary>
    void ExpLines(long amount, int type, bool rested, bool additional, int fieldId)
    {
        var t = hudModel.Tables;
        var l = ExpChatLines.Compose(amount, type, rested, additional, t.Common, key => t.Common(key) ?? "", () => t.StringTableText(3, fieldId));
        if (l.Notify) EnsureRewardNotify().ShowExp(amount, 0);
        ChatLog.AddSystemLine(l.Line, ExpChatLines.ExpKind);
        ChatLog.AddSystemLine(l.Second, TalkKind.SystemNotice);
        if (Ms2Godot.Net.LiveSession.LiveCheck)
            GD.Print($"live: exp line type {type} notify {l.Notify} second {!string.IsNullOrEmpty(l.Second)} ({l.Line?.Length ?? 0} chars)");
    }

    /// <summary>
    /// OnLevelUp's own PC part (scenestategame.cpp:9541-9567), before status 6 and NotifyChangeMyLevel:
    /// <list type="bullet">
    /// <item>the job system's level change (vfunc_4: UpdateLearnable, :9541; for the own PC here, the other PCs' job systems
    /// are not kept);</item>
    /// <item>FUN_141b16550: s_notify_levelup (0x803) of the level as an info notice that also goes to the chat
    /// (FUN_141275460(id, 3, 1));</item>
    /// <item>with the reward notify: its learnt skill toasts (FUN_141ac0210); then at level 10 or more, on a rise, up to
    /// max(SkillPointLimitLevel1, SkillPointLimitLevel2): ShowSkillLevelUpNotice while the skill book (0x4f2d) is closed and
    /// the chat line s_msg_skillbook_add_skillpoint (kind 0x14).</item>
    /// </list>
    /// STILL-OPEN: the inventory's auto-equip of the level (Unk_Inventory::FUN_140a66990, feature 232, levels
    /// AutoInstallEquipmentMinLevel..Max) is not ported: it is the inventory's own pending item, queue and excluded items
    /// (inventory.cpp:1755-1882) with its other callers (the item add and equip result, scenestategame.cpp:2858, :3128; the
    /// bind message box, uimessagebox.cpp:1901; the preset equip, mypc.cpp:4903; the user command, usercommandcli.cpp:2180)
    /// and it sends equip requests, so it goes in as one piece. The exp bar's UpdateExp(3) is the status listener's
    /// (ExpBarController).
    /// </summary>
    void OwnLevelUp(int oldLevel, int level)
    {
        var jobs = EnsureSlotJobs();
        jobs?.UpdateLearnable(level);
        var t = hudModel.Tables;
        string text = Ms2.Quest.ClientFormat.Format(t.Common(0x803), level);
        if (!string.IsNullOrEmpty(text))
        {
            SystemText?.ShowMessage(text, SystemTextController.Info, 0);
            ChatLog.AddSystemLine(text, TalkKind.SystemNotice);
        }
        var notify = EnsureRewardNotify();
        notify.ShowLearnSkills(jobs);
        if (LevelUpNotices.SkillPointNotice(level, oldLevel, t.SkillPointLimitLevel1, t.SkillPointLimitLevel2))
        {
            if (!skillBookOpen) notify.ShowSkillLevelUp();
            ChatLog.AddSystemLine(t.Common("s_msg_skillbook_add_skillpoint"), TalkKind.SystemNotice);
        }
        if (Ms2Godot.Net.LiveSession.LiveCheck) GD.Print($"live: own level {oldLevel} -> {level}");
    }

    /// <summary>OnLevelUp on a raised level of any PC (scenestategame.cpp:9534-9536): Unk_BreakDialog&lt;UIInventoryDialog&gt;(0x4e2d)
    /// finds the inventory dialog when it is loaded, and its UpdateButtonState pushes the buttons again (bAutoPickup reads the
    /// level).</summary>
    void InventoryLevelButtons()
    {
        if (inventoryMovie == null || inventory == null) return;
        inventory.UpdateButtonState();
        if (Ms2Godot.Net.LiveSession.LiveCheck) GD.Print("live: inventory buttons on the level up");
    }

    /// <summary>
    /// OnPrestige kind 2's own PC part (scenestategame.cpp:8851-8862): on a rise, with the reward notify, the item toast of
    /// AdventureLevelLvUpRewardItem with grade 1 (FUN_141abf6e0); status 0x11 is the prestige watch's; FUN_141b16580:
    /// s_notify_adventure_levelup (0xd22) of the level as an info notice that also goes to the chat. DAT_143ab2960 = 1 when
    /// the old level was 0 is the adventure state's (<see cref="Ms2Godot.Net.LiveSession.Adventure"/>).
    /// </summary>
    void OwnAdventureLevelUp(int oldLevel, int level)
    {
        if (level > oldLevel)
        {
            int item = Ms2.Ui.FeatureElements.Constants(assets.Packs).Int("AdventureLevelLvUpRewardItem", 0);
            EnsureRewardNotify().ShowItemReward(item, 1);
        }
        string text = Ms2.Quest.ClientFormat.Format(hudModel.Tables.Common(0xd22), level);
        if (Ms2Godot.Net.LiveSession.LiveCheck) GD.Print($"live: own adventure level {oldLevel} -> {level} ({text?.Length ?? 0} chars)");
        if (string.IsNullOrEmpty(text)) return;
        SystemText?.ShowMessage(text, SystemTextController.Info, 0);
        ChatLog.AddSystemLine(text, TalkKind.SystemNotice);
    }

    /// <summary>OnJob (scenestategame.cpp:7449) for the own PC: modes 0, 2, 7, 9 and 11 run the reward notify's learnt skill
    /// toasts (FUN_141ac0210, :7466, :7489, :7512, :7528) over the job system the packet read.</summary>
    void JobSkillToasts(Ms2Godot.Net.LiveSession live, int mode)
    {
        if (mode is not (0 or 2 or 7 or 9 or 11) || live.SkillBook == null) return;
        EnsureRewardNotify().ShowLearnSkills(EnsureSlotJobs());
    }

    /// <summary>OnPrestige kind 1 (scenestategame.cpp:8825): FUN_1413610f0's chat line, kind 8.</summary>
    void AdventureExpLine(long gain) =>
        ChatLog.AddSystemLine(ExpChatLines.Adventure(gain, key => hudModel.Tables.Common(key) ?? ""), ExpChatLines.ExpKind);

    /// <summary>FUN_141938ae0 (uigamemenudialog.cpp:1380-1394) for the adventure level: 0x4f87 and 0x4f67 count the rewards
    /// not claimed up to the PC's adventure level (CountUnclaimedRewards); 0x4f67 adds the open dialog's
    /// GetClaimableMissionCount. RefreshMenu asks it at the menu's build, on every field load; at adventure level 0 the
    /// count is of the reward rows at level 0, and the reward tables have none (LevelExpTests' sweep, every data set), so it
    /// is 0 without loading them. The mission count is only asked with the dialog open, which loaded the tables.</summary>
    int AdventureNotifyCount(int id)
    {
        var p = hudModel.Player;
        int n = p.AdventureLevel == 0 ? 0 : AdventureLevelTable().CountUnclaimedRewards((uint)p.AdventureLevel, p.AdventureClaimed);
        if (id == 0x4f67 && adventureLevelMovie != null && adventureLevel != null)
            n += AdventureLevelController.GetClaimableMissionCount(AdventureLevelTable(), p);
        return n;
    }
}
