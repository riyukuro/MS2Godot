using Ms2.Formats.Model;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Quest;
using Ms2.Ui.Hud;

namespace Ms2.Ui;

/// <summary>
/// What the live interact objects read from the game and how they answer: the local player's state, quest / inventory /
/// mastery checks, string tables, casting bar, animation playback and packet sink.
/// </summary>
public interface IInteractHost
{
    bool HasPlayer { get; }
    /// <summary>CGuideManager::IsLimitActive (IsRestricted, kind 3 = ActionInteraction).</summary>
    bool IsRestricted(int kind);
    int FieldId { get; }
    ushort Channel { get; }
    /// <summary>GetTickCountMs2 (ms clock for gathering respawn ticks).</summary>
    long NowMs { get; }
    /// <summary>MyPC::GetCurStateId(): the local player's client state id (1 idle, 2 walk, 0x10 skill, 0x21 riding, ...),
    /// or -1 for a state the host cannot name (refused by <see cref="LiveInteractObjects.StateAllowsInteraction"/>).</summary>
    int PlayerStateId { get; }
    /// <summary>MyPC::GetRidee() != null (actionkeyobject.cpp:1576).</summary>
    bool PlayerRiding { get; }
    /// <summary>The local pc's object id (the own FieldAddUser object), -1 before it is added.</summary>
    int LocalObjectId { get; }
    /// <summary>CItem::FUN_140523c40(itemId, 1, 0) succeeds: the item id is in the item data (pc.cpp:3439).</summary>
    bool ItemExists(int itemId);
    /// <summary>CGameEventValueManager::ShutdownInteractObjectId()->IsShutdown(fieldId, rowId) (interactobject.cpp:401).</summary>
    bool IsInteractObjectShutdown(int fieldId, int rowId);
    /// <summary>MyPC::FUN_14152e0c0 (mypc.cpp:3641): the ride's or the state controller's update before an instant interact
    /// (actionkeyobject.cpp:1597).</summary>
    void FlushPlayerState();
    /// <summary>Client::Constants::MasteryObjectInteractionDistance (actionkeyobject.cpp:2483, stateinteractdata.cpp:283).</summary>
    float MasteryInteractionDistance { get; }

    QuestMaskTable.QuestStatus QuestStatusOf(int questId);
    /// <summary>CInteractObject::CheckItemRequirement (@0x1413fde40, interactobject.cpp:515-533).</summary>
    bool HasRequiredItem(uint code, int rank, uint consume);
    /// <summary>CInteractObject::HasAnyRequiredItem (@0x1413fe220, interactobject.cpp:575-593): additional effect active.</summary>
    bool HasAdditionalEffect(uint effectId, ushort level);
    /// <summary>CStringTable::FUN_141812290(id, level, "name"): the additional effect's name (actionkeyobject.cpp:1551-1555).</summary>
    string AdditionalEffectName(uint effectId, ushort level);
    /// <summary>CPartyManager::IsLocalPlayerLeader (actionkeyobject.cpp:1560).</summary>
    bool IsPartyLeader { get; }
    /// <summary>PC::GetMasteryGrade(type) (interactobject.cpp:508).</summary>
    int MasteryGrade(int masteryType);
    /// <summary>CInteractGatheringObject::vfunc_7 (@0x141402940, interactobject.cpp:1521-1530): recipe mastery & limit checks.</summary>
    bool CanGatherRecipe(int recipeId);

    void Send(IOutPacket packet);
    string Common(int id);
    string CommonByKey(string key);
    /// <summary>CStringTable::FUN_141811980(1, itemCode): item name.</summary>
    string ItemName(uint itemCode);
    /// <summary>CStringTable::FUN_141811980(0xb, rowId): stringInteractObjectAction.</summary>
    string InteractActionName(int rowId);
    /// <summary>CStringTable::FUN_141811980(0x2b, nameId): stringInteractObjectNotice.</summary>
    string InteractNoticeText(int nameId);
    string MasteryTypeName(int type);
    string MasteryGradeName(int type, int value);

    /// <summary>ShowSystemNotice(text, style, 1).</summary>
    void SystemNotice(string text, int style = 1);
    /// <summary>FUN_1412751b0(text, 0x14): chat system message for cross-channel/field interact notices.</summary>
    void ChatNotice(string text);

    /// <summary>UICastingBar::StartCasting (stateinteractdata.cpp:294-296). False when there is no casting bar; the cast
    /// time then runs on <see cref="NowMs"/>.</summary>
    bool StartCasting(ICastingEventListener listener, string text, int durationMs);
    /// <summary>UICastingBar::FUN_1418e1390 (stateinteractdata.cpp:346-348).</summary>
    void CancelCasting();

    /// <summary>StateInteraction::FUN_1417d25e0 / vfunc_4 0x8b / 0x8c: play gather/use animation on MyPC.</summary>
    void PlayPlayerAnim(string animName, bool loop);
    void StopPlayerInteractAnim();

    /// <summary>CInteractMeshObject::vfunc_3 (@0x1413fef20): object state changed (visibility / asset / collision).</summary>
    void OnObjectStateChanged(LiveInteractObject obj);
    /// <summary>WorldService::FUN_141bc5d60 (@0x141bc5d60, worldservice_1.cpp:4511-4522): the object leaves the field's maps and its
    /// entity is destroyed (CMainSystem::DestroyEntityById); its release runs ~CInteractObject (interactobject.cpp:344-351:
    /// ClearStateEffect, the success particle removed) and ~CInteractMeshObject (ReleaseActor, the box).</summary>
    void OnObjectRemoved(LiveInteractObject obj) { }
    /// <summary>SSG_INTERACT_REMOVE's effect (scenestategame.cpp:6415-6426): CEffectService::InsertEffect, one-shot, at the
    /// object's position with no rotation, object kind 0, before the object is removed.</summary>
    void OnObjectRemoveEffect(LiveInteractObject obj, string effect) { }
    /// <summary>CInteractObject::vfunc_12 (@0x1413fe5d0): success particle effect at object pose.</summary>
    void OnObjectSuccessEffect(LiveInteractObject obj, string effectAsset);
    /// <summary>CInteractUIObject::vfunc_2 (@0x141400260, interactobject.cpp:1045-1056): the dialog the swf name finds
    /// (UISwfEntityManager::GetUISwfEntity, the name trimmed and its type name compared without case), given the value when
    /// it is not empty (UIDialog::FUN_1419265e0, m_170) and opened (CUIService::OpenDialog). Its ui class id, or 0 when no
    /// dialog is found (nothing opens).</summary>
    int OnOpenUiDialog(string swf, string value);
    /// <summary>CUIService::IsDialogClosed(id).</summary>
    bool IsDialogClosed(int dialogId) => true;
    /// <summary>CUIService::CloseDialog(id).</summary>
    void CloseDialog(int dialogId) { }
    /// <summary>CGuideManager::AddTrigger(9, rowId, 0) (stateinteractdata.cpp:484).</summary>
    void OnGuideTrigger(int triggerType, int rowId);
    void StillOpen(string what);
}

