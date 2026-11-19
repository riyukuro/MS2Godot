using Godot;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;

namespace Ms2Godot.Ui;

/// <summary>
/// The mastery dialog: UIMasteryDialog (0x4f1e, movie uimasterydialog) over CMasteryTable's recipes and the session's mastery
/// state. Live, the reward and craft requests go out (Mastery modes 1 and 2); offline, nothing is learned and the sends are
/// logged. The C++ side lives with the HUD: the dialog's item and Mastery listeners work before the first open.
/// </summary>
public partial class HudScene
{
    MasteryController mastery;              // its C++ side, from the HUD's build
    MovieDialog masteryMovie;               // the movie, from open until the close
    static readonly MasteryData offlineMastery = new();
    Action unwatchMastery;

    bool MasteryOpen => masteryMovie != null && mastery?.IsClosed == false;

    /// <summary>
    /// The quest tables' CReservedWordTable for the recipes' habitat links (CMasteryReceipe::m_strHabitat, its Load calls
    /// GetWorldMapLink(id, "#909090")): the map names and the world map's searchable fields.
    /// </summary>
    /// <summary>The server clock (GetServerTime), the item expiry test's now.</summary>
    long MasteryNow() => Ms2Godot.Net.LiveSession.Instance?.GameSession?.ServerTime ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    Ms2.Quest.QuestTable MasteryQuestTable() =>
        questTable ??= Ms2.Quest.QuestTable.Load(assets.Packs, new Ms2.Formats.World.GameStrings(assets.Packs, assets.Language).Languages,
                                                 data.Locale, f => !data.FeaturesOff.Contains(f));

    /// <summary>
    /// The dialog's C++ side and its listeners: the item changes (a tab-1 ingredient redoes the list), the Mastery packets
    /// (OnMastery, scenestategame.cpp:3920), the player's damage (a hit cancels a craft, OnDamagedMyPC @0x140eb5320) and
    /// the craft result notices (FUN_140b6d710).
    /// </summary>
    void Mastery()
    {
        unwatchMastery?.Invoke();
        var live = Ms2Godot.Net.LiveSession.Instance;
        var table = hudModel.Tables.MasteryRecipes;
        var data = live?.Mastery ?? offlineMastery;
        if (live != null) live.MasteryTable = table;
        if (mastery == null)
        {
            var c = mastery = new MasteryController
            {
                Table = table,
                Data = data,
                CastingBar = CastingBar,
                ItemCount = id => live == null ? 0 : Ms2.Ui.Items.InventoryCount.ById(live.Items.All, hudModel.Tables.Items, id, 0,
                    live.CharacterId, MasteryNow()),
                ItemCountByTag = tag => live == null ? 0 : live.Items.All
                    .Where(i => ItemTags.ByName(hudModel.Tables.Items.Get(i.Id)?.StringTag) == tag).Sum(i => i.Amount),
                IsQuestActive = id => live?.Quests.Get((int)id)?.State == 1,
                WorldMapLink = (id, color) => MasteryQuestTable().Words.GetWorldMapLink(id, color),
                Notice = text =>
                {
                    if (SystemText == null) return;      // ShowSystemNotice needs the system text, then shows even ""
                    SystemText.ShowMessage(text ?? "", Ms2.Ui.Hud.SystemTextController.Warning, 0);
                    ChatLog.AddSystemLine(text ?? "", Ms2.Ui.Hud.TalkKind.SystemNotice);
                },
            };
            c.SendReward += id =>
            {
                if (live != null) live.SendTalk(new MasteryRewardRequest(id));
                else GD.Print($"mastery: reward {id} not sent (offline)");
            };
            c.SendCraft += id =>
            {
                if (live != null) live.SendTalk(new MasteryCraftRequest(id));
                else GD.Print($"mastery: craft {id} not sent (offline)");
            };
            c.SendChatCommand += text => GD.Print($"mastery: chat command {text}");
            c.Unloaded += () =>
            {
                masteryMovie?.Host.Unload();
                masteryMovie?.QueueFree();
                masteryMovie = null;
            };
        }
        Action<long> onBag = _ => { if (masteryMovie != null) mastery.OnItemChanged(-1); };
        // CSceneStateGame::OnMastery (scenestategame.cpp:3920) over MasteryEvents' decisions. The client's early return
        // without a local player is the live session's (it raises these only with the own user block). STILL-OPEN for types
        // 5 and 6: CFunctionCubeService::FUN_141232260 (the housing cubes) and WorldService::FUN_141bc5e20(6) (the field's
        // category 6 objects' state effects) are not ported; the item lists go to the reward notice 0x4e5d.
        Action<MasteryMessage, uint, int, bool> onMastery = (m, gain, oldGrade, up) =>
        {
            switch (m)
            {
                case MasteryMessage.ExpChanged e:
                {
                    var u = MasteryEvents.OnExpChanged(e.Type, up);
                    if (u.Dialog) mastery.OnMasteryChanged(e.Type, u.Tabs);        // FUN_140eb4c10(nType, bGradeUp)
                    if (u.GameMenu) gameMenu?.UpdateNotifyCount(0x4f1e);           // FUN_141938190 on a grade up
                    break;
                }
                case MasteryMessage.RewardReceived r:
                {
                    var u = MasteryEvents.OnRewardReceived((uint)r.RewardId);
                    if (u.LevelGuide) mastery.AddLevelGuide(u.Type, u.Grade);
                    if (u.Dialog) mastery.UpdateSubTabsAndRewards();               // FUN_140eb4be0, no pending sub
                    if (u.GameMenu) gameMenu?.UpdateNotifyCount(0x4f1e);
                    break;
                }
                case MasteryMessage.Crafted c: mastery.SetPendingSub(c.Type); break;   // mode 2
                case MasteryMessage.Result x: mastery.ShowCraftResult(x.Code); break;  // mode 3, FUN_140b6d710
            }
        };
        // MyPC::vfunc_113 posts the damage record of a hit on the own player (mypc.cpp:1265); the dialog cancels the
        // craft when its total damage is negative (OnDamagedMyPC).
        Action<long> onDamage = total => { if (MasteryEvents.CancelsCraft(total)) mastery.OnDamaged(total); };
        if (live != null)
        {
            live.Items.Changed += onBag;
            live.MasteryReceived += onMastery;
            live.SelfDamaged += onDamage;
        }
        unwatchMastery = () =>
        {
            if (live == null) return;
            live.Items.Changed -= onBag;
            live.MasteryReceived -= onMastery;
            live.SelfDamaged -= onDamage;
        };
    }

    /// <summary>CUIService::ToggleDialog(0x4f1e): open when closed, else vfunc_67.</summary>
    void ToggleMastery()
    {
        if (MasteryOpen) mastery.Close();
        else OpenMastery();
    }

    void OpenMastery()
    {
        if (mastery == null) Mastery();
        if (masteryMovie != null) { masteryMovie.Host.Unload(); masteryMovie.QueueFree(); masteryMovie = null; }
        mastery.CastingBar = CastingBar;
        masteryMovie = new MovieDialog(assets, mastery, hudModel, GetWindow().Size);
        uiRoot.AddChild(masteryMovie);
    }
}
