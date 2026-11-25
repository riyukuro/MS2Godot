using System;
using System.Collections.Generic;
using System.Linq;

namespace Ms2.Formats.Options;

// The C++ side of the client's options dialog (UIGameOptionDialog, client/ms2/uigameoptiondialog.cpp; the pages
// CBaseOption, client/ms2/baseoption.cpp, CGameOptionGraphicDefault, client/ms2/gameoption.cpp, CGameOptionSoundDefault,
// client/ms2/gameoptionstruct.cpp), without a UI: the movie's controls and the dialog's own calls go through
// IOptionTab and IGameOptionDialogHost. Method names follow the client's so the options movie binds to them.

/// <summary>The tab's controls as the pages reach them: Unk_GpDialog::GetTabCheckBox / GetTabSlider / GetTabRadioButton /
/// GetTabTextField / GetTabTextInput and the dropdown getter FUN_140cfcf10, all "getTabChild"(name, -1) on the movie. A
/// control the tab does not have reads as null and ignores writes, as the client skips a null pointer. A child of a clip
/// is named "clip.child" (m_playInstrumentStep's m_slider and m_isOffBgm).</summary>
public interface IOptionTab
{
    bool? IsSelected(string name);
    void SetSelected(string name, bool selected);
    float? GetValue(string name);
    void SetValue(string name, float value);
    int? GetSelectedIndex(string name);
    void SetSelectedIndex(string name, int index);
    string GetText(string name);
    void SetText(string name, string text);
    /// <summary>UIComponent::SetEnabled ("enabled").</summary>
    void SetEnabled(string name, bool enabled);
    /// <summary>ASDropdownMenu::ClearItems @0x1422dfc10 then AddItemRow @0x1422dfc70 per row ({label, index}); false when
    /// there is no such dropdown.</summary>
    bool SetItemRows(string name, IReadOnlyList<(string Label, int Index)> rows);
    /// <summary>ASDropdownMenu::SetMenuRowCount @0x1422df5f0 ("menuRowCount").</summary>
    void SetMenuRowCount(string name, int rows);
    /// <summary>GetDataProvider()->GetItemAt(GetSelectedIndex()) ("requestItemAt") and its "index" member; null when there is
    /// no dropdown or the item is undefined or null.</summary>
    int? GetSelectedItemIndex(string name);
    /// <summary>RefreshKeyList's list calls: getTabChild("m_keyList", category) keeps its scrollPosition, clears its provider
    /// (FUN_1422dd1d0), pushes the rows (AddItem), restores scrollPosition, invalidates (vfunc_24), then
    /// getTabComponent(tabType).refreshScrollBar(). False when there is no list.</summary>
    bool SetKeyRows(int category, int tabType, IReadOnlyList<KeyRow> rows);
    /// <summary>ASMovieClip::vfunc_3 on a clip found by getTabChild (show 1 / hide 0); false when there is no clip.</summary>
    bool SetClipShown(string name, bool shown);
    /// <summary>The resolution dropdown (FillResolutionCombo @0x14124d080): ClearItems, then SetScrollBar("export_scroll")
    /// for more than 6 rows or SetMenuRowCount(rows), AddItemRow({modeid, label}) per row, SetSelectedIndex, vfunc_24(true).
    /// False when the tab has no m_resolution.</summary>
    bool SetResolutionRows(string name, IReadOnlyList<ResolutionRow> rows, int selected);
    /// <summary>GetTabComponent(type)->Invoke("getResolution") (@0x14124bf20): the selected row's modeid, -1 for none.</summary>
    int GetResolution(int tabType);
}

/// <summary>CKeySettingParam (keytable.cpp LoadKeySettingTable): one row of an input tab's key list.</summary>
public sealed record KeySetting(long Action, int Type, int PrimaryLocked, int SecondaryLocked, int ThirdlyLocked);

/// <summary>What the input pages read of CKeyTable.</summary>
public interface IKeyTable
{
    /// <summary>GetSettingList: the category's rows, null for none.</summary>
    IReadOnlyList<KeySetting> GetSettingList(int category);
    int FindKeyByActionAndPriority(long action, int priority);
    string GetKeyName(int key);
    /// <summary>CStringTable::FUN_1418125b0: the action's label.</summary>
    string ActionName(long action);
    /// <summary>CKeyTable::SendKeysToServer (keytable.cpp:399; gameoptioninputdefault.cpp:392).</summary>
    bool SendKeysToServer() => false;
    /// <summary>CKeyTable::BackupKeys (@0x14142a510, keytable.cpp:568; gameoptioninputdefault.cpp:393,587).</summary>
    void BackupKeys() { }
    /// <summary>CKeyTable::RestoreKeysFromBackup (gameoptioninputdefault.cpp:595).</summary>
    void RestoreKeysFromBackup() { }
    /// <summary>CKeyTable::FUN_1414297e0 (@0x1414297e0, keytable.cpp:429) on DefaultKey.xml, nothing sent (ResetKeysToDefault passes 0,
    /// gameoptioninputdefault.cpp:473).</summary>
    void LoadDefaultKeyboardKeys() { }
    /// <summary>CKeyTable::FUN_141429e60 (@0x141429e60, keytable.cpp:500) on DefaultPSKey.xml (pad kind 2) or DefaultXboxKey.xml
    /// (kinds 1 and 3): the game pad keys only, then sent to the server (ResetKeysToDefault, gameoptioninputdefault.cpp:451-461).</summary>
    bool LoadDefaultPadKeys(int padKind) => false;
    /// <summary>CKeyTable::GetQuickSlotActionCount (@0x14142cbb0): the quick-slot actions m_quickSlotActions holds.</summary>
    int GetQuickSlotActionCount() => 0;
    /// <summary>CKeyTable::GetQuickSlotAction (@0x14142cbe0): the action of quick slot index, 0 past the end.</summary>
    long GetQuickSlotAction(int index) => 0;
    /// <summary>CKeyTable::FindKeyByQuickSlot (@0x14142cb40): the key bound to the slot's action at that priority, 0 for none.</summary>
    int FindKeyByQuickSlot(int index, int priority) => 0;
}

/// <summary>
/// What the input pages reach of the quick slots (the key table's page and the main player info's slots): the snapshot before a key
/// reset, the put-back on cancel and the refreshes (gameoptioninputdefault.cpp:394-398, 592-638).
/// </summary>
public interface IQuickSlots
{
    /// <summary>CKeyTable::FUN_140a700b0: the current quick slot page.</summary>
    int Page { get; }
    /// <summary>CKeyTable::GetBoundKeys (@0x14142c5d0): the keys with a valid entry, into the list (cleared first).</summary>
    void GetBoundKeys(List<int> into);
    /// <summary>CKeyTable::GetSkillIdByKey (@0x14142dad0).</summary>
    int SkillIdByKey(int key);
    /// <summary>CKeyTable::GetQuickSlotItemUidByKey (@0x14142ded0).</summary>
    long ItemUidByKey(int key);
    /// <summary>CKeyTable::FindQuickSlotBySkillAndItemUid (@0x14142e090): -1 for none.</summary>
    int FindSlot(int skillId, long itemUid);
    /// <summary>FUN_14092f690 (QuickSlotEdit.Add): puts the entry on the page's slot and sends KeyTable mode 3 (not in a skill set).</summary>
    void Put(int skillId, int itemId, long itemUid, int page, int slot);
    /// <summary>UIMainPlayerInfo::RefreshQuickSlots (@0x1419f19c0), when the main player info (0x4e5f) is open.</summary>
    void Refresh();
    /// <summary>UIMainPlayerInfo::UpdateModifiedKeys (@0x1419f1e60), when the main player info (0x4e5f) is open.</summary>
    void UpdateModifiedKeys();
}

/// <summary>One row of RefreshKeyList's list: keyGUID is the action as a string, or the int 0x49409.</summary>
public sealed record KeyRow(string Label, object KeyGuid, string PrimaryKey, string SecondaryKey, string ThirdlyKey,
    bool PrimaryEnable, bool SecondaryEnable, bool ThirdlyEnable, int IsGamePad);

/// <summary>What the dialog and its pages call outside the movie's tab.</summary>
public interface IGameOptionDialogHost
{
    IOptionTab Tab { get; }
    /// <summary>The movie root's Invoke (m_30->Invoke): "initData", "setTabComponent", "enableAgreementTabButtonRedCircle".</summary>
    void Invoke(string method, params object[] args);
    /// <summary>A root Invoke read as text (FUN_141277dd0): "getCurrentTabName", "getCurrentCategoryName".</summary>
    string InvokeString(string method);
    /// <summary>FUN_140d73bc0(id, 1, this, tag): a yes/no box of a common string; the answer comes back through
    /// <see cref="GameOptionDialog.OnMessage"/> with the tag.</summary>
    void MessageBox(int stringId, string tag);
    /// <summary>FUN_140d73bc0(id, 0, 0, ""): a notice box of a common string, no answer.</summary>
    void Notice(int stringId);
    /// <summary>CreateMessageBox(0x18) with three buttons, the title, and FUN_140d73c60 (@0x141943a30). The answer comes
    /// back through <see cref="GameOptionDialog.OnMessage"/> with an empty tag: 0 all pages, 1 this page, 2 cancel.</summary>
    void ResetBox(string title, string allPages, string currentPage, string cancel);
    /// <summary>GetStringCommon(id).</summary>
    string Common(int id);
    /// <summary>GetStringCommonByKey(key).</summary>
    string CommonByKey(string key);
    /// <summary>FormatStringId(id, args...): common string id formatted with the client's {n} / {n:spec} fields.</summary>
    string FormatStringId(int id, params object[] args);
    /// <summary>m_apply.SetEnabled (SetApplyEnabled @0x141943820).</summary>
    void SetApplyEnabled(bool enabled);
    /// <summary>UIDialog::vfunc_67: the dialog closes.</summary>
    void CloseDialog();
    /// <summary>UIDialog::vfunc_55: the dialog is closed; checked before the tab and key handlers.</summary>
    bool IsClosed { get; }
    bool IsFeatureEnabled(int feature);
    /// <summary>MS2InputService::GetPadKind (m_nPadKind, FUN_140a58540): 0 none, 1 XInput pad, 2 DualShock pad, 3 Steam pad.</summary>
    int GetPadKind() => 0;
    /// <summary>CreateMessageBox(6, s_gameoption_input_reset_message), AddStringArg(<paramref name="padArg"/>), SetButtonPreset(2) and
    /// the callback FUN_1412523d0 (vfunc_12 @0x141251e90): the callback gets the clicked button and the string arg.</summary>
    void ResetMessageBox(string padArg, Func<int, string, bool> callback) { }
    /// <summary>DAT_143ab2914, compared with 0x32 for bNewBuild.</summary>
    int BuildNumber { get; }
    /// <summary>FUN_140651390, the locale passed to initData.</summary>
    string Locale { get; }
    /// <summary>GFxSceneService::GetSceneId.</summary>
    int SceneId { get; }
    /// <summary>CGuideManager::IsRestricted(n).</summary>
    bool GuideRestricted(int n);
    /// <summary>CGameOption::SaveGameSettings.</summary>
    void SaveGameSettings();
    /// <summary>CGameOption::SaveCharacterSettings.</summary>
    void SaveCharacterSettings();
    /// <summary>CBuddyManager::SendBuddyMode20.</summary>
    void SendBuddyMode20();
    /// <summary>FUN_140d86260()->vfunc_24, the secondary password dialog.</summary>
    void OpenSecondaryPassword();

    /// <summary>The quick slots the input pages snapshot and put back; null = no HUD (nothing is snapshotted or put back).</summary>
    IQuickSlots QuickSlots => null;
    /// <summary>FUN_1412532f0's post: NotifySystem.Post(NotifyChangeGameOptionInput(optionId)).</summary>
    void NotifyChangeGameOptionInput(int optionId) { }

    /// <summary>CGameOption +0x1a0, the stored gamepad block (GetGamepadState @0x140a58500).</summary>
    GameOption.GamePad GamePad { get; }
    /// <summary>SetGamepadEnabled (@0x14125f670): the block's Use, the input service, the player info's skill slots, the upper
    /// hud's gamepad mode, then SaveCharacterSettings. <paramref name="stored"/> is the block whose Use is set.</summary>
    void SetGamepadEnabled(GameOption.GamePad stored, bool use, bool notify);
    /// <summary>CGameOption +0x1b4, the stored agreement (GetPersonalInfoAgreement @0x140a46860).</summary>
    PersonalInfoAgreement Agreement { get; }
    /// <summary>CPersonalInfoAgreement::SendAgreement (@0x141261dd0): SendOp 0xC0 with the group and the stored flags.</summary>
    void SendAgreement(int group);
    /// <summary>CPersonalInfoAgreement::FUN_141262090 (@0x141262090): the reminders follow whether group 0 is agreed.</summary>
    void RefreshAgreementReminders();
    /// <summary>CPersonalInfoAgreement::OpenLink (@0x141262650) of the detail page.</summary>
    void OpenAgreementDetail();

