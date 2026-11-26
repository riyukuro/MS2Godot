using Godot;
using Ms2.Formats.World;
using Ms2.Quest;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// Fame: the fame point board (UIFamePointBoardDialog 0x4f54, movie uifamepointboarddialog), which the game menu's entry 44
/// opens (UIGameMenuDialog::FUN_14193cea0, uigamemenudialog.cpp:2101), and the fame contents notice (UIFameContentsNotify
/// 0x4f5a, movie uifamecontentsnotify), the alliance quest banners. Live, both read the session's fame book (UserEnv modes
/// 0xa-0xd); mode 0xd also gives the s_fame_point_update chat line (scenestategame.cpp:4520). Offline the book is empty.
/// </summary>
public partial class HudScene
{
    FamePointBoardController famePointBoard;    // its C++ side, kept between opens
    MovieDialog famePointBoardMovie;            // the movie, from open until OnEndClose
    static FameBook offlineFame;
    Control fameNotifySlot;                     // the fame contents notice's place among the HUD movies
    MovieDialog fameNotifyMovie;                // uifamecontentsnotify, loaded only while a banner is queued
    Action unwatchFame;
    bool fameNotifyLogged;

    /// <summary>The fame contents notice (UIFameContentsNotify 0x4f5a); the npc talk popup queues its opening banner.</summary>
    public FameContentsNotifyController FameNotify { get; private set; }

    bool FamePointBoardOpen => famePointBoard != null && !famePointBoard.IsClosed;

    FameBook FameBookNow => Ms2Godot.Net.LiveSession.Instance?.Fame ?? (offlineFame ??= new FameBook());

    /// <summary>The fame texts over the HUD model's tables and the current book.</summary>
    FameTexts FameTextsNow() => new()
    {
        Table = hudModel.Tables.Fame,
        Book = FameBookNow,
        CommonByKey = hudModel.Tables.Common,
    };

    QuestTable FameQuests() => questTable ??= QuestTable.Load(assets.Packs, new GameStrings(assets.Packs, assets.Language).Languages,
                                                               data.Locale, f => !data.FeaturesOff.Contains(f));

    static bool FameQuestRewarded(long id) => Ms2Godot.Net.LiveSession.Instance?.Quests.Get((int)id)?.State == 2;   // CQuestManager::IsQuestRewarded

    /// <summary>UIGameMenuDialog::FUN_14193cea0: without an alliance whose season quest is rewarded, the system notice
    /// s_fame_point_board_warn2; else CUIService::ToggleDialog(0x4f54).</summary>
    void FamePointBoardMenu()
    {
        if (!FameTextsNow().AnyAllianceOpen(FameQuestRewarded))
        {
            SystemNotice("s_fame_point_board_warn2");
            if (Ms2Godot.Net.LiveSession.LiveCheck)
            {
                var fame = hudModel.Tables.Fame;
                string quests = string.Join(", ", fame.AllianceIds.Select(fame.GetSeasonRequireQuest).Distinct()
                    .Select(q => $"{q} state {Ms2Godot.Net.LiveSession.Instance?.Quests.Get(q)?.State.ToString() ?? "none"}"));
                GD.Print($"live: fame board refused ({quests}): {hudModel.Tables.Common("s_fame_point_board_warn2")}");
            }
            return;
        }
        ToggleFamePointBoard();
    }

    /// <summary>CUIService::ToggleDialog(0x4f54): open when closed, else vfunc_67.</summary>
    void ToggleFamePointBoard()
    {
        if (!FamePointBoardOpen) OpenFamePointBoard();
        else famePointBoard.Close();
    }

