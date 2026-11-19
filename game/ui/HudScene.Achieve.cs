using Godot;
using Ms2.Formats.World;
using Ms2.Quest;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The trophy window: UIAchieveDialog (0x4e73, movie uiachievedialog) over CAchieveManager's trophies. Live, the book is the
/// session's (the Achieve packets, 0x5E) and its sends go out (Achieve 0x46: the reward, bookmark and debug requests;
/// RequestUserEnv 0x69 mode 3 at load, whose UserEnv mode 3 reply fills the collection grades and redraws the dialog).
/// Offline, a book over the trophy data with no trophy of the player's, and the sends are logged. The C++ side is kept between
/// opens, as the client keeps the dialog: its filters stay, while its close (vfunc_67) resets the tab, category and search.
/// </summary>
public partial class HudScene
{
    AchieveController achieve;          // its C++ side, kept between opens
    MovieDialog achieveMovie;           // the movie, from open until OnEndClose
    static AchieveBook offlineAchieves;
    static AchieveTexts achieveTexts;   // CAchieveUIData, read once
    static AchieveCategoryTable achieveCategories;      // s_aCategory, read once
    GameMenuController gameMenu;        // the game menu's C++ side of this layout, for its notify counts
    bool achievesWatched;

    bool AchieveOpen => achieve != null && !achieve.IsClosed;

    /// <summary>CUIService::ToggleDialog(0x4e73): open when closed, else vfunc_67.</summary>
    void ToggleAchieve()
    {
        if (!AchieveOpen) OpenAchieve();
        else achieve.Close();
    }

    /// <summary>
    /// FUN_141938ae0 (uigamemenudialog.cpp:1318) for the community ids this HUD has: 0x4e49 is CUserMail::GetUnreadCount, the
    /// unread mails; 0x4e73 is CAchieveManager::GetRewardTotal(0), the trophies whose reward can be taken. STILL-OPEN: the client leaves the count as it was when there is no local player
    /// (the early return before setNotifyCount); here the count is still set.
    /// </summary>
    int CommunityNotifyCount(int id) => id switch
    {
        0x4e49 => Ms2Godot.Net.LiveSession.Instance?.Mails.Unread ?? 0,      // CUserMail::GetUnreadCount
        0x4e73 => Ms2Godot.Net.LiveSession.Instance?.Achieves.GetRewardTotal(0) ?? 0,
        _ => 0,
    };

    /// <summary>
    /// The game menu's trophy count follows the Achieve packets: CAchieveManager::OnPacket mode 2 of a trophy not of type 8
    /// calls UIGameMenuDialog::FUN_141937ea0 (achievemanager.cpp:545-548), which is FUN_141938ae0(0x4e73, 0); the live
    /// session raises AchievesChanged on that path. UserEnv mode 3 redraws an open dialog (scenestategame.cpp:4455,
    /// FUN_1418a8f80). Subscribed once per scene.
    /// </summary>
    void WatchAchieves()
    {
        if (achievesWatched || Ms2Godot.Net.LiveSession.Instance is not { } live) return;
        achievesWatched = true;
        void Changed() => gameMenu?.UpdateNotifyCount(0x4e73);
        void Collection() { if (AchieveOpen) achieve.Refresh(); }
        live.AchievesChanged += Changed;
        live.CollectionChanged += Collection;
        unwatchAchieves = () => { live.AchievesChanged -= Changed; live.CollectionChanged -= Collection; achievesWatched = false; };
    }

    Action unwatchAchieves;

    /// <summary>UIAchieveDialog::vfunc_13 (:425) while it is shown: Escape (closePopup, else close) and Enter (the search).</summary>
    bool AchieveKeys(InputEventKey k)
    {
        if (!AchieveOpen) return false;
        if (k.Keycode == Key.Escape) return achieve.OnKey(true, false);
        if (k.Keycode is Key.Enter or Key.KpEnter) return achieve.OnKey(false, true);
        return false;
    }