    // Graphic page applies (FUN_14124ce20 @0x14124ce20, vfunc_10 @0x14124d6a0)
    GameOption Options { get; }
    ScreenModes Modes { get; }
    /// <summary>The adapter name for m_devicename (GPUManager::GetAdapterName).</summary>
    string AdapterName { get; }
    /// <summary>CGameOption::GetBenchmarkLevel.</summary>
    int BenchmarkLevel { get; }
    /// <summary>CGraphicOptionTable::IsHdrSupported / IsShimmerSupported (graphicoptiontable.cpp:120-146): the level is at
    /// least the table's "hdr" / "shimmer" level (Table/graphicOption.xml), false when the table has none.</summary>
    bool IsHdrSupported(int level);
    bool IsShimmerSupported(int level);
    /// <summary>CGameOption::m_docGameDefault (FUN_1412442a0), DefaultGameOption.xml of the default option folder
    /// (GetOptionFilePath(0, true), gameoption.cpp:1693-1698); null when it could not be read.</summary>
    System.Xml.Linq.XDocument DefaultGameDocument { get; }
    /// <summary>GetSystemMetrics(SM_CXSCREEN / SM_CYSCREEN).</summary>
    (int Width, int Height) ScreenSize { get; }
    /// <summary>FUN_14151f6e0 (messageboxqueue.cpp:110): the message box layer takes a box.</summary>
    bool MessageBoxFree { get; }
    /// <summary>FUN_14151f200 (messageboxqueue.cpp): the shown box closes and the newest queued one shows, as a listener does
    /// on its answer.</summary>
    void NextMessageBox();
    /// <summary>The window request of FUN_14125e910 (ScreenModes.Switch) to carry out.</summary>
    void ApplyWindow(WindowRequest request);
    /// <summary>SetQuality(state, quality, 0) on the page state (@0x14125e1f0).</summary>
    void SetQuality(GameOption.Graphic state, int quality);
    /// <summary>SetHdr (@0x14125e300).</summary>
    void SetHdr(GameOption.Graphic state, bool on);
    /// <summary>SetShimmer (@0x14125e3b0).</summary>
    void SetShimmer(GameOption.Graphic state, bool on);
    /// <summary>CEffectService::FUN_1411a8880(step).</summary>
    void SetEffectStep(int step);
    /// <summary>OpenDialog / CloseDialog of dialog 0x4e7d, the frame rate.</summary>
    void ShowFrameRate(bool shown);
    /// <summary>WorldService FUN_141bd41d0 (on) / FUN_141bd4260 (off).</summary>
    void EnchantEffectChanged(bool on);

    // Sound page applies (vfunc_10 @0x1412585a0)
    /// <summary>CGameOption::FUN_1411b1f30, the sound flag block (disableJobSound).</summary>
    SoundFlag SoundFlag { get; }
    /// <summary>The instrument manager has refs (FUN_14125fe20's bgm test).</summary>
    bool InstrumentPlaying { get; }
    /// <summary>g_bAppActive.</summary>
    bool AppActive { get; }
    /// <summary>The volumes of FUN_14125fe20 for CSoundEventMgr and the sound service.</summary>
    void ApplySoundVolumes(SoundVolumes volumes);
    /// <summary>CInstrumentManager::UpdateVolume(true).</summary>
    void UpdateInstrumentVolume();

    // ---- the Game pages (Camera, Interface, Status, Battle, Community) ----
    /// <summary>CGameOption's account block (m_account, AccountOption.xml), the Community page's request switches.</summary>
    GameOption.AccountCommunity AccountCommunity { get; set; }
    /// <summary>CGameOption::FUN_1412442c0, DefaultAccountOption.xml (the Community page's reset).</summary>
    System.Xml.Linq.XDocument DefaultAccountDocument { get; }
    /// <summary>GetInterfaceChar: the character file's "interface"/"EnableMouseControl".</summary>
    GameOption.CharacterFlag MouseControlFlag { get; }
    /// <summary>GetStatusChar: the character file's "status"/"displayLearningQuestV2".</summary>
    GameOption.CharacterFlag LearningQuestFlag { get; }
    // ---- the input pages ----
    /// <summary>GetKeyTable().</summary>
    IKeyTable Keys { get; }

    // ---- the Language page ----
    /// <summary>DAT_143ab0248, the client region (0 KR, 1 JP, 2 NA, 3 CN).</summary>
    int Region { get; }
    /// <summary>CLocaleLanguage::GetLocaleData(0) @0x14064f070: gameoptionlocale.xml's row for the region (the last row in
    /// debug mode); null without the table.</summary>
    GameOptionLocale.Row LocaleData { get; }
    /// <summary>GetVoice @0x1406516c0: the voice set at start (CGameOption::LoadLanguageOption, gameoption.cpp:1847).</summary>
    int RunningVoice { get; }
    /// <summary>_access(path, 0) == 0: the CN voice check's sound files.</summary>
    bool FileExists(string path);
    /// <summary>ShowMessageBox(text, 0, 0, ""): a notice with the given text.</summary>
    void TextNotice(string text);
    /// <summary>CMouseController::HasMode: the mouse camera is on (GetCameraFovStep / SetCameraFovStep pick its fov step).</summary>
    bool MouseCameraMode { get; }
    /// <summary>ShowMessageBox(BlockFormat(id), 1, page, tag): a yes/no box whose answer goes to the page's OnMessage.</summary>
    void PageMessageBox(int stringId, string tag, OptionPage listener);
    /// <summary>CGameOptionCamera::vfunc_10's camera side (@0x141248f10): the field's camera option switch, CameraMgr's
    /// scale and loose steps, and BGTransparencyMgr::SetUserFlags.</summary>
    void ApplyCamera(GameOption.Camera camera);
    /// <summary>CGameOptionInterface::vfunc_10's dialogs (@0x1412569a0): the damage meters (0x4edf, 0x4f23) open or close
    /// by their switches, and the damage effect (0x4e3c) takes the alpha.</summary>
    void ApplyInterface(GameOption.Interface state);
    /// <summary>FUN_14125bd20: CMouseController::SetEnabled and the upper hud's input mode.</summary>
    void SetMouseControl(bool on);
    /// <summary>UISwfEntityManager::ResetLayout: every dialog's saved place is dropped.</summary>
    void ResetUiLayout();
    /// <summary>CGameOptionInterface::vfunc_11 (@0x141256b50): CUIService::FUN_141adbe00(scale, 0), and when the scale
    /// changed, UISwfEntityManager::vfunc_6.</summary>
    void UiScaleChanged(float scale);
    /// <summary>CGameOptionStatus::vfunc_10's hud side (@0x14125ae50): the upper hp bar (0x4e3d) and the exp bars
    /// (0x4e27, 0x4f66) follow the switches.</summary>
    void ApplyStatus(GameOption.Status state);
    /// <summary>MyPC::SendRequestRideMode6 (CGameOptionCommunity::vfunc_10, @0x14124a8d0).</summary>
    void SendRideMode(bool disableSingleRiding);
}

/// <summary>Unk_SoundFlag: "sound"/"disableJobSound" (FUN_1412603d0 / FUN_141260410, gameoptionstruct.cpp:2823-2836).</summary>
public sealed class SoundFlag
{
    public bool DisableJobSound;
    public void Load(System.Xml.Linq.XDocument d) => GameOption.Xml.ReadBool(d, "sound", "disableJobSound", ref DisableJobSound);
    public void Save(System.Xml.Linq.XDocument d) => GameOption.Xml.WriteBool(d, "sound", "disableJobSound", DisableJobSound);
    public SoundFlag Clone() => (SoundFlag)MemberwiseClone();
}

/// <summary>One row of the resolution dropdown: the adapter mode index and the label (@0x14124d080).</summary>
public readonly record struct ResolutionRow(int ModeId, int Width, int Height, string Label);

/// <summary>The sound volumes FUN_14125fe20 (gameoptionstruct.cpp:2727-2752) sets: CSoundEventMgr::SetCategoryVolume for
/// categories 0..10, SetAllMute, CInstrumentManager::Mute, and the sound service's main volume.</summary>
public sealed record SoundVolumes(float[] Category, bool AllMute, bool MuteInstruments, float MainVolume)
{
    /// <summary>The movie volume of the same state (Unk_SoundState::GetMovieVolume, gameoptionstruct.cpp:2786). FUN_14125fe20
    /// does not set it: the movie players read it from the state when they play (uicutscenemovie.cpp:397, 413). It rides
    /// here so the movie bus takes the state being saved; the save applies the page state before it is stored
    /// (CGameOptionSoundDefault::vfunc_10, gameoptionstruct.cpp:645-648).</summary>
    public float MovieVolume { get; init; } = 1f;

    public static SoundVolumes From(GameOption.Sound p, bool instrumentPlaying, bool appActive)
    {
        float bgm = p.CategoryVolume(2);
        if (p.OffBgmWhilePlayInstrument && instrumentPlaying)
            bgm = 0f;
        var c = new float[11];
        c[0] = p.CategoryVolume(0);
        c[9] = p.CategoryVolume(0);
        c[1] = p.CategoryVolume(1);
        c[2] = bgm;
        c[3] = p.CategoryVolume(3);
        c[4] = p.CategoryVolume(4);
        c[7] = p.CategoryVolume(7);
        c[5] = p.CategoryVolume(5);
        c[8] = p.CategoryVolume(8);
        c[6] = p.CategoryVolume(6);
        c[10] = p.CategoryVolume(10);
        bool mute = p.MuteAll, muteInstruments = false;
        if (!appActive && !p.PlayAll)
        {
            mute = true;
            muteInstruments = true;
        }
        return new SoundVolumes(c, mute, muteInstruments, p.MainSoundStep * 10 / 100f) { MovieVolume = p.MovieVolume };
    }
}

/// <summary>CBaseOption (baseoption.cpp, @0x140a02bc0..): a page of the dialog. Id is the page map key, Type the order
/// it was created in (m_nType, the movie's tab index for GetTabComponent).</summary>
public class OptionPage
{
    public readonly int Id, Type;
    public bool Active { get; private set; }
    public bool Pending { get; private set; }
    public bool Enabled { get; set; }
    protected GameOptionDialog Dialog { get; private set; }
    protected IGameOptionDialogHost Host => Dialog.Host;
    protected IOptionTab Tab => Dialog.Host.Tab;

    public OptionPage(int id, int type) { Id = id; Type = type; }
    internal void Attach(GameOptionDialog dialog) => Dialog = dialog;

    /// <summary>CBaseOption::vfunc_2 (baseoption.cpp:35), OnTabInit: the tab's movie is ready; a pending fill runs now.</summary>
    public virtual void OnTabInit()
    {
        Active = true;
        if (Pending)
        {
            FillControls();
            Pending = false;
        }
    }

    /// <summary>CBaseOption::vfunc_3 (baseoption.cpp:46): the page opens (n 1) or the dialog refreshes (n 0): fill when active, else on OnTabInit.</summary>
    public virtual void Open(int n)
    {
        if (Active) FillControls();
        else Pending = true;
    }

    /// <summary>CBaseOption::vfunc_4 (baseoption.cpp:55): the page closes: n 0 deactivates, else reads the controls back.</summary>
    public virtual void Close(int n)
    {
        if (n == 0) Active = false;
        else ReadControls();
    }

    /// <summary>CBaseOption::vfunc_5 (baseoption.cpp:64): a movie call the dialog did not handle; nonzero when handled.</summary>
    public virtual int OnMovieCall(string method, object[] args) => 0;
    /// <summary>CBaseOption::vfunc_6 (baseoption.cpp:70): fill the controls from the page state.</summary>
    public virtual void FillControls() { }
    /// <summary>CBaseOption::vfunc_7 (baseoption.cpp:75): read the controls into the page state.</summary>
    public virtual void ReadControls() { }
    /// <summary>CBaseOption::vfunc_8 (baseoption.cpp:80): the page takes the key (UIGameOptionDialog::vfunc_13).</summary>
    public virtual int TakesKey() => 0;
    /// <summary>CBaseOption::vfunc_9 (baseoption.cpp:86): the changes are dropped (FUN_141943d80).</summary>
    public virtual void DropChanges() { }
    /// <summary>CBaseOption::vfunc_10 (baseoption.cpp:91): read the controls when active, then the page applies.</summary>
    public virtual void Save()
    {
        if (Active) ReadControls();
    }
    /// <summary>CBaseOption::vfunc_11 (baseoption.cpp:98): after SaveGameSettings.</summary>
    public virtual void AfterSave() { }
    /// <summary>CBaseOption::vfunc_12 (baseoption.cpp:103): reset to the defaults.</summary>
    public virtual void ResetToDefaults() { }
    /// <summary>CBaseOption::vfunc_13 (baseoption.cpp:108): each frame while the dialog is open (UIGameOptionDialog::vfunc_15).</summary>
    public virtual void Update() { }
    /// <summary>IMessageListener::OnMessage: the answer of a box the page opened (0 yes).</summary>
    public virtual void OnMessage(string tag, int answer) { }
    /// <summary>CBaseOption::vfunc_14 (baseoption.cpp:113): the page state differs from the stored option.</summary>
    public virtual int HasChanges() => 0;
}

