using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;
using Ms2Godot.World;

namespace Ms2Godot.Net;

/// <summary>
/// The live field's interact objects (WorldService m_678, OnInteractObject 0x63, InteractAction / GatheringAction /
/// StateInteraction, <see cref="LiveInteractObjects"/>). Created per RequestFieldEnter and attached once the field's
/// xblock entities are loaded, mirroring <see cref="Portals"/>.
/// </summary>
public partial class LiveSession
{
    public LiveInteractObjects InteractObjects { get; private set; }
    /// <summary>The field's interact draws (World.FieldInteractDraws.Apply): each object state change, set while the field is up.</summary>
    public Action<LiveInteractObject> InteractStateDrawn;
    /// <summary>The field's interact draws (World.FieldInteractDraws.Remove): an object removed (InteractObject mode 10).</summary>
    public Action<LiveInteractObject> InteractRemoved;
    /// <summary>The field's interact draws (World.FieldInteractDraws.Effect): a result's success effect (true, kept) or a remove's
    /// one-shot (false).</summary>
    public Action<LiveInteractObject, string, bool> InteractEffect;
    /// <summary>The HUD's dialogs by swf name (a UI interact object's message, CInteractUIObject::vfunc_2): open the one the
    /// name finds with the value, its ui class id or 0; whether one is closed; close one. Unset: nothing opens.</summary>
    public Func<string, string, int> InteractUiDialog;
    public Func<int, bool> InteractUiDialogClosed;
    public Action<int> InteractUiDialogClose;
    /// <summary>The field npcs InteractNpcAction (key 0x11) reads and its StateInteractionNpc cast; RemoteLayer fills it.</summary>
    public LiveInteractNpc InteractNpcs { get; private set; }
    bool npcPolled;
    /// <summary>The HUD's casting bar (UICastingBar 0x4e72); set by HudScene while the field HUD is up.</summary>
    public CastingBarController CastingBar;
    /// <summary>Cross-channel/field interact notices (FUN_1412751b0); the HUD sets it when chat is up.</summary>
    public Action<string> ChatNoticeSink;
    /// <summary>GetItemRank: an item's own rank from the item table; the HUD sets it with its tables.</summary>
    public Func<int, int> ItemRank;
    public int InteractSends { get; private set; }
    /// <summary>Whether an item id is in the item data (CItem::FUN_140523c40); the HUD sets it with its tables. Unset:
    /// every id is taken as an item.</summary>
    public Func<int, bool> ItemExists;
    /// <summary>The local pc's held item (PC::m_spItem1040): set by InteractObject mode 15, let go by SendRequestCube and
    /// ResponseCube 0x12. A new field makes a new pc, so it starts empty.</summary>
    public readonly HeldItem HeldItem = new();
    /// <summary>The housing globals ResponseCube 0x27 / 0x35 / 0x37 set; the owner part is cleared at each ServerEnter
    /// (FUN_14092f420, scenestategame.cpp:2070).</summary>
    public readonly HousingState Housing = new();

    /// <summary>MyPC's UserUGCMapInfo (GetUGCMapInfo / SetUGCMapInfo / HasNoHome), filled by ResponseCube mode 0x14.</summary>
    public readonly Ms2.Ui.OwnHome Home = new();

    Ms2.Ui.Hud.CubeBill billState;
    /// <summary>UIUGCCubeBillDialog's statics (s_pending, s_cost) with the packets that ask for and answer a bill (ResponseCube 0x24 / 0x43).</summary>
    public Ms2.Ui.Hud.CubeBill Bill => billState ??= new Ms2.Ui.Hud.CubeBill { Send = Send };
    /// <summary>CubeMapInformationService's home option rows (ResponseCube 0x2a / 0x2b); read by the home option dialog's host.</summary>
    public readonly Ms2.Ui.HomeOptionRows HomeRows = new();   // reset at GameToLogin (OnGameToLogin) and at each field teardown (FieldEntry.CubeServiceCleared) (FUN_1410b76a0 @0x1410b76a0 calls FUN_1410d0310; its caller is not traced)

    /// <summary>The home save slot dialog's UpdateSlots after a save reply (ResponseCube 0x2e / 0x40); the dialog subscribes while it lives.</summary>
    public event Action SaveSlotsUpdated;