/// <summary>
/// One field interact object (CInteractObject and its subclasses, interactobject.cpp).
/// </summary>
public sealed class LiveInteractObject
{
    /// <summary>Lowercase entity UUID (FUN_141b70dd0 + ToLower, interactobject.cpp:365-366) or spawned packet name.</summary>
    public string Name = "";
    public int RowId;
    /// <summary>vfunc_5(): 1 Mesh, 2 Telescope, 3 UI, 4 Web, 5 DisplayImage, 6 Gathering, 7 GuildPoster, 8 BillBoard, 9 WatchTower.</summary>
    public byte Kind;
    /// <summary>m_nState: 0 normal, 1 reactable, 2 hidden.</summary>
    public byte State;
    public Vec3 Position, Rotation;
    public float Scale = 1f;
    public string Model = "", Anim = "", AssetA = "", AssetB = "";
    /// <summary>CInteractMeshObject::m_nAssetType (@0x1413febf0, interactobject.cpp:752-777): 1 for an MS2InteractMesh entity
    /// (<see cref="AssetA"/> and <see cref="AssetB"/> are NifAsset urns, set on NifAsset 0x770e5fc6), 2 for an MS2InteractActor
    /// (they are sequence names, played on the actor), 0 for the kinds that are not mesh objects (telescope, UI, web, display
    /// image, guild poster).</summary>
    public byte AssetType;
    public long BoardOwner;
    public string BoardText = "";
    /// <summary>CInteractGatheringObject::m_nRespawnTick (ms timestamp, 0 when not cooling down).</summary>
    public long RespawnTickMs;
    public bool Spawned;
    public bool Highlighted;
    /// <summary>CInteractObject::m_nShownState: the state whose effect is up (3: none, ClearStateEffect).</summary>
    public byte ShownState = 3;
    /// <summary>CInteractUIObject::m_nDialogId: the ui class id of the dialog its message opened (0: none).</summary>
    public int DialogId;
    /// <summary>CInteractWatchTowerObject::m_bNear: the local player is within 2000 of a watch tower (x and y only), set by
    /// <see cref="LiveInteractObjects.Tick"/>; false at the register (vfunc_1, @0x141403470, interactobject.cpp:1700-1706).</summary>
    public bool Near;
    public FieldEntity Entity;
    public InteractObjectRow Row;
}

/// <summary>
/// The field's interact objects (WorldService m_678, worldservice_1.cpp:6871-6912), the RecvOp::InteractObject (0x63)
/// handler (CSceneStateGame::OnInteractObject @0x14172d0f0, scenestategame.cpp:6218-6494), the action-key picker
/// (InteractAction @actionkeyobject.cpp:1456-1616, GatheringAction :2467-2543, WorldService::FUN_141bc6090
/// @worldservice_1.cpp:4546-4578) and StateInteraction (@stateinteractdata.cpp:244-541).
/// <para>
/// Per-frame polling (<see cref="Poll"/> and <see cref="Tick"/>) uses indexed loops and reused lists with zero managed
/// allocations.
/// </para>
/// <para>
/// STILL-OPEN: CInteractObject::CheckHomePrivilege (m_homePrivilege.m_nCheck != 0), CInteractObject::CheckArenaTeam
/// (m_arena.m_nCanInteractTeamId != 0), ShutdownInteractObjectId event gate, CInteractTelescopeObject cutscene subscene
/// (0x6200041), CInteractMeshObject dynamic PhysX box toggle / NIF asset swap, CInteractGuildPosterObject upload,
/// CInteractBillBoardObject owner dialog check, RideOffActionInteract dismount-then-interact, and OnInteractObject
/// mode 15 (field weapon equip PC::FUN_14165f1f0).
/// </para>
/// </summary>
public sealed class LiveInteractObjects : ICastingEventListener
{
    readonly IInteractHost host;
    readonly InteractObjectTable table;
    readonly Random rng;
    readonly Dictionary<string, LiveInteractObject> byName = new(StringComparer.OrdinalIgnoreCase);
    readonly List<LiveInteractObject> list = new();
    readonly List<InteractObjectMessage> pending = new();
    readonly HashSet<int> collectedIds = new();                  // DAT_143ab3128 (telescope / collection first-use notice)
    readonly Dictionary<int, int> gatherCounts = new();          // g_143ab3150 (recipe -> gathered count)
    // reused buffers for zero-allocation Poll / Tick / gathering sends
    readonly List<LiveInteractObject> gatherInRange = new();
    readonly List<LiveInteractObject> activeGatherTargets = new();
    readonly Func<int, QuestMaskTable.QuestStatus> questLookup;

    /// <summary>CGameEventValueManager's MasteryReduce (the game event list's), for the gathering cast time and caption; the
    /// session sets its own, kept across fields.</summary>
    public MasteryReduceRule MasteryReduce = new();

    LiveInteractObject polled, gatherPick, interactPick, boardPick;
    bool polledGathering;
    // StateInteraction (0x19) active cast state
    LiveInteractObject castingTarget;
    bool castingFinished;
    /// <summary>The cast's end on <see cref="IInteractHost.NowMs"/> when no casting bar runs it; 0 otherwise.</summary>
    long castEndMs;
    bool castingActive;

    public LiveInteractObjects(IInteractHost host, InteractObjectTable table, int seed = 0)
    {
        this.host = host;
        this.table = table;
        rng = new Random(seed);
        questLookup = host.QuestStatusOf;
    }

    public bool Attached { get; private set; }
    public IReadOnlyList<LiveInteractObject> All => list;
    public LiveInteractObject Current => polled;
    public bool IsGatheringCurrent => polledGathering;
    public bool IsCasting => castingActive;
    /// <summary>The local pc's held item (PC::GetHeldItem), shared with the npc interaction; null: none is tracked.</summary>
    public HeldItem Held { get; set; }
    public LiveInteractObject CastingTarget => castingTarget;
    public IReadOnlyDictionary<int, int> GatherCounts => gatherCounts;

    public LiveInteractObject ByName(string name) =>
        string.IsNullOrEmpty(name) ? null : byName.TryGetValue(name, out var o) ? o : null;