    /// <summary>
    /// Opens the fame point board (FUN_141924680 -> vfunc_65): the movie runs its own ActionScript with
    /// UIFamePointBoardDialog's C++ side; OnLoadComplete builds the tabs, whose first one shows its alliance.
    /// </summary>
    void OpenFamePointBoard()
    {
        if (GuideRefusesOpen("FamePointBoardDialog")) return;
        if (FamePointBoardOpen) return;
        if (famePointBoardMovie != null) { famePointBoardMovie.Host.Unload(); famePointBoardMovie.QueueFree(); famePointBoardMovie = null; }
        if (famePointBoard == null)
        {
            var reserved = hudModel.Tables.ReservedWords;
            string Player() => hudModel.HasLocalPlayer ? hudModel.Player.Name : null;
            var c = new FamePointBoardController
            {
                QuestRewarded = FameQuestRewarded,
                ServerTime = () => Ms2Godot.Net.LiveSession.Instance?.ServerTime ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                TableString = hudModel.Tables.StringTableText,
                Words = t => reserved.ReplaceValueWords(FameQuests().Words.ReplaceObjectWords(t), Player()),
            };
            c.Unloaded += () => { famePointBoardMovie?.Host.Unload(); famePointBoardMovie?.QueueFree(); famePointBoardMovie = null; };
            famePointBoard = c;
        }
        famePointBoard.Fame = FameTextsNow();
        famePointBoardMovie = new MovieDialog(assets, famePointBoard, hudModel, ScreenMode.PixelSize(GetWindow()));
        GD.Print($"fame board: load={famePointBoardMovie.Loaded}, tabs {string.Join(",", famePointBoard.Tabs)}");
        uiRoot.AddChild(famePointBoardMovie);
    }

    /// <summary>
    /// The fame contents notice's slot and C++ side, made with the HUD; its movie loads while a banner is queued
    /// (UIHeadUpDisplay vfunc_2 / vfunc_3). The live session's quest packets queue the banners (CAllianceQuest::DecodeStart,
    /// CFieldQuest / CAllianceQuest::Complete) and its UserEnv mode 0xd gives the fame update chat line.
    /// </summary>
    void FameContentsNotify(Control ui)
    {
        ui.AddChild(fameNotifySlot = new Control { Name = "uifamecontentsnotify", MouseFilter = Control.MouseFilterEnum.Ignore });
        FameNotify ??= new FameContentsNotifyController
        {
            LoadMovie = LoadFameNotify,
            UnloadMovie = UnloadFameNotify,
            QuestType = id => FameQuests().Quest((int)id)?.Type,
            QuestAlliance = id => FameQuests().Quest((int)id)?.Alliance,
        };
        FameNotify.Fame = FameTextsNow();
        if (Ms2Godot.Net.LiveSession.LiveCheck && !fameNotifyLogged)
        {
            fameNotifyLogged = true;
            FameNotify.Notified += n => GD.Print($"live: fame notice {n["alliance"]} {n["msg"]} {n["current"]}/{n["max"]} {n["point"]}");
        }
        if (unwatchFame != null || Ms2Godot.Net.LiveSession.Instance is not { } live) return;
        Action<Ms2.Net.Packets.QuestMessage, bool> onQuest = FameNotify.OnQuest;
        Action<FameBook.Change> onFame = change =>
        {
            if (change != FameBook.Change.LogEntry) return;
            string text = hudModel.Tables.Common("s_fame_point_update");    // FUN_1412751b0(text, 0x14)
            ChatLog.AddSystemLine(text, Ms2.Ui.Hud.TalkKind.SystemNotice);
            if (Ms2Godot.Net.LiveSession.LiveCheck) GD.Print($"live: fame line {text}");
        };
        live.QuestReceived += onQuest;
        live.FameChanged += onFame;
        unwatchFame = () => { live.QuestReceived -= onQuest; live.FameChanged -= onFame; };
    }

    /// <summary>UIHeadUpDisplay::vfunc_2 for the fame contents notice: its movie loads; OnLoadComplete shows the head.</summary>
    void LoadFameNotify()
    {
        if (fameNotifyMovie != null || fameNotifySlot == null || !IsInstanceValid(fameNotifySlot)) return;
        FameNotify.Fame = FameTextsNow();
        fameNotifyMovie = new MovieDialog(assets, FameNotify, NewModel(), ScreenMode.PixelSize(GetWindow()));
        if (Ms2Godot.Net.LiveSession.LiveCheck)
            GD.Print($"live: fame notice load {fameNotifyMovie.Loaded}: {string.Join(",", FameNotify.Pending)}");
        fameNotifySlot.AddChild(fameNotifyMovie);
    }

    /// <summary>UIHeadUpDisplay::vfunc_3 for the fame contents notice: the movie unloads.</summary>
    void UnloadFameNotify()
    {
        if (fameNotifyMovie == null) return;
        var m = fameNotifyMovie;
        fameNotifyMovie = null;
        m.Host.Unload();
        m.GetParent()?.RemoveChild(m);
        m.QueueFree();
        if (Ms2Godot.Net.LiveSession.LiveCheck) GD.Print("live: fame notice unloaded");
    }
}
