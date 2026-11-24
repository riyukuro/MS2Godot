using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIGlobalPortalDialog (client/ms2/uiglobalportaldialog.cpp, "GlobalPortalDialog", UI id 0x4ed7, movie uiglobalportaldialog): the
/// massive event entrance notice. The GlobalPortal packet's mode 0 (<see cref="OnPacket"/>) sets the event, its text, sound and up
/// to three enter-button captions, and arms a random 0-10 s delay; the per-frame update (<see cref="Update"/>, which the client
/// runs even while the dialog is closed: the ctor's SetFlag(3, 1), uiservice.cpp:1927) then opens the dialog, or refreshes it when
/// open. An enter button sends GlobalPortal 2 with the event and the button (<see cref="GlobalPortalEnter"/>). Mode 1 ends the
/// event and closes the dialog. The ctor (@0x1412c2020) also sets SetTweenEnabled(1) and SetFlag(0, 1): an always-on dialog.
/// STILL-OPEN: FUN_1412c3240 (:244), which remembers the shown event and param and closes the dialog (so the same notice does not
/// open again), has no caller in the recovered code (it is reached through a pointer); it is not wired, so a notice sent again
/// with the same event and param opens again.
/// </summary>
public sealed class GlobalPortalController : UiController
{
    public const int UiId = 0x4ed7;
    /// <summary>CStateService::GetCurrentSceneId of the game scene (CSceneStateGame).</summary>
    public const int GameSceneId = 0x620000c;

    public override string Movie => "uiglobalportaldialog";
    protected override string EntityMethod => "alignment";        // Dialog's
    public override string TypeName => "GlobalPortalDialog";      // GetTypeName (@0x1412c22b0)
    public override bool AlwaysOn => true;                          // ctor SetFlag(0, 1)

    /// <summary>The string tables: the packet is read while the movie may not be loaded (no host yet).</summary>
    public GameTables Tables;
    /// <summary>GetTickCountMs2.</summary>
    public Func<uint> NowMs = () => (uint)Environment.TickCount;
    /// <summary>GetRandomRange(10000, 0): a draw in 0 to 9999.</summary>
    public Func<int> RetryDelay = () => Random.Shared.Next(10000);
    /// <summary>WorldService::GetLocalPlayer's level (vfunc_78); null without a local player.</summary>
    public Func<int?> PlayerLevel = () => null;
    /// <summary>Client::Constants GlobalPortalMinLevel (+0xe150); 0 is no limit.</summary>
    public Func<int> MinLevel = () => 0;
    /// <summary>CStateService::GetCurrentSceneId.</summary>
    public Func<int> SceneId = () => GameSceneId;
    /// <summary>FUN_141924630: the dialog is open (not closed).</summary>
    public Func<bool> IsOpen;
    /// <summary>FUN_141924680: the host loads the movie (the open of UIDialog, uidialog.cpp:445).</summary>
    public Action OpenRequested;
    /// <summary>ShowSystemNotice(text, type, log).</summary>
    public Action<string, int, bool> Notice;
    /// <summary>PlaySystemSound(name, "").</summary>
    public Action<string> PlaySound;
    /// <summary>FUN_140710590: the packet out.</summary>
    public Action<IOutPacket> Send;

    int eventId;             // m_nEventId
    int param;               // m_nParam
    string text = "";        // m_strText (the key's common string)
    string sound = "";       // m_strSound
    readonly string[] captions = { "", "", "" };   // m_strCaption1..3 (common string keys)
    readonly object[] buttons = new object[3];     // m_enterButton1..3
    int shownEventId, shownParam;                  // m_nShownEventId, m_nShownParam
    uint retryTime;                                 // m_nRetryTime

    public GlobalPortalController()
    {
        Unloaded += ClearButtons;
    }

    public int EventId => eventId;
    public uint RetryTime => retryTime;
    public string Text => text;

    /// <summary>OnGlobalPortalPacket (@0x1412c3020, :216): mode 0 reads the notice, 1 ends the event, 3 and 4 show the dead and the
    /// level limit notices (ShowSystemNotice(key text, 1, 1)); another mode does nothing.</summary>
    public void OnPacket(GlobalPortalPacket p)
    {
        switch (p)
        {
            case GlobalPortalPacket.Notice n: ReadNotice(n); break;
            case GlobalPortalPacket.End: EventOver(); break;
            case GlobalPortalPacket.Other { Mode: GlobalPortalPacket.DeadMode }:
                Notice?.Invoke(Common("s_massive_event_error_cant_enter_dead"), 1, true); break;
            case GlobalPortalPacket.Other { Mode: GlobalPortalPacket.LevelLimitMode }:
                Notice?.Invoke(Common("s_massive_event_error_cant_enter_level_limit"), 1, true); break;
        }
    }

    /// <summary>FUN_1412c32a0 (:252): the event, its param, the text (GetStringCommonByKey of the key), the sound and the captions;
    /// the same event and param as the last shown one stop there, else the shown pair is cleared and the open is armed for a
    /// random 0-10 s from now.</summary>
    void ReadNotice(GlobalPortalPacket.Notice n)
    {
        eventId = n.EventId;
        param = n.Param;
        text = Common(n.TextKey);
        sound = n.Sound ?? "";
        captions[0] = n.Caption1 ?? "";
        captions[1] = n.Caption2 ?? "";
        captions[2] = n.Caption3 ?? "";
        if (eventId == shownEventId && param == shownParam) return;
        shownEventId = 0;
        shownParam = 0;
        retryTime = NowMs() + (uint)RetryDelay();
    }

