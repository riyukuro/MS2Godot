using System;
using System.Numerics;
using Ms2.Net.Packets;
using Ms2.Ui.Items;

namespace Ms2.Ui;

/// <summary>
/// The local pc's cube mode and cube craft: PC::m_nCubeModeFFC, the craft object m_pCubeCraft (Unk_PCCubeCraft) and the
/// craft held back until the pc's model is in the scene (m_n1000, m_spCube1008, m_n1018).
/// <para>The mode is set by PC::vfunc_135 (@0x141659450, pc.cpp:3068), from SetCraftMode (CSceneStateGame::OnSetCraftMode
/// @0x141723410, scenestategame.cpp:7827) and from the pc's AddUser (scenestategame.cpp:11189-11192). A nonzero mode puts
/// the action key on its cube-mode actions (Unk_ActionKeyManager::GetActiveActionMap @0x140953770,
/// actionkeyobject.cpp:707-717).</para>
/// STILL-OPEN: what the mode change does besides the craft: the stance change (vfunc_59 5 or 1, pc.cpp:3072-3077), the
/// weapon effects (FUN_141659000, pc.cpp:2491), the housing service's state (FUN_1410cc160, pc.cpp:3083-3089), MyPC's
/// build HUD dialog 0x4e85 with its sounds and FUN_140ef4330 (mypc.cpp:1364-1397), and the target refresh after
/// SetCraftMode (scenestategame.cpp:7846-7851). The craft's start (<see cref="Start"/>) keeps its cell and its guide's
/// yaw, not the scale pulse, the effect items or the god mode info. Other pcs' cube modes are not kept.
/// </summary>
public sealed class CubeCraftState
{
    /// <summary>Unk_PCCubeCraft (ctor @0x1410a8550, cubecraft_2.cpp:282): the craft mode and the cube item.</summary>
    public sealed class Craft
    {
        /// <summary>m_nMode (GetMode @0x1410a8a90).</summary>
        public int Mode { get; }
        /// <summary>m_spItem: the CubeItemInfo the craft was made with.</summary>
        public CubeItemInfo Item { get; }
        /// <summary>FUN_1410a8ab0 (cubecraft_2.cpp:332): mode 2, the craft of a lifted cube.</summary>
        public bool HoldsLift => Mode == 2;
        /// <summary>m_ptCell: the corner of the cell the craft points at (GetCellPosition).</summary>
        public Vector3 Cell { get; internal set; }
        /// <summary>m_fLift (ctor 0, cubecraft_2.cpp:291; 0 again at the start, :351): the height added to the cell in front.
        /// STILL-OPEN: AdjustLift (@0x1410aa060, :590-596, +-150) has no caller ported, so it stays 0.</summary>
        public float Lift { get; internal set; }
        /// <summary>m_spGuide is set: the guide cube CreateGuide (@0x1410a92c0, :421-464) loads. Here it is the guide's state
        /// only (no model is loaded).</summary>
        public bool HasGuide { get; internal set; }
        /// <summary>The z of the rotation FUN_1410aa6f0 (:921-927) gives the guide, in degrees: the owner's target yaw
        /// snapped by <see cref="GuideYawOf"/>, plus m_fYaw (always 0: only the ctor and ReleaseGuide write it, :296, :537).</summary>
        public float GuideYaw { get; internal set; }
        /// <summary>m_bVisible (true in the ctor, cubecraft_2.cpp:286): whether the guide is shown (CreateGuide hands it to
        /// the guide, :461).</summary>
        public bool GuideVisible { get; private set; } = true;
        public Craft(int mode, CubeItemInfo item) { Mode = mode; Item = item; }

        /// <summary>SetGuideVisible (@0x1410aa380, cubecraft_2.cpp:658-665): nothing in craft mode 1, else sets
        /// <see cref="GuideVisible"/>. STILL-OPEN: the guide model's own hide (m_spGuide-&gt;vfunc_6), as no guide model
        /// is loaded.</summary>
        public void SetGuideVisible(bool visible)
        {
            if (Mode == 1) return;
            GuideVisible = visible;
        }
    }

    /// <summary>The craft's owner as it reads the local pc. Null while there is no pc to read.</summary>
    public ICubeCraftOwner Owner { get; set; }