    /// <summary>
    /// WorldService field-load registration (worldservice_1.cpp:6871-6912): scans xblock entities carrying the
    /// MS2InteractObject model/mixin (tag 0x72f49865), creates each with initial state 0 and lowercase entity UUID name
    /// (FUN_141b70dd0 + ToLower, interactobject.cpp:365-366), then applies any InteractObject messages received while the
    /// field loaded.
    /// </summary>
    public void Attach(IReadOnlyList<FieldEntity> entities)
    {
        if (Attached) return;
        Attached = true;
        if (entities != null)
        {
            for (int i = 0; i < entities.Count; i++)
            {
                var e = entities[i];
                if (!IsInteractEntity(e)) continue;
                int rowId = (int)e.Number("interactID");
                var row = table?.Get(rowId);
                if (row == null) continue;
                string rawId = e.Raw?.Id;
                if (string.IsNullOrEmpty(rawId)) continue;
                string name = rawId.ToLowerInvariant();
                if (byName.ContainsKey(name)) continue;
                var (assetType, assetA, assetB) = MeshAssets(e);
                // CInteractMeshObject::vfunc_1 returns 0 for an entity of neither model before CInteractObject::vfunc_1 sets
                // the row and the name, so no mesh-kind object of another model is reachable by name.
                if (IsMeshKind(row.Type) && assetType == 0) continue;
                var obj = new LiveInteractObject
                {
                    Name = name,
                    RowId = rowId,
                    Kind = (byte)row.Type,
                    State = 0,
                    Position = e.Position,
                    Rotation = e.Vector("Rotation"),
                    Scale = (float)e.Number("Scale", 1),
                    AssetType = IsMeshKind(row.Type) ? assetType : (byte)0,
                    AssetA = IsMeshKind(row.Type) ? assetA : "",
                    AssetB = IsMeshKind(row.Type) ? assetB : "",
                    Entity = e,
                    Row = row,
                };
                byName[name] = obj;
                list.Add(obj);
                host.OnObjectStateChanged(obj);   // CInteractObject::vfunc_1 ends with vfunc_3(nState), here 0 (interactobject.cpp:369)
            }
        }
        for (int i = 0; i < pending.Count; i++) Apply(pending[i]);
        pending.Clear();
    }

    /// <summary>The kinds the factory makes a CInteractMeshObject or a subclass of it for (worldservice_1.cpp:6884-6902):
    /// mesh, gathering, bill board and watch tower.</summary>
    public static bool IsMeshKind(int kind) =>
        kind == InteractObjectMessage.KindMesh || kind == InteractObjectMessage.KindGathering ||
        kind == InteractObjectMessage.KindBillBoard || kind == InteractObjectMessage.KindWatchTower;

    /// <summary>CInteractMeshObject::vfunc_1 (@0x1413febf0, interactobject.cpp:752-777): an MS2InteractMesh entity's assets are
    /// NifAsset (0x770e5fc6) and reactableAssetName (0xae1cf70e), an MS2InteractActor's are InitialSequence (0x3ff293c8) and
    /// reactableSequenceName (0x63c7b14a); any other model has none (type 0).</summary>
    public static (byte Type, string A, string B) MeshAssets(FieldEntity e)
    {
        if (e.HasMixin("MS2InteractMesh")) return (1, e.Get("NifAsset") ?? "", e.Get("reactableAssetName") ?? "");
        if (e.HasMixin("MS2InteractActor")) return (2, e.Get("InitialSequence") ?? "", e.Get("reactableSequenceName") ?? "");
        return (0, "", "");
    }

    /// <summary>What CInteractMeshObject::vfunc_3 (@0x1413fef20, interactobject.cpp:781-819) does to a mesh object's entity
    /// for its state. State 2 sets IsVisible (0xa0b6b179) false and returns: <paramref name="asset"/> is null and
    /// <paramref name="collision"/> -1, the asset and the box stay as they were. Any other state sets IsVisible, applies asset A
    /// (state 0) or B (FUN_1413ff690: an MS2InteractMesh's NifAsset, an MS2InteractActor's sequence played once, PlayAnimation
    /// 1.0f, false) and SetCollision of the row's NormalCollision (state 0) or ReactableCollision: non-zero makes the box
    /// (MakeNxBoxController), zero releases it (ReleaseActor).
    /// False for an object that is not a mesh object, and for a watch tower, whose own vfunc_3 (@0x1414034e0, :1721-1734)
    /// sets neither IsVisible nor the box and applies the asset only while the player is near (<see cref="WatchTowerAsset"/>).</summary>
    public static bool MeshDraw(LiveInteractObject o, out bool visible, out string asset, out int collision)
    {
        visible = true; asset = null; collision = -1;
        if (o?.Row == null || o.AssetType == 0 || !IsMeshKind(o.Kind) || o.Kind == InteractObjectMessage.KindWatchTower) return false;
        if (o.State == 2) { visible = false; return true; }
        bool first = o.State == 0;
        asset = first ? o.AssetA : o.AssetB;
        collision = first ? o.Row.NormalCollision : o.Row.ReactableCollision;
        return true;
    }

    /// <summary>CInteractObject::UpdateStateEffect (@0x1413fe500, interactobject.cpp:627-634) for the object's state: the
    /// effect to show (vfunc_13, @0x1413fe7d0: state 0 the row's normal effect, 1 its reactable effect, else none), or null to
    /// clear it, when the row's use mask (FUN_1413fdfc0) or the state's mask (GetStateQuestMask) fails. ShowStateEffect
    /// (@0x1413fda50, :451-471) keeps an effect up while the shown state is the same and sets the shown state only when it
    /// plays one, at the object's position + 1 z with its rotation; ClearStateEffect resets it to 3. The host plays and
    /// removes it (<see cref="IInteractHost.OnObjectStateChanged"/>); this sets <see cref="LiveInteractObject.ShownState"/>.
    /// Returns whether the shown effect changes, and the effect to show (empty: none).
    /// STILL-OPEN: a gathering object's vfunc_13 (@0x141402880, :1504-1509) shows its effect only when the mastery manager's
    /// FUN_140b6d470(recipe) is zero, which has no port; a gathering object shows none.</summary>
    /// <summary>DAT_143aacd50, the empty mask GetStateQuestMask gives a state other than 0 and 1 (it passes).</summary>
    static readonly QuestMaskTable.Mask EmptyMask = new();

    public bool StateEffect(LiveInteractObject o, out string effect)
    {
        effect = "";
        if (o?.Row == null) return false;
        if (o.Kind == InteractObjectMessage.KindWatchTower) return WatchTowerEffect(o, out effect);
        string want = null;
        if (CheckQuestMask(o) && QuestMaskTable.Check(o.State == 0 ? o.Row.MaskNormal : o.State == 1 ? o.Row.MaskReactable : EmptyMask, questLookup))
        {
            if (o.ShownState == o.State) return false;
            want = o.Kind == InteractObjectMessage.KindGathering ? "" : o.State == 0 ? o.Row.EffectNormal : o.State == 1 ? o.Row.EffectReactable : "";
        }
        bool wasUp = o.ShownState != 3;
        o.ShownState = 3;
        if (!string.IsNullOrEmpty(want)) { o.ShownState = o.State; effect = want; return true; }
        return wasUp;
    }