/// <summary>UIGameOptionDialog (uigameoptiondialog.cpp).</summary>
public sealed class GameOptionDialog
{
    public readonly IGameOptionDialogHost Host;
    readonly SortedDictionary<int, OptionPage> pages = new();
    // the per-frame Update walks a copy of the pages (a page may change the set); it is rebuilt only when the set changes,
    // so a frame allocates nothing
    OptionPage[] frameSnapshot;
    public int Tab { get; private set; } = 1;
    public bool Simple { get; private set; }
    int resetScope;   // m_nF1cc
    /// <summary>FUN_141254a00 (m_nF1cc): the scope the last reset ran with (0 all pages, else the current page).</summary>
    public int ResetScope => resetScope;
    bool agreementRedCircle;   // m_bF1d0

    /// <summary>The ctor (@0x141941b80): the pages by key, typed in creation order. Pages the dialog does not port yet
    /// are plain CBaseOption pages.</summary>
    public GameOptionDialog(IGameOptionDialogHost host)
    {
        Host = host;
        int order = 1;
        void Add(int key, Func<int, int, OptionPage> make) => Add2(make(key, order++));
        void Add2(OptionPage p) { p.Attach(this); pages[p.Id] = p; frameSnapshot = null; }
        OptionPage Plain(int k, int t) => new(k, t);
        Add(1, (k, t) => new CameraOptionPage(k, t));
        Add(2, (k, t) => new InterfaceOptionPage(k, t));
        Add(3, (k, t) => new StatusOptionPage(k, t));
        Add(4, (k, t) => new BattleOptionPage(k, t));
        Add(5, (k, t) => new CommunityOptionPage(k, t));
        Add(6, (k, t) => new GraphicOptionPage(k, t));
        Add(7, (k, t) => new SoundOptionPage(k, t));
        bool gamePad = host.IsFeatureEnabled(0xb5);
        OptionPage Input(int k, int t) => new InputOptionPage(k, t);
        if (gamePad) Add(8, (k, t) => new GamePadOptionPage(k, t)); // CGameOptionGamePad
        Add(9, Input);                                   // CGameOptionInputDefault x5
        Add(10, Input);
        Add(11, Input);
        if (gamePad) Add(12, Input);
        Add(13, Input);
        Add(14, (k, t) => new LanguageOptionPage(k, t)); // CGameOptionLanguage
        Add(16, Plain);                                  // CGameOptionAccount
        if (host.IsFeatureEnabled(0x117)) Add(15, (k, t) => new AgreementOptionPage(k, t)); // CGameOptionPersonalInfoAgreement
    }

    /// <summary>The dtor (@0x141942810): the page map is emptied (RemoveAllPages).</summary>
    public void Dispose() => RemoveAllPages();

    /// <summary>UIGameOptionDialog::RemoveAllPages (@0x141943a00, uigameoptiondialog.cpp:427): the map drops every page.</summary>
    public void RemoveAllPages() { pages.Clear(); frameSnapshot = null; }

    /// <summary>vfunc_15 (@0x141942bf0), each frame: while the dialog is loaded and open, every page runs its frame step.</summary>
    public void Update()
    {
        if (Host.IsClosed) return;
        var snap = frameSnapshot ??= pages.Values.ToArray();
        for (int i = 0; i < snap.Length; i++) snap[i].Update();
    }

    /// <summary>UIGameOptionDialog::SetAgreementRedCircle (@0x141943900, uigameoptiondialog.cpp:410): the flag is stored and the
    /// agreement tab button follows it (CPersonalInfoAgreement::FUN_141262090 @0x141262090 calls this while group 0 is not agreed).</summary>
    public void SetAgreementRedCircle(bool show)
    {
        agreementRedCircle = show;
        UpdateAgreementRedCircle();
    }

    /// <summary>SetTab @0x141b15670 (inline in uisystemmenudialog.cpp): m_nTab, the page the next open shows.</summary>
    public void SetTab(int tab) => Tab = tab;

    public IEnumerable<OptionPage> Pages => pages.Values;
    public OptionPage Page(int key) => pages.TryGetValue(key, out var p) ? p : null;

    /// <summary>@0x141944460: the n-th page in map order, counted from 1. The map has 17 bins and keys 1..16, so its order
    /// is the key order.</summary>
    public OptionPage GetPageByIndex(int index) => index >= 1 && index <= pages.Count ? pages.Values.ElementAt(index - 1) : null;

    /// <summary>@0x1419445c0 (FUN_141944590 @0x141944590 is the call from OnLoadComplete): the key goes only when n is 0.</summary>
    public void RemovePage(int key, int n)
    {
        if (n == 0) { pages.Remove(key); frameSnapshot = null; }
    }

    /// <summary>@0x1419428d0. initData goes through the root Invoke(string, int, int) helper (@0x141944620), setTabComponent through Invoke(int, bool, bool) (@0x141944710).</summary>
    public void OnLoadComplete()
    {
        int newBuild = Host.BuildNumber >= 0x32 ? 1 : 0;
        int gamePad = Host.IsFeatureEnabled(0xb5) ? 1 : 0;
        RemovePage(0x10, newBuild);   // FUN_141944590
        Host.Invoke("initData", Host.Locale, newBuild, gamePad);
        ApplySimpleMode();
        OpenTab(Tab);
    }

    /// <summary>UIGameOptionDialog::FUN_141943480 (@0x141943480): simple mode in scenes 0x620000d / 0x6200009 / 0x6200085 or under guide restriction 1, where
    /// only the pages of type 6, 7 and 0xe are enabled and the graphics tab opens.</summary>
    public void ApplySimpleMode()
    {
        int scene = Host.SceneId;
        Simple = scene == 0x620000d || scene == 0x6200009 || scene == 0x6200085 || Host.GuideRestricted(1);
        if (Simple) Tab = 6;
        foreach (var p in pages.Values)
        {
            p.Enabled = !Simple || p.Type == 6 || p.Type == 7 || p.Type == 0xe;
            p.Open(0);
        }
        Host.Invoke("setTabComponent", Tab, Simple, false);
        UpdateAgreementRedCircle();
    }

    /// <summary>UIGameOptionDialog::FUN_141943940 (@0x141943940): the agreement tab button's red circle, when the page is there.</summary>
    public void UpdateAgreementRedCircle()
    {
        if (pages.ContainsKey(0xf))
            Host.Invoke("enableAgreementTabButtonRedCircle", agreementRedCircle);
    }

    /// <summary>@0x1419436e0.</summary>
    public void OpenTab(int tab)
    {
        Tab = tab;
        if (Host.IsClosed) return;
        GetPageByIndex(Tab)?.Open(1);
    }

    /// <summary>UIGameOptionDialog::ReopenCurrentTab (@0x1419438b0, uigameoptiondialog.cpp:403-408): reopens the active tab when the dialog is open.</summary>
    public void ReopenCurrentTab()
    {
        if (Host.IsClosed) return;
        GetPageByIndex(Tab)?.Open(1);
    }

    /// <summary>@0x141943790.</summary>
    public void OnCloseTab(int tab)
    {
        if (Host.IsClosed) return;
        GetPageByIndex(tab)?.Close(1);
    }

    /// <summary>vfunc_28 @0x141942cc0: the movie's ExternalInterface calls, names compared case-insensitively. True when
    /// handled here (the client then returns 1; the rest go on to UIDialog::vfunc_28).</summary>
    public bool Call(string method, params object[] args)
    {
        static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        if (Is(method, "OnCloseDialog")) { OnCloseDialog(); return true; }
        if (Is(method, "OnOpenTab")) { if (args.Length > 0) OpenTab(Convert.ToInt32(args[0])); return false; }
        if (Is(method, "OnCloseTab")) { OnCloseTab(Tab); return false; }
        if (Is(method, "OnUndo")) return true;
        if (Is(method, "OnInit")) { OpenResetBox(); return true; }
        if (Is(method, "OnSave")) { OnSave(); return true; }
        if (Is(method, "OnTabInit")) { if (args.Length > 0) GetPageByIndex(Convert.ToInt32(args[0]))?.OnTabInit(); return false; }
        if (Is(method, "OnSecondaryPassword")) { Host.OpenSecondaryPassword(); return true; }
        var page = GetPageByIndex(Tab);
        return page != null && page.OnMovieCall(method, args) != 0;
    }

    /// <summary>vfunc_13 @0x141942b10: a key while open. True when the page or the dialog took it; Esc closes.</summary>
    public bool OnKey(bool escape)
    {
        if (Host.IsClosed) return false;
        var page = GetPageByIndex(Tab);
        if (page != null && page.TakesKey() != 0) return true;
        if (!escape) return false;
        OnCloseDialog();
        return true;
    }

    /// <summary>UIGameOptionDialog::FUN_141943a30: the reset box. The category is the movie's, or s_gameoption_init_shoutcut on tabs 9..0xd.</summary>
    public void OpenResetBox()
    {
        string tab = Host.InvokeString("getCurrentTabName");
        string category = Host.InvokeString("getCurrentCategoryName");
        if (Tab >= 9 && Tab <= 0xd)
            category = Host.CommonByKey("s_gameoption_init_shoutcut");
        Host.ResetBox(Host.Common(0x847), Host.Common(0x848), Host.FormatStringId(0x849, tab, category), Host.Common(0x476));
    }

    /// <summary>@0x141943e30: 0x84a is s_gameoption_quickslot_warning.</summary>
    public void OnSave()
    {
        if (!CanSaveQuickSlots()) Host.MessageBox(0x84a, "OnSave");
        else SaveAll();
    }

    /// <summary>UIGameOptionDialog::FUN_141943ec0 (@0x141943ec0): every page saves, the option file is written, then every page's vfunc_11.</summary>
    public void SaveAll()
    {
        foreach (var p in pages.Values.ToList()) p.Save();
        Host.SaveGameSettings();
        foreach (var p in pages.Values.ToList()) p.AfterSave();
        Host.SendBuddyMode20();
    }

    /// <summary>UIGameOptionDialog::FUN_141943d80 (@0x141943d80): every page drops its changes.</summary>
    public void DropAllChanges()
    {
        foreach (var p in pages.Values.ToList()) p.DropChanges();
    }

    /// <summary>UIGameOptionDialog::FUN_141944030 (@0x141944030): reset this page (n != 0) or all pages.</summary>
    public void ResetPages(int n)
    {
        resetScope = n;
        if (n != 0) GetPageByIndex(Tab)?.ResetToDefaults();
        else foreach (var p in pages.Values.ToList()) p.ResetToDefaults();
    }

    /// <summary>@0x141944150: 0x84b is s_gameoption_modified_warning.</summary>
    public void OnCloseDialog()
    {
        if (!IsNoPageChanged()) Host.MessageBox(0x84b, "OnSaveAndCheck");
        else if (!CanSaveQuickSlots()) Host.MessageBox(0x84a, "OnSaveAndCheck");
        else Close();
    }

    /// <summary>UIGameOptionDialog::FUN_141944250 (@0x141944250): simple mode skips the test; otherwise every quick-slot action
    /// except the last three (0x7a137, 0x7a138, 0x7a139) needs a primary key.</summary>
    public bool CanSaveQuickSlots()
    {
        if (Simple) return true;
        var keys = Host.Keys;
        if (keys == null) return true;
        int n = keys.GetQuickSlotActionCount();
        for (int i = 0; i < n; i++)
        {
            long a = keys.GetQuickSlotAction(i);
            if (a == 0x7a137 || a == 0x7a138 || a == 0x7a139) continue;
            if (keys.FindKeyByQuickSlot(i, 0) == 0) return false;
        }
        return true;
    }

    /// <summary>@0x141944330: the open page reads its controls back, then any page that differs counts.</summary>
    public bool IsNoPageChanged()
    {
        GetPageByIndex(Tab)?.ReadControls();
        return !pages.Values.Any(p => p.HasChanges() != 0);
    }

    /// <summary>OnMessage @0x141943010: a message box answer (0 yes / first button, 1 no / second, 2 cancel, -1 none), after
    /// FUN_14151f200 closes the box.</summary>
    public void OnMessage(string tag, int answer)
    {
        Host.NextMessageBox();
        if (tag == "OnSave")
        {
            if (answer == 0)
            {
                SaveAll();
                Host.SetApplyEnabled(false);
            }
        }
        else if (tag == "OnSaveAndCheck")
        {
            if (answer == 0) SaveAll();
            else DropAllChanges();
            Close();
        }
        else if (tag == "OnClose")
        {
            if (answer == 0) Close();
        }
        else
        {
            if (answer == 0) ResetPages(0);
            else if (answer == 1) ResetPages(1);
            if (answer != 2 && answer != -1) Host.SetApplyEnabled(true);
        }
    }

