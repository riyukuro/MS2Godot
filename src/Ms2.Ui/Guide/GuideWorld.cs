using System.Numerics;
using Ms2.Net.Wire;

namespace Ms2.Ui.Guide;

/// <summary>
/// What the guide reads from and does to the rest of the client: the singletons the conditions and actions of
/// client/ms2/guideentity_9.cpp, unnamed/unit_1413a*.cpp and guideaction.cpp call. Each member's default is the client's
/// answer when the object it asks is not there (no player, no dialog, no house), so a world that overrides nothing lets
/// no condition but the time and trigger ones pass. The game overrides what it has; the tests override what they test.
/// </summary>
public class GuideWorld
{
    // ---- time, scene and input ---------------------------------------------------------------------------------

    /// <summary>GetTickCountMs2: the client's millisecond clock.</summary>
    public virtual uint Now => (uint)Environment.TickCount;
    /// <summary>GetServerTime: the server's time in Unix seconds.</summary>
    public virtual long ServerTime => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    /// <summary>GFxSceneService::GetSceneId.</summary>
    public virtual int SceneId => 0;
    /// <summary>GFxSceneService::GetSubSceneId.</summary>
    public virtual int SubSceneId => 0;
    /// <summary>GFxSceneService sub scene change (SequenceCamera's cinematic scene and back).</summary>
    public virtual void SetSubScene(int id) { }
    /// <summary>CUIService::IsTextFieldFocused.</summary>
    public virtual bool TextFocused => false;
    /// <summary>The keyboard's IsKeyDown(key, false, 0).</summary>
    public virtual bool IsKeyDown(int key) => false;
    /// <summary>CInputKeyHandler::IsActionKeyDown(action, false, 0).</summary>
    public virtual bool IsActionKeyDown(int action) => false;
    /// <summary>CKeyTable::FindKeyByActionAndPriority: the key of an action's binding of a priority (0 when none).</summary>
    public virtual int FindKeyByActionAndPriority(int action, int priority) => 0;
    /// <summary>CKeyTable::GetPairKey: the other key of a paired binding.</summary>
    public virtual bool GetPairKey(int key, out int pair) { pair = 0; return false; }
    /// <summary>The input device state UITextBase::ControllerMatches reads: 0 the keyboard, 1 a gamepad.</summary>
    public virtual int GamepadState => 0;
    /// <summary>CGameOption's guide mode (m_nMode of FUN_140e09150): 0 shows only the forced guide events.</summary>
    public virtual int GuideMode => 1;
    /// <summary>DAT_143ab2890: a field move is in progress (FieldId fails meanwhile).</summary>
    public virtual bool Migrating => false;
    /// <summary>The cut scene movie dialog (0x4e7e) plays: no guide update and no trigger meanwhile.</summary>
    public virtual bool CutScenePlaying => false;

    /// <summary>The guide event pane (FindGuideEvent(0x4e53)); null while it is not there.</summary>
    public virtual IGuideEventPane Pane => null;
    /// <summary>The app has an active window (GetState()->m_pActiveWnd): the load errors are shown then.</summary>
    public virtual bool AppActive => true;
    /// <summary>FUN_1412751b0(text, 0x14): the manager's messages (load errors, debug lines). STILL-OPEN: which notice
    /// kind 0x14 shows as; the default drops them.</summary>
    public virtual void GuideLog(string text) { }

    // ---- dialogs -------------------------------------------------------------------------------------------------

    /// <summary>FindDialog(id).</summary>
    public virtual GuideDialog FindDialog(int id) => null;
    /// <summary>Unk_GaGetUIByName&lt;UIDialog&gt;(name).</summary>
    public virtual GuideDialog FindDialog(string name) => null;
    /// <summary>OpenDialog(id) (the OpenDialog action).</summary>
    public virtual void OpenDialog(int id) { }