    /// <summary>FUN_1412c3640 (:268): the event is cleared, and an open dialog is closed (vfunc_67).</summary>
    void EventOver()
    {
        eventId = 0;
        if (IsOpen?.Invoke() == true) Close();
    }

    /// <summary>
    /// vfunc_72 (@0x1412c2880): nothing until the armed time; without a local player it waits; a level under GlobalPortalMinLevel
    /// (when set) disarms it. Else disarmed, and in the game scene the dialog is opened when closed (FUN_141924680) or refreshed
    /// (FUN_1412c2980).
    /// </summary>
    public void Update()
    {
        if (retryTime == 0) return;
        if (NowMs() < retryTime) return;
        if (PlayerLevel() is not { } level) return;
        int limit = (ushort)MinLevel();
        if (limit != 0 && (ushort)level < limit)
        {
            retryTime = 0;
            return;
        }
        retryTime = 0;
        if (SceneId() != GameSceneId) return;
        if (IsOpen?.Invoke() != true) OpenRequested?.Invoke();
        else Refresh();
    }

    /// <summary>OnLoadComplete (@0x1412c2300): UIDialog's, the close button bound, the three enter buttons bound to the send with
    /// their index (0 to 2), then the texts (FUN_1412c2980).</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        BindCloseButton("m_closeButton");
        for (int i = 0; i < 3; i++)
        {
            buttons[i] = Host.Child("m_enterButton" + (i + 1));
            if (buttons[i] == null) continue;
            int index = i;
            Func<object[], object> f = _ => { SendEnter(index); return null; };
            Host.Invoke(buttons[i], "addEventListener", "buttonClick", f, false, 0, false);
        }
        Refresh();
    }

    /// <summary>BindCloseButton (uidialog.cpp:977): the button's click handler is the close.</summary>
    void BindCloseButton(string name)
    {
        if (Host.Child(name) is not { } b) { Host.RT.NoteMissing("global portal dialog: " + name); return; }
        Func<object[], object> f = _ => { Close(); return null; };
        Host.Invoke(b, "addEventListener", "buttonClick", f, false, 0, false);
    }

    /// <summary>The bottom bar heights and margin FUN_1418dd0d0 sums (uibuiltindialog.cpp:53), as PopupNpcTalk keeps them.</summary>
    public int BottomBarHeight = 21, AdventureBarHeight = 21, ScreenMargin = 193;

    /// <summary>FUN_1418dd0d0: (DAT_1439f8ccc - 2) + the adventure bar's height - 2 with feature 0xbd + DAT_1439f8cc8.</summary>
    public int BottomInset
        => BottomBarHeight - 2 + (Model.IsFeatureEnabled(MainPlayerInfoController.AdventureLevelFeature) ? AdventureBarHeight - 2 : 0) + ScreenMargin;

    /// <summary>vfunc_43 (@0x1412c2730): UIDialog's, then the entity aligned bottom right (8), 0x3c over the bottom margin.</summary>
    public override void Layout(double stageW, double stageH)
    {
        base.Layout(stageW, stageH);
        if (Host.Entity == null) return;
        Host.Align(Host.Entity, 8, 0, -(BottomInset + 0x3c));
    }

    /// <summary>vfunc_67 (@0x1412c27d0): UIDialog's close, then the buttons and the captions forgotten.</summary>
    public override void Close()
    {
        base.Close();
        ClearButtons();
        captions[0] = captions[1] = captions[2] = "";
    }

    void ClearButtons()
    {
        buttons[0] = buttons[1] = buttons[2] = null;
    }

    /// <summary>The caption of each enter button as last set (empty for a hidden one), for checks.</summary>
    public IReadOnlyList<string> ButtonLabels => labels;
    readonly string[] labels = { "", "", "" };

    /// <summary>
    /// FUN_1412c2980 (:156): with the movie loaded, m_text's textField gets the text; with no caption the dialog closes (vfunc_67);
    /// else each bound button with a caption is shown with s_massive_event_enter_button of the caption's common string, one without
    /// is hidden, and the sound (when set) plays.
    /// </summary>
    void Refresh()
    {
        if (Host?.Entity == null) return;
        if (Host.Child("m_text") is Ms2.Avm2.Flash.AsDisplayObject t) Host.SetChildText("textField", text, t);   // SetNestedChildText
        if (captions[0].Length == 0 && captions[1].Length == 0 && captions[2].Length == 0)
        {
            Close();
            return;
        }
        for (int i = 0; i < 3; i++)
        {
            if (buttons[i] == null) continue;
            if (captions[i].Length != 0)
            {
                Host.Set(buttons[i], "visible", true);
                labels[i] = ClientFormat.Format(Common("s_massive_event_enter_button"), Common(captions[i]));
                Host.Set(buttons[i], "label", labels[i]);      // FUN_1422cdfb0
            }
            else
            {
                Host.Set(buttons[i], "visible", false);
                labels[i] = "";
            }
        }
        if (sound.Length != 0) PlaySound?.Invoke(sound);
    }

    /// <summary>SendGlobalPortal (@0x1412c31a0, :233): GlobalPortal 2 with the event id and the button's index.</summary>
    void SendEnter(int button) => Send?.Invoke(new GlobalPortalEnter(eventId, button));

    /// <summary>The click of enter button <paramref name="button"/> (0 to 2), as the movie's buttonClick, for checks.</summary>
    public void ClickEnter(int button) => SendEnter(button);

    string Common(string key) => string.IsNullOrEmpty(key) ? "" : (Tables ?? Host?.Model.Tables)?.Common(key) ?? "";
}