    /// <summary>
    /// A watch tower's state effect, with no mask check. Near: ShowStateEffect (vfunc_3 @0x1414034e0, interactobject.cpp:1721-1734,
    /// and vfunc_11's near branch, :1760-1767): the effect of the state (vfunc_13, @0x1413fe7d0) unless the shown state is the
    /// same. Far: vfunc_11's far branch ClearStateEffect (:1769-1772); a state change while far (vfunc_3 with m_bNear 0) does
    /// nothing, and the effect is already cleared then.
    /// </summary>
    static bool WatchTowerEffect(LiveInteractObject o, out string effect)
    {
        effect = "";
        if (!o.Near)
        {
            if (o.ShownState == 3) return false;
            o.ShownState = 3;
            return true;
        }
        if (o.ShownState == o.State) return false;
        bool wasUp = o.ShownState != 3;
        o.ShownState = 3;
        string want = o.State == 0 ? o.Row.EffectNormal : o.State == 1 ? o.Row.EffectReactable : "";
        if (!string.IsNullOrEmpty(want)) { o.ShownState = o.State; effect = want; return true; }
        return wasUp;
    }

    /// <summary>The asset a watch tower applies (FUN_1413ff690: asset A in state 0, else B) while the player is near
    /// (vfunc_3 @0x1414034e0, :1729-1733; vfunc_11's near branch, :1764-1766), else null. Its entity's IsVisible and box are
    /// never set by its state.</summary>
    public static string WatchTowerAsset(LiveInteractObject o) =>
        o != null && o.Kind == InteractObjectMessage.KindWatchTower && o.Near && o.Row != null && o.AssetType != 0
            ? (o.State == 0 ? o.AssetA : o.AssetB) : null;

    /// <summary>The 2D distance a watch tower counts as near under (vfunc_11, interactobject.cpp:1753-1754).</summary>
    public const float WatchTowerNearDistance = 2000f;

    /// <summary>
    /// CInteractWatchTowerObject::vfunc_11 (@0x1414035d0, interactobject.cpp:1746-1773), run for every object each frame by
    /// WorldService::FUN_141bcb830 (worldservice_1.cpp:9666-9670): near is the local player within 2000 of the object's
    /// position, x and y only (GetDistance2D @0x141403f20, float); with no local player it is far. Only a change does
    /// anything: the host redraws the effect and the asset (<see cref="WatchTowerEffect"/>, <see cref="WatchTowerAsset"/>).
    /// EnableHighlight / DisableDynamicUpdate have no draw effect here.
    /// </summary>
    void TickWatchTower(LiveInteractObject o, Vec3 playerPos)
    {
        bool near = false;
        if (host.HasPlayer)
        {
            float dx = (float)playerPos.X - (float)o.Position.X, dy = (float)playerPos.Y - (float)o.Position.Y;
            near = MathF.Sqrt(dx * dx + dy * dy) < WatchTowerNearDistance;
        }
        if (o.Near == near) return;
        o.Near = near;
        host.OnObjectStateChanged(o);
    }

    static bool IsInteractEntity(FieldEntity e) =>
        string.Equals(e.ModelName, "MS2InteractObject", StringComparison.OrdinalIgnoreCase) || e.HasMixin("MS2InteractObject");

    // ---- Recv: CSceneStateGame::OnInteractObject (@0x14172d0f0, scenestategame.cpp:6218-6494) ------------------------

    public void Apply(InteractObjectMessage m)
    {
        if (!Attached) { pending.Add(m); return; }
        switch (m.Mode)
        {
            case InteractObjectMessage.ModeState:
                ApplyState(m.Name, m.State, m.Kind, null);
                break;
            case InteractObjectMessage.ModeStates:
                if (m.Entries != null)
                    for (int i = 0; i < m.Entries.Count; i++)
                    {
                        var e = m.Entries[i];
                        ApplyState(e.Name, e.State, e.Kind, e.Kind == InteractObjectMessage.KindGathering ? e.RespawnTick : null);
                    }
                break;
            case InteractObjectMessage.ModeKindState:
                // WorldService::FUN_141bc6930 (@0x141bc6930, worldservice_1.cpp:4667-4678)
                for (int i = 0; i < list.Count; i++)
                {
                    var o = list[i];
                    if (o.RowId != m.RowId) continue;
                    SetState(o, m.State);
                }
                break;
            case InteractObjectMessage.ModeAllState:
                // WorldService::FUN_141bc6a20 (@0x141bc6a20, worldservice_1.cpp:1226-1235)
                for (int i = 0; i < list.Count; i++) SetState(list[i], m.State);
                break;
            case InteractObjectMessage.ModeMessage:
                ApplyMessage(m);
                break;
            case InteractObjectMessage.ModeAdd:
                ApplyAdd(m);
                break;
            case InteractObjectMessage.ModeRemove:
                ApplyRemove(m);
                break;
            case InteractObjectMessage.ModeResult:
                ApplyResult(m);
                break;
            case InteractObjectMessage.ModeNotice:
                ApplyNotice(m);
                break;
            case InteractObjectMessage.ModeWeapon:
                // SSG_INTERACT_WEAPON (scenestategame.cpp:6482-6490): FindPCById(pcId), then PC::FUN_14165f1f0(weaponId, 1)
                if (m.PcId == host.LocalObjectId && Held != null) Held.Hold(m.WeaponId, host.ItemExists(m.WeaponId));
                else host.StillOpen($"InteractObject mode 15 (weapon {m.WeaponId} for pc {m.PcId}): another pc's held item is not shown");
                break;
        }
    }

    /// <summary>WorldService::FUN_141bc6860 / FUN_141bc6760 (worldservice_1.cpp:4640-4664).</summary>
    void ApplyState(string name, byte state, byte kind, int? respawnTick)
    {
        var obj = ByName(name);
        if (obj == null || obj.Kind != kind) return;
        SetState(obj, state);
        if (respawnTick.HasValue && obj.Kind == InteractObjectMessage.KindGathering)
            obj.RespawnTickMs = respawnTick.Value;
    }

    void SetState(LiveInteractObject obj, byte state)
    {
        obj.State = state;
        host.OnObjectStateChanged(obj);
    }