    /// <summary>The achieve dialog (0x4e73) is open and not hidden: its tab; else null.</summary>
    public virtual int? AchieveTab => null;
    /// <summary>The inventory (0x4e2d) is open: its selected tab; else null.</summary>
    public virtual int? InventoryTab => null;
    /// <summary>The party dialog: IsObserveModeAvailable; null when there is none.</summary>
    public virtual bool? CanObserveMode => null;
    /// <summary>The chat emoticon dialog (0x4f80): the flag the ChatEmoticon condition reads (Unk_GcNoPoint, STILL-OPEN
    /// what it is); null when there is none.</summary>
    public virtual bool? ChatEmoticonFlag => null;
    /// <summary>The mastery dialog (0x4f1e): its main / sub tab index; null when there is none.</summary>
    public virtual int? MasteryMainTab => null;
    public virtual int? MasterySubTab => null;
    /// <summary>The mentoring dialog (0x4f4b) is open and shows a page of this name.</summary>
    public virtual bool MentoringPageShown(string page) => false;
    /// <summary>The game menu (0x4e28): IsMenuExpanded(name); null when there is none.</summary>
    public virtual bool? MenuExpanded(string name) => null;
    /// <summary>The instrument dialog (0x4eec): HasAnyScore / the tab; null when there is none.</summary>
    public virtual bool? InstrumentHasScore => null;
    public virtual int? InstrumentTab => null;
    /// <summary>The enchant dialog (0x4e92): an item is selected; null when there is none.</summary>
    public virtual bool? EnchantHasItem => null;
    /// <summary>The UGC management dialog (0x4ec5) is there and not hidden.</summary>
    public virtual bool UgcManagementShown => false;
    /// <summary>The cube palette: the selected item id; null when there is none.</summary>
    public virtual int? CubePaletteSelection => null;

    // ---- the player ----------------------------------------------------------------------------------------------

    /// <summary>WorldService::GetLocalPlayer is set.</summary>
    public virtual bool HasPlayer => false;
    /// <summary>The player's position (vfunc_18).</summary>
    public virtual Vector3 PlayerPosition => Vector3.Zero;
    /// <summary>The cell of the active cube craft when the player has a construction guide; else null.</summary>
    public virtual Vector3? ConstructionCell => null;
    public virtual bool InBattle => false;
    /// <summary>IsCubeMode1 (ConstructMode) and MyPC FUN_141527f60 (Constructable).</summary>
    public virtual bool CubeMode => false;
    public virtual bool Constructable => false;
    public virtual bool Dead => false;
    /// <summary>FindQuickSlotBySkill: the quick slot holding a skill, -1 when none.</summary>
    public virtual int FindQuickSlotBySkill(int skillId) => -1;
    /// <summary>The skill id in a quick slot.</summary>
    public virtual int QuickSlotSkill(int slot) => 0;
    /// <summary>The stat manager's remaining points; null when there is no stat manager.</summary>
    public virtual int? StatPointsLeft => null;
    public virtual int JobGroup => 0;
    public virtual int SubJob => 0;
    public virtual int Level => 0;
    /// <summary>The player's state id (State; PlayingInstrument tests 0x2f).</summary>
    public virtual int PlayerState => 0;
    /// <summary>FUN_14166cc80 while playing an instrument (PlayingInstrument tests 0x7f; STILL-OPEN what it is).</summary>
    public virtual int InstrumentMotion => 0;
    public virtual bool PetSummoned => false;
    public virtual int TitleCount => 0;

    // ---- items ---------------------------------------------------------------------------------------------------

    /// <summary>CountItemsOfType(type) over the inventory.</summary>
    public virtual int CountItemsOfType(int itemType) => 0;
    /// <summary>FUN_1413a9ac0: the indoor install items (CountInDoorInstallItem).</summary>
    public virtual int CountIndoorInstallItems => 0;
    /// <summary>An inventory item that is valid, has a cube texture mesh and no UGC id, of a type (0 any).</summary>
    public virtual bool HasDesignItem(int itemType) => false;
    /// <summary>The equipment slot at (slot name, slot type): null when there is none; else its item id and whether an
    /// item is in it and valid.</summary>
    public virtual GuideEquipSlot? Equip(string slotName, int slotType) => null;
    public virtual bool HasItemCount(int itemId, int count) => false;
    public virtual bool HasItemOfType(int itemType) => false;
    public virtual bool HasItemWithIconCode(int iconCode) => false;
    /// <summary>GetIconCode(item id) (the AddItemByIconType trigger).</summary>
    public virtual int IconCodeOf(int itemId) => 0;

    // ---- the field -----------------------------------------------------------------------------------------------

    public virtual int FieldId => 0;
    /// <summary>The field's continent code; null when there is no field data.</summary>
    public virtual int? ContinentCode => null;
    /// <summary>DungeonEnterState(dungeon id).</summary>
    public virtual int DungeonEnterState(int dungeonId) => -1;
    /// <summary>The nearest interactable object (FUN_141bc6090(pos, 1)): null when none; else whether its five gates
    /// pass (vfunc_7, FUN_1413fdfc0, CheckItemRequirement, HasAnyRequiredItem, HasRequiredMastery).</summary>
    public virtual bool? NearestInteractableUsable => null;
    /// <summary>The interact object of an id near the player: null when none; else its distance and state.</summary>
    public virtual (float Distance, int State)? InteractObject(int interactId) => null;
    /// <summary>The portal list near the player is not empty.</summary>
    public virtual bool PortalNearby => false;
    /// <summary>FUN_141bc2f40 (ReturnPortal).</summary>
    public virtual bool ReturnPortal => false;