    /// <summary>m_nCubeModeFFC (GetCubeMode, pc.cpp:1842).</summary>
    public int CubeMode { get; private set; }
    /// <summary>IsInCubeMode (pc.cpp:1845).</summary>
    public bool InCubeMode => CubeMode != 0;
    /// <summary>MyPC::SendCraftModeOff's gate (@0x141530200, mypc.cpp:4501): GetActiveCubeCraft (@0x141530290: not dead, in cube mode, the craft set)
    /// and the craft is not the lift's mode 2 (FUN_1410aa1f0).</summary>
    public bool CanSendCraftModeOff(bool dead) => !dead && InCubeMode && Current is { HoldsLift: false };

    /// <summary>m_pCubeCraft (GetCubeCraft, pc.cpp:1867).</summary>
    public Craft Current { get; private set; }
    /// <summary>The craft waiting for the model (FUN_141661880 with 1, pc.cpp:2627): m_n1000 set, m_n1018 and
    /// m_spCube1008.</summary>
    public Craft Pending { get; private set; }
    /// <summary>The pc's model is in the scene (FUN_141b6efc0 on the pc's entity gives a root).</summary>
    public bool ModelLoaded { get; private set; }

    /// <summary>A new pc (PC ctor, pc.cpp:640: mode 0, no craft, nothing pending, no model yet; MyPC ctor, mypc.cpp:879:
    /// m_b15d4 0).</summary>
    public void NewPc()
    {
        CubeMode = 0;
        ConstructionUnlocked = false;
        Current = null;
        Pending = null;
        ModelLoaded = false;
    }

    /// <summary>MyPC::vfunc_135 (@0x1415238d0, mypc.cpp:1364-1397) around PC::vfunc_135 (@0x141659450, pc.cpp:3068):
    /// PC's part does nothing on a dead pc, else sets the mode, then FUN_141659250 with the craft mode and the item. MyPC's
    /// part, when IsInCubeMode changed and the craft mode is not 2: entering sets <see cref="ConstructionUnlocked"/> from
    /// <see cref="ConstructionUnlockOnEnter"/>, leaving clears it.
    /// <para>Not ported (visuals): the palette dialog switch FUN_140ef4330, dialog 0x4e85 and the enter / leave system
    /// sounds.</para></summary>
    public void Set(int cubeMode, int craftMode, CubeItemInfo item, bool dead)
    {
        bool wasIn = InCubeMode;
        if (!dead)
        {
            CubeMode = cubeMode;
            SetCraft(craftMode, item);
        }
        if (wasIn != InCubeMode && craftMode != 2)
            ConstructionUnlocked = InCubeMode ? ConstructionUnlockOnEnter?.Invoke() : false;
    }

    /// <summary>The value entering the cube mode gives <see cref="ConstructionUnlocked"/>; null (or no function) = unknown.
    /// See <see cref="ConstructionUnlock"/>.</summary>
    public Func<bool?> ConstructionUnlockOnEnter;

    /// <summary>
    /// MyPC::vfunc_135's test on entering the cube mode (mypc.cpp:1374-1384): m_b15d4 is 1 when the CubeMapInformationService
    /// is there and (a) its FUN_1410d55d0(field id, block) is true, or (b) FUN_1408a07b0(field id, current channel, own
    /// account) (<see cref="ConstructionEvents.IsMassiveConstruction"/>, ugcmapdata_1.cpp:158-161), or (c) the own account
    /// is on the ignored construction lock list (<see cref="ConstructionEvents.IsIgnoredConstructionLock"/>); else 0.
    /// <paramref name="adminOnlyMap"/> is (a), null while unknown: then only a true (b) or (c) gives an answer.
    /// <para>STILL-OPEN: (a), FUN_1410d55d0 with FUN_1408a18d0's block: the UGCMap group's adminOnly
    /// (FUN_1408a06a0, ugcmapdata_1.cpp:150-156) for the field and the block the entity search near the pc finds
    /// (ugcmapdata_1.cpp:335-351), or the indoor house's ids; no UGCMap table is read.</para>
    /// </summary>
    public static bool? ConstructionUnlock(bool? adminOnlyMap, bool massiveConstruction, bool ignoredLock)
    {
        if (adminOnlyMap == true || massiveConstruction || ignoredLock) return true;
        return adminOnlyMap == false ? false : null;
    }