    /// <summary>
    /// Opens the trophy window (FUN_141924680 -> vfunc_65): the movie runs its own ActionScript with UIAchieveDialog's C++ side
    /// (AchieveController). A dialog that is open is left; the movie is dropped when its close tween ends.
    /// </summary>
    void OpenAchieve()
    {
        if (AchieveOpen) return;
        if (achieveMovie != null) { achieveMovie.Host.Unload(); achieveMovie.QueueFree(); achieveMovie = null; }
        var live = Ms2Godot.Net.LiveSession.Instance;
        var book = live != null ? live.Achieves : (offlineAchieves ??= OfflineAchieves());
        if (achieve == null || achieve.Book != book)
        {
            achieveCategories ??= AchieveCategory.Load(assets.Packs);
            if (achieveTexts == null)
            {
                var words = (questTable ??= QuestTable.Load(assets.Packs, new GameStrings(assets.Packs, assets.Language).Languages, data.Locale,
                                                            f => !data.FeaturesOff.Contains(f))).Words;   // the quest log's tables
                achieveTexts = AchieveTexts.Load(assets.Packs, new GameStrings(assets.Packs, assets.Language).Languages,
                                                 words.ReplaceObjectWords, words.ReplaceBoldWords);
            }
            achieveTexts.Data = book.DataOf;
            achieveTexts.Find = book.Find;
            achieveTexts.CommonByKey = hudModel.Tables.Common;
            achieveTexts.TableString = hudModel.Tables.StringTableText;
            achieveTexts.TitleName = hudModel.Tables.TitleName;
            achieveTexts.FeatureOn = hudModel.IsFeatureEnabled;
            achieveTexts.Gender = () => live != null ? live.Me.User?.Gender : hudModel.HasLocalPlayer ? 0 : null;
            var c = new AchieveController(book, achieveTexts, achieveCategories)
            {
                HasPlayer = () => live == null || live.Me.User != null,
                LocalPlayerDead = () => live?.Me.Dead ?? false,
                QuestName = id => questTable?.QuestName((int)id) ?? "",
                QuestRewarded = id => live?.Quests.Get((int)id)?.State == 2,
                Room = id => (dungeonRooms ??= DungeonRooms.Load(assets.Packs, questTable != null ? questTable.Available : _ => true)).GetRoom((int)id),
                ItemCollected = id => live != null && live.CollectionGrades.ContainsKey((uint)id),   // DAT_143ab30e0.Lookup
                Send = packet =>
                {
                    if (Ms2Godot.Net.LiveSession.Instance is { } s) s.SendAchieve(packet);
                    else GD.Print($"trophy: offline, not sent: {packet}");
                },
            };
            c.SystemNotice += id => SystemText?.ShowMessage(achieveTexts.Common(id), SystemTextController.Warning, 0);
            c.PlaySound += key => FieldAudio.Instance?.PlayUi(key);
            c.Open += OpenAchieve;
            c.UseLink += (type, id) => GD.Print($"trophy: link {type}/{id} is not in this HUD");
            c.Unloaded += () => { achieveMovie?.Host.Unload(); achieveMovie?.QueueFree(); achieveMovie = null; };
            achieve = c;
        }
        var t = System.Diagnostics.Stopwatch.StartNew();
        achieveMovie = new MovieDialog(assets, achieve, hudModel, GetWindow().Size);
        GD.Print($"trophy: load={achieveMovie.Loaded} in {t.ElapsedMilliseconds} ms");
        uiRoot.AddChild(achieveMovie);
    }

    /// <summary>The offline book: the trophy data in its load order, no trophy of the player's.</summary>
    AchieveBook OfflineAchieves()
    {
        var order = new List<AchieveData>();
        var table = AchieveData.LoadAll(assets.Packs, order);
        return new AchieveBook(table, order);
    }
}