    // ---- quests, achievements, guild -----------------------------------------------------------------------------

    public virtual bool IsQuestRewarded(int questId) => false;
    public virtual bool IsQuestActive(int questId) => false;
    public virtual bool IsQuestComplete(int questId) => false;
    /// <summary>HasCompletedOfType(achieve type) (AchieveExistTrophy).</summary>
    public virtual bool HasTrophyOfType(int? type) => false;
    public virtual bool InGuild => false;
    /// <summary>FUN_1404c6160: a guild PvP season runs.</summary>
    public virtual bool GuildPvpSeason => false;
    /// <summary>The HasManufacture condition's answer.</summary>
    public virtual bool HasManufacture => false;

    // ---- money, home and UGC -------------------------------------------------------------------------------------

    /// <summary>The balance of a HasMoney type: 0 meso, 1 merat, then the globals DAT_143ab28b8, b0, c0, c8, d0, d8, e0
    /// (2 karma, 3 honor, 4 lu by the loader's names; 5-8 STILL-OPEN).</summary>
    public virtual long Balance(int moneyType) => 0;
    /// <summary>The house mode (HomeMode) and FUN_1410d5640 (IsMyHome).</summary>
    public virtual int HouseMode => 0;
    public virtual bool IsMyHome => false;
    public virtual bool HasCubeOfItem(int itemId) => false;
    public virtual int CubeCount => 0;
    public virtual bool HasCubeWithIconCode(int iconCode) => false;
    /// <summary>The cube at a point: null when none; else whether its item is attachable (the item data's m_nValue).</summary>
    public virtual bool? CubeAttachableAt(Vector3 pos) => null;
    /// <summary>The UGC map info's expiry (Unix seconds); null without a player or UGC map info.</summary>
    public virtual long? UgcMapExpire => null;
    /// <summary>FUN_140d56470 &gt; 0 (UgcCollectMoney).</summary>
    public virtual bool UgcMoneyToCollect => false;
    /// <summary>The merat market's earliest sale end (Unix seconds, 0 none); null without the market.</summary>
    public virtual long? EarliestSaleEnd => null;

    // ---- what the actions do -------------------------------------------------------------------------------------

    /// <summary>The minimap (0x4e29): SetMax and OnTabKeyDown / OnTabKeyUp.</summary>
    public virtual void SetMinimapMaximized(bool on) { }
    /// <summary>CEffectService FUN_1411a49c0: plays an effect at a transform (translate, rotation
    /// NiMatrix3::FromEulerAnglesXYZ of <paramref name="eulerRadians"/>, scale); a handle (0 none).</summary>
    public virtual int PlayEffect(string path, Vector3 translate, Vector3 eulerRadians, float scale) => 0;
    public virtual void StopEffect(int handle) { }
    /// <summary>The inventory's align mode and open animation (EquipWeaponTutorial).</summary>
    public virtual void SetInventoryTutorial(bool on) { }
    /// <summary>The camera mode (SequenceCamera skips the interpolation in mode 2).</summary>
    public virtual int CameraMode => 0;
    public virtual void CameraInterpolation(float time) { }
    /// <summary>Queues trigger camera steps.</summary>
    public virtual void QueueCameraSteps(IReadOnlyList<int> cameraIds) { }
    /// <summary>Opens a dialog by name when it is hidden (UIDialog FUN_141924630 / FUN_141924680).</summary>
    public virtual void OpenDialog(string name)
    {
        var d = FindDialog(name);
        if (d != null && !d.IsOpen) d.Open();
    }
    /// <summary>The cinematic dialog (0x4e52) shows the skip prompt; <paramref name="onSkip"/> runs on skip. False when the
    /// dialog is not there.</summary>
    public virtual bool StartCinematicSkip(Action onSkip) => false;
    public virtual void StopCinematicSkip() { }
    /// <summary>The smart recommend dialog (0x4e9e): set the item code and reopen it.</summary>
    public virtual void SmartRecommend(int code) { }
    /// <summary>The game menu (0x4e28) and the gamepad menu (0x4f63): SetMenuEnable.</summary>
    public virtual void SetMenuEnable(int menuId, bool enabled) { }
    /// <summary>The side NPC talk popup (0x4f57).</summary>
    public virtual void ShowSideNpcTalk(GuideSideTalk talk) { }
    /// <summary>Sends a packet.</summary>
    public virtual void Send(IOutPacket packet) { }
}

