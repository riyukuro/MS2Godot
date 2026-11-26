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
    /// FUN_141938ae0 (uigamemenudialog.cpp:1318) for the ids this HUD has: 0x4e49 is CUserMail::GetUnreadCount, the
    /// unread mails; 0x4e73 is CAchieveManager::GetRewardTotal(0), the trophies whose reward can be taken; 0x4f1e is
    /// CMasteryManager::GetTotalClaimableRewardLevels (without FUN_140930e10's housing grades, whose tab is not ported).
    /// STILL-OPEN: the client leaves the count as it was when there is no local player
    /// (the early return before setNotifyCount); here the count is still set.
    /// </summary>
    int CommunityNotifyCount(int id) => id switch
    {
        0x4e49 => Ms2Godot.Net.LiveSession.Instance?.Mails.Unread ?? 0,      // CUserMail::GetUnreadCount
        0x4e4d => Ms2Godot.Net.LiveSession.Instance?.Buddies.ReceivedCount ?? 0,   // CBuddyManager::FUN_140ae5070, the requests from others
        0x4e4f => Ms2Godot.Net.LiveSession.Instance?.Guilds.NotificationCount ?? 0,   // CGuildManager::GetGuildNotificationCount
        0x4e73 => Ms2Godot.Net.LiveSession.Instance?.Achieves.GetRewardTotal(0) ?? 0,
        0x4f1e => mastery?.TotalClaimableRewardLevels() ?? 0,      // CMasteryManager::GetTotalClaimableRewardLevels
        0x4f67 or 0x4f87 => AdventureNotifyCount(id),             // CAdventureLevelTable::CountUnclaimedRewards (+ the missions)
        0x4eb8 => Ms2Godot.Net.LiveSession.Instance?.AttendDialog.GetTotalUsablePoints() ?? 0,     // UIAttendGiftDialog::GetTotalUsablePoints (uigamemenudialog.cpp:1359)
        0x4f2d => hudModel.HasLocalPlayer ? LocalJobs().ChangedMainSkills() : 0,     // JobSystemCli::ChangedMainSkills (uigamemenudialog.cpp:1329)
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
        var notices = AchieveNoticesFor(live.Achieves);
        live.Achieves.Notices = notices;
        unwatchAchieves = () =>
        {
            live.AchievesChanged -= Changed; live.CollectionChanged -= Collection; achievesWatched = false;
            if (live.Achieves.Notices == notices) live.Achieves.Notices = null;
        };
    }

    /// <summary>
    /// The mode 2 notices of <paramref name="book"/> on this HUD: the reward notify's toasts, the chat log's system lines, the
    /// guide's triggers and limit, the local player (the chat link's name, the beauty reward's gender), the skill icons, the
    /// main player info's progress lines and the dynamic action window's refresh on a clear. The trophy texts
    /// (CAchieveUIData) are read on the first notice, so the HUD's load does not read them.
    /// </summary>
    AchieveNotices AchieveNoticesFor(AchieveBook book)
    {
        var n = new AchieveNotices(() => AchieveTextsFor(book))     // the texts are read on the first mode 2 update
        {
            Toasts = () => Ms2Godot.Net.LiveSession.LiveCheck ? new LoggedToasts(EnsureRewardNotify()) : EnsureRewardNotify(),
            Line = (text, kind) =>
            {
                ChatLog.AddSystemLine(text, kind);
                if (Ms2Godot.Net.LiveSession.LiveCheck) GD.Print($"live: achieve line {kind:x} {text}");
            },
            Trigger = (type, value) => Ms2.Ui.Guide.GuideManager.Current?.AddTrigger((Ms2.Ui.Guide.GuideTrigger)type, value),
            ProgressRestricted = () => Ms2.Ui.Guide.GuideManager.Current?.IsRestricted(Ms2.Ui.Guide.GuideLimit.ProgressAchieve) == true,
            PlayerName = () => hudModel.HasLocalPlayer ? hudModel.Player.Name : null,
            SkillIcon = code => hudModel.Tables.Skills.GetSkillData(code)?.Icon,
            AppendMessage = (msg, type) =>
            {
                playerInfo?.AppendMessage(msg, type);
                if (Ms2Godot.Net.LiveSession.LiveCheck) GD.Print($"live: achieve progress {type} {msg}");
            },
            HasPlayerInfo = () => playerInfo != null,
        };
        n.Cleared += id =>
        {
            dynamicAction?.Refresh();
            if (Ms2Godot.Net.LiveSession.LiveCheck) GD.Print($"live: achieve clear notice {id}");
        };
        return n;
    }

    Action unwatchAchieves;

    /// <summary>The reward notify's trophy toasts, each also printed for the scripted run's log.</summary>
    sealed class LoggedToasts(IAchieveToasts inner) : IAchieveToasts
    {
        public void ShowAchieve(string name, string icon, int achieveType) { GD.Print($"live: achieve toast {name} {icon} {achieveType}"); inner.ShowAchieve(name, icon, achieveType); }
        public void ShowItemReward(int itemId, int grade) { GD.Print($"live: achieve toast item {itemId} {grade}"); inner.ShowItemReward(itemId, grade); }
        public void ShowStatPoint(int points) { GD.Print($"live: achieve toast stat points {points}"); inner.ShowStatPoint(points); }
        public void ShowSkillPoint(int points, int kind) { GD.Print($"live: achieve toast skill points {points} {kind}"); inner.ShowSkillPoint(points, kind); }
        public void ShowIconNotice(string icon, string msg, int effect) { GD.Print($"live: achieve toast icon {icon} {msg}"); inner.ShowIconNotice(icon, msg, effect); }
    }

    /// <summary>UIAchieveDialog::vfunc_13 (:425) while it is shown: Escape (closePopup, else close) and Enter (the search).</summary>
    bool AchieveKeys(InputEventKey k)
    {
        if (!AchieveOpen) return false;
        if (k.Keycode == Key.Escape) return achieve.OnKey(true, false);
        if (k.Keycode == Key.Enter) return achieve.OnKey(false, true);
        return false;
    }

    /// <summary>CAchieveUIData for <paramref name="book"/>: read once, then pointed at the book and the HUD model's tables
    /// (the trophy window's texts, also GetRequireAchieveText for an install item's guide line).</summary>
    AchieveTexts AchieveTextsFor(AchieveBook book)
    {
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
        achieveTexts.PaletteColors = hudModel.Tables.ColorPalettes.Rows;
        achieveTexts.Gender = () => Ms2Godot.Net.LiveSession.Instance is { } live ? live.Me.User?.Gender : hudModel.HasLocalPlayer ? 0 : null;
        return achieveTexts;
    }

    /// <summary>
    /// Opens the trophy window (FUN_141924680 -> vfunc_65): the movie runs its own ActionScript with UIAchieveDialog's C++ side
    /// (AchieveController). A dialog that is open is left; the movie is dropped when its close tween ends.
    /// </summary>
    void OpenAchieve()
    {
        if (GuideRefusesOpen("AchieveDialog")) return;
        if (AchieveOpen) return;
        if (achieveMovie != null) { achieveMovie.Host.Unload(); achieveMovie.QueueFree(); achieveMovie = null; }
        var live = Ms2Godot.Net.LiveSession.Instance;
        var book = live != null ? live.Achieves : (offlineAchieves ??= OfflineAchieves());
        if (achieve == null || achieve.Book != book)
        {
            achieveCategories ??= AchieveCategory.Load(assets.Packs);
            AchieveTextsFor(book);
            var c = new AchieveController(book, achieveTexts, achieveCategories)
            {
                HasPlayer = () => live == null || live.Me.User != null,
                LocalPlayerDead = () => live?.Me.Dead ?? false,
                QuestName = id => questTable?.QuestName((int)id) ?? "",
                QuestRewarded = id => live?.Quests.Get((int)id)?.State == 2,
                Room = id => (dungeonRooms ??= DungeonRooms.Load(assets.Packs, questTable != null ? questTable.Available : _ => true)).GetRoom((int)id),
                Colors = (palette, sn) => hudModel.Tables.ColorPalettes.BySN((int)palette, (int)sn),
                ItemCollected = id => live != null && live.CollectionGrades.ContainsKey((uint)id),   // DAT_143ab30e0.Lookup(id) != 0: the one-key CAtlMap::Lookup returns the pair, so a present id, any grade
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
        achieveMovie = new MovieDialog(assets, achieve, hudModel, ScreenMode.PixelSize(GetWindow()));
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