    /// <summary>SSG_INTERACT_MSG (mode 5, scenestategame.cpp:6223-6235 -> CInteractObject::vfunc_2).</summary>
    void ApplyMessage(InteractObjectMessage m)
    {
        var obj = ByName(m.Name);
        if (obj == null || obj.Kind != m.Kind) return;
        switch (obj.Kind)
        {
            case InteractObjectMessage.KindGathering:
            {
                // CInteractGatheringObject::vfunc_2 (@0x141402490, interactobject.cpp:1450-1478)
                SetState(obj, 0);
                if (obj.Row != null) obj.RespawnTickMs = host.NowMs + obj.Row.ResetTimeMs;
                if (host.HasPlayer && obj.Row != null)
                {
                    // StateInteraction::vfunc_4 sub 0x8b (successAni) when gatherType == 0, else 0x8c (failAni)
                    // (stateinteractdata.cpp:299-318)
                    string anim = m.GatherType == 0 ? obj.Row.SuccessAni : obj.Row.FailAni;
                    if (!string.IsNullOrEmpty(anim)) host.PlayPlayerAnim(anim, loop: false);
                }
                int recipeId = obj.Row?.GatheringRecipeId ?? 0;
                if (recipeId != 0 && m.GatherType == 0)
                {
                    gatherCounts.TryGetValue(recipeId, out int have);
                    gatherCounts[recipeId] = have + m.GatherCount;
                }
                break;
            }
            case InteractObjectMessage.KindUi:
                // CInteractUIObject::vfunc_2 (@0x141400260, interactobject.cpp:1045-1056)
                if (obj.Row != null && !string.IsNullOrEmpty(obj.Row.UiSwf))
                {
                    int id = host.OnOpenUiDialog(obj.Row.UiSwf, obj.Row.UiValue ?? "");
                    if (id != 0) obj.DialogId = id;
                }
                break;
            case InteractObjectMessage.KindTelescope:
                // CInteractTelescopeObject::vfunc_2 (@0x1413ff980, interactobject.cpp:964-1009)
                host.StillOpen($"InteractObject telescope '{obj.Name}': cutscene subscene 0x6200041 not ported");
                break;
        }
    }

    /// <summary>SSG_INTERACT_ADD (mode 9, scenestategame.cpp:6310-6406): only mesh (1) and billboard (8) spawn dynamically.</summary>
    void ApplyAdd(InteractObjectMessage m)
    {
        var row = table?.Get(m.RowId);
        if (row == null) return;
        if (row.Type != InteractObjectMessage.KindMesh && row.Type != InteractObjectMessage.KindBillBoard) return;
        // only these two entity models are made (:6332, :6351); any other leaves no object
        if (m.Model != "MS2InteractMesh" && m.Model != "MS2InteractActor") return;
        if (m.Kind != InteractObjectMessage.KindMesh && m.Kind != InteractObjectMessage.KindBillBoard) return;
        if (string.IsNullOrEmpty(m.Name)) return;
        string key = m.Name.ToLowerInvariant();
        if (byName.TryGetValue(key, out var existing))
        {
            list.Remove(existing);
            byName.Remove(key);
        }
        // the entity's assets (:6332-6367): an MS2InteractMesh gets NifAsset and reactableAssetName only when both urns are
        // sent, each as "urn:gamebryo-scenegraph:" + the name; an MS2InteractActor gets its KfmAsset and the two sequence
        // names only when an animation file is sent. Otherwise the factory's empty values stay.
        byte assetType = m.Model == "MS2InteractMesh" ? (byte)1 : (byte)2;
        string assetA = "", assetB = "";
        if (assetType == 1 && !string.IsNullOrEmpty(m.AssetA) && !string.IsNullOrEmpty(m.AssetB))
        {
            assetA = "urn:gamebryo-scenegraph:" + m.AssetA;
            assetB = "urn:gamebryo-scenegraph:" + m.AssetB;
        }
        else if (assetType == 2 && !string.IsNullOrEmpty(m.Anim))
        {
            assetA = m.AssetA ?? "";
            assetB = m.AssetB ?? "";
        }
        var obj = new LiveInteractObject
        {
            Name = key,
            RowId = m.RowId,
            Kind = m.Kind,
            State = m.State,
            Position = new Vec3(m.Position.X, m.Position.Y, m.Position.Z),
            Rotation = new Vec3(m.Rotation.X, m.Rotation.Y, m.Rotation.Z),
            Scale = m.Scale,
            Model = m.Model,
            Anim = m.Anim,
            AssetType = assetType,
            AssetA = assetA,
            AssetB = assetB,
            BoardOwner = m.BoardOwner,
            BoardText = m.BoardText,
            Spawned = true,
            Row = row,
        };
        byName[key] = obj;
        list.Add(obj);
        host.OnObjectStateChanged(obj);
    }

    /// <summary>SSG_INTERACT_REMOVE (mode 10, scenestategame.cpp:6408-6432 -> WorldService::FUN_141bc5d60).</summary>
    void ApplyRemove(InteractObjectMessage m)
    {
        var obj = ByName(m.Name);
        if (obj == null) return;
        if (!string.IsNullOrEmpty(m.Effect)) host.OnObjectRemoveEffect(obj, m.Effect);
        if (polled == obj) polled = null;
        if (castingTarget == obj) CancelCast();
        byName.Remove(obj.Name);
        list.Remove(obj);
        host.OnObjectRemoved(obj);
    }

    /// <summary>SSG_INTERACT_RESULT (mode 13, scenestategame.cpp:6236-6309).</summary>
    void ApplyResult(InteractObjectMessage m)
    {
        switch (m.Result)
        {
            case 0:
            {
                var obj = ByName(m.Name);
                if (obj == null || obj.Kind != m.Kind) return;
                // CInteractObject::vfunc_12 (@0x1413fe5d0, interactobject.cpp:647-665): success effect
                if (obj.Row != null && !string.IsNullOrEmpty(obj.Row.EffectSuccess))
                    host.OnObjectSuccessEffect(obj, obj.Row.EffectSuccess);
                if (obj.Row != null && obj.Row.Collection != 0 && collectedIds.Add(obj.RowId))
                {
                    if (obj.Kind == InteractObjectMessage.KindTelescope)
                        host.SystemNotice(host.Common(0x897), 3);
                }
                break;
            }
            case 1:
                host.SystemNotice(ClientFormat.Format(host.Common(0x895), (int)m.Result));
                break;
            case 12:
            case 6:
                break;
            case 4:
            {
                var obj = ByName(m.Name);
                if (obj?.Row == null) return;
                host.SystemNotice(ClientFormat.Format(host.Common(0x7c9), host.ItemName(obj.Row.ItemCode)));
                break;
            }
            case 13:
            {
                var obj = ByName(m.Name);
                if (obj?.Row == null) return;
                string mType = host.MasteryTypeName(obj.Row.MasteryType);
                string mGrade = host.MasteryGradeName(obj.Row.MasteryType, obj.Row.MasteryValue);
                host.SystemNotice(ClientFormat.Format(host.Common(0x896), mType, mGrade));
                break;
            }
            case 7: host.SystemNotice(host.Common(0x891)); break;
            case 2: host.SystemNotice(host.Common(0x892)); break;
            case 3: host.SystemNotice(host.Common(0x893)); break;
            case 5: host.SystemNotice(host.Common(0x894)); break;
        }
    }

    /// <summary>SSG_INTERACT_NOTICE (mode 14, scenestategame.cpp:6469-6481).</summary>
    void ApplyNotice(InteractObjectMessage m)
    {
        string fmt = host.InteractNoticeText(m.NameId);
        string msg = ClientFormat.Format(fmt, m.NoticeArg);
        if (m.Channel == host.Channel && m.MapId == host.FieldId) host.SystemNotice(msg, 10);
        else host.ChatNotice(msg);
    }

