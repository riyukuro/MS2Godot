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

    /// <summary>CubeMapInformationService's home option rows (ResponseCube 0x2a / 0x2b); read by the home option dialog's host.</summary>
    public readonly Ms2.Ui.HomeOptionRows HomeRows = new();   // reset at GameToLogin (OnGameToLogin) and at each field teardown (FieldEntry.CubeServiceCleared) (FUN_1410b76a0 @0x1410b76a0 calls FUN_1410d0310; its caller is not traced)

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
        HeldItem.Release();
        InteractNpcs ??= new LiveInteractNpc(new InteractNpcHost(this)) { Held = HeldItem };
        InteractNpcs.Clear();
        npcPolled = false;
        taxiPolled = false;
        TaxiAction.Attach(null);
        var packs = PackSet.Shared(Settings.DataDir);
        InteractObjectTable tbl = null;
        try { tbl = InteractObjectTable.Shared(packs); }
        catch (Exception e) { GD.Print($"live: interact object table unavailable ({e.Message})"); }
        InteractObjects = new LiveInteractObjects(new InteractHost(this, mapId), tbl) { Held = HeldItem };
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
        // the liftable target boxes loop (worldservice_1.cpp:6788-6801)
        LiftTargets.AttachField(same ? entities : null);
        TaxiAction.Attach(same ? entities : null);
        GD.Print($"live: interact objects attached to xblock {xblock}{(same ? "" : $" (different xblock: no entities)")}: {InteractObjects.All.Count} registered, {Breakables.Count} breakables, {Liftables.Count} lifts, {LiftTargets.Count} lift targets");
    }

    void OnInteractObject(InteractObjectMessage m)
    {
        if (InteractObjects == null) return;
        InteractObjects.Apply(m);
        if (m.Mode == InteractObjectMessage.ModeStates)
            GD.Print($"live: InteractObject mode 8 ({m.Entries?.Count ?? 0} states)");
        else if (m.Mode is InteractObjectMessage.ModeState or InteractObjectMessage.ModeAdd or InteractObjectMessage.ModeRemove or InteractObjectMessage.ModeResult or InteractObjectMessage.ModeMessage)
            GD.Print($"live: InteractObject mode {m.Mode} name '{m.Name}' kind {m.Kind} state {m.State} result {m.Result}");
    }

    /// <summary>
    /// CSceneStateGame::OnSetCraftMode (@0x141723410, scenestategame.cpp:7827-7851): the pc by id, nothing if it is missing or
    /// dead (:7832-7835), then PC::vfunc_135 with the cube mode, the craft mode and the item (0, 0 and none with mode 0).
    /// Another pc's goes to <see cref="RemoteLayer.SetCraftMode"/>.
    /// STILL-OPEN: MyPC's target refresh after it (FUN_141bd3620()-&gt;vfunc_149, :7846-7851); the own pc's StateIdle on
    /// entering the cube mode (PC::vfunc_135's vfunc_59, pc.cpp:3072-3078).
    /// </summary>
    void OnSetCraftMode(SetCraftMode m)
    {
        if (m.Id != selfObject)
        {
            if (remote?.SetCraftMode(m.Id, m.CubeMode, m.CraftMode, m.Item) == true)
                GD.Print($"live: SetCraftMode pc {m.Id} mode {m.CubeMode} craft {m.CraftMode}");
            return;
        }
        CubeCraft.Set(m.CubeMode, m.CraftMode, m.Item, Me.Dead);
        GD.Print($"live: SetCraftMode mode {m.CubeMode} craft {m.CraftMode} item {m.Item?.ItemId ?? 0}");
    }

    /// <summary>CSceneStateGame::OnResponseCube (scenestategame.cpp:9876), the held-item modes (<see cref="HeldItem.ResponseCube"/>)
    /// and their errors' build notice for the local pc (<see cref="BuildNotice.OfHeldItemResponse"/>); another pc's go to
    /// <see cref="RemoteLayer.ResponseCube"/>.
    /// The housing modes 0x27 / 0x35 / 0x37 fill <see cref="Housing"/>. The place answers 0xa / 0xf add their cube to the
    /// player's cube map (<see cref="Ms2.Ui.CubeCellMap.Apply"/>).
    /// The place answers' own-pc part (the guide, the place sound and an error's notice) is
    /// <see cref="Ms2.Ui.CubeCraftState.OnPlaceAnswer"/>, before the cube goes into the map as in the client.
    /// The home info mode 0x14 fills <see cref="Home"/>; the row modes 0x2a / 0x2b fill <see cref="HomeRows"/>.
    /// STILL-OPEN: the other modes; the cube map's weapon lookup for mode 0x11, so a 0x11 holds nothing yet.</summary>
    void OnResponseCube(Ms2.Net.Wire.RawPacket p)
    {
        var r = p.Reader();
        switch (r.ReadByte())
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
                GD.Print($"live: ResponseCube 0x{placed.Mode:x} error {placed.Error} cell 0x{placed.Cell:x} item {placed.Item?.ItemId ?? 0}{(added ? ", cube added" : "")}");
                FieldCubesMade(added ? 1 : 0);   // CreateCube's cube waits for its entity too (f78, :2699)
                return;
            }
            case ResponseCubeHomeInfo.Mode: OnHomeInfo(ResponseCubeHomeInfo.Read(r, game.Profile)); return;
            case ResponseCubeRowSwitch.Mode: HomeRows.Apply(ResponseCubeRowSwitch.Read(r)); return;
            case ResponseCubeRowLevel.Mode: HomeRows.Apply(ResponseCubeRowLevel.Read(r)); return;
        }
        var m = ResponseCubeHeldItem.Read(p.Reader());
        if (m == null) return;
        // the pc by id (:10165 / :10180): another pc's held item is its own; the cube weapon test is not ported (null)
        if (m.Id == selfObject) HeldItem.ResponseCube(m, selfObject, null, ItemExists?.Invoke(m.ItemId) ?? true);
        else remote?.ResponseCube(m, null);
        int notice = BuildNotice.OfHeldItemResponse(m, selfObject);
        if (notice != 0) ShowBuildNotice(notice);
        GD.Print($"live: ResponseCube 0x{m.Mode:x} error {m.Error} id {m.Id}");
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

    /// <summary>The built field's cube map (its player's CubeGrid), or null before the field is built.</summary>
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
        GD.Print($"live: GameEvent kind {m.Kind}: {m.Shutdowns.Count} shutdown events, {m.HotTimes.Count} hot times, {m.RemovedIds.Count} removed{(m.Truncated ? ", stopped at an unregistered type" : "")}");
    }

    void OnInteractNpc(InteractNpcMessage m)
    {
        bool applied = InteractNpcs != null && InteractNpcs.Apply(m);
        GD.Print($"live: InteractNpc npc {m.ObjectId} talkable {(m.HasTalkable ? m.Talkable.ToString() : "-")}{(applied ? "" : " (no such npc)")}");
    }

    /// <summary>
    /// Per-frame action-key poll before the portal, by ascending action key: GatheringAction (4), TaxiStationAction (8,
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
        return InteractKey.Poll(InteractObjects, InteractNpcs, at, npcAt, taxiPoll, out npcPolled, out taxiPolled);
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

    /// <summary>The action key on the polled lift (LiftCubeAction::vfunc_2): Liftable (0x3C), or nothing.</summary>
    public void UseLift()
    {
        bool riding = player != null && IsInstanceValid(player) && player.Mount != null;
        var req = Liftables.Use(riding, out bool rideOffOpen);
        if (rideOffOpen) GD.Print("live: STILL-OPEN the ride off before a lift (MyPC::SendRequestRideMode1); nothing sent");
        if (req == null) return;
        GD.Print($"live: Liftable lift {req.Name}");
        Send(req);
    }

    /// <summary>Per-frame tick for gathering respawns and active StateInteraction cast (0 B/frame).</summary>
    public void TickInteract(Vec3 at, bool playerMoved)
    {
        InteractObjects?.Tick(at, playerMoved);
        InteractNpcs?.Tick(at, playerMoved);
    }

    /// <summary>Action key press on the polled interact object: only the polled action runs, and its own state gate refuses it
    /// while the other interaction's cast runs (that cast is not cancelled).</summary>
    public bool UseInteract(Vec3 at)
    {
        if (taxiPolled) { UseTaxi(); return true; }
        return InteractKey.Use(InteractObjects, InteractNpcs, npcPolled, at);
    }

    /// <summary>The client state id as far as it is known here (<see cref="InteractKey.StateId"/>): 0x3d while StateReact
    /// runs; 0x19 / 0x1a while an interaction's cast runs; riding 0x21; on the spawn or idle state, 1 while the body stands and 2 while it walks; a
    /// state the server set otherwise. A move the client makes on its own that is not idle or walk (jump, fall, swim, climb,
    /// ...) is -1, which the interactions refuse.</summary>
    int InteractPlayerStateId()
    {
        if (selfObject < 0 || player == null || !IsInstanceValid(player)) return -1;
        if (TaxiCalling) return TaxiStationAction.StateTaxiCall;
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
        return InteractKey.StateId(InteractObjects, InteractNpcs, reactState, move);
    }

    /// <summary>The own PC's current state id as PCBase::GetCurStateId gives it (<see cref="Ms2.Ui.OwnPcState.CurStateId"/>): the
    /// taxi call and the interaction casts as <see cref="InteractPlayerStateId"/> has them, but a move that is not idle or walk
    /// keeps its own id (jump 6, fall 5, swim 27, climb 29 ...) and the own skill state is 0x10. -1 outside the field.</summary>
    public int OwnCurStateId(bool skillStateRuns)
    {
        if (selfObject < 0 || player == null || !IsInstanceValid(player)) return -1;
        if (TaxiCalling) return TaxiStationAction.StateTaxiCall;
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

        public void OnObjectStateChanged(LiveInteractObject obj)
        {
            // STILL-OPEN: dynamic mesh swap / visibility / PhysX box toggle on the rendered field entity
        }

        public void OnObjectSuccessEffect(LiveInteractObject obj, string effectAsset)
        {
            GD.Print($"live: interact effect '{effectAsset}' at {obj.Name}");
        }

        public void OnOpenUiDialog(string swf, string value)
        {
            GD.Print($"live: interact open UI '{swf}' value '{value}' (STILL-OPEN)");
        }

        public void OnGuideTrigger(int triggerType, int rowId) => s.guide?.AddTrigger((Ms2.Ui.Guide.GuideTrigger)triggerType, rowId);

        public void StillOpen(string what) => GD.Print($"live: STILL-OPEN {what}; nothing sent");
    }
}