    /// <summary>
    /// PC::FUN_141659250 (@0x141659250, pc.cpp:3038): the old craft always goes. In cube mode a new craft is made at once
    /// when the model is loaded, else it waits for it (FUN_141661880). Leaving the cube mode does not drop a craft that is
    /// waiting (the else branch, pc.cpp:3056-3064), so PC::vfunc_12 still makes it.
    /// </summary>
    void SetCraft(int craftMode, CubeItemInfo item)
    {
        Current = null;
        if (!InCubeMode) return;
        if (!ModelLoaded) Pending = new Craft(craftMode, item);
        else Start(Current = new Craft(craftMode, item));
    }

    /// <summary>
    /// Unk_PCCubeCraft::FUN_1410a8af0(true) (@0x1410a8af0, cubecraft_2.cpp:340-392), as far as the cell and the guide go:
    /// for the local owner the lift goes to 0 and, outside cube mode 1, the cell is the one in front of the pc
    /// (GetCellInFrontOfActor with the lift and 1, then GetCellPosition). RebuildGuides (:398) then makes the guide through
    /// CreateGuide (:421-464): only for the local owner and craft modes 0, 1 and 2, with the yaw FUN_1410aa6f0 gives it
    /// (:463). The second FUN_1410aa6f0 when a cube is in the cell (:375-381) gives the same yaw.
    /// STILL-OPEN: cube mode 1 takes the cell from the pc's construction guide (FUN_1410b7ba0 with -200, :355-359), which
    /// is not ported, so its cell stays unset; CreateGuide's m_spItem-&gt;Clear() in craft mode 1 (:439); the build
    /// validator FUN_1410ae380 (:398, CUGCBuildValidatorCli) that picks the guide asset in craft mode 0.
    /// </summary>
    void Start(Craft c)
    {
        var o = Owner;
        bool local = o != null && o.IsLocal;
        if (local)
        {
            c.Lift = 0f;
            if (CubeMode != 1) c.Cell = CubeCell.Position(CubeCell.InFrontOf(o.Point, o.RotZ, c.Lift, 1));
        }
        c.HasGuide = local && c.Mode is 0 or 1 or 2;
        if (c.HasGuide) c.GuideYaw = GuideYawOf(o.TargetYaw);
    }

    /// <summary>FUN_1410aa6f0 (:922-925): the target yaw (PCBase::GetTargetYaw, m_f934) snapped up to 90 degrees,
    /// (float)(int)((z + 89) / 90) * 90 with no sign branch; then m_fYaw (0) added. The client keeps m_f934 in [0, 360)
    /// (TurnTowardTargetYaw runs NormalizeDegrees on it, pcbase.cpp:1395; FUN_14166dad0 adds 360 to a negative angle,
    /// :1492), so the owner's yaw, which may be negative, goes through <see cref="NormalizeDegrees"/> first.</summary>
    public static float GuideYawOf(float targetYaw) => (float)(int)((NormalizeDegrees(targetYaw) + 89f) / 90f) * 90f + 0f;

    /// <summary>
    /// Unk_PCCubeCraft::Update (@0x1410ac9b0, cubecraft_2.cpp:1530-1590), from MyPC::vfunc_53 while in cube mode
    /// (mypc.cpp:2969-2970): nothing without the guide, for another owner, or in state 0x10 (it hides the guides). Else the
    /// cell in front of the pc, for a held lift moved to the first free cell of its column (<see cref="FreeCell"/>); when
    /// that cell is not the craft's, the craft takes it and FUN_1410aa6f0 sets the guide's yaw again (:1579). No
    /// allocation.
    /// STILL-OPEN: cube mode 1's construction-guide cell (:1557-1561); the build validator's check and the CreateGuide it
    /// triggers (:1567-1573) in craft mode 0 (mode 2's check is always 1); the slide, the sound and the rotate tween
    /// (visual).
    /// </summary>
    public void Update()
    {
        if (!InCubeMode) return;
        var c = Current;
        var o = Owner;
        if (c == null || !c.HasGuide || o == null || !o.IsLocal) return;
        if (o.StateId == 0x10) return;
        if (CubeMode == 1) return;
        var front = CubeCell.InFrontOf(o.Point, o.RotZ, c.Lift, 1);
        if (c.HoldsLift) front = FreeCell(front, o.Cubes);
        var down = CubeCell.Position(front);
        if (down == c.Cell) return;
        c.Cell = down;
        c.GuideYaw = GuideYawOf(o.TargetYaw);
    }