    // ---- Queries & Gates ---------------------------------------------------------------------------------------------

    /// <summary>FUN_141275260(0x2f7, 1, 1): the notice for a shut-down interact object (interactobject.cpp:403).</summary>
    public const int ShutdownNotice = 0x2f7;

    /// <summary>MyPC::FUN_141527160(0x19) (mypc.cpp:1873): StateInteraction may be entered only from idle (1), the emotion
    /// states (0xb, 0xf), riding (0x21) and 0x3a.</summary>
    public static bool StateAllowsInteraction(int stateId) => stateId is 1 or 0xb or 0xf or 0x21 or 0x3a;

    /// <summary>CInteractObject::vfunc_7 (@0x141404070; WatchTower @0x1414034c0 = 0; Gathering @0x141402940).</summary>
    public bool IsReactable(LiveInteractObject obj)
    {
        if (obj == null || obj.Row == null) return false;
        if (obj.Kind == InteractObjectMessage.KindWatchTower) return false;
        if (obj.State != 1) return false;
        if (obj.Kind == InteractObjectMessage.KindGathering)
        {
            if (obj.RespawnTickMs != 0) return false;
            if (!host.HasPlayer) return false;
            if (obj.Row.GatheringRecipeId != 0 && !host.CanGatherRecipe(obj.Row.GatheringRecipeId)) return false;
        }
        return true;
    }

    /// <summary>CInteractObject::CheckHomePrivilege (@0x1413fe040): STILL-OPEN when check != 0 (false -> sends nothing).</summary>
    bool CheckHomePrivilege(LiveInteractObject obj) => obj.Row != null && obj.Row.HomePrivilegeCheck == 0;

    /// <summary>CInteractObject::CheckArenaTeam (@0x1413fe120): STILL-OPEN when team != 0 (false -> sends nothing).</summary>
    bool CheckArenaTeam(LiveInteractObject obj) => obj.Row != null && obj.Row.CanInteractTeamId == 0;

    /// <summary>CInteractObject::FUN_1413fdfc0 (@0x1413fdfc0): quest mask check on m_maskUse.</summary>
    bool CheckQuestMask(LiveInteractObject obj) => obj.Row != null && QuestMaskTable.Check(obj.Row.MaskUse, questLookup);

    bool CheckItemRequirement(LiveInteractObject obj) =>
        obj.Row != null && (obj.Row.ItemCode == 0 || host.HasRequiredItem(obj.Row.ItemCode, obj.Row.ItemRank, obj.Row.ItemConsume));

    bool HasAnyRequiredEffect(LiveInteractObject obj)
    {
        if (obj.Row == null) return false;
        var req = obj.Row.ConditionEffects;
        if (req.Length == 0) return true;
        for (int i = 0; i < req.Length; i++)
            if (host.HasAdditionalEffect(req[i].Id, req[i].Level)) return true;
        return false;
    }

    bool HasRequiredMastery(LiveInteractObject obj) =>
        obj.Row != null && (obj.Row.MasteryType == 0 || host.MasteryGrade(obj.Row.MasteryType) >= obj.Row.MasteryValue);

    /// <summary>CInteractObject::vfunc_4 (@0x1413fd740, interactobject.cpp:391-419): all gates to send interact packets. A
    /// shut-down object (the game event list, :401) shows common string 0x2f7 and is refused.</summary>
    public bool CanUse(LiveInteractObject obj)
    {
        if (obj == null || obj.Row == null || obj.State != 1 || !host.HasPlayer) return false;
        if (obj.Kind == InteractObjectMessage.KindWatchTower) return false;
        if (host.IsInteractObjectShutdown(host.FieldId, obj.Row.Id))
        {
            host.SystemNotice(host.Common(ShutdownNotice));
            return false;
        }
        return CheckHomePrivilege(obj) && CheckArenaTeam(obj) && HasAnyRequiredEffect(obj)
            && CheckQuestMask(obj) && CheckItemRequirement(obj) && HasRequiredMastery(obj);
    }

    /// <summary>WorldService::FUN_141bc6090 (@0x141bc6090, worldservice_1.cpp:4546-4578): nearest interact object within
    /// its own row's interactionDistance (3D Euclidean, MS2 axes). Zero allocations.</summary>
    public LiveInteractObject FindNearest(Vec3 pos, bool skipDisabled)
    {
        float best = float.MaxValue;
        LiveInteractObject found = null;
        for (int i = 0; i < list.Count; i++)
        {
            var o = list[i];
            if (o.Row == null) continue;
            if (skipDisabled && !IsReactable(o)) continue;
            float range = o.Row.InteractionDistance;
            float dist = Distance(pos, o.Position);
            if (dist > range || dist > best) continue;
            best = dist;
            found = o;
        }
        return found;
    }

    /// <summary>GuideWorld::InteractObject(interactId) (WorldService::FUN_141bc6510 @0x141bc6510, worldservice_1.cpp:4608):
    /// nearest interact object of a row id regardless of range.</summary>
    public (float Distance, int State)? NearestOfRow(Vec3 pos, int rowId)
    {
        float best = float.MaxValue;
        LiveInteractObject found = null;
        for (int i = 0; i < list.Count; i++)
        {
            var o = list[i];
            if (o.RowId != rowId) continue;
            float dist = Distance(pos, o.Position);
            if (dist > best) continue;
            best = dist;
            found = o;
        }
        return found == null ? null : (best, found.State);
    }

    /// <summary>GuideWorld::NearestInteractableUsable (InteractableCondition): nearest reactable object's vfunc_4().</summary>
    public bool? NearestUsable(Vec3 pos) => FindNearest(pos, skipDisabled: true) is { } o ? CanUse(o) : null;

    /// <summary>
    /// The action-key poll's interact part before the portal (Unk_ActionKeyManager::Update @0x140952ef0,
    /// actionkeyobject.cpp:549: the actions sit in a CRBMap, so they are polled by ascending key and the first that takes
    /// the key wins): GatheringAction (key 4, <see cref="PollGathering"/>), then InteractAction (key 0x12, kinds other than
    /// 6 and 8). The portal (0x13) and the billboard action (0x18, <see cref="PollBillBoard"/>) come after. Zero allocations.
    /// </summary>
    public bool Poll(Vec3 playerPos) => PollGatheringAction(playerPos) || PollInteractAction(playerPos);

    /// <summary>GatheringAction (key 4), the first of <see cref="Poll"/>; clears the polled object when it does not take
    /// the key. InteractNpcAction (key 0x11) sits between it and <see cref="PollInteractAction"/>.</summary>
    public bool PollGatheringAction(Vec3 playerPos)
    {
        polled = null;
        polledGathering = false;
        if (!PollGathering(playerPos)) return false;
        polled = gatherPick;
        polledGathering = true;
        return true;
    }