    /// <summary>@0x141943820.</summary>
    public void SetApplyEnabled(bool enabled) => Host.SetApplyEnabled(enabled);

    /// <summary>vfunc_67 @0x141942a10: every page deactivates, the dialog closes, simple mode and the tab reset.</summary>
    public void Close()
    {
        foreach (var p in pages.Values.ToList()) p.Close(0);
        Host.CloseDialog();
        Simple = false;
        Tab = 1;
    }
}

/// <summary>CGameOptionCamera (gameoption.cpp:397-557).</summary>
public sealed class CameraOptionPage : OptionPage
{
    public GameOption.Camera State = new();
    public CameraOptionPage(int id, int type) : base(id, type) { }

    /// <summary>@0x1412488c0.</summary>
    public override void Open(int n)
    {
        if (n == 0) State = Host.Options.CameraOption.Clone();
        base.Open(n);
    }

    /// <summary>GetCameraFovStep @0x14125b6e0.</summary>
    int FovStep => Host.MouseCameraMode ? State.CameraFovStepMouseMode : State.CameraFovStep;

    /// <summary>@0x141248960.</summary>
    public override void FillControls()
    {
        Tab.SetSelected("m_enableBgTransparency", State.BgTransparency);
        Tab.SetSelected("m_enableVibrateEffect", State.VibrateEffect);
        Tab.SetValue("m_cameraLooseStep", State.CameraLooseStep);
        Tab.SetValue("m_cameraFovStep", FovStep);
    }

    /// <summary>@0x141248c40; the fov step goes back by SetCameraFovStep @0x14125b720.</summary>
    public override void ReadControls()
    {
        if (Tab.IsSelected("m_enableBgTransparency") is bool t) State.BgTransparency = t;
        if (Tab.IsSelected("m_enableVibrateEffect") is bool v) State.VibrateEffect = v;
        if (Tab.GetValue("m_cameraLooseStep") is float l) State.CameraLooseStep = (int)l;
        if (Tab.GetValue("m_cameraFovStep") is float f)
        {
            if (Host.MouseCameraMode) State.CameraFovStepMouseMode = (int)f;
            else State.CameraFovStep = (int)f;
        }
    }

    /// <summary>@0x141248f10: stored, then the camera follows.</summary>
    public override void Save()
    {
        base.Save();
        Host.Options.CameraOption = State.Clone();
        Host.ApplyCamera(State);
    }

    /// <summary>@0x1412491c0: reset, the default document's camera block, refill.</summary>
    public override void ResetToDefaults()
    {
        base.ResetToDefaults();
        State = new GameOption.Camera();
        State.Load(Host.DefaultGameDocument ?? new System.Xml.Linq.XDocument());
        FillControls();
    }

    /// <summary>@0x141249270: memcmp of the 0x10-byte block.</summary>
    public override int HasChanges() => GameOption.SameFields(State, Host.Options.CameraOption) ? 0 : 1;
}

/// <summary>CGameOptionInterface (gameoptionstruct.cpp:189-431): the interface block and the character file's mouse
/// control switch.</summary>
public sealed class InterfaceOptionPage : OptionPage
{
    public GameOption.Interface State = new();
    public bool MouseControl;
    public InterfaceOptionPage(int id, int type) : base(id, type) { }

    /// <summary>@0x1412559a0.</summary>
    public override void Open(int n)
    {
        if (n == 0)
        {
            State = Host.Options.InterfaceOption.Clone();
            MouseControl = Host.MouseControlFlag.Value;
        }
        base.Open(n);
    }

    /// <summary>@0x141255a40: onResetUILayout asks first (0x852 s_gameoption_confirm_reset_layout). The client returns 0
    /// after opening the box.</summary>
    public override int OnMovieCall(string method, object[] args)
    {
        if (string.Equals(method, "onResetUILayout", StringComparison.OrdinalIgnoreCase))
            Host.PageMessageBox(0x852, "resetSwfLayout", this);
        return 0;
    }

    /// <summary>@0x141255b50: yes resets the layout; then the box closes (FUN_14151f200).</summary>
    public override void OnMessage(string tag, int answer)
    {
        if (answer == 0 && tag == "resetSwfLayout") Host.ResetUiLayout();
        Host.NextMessageBox();
    }

    /// <summary>@0x141255c10. The UI scale slider runs 80..120 the other way round (200 - scale * 100).</summary>
    public override void FillControls()
    {
        Tab.SetSelected("m_enableMouseControl", MouseControl);
        Tab.SetSelected("m_closeDialogAll", State.EnableCloseAllDlg);
        Tab.SetSelected("m_autoDesc", State.EnableAutoDesc);
        Tab.SetSelected("m_showTipText", State.EnableShowTipText);
        Tab.SetEnabled("m_showDamageMeter", State.IndividualDamageMeterAllowed);
        Tab.SetSelected("m_showDamageMeter", State.EnableIndividualDamageMeter);
        Tab.SetEnabled("m_showChaosDamageMeter", State.ChaosDamageMeterAllowed);
        Tab.SetSelected("m_showChaosDamageMeter", State.EnableChaosDamageMeter);
        Tab.SetValue("m_sliderUI", 200.0f - State.UIScale * 100.0f);
        Tab.SetValue("m_damageTextScaleStep", State.DamageTextScaleStep);
        Tab.SetValue("m_damageEffectAlphaStep", State.DamageEffectAlpha);
    }

    /// <summary>@0x1412562f0. The chaos meter switch is read only with feature 0xbe (PartyDamageMeter).</summary>
    public override void ReadControls()
    {
        if (Tab.IsSelected("m_enableMouseControl") is bool m) MouseControl = m;
        if (Tab.IsSelected("m_closeDialogAll") is bool c) State.EnableCloseAllDlg = c;
        if (Tab.IsSelected("m_autoDesc") is bool a) State.EnableAutoDesc = a;
        if (Tab.IsSelected("m_showTipText") is bool t) State.EnableShowTipText = t;
        if (Tab.IsSelected("m_showDamageMeter") is bool d) State.EnableIndividualDamageMeter = d;
        if (Tab.IsSelected("m_showChaosDamageMeter") is bool x && Host.IsFeatureEnabled(0xbe)) State.EnableChaosDamageMeter = x;
        if (Tab.GetValue("m_sliderUI") is float s) State.UIScale = (200 - (int)s) / 100.0f;
        if (Tab.GetValue("m_damageTextScaleStep") is float ds) State.DamageTextScaleStep = (int)ds;
        if (Tab.GetValue("m_damageEffectAlphaStep") is float da) State.DamageEffectAlpha = (int)da;
    }

    /// <summary>@0x1412569a0: the meters and the damage effect, the mouse control switch (FUN_14125bd20), the blocks back,
    /// then the character file.</summary>
    public override void Save()
    {
        base.Save();
        Host.ApplyInterface(State);
        Host.MouseControlFlag.Value = MouseControl;
        Host.SetMouseControl(MouseControl);
        Host.Options.InterfaceOption = State.Clone();
        Host.SaveCharacterSettings();
    }

    /// <summary>@0x141256b50.</summary>
    public override void AfterSave() => Host.UiScaleChanged(State.UIScale);

    /// <summary>@0x141256bb0: both blocks from the default game document, refill, then the layout reset.</summary>
    public override void ResetToDefaults()
    {
        base.ResetToDefaults();
        var doc = Host.DefaultGameDocument ?? new System.Xml.Linq.XDocument();
        State = new GameOption.Interface();
        State.Load(doc, Host.IsFeatureEnabled(0xbe));
        var flag = GameOption.CharacterFlag.MouseControl();
        flag.Load(doc);
        MouseControl = flag.Value;
        FillControls();
        Host.ResetUiLayout();
    }

    /// <summary>@0x141256ca0: memcmp of the 0x18-byte interface block only (the mouse switch is not compared).</summary>
    public override int HasChanges() => GameOption.SameFields(State, Host.Options.InterfaceOption) ? 0 : 1;
}

/// <summary>CGameOptionStatus (gameoptionstruct.cpp:704-1067): the status block and the character file's learning quest
/// switch.</summary>
public sealed class StatusOptionPage : OptionPage
{
    public GameOption.Status State = new(true);
    public bool LearningQuest;
    public StatusOptionPage(int id, int type) : base(id, type) { }

    /// <summary>@0x141258a50.</summary>
    public override void Open(int n)
    {
        if (n == 0)
        {
            State = Host.Options.StatusOption.Clone();
            LearningQuest = Host.LearningQuestFlag.Value;
        }
        base.Open(n);
    }

    static readonly (string Name, Func<GameOption.Status, bool> Get, Action<GameOption.Status, bool> Set)[] Checks =
    {
        ("m_displayMyName", s => s.DisplayMyName, (s, v) => s.DisplayMyName = v),
        ("m_displayPlayerSymbol", s => s.DisplayPlayerSymbol, (s, v) => s.DisplayPlayerSymbol = v),
        ("m_displayFriendlyName", s => s.DisplayFriendlyName, (s, v) => s.DisplayFriendlyName = v),
        ("m_displayEnemyName", s => s.DisplayEnemyName, (s, v) => s.DisplayEnemyName = v),
        ("m_displayNpcName", s => s.DisplayNpcName, (s, v) => s.DisplayNpcName = v),
        ("m_displayMonsterName", s => s.DisplayMonsterName, (s, v) => s.DisplayMonsterName = v),
        ("m_displayPcIllust", s => s.DisplayPcIllust, (s, v) => s.DisplayPcIllust = v),
        ("m_displayMonsterGaugeValue", s => s.DisplayMonsterGaugeValue, (s, v) => s.DisplayMonsterGaugeValue = v),
        ("m_displayExpValue", s => s.DisplayExpValue, (s, v) => s.DisplayExpValue = v),
        ("m_displayOnlyMyEffectToMonster", s => s.DisplayOnlyMyEffectToMonster, (s, v) => s.DisplayOnlyMyEffectToMonster = v),
        ("m_displayMyHpGauge", s => s.DisplayMyHpGauge, (s, v) => s.DisplayMyHpGauge = v),
        ("m_displayMyShieldGauge", s => s.DisplayMyShieldGauge, (s, v) => s.DisplayMyShieldGauge = v),
        ("m_displayPartyHpGauge", s => s.DisplayPartyHpGauge, (s, v) => s.DisplayPartyHpGauge = v),
        ("m_displayPartyShieldGauge", s => s.DisplayPartyShieldGauge, (s, v) => s.DisplayPartyShieldGauge = v),
        ("m_displayMonsterHpGauge", s => s.DisplayMonsterHpGauge, (s, v) => s.DisplayMonsterHpGauge = v),
    };

    static readonly (string Name, Func<GameOption.Status, bool> Get, Action<GameOption.Status, bool> Set)[] Balloons =
    {
        ("m_displayNormalChatBalloon", s => s.DisplayNormalChatBalloon, (s, v) => s.DisplayNormalChatBalloon = v),
        ("m_displayPartyChatBalloon", s => s.DisplayPartyChatBalloon, (s, v) => s.DisplayPartyChatBalloon = v),
        ("m_displayGuildChatBalloon", s => s.DisplayGuildChatBalloon, (s, v) => s.DisplayGuildChatBalloon = v),
    };

    /// <summary>@0x141258b10. The pet switches only with feature 0x8c. The GMS2 m_displayHideOtherPet box sits with them
    /// (INFERRED: the GMS2 exe is packed); a KMS movie has no such box, so it does nothing there.</summary>
    public override void FillControls()
    {
        foreach (var c in Checks) Tab.SetSelected(c.Name, c.Get(State));
        Tab.SetSelectedIndex("m_bossMonsterNotify", State.DisplayBossMonsterNotify);
        foreach (var c in Balloons) Tab.SetSelected(c.Name, c.Get(State));
        if (Host.IsFeatureEnabled(0x8c))
        {
            Tab.SetSelected("m_displayShowOnlyMyPet", State.DisplayShowOnlyMyPet);
            Tab.SetSelected("m_displayHideMyPet", State.DisplayHideMyPet);
            Tab.SetSelected("m_displayHideOtherPet", State.DisplayHideOtherPet);   // GMS2 movie only, see Status.DisplayHideOtherPet
        }
        Tab.SetSelected("m_displayHideSmartPush", State.DisplayHideSmartPush);
        Tab.SetSelected("m_displayLearningQuest", LearningQuest);
        Tab.SetSelected("m_displayHideHighlightMenu", State.DisplayHideHighlightMenu);
    }

