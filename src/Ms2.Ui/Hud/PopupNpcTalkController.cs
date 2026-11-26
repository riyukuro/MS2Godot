using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIPopupNpcTalkDialog (ms2/client/ms2/uipopupnpctalkdialog.cpp, UI id 0x4f4d, "PopupNpcTalkDialog"), the C++ side of
/// uipopupnpctalkdialog: the small npc talk popup of a field quest (questType 8). It shows the quest's talk script one line
/// at a time: the speaker's portrait (setPortrait), the npc's name (setNpc), the line (m_script), Next / OK / Complete on
/// m_next and Close on m_end. Past the last line it completes a complete quest (Quest mode 4) or, for a quest still in
/// progress, queues the fame contents opening banner.
/// It opens on its own: when the loading screen fades in on a field (NotifyFadeInComplete), the first active field quest of
/// the field not opened before; when the server starts a field quest of this field (UIQuestEventListener slot 1); and when a
/// quest's progress makes it ready to complete remotely (slot 3, CQuest::FUN_140d738d0).
/// <see cref="OpeningBanner"/> is UIFameContentsNotify::QueueOpeningBanner (0x4f5a), which the host's
/// FameContentsNotifyController answers.
/// STILL-OPEN: CQuestManager::FUN_1416cf870 (questmanager_2.cpp:1472) starts the popup from the quest dialog too; that
/// caller is not ported.
/// </summary>
public sealed class PopupNpcTalkController : UiController
{
    public override string Movie => "uipopupnpctalkdialog";
    /// <summary>scaleform.ms2.controls.dialog.PopupNpcTalk's own method.</summary>
    protected override string EntityMethod => "setPortrait";
    public override string TypeName => "PopupNpcTalkDialog";     // GetTypeName (:@0x140e769d0)

    /// <summary>vfunc_13 (uipopupnpctalkdialog.cpp:186) has its own Esc (the opening banner, then vfunc_69), run by the npc
    /// talk's key input, so the generic dialog Esc passes it by.</summary>
    public override bool EscapeKey() => false;
    /// <summary>vfunc_48.</summary>
    public const int DialogId = 0x4f4d;
    /// <summary>CFeatureManager id 0x95, "FameContents": StartQuestTalk does nothing without it.</summary>
    public const int FameContentsFeature = 0x95;
    /// <summary>The talk key (IsActionKeyPressed(0x493e2), Space): the talk cinematic's Next as well.</summary>
    public const int TalkAction = 0x493e2;
    /// <summary>The fame contents notify (FindFameContentsNotify(0x4f5a)).</summary>
    public const int FameContentsNotifyId = 0x4f5a;

    /// <summary>A line of the talk script (a CCinematicContent): its text (m_name) and speaker illust (m_speakerIllust).</summary>
    public sealed record Line(string Text, string SpeakerIllust);

    // ---- what the client reads (supplied by the host) ------------------------------------------------------------------

    /// <summary>FindQuestTalk(quest, step): the lines of the quest's talk script <c>step</c>, null when it has none.</summary>
    public Func<int, int, IReadOnlyList<Line>> FindQuestTalk = (_, _) => null;
    /// <summary>GetQuestData, null when the quest is not in the table.</summary>
    public Func<int, QuestData> QuestOf = _ => null;
    /// <summary>CQuestManager vfunc_o10_3: the character's record of a quest, null when it has none.</summary>
    public Func<int, QuestRecord> RecordOf = _ => null;
    /// <summary>CReservedWordTable::ReplaceObjectAndValueWords.</summary>
    public Func<string, string> ReplaceWords = s => s;
    /// <summary>FUN_1401aa6a0: the npc's name.</summary>
    public Func<int, string> NpcName = _ => "";
    /// <summary>WorldService::GetFieldId.</summary>
    public Func<int> FieldId = () => 0;
    /// <summary>CFeatureManager::IsFeatureEnabled (read before the movie loads, so not from the model).</summary>
    public Func<int, bool> FeatureEnabled = _ => true;
    /// <summary>The live client's globals CQuest::FUN_140d738d0 reads for a story quest (<see cref="QuestCommon.Dispatch738d0"/>).</summary>
    public Func<QuestButtonInputs> Inputs = () => new QuestButtonInputs();
    /// <summary>QuestDataTable::FUN_14074d360(field): the field quests (questType 8) whose first progressMap is the field, in
    /// quest id order (m_240, built by BuildIndexes, questdata.cpp:2459).</summary>
    public Func<int, IReadOnlyList<int>> FieldQuests = _ => Array.Empty<int>();

    /// <summary>The bottom bar heights of FUN_1418dd0d0 (uibuiltindialog.cpp:53): DAT_1439f8ccc, DAT_1439f8cd0 (with feature
    /// 0xbd) and the margin DAT_1439f8cc8 (193 in the data; STILL-OPEN whether the client changes it at run time).</summary>
    public int BottomBarHeight = 21, AdventureBarHeight = 21, ScreenMargin = 193;