    /// <summary>InteractAction (key 0x12), the second of <see cref="Poll"/>.</summary>
    public bool PollInteractAction(Vec3 playerPos)
    {
        if (!PollAction(playerPos, ref interactPick, billBoard: false)) return false;
        polled = interactPick;
        polledGathering = false;
        return true;
    }

    /// <summary>BillBoardInteractAction (key 0x18): InteractAction::vfunc_1 with a vfunc_13 that takes kind 8 only
    /// (actionkeyobject.cpp:2591). Polled after the portal.</summary>
    public bool PollBillBoard(Vec3 playerPos)
    {
        if (!PollAction(playerPos, ref boardPick, billBoard: true)) return false;
        polled = boardPick;
        polledGathering = false;
        return true;
    }

    /// <summary>
    /// GatheringAction::vfunc_1 (@0x14095d0e0, actionkeyobject.cpp:2467-2500): every kind 6 object is highlighted when
    /// nearer than MasteryObjectInteractionDistance and vfunc_13 takes it (:2529: not restricted, a row, vfunc_7); the
    /// nearest of them is the action's object.
    /// </summary>
    bool PollGathering(Vec3 playerPos)
    {
        gatherInRange.Clear();
        gatherPick = null;
        if (!host.HasPlayer) return false;
        float limit = host.MasteryInteractionDistance;
        bool restricted = host.IsRestricted(3);
        float best = float.MaxValue;
        for (int i = 0; i < list.Count; i++)
        {
            var g = list[i];
            if (g.Kind != InteractObjectMessage.KindGathering) continue;
            float dist = Distance(playerPos, g.Position);
            bool inRange = dist < limit && !restricted && g.Row != null && IsReactable(g);
            g.Highlighted = inRange;
            if (!inRange) continue;
            gatherInRange.Add(g);
            if (dist < best) { best = dist; gatherPick = g; }
        }
        return gatherPick != null;
    }

    /// <summary>
    /// InteractAction::vfunc_1 (@0x140958120, actionkeyobject.cpp:1456-1478) with FindUsableInteractObject (@0x140958e70,
    /// :1480-1498): the nearest reactable object (FUN_141bc6090(pos, 1)) that passes the home, arena and quest checks. The
    /// previous pick loses its highlight when it changes; a pick of the wrong kind (vfunc_13: InteractAction rejects 6 and
    /// 8, the billboard action takes only 8) makes the action not apply.
    /// </summary>
    bool PollAction(Vec3 playerPos, ref LiveInteractObject pick, bool billBoard)
    {
        LiveInteractObject obj = null;
        if (host.HasPlayer && !host.IsRestricted(3))
        {
            obj = FindNearest(playerPos, skipDisabled: true);
            if (obj != null && !(CheckHomePrivilege(obj) && CheckArenaTeam(obj) && CheckQuestMask(obj))) obj = null;
        }
        if (pick != null && pick != obj) pick.Highlighted = false;
        if (obj == null) return false;
        bool accepts = billBoard
            ? obj.Kind == InteractObjectMessage.KindBillBoard
            : obj.Kind != InteractObjectMessage.KindGathering && obj.Kind != InteractObjectMessage.KindBillBoard;
        if (!accepts) return false;
        obj.Highlighted = true;
        pick = obj;
        return true;
    }

    // ---- Use & StateInteraction (0x19) -------------------------------------------------------------------------------

    /// <summary>
    /// Action-key press: InteractAction::vfunc_2 (@0x1409582b0, actionkeyobject.cpp:1532-1602) / GatheringAction::vfunc_2
    /// (:2502). Runs client gates (with client error notices), then either sends mode 0x0c immediately (reactTime == 0) or
    /// enters StateInteraction (0x8a): plays reactAni, sends mode 0x0b, starts CastingBar.
    /// </summary>
    public bool Use(Vec3 playerPos)
    {
        var obj = polled;
        if (obj == null || obj.Row == null || !host.HasPlayer) return false;
        var row = obj.Row;

        if (!CheckItemRequirement(obj))
        {
            host.SystemNotice(ClientFormat.Format(host.Common(0x7c9), host.ItemName(row.ItemCode)));
            return false;
        }
        if (!HasAnyRequiredEffect(obj))
        {
            if (row.ConditionEffects.Length == 1)
                host.SystemNotice(ClientFormat.Format(host.CommonByKey("s_interact_err_additional_effect"),
                    host.AdditionalEffectName(row.ConditionEffects[0].Id, row.ConditionEffects[0].Level)));
            else
                host.SystemNotice(host.CommonByKey("s_interact_err_additional_effect_multi"));
            return false;
        }
        if (row.PartyPortal != 0 && !host.IsPartyLeader)
        {
            host.SystemNotice(host.Common(0x893));
            return false;
        }
        if (!HasRequiredMastery(obj))
        {
            host.SystemNotice(ClientFormat.Format(host.Common(0x896),
                host.MasteryTypeName(row.MasteryType), host.MasteryGradeName(row.MasteryType, row.MasteryValue)));
            return false;
        }

        if (row.ReactTimeMs == 0)
        {
            // instant interact: actionkeyobject.cpp:1591-1600 -> CInteractObject::vfunc_10 (mode 0x0c)
            if (host.PlayerStateId == 0x10) return false;
            if (obj.Kind == InteractObjectMessage.KindGuildPoster)
            {
                host.StillOpen($"InteractObject guild poster '{obj.Name}': upload dialog not ported");
                return false;
            }
            host.FlushPlayerState();
            if (!CanUse(obj)) return false;
            host.Send(new InteractObjectCompleteRequest(obj.Name));
            return true;
        }

        // timed interact (:1574-1590): only from a state StateInteraction can follow
        if (!StateAllowsInteraction(host.PlayerStateId)) return false;
        if (host.PlayerRiding)
        {
            host.StillOpen($"InteractObject '{obj.Name}': RideOffActionInteract dismount-before-interact not ported");
            return false;
        }
        EnterInteraction(obj, playerPos);
        return true;
    }