    /// <summary>FUN_1410aeec0 (@0x1410aeec0, cubecraft_2.cpp:1456-1480): the cell of the point one down, then the cell,
    /// then one up; the first with no cube (GetCubeAtCell) gives the point, each axis the cell times 150 (FUN_1408a15b0,
    /// ugcmapdata_1.cpp:293). With all three taken the point stays.</summary>
    public static Vector3 FreeCell(Vector3 front, ICubeCells cubes)
    {
        var (x, y, z) = CubeCell.Of(front);
        for (int dz = -1; dz <= 1; dz++)
            if (cubes == null || !cubes.HasCubeAtKey(CubeCell.Key(x, y, z + dz)))
                return new Vector3(x * CubeCell.Size, y * CubeCell.Size, (z + dz) * CubeCell.Size);
        return front;
    }

    /// <summary>MyPC::GetActiveCubeCraft (@0x141530290, mypc.cpp:4516): no craft for a dead pc or outside the cube mode;
    /// else the craft.</summary>
    public Craft Active(bool dead) => dead || !InCubeMode ? null : Current;

    /// <summary>Where Unk_PCCubeCraft::ConfirmPlacement stopped.</summary>
    public enum PlacementStop
    {
        /// <summary>No active craft (PutDownLiftableAction::vfunc_2 @0x14095ad50 does not call it).</summary>
        NoCraft,
        /// <summary>Cube mode 1 (PC::IsCubeMode1, pc.cpp:1848) without the pc's construction guide
        /// (GetConstructionGuide, pc.cpp:2102).</summary>
        NoConstructionGuide,
        /// <summary>Craft mode 1: the housing service's build notice <see cref="ModeOneNotice"/>.</summary>
        BuildNotice,
        /// <summary>The craft has no guide cube (m_spGuide).</summary>
        NoGuide,
        /// <summary>FUN_14053f870 (the item data table) has no row for the craft's item.</summary>
        NoItemData,
        /// <summary>A craft mode other than 2 while the pc's construction unlock (MyPC m_b15d4, an input of the lock check
        /// <see cref="CubeLock"/>) is unknown: <see cref="ConstructionUnlocked"/> null or no <see cref="Locks"/>.
        /// STILL-OPEN: m_b15d4's setter (mypc.cpp:1365-1395) is not ported, so nothing is sent.</summary>
        InventoryCheckOpen,
        /// <summary>A craft mode other than 2 and MyPC::FUN_1415302e0 says the item is locked (cubecraft_2.cpp:955-959):
        /// build notice <see cref="CubeLock.LockedNotice"/>.</summary>
        Locked,
        /// <summary>The item has a cube texture mesh and the craft's CubeItemInfo has no uploaded look: build notice
        /// <see cref="TextureNotice"/> (:961-968).</summary>
        NoTextureLook,
        /// <summary>Craft mode 2 and an attachable item (install attachable, :971-974): FUN_1410aded0. STILL-OPEN: not
        /// ported, nothing is sent.</summary>
        AttachableOpen,
        /// <summary>A cube is in the cell but the cube map does not know its item (<see cref="ICubeCells.TryCubeItemAtKey"/>
        /// false), so the 0xf / same-item branch (:1007-1049) is not decided and nothing is sent.</summary>
        CubeInCellOpen,
        /// <summary>The cell's cube holds the same item (Cube::GetItemId and CubeItemInfo::IsSameItem, :1007) and the pc's
        /// craft is in mode 2 (PC::FUN_141659690, pc.cpp:1854): common notice <see cref="SameItemHeldText"/>
        /// (FUN_141275260(0x6c9, 1, 1)) then the NoBuild sound (PlaySystemSound(DAT_143acf6b8), :1039-1043).</summary>
        SameItemHeld,
        /// <summary>The cell's cube holds the same item outside craft mode 2: build notice <see cref="SameItemNotice"/>
        /// (:1044-1047).</summary>
        SameItem,
        /// <summary>The owner has no cube map to look the cell up in (ours: the client always has its service).</summary>
        NoCubeMap,
        /// <summary>RequestCube 10, or 0xf for a cell holding another item (<see cref="CubePlaceRequest"/>), is to be
        /// sent.</summary>
        Send,
    }