    /// <summary>@0x141259c40.</summary>
    public override void ReadControls()
    {
        foreach (var c in Checks) if (Tab.IsSelected(c.Name) is bool v) c.Set(State, v);
        if (Tab.GetSelectedIndex("m_bossMonsterNotify") is int b) State.DisplayBossMonsterNotify = b;
        foreach (var c in Balloons) if (Tab.IsSelected(c.Name) is bool v) c.Set(State, v);
        if (Host.IsFeatureEnabled(0x8c))
        {
            if (Tab.IsSelected("m_displayShowOnlyMyPet") is bool p) State.DisplayShowOnlyMyPet = p;
            if (Tab.IsSelected("m_displayHideMyPet") is bool h) State.DisplayHideMyPet = h;
            if (Tab.IsSelected("m_displayHideOtherPet") is bool o) State.DisplayHideOtherPet = o;
        }
        if (Tab.IsSelected("m_displayHideSmartPush") is bool sp) State.DisplayHideSmartPush = sp;
        if (Tab.IsSelected("m_displayLearningQuest") is bool lq) LearningQuest = lq;
        if (Tab.IsSelected("m_displayHideHighlightMenu") is bool hm) State.DisplayHideHighlightMenu = hm;
    }

    /// <summary>@0x14125ae50: the hud bars, the blocks back (the pet change hooks FUN_14125b130 / FUN_14125b140 are empty),
    /// then the character file.</summary>
    public override void Save()
    {
        base.Save();
        Host.ApplyStatus(State);
        Host.Options.StatusOption = State.Clone();
        Host.LearningQuestFlag.Value = LearningQuest;
        Host.SaveCharacterSettings();
    }

    /// <summary>@0x14125afe0 (no CBaseOption::vfunc_12 call): both from the default game document, refill.</summary>
    public override void ResetToDefaults()
    {
        var doc = Host.DefaultGameDocument ?? new System.Xml.Linq.XDocument();
        State = new GameOption.Status(Host.Options.StatusOption.RegionKr);
        State.Load(doc);
        var flag = GameOption.CharacterFlag.LearningQuest();
        flag.Load(doc);
        LearningQuest = flag.Value;
        FillControls();
    }

    /// <summary>@0x14125b0b0: the 0x1c-byte block, then the 1-byte flag.</summary>
    public override int HasChanges() =>
        !GameOption.SameFields(State, Host.Options.StatusOption) || LearningQuest != Host.LearningQuestFlag.Value ? 1 : 0;
}

/// <summary>CGameOptionBattle (gameoption.cpp:180-395). The damage forms are 1 mine, 3 party, 7 all; the other damage
/// form is a bit set (1 size, 2 alpha).</summary>
public sealed class BattleOptionPage : OptionPage
{
    public GameOption.Battle State = new();
    public BattleOptionPage(int id, int type) : base(id, type) { }

    /// <summary>@0x141246dd0.</summary>
    public override void Open(int n)
    {
        if (n == 0) State = Host.Options.BattleOption.Clone();
        base.Open(n);
    }

    static readonly (string Name, Func<GameOption.Battle, bool> Get, Action<GameOption.Battle, bool> Set)[] Checks =
    {
        ("m_displayMyGiveRecovery", s => s.DisplayMyGiveRecovery, (s, v) => s.DisplayMyGiveRecovery = v),
        ("m_displayMyTakeRecovery", s => s.DisplayMyTakeRecovery, (s, v) => s.DisplayMyTakeRecovery = v),
        ("m_displayPartyRecovery", s => s.DisplayPartyRecovery, (s, v) => s.DisplayPartyRecovery = v),
        ("m_displayGetMeso", s => s.DisplayGetMeso, (s, v) => s.DisplayGetMeso = v),
        ("m_displayGetExp", s => s.DisplayGetExp, (s, v) => s.DisplayGetExp = v),
        ("m_displayEpGauge", s => s.DisplayEpGauge, (s, v) => s.DisplayEpGauge = v),
    };

    static readonly (string Suffix, int Value)[] Forms = { ("Mine", 1), ("Party", 3), ("All", 7) };

    /// <summary>@0x141246e70: one radio per form is selected (a form outside 1/3/7 selects none).</summary>
    public override void FillControls()
    {
        foreach (var c in Checks) Tab.SetSelected(c.Name, c.Get(State));
        foreach (var (suffix, value) in Forms) if (State.GiveDamage == value) Tab.SetSelected("m_giveDamage" + suffix, true);
        foreach (var (suffix, value) in Forms) if (State.TakeDamage == value) Tab.SetSelected("m_takeDamage" + suffix, true);
        Tab.SetSelected("m_diffSize", (State.OtherDamageForm & 1) != 0);
        Tab.SetSelected("m_diffAlpha", (State.OtherDamageForm & 2) != 0);
    }

    /// <summary>@0x141247770: the first selected radio of each group; the other form is rebuilt from the boxes there are.</summary>
    public override void ReadControls()
    {
        foreach (var c in Checks) if (Tab.IsSelected(c.Name) is bool v) c.Set(State, v);
        foreach (var (suffix, value) in Forms)
            if (Tab.IsSelected("m_giveDamage" + suffix) == true) { State.GiveDamage = value; break; }
        foreach (var (suffix, value) in Forms)
            if (Tab.IsSelected("m_takeDamage" + suffix) == true) { State.TakeDamage = value; break; }
        int diff = 0;
        if (Tab.IsSelected("m_diffSize") is bool size)
        {
            if (size) diff |= 1;
            State.OtherDamageForm = diff;
        }
        if (Tab.IsSelected("m_diffAlpha") is bool alpha)
        {
            if (alpha) diff |= 2;
            State.OtherDamageForm = diff;
        }
    }

    /// <summary>@0x141248140.</summary>
    public override void Save()
    {
        base.Save();
        Host.Options.BattleOption = State.Clone();
    }

    /// <summary>@0x141248190.</summary>
    public override void ResetToDefaults()
    {
        base.ResetToDefaults();
        State = new GameOption.Battle();
        State.Load(Host.DefaultGameDocument ?? new System.Xml.Linq.XDocument(), Host.IsFeatureEnabled(0x66));
        FillControls();
    }

    /// <summary>@0x141248240: memcmp of the 0x14-byte block.</summary>
    public override int HasChanges() => GameOption.SameFields(State, Host.Options.BattleOption) ? 0 : 1;
}

/// <summary>CGameOptionCommunity (gameoption.cpp:560-783): the game file's balloons and riding switch, and the account
/// file's request switches.</summary>
public sealed class CommunityOptionPage : OptionPage
{
    public GameOption.Community State = new();
    public GameOption.AccountCommunity Account = new();
    public CommunityOptionPage(int id, int type) : base(id, type) { }

    /// <summary>@0x1412493a0.</summary>
    public override void Open(int n)
    {
        if (n == 0)
        {
            State = Host.Options.CommunityOption.Clone();
            Account = Host.AccountCommunity.Clone();
        }
        base.Open(n);
    }

    static readonly (string Name, Func<GameOption.AccountCommunity, bool> Get, Action<GameOption.AccountCommunity, bool> Set)[] Requests =
    {
        ("m_displayChatComfirmSuper", s => s.DisplayChatComfirmSuper, (s, v) => s.DisplayChatComfirmSuper = v),
        ("m_displayChatComfirmWorld", s => s.DisplayChatComfirmWorld, (s, v) => s.DisplayChatComfirmWorld = v),
        ("m_displayChatComfirmChannel", s => s.DisplayChatComfirmChannel, (s, v) => s.DisplayChatComfirmChannel = v),
        ("m_disablePartyRequest", s => s.DisablePartyRequest, (s, v) => s.DisablePartyRequest = v),
        ("m_disableBuddyRequest", s => s.DisableBuddyRequest, (s, v) => s.DisableBuddyRequest = v),
        ("m_disableGuildRequest", s => s.DisableGuildRequest, (s, v) => s.DisableGuildRequest = v),
        ("m_disableGroupRequest", s => s.DisableGroupRequest, (s, v) => s.DisableGroupRequest = v),
        ("m_disableWhisperNotify", s => s.DisableWhisperNotify, (s, v) => s.DisableWhisperNotify = v),
        ("m_disableTradeRequest", s => s.DisableTradeRequest, (s, v) => s.DisableTradeRequest = v),
        ("m_disableHomeRequest", s => s.DisableHomeRequest, (s, v) => s.DisableHomeRequest = v),
        ("m_disableCoupleEmotionRequest", s => s.DisableCouplemotionRequest, (s, v) => s.DisableCouplemotionRequest = v),
    };

    /// <summary>@0x141249460.</summary>
    public override void FillControls()
    {
        Tab.SetSelected("m_displayMyChatBalloon", State.DisplayMyChatBalloon);
        Tab.SetSelected("m_displayOtherChatBalloon", State.DisplayOtherChatBalloon);
        Tab.SetSelected("m_disableSingleRiding", State.DisableSingleRiding);
        foreach (var r in Requests) Tab.SetSelected(r.Name, r.Get(Account));
    }

    /// <summary>@0x141249e50.</summary>
    public override void ReadControls()
    {
        if (Tab.IsSelected("m_displayMyChatBalloon") is bool m) State.DisplayMyChatBalloon = m;
        if (Tab.IsSelected("m_displayOtherChatBalloon") is bool o) State.DisplayOtherChatBalloon = o;
        if (Tab.IsSelected("m_disableSingleRiding") is bool r) State.DisableSingleRiding = r;
        foreach (var q in Requests) if (Tab.IsSelected(q.Name) is bool v) q.Set(Account, v);
    }

    /// <summary>@0x14124a8d0: a changed riding switch goes to the server, then both blocks back.</summary>
    public override void Save()
    {
        base.Save();
        if (Host.Options.CommunityOption.DisableSingleRiding != State.DisableSingleRiding) Host.SendRideMode(State.DisableSingleRiding);
        Host.Options.CommunityOption = State.Clone();
        Host.AccountCommunity = Account.Clone();
    }

    /// <summary>@0x14124a990: the account block from DefaultAccountOption.xml, the game block from the default game
    /// document, refill.</summary>
    public override void ResetToDefaults()
    {
        base.ResetToDefaults();
        Account = new GameOption.AccountCommunity();
        State = new GameOption.Community();
        Account.Load(Host.DefaultAccountDocument ?? new System.Xml.Linq.XDocument());
        State.Load(Host.DefaultGameDocument ?? new System.Xml.Linq.XDocument());
        FillControls();
    }

    /// <summary>@0x14124aa70: FUN_14125d2d0 / FUN_14125d740 compare every switch.</summary>
    public override int HasChanges() =>
        !GameOption.SameFields(State, Host.Options.CommunityOption) || !GameOption.SameFields(Account, Host.AccountCommunity) ? 1 : 0;
}

/// <summary>CGameOptionInputDefault (gameoptioninputdefault.cpp), pages 9..13: the key list of keySetting.xml's category
/// (the page id) with the bound keys. Key editing (onFocusIn*, the key capture in vfunc_8, the save to the server) is not
/// ported: it changes the live key bindings.</summary>
public sealed class InputOptionPage : OptionPage
{
    public InputOptionPage(int id, int type) : base(id, type) { }

    /// <summary>@0x141250c10: n 0 runs the base (fill or pend) and ends any edit; otherwise the list is refreshed.</summary>
    public override void Open(int n)
    {
        if (n == 0) base.Open(n);
        else RefreshKeyList(Type);
    }

    /// <summary>@0x1412514e0.</summary>
    public override void FillControls() => RefreshKeyList(Type);
    /// <summary>@0x141251510.</summary>
    public override void ReadControls() => RefreshKeyList(Type);

    /// <summary>m_mapQuickSlots (+0x98): the quick slots as they were before a key reset, slot to (skill, item uid); the entry's
    /// item id stays 0 (the client's Unk_SsQuickEntry sets the skill and the uid only, :355-357).</summary>
    readonly Dictionary<int, (int SkillId, long ItemUid)> quickSlots = new();
    readonly List<int> boundKeys = new();

    /// <summary>FUN_141252c10 (@0x141252c10, gameoptioninputdefault.cpp:642-650): pages 9..12 are in their tab when it is the
    /// dialog's; the rest compare with tab 9.</summary>
    bool InTab() => Id >= 9 && Id <= 12 ? Type == Dialog.Tab : Type == 9;

    /// <summary>FUN_1412532f0 (@0x1412532f0, :843-848): posts NotifyChangeGameOptionInput(id) while the page is active.</summary>
    void NotifyChanged()
    {
        if (Active) Host.NotifyChangeGameOptionInput(Id);
    }

    /// <summary>FUN_141252970 (@0x141252970, :602-622): in the tab, the snapshot is rebuilt from every bound key: its skill and item
    /// uid, and the slot of the current page holding both (-1 is left out).</summary>
    void SnapshotQuickSlots()
    {
        if (!InTab()) return;
        quickSlots.Clear();
        var q = Host.QuickSlots;
        if (q == null) return;
        q.GetBoundKeys(boundKeys);
        foreach (int key in boundKeys)
        {
            int skill = q.SkillIdByKey(key);
            long uid = q.ItemUidByKey(key);
            int slot = q.FindSlot(skill, uid);
            if (slot != -1) quickSlots[slot] = (skill, uid);
        }
    }

