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
    /// The dialog's C++ side and its listeners: the item changes (a tab-1 ingredient redoes the list), the Mastery packets
    /// (OnMastery, scenestategame.cpp:3920) and the player's damage (a hit cancels a craft).
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
                ItemCount = id => live == null ? 0 : live.Items.All.Where(i => i.Id == id).Sum(i => i.Amount),
                ItemCountByTag = tag => live == null ? 0 : live.Items.All
                    .Where(i => ItemTags.ByName(hudModel.Tables.Items.Get(i.Id)?.StringTag) == tag).Sum(i => i.Amount),
                IsQuestActive = id => live?.Quests.Get((int)id)?.State == 1,
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
        Action<MasteryMessage, uint, int, bool> onMastery = (m, gain, oldGrade, up) =>
        {
            if (masteryMovie == null) return;
            switch (m)
            {
                case MasteryMessage.ExpChanged e: mastery.OnMasteryChanged(e.Type, true); break;     // mode 0, FUN_140eb4c10
                case MasteryMessage.RewardReceived r:                                                // mode 1: the grade turns inactive
                    mastery.AddLevelGuide(MasteryRecipeTable.TypeFromID((uint)r.RewardId), MasteryRecipeTable.GradeFromID((uint)r.RewardId));
                    mastery.OnMasteryChanged(MasteryRecipeTable.TypeFromID((uint)r.RewardId), true);
                    break;
                case MasteryMessage.Crafted c: mastery.SetPendingSub(c.Type); break;                 // mode 2
            }
        };
        if (live != null)
        {
            live.Items.Changed += onBag;
            live.MasteryReceived += onMastery;
        }
        unwatchMastery = () =>
        {
            if (live == null) return;
            live.Items.Changed -= onBag;
            live.MasteryReceived -= onMastery;
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