    /// <summary>MyPC m_b15d4, the pc's construction unlock the lock check reads; null while unknown (set by
    /// <see cref="Set"/> and <see cref="NewPc"/>).</summary>
    public bool? ConstructionUnlocked;
    /// <summary>MyPC::FUN_1415302e0 with its tables and housing state.</summary>
    public CubeLock Locks;

    /// <summary>The build notice ConfirmPlacement gives in craft mode 1 (ShowBuildNotice(0x39), cubecraft_2.cpp:945).</summary>
    public const int ModeOneNotice = 0x39;
    /// <summary>The build notice for a texture cube without its look (ShowBuildNotice(0x62), cubecraft_2.cpp:966).</summary>
    public const int TextureNotice = 0x62;
    /// <summary>The build notice for the same item in the cell outside craft mode 2 (ShowBuildNotice(0x35),
    /// cubecraft_2.cpp:1046).</summary>
    public const int SameItemNotice = 0x35;
    /// <summary>The common string of the same-item notice in craft mode 2 (FUN_141275260(0x6c9, 1, 1),
    /// cubecraft_2.cpp:1041).</summary>
    public const int SameItemHeldText = 0x6c9;
    /// <summary>The system sound of a placed cube (PlaySystemSound(DAT_143ae79b8), scenestategame.cpp:1222).</summary>
    public const string PlaceSound = "ugc_setblock";
    /// <summary>The system sound of a cube the remove answer takes away (PlaySystemSound(DAT_143ae79c8), scenestategame.cpp:1219).</summary>
    public const string RemoveSound = "ugc_removeblock";

    /// <summary>What a place answer (ResponseCube 0xa / 0xf) does for the local pc (<see cref="OnPlaceAnswer"/>).</summary>
    public enum PlaceAnswer
    {
        /// <summary>Not the local pc's answer: nothing.</summary>
        None,
        /// <summary>Error 0: the guide hidden and <see cref="PlaceSound"/> played.</summary>
        Placed,
        /// <summary>An error while the pc's craft holds a lift: common notice <see cref="SameItemHeldText"/>, then the
        /// NoBuild sound.</summary>
        SameItemHeld,
        /// <summary>An error otherwise: ShowBuildNotice with the answer's error.</summary>
        BuildNotice,
        /// <summary>A remove answer's error 0: the guide shown again and <see cref="RemoveSound"/> played.</summary>
        Removed,
    }

    /// <summary>
    /// CSceneStateGame::OnResponseCube 0xa / 0xf, the local pc's part (scenestategame.cpp:9984-9992, :10012-10030,
    /// :10107-10115, :10122-10140). Only when there is a pc and the answer's character is its object id (0xa with error 0
    /// also needs a character other than 0). Error 0: the active craft (MyPC::GetActiveCubeCraft, mypc.cpp:4516-4523: none
    /// while dead or out of the cube mode) hides its guide (<see cref="Craft.SetGuideVisible"/>), then the place sound.
    /// An error: when the pc's craft holds a lift (PC::FUN_141659690, pc.cpp:1854) the 0x6c9 notice and the NoBuild sound,
    /// else the build notice of the error. <paramref name="selfObject"/> is -1 without a pc.
    /// <para>Not ported (visual only): 0xa's error starts the active craft's 200 ms scale pulse (FUN_1410aa590,
    /// cubecraft_2.cpp:701-704; :10021) before the notice.</para>
    /// </summary>
    public PlaceAnswer OnPlaceAnswer(ResponseCubePlace m, int selfObject, bool dead)
    {
        if (m == null || selfObject == -1 || m.CharacterId != selfObject) return PlaceAnswer.None;
        if (m.Error == 0)
        {
            if (m.Mode == ResponseCubePlace.ModePlace && m.CharacterId == 0) return PlaceAnswer.None;
            Active(dead)?.SetGuideVisible(false);
            return PlaceAnswer.Placed;
        }
        return Current is { HoldsLift: true } ? PlaceAnswer.SameItemHeld : PlaceAnswer.BuildNotice;
    }