    /// <summary>
    /// StateInteraction::vfunc_4 sub 0x8a (stateinteractdata.cpp:244-298), entered with the picked object's row id and
    /// name. FUN_1417d25e0 (:518) plays a random reactAni of the row (none: no motion; "Object_React_A" is for a missing
    /// row only, which InteractAction never passes). The cast's object is then the nearest reactable one
    /// (FUN_141bc6090(pos, 1)); none ends the state. A gathering object sends 0x0b for every usable kind 6 object in the
    /// mastery distance (none: the state stays, with no cast); any other kind sends 0x0b only when usable (vfunc_8), and
    /// the casting bar starts either way. A held item is let go first (SendRequestCube, :264-265). STILL-OPEN: CreateReactionEffect.
    /// </summary>
    void EnterInteraction(LiveInteractObject obj, Vec3 playerPos)
    {
        CancelCast();
        var row = obj.Row;
        if (row.ReactAnis.Length > 0) host.PlayPlayerAnim(row.ReactAnis[rng.Next(row.ReactAnis.Length)], loop: false);
        Held?.SendRequestCube(host.Send);     // GetHeldItem, then PC::SendRequestCube (stateinteractdata.cpp:264-265)

        var target = FindNearest(playerPos, skipDisabled: true);
        if (target == null)
        {
            host.StopPlayerInteractAnim();
            return;
        }
        castingTarget = obj;
        castingFinished = false;
        castingActive = true;
        activeGatherTargets.Clear();
        if (target.Kind == InteractObjectMessage.KindGathering)
        {
            // SendInteractObjectMode11 (@0x1417d1ed0, :432-446)
            float limit = host.MasteryInteractionDistance;
            for (int i = 0; i < list.Count; i++)
            {
                var g = list[i];
                if (g.Kind != InteractObjectMessage.KindGathering) continue;
                if (!CanUse(g)) continue;
                if (Distance(playerPos, g.Position) > limit) continue;
                activeGatherTargets.Add(g);
            }
            if (activeGatherTargets.Count == 0) return;
            var names = new string[activeGatherTargets.Count];
            for (int i = 0; i < activeGatherTargets.Count; i++) names[i] = activeGatherTargets[i].Name;
            host.Send(new InteractObjectStartRequest(names));
        }
        else if (CanUse(target))
        {
            // CInteractObject::vfunc_8 (@0x1413fe370)
            host.Send(new InteractObjectStartRequest(target.Name));
        }

        // CInteractObject::vfunc_6 (@0x1413fd9b0): the action name and the row's reactTime; CInteractGatheringObject::vfunc_6
        // (@0x1414027c0, interactobject.cpp:1495-1501): the time through MasteryReduce's Apply, the caption s_action_gathering
        // with MasteryReduce's text after it
        string caption;
        uint castMs = target.Row.ReactTimeMs;
        if (target.Kind == InteractObjectMessage.KindGathering)
        {
            castMs = MasteryReduce.Apply(castMs);
            caption = host.CommonByKey("s_action_gathering") + MasteryReduce.Text(host.CommonByKey);
        }
        else caption = host.InteractActionName(target.RowId);
        castEndMs = 0;
        if (!host.StartCasting(this, caption, (int)castMs))
            castEndMs = host.NowMs + castMs;      // no bar: the cast time still runs

    }

    /// <summary>ICastingEventListener::OnCastingFinished (@0x1417d2210, stateinteractdata.cpp:467).</summary>
    public void OnCastingFinished() => castingFinished = true;

    /// <summary>ICastingEventListener::OnCastingCancelled.</summary>
    public void OnCastingCancelled()
    {
        if (!castingActive) return;
        castingActive = false;
        castingFinished = false;
        castEndMs = 0;
        castingTarget = null;
        activeGatherTargets.Clear();
        host.StopPlayerInteractAnim();
    }

    /// <summary>
    /// Per-frame tick: gathering respawn timers (CInteractGatheringObject::vfunc_11 @0x141402750, interactobject.cpp:1481-1492),
    /// the UI objects' dialogs, the watch towers' near rule (<see cref="TickWatchTower"/>)
    /// and StateInteraction::vfunc_8 (@0x1417d18c0, stateinteractdata.cpp:353-402). Zero allocations when no cast completes.
    /// </summary>
    public void Tick(Vec3 playerPos, bool playerMoved)
    {
        long now = host.NowMs;
        for (int i = 0; i < list.Count; i++)
        {
            var o = list[i];
            if (o.Kind == InteractObjectMessage.KindGathering && o.RespawnTickMs != 0 && now >= o.RespawnTickMs)
            {
                o.RespawnTickMs = 0;
                SetState(o, 1);
            }
            else if (o.DialogId != 0) TickDialog(o, playerPos);
            else if (o.Kind == InteractObjectMessage.KindWatchTower) TickWatchTower(o, playerPos);
        }

        if (!castingActive) return;
        var obj = castingTarget;
        if (obj == null || obj.Row == null) { CancelCast(); return; }

        if (!castingFinished && castEndMs != 0 && now >= castEndMs) castingFinished = true;
        if (castingFinished)
        {
            // StateInteraction::FUN_1417d2230 (@0x1417d2230, stateinteractdata.cpp:470-485)
            CompleteCast(obj);
            return;
        }
        if (playerMoved || Distance(playerPos, obj.Position) > obj.Row.InteractionDistance)
            CancelCast();
    }

    /// <summary>CInteractUIObject::vfunc_11 (@0x1414003c0, interactobject.cpp:1059-1077): a closed dialog lets the object go;
    /// with no local player it is closed; one farther than the row's interactionDistance from the object (3D) is closed
    /// (the id is let go on the next frame, when it shows closed).</summary>
    void TickDialog(LiveInteractObject o, Vec3 playerPos)
    {
        if (host.IsDialogClosed(o.DialogId)) { o.DialogId = 0; return; }
        if (!host.HasPlayer) { host.CloseDialog(o.DialogId); return; }
        if (o.Row != null && Distance(playerPos, o.Position) > o.Row.InteractionDistance) host.CloseDialog(o.DialogId);
    }

    void CompleteCast(LiveInteractObject obj)
    {
        castingActive = false;
        castingFinished = false;
        castEndMs = 0;
        castingTarget = null;
        host.StopPlayerInteractAnim();

        if (obj.Kind == InteractObjectMessage.KindGathering)
        {
            if (activeGatherTargets.Count > 0)
            {
                // SendInteractObjectMode12 (@0x1417d2070, stateinteractdata.cpp:449-464)
                int usable = 0;
                for (int i = 0; i < activeGatherTargets.Count; i++)
                    if (CanUse(activeGatherTargets[i])) usable++;
                var names = new string[usable];
                int k = 0;
                for (int i = 0; i < activeGatherTargets.Count; i++)
                    if (CanUse(activeGatherTargets[i])) names[k++] = activeGatherTargets[i].Name;
                host.Send(new InteractObjectCompleteRequest(names));
                activeGatherTargets.Clear();
            }
        }
        else if (obj.Kind == InteractObjectMessage.KindGuildPoster)
        {
            host.StillOpen($"InteractObject guild poster '{obj.Name}': upload dialog not ported");
        }
        else if (CanUse(obj))
        {
            host.Send(new InteractObjectCompleteRequest(obj.Name));
        }
        host.OnGuideTrigger(9, obj.RowId);
    }

    public void CancelCast()
    {
        if (!castingActive) return;
        castingActive = false;
        castingFinished = false;
        castEndMs = 0;
        castingTarget = null;
        activeGatherTargets.Clear();
        host.CancelCasting();
        host.StopPlayerInteractAnim();
    }

    static float Distance(Vec3 a, Vec3 b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