/// <summary>An equipment slot as EquipItem reads it.</summary>
public readonly record struct GuideEquipSlot(int ItemId, bool HasValidItem);

/// <summary>A dialog as the guide reads it (UIDialog).</summary>
public abstract class GuideDialog
{
    /// <summary>GetTypeName (UIObject's vfunc, the client's by-name registry compares it): the default is UIDialog's
    /// "Dialog". The game's wrapper gives its controller's own name.</summary>
    public virtual string TypeName => "Dialog";
    /// <summary>vfunc_55: hidden (not shown).</summary>
    public abstract bool Hidden { get; }
    /// <summary>FUN_141924630: open.</summary>
    public virtual bool IsOpen => !Hidden;
    /// <summary>vfunc_82: the dialog's sub index.</summary>
    public virtual int SubIndex => 0;
    /// <summary>FUN_141924680: opens it.</summary>
    public virtual void Open() { }
    /// <summary>GetDisplayObject: the movie's root; null when it has none.</summary>
    public virtual GuideNode Root => null;
    /// <summary>GetChild&lt;ASDisplayObject&gt;(name): a control of the movie by name.</summary>
    public virtual GuideNode Child(string name) => Root?.Child(name);
}

/// <summary>A display object of a dialog's movie (ASDisplayObject, ASButton).</summary>
public abstract class GuideNode
{
    public abstract bool Visible { get; }
    /// <summary>ASButton::IsSelected.</summary>
    public virtual bool Selected => false;
    /// <summary>ASDisplayObject's x, y and z.</summary>
    public abstract (double X, double Y, double Z) Position { get; }
    /// <summary>ASDisplayObject::GetWidthCeil (asdisplayobject.cpp:539).</summary>
    public virtual double WidthCeil => 0;
    /// <summary>The child of a name.</summary>
    public abstract GuideNode Child(string name);
}

/// <summary>The guide event pane (UIGuideEvent, 0x4e53) the UI actions drive.</summary>
public interface IGuideEventPane
{
    /// <summary>FUN_1422dc2d0: the movie is loaded (UpdateEntities waits for it).</summary>
    bool Loaded { get; }
    void ShowTutorial(int entityId, int textId, bool first);
    void HideTutorial(int entityId);
    bool TutorialShowing { get; }
    /// <summary>ShowSummary (guideId, stringId, durationMs): the action passes 0; the server's trigger packets pass
    /// theirs (triggerservice.cpp:994).</summary>
    void ShowSummary(int entityId, int textId, int durationMs = 0);
    void HideSummary(int entityId);
    bool SummaryShowing { get; }
    void AddArrow(GuideArrow arrow);
    void ReleaseArrow(int entityId);
    void QueueOrShowTextTip(int entityId, int textId);
    void AdvanceTextTip(int entityId);
    /// <summary>The entity id of the last closed text tip (LastClosedTextTip).</summary>
    int LastClosedTip { get; }
}

/// <summary>A guide arrow (UIArrow): where it points and the text beside it.</summary>
public record GuideArrow(int EntityId, Vector3 Offset, int Direction, int Align, int TextId);

/// <summary>An arrow at a dialog's control (UIArrowDialog). <see cref="PosKind"/> is customPosType's text
/// (SetKindFromName; STILL-OPEN until the arrow controller).</summary>
public record GuideDialogArrow(int EntityId, Vector3 Offset, int Direction, int Align, int TextId, int DialogId, string DialogName,
    int ChildIndex, IReadOnlyList<string> ChildPath, IReadOnlyList<string> FocusDialogs, string PosKind, int PosValue)
    : GuideArrow(EntityId, Offset, Direction, Align, TextId);

/// <summary>GetGameMenuPathPosition's moved-entry answer (uiguideeventarrow.cpp:919-928): the shown entry's place
/// and half width, or <see cref="Fail"/> when the movie's getChildObjectPos gave no object — the client's whole
/// lookup fails then (:920-921).</summary>
public readonly record struct GuideMenuMoved(bool Fail, double X, double Y, double HalfWidth);

/// <summary>The side NPC talk popup's content (SideNpcTalk): kind 0 talk, 1 movie, 2 cutin, 3 talkbottom.</summary>
public record GuideSideTalk(int Kind, string Usm, IReadOnlyList<int> ScriptIds, string Illust, long Duration, string Voice, string Sound);