    /// <summary>
    /// CSceneStateGame::OnResponseCube 0xc, the local pc's part (scenestategame.cpp:10037-10069), before the cube leaves the
    /// map (<see cref="CubeCellMap.Apply(ResponseCubeRemove)"/>). Only when there is a pc and the answer's character is its object
    /// id. Error 0: the active craft (none while dead or out of the cube mode) shows its guide again
    /// (<see cref="Craft.SetGuideVisible"/>), then <see cref="RemoveSound"/>. An error: as a place answer's, the 0x6c9 notice
    /// and the NoBuild sound when the pc's craft holds a lift, else the build notice of the error.
    /// </summary>
    public PlaceAnswer OnRemoveAnswer(ResponseCubeRemove m, int selfObject, bool dead)
    {
        if (m == null || selfObject == -1 || m.CharacterId != selfObject) return PlaceAnswer.None;
        if (m.Error == 0)
        {
            Active(dead)?.SetGuideVisible(true);
            return PlaceAnswer.Removed;
        }
        return Current is { HoldsLift: true } ? PlaceAnswer.SameItemHeld : PlaceAnswer.BuildNotice;
    }

    /// <summary>The build notice (<see cref="BuildNotice.Show"/>) ConfirmPlacement gives at a stop, 0 for none.</summary>
    public static int NoticeOf(PlacementStop stop) => stop switch
    {
        PlacementStop.BuildNotice => ModeOneNotice,
        PlacementStop.NoTextureLook => TextureNotice,
        PlacementStop.Locked => CubeLock.LockedNotice,
        PlacementStop.SameItem => SameItemNotice,
        _ => 0,
    };

    /// <summary>
    /// PutDownLiftableAction::vfunc_2 (@0x14095ad50, actionkeyobject.cpp) through GetActiveCubeCraft, then
    /// Unk_PCCubeCraft::ConfirmPlacement (@0x1410aad50, cubecraft_2.cpp:936-1063), in its order: in cube mode 1 it returns
    /// without the pc's construction guide; in craft mode 1 it shows build notice 0x39; it returns without its guide cube
    /// or the item's data; outside craft mode 2 the lock check comes next (build notice 0x58; it needs the pc's construction unlock, STILL-OPEN until
    /// its setter is ported); a texture-mesh item needs
    /// the craft's uploaded look (notice 0x62); craft mode 2 with an attachable item takes the attach path (STILL-OPEN).
    /// With the cell free it gives RequestCube 10 with the cell's key, a copy of the craft's item and
    /// <see cref="PlacementYaw"/> of the guide's yaw in <paramref name="send"/>. A cube in the cell holding another item
    /// (another item id, or not CubeItemInfo::IsSameItem) gives RequestCube 0xf with the same key, the copy and
    /// <see cref="SnappedYaw"/> (this branch does not normalize, :1021-1030); the same item gives <see cref="PlacementStop.SameItemHeld"/>
    /// in craft mode 2, else build notice 0x35. Every other stop leaves <paramref name="send"/> null and sends nothing.
    /// <para>Cube mode 1 never gets past the first check: its construction guide (the pc's guide slot 0) is not ported.
    /// That guide's own yaw plus the item's staticAngle (:981-985) is the other source of the rotation.</para>
    /// The caller shows a notice stop's build notice (<see cref="NoticeOf"/>, ShowBuildNotice).
    /// </summary>
    public PlacementStop ConfirmPlacement(bool dead, Func<int, ItemData> itemData, out CubePlaceRequest send)
    {
        send = null;
        var craft = Active(dead);
        if (craft == null) return PlacementStop.NoCraft;
        const bool hasConstructionGuide = false;
        if (CubeMode == 1 && !hasConstructionGuide) return PlacementStop.NoConstructionGuide;
        if (craft.Mode == 1) return PlacementStop.BuildNotice;
        if (!craft.HasGuide) return PlacementStop.NoGuide;
        var item = craft.Item;
        var data = item == null ? null : itemData?.Invoke(item.ItemId);
        if (data == null) return PlacementStop.NoItemData;
        if (craft.Mode != 2)
        {
            if (ConstructionUnlocked is not { } unlocked || Locks == null) return PlacementStop.InventoryCheckOpen;
            if (Locks.IsLocked(item.ItemId, hasItemData: true, unlocked)) return PlacementStop.Locked;
        }
        if (data.HasCubeTextureMesh && !item.HasUploadedLook) return PlacementStop.NoTextureLook;
        if (craft.Mode == 2 && data.InstallAttachable != 0) return PlacementStop.AttachableOpen;
        var cubes = Owner?.Cubes;
        if (cubes == null) return PlacementStop.NoCubeMap;
        int key = CubeCell.Key(craft.Cell);
        if (cubes.HasCubeAtKey(key))
        {
            if (!cubes.TryCubeItemAtKey(key, out var inCell) || inCell == null) return PlacementStop.CubeInCellOpen;
            if (inCell.ItemId == item.ItemId && inCell.IsSameItem(item))
                return craft.Mode == 2 ? PlacementStop.SameItemHeld : PlacementStop.SameItem;
            send = new CubePlaceRequest(key, CopyOf(item), SnappedYaw(craft.GuideYaw), CubePlaceRequest.ReplaceMode);
            return PlacementStop.Send;
        }
        send = new CubePlaceRequest(key, CopyOf(item), PlacementYaw(craft.GuideYaw));
        return PlacementStop.Send;
    }