    /// <summary>Quest mode 4 (Encode1 4, Encode4 quest, Encode4 0, Encode4 0): the quest's complete, past the last line.</summary>
    public event Action<int> SendComplete;
    /// <summary>UIFameContentsNotify::QueueOpeningBanner(quest).</summary>
    public event Action<int> OpeningBanner;
    /// <summary>FUN_141924680 on a dialog whose movie is not loaded (m_30 null): the host opens the movie.</summary>
    public event Action OpenRequested;

    // ---- the dialog's state ---------------------------------------------------------------------------------------------

    int line;                               // m_180
    int quest;                              // m_184
    IReadOnlyList<Line> script;             // m_188
    /// <summary>m_198: the quests the fade-in opened (each once). The host keeps one set for the character across fields,
    /// as the client keeps its one dialog.</summary>
    public HashSet<int> Opened = new();
    bool loaded;                            // m_30 is set: the movie is loaded
    bool opening;                           // FUN_141924680 ran (vfunc_65 cleared m_68) and the movie has not loaded yet

    public int Quest => quest;
    public int LineIndex => line;
    public bool Loaded => loaded;

    public PopupNpcTalkController()
    {
        // OnEndClose unloads the movie (UIDialog's close tween): m_30 is gone
        Unloaded += () => { loaded = false; opening = false; };
    }

    bool IsComplete(int q) => RecordOf(q)?.Complete ?? false;      // CQuestManagerCommon::IsQuestComplete (questdata.cpp:437)
    bool IsActive(int q) => RecordOf(q)?.IsActive ?? false;        // IsQuestActive (:428)

    /// <summary>
    /// FUN_140e770a0, NotifyFadeInComplete: the field's field quests in order; the first that is active and not opened
    /// before is marked opened and its talk starts.
    /// </summary>
    public void OnFadeInComplete()
    {
        foreach (int q in FieldQuests(FieldId()))
        {
            if (!IsActive(q)) continue;
            if (Opened.Contains(q)) continue;
            Opened.Add(q);
            StartQuestTalk(q);
            break;
        }
    }

    /// <summary>FUN_140e771b0, NotifyChangeMyPC: ClearOpenedQuests (:@0x140e77300).</summary>
    public void ClearOpenedQuests() => Opened.Clear();

    /// <summary>UIQuestEventListener slot 1 (vfunc_o178_1), a quest the server started: a field quest of this field.</summary>
    public void OnQuestStarted(int q)
    {
        if (QuestOf(q) is not { Type: 8 }) return;      // CQuest::GetQuestType
        if (!QuestOnField(q)) return;
        StartQuestTalk(q);
    }

    /// <summary>UIQuestEventListener slot 3 (vfunc_o178_3), a quest's progress: CQuest::FUN_140d738d0.</summary>
    public void OnQuestProgress(int q)
    {
        if (QuestOf(q) is not { } d || RecordOf(q) is not { } r) return;    // the listener gets the CQuest, so a record
        if (!QuestCommon.Dispatch738d0(d, r, Inputs())) return;
        StartQuestTalk(q);
    }

    /// <summary>FUN_140e77b80: the quest's progressMap (m_fields) has the current field.</summary>
    bool QuestOnField(int q)
    {
        if (QuestOf(q) is not { } d) return false;
        uint field = (uint)FieldId();
        return Array.IndexOf(d.Fields, field) != -1;
    }

    /// <summary>
    /// StartQuestTalk (:@0x140e771e0): with feature FameContents, the quest's talk script 300 when it is complete, else 100;
    /// with one, the talk starts on its first line: the movie opens (FUN_141924680) or, loaded, shows it.
    /// </summary>
    public void StartQuestTalk(int q)
    {
        if (!FeatureEnabled(FameContentsFeature)) return;
        int step = IsComplete(q) ? 300 : 100;
        script = FindQuestTalk(q, step);
        if (script == null) return;
        quest = q;
        line = 0;
        if (!loaded) Open();
        else ShowCurrentLine();
    }

    /// <summary>
    /// UIDialog::FUN_141924680 (uidialog.cpp:445): vfunc_65 opens only a closed dialog (vfunc_55, uidialog.cpp:487) and marks
    /// it open (m_68 0), so a second call while it loads does nothing. STILL-OPEN: the vfunc_58 local player test and
    /// ClosePopupMenu are not ported.
    /// </summary>
    void Open()
    {
        if (opening || !IsClosedOrUnhosted) return;
        opening = true;
        OpenRequested?.Invoke();
    }

    bool IsClosedOrUnhosted => Host == null || IsClosed;

    /// <summary>OnLoadComplete (:@0x140e76ab0): UIDialog's, then the current line; none (0) closes the dialog.</summary>
    public override void OnLoadComplete()
    {
        loaded = true;
        opening = false;
        base.OnLoadComplete();
        if (ShowCurrentLine() == 0) Close();
    }