    /// <summary>FUN_141252ae0 (@0x141252ae0, :625-638): each snapshot entry goes back on its slot of the current page
    /// (FUN_14092f690), then RefreshQuickSlots and the notify.</summary>
    void PutBackQuickSlots()
    {
        var q = Host.QuickSlots;
        if (q != null)
        {
            int page = q.Page;
            foreach (var (slot, e) in quickSlots) q.Put(e.SkillId, 0, e.ItemUid, page, slot);
            q.Refresh();
        }
        NotifyChanged();
    }

    /// <summary>
    /// ResetKeysToDefault(nPadKind) (@0x141252200, gameoptioninputdefault.cpp:444-485): in the tab, the quick slots are snapshotted;
    /// with the game pad feature (0xb5) on, pad kind 2 loads DefaultPSKey.xml and kinds 1 and 3 DefaultXboxKey.xml through
    /// FUN_141429e60 (sent to the server); any other kind, or the feature off, loads DefaultKey.xml with nothing sent. Then the slots
    /// refresh and the notify posts; the list refreshes when the tab is current. STILL-OPEN: the edit reset (m_spEdit = 0, the key
    /// capture is not ported).
    /// </summary>
    void ResetKeysToDefault(int padKind)
    {
        if (InTab())
        {
            SnapshotQuickSlots();
            if (Host.IsFeatureEnabled(0xb5) && padKind is 1 or 2 or 3) Host.Keys?.LoadDefaultPadKeys(padKind);
            else Host.Keys?.LoadDefaultKeyboardKeys();
            Host.QuickSlots?.Refresh();
            NotifyChanged();
        }
        if (Dialog.Tab == Type) RefreshKeyList(Type);
    }

    /// <summary>vfunc_12 (@0x141251e90, :402-432): a page reset (FUN_141254a00 != 0) with the game pad feature (0xb5) on, in its tab,
    /// asks s_gameoption_input_reset_message first (the pad arg is whether a pad is connected) and resets from the answer
    /// (<see cref="OnResetMessage"/>); with the feature off the keyboard file loads (pad kind 0); a reset of all pages resets for
    /// the connected pad's kind.</summary>
    public override void ResetToDefaults()
    {
        if (Dialog.ResetScope == 0) { ResetKeysToDefault(Host.GetPadKind()); return; }
        if (!Host.IsFeatureEnabled(0xb5)) { ResetKeysToDefault(0); return; }
        if (InTab()) Host.ResetMessageBox(Host.GetPadKind() != 0 ? "1" : "0", OnResetMessage);
    }

    /// <summary>FUN_1412523d0 (@0x1412523d0, :623-635): the first button resets: a pad arg of 0 for the keyboard file, otherwise for
    /// the connected pad's kind. Handled either way.</summary>
    bool OnResetMessage(int button, string padArg)
    {
        if (button == 0) ResetKeysToDefault((int.TryParse(padArg, out var pad) ? pad : 0) == 0 ? 0 : Host.GetPadKind());
        return true;
    }

    /// <summary>vfunc_9 (@0x141251db0, gameoptioninputdefault.cpp:378-385, 592-638): page 9 restores keys from backup (FUN_141252940),
    /// then puts the snapshot's quick slots back (FUN_141252ae0).</summary>
    public override void DropChanges()
    {
        if (Id != 9) return;
        Host.Keys?.RestoreKeysFromBackup();
        PutBackQuickSlots();
    }

    /// <summary>vfunc_10 (@0x141251df0, gameoptioninputdefault.cpp:389-400): sends dirty keys to server, backs up on page 9, drops the
    /// snapshot, UpdateModifiedKeys on the main player info and posts the notify.</summary>
    public override void Save()
    {
        base.Save();
        Host.Keys?.SendKeysToServer();
        if (Id == 9) Host.Keys?.BackupKeys();
        quickSlots.Clear();
        Host.QuickSlots?.UpdateModifiedKeys();
        NotifyChanged();
    }

    /// <summary>RefreshKeyList @0x141250cc0: a row per setting of the page id's category. Action 0x49409 shows key 1 as
    /// its second key and keeps an int keyGUID; the third key shows only with the game pad feature (0xb5).</summary>
    public void RefreshKeyList(int category)
    {
        var keys = Host.Keys;
        bool pad = Host.IsFeatureEnabled(0xb5);
        var rows = new List<KeyRow>();
        if (keys?.GetSettingList(Id) is { } list)
            foreach (var k in list)
            {
                int primary = keys.FindKeyByActionAndPriority(k.Action, 0);
                int secondary = keys.FindKeyByActionAndPriority(k.Action, 1);
                int thirdly = keys.FindKeyByActionAndPriority(k.Action, 2);
                bool special = k.Action == 0x49409;
                rows.Add(new KeyRow(keys.ActionName(k.Action),
                    special ? 0x49409 : k.Action.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    keys.GetKeyName(primary), special ? keys.GetKeyName(1) : keys.GetKeyName(secondary),
                    pad ? keys.GetKeyName(thirdly) : "",
                    k.PrimaryLocked == 0, k.SecondaryLocked == 0, k.ThirdlyLocked == 0, pad ? 1 : 0));
            }
        Tab.SetKeyRows(category, Type, rows);
    }

    /// <summary>@0x141251a30: OnSelectCategory refreshes the list for the category given; the focus callbacks are taken
    /// (their key editing is not ported).</summary>
    public override int OnMovieCall(string name, object[] args)
    {
        if (string.Equals(name, "OnSelectCategory", StringComparison.OrdinalIgnoreCase))
        {
            if (args is { Length: > 0 }) RefreshKeyList(Convert.ToInt32(args[0], System.Globalization.CultureInfo.InvariantCulture));
            return 1;
        }
        foreach (var f in FocusCallbacks)
            if (string.Equals(name, f, StringComparison.OrdinalIgnoreCase)) return 1;
        return 0;
    }

    static readonly string[] FocusCallbacks =
    {
        "onFocusInPrimary", "onFocusInSecondary", "onFocusInThirdly", "onFocusOutPrimary", "onFocusOutSecondary", "onFocusOutThirdly",
    };
}

/// <summary>CGameOptionGamePad (gameoptiongamepad.cpp): two check boxes and the vibration box, three sliders; the state is a
/// copy of CGameOption's gamepad block.</summary>
public sealed class GamePadOptionPage : OptionPage
{
    public readonly GameOption.GamePad State = new();
    public GamePadOptionPage(int id, int type) : base(id, type) { }

    /// <summary>@0x140a57240: n 0 copies the stored block.</summary>
    public override void Open(int n)
    {
        if (n == 0) State.CopyFrom(Host.GamePad);
        base.Open(n);
    }

    /// <summary>@0x140a572e0; the sensitivities show x100.</summary>
    public override void FillControls()
    {
        Tab.SetSelected("m_bGamePadUse", State.Use);
        Tab.SetSelected("m_bShowSkillName", State.ShowSkillName);
        Tab.SetValue("m_gamePadLStickMoveSensitivity", State.LStick);
        Tab.SetSelected("m_bVibration", State.Vibration);
        Tab.SetValue("m_sensitivityH", State.SensitivityH * 100f);
        Tab.SetValue("m_sensitivityV", State.SensitivityV * 100f);
    }

    /// <summary>@0x140a57730: the left stick (feature 0xd6) and vibration (0xc3) read as 0 / false without their feature; the
    /// sensitivities have a floor of 0.01.</summary>
    public override void ReadControls()
    {
        if (Tab.IsSelected("m_bGamePadUse") is bool use) State.Use = use;
        if (Tab.IsSelected("m_bShowSkillName") is bool skill) State.ShowSkillName = skill;
        if (Tab.GetValue("m_gamePadLStickMoveSensitivity") is float stick) State.LStick = Host.IsFeatureEnabled(0xd6) ? (int)stick : 0;
        if (Tab.IsSelected("m_bVibration") is bool vibration) State.Vibration = Host.IsFeatureEnabled(0xc3) && vibration;
        if (Tab.GetValue("m_sensitivityH") is float h) State.SensitivityH = Math.Max(h / 100f, 0.01f);
        if (Tab.GetValue("m_sensitivityV") is float v) State.SensitivityV = Math.Max(v / 100f, 0.01f);
    }

    /// <summary>@0x140a57c20: the stored block takes the state, then SetGamepadEnabled(state.Use, notify). The client also
    /// unlocks the mouse cursor and resets its type when the input service is in cursor mode; not ported.</summary>
    public override void Save()
    {
        base.Save();
        Host.GamePad.CopyFrom(State);
        Host.SetGamepadEnabled(Host.GamePad, State.Use, true);
    }

    /// <summary>@0x140a57d00: the defaults, then the default game document's gamepad section (FUN_1412442a0).</summary>
    public override void ResetToDefaults()
    {
        State.Reset();
        var d = Host.DefaultGameDocument;
        if (d != null) State.Load(d, Host.IsFeatureEnabled(0xc3), Host.IsFeatureEnabled(0xd6));
        FillControls();
    }

    /// <summary>@0x140a57db0 (a memcmp).</summary>
    public override int HasChanges() => State.SameAs(Host.GamePad) ? 0 : 1;
}

/// <summary>CGameOptionPersonalInfoAgreement (gameoptionpersonalinfoagreement.cpp): the agree / deny radio buttons of the log
/// sending question.</summary>
public sealed class AgreementOptionPage : OptionPage
{
    public readonly PersonalInfoAgreement State = new();
    public AgreementOptionPage(int id, int type) : base(id, type) { }

    /// <summary>@0x140a45dd0: n 0 copies the stored agreement.</summary>
    public override void Open(int n)
    {
        if (n == 0) State.CopyFrom(Host.Agreement);
        base.Open(n);
    }

    /// <summary>@0x140a45e50: the detail button.</summary>
    public override int OnMovieCall(string method, object[] args)
    {
        if (string.Equals(method, "OnDetail", StringComparison.OrdinalIgnoreCase)) Host.OpenAgreementDetail();
        return 0;
    }

    /// <summary>@0x140a45ed0.</summary>
    public override void FillControls()
    {
        Tab.SetSelected("m_sendLogAgree", State.Agree);
        Tab.SetSelected("m_sendLogDeny", !State.Agree);
    }

    /// <summary>@0x140a46040: agree and deny each only set their answer when selected, then group 0 takes mode 2 (agree) or 4 on
    /// the page copy, and the reminders refresh.</summary>
    public override void ReadControls()
    {
        if (Tab.IsSelected("m_sendLogAgree") == true) State.Agree = true;
        if (Tab.IsSelected("m_sendLogDeny") == true) State.Agree = false;
        State.SetGroup(0, State.Agree ? 2u : 4u);
        Host.RefreshAgreementReminders();
    }

    /// <summary>@0x140a461f0 (no region gate): when the answer or group 0's agreement differs from the stored one, the stored
    /// agreement takes the page copy and the answer goes to the server. The stored word is filled from the server only for
    /// region 3 (scenestategame.cpp:11161-11168, SetFlagsAndRefresh); on the Korean service nothing fills it, so it stays 0,
    /// group 0 is never agreed and every Save sends mode 2 (agree) or 4 (deny), as the client does.
    /// FUN_141262090 (@0x141262090, gameoptionstruct.cpp:3503-3518) is the host's RefreshAgreementReminders (the menu icons'
    /// red circles included). The host's HasTutorial hook is its first check (WorldService::HasTutorial, the field's tutorialType).
    /// The call at scene entry (SetFlagsAndRefresh, scenestategame.cpp:11161-11168) needs DAT_143ab0248 == 3 (China) and is not made
    /// on the Korean service. OpenLink (:3597) navigates UIWebBrowserBase 0x4e6a; the host opens
    /// the PersonalInfoAgreementURL constant in the system browser (WebPage). MyPC::SendHouseSetting (mypc.cpp:4385-4396) sends the same 0xC0 after
    /// the RequestCube 0x18 packet (HouseSettingRequest; the host's SendHouseSetting). The house visit question is wired; STILL-OPEN: the home option dialog's movie (its other caller) is not ported.</summary>
    public override void Save()
    {
        base.Save();
        var stored = Host.Agreement;
        if (State.Agree != stored.Agree || !stored.HasGroupAgreement(0))
        {
            int mode = State.Agree ? 2 : 4;
            stored.CopyFrom(State);
            Host.SendAgreement(mode);
            Host.RefreshAgreementReminders();
        }
    }

    /// <summary>@0x140a462f0: the page copy's answer is cleared and re-derived from its flags.</summary>
    public override void ResetToDefaults()
    {
        State.Agree = false;
        State.RefreshAgree();
        FillControls();
    }

