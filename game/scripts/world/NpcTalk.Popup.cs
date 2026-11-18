using Godot;
using Ms2.Net.Packets;
using Ms2.Quest;
using Ms2Godot.Ui;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.World;

/// <summary>
/// The small npc talk popup of a field quest: UIPopupNpcTalkDialog (0x4f4d, movie uipopupnpctalkdialog) with its C++ side
/// <see cref="PopupNpcTalkController"/>. It opens on its own when the loading screen fades in on a field with an active field
/// quest not opened before, when the server starts a field quest of the field (Quest mode 2), and when a quest's progress
/// (Quest mode 3) makes it ready (CQuest::FUN_140d738d0); past the last line a complete quest sends Quest mode 4.
/// The client keeps the one dialog for the whole session, so the quests it opened (m_198) are kept across fields here and
/// cleared when the character changes (NotifyChangeMyPC). Offline, the sandbox plays the server: an accepted quest starts
/// (slot 1) and the complete marks it rewarded. FIXTURE: --popup-talk QUEST opens the popup on the quest's talk offline,
/// as StartQuestTalk does.
/// STILL-OPEN: the popup is dropped with the field's NpcTalk node on a field move, while the client's dialog stays open
/// across it.
/// STILL-OPEN: CQuestManager::UpdateQuestProgress (questmanager_2.cpp:1377, its listener dispatch at :1416), the client's
/// local recount of the condition types the server does not count (IsConditionTypeCounted == 0: 3, 4, 0x49, 0xad), is not
/// ported, so a field quest made complete by that recount alone never reaches slot 3 here; when it is ported it should
/// raise the quest book's ProgressUpdated.
/// STILL-OPEN: the talk key is Space (FieldContents' key handling), while the labels show the key table's binding of the
/// talk action 0x493e2; a rebound talk key shows in the label but does not work.
/// </summary>
public sealed partial class NpcTalk
{
    /// <summary>The field the player is on (WorldService::GetFieldId), set by the field.</summary>
    public int FieldId;
    PopupNpcTalkController popup;
    MovieDialog popupMovie;
    CanvasLayer popupLayer;
    UiModel popupModel;
    static KeyTable popupKeys;                         // the key setting and defaultkey tables, parsed once
    static readonly HashSet<int> popupOpened = new();    // m_198, for the character below
    static long popupOpenedFor;
    static Dictionary<int, List<int>> fieldQuests;     // QuestDataTable m_240

    public PopupNpcTalkController Popup => popup;
    public bool PopupOpen => popup != null && popup.Loaded && !popup.IsClosed;

    int CurrentField => live?.FieldId ?? FieldId;

    /// <summary>QuestDataTable::BuildIndexes (questdata.cpp:2459): the field quests (questType 8) with a progressMap, by
    /// their first field, in quest id order.</summary>
    IReadOnlyList<int> FieldQuestsOf(int field)
    {
        if (fieldQuests == null)
        {
            fieldQuests = new();
            foreach (var d in ClientQuests(packs).Quests.OrderBy(d => d.Id))
            {
                if (d.Type != 8 || d.Fields.Length == 0) continue;
                int key = (int)d.Fields[0];
                if (!fieldQuests.TryGetValue(key, out var list)) fieldQuests[key] = list = new();
                list.Add(d.Id);
            }
        }
        return fieldQuests.TryGetValue(field, out var l) ? l : Array.Empty<int>();
    }

    /// <summary>NotifyChangeMyPC: ClearOpenedQuests.</summary>
    void ClearOpenedOnCharacterChange()
    {
        long character = live?.CharacterId ?? StubHudData.Instance.Player.CharacterId;
        if (character != popupOpenedFor) { popupOpened.Clear(); popupOpenedFor = character; }
    }

    /// <summary>The popup's UI model, built only when a talk is about to open (StartQuestTalk's feature check, the movie);
    /// the key table is shared by every field's popup.</summary>
    UiModel PopupModel()
    {
        if (popupModel != null) return popupModel;
        tables ??= GameTables.Shared(packs, GameOptions.StringLanguage);
        popupModel = new UiModel(tables) { FeatureEnabledByName = f => LobbyFeatures.IsEnabled(packs, f) };
        popupModel.Keys = popupKeys ??= new KeyTable(packs, GameOptions.StringLanguage);
        // the server's key table when live (OnKeyTable), else the packet a new character gets
        popupModel.ServerKeys = live?.KeyTable is { Loaded: true } k ? k : UiFixture.NewCharacterKeyTable();
        popupModel.Keys.Apply(popupModel.ServerKeys);
        return popupModel;
    }