    /// <summary>CubeItemInfo's copy ctor (kInfo(*m_spItem), :996 and :1025).</summary>
    static CubeItemInfo CopyOf(CubeItemInfo item) =>
        new() { ItemId = item.ItemId, InstanceId = item.InstanceId, Count = item.Count, Look = item.Look };

    /// <summary>
    /// The yaw ConfirmPlacement sends outside cube mode 1 (cubecraft_2.cpp:987-997): the guide's rotation (set by
    /// EulerToMatrix @0x140b86230, statecoupleemotion.cpp:1033, from the owner's angles with z = the guide yaw) read back
    /// with ToEulerAnglesXYZ (z = atan2(m[0][1], m[0][0])) times -57.295776; then snapped to 90 degrees away from 0 by its
    /// sign (the compare in double) and NormalizeDegrees. The owner's x and y angles are taken as 0 (the pc only turns
    /// about z), so the matrix is MakeZRotation(yaw * -0.017453292) alone; efd's atan2 is taken as the exact one.
    /// </summary>
    public static float PlacementYaw(float guideYaw) => NormalizeDegrees(SnappedYaw(guideYaw));

    /// <summary>The guide's yaw read back and snapped to 90 degrees away from 0 (<see cref="PlacementYaw"/> without
    /// NormalizeDegrees): what RequestCube 0xf sends (cubecraft_2.cpp:1017-1024).</summary>
    public static float SnappedYaw(float guideYaw)
    {
        float a = guideYaw * -0.017453292f;
        float z = MathF.Atan2(MathF.Sin(a), MathF.Cos(a)) * -57.295776f;
        if ((double)z < 0.0) z = (float)(int)((z - 89f) / 90f) * 90f;
        else z = (float)(int)((z + 89f) / 90f) * 90f;
        return z;
    }

    /// <summary>NormalizeDegrees (@0x140291b70, commonfiles/commonutil.cpp:1518-1525): truncated to an int, % 360, a
    /// negative one plus 360.</summary>
    public static float NormalizeDegrees(float d)
    {
        int n = (int)d % 360;
        if (n < 0) n += 360;
        return n;
    }

    /// <summary>PC::vfunc_12 (@0x1416515a0, pc.cpp:844-852): the model is in the scene; a waiting craft is made and the
    /// wait cleared, then it starts (FUN_1410a8af0(true), pc.cpp:848-850).</summary>
    public void ModelAttached()
    {
        ModelLoaded = true;
        if (Pending == null) return;
        Current = Pending;
        Pending = null;
        Start(Current);
    }
}

/// <summary>The local pc as Unk_PCCubeCraft reads it.</summary>
public interface ICubeCraftOwner
{
    /// <summary>WorldService::GetLocalPlayer() is the craft's owner.</summary>
    bool IsLocal { get; }
    /// <summary>PCBase::FUN_1416767d0: the pc's point, the ridee's when riding (MS2 axes).</summary>
    Vector3 Point { get; }
    /// <summary>GetRideeAwareRotation().z in degrees.</summary>
    float RotZ { get; }
    /// <summary>PCBase::GetTargetYaw (m_f934, pcbase.cpp:1469) in degrees.</summary>
    float TargetYaw { get; }
    /// <summary>GetCurStateId.</summary>
    int StateId { get; }
    /// <summary>The cube map's cubes.</summary>
    ICubeCells Cubes { get; }
}