    /// <summary>@0x140a46390.</summary>
    public override int HasChanges() => State.Agree != Host.Agreement.Agree ? 1 : 0;
}

/// <summary>CGameOptionLanguage (gameoptionlanguage.cpp): the language, region and voice dropdowns, filled from
/// gameoptionlocale.xml's row; the state is CGameOption's language block (languageIndex, voiceIndex).</summary>
public sealed class LanguageOptionPage : OptionPage
{
    public GameOption.Language State = new(0);
    public LanguageOptionPage(int id, int type) : base(id, type) { }

    static GameOption.Language Copy(GameOption.Language l) => new(0) { LanguageIndex = l.LanguageIndex, VoiceIndex = l.VoiceIndex };

    /// <summary>@0x140cfb4e0: n 0 copies the block, otherwise the dropdowns are filled (FUN_140cfbf50).</summary>
    public override void Open(int n)
    {
        if (n == 0) State = Copy(Host.Options.LanguageOption);
        else Fill();
        base.Open(n);
    }

    /// <summary>FUN_140cfbf50 (@0x140cfbf50): each dropdown gets ClearItems and a {label, index} row per code; three rows or
    /// fewer set menuRowCount, one row disables it. NA's region dropdown holds the connect block's region name
    /// ("stage" when empty), selected and disabled.</summary>
    void Fill()
    {
        var row = Host.LocaleData;
        if (row == null) return;
        FillList("m_languageSelect", row.Languages, c => "s_gameoption_language_" + c, GameOptionLocale.GetLanguageId);
        if (Host.Region == 2)
        {
            string region = Host.Options.ConnectOption.Region;
            if (string.IsNullOrEmpty(region)) region = "stage";
            if (Tab.SetItemRows("m_regionSelect", new[] { (Host.CommonByKey("s_gameoption_region_" + region), 0) }))
            {
                Tab.SetMenuRowCount("m_regionSelect", 1);
                Tab.SetSelectedIndex("m_regionSelect", 0);
                Tab.SetEnabled("m_regionSelect", false);
            }
        }
        else FillList("m_regionSelect", row.Regions, c => "s_gameoption_region_" + c, GameOptionLocale.RegionId);
        FillList("m_voiceSelect", row.Voices, c => "s_gameoption_voice_" + c, GameOptionLocale.GetLanguageId);
    }

    void FillList(string name, string[] codes, Func<string, string> key, Func<string, int> index)
    {
        var rows = codes.Select(c => (Host.CommonByKey(key(c)), index(c))).ToList();
        if (!Tab.SetItemRows(name, rows)) return;
        if (codes.Length <= 3) Tab.SetMenuRowCount(name, codes.Length);
        if (codes.Length == 1) Tab.SetEnabled(name, false);
    }

    /// <summary>@0x140cfb590: each dropdown selects the row whose code matches. The client's region loop counts the region
    /// codes but compares the voice codes to the region's code; kept.</summary>
    public override void FillControls()
    {
        var row = Host.LocaleData;
        if (row == null) return;
        Select("m_languageSelect", row.Languages.Length, i => row.Languages[i], GameOptionLocale.LanguageCode(State.LanguageIndex));
        Select("m_regionSelect", row.Regions.Length, i => i < row.Voices.Length ? row.Voices[i] : null, GameOptionLocale.RegionCode(Host.Region));
        Select("m_voiceSelect", row.Voices.Length, i => row.Voices[i], GameOptionLocale.LanguageCode(State.VoiceIndex));
    }

    void Select(string name, int count, Func<int, string> code, string want)
    {
        for (int i = 0; i < count; i++)
            if (string.Equals(code(i), want, StringComparison.OrdinalIgnoreCase)) { Tab.SetSelectedIndex(name, i); break; }
    }

    /// <summary>@0x140cfb9e0: the selected rows' "index".</summary>
    public override void ReadControls()
    {
        if (Tab.GetSelectedItemIndex("m_languageSelect") is int l) State.LanguageIndex = l;
        if (Tab.GetSelectedItemIndex("m_voiceSelect") is int v) State.VoiceIndex = v;
    }

    static readonly string[] KorVoiceFiles =
    {
        "./Data/Sound/MS2Sound_AttackVoice_Kor_def.fsb", "./Data/Sound/MS2Sound_Emotion_Kor_def.fsb",
        "./Data/Sound/MS2Sound_Voice_Kor_def.fsb", "./Data/Sound/MS2Sound_Voice2_Kor_def.fsb",
        "./Data/Sound/MS2Sound_Voice3_Kor_def.fsb", "./Data/Sound/MS2Sound_Voice4_Kor_def.fsb",
        "./Data/Sound/MS2Sound_Voice5_Kor_def.fsb", "./Data/Sound/MS2Sound_Voice6_Kor_def.fsb",
    };

    /// <summary>FUN_140cfbcd0 (@0x140cfbcd0): only CN checks; its Chinese voice is always there, the Korean one needs its
    /// sound files.</summary>
    bool VoiceAvailable(int voice)
    {
        if (Host.Region != 3) return true;
        if (voice == 3) return true;
        if (voice == 0) return KorVoiceFiles.All(Host.FileExists);
        return false;
    }

    /// <summary>@0x140cfcb20: a changed language asks for a restart; a changed voice is checked (a missing one goes back with
    /// s_gameoption_voice_select_warning) and asks for a restart when it differs from the running voice; then stored.</summary>
    public override void Save()
    {
        base.Save();
        var stored = Host.Options.LanguageOption;
        bool restart = State.LanguageIndex != stored.LanguageIndex;
        if (State.VoiceIndex != stored.VoiceIndex)
        {
            bool ok = VoiceAvailable(State.VoiceIndex);
            if (!ok)
            {
                State.VoiceIndex = stored.VoiceIndex;
                Tab.SetSelectedIndex("m_voiceSelect", State.VoiceIndex);
                Host.TextNotice(Host.CommonByKey("s_gameoption_voice_select_warning"));
            }
            else if (Host.RunningVoice != State.VoiceIndex) restart = true;
        }
        if (restart) Host.TextNotice(Host.CommonByKey("s_gameoption_voice_select_restart"));
        Host.Options.LanguageOption = Copy(State);
    }

    /// <summary>@0x140cfce00: reset to the region's values, read the default game document, refill.</summary>
    public override void ResetToDefaults()
    {
        base.ResetToDefaults();
        State = new GameOption.Language(Host.Region);
        State.Load(Host.DefaultGameDocument ?? new System.Xml.Linq.XDocument());
        FillControls();
    }

    /// <summary>@0x140cfceb0.</summary>
    public override int HasChanges() =>
        State.LanguageIndex != Host.Options.LanguageOption.LanguageIndex || State.VoiceIndex != Host.Options.LanguageOption.VoiceIndex ? 1 : 0;
}

/// <summary>CGameOptionGraphicDefault (gameoption.cpp). The page state is a copy of the graphic and optimizer options
/// (m_state, m_state2), stored back on save.</summary>
public sealed class GraphicOptionPage : OptionPage
{
    public GameOption.Graphic State = new();
    public GameOption.Optimizer State2 = new();
    bool filled;   // m_n74: onDisplayQuality is ignored until the controls are filled

    public GraphicOptionPage(int id, int type) : base(id, type) { }

    GameOption.Graphic Global => Host.Options.GraphicOption;
    GameOption.Optimizer Global2 => Host.Options.OptimizerOption;

    /// <summary>@0x14124abe0: n 0 copies the stored options into the page state.</summary>
    public override void Open(int n)
    {
        if (n == 0)
        {
            State = Global.Clone();
            State2 = Global2.Clone();
            filled = false;
        }
        base.Open(n);
    }

    /// <summary>@0x14124ac90: onDisplayQuality(level) from the quality slider. The hdr box, the shadow index and the
    /// effect step follow the level; a level above the benchmark warns once (0x84d s_gameoption_quality_warning).</summary>
    public override int OnMovieCall(string method, object[] args)
    {
        if (!string.Equals(method, "onDisplayQuality", StringComparison.OrdinalIgnoreCase) || !filled || args.Length == 0)
            return 0;
        int level = Convert.ToInt32(args[0]);
        Tab.SetSelected("m_hdr", Host.IsHdrSupported(level));
        if (level >= 3) Tab.SetSelectedIndex("m_shadow", 2);
        else if (level >= 2) Tab.SetSelectedIndex("m_shadow", 1);
        else if (level >= 0) Tab.SetSelectedIndex("m_shadow", 0);
        Tab.SetValue("m_effectStep", Math.Max(level, 1));
        if (level > Host.BenchmarkLevel && level != State.Quality && Host.MessageBoxFree)
            Host.Notice(0x84d);
        return 0;
    }