    /// <summary>After ResponseCube 0x14 stored the local player's home info (the changed flag UIUGCEventDispatcher::vfunc_7 gets).
    /// The client then asks the house visit question when the player has a home (scenestategame.cpp:10231-10236).</summary>
    public event Action<bool> HomeInfoApplied;
    /// <summary>The game event list's interact object shutdowns (GameEvent 0xB2, CGameEventValueManager
    /// ShutdownInteractObjectId); kept across fields, cleared with a new game scene.</summary>
    public readonly InteractObjectShutdowns InteractShutdowns = new();
    /// <summary>The game event list's construction sets (GameEventMassiveConstructionEvent / IgnoreConstructionLockEvent into
    /// CGameRule); kept across fields, cleared with a new game scene.</summary>
    public readonly ConstructionEvents Construction = new();
    /// <summary>The game event list's special ability hot times (GameEventSAHotTime / PCBangSAHotTime into CGameRule's
    /// sums); kept across fields, cleared with a new game scene.</summary>
    /// <summary>The running premium market sale events (CShopEventManager), for the shop's prices.</summary>
    public readonly Ms2.Ui.Shop.ShopSaleEvents SaleEvents = new();
    /// <summary>The running beauty shop sale events (GameEventSaleBeautyShop into CGameRule m_158), for the beauty shop's fee.</summary>
    public readonly Ms2.Ui.BeautyShopSales BeautySales = new();
    /// <summary>The game rule's meso revival events and flags (CGameRule m_2f0, m_368, m_36c), the revival dialog's flat meso rule.</summary>
    public readonly Ms2.Ui.MesoRevivalRule MesoRevivals = new();
    /// <summary>The game event list's MasteryReduce rate (CGameEventValueManager MasteryReduce), the gathering cast's time and
    /// caption; kept across fields, cleared with a new game scene.</summary>
    public readonly Ms2.Ui.MasteryReduceRule MasteryReduce = new();
    public readonly SpecialAbilityHotTimes HotTimes = new(PerMilleAbility);
    static Func<int, bool> perMilleAbility;
    static bool PerMilleAbility(int type)
    {
        try { return (perMilleAbility ??= GameTables.SpecialAbilityPerMille(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir)))(type); }
        catch (Exception e) { GD.PushWarning($"live: special ability table: {e.Message}"); return false; }
    }

    void NewFieldInteractObjects(int mapId)
    {
        InteractObjects?.CancelCast();
        InteractObjects?.Release();     // the old field's objects go: an active telescope's dtor skips its view
        HeldItem.Release();
        InteractNpcs ??= new LiveInteractNpc(new InteractNpcHost(this)) { Held = HeldItem };
        InteractNpcs.Clear();
        npcPolled = false;
        taxiPolled = false;
        holdPolled = false;
        TaxiAction.Attach(null);
        AutoFindFieldLeave();   // FUN_1409f1de0 and the OnceCallbackTimer's Clear (worldservice_1.cpp:9988-9989)
        var packs = PackSet.Shared(Settings.DataDir);
        InteractObjectTable tbl = null;
        try { tbl = InteractObjectTable.Shared(packs); }
        catch (Exception e) { GD.Print($"live: interact object table unavailable ({e.Message})"); }
        InteractObjects = new LiveInteractObjects(new InteractHost(this, mapId), tbl) { Held = HeldItem, MasteryReduce = MasteryReduce };
    }

    /// <summary>Binds the loaded field's xblock MS2InteractObject, MS2Breakable and MS2Liftable entities and applies any held
    /// 0x63, Breakable and Liftable messages.</summary>
    public void AttachInteractObjects(string xblock, IReadOnlyList<FieldEntity> entities)
    {
        if (InteractObjects == null || InteractObjects.Attached) return;
        string want = null;
        try { want = FieldData.FindById(PackSet.Shared(Settings.DataDir), fieldEntry.Current?.MapId ?? 0)?.XBlock; } catch { }
        bool same = want != null && string.Equals(want, xblock, StringComparison.OrdinalIgnoreCase);
        InteractObjects.Attach(same ? entities : Array.Empty<FieldEntity>());
        // the field load's breakables loop (worldservice_1.cpp:6757-6768); the Breakable updates held since the field enter follow
        if (Breakables.Holding) Breakables.AttachField(same ? entities : null);
        // the liftables loop (worldservice_1.cpp:6771-6786); the Liftable updates held since the field enter follow
        if (Liftables.Holding) Liftables.AttachField(same ? entities : null);
        // the vibrate objects loop (worldservice_1.cpp:6819-6866); the Vibrate messages held since the field enter follow
        if (Vibrates.Holding) Vibrates.AttachField(same ? entities : null, (uint)ServerTick);
        // the liftable target boxes loop (worldservice_1.cpp:6788-6801)
        LiftTargets.AttachField(same ? entities : null);
        TaxiAction.Attach(same ? entities : null);
        UgcBanners.Clear();                     // WorldService::FUN_141bd0420(0xc7f9dd65), the mouse's ugc banner pick
        if (same)
            foreach (var e in entities)
                if (e.HasMixin(Ms2.Ui.MouseInteract.UgcBannerMixin)) UgcBanners.Add(e.Position);
        AutoFindStationsAttached();
        GD.Print($"live: interact objects attached to xblock {xblock}{(same ? "" : $" (different xblock: no entities)")}: {InteractObjects.All.Count} registered, {Breakables.Count} breakables, {Liftables.Count} lifts, {LiftTargets.Count} lift targets, {Vibrates.Count} vibrate objects");
    }

    void OnInteractObject(InteractObjectMessage m)
    {
        if (InteractObjects == null) return;
        InteractObjects.Apply(m);
        if (m.Mode == InteractObjectMessage.ModeStates)
            if (LiveCheck) GD.Print($"live: InteractObject mode 8 ({m.Entries?.Count ?? 0} states)");
        else if (m.Mode is InteractObjectMessage.ModeState or InteractObjectMessage.ModeAdd or InteractObjectMessage.ModeRemove or InteractObjectMessage.ModeResult or InteractObjectMessage.ModeMessage)
            if (LiveCheck) GD.Print($"live: InteractObject mode {m.Mode} name '{m.Name}' kind {m.Kind} state {m.State} result {m.Result}");
    }

    /// <summary>
    /// CSceneStateGame::OnSetCraftMode (@0x141723410, scenestategame.cpp:7827-7851): the pc by id, nothing if it is missing or
    /// dead (:7832-7835), then PC::vfunc_135 with the cube mode, the craft mode and the item (0, 0 and none with mode 0).
    /// Another pc's goes to <see cref="RemoteLayer.SetCraftMode"/>.
    /// A pc not in the cube mode yet goes to StateIdle, or StateFall while its model is not in (PC::vfunc_135's vfunc_59,
    /// pc.cpp:3072-3078), which also ends a taxi call (ChangeState's vfunc_5).
    /// STILL-OPEN: MyPC's target refresh after it (FUN_141bd3620()-&gt;vfunc_149, :7846-7851).
    /// </summary>
    void OnSetCraftMode(SetCraftMode m)
    {
        if (m.Id != selfObject)
        {
            if (remote?.SetCraftMode(m.Id, m.CubeMode, m.CraftMode, m.Item) == true)
                if (LiveCheck) GD.Print($"live: SetCraftMode pc {m.Id} mode {m.CubeMode} craft {m.CraftMode}");
            return;
        }
        if (!Me.Dead && !CubeCraft.InCubeMode)
        {
            if (player != null && IsInstanceValid(player)) player.EnterIdleOrFall(!CubeCraft.ModelLoaded);
            EndTaxiCall();
        }
        CubeCraft.Set(m.CubeMode, m.CraftMode, m.Item, Me.Dead);
        GD.Print($"live: SetCraftMode mode {m.CubeMode} craft {m.CraftMode} item {m.Item?.ItemId ?? 0}");
    }

    /// <summary>RebuildGuides' carried model (Unk_PCCubeCraft::FUN_1410a9630, cubecraft_2.cpp:467-531) for the own pc's
    /// craft, on the bound body (<see cref="Look.CarriedModel"/>), and the body's carry stance (<see cref="Player.Craft"/>).</summary>
    void SyncCarriedModel()
    {
        if (player == null || !IsInstanceValid(player)) return;
        bool on = Look.CarriedModel.Sync(player.Visual, CubeCraft.Current, fieldBuilder, Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir), ItemDataOf);
        // the move states' cube tests: IsInCubeMode, IsCubeMode1, FUN_141659690 (a held lift) and HasGuide2
        player.Craft = new Player.CraftStance
        {
            InCubeMode = CubeCraft.InCubeMode, CubeMode1 = CubeCraft.CubeMode == 1, HoldsLift = CubeCraft.Current is { HoldsLift: true }, HasGuide2 = on,
        };
        if (on || CubeCraft.Current?.CarriesItemModel == true) GD.Print($"live: carried model of item {CubeCraft.Current?.Item?.ItemId ?? 0}: {(on ? "on the body" : "not built")}");
        RefreshLiftMarkers();
    }

    /// <summary>WorldService::FUN_141bd3920 (worldservice_1.cpp:7117-7131) at the end of a craft start or stop
    /// (cubecraft_2.cpp:416), the own pc's or another's: each lift target box's marker by the own pc's cube mode and craft
    /// item (<see cref="Ms2.Ui.LiftableTargetBoxes.RefreshMarkers"/>).</summary>
    internal void RefreshLiftMarkers()
    {
        if (LiftTargets.Count == 0) return;
        var packs = Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir);
        LiftTargets.RefreshMarkers(CubeCraft.InCubeMode, CubeCraft.Current?.Item?.ItemId ?? 0, Quests.All,
            id => World.NpcTalk.ClientQuests(packs).Quest(id));
    }

    /// <summary>CSceneStateGame::OnResponseCube (scenestategame.cpp:9876), the held-item modes (<see cref="HeldItem.ResponseCube"/>)
    /// and their errors' build notice for the local pc (<see cref="BuildNotice.OfHeldItemResponse"/>); another pc's go to
    /// <see cref="RemoteLayer.ResponseCube"/>.
    /// The housing modes 0x27 / 0x35 / 0x37 fill <see cref="Housing"/>. The place answers 0xa / 0xf add their cube to the
    /// player's cube map (<see cref="Ms2.Ui.CubeCellMap.Apply"/>).
    /// The place answers' own-pc part (the guide, the place sound and an error's notice) is
    /// <see cref="Ms2.Ui.CubeCraftState.OnPlaceAnswer"/>, before the cube goes into the map as in the client.
    /// The remove answer 0xc takes its cube out of the player's cube map (<see cref="Ms2.Ui.CubeCellMap.Apply(ResponseCubeRemove)"/>)
    /// after its own-pc part (<see cref="Ms2.Ui.CubeCraftState.OnRemoveAnswer"/>: the guide shown, the remove sound).
    /// The home info mode 0x14 fills <see cref="Home"/>; the row modes 0x2a / 0x2b fill <see cref="HomeRows"/>.
    /// STILL-OPEN: the other modes; the cube map's weapon lookup for mode 0x11, so a 0x11 holds nothing yet.</summary>
    void OnResponseCube(Ms2.Net.Wire.RawPacket p)
    {
        var r = p.Reader();
        byte mode = r.ReadByte();
        switch (mode)
        {
            case ResponseCubeHousingGrade.Mode: Housing.Apply(ResponseCubeHousingGrade.Read(r, accountId)); return;
            case ResponseCubeHouseOwner.Mode: Housing.Apply(ResponseCubeHouseOwner.Read(r), accountId); return;
            case ResponseCubeOwnerTrophies.Mode: Housing.Apply(ResponseCubeOwnerTrophies.Read(r)); return;
            case ResponseCubePlace.ModePlace:
            case ResponseCubePlace.ModeReplace:
            {
                var placed = ResponseCubePlace.Read(p.Reader());
                switch (CubeCraft.OnPlaceAnswer(placed, selfObject, Me.Dead))
                {
                    case Ms2.Ui.CubeCraftState.PlaceAnswer.Placed: FieldAudio.Instance?.PlayUi(Ms2.Ui.CubeCraftState.PlaceSound); break;
                    case Ms2.Ui.CubeCraftState.PlaceAnswer.SameItemHeld: ShowSameItemHeld(); break;
                    case Ms2.Ui.CubeCraftState.PlaceAnswer.BuildNotice: ShowBuildNotice(placed.Error); break;
                }
                bool added = player != null && IsInstanceValid(player) && player.Cubes.Cells.Apply(placed, id => ItemExists?.Invoke(id) ?? true);
                if (LiveCheck) GD.Print($"live: ResponseCube 0x{placed.Mode:x} error {placed.Error} cell 0x{placed.Cell:x} item {placed.Item?.ItemId ?? 0}{(added ? ", cube added" : "")}");
                FieldCubesMade(added ? 1 : 0);   // CreateCube's cube waits for its entity too (f78, :2699)
                return;
            }
            case ResponseCubeRemove.Mode:
            {
                var removed = ResponseCubeRemove.Read(p.Reader());
                switch (CubeCraft.OnRemoveAnswer(removed, selfObject, Me.Dead))
                {
                    case Ms2.Ui.CubeCraftState.PlaceAnswer.Removed: FieldAudio.Instance?.PlayUi(Ms2.Ui.CubeCraftState.RemoveSound); break;
                    case Ms2.Ui.CubeCraftState.PlaceAnswer.SameItemHeld: ShowSameItemHeld(); break;
                    case Ms2.Ui.CubeCraftState.PlaceAnswer.BuildNotice: ShowBuildNotice(removed.Error); break;
                }
                bool gone = player != null && IsInstanceValid(player) && player.Cubes.Cells.Apply(removed);
                if (LiveCheck) GD.Print($"live: ResponseCube 0xc error {removed.Error} cell 0x{removed.Cell:x}{(gone ? ", cube removed" : "")}");
                return;
            }
            case ResponseCubeHomeInfo.Mode: OnHomeInfo(ResponseCubeHomeInfo.Read(r, game.Profile)); return;
            case ResponseCubeRowSwitch.Mode: HomeRows.Apply(ResponseCubeRowSwitch.Read(r)); return;
            case ResponseCubeRowLevel.Mode: HomeRows.Apply(ResponseCubeRowLevel.Read(r)); return;
            case ResponseCubeSaveSlot.ModeDesign:
            case ResponseCubeSaveSlot.ModeBlueprint: OnSaveSlot(ResponseCubeSaveSlot.Read(r, mode)); return;
            case ResponseCubeHomeOption.ModeBackground:
            case ResponseCubeHomeOption.ModeLight:
            case ResponseCubeHomeOption.ModeCamera: OnHomeOption(ResponseCubeHomeOption.Read(r, mode)); return;
            case ResponseCubeBill.ModeCube: Bill.OnBill(ResponseCubeBill.Read(r, ResponseCubeBill.ModeCube)); return;
            case ResponseCubeBill.ModeBlueprint: Bill.OnBill(ResponseCubeBill.Read(r, ResponseCubeBill.ModeBlueprint)); return;
        }
        var m = ResponseCubeHeldItem.Read(p.Reader());
        if (m == null) return;
        // the pc by id (:10165 / :10180): another pc's held item is its own; the cube weapon test is not ported (null)
        if (m.Id == selfObject) HeldItem.ResponseCube(m, selfObject, null, ItemExists?.Invoke(m.ItemId) ?? true);
        else remote?.ResponseCube(m, null);
        int notice = BuildNotice.OfHeldItemResponse(m, selfObject);
        if (notice != 0) ShowBuildNotice(notice);
        if (LiveCheck) GD.Print($"live: ResponseCube 0x{m.Mode:x} error {m.Error} id {m.Id}");
    }

    /// <summary>LoadCubes (RecvOp 0x6A), CSceneStateGame::OnLoadCubes (@0x141728110, scenestategame.cpp:10857): mode 0's
    /// house cubes go into the field's cube map through the 0xa place path (<see cref="Ms2.Ui.CubeCellMap.Load"/>); the cubes
    /// made may answer the field entry (<see cref="FieldCubesMade"/>). Modes 1-3 (:10902-10944) fill the plot states, owners
    /// and sale states (<see cref="Ms2.Ui.CubePlots.Apply"/>). The cubes come before the own FieldAddUser, so they go to the
    /// built field's cube map, not through the bound player.</summary>
    void OnLoadCubes(Ms2.Net.Wire.RawPacket p)
    {
        var cells = FieldCubeCells();
        var m = LoadCubes.Read(p.Reader());
        if (m == null)
        {
            var plots = LoadCubesPlots.Read(p.Reader());
            if (plots != null && cells != null) cells.Plots.Apply(plots);
            return;
        }
        int added = cells?.Load(m, id => ItemExists?.Invoke(id) ?? true) ?? 0;
        GD.Print($"live: LoadCubes {m.Cubes.Count} cube(s), {added} added");
        FieldCubesMade(added);
    }

    /// <summary>ResponseCube 0x2e / 0x40 (scenestategame.cpp:10655-10697): an error shows its build notice; otherwise, for the own
    /// character with the service up, the slot goes into the list and the changed flag clears; then the open slot dialog updates.</summary>
    void OnSaveSlot(ResponseCubeSaveSlot m)
    {
        if (m.Error != 0) { ShowBuildNotice(m.Error); return; }
        // the client tests the service pointer (the home's CubeMapInformationService); here the field id stands for it
        if (m.CharacterId == accountId && FieldId != null)
        {
            if (m.Mode == ResponseCubeSaveSlot.ModeDesign) CubeMap.SetDesignSlot(m.CharacterId, m.Slot, m.Name, m.Time);
            else CubeMap.SetBlueprintSlot(m.CharacterId, m.Slot, m.Name, m.Time);
            CubeMap.SetDesignChanged(false, accountId);
        }
        SaveSlotsUpdated?.Invoke();
    }

    /// <summary>ResponseCube 0x33 / 0x34 / 0x36 (scenestategame.cpp:10699-10743): an error shows its build notice; otherwise the home
    /// setting's byte is set. STILL-OPEN: UgcHomeOptionData::ApplyBg / ApplyLight / ApplyCamera (the field's background, light and camera) are not ported.</summary>
    void OnHomeOption(ResponseCubeHomeOption m)
    {
        if (m.Error != 0) { ShowBuildNotice(m.Error); return; }
        if (m.Mode == ResponseCubeHomeOption.ModeBackground) CubeMap.SetBackground(m.Value);
        else if (m.Mode == ResponseCubeHomeOption.ModeLight) CubeMap.SetLighting(m.Value);
        else CubeMap.SetCamera(m.Value);
    }

    /// <summary>The built field's cube map (its player's CubeGrid), or null before the field is built.</summary>
    /// <summary>
    /// FunctionCube (CSceneStateGame::OnFunctionCube @0x1417212a0, scenestategame.cpp:3660 → CFunctionCubeService::FUN_14122f270
    /// @0x14122f270, functioncubeobject.cpp:2411): the state records switch the field's function cubes
    /// (<see cref="Ms2.Ui.FunctionCubes.Apply(Ms2.Net.Packets.FunctionCubeReply, long, bool, bool)"/>); a nurturing cube's record is
    /// read with its pet fields by the cube's kind. A use reply asks for a dialog, the furniture or a web link.
    /// STILL-OPEN: the dialogs of the sub-kinds (the object store is UIObjectStoreDialog; the trigger editor, cube sub scene,
    /// home password and summon have no port), the furniture state 0x29 and the web browser; each is logged. The home-made
    /// and nurturing modes (9, 10, 0x0b-0x0e) have no cube subclass here and are logged as read.
    /// </summary>
    void OnFunctionCube(Ms2.Net.Wire.RawPacket p)
    {
        var cubes = FieldCubeCells()?.Functions;
        var m = Ms2.Net.Packets.FunctionCubeReply.Read(p.Reader(), game.Profile, cubes == null ? null : cubes.IsNurturing);
        if (cubes == null) { ReadNotApplied("FunctionCube", m.Mode); return; }
        var r = cubes.Apply(m, CharacterId, OwnPlayer != null);
        switch (m.Kind)
        {
            case Ms2.Net.Packets.FunctionCubeKind.States:
            case Ms2.Net.Packets.FunctionCubeKind.State:
                if (LiveCheck)
                    foreach (var s in m.States)
                        GD.Print(cubes.Find(s.Id) is { } c
                            ? $"live: function cube {s.Id} state {s.State}{(s.Flag ? " moving" : "")}: now {c.State}, kind {c.Kind}, sequence '{c.Sequence}', sound '{c.Sound}'"
                            : $"live: function cube {s.Id} state {s.State}: {(cubes.FindPending(s.Id) != null ? "pending" : "no cube")}");
                return;
            case Ms2.Net.Packets.FunctionCubeKind.Use:
                if (LiveCheck) GD.Print($"live: function cube use {m.CubeId} owner {m.Owner}{(m.Owner == CharacterId ? " (own)" : "")} flag {m.Flag}: {r.Action}{(r.Action == Ms2.Ui.FunctionCubes.UseAction.OpenDialog ? $" {r.Dialog}" : "")}{(r.Cube != null ? $", state {r.Cube.State}" : ", no cube")}");
                if (r.Action != Ms2.Ui.FunctionCubes.UseAction.None) OnFunctionCubeUse(r);
                return;
            default:
                ReadNotApplied("FunctionCube", m.Mode);
                return;
        }
    }

    /// <summary>A bank cube's use reply asked for its store (OpenSubKindDialog case 4, OpenObjectStore); the HUD opens it.</summary>
    public event Action<Ms2.Ui.FunctionCubes.Cube> ObjectStoreRequested;

    /// <summary>The FunctionCube sends of the object store window (its close, mode 7).</summary>
    public void SendFunctionCube(Ms2.Net.Wire.IOutPacket packet)
    {
        if (game != null) Send(packet);
    }

    /// <summary>The use reply's asks (FUN_14122f270 mode 6): the object store (sub-kind 4) opens through
    /// <see cref="ObjectStoreRequested"/>. STILL-OPEN: each other one is logged only.</summary>
    void OnFunctionCubeUse(Ms2.Ui.FunctionCubes.UseResult r)
    {
        if (r.Action == Ms2.Ui.FunctionCubes.UseAction.OpenDialog && r.Dialog == 4 && ObjectStoreRequested != null)
        {
            if (LiveCheck) GD.Print($"live: function cube {r.Cube.Id} opens the object store of item {r.Cube.ItemId}");
            ObjectStoreRequested(r.Cube);
            return;
        }
        GD.Print($"live: function cube {r.Cube.Id} asks {r.Action}{(r.Action == Ms2.Ui.FunctionCubes.UseAction.OpenDialog ? $" {r.Dialog}" : "")}, not applied");
    }

    Ms2.Ui.CubeCellMap FieldCubeCells() => triggerField != null && IsInstanceValid(triggerField) ? triggerField.Cubes?.Cells : null;

    /// <summary>Cubes were made in the field (CubeMapInformationService FUN_1410cf5c0, cubemapinformationservice_3.cpp:5701-5705):
    /// in the loaded field with the timer flag clear, the field entry is answered (<see cref="Ms2.Net.FieldEntry.CubesMade"/>).</summary>
    void FieldCubesMade(int made)
    {
        bool first = !fieldEntry.Answered;
        FieldAnswered(fieldEntry.CubesMade(made, Send), first);
    }

    /// <summary>LoadUgcMap (RecvOp 0x7D), CSceneStateGame::OnLoadUgcMap @0x14170e800 (scenestategame.cpp:2172-2331): read in
    /// full; in the loaded field mode 0 answers the field entry, another mode waits for the cubes
    /// (<see cref="Ms2.Net.FieldEntry.LoadUgcMap"/>). STILL-OPEN: the house info's setters on CubeMapInformationService
    /// (:2247-2280) and the rule's Apply (housing).</summary>
    void OnLoadUgcMap(Ms2.Net.Wire.RawPacket p)
    {
        var m = LoadUgcMap.Read(p.Reader(), accountId);
        bool first = !fieldEntry.Answered;
        var answer = fieldEntry.LoadUgcMap(m, Send);
        GD.Print($"live: LoadUgcMap mode {m.Mode} map {m.MapId}{(m.Home != null ? ", with the house info" : "")}"
            + (answer != null ? ", field entry answered" : fieldEntry.Loaded ? ", waiting for the cubes" : ", field not loaded: dropped"));
        FieldAnswered(answer, first);
    }

    /// <summary>ResponseCube 0x14 (scenestategame.cpp:10199-10244): the info is the local player's when its owner id is the local object's
    /// (<see cref="Ms2.Ui.OwnHome.Apply"/>); then <see cref="HomeInfoApplied"/> for the callers.
    /// With no home and in cube mode the client sends SendCraftModeOff (:10238-10239, <see cref="SendCraftModeOff"/>).
    /// STILL-OPEN: CubeMapInformationService FUN_1410d5ae0(1), UIUGCEventDispatcher::vfunc_7, and the taxi refresh
    /// (CTaxiManager FUN_14085f730 / FUN_141739450) are not ported.</summary>
    void OnHomeInfo(ResponseCubeHomeInfo m)
    {
        if (!Home.Apply(m, selfObject, out bool changed)) return;
        GD.Print($"live: home info plot {m.PlotId} home {m.HomeId} open type {m.OpenType} changed {changed}");
        HomeInfoApplied?.Invoke(changed);
        if (Home.HasNoHome && CubeCraft.InCubeMode) SendCraftModeOff();
    }

    /// <summary>MyPC::SendCraftModeOff (@0x141530200, mypc.cpp:4501): nothing without an active cube craft (GetActiveCubeCraft @0x141530290: not dead,
    /// in cube mode, then the craft) or when the craft is the lift's mode 2 (FUN_1410aa1f0); else RequestSetCraftMode 0, 0. Returns whether it sent.</summary>
    public bool SendCraftModeOff()
    {
        if (!CubeCraft.CanSendCraftModeOff(Me.Dead)) return false;
        Send(new CraftModeOffRequest());
        return true;
    }

    /// <summary>GameEvent 0xB2 (GameEventManagerCli::Dispatch, gameeventmanagercli.cpp:77): the shutdown events are applied.
    /// Every entry is read with its type's layout; Truncated only for a name no class is registered under, where the client
    /// returns too (:144-147).</summary>
    void OnGameEvent(GameEventMessage m)
    {
        InteractShutdowns.Apply(m);
        Construction.Apply(m);
        HotTimes.Apply(m);
        SaleEvents.Apply(m);
        BeautySales.Apply(m);
        MesoRevivals.Apply(m);
        MasteryReduce.Apply(m);
        DungeonExtraRewards.Apply(m);
        GD.Print($"live: GameEvent kind {m.Kind}: {m.Shutdowns.Count} shutdown events, {m.HotTimes.Count} hot times, {m.RemovedIds.Count} removed{(m.Truncated ? ", stopped at an unregistered type" : "")}");
    }

    void OnInteractNpc(InteractNpcMessage m)
    {
        bool applied = InteractNpcs != null && InteractNpcs.Apply(m);
        GD.Print($"live: InteractNpc npc {m.ObjectId} talkable {(m.HasTalkable ? m.Talkable.ToString() : "-")}{(applied ? "" : " (no such npc)")}");
    }

    /// <summary>
    /// Per-frame action-key poll before the portal, by ascending action key: GatheringAction (4), HoldAction (6,
    /// <see cref="PollHold"/>), TaxiStationAction (8,
    /// <see cref="PollTaxi"/>), InteractNpcAction
    /// (0x11, <see cref="LiveInteractNpc.Poll"/>), InteractAction (0x12). 0 B/frame.
    /// </summary>
    public bool PollInteract(Vec3 at) => PollInteract(at, at);

    /// <summary>As <see cref="PollInteract(Vec3)"/>, with the npc search from <paramref name="npcAt"/> (the ridee's
    /// position when riding, WorldService::FindNearestNpc via worldservice_1.cpp:9679-9682).</summary>
    public bool PollInteract(Vec3 at, Vec3 npcAt)
    {
        taxiAt = at;
        taxiPoll ??= PollTaxi;
        holdPoll ??= PollHold;
        return InteractKey.Poll(InteractObjects, InteractNpcs, at, npcAt, holdPoll, taxiPoll, out npcPolled, out holdPolled, out taxiPolled);
    }

    /// <summary>The billboard action's poll (key 0x18, after the portal; 0 B/frame).</summary>
    public bool PollInteractBillBoard(Vec3 at) => InteractObjects != null && InteractObjects.PollBillBoard(at);

    /// <summary>The lift action's poll (LiftCubeAction key 0x15, after the portal; 0 B per poll), from the player's point or the
    /// ridee's when riding (PCBase::FUN_1416767d0, pcbase.cpp:3235).</summary>
    public bool PollLift(Vec3 at) => Liftables.Poll(at);

    /// <summary>PutDownLiftableAction::vfunc_2 (@0x14095ad50): ConfirmPlacement on the active cube craft; RequestCube 10
    /// goes out when it gets that far.</summary>
    public Ms2.Ui.CubeCraftState.PlacementStop ConfirmPlacement()
    {
        var stop = CubeCraft.ConfirmPlacement(Me.Dead, ItemDataOf, out var send);
        GD.Print(send != null
            ? $"live: RequestCube mode {send.Mode} cell 0x{send.CellKey:x} item {send.Item?.ItemId ?? 0} yaw {send.Yaw}"
            : $"live: put-down stopped: {stop}");
        if (send != null) Send(send);
        int notice = Ms2.Ui.CubeCraftState.NoticeOf(stop);
        if (notice != 0) ShowBuildNotice(notice);
        // FUN_141275260(0x6c9, 1, 1), then PlaySystemSound(DAT_143acf6b8, "") (cubecraft_2.cpp:1041-1042)
        if (stop == Ms2.Ui.CubeCraftState.PlacementStop.SameItemHeld) ShowSameItemHeld();
        return stop;
    }

    /// <summary>FUN_141275260(0x6c9, 1, 1) then the NoBuild system sound: ConfirmPlacement's same item in craft mode 2
    /// (cubecraft_2.cpp:1041-1042) and a refused place answer while the craft holds a lift (scenestategame.cpp:10024-10025,
    /// :10132-10133).</summary>
    void ShowSameItemHeld()
    {
        string text = MessageBoxData.Common(Ms2.Ui.CubeCraftState.SameItemHeldText);
        if (!string.IsNullOrEmpty(text)) SystemNoticeSink?.Invoke(text);
        FieldAudio.Instance?.PlayUi(BuildNotice.NoBuild);
    }

    /// <summary>MyPC::vfunc_135's construction unlock on entering the cube mode (<see cref="CubeCraftState.ConstructionUnlock"/>):
    /// (a) unknown, (b) the massive construction sets for the field (WorldService GetFieldId), the channel of the last
    /// ServerEnter (DAT_1439f4a7c) and the own account (DAT_143ab2820), (c) the own account's ignored construction lock.</summary>
    bool? ConstructionUnlockOnEnter() => CubeCraftState.ConstructionUnlock(null,
        Construction.IsMassiveConstruction((uint)(fieldEntry.Current?.MapId ?? 0), serverChannel, accountId),
        Construction.IsIgnoredConstructionLock(accountId));

    bool? housingLevelFeature;
    /// <summary>MyPC::FUN_1415302e0's inputs: the housing and interior tables, the housing state, feature 0xdb and the own
    /// trophies, and the game event list's ignored construction lock ids. Null when the tables do not load
    /// (<see cref="Ms2.Ui.CubeLock.WithTables"/>): the placement then stops at InventoryCheckOpen.</summary>
    Ms2.Ui.CubeLock CubeLocks()
    {
        var l = Ms2.Ui.CubeLock.WithTables(
            () => new Ms2.Formats.World.HousingTable(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir)),
            () => new Ms2.Formats.World.CubeInteriorTable(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir)),
            e => GD.Print($"live: housing tables unavailable ({e.Message}); cube placement stops at the inventory check"));
        if (l == null) return null;
        l.State = Housing;
        l.IsGradeCleared = Achieves.IsGradeCleared;
        l.IsFeatureEnabled = HousingFeature;
        l.IgnoredConstructionLock = Construction.IsIgnoredConstructionLock;
        return l;
    }

    bool HousingFeature(int id)
    {
        if (id != Ms2.Ui.CubeLock.HousingLevelFeature) return false;
        if (housingLevelFeature is { } known) return known;
        bool on = false;
        try { on = Ms2.Ui.LobbyFeatures.IsEnabled(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir), Ms2.Ui.FeatureTable.Names[id - 1]); }
        catch (Exception e) { GD.Print($"live: feature table unavailable ({e.Message})"); }
        housingLevelFeature = on;
        return on;
    }

    /// <summary>Unk_MyPCHousingSvc::ShowBuildNotice (<see cref="BuildNotice.Show"/>): the sound through the field audio's
    /// system sounds, the common string's text through <see cref="SystemNoticeSink"/> (ShowSystemNotice(text, 1, 1)).</summary>
    public void ShowBuildNotice(int message)
    {
        var shown = BuildNotice.Show(message, key => FieldAudio.Instance?.PlayUi(key), MessageBoxData.CommonByKey, SystemNoticeSink);
        if (shown == BuildNotice.Outcome.LackMeratOpen) GD.Print("live: STILL-OPEN the lack-merat box (build notice 0x22); nothing shown");
    }

    /// <summary>PutDownLiftableAction::vfunc_1 (@0x14095aa70) from the pc's (or ridee's) point and facing.</summary>
    public bool PollPutDown(Vec3 at, float rotZDegrees, Ms2.Ui.ICubeCells cubes) =>
        Ms2.Ui.PutDownLiftable.Poll(CubeCraft, InteractPlayerStateId(), new System.Numerics.Vector3((float)at.X, (float)at.Y, (float)at.Z),
            rotZDegrees, cubes, LiftTargets);

    /// <summary>The action key on the polled lift (LiftCubeAction::vfunc_2): a rider first sends RequestRide mode 1 with a
    /// RideOffAction(0) (SendRequestRideMode1(0): a battle or object mount refuses it with s_action_cant_ride), then
    /// Liftable (0x3C); or nothing.</summary>
    public void UseLift()
    {
        bool riding = player != null && IsInstanceValid(player) && player.Mount != null;
        var req = Liftables.Use(riding ? (rideOffForLift ??= RideOffForLift) : null);
        if (req == null) return;
        GD.Print($"live: Liftable lift {req.Name}");
        Send(req);
    }

    Func<bool> rideOffForLift;

    /// <summary>SendRequestRideMode1(0) before a lift: true when it was sent or there was nothing to ride off.</summary>
    bool RideOffForLift()
    {
        var ride = player?.Mount?.Data;
        var outcome = RideOff.Request(true, ride != null, ride?.Type ?? 0, ride?.EnableRideOffUI ?? false, 0, false, new RideOffRequest(), Send);
        if (outcome == RideOff.Outcome.CantRide) SystemNoticeSink?.Invoke(MessageBoxData.Common(RideOff.CantRideNoticeId));
        GD.Print($"live: ride off before a lift: {outcome}");
        return outcome is RideOff.Outcome.Sent or RideOff.Outcome.NotRiding;
    }

    /// <summary>Per-frame tick for gathering respawns and active StateInteraction cast (0 B/frame).</summary>
    public void TickInteract(Vec3 at, bool playerMoved)
    {
        InteractObjects?.Tick(at, playerMoved);
        InteractNpcs?.Tick(at, playerMoved);
        rideSummon?.Tick(playerMoved);   // StateSummon::vfunc_8, the body's move standing in for the move input
        recallCast?.Tick(playerMoved);   // StateRecall::vfunc_8, likewise
    }

    /// <summary>The field's ugc banner and prop entities (the MS2UccProp model) by position, MS2 axes.</summary>
    public readonly List<Vec3> UgcBanners = new();
    /// <summary>The last interact poll gave the key to the interact npc action (0x11).</summary>
    public bool InteractNpcPolled => npcPolled;
    /// <summary>MyPC::GetCurStateId as <see cref="OwnCurStateId"/> knows it, with the own skill state.</summary>
    public int OwnCurStateIdNow => OwnCurStateId(OwnSkillStateRunsOf?.Invoke() ?? false);

    /// <summary>Action key press on the polled interact object: only the polled action runs, and its own state gate refuses it
    /// while the other interaction's cast runs (that cast is not cancelled).</summary>
    public bool UseInteract(Vec3 at)
    {
        if (taxiPolled) { UseTaxi(); return true; }
        return InteractKey.Use(InteractObjects, InteractNpcs, npcPolled, at);
    }

    /// <summary>The client state id as far as it is known here (<see cref="InteractKey.StateId"/>): 0x26 while the ride item's
    /// StateSummon casts; 0x3d while StateReact runs; 0x19 / 0x1a while an interaction's cast runs; riding 0x21; on the spawn or idle state, 1 while the body stands and 2 while it walks; a
    /// state the server set otherwise. A move the client makes on its own that is not idle or walk (jump, fall, swim, climb,
    /// ...) is -1, which the interactions refuse.</summary>
    int InteractPlayerStateId()
    {
        if (selfObject < 0 || player == null || !IsInstanceValid(player)) return -1;
        if (TaxiCalling) return TaxiStationAction.StateTaxiCall;
        if (recallCast is { IsCasting: true }) return RecallChecks.StateRecall;
        if (CashCalling) return CashCall.StateId;
        int move;
        if (player.Mount != null) move = 0x21;
        else
        {
            int st = Me.State;
            move = st is not (0 or 1 or 2) ? st : player.State switch
            {
                Player.MoveState.Idle => 1,
                Player.MoveState.Walk => 2,
                _ => -1,
            };
        }
        return InteractKey.StateId(InteractObjects, InteractNpcs, reactState, rideSummon, move);
    }

    /// <summary>The own PC's current state id as PCBase::GetCurStateId gives it (<see cref="Ms2.Ui.OwnPcState.CurStateId"/>): the
    /// taxi call and the interaction casts as <see cref="InteractPlayerStateId"/> has them, but a move that is not idle or walk
    /// keeps its own id (jump 6, fall 5, swim 27, climb 29 ...) and the own skill state is 0x10. -1 outside the field.</summary>
    public int OwnCurStateId(bool skillStateRuns)
    {
        if (selfObject < 0 || player == null || !IsInstanceValid(player)) return -1;
        if (TaxiCalling) return TaxiStationAction.StateTaxiCall;
        if (recallCast is { IsCasting: true }) return RecallChecks.StateRecall;
        if (CashCalling) return CashCall.StateId;
        return InteractKey.StateId(InteractObjects, InteractNpcs, reactState,
            Ms2.Ui.OwnPcState.CurStateId(Me.State, player.Mount != null, skillStateRuns, player.MoveStateId));
    }

    /// <summary>The local pc as the cube craft reads it (<see cref="Ms2.Ui.ICubeCraftOwner"/>): the player's point or the
    /// ridee's when riding (PCBase::FUN_1416767d0), the facing (GetRideeAwareRotation), the target yaw, the state id as
    /// <see cref="InteractPlayerStateId"/> knows it, and the field's cubes. No allocation.</summary>
    sealed class CubeCraftOwner : Ms2.Ui.ICubeCraftOwner
    {
        readonly LiveSession s;
        public CubeCraftOwner(LiveSession s) { this.s = s; }

        public bool IsLocal => s.selfObject >= 0 && s.player != null && IsInstanceValid(s.player);

        public System.Numerics.Vector3 Point
        {
            get
            {
                var ridee = s.player.Mount?.Visual;
                var p = FieldPortals.ToMs2(ridee != null && IsInstanceValid(ridee) ? ridee.GlobalPosition : s.player.GlobalPosition);
                return new System.Numerics.Vector3((float)p.X, (float)p.Y, (float)p.Z);
            }
        }

        public float RotZ => s.player.FacingRotZ;
        public float TargetYaw => s.player.TargetRotZ;
        public int StateId => s.InteractPlayerStateId();
        public Ms2.Ui.ICubeCells Cubes => s.player.Cubes;
    }

    sealed class InteractNpcHost : IInteractNpcHost
    {
        readonly LiveSession s;
        float? interactDist;
        public InteractNpcHost(LiveSession s) { this.s = s; }

        public bool HasPlayer => s.selfObject >= 0 && s.player != null && IsInstanceValid(s.player);

        public float InteractionDistance
        {
            get
            {
                if (interactDist is { } d) return d;
                float v = 0f;     // Constants InteractionDistance.Define(.., 0.0f) (unit_1402ab440_constantsblocks.cpp:774)
                try { v = FieldContents.Constants(PackSet.Shared(Settings.DataDir)).Float("InteractionDistance", 0f); } catch { }
                interactDist = v;
                return v;
            }
        }

        public Vec3 PlayerForward
        {
            get
            {
                if (s.player == null || !IsInstanceValid(s.player)) return new Vec3(0, 0, 0);
                var f = s.player.FacingDirection;      // godot (x, y, z) -> MS2 (x, -z, y), FieldPortals.ToMs2
                return new Vec3(f.X, -f.Z, f.Y);
            }
        }

        // STILL-OPEN: PvPInfo point (CPvPInfo::HasPoint / GetPoint, actionkeyobject.cpp:1667) is not tracked
        public int? PvPPoint => null;
        public int PlayerStateId => s.InteractPlayerStateId();

        public void Send(Ms2.Net.Wire.IOutPacket packet)
        {
            s.Send(packet);
            s.InteractSends++;
            GD.Print($"live: interact npc send {packet}");
        }

        public bool StartCasting(ICastingEventListener listener, int durationMs, bool showBar)
        {
            if (s.CastingBar == null) return false;
            s.CastingBar.StartCastingText(listener, "", durationMs, showTime: false, activeBar: showBar, endText: "");
            return true;
        }

        public long NowMs => (long)Time.GetTicksMsec();

        public void CancelCasting() => s.CastingBar?.Cancel();

        public void PlayPlayerAnim(string animName, bool loop)
        {
            if (s.player != null && IsInstanceValid(s.player)) s.player.StartInteractAnim(animName, loop);
        }

        public void StopPlayerInteractAnim()
        {
            if (s.player != null && IsInstanceValid(s.player)) s.player.StopInteractAnim();
        }
    }

    sealed class InteractHost : IInteractHost
    {
        readonly LiveSession s;
        readonly int fieldId;
        Dictionary<string, string> actionNames, noticeTexts, itemNames;
        float? masteryDist;

        public InteractHost(LiveSession s, int fieldId) { this.s = s; this.fieldId = fieldId; }
        static PackSet Packs => PackSet.Shared(Settings.DataDir);
        static GameStrings Strings => new(Packs, GameOptions.StringLanguage);

        public bool HasPlayer => s.selfObject >= 0 && s.player != null && IsInstanceValid(s.player);
        /// <summary>CGuideManager::IsRestricted(ActionInteraction); no limit before the guide is made.</summary>
        public bool IsRestricted(int kind) => s.guide != null && s.guide.IsRestricted((Ms2.Ui.Guide.GuideLimit)kind);
        public int FieldId => fieldId;
        /// <summary>The ServerEnter channel.</summary>
        public ushort Channel => (ushort)(s.entry?.Enter?.Channel ?? 0);
        public long NowMs => (long)Time.GetTicksMsec();
        public bool PlayerRiding => s.player != null && IsInstanceValid(s.player) && s.player.Mount != null;
        public int LocalObjectId => s.selfObject;
        public bool ItemExists(int itemId) => s.ItemExists?.Invoke(itemId) ?? true;

        public int PlayerStateId => s.InteractPlayerStateId();

        public bool IsInteractObjectShutdown(int fieldId, int rowId) => s.InteractShutdowns.IsShutdown(fieldId, rowId);

        // STILL-OPEN: MyPC::FUN_14152e0c0's state controller update has no counterpart; the move sync runs on its own
        public void FlushPlayerState() { }

        public float MasteryInteractionDistance
        {
            get
            {
                if (masteryDist is { } d) return d;
                float v = 300f;
                try { v = FieldContents.Constants(Packs).Float("MasteryObjectInteractionDistance", 300f); } catch { }
                masteryDist = v;
                return v;
            }
        }

        public QuestMaskTable.QuestStatus QuestStatusOf(int questId) => s.QuestStatusOf(questId);

        /// <summary>CInteractObject::CheckItemRequirement (interactobject.cpp:515-533): rank 0 is the item's own rank
        /// (GetItemRank); the first bag item of that code and rarity (Unk_Inventory::FUN_140a5e5c0, inventory.cpp:671) must
        /// hold the consume count. STILL-OPEN: PC::CanUseItem on that item.</summary>
        public bool HasRequiredItem(uint code, int rank, uint consume)
        {
            if (code == 0) return true;
            int rarity = rank != 0 ? rank : s.ItemRank?.Invoke((int)code) ?? 0;
            foreach (var it in s.Items.All)
            {
                if ((uint)it.Id != code || it.Rarity != rarity) continue;
                return (uint)it.Amount >= consume;
            }
            return false;
        }

        // STILL-OPEN: PCBase additional effect active check (FUN_140970020) is not wired to LiveSession buffs yet
        public bool HasAdditionalEffect(uint effectId, ushort level) => false;
        public string AdditionalEffectName(uint effectId, ushort level)
        {
            try { return GameTables.Shared(Packs, GameOptions.StringLanguage).AdditionalDescription(effectId, level, "name") ?? ""; }
            catch { return ""; }
        }

        public bool IsPartyLeader => s.Me.User != null && s.Party.IsLeader(s.Me.User.CharacterId);
        // STILL-OPEN: the mastery grade and the recipe's mastery and limit checks (CInteractGatheringObject::vfunc_7) are
        // not read; a mastery-gated object is refused, and a gathering recipe sends more once MasteryGrade is wired
        public int MasteryGrade(int masteryType) => 0;
        public bool CanGatherRecipe(int recipeId) => true;

        public void Send(Ms2.Net.Wire.IOutPacket packet)
        {
            s.Send(packet);
            s.InteractSends++;
            GD.Print($"live: interact send {packet}");
        }

        public string Common(int id) => CommonStringIds.Key(id) is { } k ? MessageBoxData.CommonByKey(k) ?? "" : "";
        public string CommonByKey(string key) => MessageBoxData.CommonByKey(key) ?? "";

        public string ItemName(uint itemCode)
        {
            try { itemNames ??= Strings.Table("itemname", "name"); } catch { itemNames = new(); }
            return itemNames.TryGetValue(itemCode.ToString(), out var n) ? n : itemCode.ToString();
        }

        public string InteractActionName(int rowId)
        {
            try { actionNames ??= Strings.Table("stringinteractobjectaction", "name"); } catch { actionNames = new(); }
            return actionNames.TryGetValue(rowId.ToString(), out var n) ? n : "";
        }

        public string InteractNoticeText(int nameId)
        {
            try { noticeTexts ??= Strings.Table("stringinteractobjectnotice", "name"); } catch { noticeTexts = new(); }
            return noticeTexts.TryGetValue(nameId.ToString(), out var n) ? n : "";
        }

        public string MasteryTypeName(int type) => type.ToString();
        public string MasteryGradeName(int type, int value) => value.ToString();

        public void SystemNotice(string text, int style = 1)
        {
            if (string.IsNullOrEmpty(text)) return;
            GD.Print($"live: interact notice (style {style}) '{text}'");
            s.SystemNoticeSink?.Invoke(text);
        }

        public void ChatNotice(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            GD.Print($"live: interact chat notice '{text}'");
            if (s.ChatNoticeSink != null) s.ChatNoticeSink(text);
            else s.SystemNoticeSink?.Invoke(text);
        }

        public bool StartCasting(ICastingEventListener listener, string text, int durationMs)
        {
            if (s.CastingBar == null) return false;
            s.CastingBar.StartCastingText(listener, text ?? "", durationMs, showTime: false, activeBar: true, endText: "");
            return true;
        }

        public void CancelCasting() => s.CastingBar?.Cancel();

        public void PlayPlayerAnim(string animName, bool loop)
        {
            if (s.player != null && IsInstanceValid(s.player)) s.player.StartInteractAnim(animName, loop);
        }

        public void StopPlayerInteractAnim()
        {
            if (s.player != null && IsInstanceValid(s.player)) s.player.StopInteractAnim();
        }

        public void OnObjectStateChanged(LiveInteractObject obj) => s.InteractStateDrawn?.Invoke(obj);

        public void OnObjectRemoved(LiveInteractObject obj) => s.InteractRemoved?.Invoke(obj);

        public void OnObjectSuccessEffect(LiveInteractObject obj, string effectAsset) => s.InteractEffect?.Invoke(obj, effectAsset, true);

        public void OnObjectRemoveEffect(LiveInteractObject obj, string effect) => s.InteractEffect?.Invoke(obj, effect, false);

        public int OnOpenUiDialog(string swf, string value)
        {
            int id = s.InteractUiDialog?.Invoke(swf, value) ?? 0;
            if (LiveCheck) GD.Print($"live: interact open UI '{swf}' value '{value}': dialog 0x{id:x}");
            return id;
        }

        public bool IsDialogClosed(int dialogId) => s.InteractUiDialogClosed?.Invoke(dialogId) ?? true;

        public void CloseDialog(int dialogId) => s.InteractUiDialogClose?.Invoke(dialogId);

        public void OnGuideTrigger(int triggerType, int rowId) => s.guide?.AddTrigger((Ms2.Ui.Guide.GuideTrigger)triggerType, rowId);

        public void StillOpen(string what) => GD.Print($"live: STILL-OPEN {what}; nothing sent");

        // ---- the telescope (CInteractTelescopeObject) ------------------------------------------------------------------------

        Field TriggerField => s.triggerField != null && IsInstanceValid(s.triggerField) ? s.triggerField : null;
        float? telescopeDist;

        /// <summary>The own pc's field position (PCBase::FUN_1416767d0).</summary>
        public Vec3 PlayerPosition => HasPlayer ? FieldPortals.ToMs2(s.player.GlobalPosition) : default;
        public float TelescopeFindDistance
        {
            get
            {
                if (telescopeDist is { } d) return d;
                float v = LiveInteractObjects.DefaultTelescopeFindDistance;
                try { v = FieldContents.Constants(Packs).Float("TelescopeFindDistance", v); } catch { }
                telescopeDist = v;
                return v;
            }
        }
        /// <summary>CTriggerService::QueueCameraSteps(ids, 0) on the field's trigger objects; nothing before the field is built.</summary>
        public void QueueCameraSteps(IReadOnlyList<int> ids) => TriggerField?.Triggers?.QueueCameraSteps(ids, false);
        /// <summary>CTriggerService::FUN_141863460(0.0f) (<see cref="World.FieldTriggers.ResetCamera"/>).</summary>
        public void ResetTriggerCamera() => TriggerField?.Triggers?.ResetCamera(0f);
        public void PlaySystemSound(string name) => FieldAudio.Instance?.PlayUi(name);
        /// <summary>GFxSceneService::SetSubScene: the cinematic sub scene is the cut scene cinematic pane's (its key group).</summary>
        public void SetSubScene(int id) => s.Cinematic.SetSubScene(id);
        /// <summary>FindCutSceneCinematic(0x4e52): the session's pane model; the HUD makes its movie when the sub scene is entered.</summary>
        public Ms2.Ui.Hud.CutSceneCinematic Cinematic => s.Cinematic;
        // STILL-OPEN: GetGamepadState and the pad's action key names (GetActionKeyName kind 2) are not ported; the key cap is the
        // keyboard's common text
        public void SetFogRange(bool on, float near, float far, uint color) => TriggerField?.SetFogRange(on, near, far, color);
        public void SetFogEffect(bool on, uint color, float upper, float lower, float percentage) =>
            TriggerField?.SetFogEffect(on, color, upper, lower, percentage);
        /// <summary>The fog the field's load applied (<see cref="Field.LoadedFog"/>).</summary>
        public FieldFog FieldFog => TriggerField?.LoadedFog;
    }
}