    /// <summary>vfunc_67 (:@0x140e76a50): UIDialog's close, then no quest, line or script. Before the movie loaded there is
    /// nothing to close: the dialog is only marked closed again (m_68), so a later talk opens it.</summary>
    public override void Close()
    {
        if (loaded) base.Close();
        else opening = false;
        quest = 0;
        line = 0;
        script = null;
    }

    /// <summary>
    /// ShowCurrentLine (:@0x140e77330): 0 without the quest's data or a script, 2 past the last line, else 1 with the line
    /// shown: the speaker's portrait, the npc (the complete npc m_2ec of a complete quest, else the start npc m_2e8), the
    /// text after the reserved words, m_end ("s_field_quest_talk_end" with the name of key 1, Esc) shown except on the
    /// last line, and m_next "s_field_quest_talk_next" with the talk key's name, on the last line
    /// "s_field_quest_talk_complete" for a complete quest, else "s_field_quest_talk_ok".
    /// </summary>
    public int ShowCurrentLine()
    {
        if (QuestOf(quest) is not { } d) return 0;
        if (script == null) return 0;
        if (line >= script.Count) return 2;
        bool done = IsComplete(quest);
        if (Host?.Entity == null) return 1;
        var content = script[line];
        // XML quest contents are always CCinematicContent (talkscript.cpp:594), so the portrait is always set
        Host.Invoke(Host.Entity, "setPortrait", content.SpeakerIllust ?? "");
        Host.Invoke(Host.Entity, "setNpc", NpcName(done ? d.CompleteNpc : d.StartNpc) ?? "");
        if (Host.Child("m_script") is { } text) Host.Set(text, "htmlText", ReplaceWords(content.Text ?? ""));   // SetTextHTMLW
        bool last = line == script.Count - 1;
        if (Host.Child("m_end") is { } end)
        {
            if (!last)
            {
                Host.Set(end, "label", Common("s_field_quest_talk_end", Model.Keys.GetKeyName(1)));   // FUN_1422cdfb0
                Host.SetVisible(end, true);                                                          // vfunc_3(1)
            }
            else Host.SetVisible(end, false);
        }
        if (Host.Child("m_next") is { } next)
        {
            string key = !last ? "s_field_quest_talk_next" : done ? "s_field_quest_talk_complete" : "s_field_quest_talk_ok";
            // CKeyTable::GetActionKeyName(0x493e2, 0)
            Host.Set(next, "label", Common(key, Model.Keys.GetKeyName(Model.Keys.FindKeyByActionAndPriority(TalkAction, 0))));
        }
        return 1;
    }

    string Common(string key, params object[] args) => ClientFormat.Format(Model.Tables.Common(key) ?? "", args);   // FUN_14028dfa0

    /// <summary>Past the last line (state 2): Quest mode 4 for a complete quest, else the opening banner.</summary>
    void Finish()
    {
        if (IsComplete(quest)) SendComplete?.Invoke(quest);
        else OpeningBanner?.Invoke(quest);
    }

    /// <summary>vfunc_28 (:@0x140e76af0), after UIDialog's: onClickNext moves to the next line, and when none is shown
    /// finishes (state 2) and closes; onClickEnd closes.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (string.Equals(name, "onClickNext", StringComparison.OrdinalIgnoreCase))
        {
            line++;
            int state = ShowCurrentLine();
            if (state != 1)
            {
                if (state == 2) Finish();
                Close();
            }
            return true;
        }
        if (string.Equals(name, "onClickEnd", StringComparison.OrdinalIgnoreCase))
        {
            Close();
            return true;
        }
        return false;
    }

    /// <summary>The keys vfunc_13 (:@0x140e76e70) tests.</summary>
    public enum PopupKey { Escape, Talk }

    /// <summary>
    /// vfunc_13 (:@0x140e76e70), on an open dialog (not vfunc_55): Esc queues the opening banner of a quest that is not
    /// complete and closes (vfunc_69: vfunc_71 is 0); the talk key moves to the next line and past the last one finishes and
    /// closes (a missing quest, state 0, does not close here). It returns 0: the key is not taken. STILL-OPEN: the vfunc_56
    /// test (UIDialog flag 0, uidialog.cpp:494) is not ported; this dialog does not set the flag that is known.
    /// </summary>
    public void Key(PopupKey key)
    {
        if (Host == null || IsClosed) return;
        if (key == PopupKey.Escape)
        {
            if (!IsComplete(quest)) OpeningBanner?.Invoke(quest);
            Close();
            return;
        }
        line++;
        if (ShowCurrentLine() == 2)
        {
            Finish();
            Close();
        }
    }

    /// <summary>vfunc_43 (:@0x140e76db0): bottom centre, FUN_1418dd0d0 above the bottom: the bottom bar's height - 2, the
    /// adventure bar's - 2 with feature 0xbd (FUN_1418dd110) and the margin.</summary>
    public override void Layout(double stageW, double stageH)
        => Host.Align(Host.Entity, 2, 0, -(BottomBarHeight - 2
            + (Model.IsFeatureEnabled(MainPlayerInfoController.AdventureLevelFeature) ? AdventureBarHeight - 2 : 0) + ScreenMargin));
}
