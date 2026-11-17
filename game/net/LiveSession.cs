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
            GD.Print($"live: logged in, {o.Characters.Count} character(s): {string.Join(", ", o.Characters.Select(c => c.User.Name))}");
            return true;
        }
        catch (Exception ex) { GD.PushWarning("live: login failed: " + ex.Message); return false; }
        finally { Busy = false; }
    }

    /// <summary>Select the character and migrate to the game server, up to RequestFieldEnter. The chosen character's map becomes the server's.</summary>
    public async Task<bool> EnterAsync(CharacterEntry chosen)
    {
        Busy = true;
        try
        {
            var go = await GameClient.SelectAsync(login, chosen.User.CharacterId);
            (entry, game) = await GameClient.EnterAsync(go, accountId, machine, host);
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
        _ = game.SendAsync(new ResponseFieldEnter(entry.Field.FieldKey));
        BindPlayer();
        GD.Print($"live: ResponseFieldEnter sent, fieldKey {entry.Field.FieldKey}");
    }

    /// <summary>Movement out: the player's record packets and state changes become UserSync (0x12) and State (0x80); its offline
    /// server emulation turns off. The cadence and content are the player's own (PlayerFallReturn.StateSync), not a second copy.</summary>
    void BindPlayer()
    {
        player = GetTree().Root.FindChild("Player", true, false) as Player;
        if (player == null) { GD.PushWarning("live: no player to send movement for"); return; }
        player.Live = true;
        player.StateSyncOut += records =>
        {
            if (game == null) return;
            var pk = new UserSyncOut { SyncNumber = syncNumber, ServerTick = game.Session.ElapsedMs, ClientTick = game.Session.ElapsedMs };
            int t = pk.ClientTick;
            foreach (var r in records) pk.Records.Add((r, t));
            _ = game.SendAsync(pk);
            var l = records[^1];
            GD.Print($"live: UserSync sent, {records.Count} record(s), last state {l.State} pos ({l.Position.X},{l.Position.Y},{l.Position.Z}) sync {l.SyncNumber}");
        };
        player.StateChangeOut += (mode, arg, pos) =>
        {
            if (game == null) return;
            _ = game.SendAsync(new StateChange(mode, arg, game.Session.ElapsedMs, new System.Numerics.Vector3(pos.X, pos.Y, pos.Z)));
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
                bool own = p.Opcode == (ushort)Inbound.Stat && p.Body.Length >= 4 && BitConverter.ToInt32(p.Body, 0) == selfObject;
                if (own) ApplyStat(PlayerStat.Read(p.Reader()));
                else remote?.Tracker.Inbound(p);     // others' users and npcs (FieldTracker keeps the rest)
                if (own) { }
                else if (p.Opcode == (ushort)Inbound.FieldAddUser)
                {
                    var u = FieldAddUser.Read(p.Reader());
                    bool self = u.User.CharacterId == entry.Enter.CharacterId;
                    if (self) selfObject = u.ObjectId;
                    GD.Print($"live: FieldAddUser object {u.ObjectId} {(self ? "SELF" : "other")} char {u.User.CharacterId} '{u.User.Name}' pos {u.Position}");
                }
                else if (p.Opcode == (ushort)Inbound.SyncNumber)
                {
                    syncNumber = SyncNumber.Read(p.Reader()).Number;
                    player?.OnSyncNumber();
                    GD.Print($"live: SyncNumber {syncNumber}");
                }
                else if (p.Opcode == (ushort)Inbound.UserMoveByPortal)
                {
                    var m = UserMoveByPortal.Read(p.Reader());
                    GD.Print($"live: MoveByPortal object {m.ObjectId} pos {m.Position} rot {m.Rotation} portal {m.IsPortal}");
                    if (m.ObjectId == selfObject) player?.ServerMoveByPortal(new Vector3(m.Position.X, m.Position.Y, m.Position.Z), m.Rotation.Z);
                }
                else GD.Print($"live: packet 0x{p.Opcode:X4} ({p.Body.Length} bytes)");
            }
            catch (Exception ex) { GD.PushWarning($"live: packet 0x{p.Opcode:X4}: {ex.Message}"); }
        }
        if (game.IsClosed) { GD.PushWarning("live: game connection closed"); game.Dispose(); game = null; }
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
        var first = Session.List().FirstOrDefault();
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