    /// <summary>@0x14124aff0.</summary>
    public override void FillControls()
    {
        FillResolutionCombo();
        Tab.SetSelected("m_screenModeWindow", State.ScreenMode == 1);
        Tab.SetSelected("m_screenModeFullWindow", State.ScreenMode == 2);
        Tab.SetSelected("m_showFrameRate", State.ShowFrameRate);
        Tab.SetValue("m_effectStep", State.EffectStep);
        Tab.SetValue("m_quality", State.Quality);
        Tab.SetSelected("m_hdr", State.DisplayHdr);
        Tab.SetSelected("m_shimmer", State.DisplayShimmer);
        Tab.SetSelectedIndex("m_multisampling", State.MultisamplingIndex);
        Tab.SetSelected("m_vsync", State.Vsync);
        Tab.SetSelected("m_enchantEffect", State.ShowEnchantEffect);
        int shadow = State.ShadowLevel;
        if (shadow >= 3) Tab.SetSelectedIndex("m_shadow", 2);
        else if (shadow >= 2) Tab.SetSelectedIndex("m_shadow", 1);
        else if (shadow >= 0) Tab.SetSelectedIndex("m_shadow", 0);
        Tab.SetText("m_devicename", Host.AdapterName);
        Tab.SetSelectedIndex("m_optimalHide", State2.OptimalHide);
        Tab.SetSelected("m_hideAlways", State2.HideAlways);
        Tab.SetSelected("m_hideBossBattle", State2.HideBossBattle);
        Tab.SetSelected("m_showParty", State2.ShowParty);
        Tab.SetSelected("m_showGuild", State2.ShowGuild);
        Tab.SetSelected("m_showCustom", State2.ShowCustom);
        Tab.SetText("m_customInput", State2.CustomCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Tab.SetSelected("m_showPhysx", State2.ShowPhysx);
        filled = true;
    }

    /// <summary>@0x14124bf20.</summary>
    public override void ReadControls()
    {
        int res = Tab.GetResolution(Type);
        if (res != -1)
        {
            State.ResolutionIndex = res;
            GetModeSize(res, ref State.Width, ref State.Height);
        }
        if (Tab.IsSelected("m_screenModeWindow") == true) State.ScreenMode = 1;
        if (Tab.IsSelected("m_screenModeFullWindow") == true) State.ScreenMode = 2;
        if (Tab.IsSelected("m_showFrameRate") is bool fr) State.ShowFrameRate = fr;
        if (Tab.GetValue("m_effectStep") is float es) State.EffectStep = (int)es;
        if (Tab.GetValue("m_quality") is float q) State.Quality = (int)q;
        if (Tab.IsSelected("m_hdr") is bool hdr) State.DisplayHdr = hdr;
        if (Tab.IsSelected("m_shimmer") is bool sh) State.DisplayShimmer = sh;
        if (Tab.GetSelectedIndex("m_shadow") is int sd)
        {
            if (sd == 0) State.ShadowLevel = 0;
            else if (sd == 1) State.ShadowLevel = 2;
            else if (sd == 2) State.ShadowLevel = 3;
        }
        if (Tab.IsSelected("m_vsync") is bool vs) State.Vsync = vs;
        if (Tab.IsSelected("m_enchantEffect") is bool en) State.ShowEnchantEffect = en;
        if (Tab.GetSelectedIndex("m_multisampling") is int ms) State.MultisamplingIndex = ms;
        if (Tab.GetSelectedIndex("m_optimalHide") is int oh) State2.OptimalHide = oh;
        if (Tab.IsSelected("m_hideAlways") is bool ha) State2.HideAlways = ha;
        if (Tab.IsSelected("m_hideBossBattle") is bool hb) State2.HideBossBattle = hb;
        if (Tab.IsSelected("m_showParty") is bool sp) State2.ShowParty = sp;
        if (Tab.IsSelected("m_showGuild") is bool sg) State2.ShowGuild = sg;
        if (Tab.IsSelected("m_showCustom") is bool sc) State2.ShowCustom = sc;
        if (Tab.GetText("m_customInput") is string ci) State2.CustomCount = Wtoi(ci);
        if (Tab.IsSelected("m_showPhysx") is bool ph) State2.ShowPhysx = ph;
    }

    /// <summary>FUN_1407181a0, _wtoi: leading blanks, a sign, then digits; 0 when there are none.</summary>
    public static int Wtoi(string s)
    {
        int i = 0;
        while (i < s.Length && (s[i] == ' ' || (s[i] >= '\t' && s[i] <= '\r'))) i++;
        bool neg = false;
        if (i < s.Length && (s[i] == '+' || s[i] == '-')) neg = s[i++] == '-';
        long v = 0;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9' && v <= int.MaxValue) v = v * 10 + (s[i++] - '0');
        if (v > int.MaxValue) return neg ? int.MinValue : int.MaxValue;
        return (int)(neg ? -v : v);
    }

    /// <summary>CGameOptionGraphicDefault::FUN_14124ce20 @0x14124ce20: carry out what changed against the stored option. A changed screen mode or row
    /// switches through FUN_14125e910 on the stored option and takes its size back. Multisampling and vsync only raise
    /// the notice 0x84e: their setters FUN_14125e4b0 / FUN_14125e5b0 have empty bodies (gameoptionstruct.cpp:2112,2136).</summary>
    public void ApplyChangedSettings()
    {
        var g = Global;
        bool notify = false;
        if (State.ScreenMode != g.ScreenMode || State.ResolutionIndex != g.ResolutionIndex)
        {
            var req = Host.Modes.Switch(g, State.ScreenMode, State.ResolutionIndex, out _);
            if (req != null) Host.ApplyWindow(req.Value);
            State.Width = g.Width;
            State.Height = g.Height;
        }
        if (State.Quality != g.Quality) Host.SetQuality(State, State.Quality);
        if (State.DisplayHdr != g.DisplayHdr) Host.SetHdr(State, State.DisplayHdr);
        if (State.DisplayShimmer != g.DisplayShimmer) Host.SetShimmer(State, State.DisplayShimmer);
        if (State.EffectStep != g.EffectStep) Host.SetEffectStep(State.EffectStep);
        if (State.MultisamplingIndex != g.MultisamplingIndex) notify = true;
        if (State.Vsync != g.Vsync) notify = true;
        Host.ShowFrameRate(State.ShowFrameRate);
        if (notify) Host.Notice(0x84e);
    }

    /// <summary>@0x14124d6a0: apply, then the page state becomes the stored option (FUN_14125dff0 / FUN_14125ef60).
    /// FUN_14125ef90(&amp;m_state2) runs on the page copy after it is stored.</summary>
    public override void Save()
    {
        base.Save();
        bool oldEnchant = Global.ShowEnchantEffect;
        ApplyChangedSettings();
        Host.Options.GraphicOption = State.Clone();
        Host.Options.OptimizerOption = State2.Clone();
        if (State.ShowEnchantEffect != oldEnchant)
            Host.EnchantEffectChanged(State.ShowEnchantEffect);
    }

    /// <summary>@0x14124d7a0: the defaults (FUN_14125d8c0, then LoadGameOptionGraphic of the default document in its
    /// default form; FUN_14125ec50, then LoadGameOptionOptimizer of the default document), then refill.</summary>
    public override void ResetToDefaults()
    {
        base.ResetToDefaults();
        var doc = Host.DefaultGameDocument ?? new System.Xml.Linq.XDocument();
        var (w, h) = Host.ScreenSize;
        State = new GameOption.Graphic();
        State.LoadDefault(doc, Host.BenchmarkLevel, Host.IsHdrSupported, Host.IsShimmerSupported, Host.Modes, w, h);
        State2 = new GameOption.Optimizer();
        State2.Load(doc);
        FillControls();
    }

    /// <summary>@0x14124d890: either block differs from the stored option.</summary>
    public override int HasChanges() => GameOption.SameFields(State, Global) && GameOption.SameFields(State2, Global2) ? 0 : 1;

    /// <summary>@0x14124d080.</summary>
    public void FillResolutionCombo()
    {
        var rows = ResolutionRows(Host.Modes, Host.FormatStringId);
        if (rows.Count == 0) return;
        int selected = 0;
        for (int j = 0; j < rows.Count; j++)
            if (rows[j].Width == State.Width && rows[j].Height == State.Height)
                selected = j;
        Tab.SetResolutionRows("m_resolution", rows, selected);
    }

    /// <summary>The rows of FillResolutionCombo: the usable modes (FUN_14125e7e0) by mode index, sorted by width then height
    /// (Unk_GoResModeLess @0x14124fdc0, a stable order is not promised by std::sort but equal sizes cannot occur in the
    /// unique mode list), labelled with 0x310 s_option_resolution_wide when the ratio is at least 1.59, else 0x30f
    /// s_option_resolution, both formatted with (width, height, ratio a, ratio b).</summary>
    public static List<ResolutionRow> ResolutionRows(ScreenModes modes, Func<int, object[], string> format)
    {
        var rows = new List<ResolutionRow>();
        for (int i = 0; i < modes.Modes.Count; i++)
            if (ScreenModes.Usable(modes.Modes[i]))
                rows.Add(new ResolutionRow(i, modes.Modes[i].Width, modes.Modes[i].Height, null));
        rows.Sort((a, b) => a.Width != b.Width ? a.Width.CompareTo(b.Width) : a.Height.CompareTo(b.Height));
        for (int j = 0; j < rows.Count; j++)
        {
            var (a, b) = GetAspectRatio(rows[j].Width, rows[j].Height);
            int id = (float)a / b >= 1.59f ? 0x310 : 0x30f;
            rows[j] = rows[j] with { Label = format(id, new object[] { rows[j].Width, rows[j].Height, a, b }) };
        }
        return rows;
    }

    /// <summary>@0x14124d5f0: the size of an adapter mode; unchanged when there is no such mode.</summary>
    void GetModeSize(int index, ref int width, ref int height)
    {
        var m = Host.Modes;
        if (index < 0 || index >= m.Modes.Count) return;
        width = m.Modes[index].Width;
        height = m.Modes[index].Height;
    }

    /// <summary>Gcd @0x14124d900.</summary>
    public static int Gcd(int a, int b)
    {
        while (b != 0)
        {
            int r = a % b;
            a = b;
            b = r;
        }
        return Math.Abs(a);
    }

    /// <summary>RoundHalfUp @0x14124d950: the integer part, plus one when the fraction is at least 0.5.</summary>
    public static float RoundHalfUp(float f)
    {
        float i = (int)f;
        return 0.5f <= f - i ? MathF.Ceiling(i + 1f) : i;
    }

    /// <summary>GetAspectRatio @0x14124d9d0 (gameoption.cpp:1499-1556): the reduced ratio; ratios in [1.6, 1.67) are
    /// scaled to a 16-wide form, other large ones are rounded to a tenth (under 1.57) or to x:9.</summary>
    public static (int A, int B) GetAspectRatio(int width, int height)
    {
        int g = Gcd(width, height);
        int a = width, b = height;
        if (1 < g)
        {
            a /= g;
            b /= g;
        }
        bool scale = 0x10 < a;
        if (!scale)
        {
            float r = (float)a / b;
            if (r < 1.6f || 1.67f <= r)
                scale = 5 < a;
            else
            {
                int mul = (int)(16.0f / a);
                a *= mul;
                b *= mul;
                if (a < 0x10 && (g = Gcd(a, b)) > 1)
                {
                    a /= g;
                    b /= g;
                }
            }
        }
        if (scale)
        {
            float r = (float)a / b;
            float scaled = r < 1.6f ? RoundHalfUp(r * 10.0f) : r * 10.0f;
            if (r < 1.57f)
            {
                int n = (int)scaled;
                float mul = (float)n / a;
                a = n;
                b = (int)RoundHalfUp(b * mul);
                float r2 = (float)a / b;
                if ((r2 < 1.6f || 1.67f <= r2) && (g = Gcd(a, b)) > 1)
                {
                    a /= g;
                    b /= g;
                }
            }
            else
            {
                float mul = 9.0f / b;
                a = (int)RoundHalfUp(a * mul);
                b = 9;
            }
        }
        return (a, b);
    }
}

/// <summary>CGameOptionSoundDefault (gameoptionstruct.cpp:440-680).</summary>
public sealed class SoundOptionPage : OptionPage
{
    public GameOption.Sound State = new();
    public SoundFlag Flag = new();

    public SoundOptionPage(int id, int type) : base(id, type) { }

    /// <summary>@0x141256dd0.</summary>
    public override void Open(int n)
    {
        if (n == 0)
        {
            State = Host.Options.SoundOption.Clone();
            Flag = Host.SoundFlag.Clone();
        }
        base.Open(n);
    }

    static readonly (string Name, Func<GameOption.Sound, int> Get, Action<GameOption.Sound, int> Set)[] Steps =
    {
        ("m_mainSoundStep", s => s.MainSoundStep, (s, v) => s.MainSoundStep = v),
        ("m_effectSoundStep", s => s.EffectSoundStep, (s, v) => s.EffectSoundStep = v),
        ("m_enviromentSoundStep", s => s.EnviromentSoundStep, (s, v) => s.EnviromentSoundStep = v),
        ("m_uiSoundStep", s => s.UiSoundStep, (s, v) => s.UiSoundStep = v),
        ("m_npcSoundStep", s => s.NpcSoundStep, (s, v) => s.NpcSoundStep = v),
        ("m_bossSoundStep", s => s.BossSoundStep, (s, v) => s.BossSoundStep = v),
        ("m_bgmSoundStep", s => s.BgmSoundStep, (s, v) => s.BgmSoundStep = v),
        ("m_systemSoundStep", s => s.SystemSoundStep, (s, v) => s.SystemSoundStep = v),
        ("m_movieSoundStep", s => s.MovieSoundStep, (s, v) => s.MovieSoundStep = v),
    };

    /// <summary>@0x141256e90. The instrument row shows only with feature 10.</summary>
    public override void FillControls()
    {
        Tab.SetSelected("m_bMuteAll", State.MuteAll);
        Tab.SetSelected("m_bPlayAll", State.PlayAll);
        Tab.SetSelected("m_disableJobSound", Flag.DisableJobSound);
        foreach (var s in Steps) Tab.SetValue(s.Name, s.Get(State));
        if (Host.IsFeatureEnabled(10))
        {
            if (Tab.SetClipShown("m_playInstrumentStep", true))
            {
                Tab.SetValue("m_playInstrumentStep.m_slider", State.InstrumentSoundStep);
                Tab.SetSelected("m_playInstrumentStep.m_isOffBgm", State.OffBgmWhilePlayInstrument);
            }
        }
        else Tab.SetClipShown("m_playInstrumentStep", false);
        Tab.SetValue("m_charSoundStep", State.CharSoundStep);
    }

    /// <summary>@0x141257a90.</summary>
    public override void ReadControls()
    {
        if (Tab.IsSelected("m_bMuteAll") is bool m) State.MuteAll = m;
        if (Tab.IsSelected("m_bPlayAll") is bool p) State.PlayAll = p;
        if (Tab.IsSelected("m_disableJobSound") is bool j) Flag.DisableJobSound = j;
        foreach (var s in Steps)
            if (Tab.GetValue(s.Name) is float v) s.Set(State, (int)v);
        if (Host.IsFeatureEnabled(10))
        {
            if (Tab.GetValue("m_playInstrumentStep.m_slider") is float iv) State.InstrumentSoundStep = (int)iv;
            if (Tab.IsSelected("m_playInstrumentStep.m_isOffBgm") is bool off) State.OffBgmWhilePlayInstrument = off;
        }
        if (Tab.GetValue("m_charSoundStep") is float c) State.CharSoundStep = (int)c;
    }

    /// <summary>@0x1412585a0: the page's volumes are set (FUN_14125fe20 on the page state), then stored, the instruments
    /// follow, and the character settings are written.</summary>
    public override void Save()
    {
        base.Save();
        Host.ApplySoundVolumes(SoundVolumes.From(State, Host.InstrumentPlaying, Host.AppActive));
        Host.Options.SoundOption = State.Clone();
        Host.SoundFlag.DisableJobSound = Flag.DisableJobSound;
        Host.UpdateInstrumentVolume();
        Host.SaveCharacterSettings();
    }

    /// <summary>@0x141258650: the defaults (FUN_14125f740 + LoadGameOptionSound of the default document, FUN_1412603b0 +
    /// FUN_1412603d0), then refill.</summary>
    public override void ResetToDefaults()
    {
        base.ResetToDefaults();
        var doc = Host.DefaultGameDocument ?? new System.Xml.Linq.XDocument();
        State = new GameOption.Sound();
        State.Load(doc);
        Flag = new SoundFlag();
        Flag.Load(doc);
        FillControls();
    }

    /// <summary>@0x141258730: as the client has it, the page counts as changed only when both the state and the flag
    /// differ.</summary>
    public override int HasChanges()
    {
        if (GameOption.SameFields(State, Host.Options.SoundOption)) return 0;
        if (Flag.DisableJobSound == Host.SoundFlag.DisableJobSound) return 0;
        return 1;
    }
}
