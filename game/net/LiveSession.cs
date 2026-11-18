using Godot;
using Ms2.Formats.Model;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2Godot.Flow;
using Ms2Godot.Ui;
using Ms2Godot.Ui.Contracts;
using Ms2Godot.World;

namespace Ms2Godot.Net;

/// <summary>
/// The opt-in live mode: the login, character list, select and field entry run against a real server instead of the offline
/// stubs. Off unless MS2_LIVE_LOGIN (host:port of the login server) and MS2_LIVE_ACCOUNT (user:password) are set; offline stays
/// the default and nothing here runs then. MS2_LIVE_AUTO=1 drives the screens by itself (first character, no input), for a
/// headless run. The transports only queue packets on their network thread; this node drains them in _Process.
/// </summary>
public partial class LiveSession : Node
{
    public static bool Enabled => !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("MS2_LIVE_LOGIN"))
                                  && !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("MS2_LIVE_ACCOUNT"));
    public static LiveSession Instance { get; private set; }
    /// <summary>A live session is running: the field's offline npcs, mobs and portal walking stay off; the server's replace them.</summary>
    public static bool Active => Instance != null;

    readonly byte[] machine = new byte[16];
    TcpTransport login, game;
    long accountId;
    GameEntry entry;
    string host;
    int selfObject = -1;     // the own FieldAddUser object id
    byte syncNumber;         // CNetworkService::OnSyncNumber: the number UserSync carries
    Player player;           // the own player, once the field is built
    RemoteLayer remote;      // the other players and npcs, once the field is built
    bool answered;           // ResponseFieldEnter sent for this entry
    public bool Busy { get; private set; }
    /// <summary>The last create the server refused (null after a create that went through).</summary>
    /// <summary>The account's character limit from CharMaxCount 0xB8 (DAT_1439f4a84); 0 until the server sent it.</summary>
    public int CharacterLimit;
    /// <summary>The quest state the quest packets build (live only; the quest log reads it, offline it stays empty).</summary>
    public readonly QuestBook Quests = new();
    /// <summary>The item inventory from the ItemInventory packets (OnItemInventory); live only.</summary>
    public readonly ItemBook Items = new();
    public readonly GlobalFactor Factors = new();   // CGloabalFactor: exp/meso rates of OnGlobalFactor
    public CharacterCreateRefused LastRefusal { get; private set; }

    public static void Attach(Node parent)
    {
        if (!Enabled || Instance != null) return;
        parent.AddChild(Instance = new LiveSession { Name = "LiveSession", ProcessMode = ProcessModeEnum.Always });
        Field.Built += Instance.OnFieldBuilt;
    }

    public override void _ExitTree()
    {
        Field.Built -= OnFieldBuilt;
        game?.Dispose(); login?.Dispose();
        if (Instance == this) Instance = null;
    }

    /// <summary>Login and the character list; the list replaces the stub list. False when the server refuses.</summary>
    public async Task<bool> LoginAsync()
    {
        Busy = true;
        try
        {
            var hp = System.Environment.GetEnvironmentVariable("MS2_LIVE_LOGIN").Split(':');
            var up = System.Environment.GetEnvironmentVariable("MS2_LIVE_ACCOUNT").Split(':', 2);
            host = hp[0];
            login?.Dispose();
            login = new TcpTransport();
            // the real client appends its region code to the id
            var o = await new LoginClient(login).LoginAsync(host, int.Parse(hp[1]), up[0] + "KR", up.Length > 1 ? up[1] : "", machine);
            if (!o.Success) { GD.PushWarning($"live: login refused, state {o.Result.State} {o.Result.Message}"); return false; }
            accountId = o.Result.AccountId;
            Session.AccountId = up[0];
            Session.Characters = o.Characters.ToList();
            if (o.CharacterLimit > 0) CharacterLimit = o.CharacterLimit;
            GD.Print($"live: logged in, {o.Characters.Count} character(s): {string.Join(", ", o.Characters.Select(c => c.User.Name))}");
            return true;
        }
        catch (Exception ex) { GD.PushWarning("live: login failed: " + ex.Message); return false; }
        finally { Busy = false; }
    }

    /// <summary>The create screen's request goes to the login server; the new character joins the list. Null (logged) when the server refuses.</summary>
    public async Task<CharacterEntry> CreateAsync(CreateCharacter request)
    {
        Busy = true;
        LastRefusal = null;
        try
        {
            var e = await new LoginClient(login).CreateAsync(request);
            Session.Characters.Add(e);
            GD.Print($"live: character created: {e.User.Name} id {e.User.CharacterId}");
            return e;
        }
        catch (CharacterCreateRefused ex) { GD.PushWarning("live: " + ex.Message); LastRefusal = ex; return null; }
        catch (Exception ex) { GD.PushWarning("live: create failed: " + ex.Message); return null; }
        finally { Busy = false; }
    }

    /// <summary>Delete request for a character (OnDeletePC): BeginDelete (CharacterManagement 2). A low-level character goes at once (CharacterList DeleteEntry, error 0: the entry leaves the list);
    /// otherwise the server answers BeginDelete with the deadline. Returns the reply, null when the request failed. A refusal (error != 0) is logged and the list stays.</summary>
    public async Task<CharacterListMessage> DeleteAsync(CharacterEntry entry)
    {
        Busy = true;
        try
        {
            long id = entry.User.CharacterId;
            var m = await new LoginClient(login).ManageAsync(CharacterIdCommand.BeginDelete, id);
            if (m.Command == CharacterListCommand.DeleteEntry && m.Error == 0)
            {
                Session.Characters.RemoveAll(c => c.User.CharacterId == id);
                if (Session.SelectedIndex >= Session.Characters.Count) Session.SelectedIndex = Math.Max(0, Session.Characters.Count - 1);
                GD.Print($"live: character deleted: {entry.User.Name} id {id}");
            }
            else if (m.Command == CharacterListCommand.BeginDelete && m.Error == 0) GD.Print($"live: delete pending for {entry.User.Name}, until unix {m.DeleteTime}");
            else GD.PushWarning($"live: delete {m.Command} error {m.Error} (time {m.DeleteTime})");
            return m;
        }
        catch (Exception ex) { GD.PushWarning("live: delete failed: " + ex.Message); return null; }
        finally { Busy = false; }
    }

    static string Describe(ItemMessage m) => m switch {
        ItemMessage.LoadTab t => $"LoadTab {t.Tab} x{t.Items.Count} [{string.Join(", ", t.Items.Select(i => $"{i.Id}@{i.InventorySlot}x{i.Amount}"))}]",
        ItemMessage.Load l => $"Load x{l.Items.Count} [{string.Join(", ", l.Items.Select(i => $"{i.Id}@{i.InventorySlot}x{i.Amount}"))}]",
        _ => m.GetType().Name };

    void ApplyItems(Ms2.Net.Wire.RawPacket p) {
        if (System.Environment.GetEnvironmentVariable("MS2_LIVE_DUMP") == "1") GD.Print("live: item raw " + Convert.ToHexString(p.Body));
        try { var r = p.Reader(); var m = ItemMessage.Read(r, Items.Get); Items.Apply(m); GD.Print("live: item " + Describe(m) + (r.Available != 0 ? $" !! {r.Available} bytes left" : "")); }
        catch (Exception e) { GD.PushWarning($"live: item packet: {e.Message}"); }
    }

    /// <summary>The first model slot of an item for the body layout; Unknown when the item data is missing.</summary>
    static Func<int, EquipSlot> ItemSlotTable() {
        ItemModelSlots slots = null;
        return id => {
            try { return (slots ??= new ItemModelSlots(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir))).SlotOf(id); }
            catch (Exception e) { GD.Print($"live: item model table unavailable ({e.Message})"); return EquipSlot.Unknown; }
        };
    }

    static ItemKinds kinds;
    static ItemKinds Kinds() => kinds ??= new ItemKinds(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir));

    /// <summary>The quest table columns for QuestBook (questType, repeatable, groupID), read once from the quest XML; no lookup when the data is missing.</summary>
    static Func<int, QuestBook.QuestInfoRow?> QuestTable() {
        System.Collections.Generic.Dictionary<int, (int Type, int Repeatable, int GroupId)> rows = null;
        return id => {
            try { rows ??= Ms2.Quest.QuestKinds.Load(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir)); }
            catch (Exception e) { GD.Print($"live: quest table unavailable ({e.Message})"); rows = new(); }
            return rows.TryGetValue(id, out var r) ? new QuestBook.QuestInfoRow(r.Type, r.Repeatable, r.GroupId) : null;
        };
    }

    /// <summary>Select the character and migrate to the game server, up to RequestFieldEnter. The chosen character's map becomes the server's.</summary>
    public async Task<bool> EnterAsync(CharacterEntry chosen)
    {
        Busy = true;
        try
        {
            var go = await GameClient.SelectAsync(login, chosen.User.CharacterId);
            (entry, game) = await GameClient.EnterAsync(go, accountId, machine, host);
            Quests.QuestInfo = QuestTable();
            ItemReader.SlotOf ??= ItemSlotTable();
            ItemReader.ExtraOf ??= id => { try { return Kinds().ExtraOf(id); } catch (Exception e) { GD.Print($"live: item kinds unavailable ({e.Message})"); return null; } };
            ItemReader.Known ??= id => { try { return Kinds().Known(id); } catch { return true; } };   // data unavailable: read every id
            Items.TabOf = id => { try { return Kinds().TabOf(id); } catch { return 7; } };
            Items.MaxStackOf = id => { try { return Kinds().SlotMax(id); } catch { return uint.MaxValue; } };
            foreach (var qp in entry.EarlyQuests) Quests.Apply(QuestMessage.Read(qp.Reader()));   // the quest states the server sent before the field enter
            GD.Print($"live: {Quests.All.Count} quests loaded before the field");
            foreach (var ip in entry.EarlyItems) ApplyItems(ip);
            GD.Print($"live: {Items.All.Count} items loaded before the field ({entry.EarlyItems.Count} packets)");
            login.Dispose(); login = null;
            answered = false;
            syncNumber = entry.Sync?.Number ?? 0;
            var f = entry.Field;
            chosen.User.MapA = f.MapId;
            GD.Print($"live: RequestFieldEnter map {f.MapId} pos {f.Position} rot {f.Rotation} fieldKey {f.FieldKey}");
            return true;
        }
        catch (Exception ex) { GD.PushWarning("live: enter failed: " + ex.Message); return false; }
        finally { Busy = false; }
    }

    /// <summary>The server's arrival (position, rotation, field key) on the move the flow built for the chosen map.</summary>
    public static Travel.Move Decorate(Travel.Move m)
    {
        var f = Instance?.entry?.Field;
        if (m == null || f == null) return m;
        return m with { At = new Vec3(f.Position.X, f.Position.Y, f.Position.Z), RotZDeg = f.Rotation.Z, FieldKey = f.FieldKey };
    }

    void OnFieldBuilt(Field field, ModelBuilder builder)
    {
        if (entry == null) return;
        remote = new RemoteLayer(field, builder, entry.Enter.CharacterId);
        remote.Tracker.Profile = game.Profile;
        field.TreeExiting += () => { remote = null; };   // the field is freed: its tracker stops filling Events
        GD.Print("live: remote layer ready");
    }

    /// <summary>The own Stat (0x2E; CSceneStateGame::OnStat, scenestategame.cpp:9424): health, spirit and stamina reach the HUD data.</summary>
    void ApplyStat(PlayerStat st)
    {
        var hud = StubHudData.Instance;
        if (st.TryGet(PlayerStat.Health, out long hm, out long hc)) { hud.Player.HpMax = (int)hm; hud.Player.HpCurrent = (int)hc; }
        if (st.TryGet(PlayerStat.Spirit, out long sm, out long sc)) { hud.SpMax = (int)sm; hud.SpCurrent = (int)sc; }
        if (st.TryGet(PlayerStat.Stamina, out long em, out long ec)) { hud.EpMax = (int)em; hud.EpCurrent = (int)ec; }
        GD.Print($"live: Stat hp {hud.Player.HpCurrent}/{hud.Player.HpMax} sp {hud.SpCurrent}/{hud.SpMax} ep {hud.EpCurrent}/{hud.EpMax} ({st.Values.Count} attribute(s))");
    }

    /// <summary>The field is built and shown: ResponseFieldEnter(field key) goes out. The client sends it when the map's entities are all created
    /// (CubeMapInformationService::FUN_1410cf5c0, cubemapinformationservice_3.cpp:5701-5705: the pending entity set is empty, then
    /// COutPacket ResponseFieldEnter with the world's field key).</summary>
    public void FieldLoaded()
    {
        if (entry == null || answered) return;
        answered = true;
        Send(new ResponseFieldEnter(entry.Field.FieldKey));
        BindPlayer();
        GD.Print($"live: ResponseFieldEnter sent, fieldKey {entry.Field.FieldKey}");
    }

    /// <summary>Movement out: the player's record packets and state changes become UserSync (0x12) and State (0x80); its offline
    /// server emulation turns off. The cadence and content are the player's own (PlayerFallReturn.StateSync), not a second copy.</summary>
    /// <summary>Fire-and-forget send whose failure is logged (an unmapped op under the profile, a closed socket) instead of going unobserved.</summary>
    void Send(Ms2.Net.Wire.IOutPacket pk)
    {
        var g = game;
        if (g == null) return;
        g.SendAsync(pk).ContinueWith(t => GD.PushWarning($"live: send {pk.GetType().Name} failed: {t.Exception?.GetBaseException().Message}"),
            System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
    }

    void BindPlayer()
    {
        player = GetTree().Root.FindChild("Player", true, false) as Player;
        if (player == null) { GD.PushWarning("live: no player to send movement for"); return; }
        player.Live = true;
        player.ServerTick = () => game?.Session.ServerTick ?? 0;
        player.StateSyncOut += records =>
        {
            if (game == null) return;
            var pk = new UserSyncOut { SyncNumber = syncNumber, ServerTick = game.Session.ServerTick, ClientTick = game.Session.ElapsedMs };   // state.cpp:491-493: GetServerTick, then GetElapsedTime
            foreach (var r in records) pk.Records.Add(r);
            Send(pk);
            var l = records[^1].Record;
            GD.Print($"live: UserSync sent, {records.Count} record(s), last state {l.State} pos ({l.Position.X},{l.Position.Y},{l.Position.Z}) sync {l.SyncNumber}");
        };
        player.StateChangeOut += (mode, arg, pos) =>
        {
            if (game == null) return;
            Send(new StateChange(mode, arg, game.Session.ServerTick, new System.Numerics.Vector3(pos.X, pos.Y, pos.Z)));   // mypc.cpp:3661: GetServerTick
            GD.Print($"live: State sent, mode {mode} arg {arg}");
        };
    }

    public override void _Process(double delta)
    {
        if (game == null) return;
        while (game.Incoming.TryDequeue(out var p))
        {
            try
            {
                bool own = game.Is(p, Inbound.Stat) && p.Body.Length >= 4 && BitConverter.ToInt32(p.Body, 0) == selfObject;
                if (own) ApplyStat(PlayerStat.Read(p.Reader()));
                else remote?.Tracker.Inbound(p);     // others' users and npcs (FieldTracker keeps the rest)
                if (own) { }
                else if (game.Is(p, Inbound.FieldAddUser))
                {
                    var u = FieldAddUser.Read(p.Reader());
                    bool self = u.User.CharacterId == entry.Enter.CharacterId;
                    if (self) selfObject = u.ObjectId;
                    GD.Print($"live: FieldAddUser object {u.ObjectId} {(self ? "SELF" : "other")} char {u.User.CharacterId} '{u.User.Name}' pos {u.Position}");
                }
                else if (game.Is(p, Inbound.SyncNumber))
                {
                    syncNumber = SyncNumber.Read(p.Reader()).Number;
                    player?.OnSyncNumber();
                    GD.Print($"live: SyncNumber {syncNumber}");
                }
                else if (game.Is(p, Inbound.UserMoveByPortal))
                {
                    var m = UserMoveByPortal.Read(p.Reader());
                    GD.Print($"live: MoveByPortal object {m.ObjectId} pos {m.Position} rot {m.Rotation} portal {m.IsPortal}");
                    if (m.ObjectId == selfObject) player?.ServerMoveByPortal(new Vector3(m.Position.X, m.Position.Y, m.Position.Z), m.Rotation.Z, m.IsPortal);
                }
                else if (game.Is(p, Inbound.Quest))
                {
                    var q = QuestMessage.Read(p.Reader());   // CQuestManager::OnQuest (questmanager_2.cpp:942); no ExpectEnd: the client ignores trailing bytes
                    Quests.Apply(q);
                    GD.Print($"live: quest {q.GetType().Name} ({Quests.All.Count} quests)");
                }
                else if (game.Is(p, Inbound.ItemInventory))
                {
                    ApplyItems(p);
                    GD.Print($"live: item packet ({Items.All.Count} items)");
                }
                else if (game.Is(p, Inbound.ItemPutOnOrOff))
                {
                    var o = ItemPutOnOrOff.Read(p.Reader());   // CSceneStateGame::OnItemPutOnOrOff (scenestategame.cpp:2575); read only, no c2s answer
                    GD.Print($"live: item put on/off {o}");
                }
                else if (game.Is(p, Inbound.GlobalFactor))
                {
                    bool first = Factors.Apply(p.Reader());   // CSceneStateGame::OnGlobalFactor (scenestategame.cpp:4851): taken once; the quest reward rescale (QuestRates) reads it
                    GD.Print($"live: global factor {(first ? "applied" : "ignored (already taken)")}");
                }
                else GD.Print($"live: packet 0x{p.Opcode:X4} ({p.Body.Length} bytes)");
            }
            catch (Exception ex) { GD.PushWarning($"live: packet 0x{p.Opcode:X4}: {ex.Message}"); }
        }
        if (game.IsClosed) { GD.PushWarning("live: game connection closed" + (game.Failure != null ? ": " + game.Failure.Message : "")); game.Dispose(); game = null; if (player != null) player.Live = false; }   // offline fall-return emulation comes back
    }

    /// <summary>Headless drive (MS2_LIVE_AUTO=1): the login button, then start with the first character.</summary>
    public static async Task AutoDrive(GameFlow flow)
    {
        if (System.Environment.GetEnvironmentVariable("MS2_LIVE_AUTO") != "1") return;
        var tree = flow.GetTree();
        async Task Frames(int n) { for (int i = 0; i < n; i++) await flow.ToSignal(tree, SceneTree.SignalName.ProcessFrame); }
        async Task Until(Func<bool> c, int max = 6000) { for (int i = 0; i < max && !c(); i++) await Frames(1); }
        await Until(() => tree.CurrentScene?.SceneFilePath == GameFlow.LoginScene); await Frames(30);
        UiBridge.Raise(UILoginDialogContract.Movie, UILoginDialogContract.ToHost.OnConnect);
        await Until(() => flow.Current == GameFlow.State.Lobby && tree.CurrentScene?.SceneFilePath == GameFlow.LobbyScene, 600); await Frames(30);
        if (flow.Current != GameFlow.State.Lobby) { GD.PushWarning("live: no character list"); tree.Quit(1); return; }
        string newName = System.Environment.GetEnvironmentVariable("MS2_LIVE_CREATE");   // create this character first (slice 6), then enter with it
        if (!string.IsNullOrEmpty(newName))
        {
            UiBridge.Raise(UICharacterListDialogContract.Movie, UICharacterListDialogContract.ToHost.OnCreatePC);
            await Until(() => flow.Current == GameFlow.State.Create && tree.CurrentScene?.SceneFilePath == GameFlow.CreateScene, 600); await Frames(120);
            int before = Session.List().Count;
            Ms2Godot.Ui.Create.CharacterCreateScene.PickJobScripted();
            await Frames(10); await Until(() => Ms2Godot.Ui.Create.CharacterCreateScene.OnCreatePage, 600); await Frames(60);
            Ms2Godot.Ui.Create.CharacterCreateScene.ConfirmScripted(newName);
            await Until(() => flow.Current == GameFlow.State.Lobby && Session.List().Count > before, 600); await Frames(60);
            if (Session.List().Count <= before) { GD.PushWarning("live: create did not add a character"); tree.Quit(1); return; }
        }
        if (System.Environment.GetEnvironmentVariable("MS2_LIVE_DELETE") == "1")   // delete the last character through the dialog's OnDeletePC, then stop
        {
            int n0 = Session.List().Count;
            Session.SelectedIndex = n0 - 1;
            UiBridge.Raise(UICharacterListDialogContract.Movie, UICharacterListDialogContract.ToHost.OnDeletePC);
            await Until(() => Session.List().Count < n0, 600); await Frames(60);
            bool gone = Session.List().Count == n0 - 1 && flow.Current == GameFlow.State.Lobby;
            GD.Print(gone ? $"live: delete run done, {n0} -> {Session.List().Count}" : "live: delete run failed");
            tree.Quit(gone ? 0 : 1); return;
        }
        string pick = System.Environment.GetEnvironmentVariable("MS2_LIVE_CHAR");   // enter with this character name (default: the first)
        var first = newName != null && newName != "" ? Session.List().Last() : !string.IsNullOrEmpty(pick) ? Session.List().FirstOrDefault(c => c.User.Name == pick) : Session.List().FirstOrDefault();
        if (first == null) { GD.PushWarning("live: no character"); tree.Quit(1); return; }
        UiBridge.Raise(UICharacterListDialogContract.Movie, UICharacterListDialogContract.ToHost.OnGameStart, first.User.CharacterId.ToString());
        await Until(() => flow.Current == GameFlow.State.Game);
        // MS2_LIVE_HOLD: seconds to stay in the field (a movement script runs meanwhile)
        int hold = int.TryParse(System.Environment.GetEnvironmentVariable("MS2_LIVE_HOLD"), out var hs) ? hs : 2;
        await Frames(60 * hold);
        bool ok = flow.Current == GameFlow.State.Game;
        GD.Print(ok ? "live: auto run done" : "live: auto run failed");
        tree.Quit(ok ? 0 : 1);
    }
}