    /// <summary>The dialog (a few delegates), built on first use; its model and movie only when a talk opens it.</summary>
    PopupNpcTalkController PopupDialog()
    {
        ClearOpenedOnCharacterChange();
        if (popup != null) return popup;
        var c = new PopupNpcTalkController
        {
            Opened = popupOpened,
            FindQuestTalk = (q, step) => QuestScripts(packs).Find(q, step) is { Contents.Count: > 0 } s
                ? s.Contents.Select(x => new PopupNpcTalkController.Line(x.Text, x.SpeakerIllust)).ToList() : null,
            QuestOf = q => ClientQuests(packs).Quest(q),
            RecordOf = q => QuestContext.Get(q),
            ReplaceWords = s => Scripts(packs).Resolve(s, PlayerName, player?.Gender),
            NpcName = id => Scripts(packs).NpcDisplayName(id),
            FieldId = () => CurrentField,
            FeatureEnabled = id => PopupModel().IsFeatureEnabled(id),
            FieldQuests = FieldQuestsOf,
            Inputs = () => new QuestButtonInputs
            {
                FieldId = CurrentField, FieldExists = f => f == CurrentField, LocalPlayerAlive = true, HasFieldData = true,
            },
        };
        c.OpenRequested += OpenPopupMovie;
        c.OpeningBanner += q => GD.Print($"npc talk popup: quest {q}: the fame contents opening banner (UIFameContentsNotify 0x4f5a) is not ported");
        c.SendComplete += q =>
        {
            if (live != null) { GD.Print($"npc talk popup: quest {q} complete sent"); live.SendTalk(new QuestCompleteRequest(q, 0, 0)); }
            else GD.Print($"npc talk popup: quest {q} complete: {(Records.Complete(q) ? "rewarded" : "refused")}");
        };
        c.Unloaded += ClosePopupMovie;
        return popup = c;
    }

    /// <summary>FUN_141924680 on the popup: its movie loads; OnLoadComplete shows the line.</summary>
    void OpenPopupMovie()
    {
        if (popupMovie != null) return;
        ui ??= new UiAssets(packs);
        if (!UiCode.Load(Settings.DataDir)) GD.PushWarning("UI code is not built for this data: the npc talk popup runs interpreted");
        if (popupLayer == null) AddChild(popupLayer = new CanvasLayer { Layer = 10, Name = "PopupNpcTalk" });
        popupMovie = new MovieDialog(ui, popup, PopupModel(), GetWindow().Size);
        popupLayer.AddChild(popupMovie);
        GD.Print($"npc talk popup: quest {popup.Quest}");
    }

    /// <summary>NotifyFadeInComplete: the loading screen faded in on the field.</summary>
    public void OnFadeInComplete()
    {
        ClearOpenedOnCharacterChange();
        // FUN_140e770a0 opens nothing unless m_240 has an active field quest of the field not in m_198: nothing is built
        if (FieldQuestsOf(CurrentField).Any(q => QuestContext.Get(q)?.IsActive == true && !popupOpened.Contains(q)))
            PopupDialog().OnFadeInComplete();
        if (live == null && PopupFixture() is int f and > 0) PopupDialog().StartQuestTalk(f);
    }

    /// <summary>The popup movie unloads: on Unloaded, and when the field's NpcTalk leaves the tree with the popup up.</summary>
    void ClosePopupMovie()
    {
        if (popupMovie == null) return;
        popupMovie.Host.Unload();
        popupMovie.QueueFree();
        popupMovie = null;
    }

    static int? PopupFixture()
    {
        var args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--popup-talk" && int.TryParse(args[i + 1], out int q)) return q;
        return null;
    }

    void OnLiveQuestStarted(int q) => PopupDialog().OnQuestStarted(q);
    void OnLiveQuestProgress(int q) => PopupDialog().OnQuestProgress(q);

    /// <summary>The keys of vfunc_13 on the open popup; they are not taken (it returns 0).</summary>
    void PopupKey(PopupNpcTalkController.PopupKey key)
    {
        if (PopupOpen) popup.Key(key);
    }
}
