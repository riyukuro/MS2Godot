using System.Net;
using System.Net.Sockets;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// A field move in game: after the login's entry the server sends RequestFieldEnter again (a portal, /warp, a dungeon), and
/// the client takes it the same way (CSceneStateGame::OnRequestFieldEnter): the field changes and ResponseFieldEnter goes
/// out once, with the new entry's portal.
/// </summary>
public class FieldMoveTests {
    static async Task<(ushort op, byte[] body)> ReadFrame(NetworkStream s, FakeServer srv) {
        var head = new byte[6]; await s.ReadExactlyAsync(head);
        var frame = new byte[6 + BitConverter.ToInt32(head, 2)]; head.CopyTo(frame, 0);
        await s.ReadExactlyAsync(frame.AsMemory(6));
        return srv.Read(frame);
    }

    static async Task<RawPacket> Dequeue(TcpTransport t) {
        for (int i = 0; i < 500; i++) { if (t.Incoming.TryDequeue(out var p)) return p; await Task.Delay(10); }
        throw new TimeoutException("no packet");
    }

    static Action<ByteWriter> FieldEnter(int map, int portal, byte type = 0) => w => {
        w.WriteByte(0); w.WriteInt(map); w.WriteByte(type); w.WriteByte(0); w.WriteInt(0); w.WriteInt(0);
        w.WriteFloat(1); w.WriteFloat(2); w.WriteFloat(3); w.WriteFloat(0); w.WriteFloat(0); w.WriteFloat(90); w.WriteInt(portal);
    };

    /// <summary>The login up to its RequestFieldEnter (map 2000001, portal 55), as GameClientTests' entry.</summary>
    static async Task<(NetworkStream s, TcpListener l, FakeServer srv, GameEntry e, TcpTransport game)> Enter() {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        int port = ((IPEndPoint) l.LocalEndpoint).Port;
        var srv = new FakeServer(5, 6);
        var accept = l.AcceptTcpClientAsync();
        var go = new LoginToGame(0, new byte[] { 127, 0, 0, 1 }, (ushort) port, 1, 2000001, "");
        var enter = GameClient.EnterAsync(go, 10000000000, new byte[16]);
        var s = (await accept).GetStream();
        await s.WriteAsync(srv.HelloFrame);
        await ReadFrame(s, srv);
        await s.WriteAsync(srv.Frame(Inbound.RequestKey));
        await ReadFrame(s, srv);
        await s.WriteAsync(srv.Frame(Inbound.MoveResult, w => w.WriteByte(0)));
        await s.WriteAsync(srv.Frame(Inbound.ServerEnter, w => {
            w.WriteInt(7); w.WriteLong(20000000000); w.WriteShort(1); w.WriteLong(100); w.WriteLong(50); w.WriteLong(9);
            for (int i = 0; i < 14; i++) w.WriteLong(i);
            w.WriteUnicodeString("pic"); w.WriteByte(0);
            w.WriteShort(1); w.WriteInt(2000001); w.WriteShort(0);
            w.WriteLong(0); w.WriteUnicodeString(""); w.WriteUnicodeString("u1"); w.WriteUnicodeString("u2"); w.WriteUnicodeString("u3");
        }));
        await s.WriteAsync(srv.Frame(Inbound.SyncNumber, w => w.WriteByte(0)));
        await s.WriteAsync(srv.Frame(Inbound.RequestFieldEnter, FieldEnter(2000001, 55)));
        var (e, game) = await enter.WaitAsync(TimeSpan.FromSeconds(10));
        return (s, l, srv, e, game);
    }

    const long Account = 10000000000;   // Enter()'s account: the client's DAT_143ab2820

    static Action<ByteWriter> UgcMap(int mode) => w => { w.WriteInt(mode); w.WriteInt(0); w.WriteByte(0); };

    /// <summary>A LoadCubes mode 0 frame with <paramref name="n"/> cubes (OnLoadCubes, scenestategame.cpp:10857-10897).</summary>
    static Action<ByteWriter> Cubes(int n) => w => {
        w.WriteByte(0); w.WriteByte(0); w.WriteInt(n);
        for (int i = 0; i < n; i++) {
            w.WriteInt(i + 1); w.WriteLong(Account);
            w.WriteInt(50100001); w.WriteLong(9 + i); w.WriteLong(1); w.WriteByte(0);
            w.WriteInt(0); w.WriteInt(0); w.WriteByte(0); w.WriteFloat(0f); w.WriteInt(0); w.WriteByte(0);
        }
    };

    /// <summary>LiveSession's routing of the field-entry packets, the same FieldEntry calls: RequestFieldEnter to
    /// <see cref="FieldEntry.Enter"/> (OnRequestFieldEnter), LoadUgcMap to <see cref="FieldEntry.LoadUgcMap"/> (OnLoadUgcMap)
    /// and LoadCubes to <see cref="FieldEntry.CubesMade"/> with the cubes made (OnLoadCubes; every cube's item exists here, so
    /// each one read is made, as CubeCellMap.Load does). The sends go out through <paramref name="sent"/>.</summary>
    static void Route(TcpTransport game, RawPacket p, FieldEntry entry, List<IOutPacket> sent) {
        if (game.Is(p, Inbound.RequestFieldEnter)) entry.Enter(RequestFieldEnter.Read(p.Reader()));
        else if (game.Is(p, Inbound.LoadUgcMap)) entry.LoadUgcMap(LoadUgcMap.Read(p.Reader(), Account), sent.Add);
        else if (game.Is(p, Inbound.LoadCubes)) entry.CubesMade(LoadCubes.Read(p.Reader()).Cubes.Count, sent.Add);
    }

    /// <summary>The server sends <paramref name="frames"/>; each is routed as LiveSession does, then the sends go out.</summary>
    static async Task Serve(NetworkStream s, FakeServer srv, TcpTransport game, FieldEntry entry, params byte[][] frames) {
        var sent = new List<IOutPacket>();
        foreach (var f in frames) { await s.WriteAsync(f); Route(game, await Dequeue(game), entry, sent); }
        foreach (var p in sent) await game.SendAsync(p);
    }

    /// <summary>LiveSession.FieldLoaded (the field built): <see cref="FieldEntry.FieldLoaded"/>, reported twice as the flow
    /// and the field both report it.</summary>
    static async Task Loaded(TcpTransport game, FieldEntry entry) {
        var sent = new List<IOutPacket>();
        foreach (var _ in new[] { 1, 2 }) entry.FieldLoaded(sent.Add);
        foreach (var p in sent) await game.SendAsync(p);
    }

    /// <summary>The frames the client sent up to the marker (GlobalFactor), as (op, body).</summary>
    static async Task<List<(ushort op, byte[] body)>> UpToMarker(NetworkStream s, FakeServer srv, TcpTransport game) {
        await game.SendAsync(new GlobalFactorRequest());
        var got = new List<(ushort, byte[])>();
        for (var f = await ReadFrame(s, srv); f.op != (ushort) Outbound.GlobalFactor; f = await ReadFrame(s, srv)) got.Add(f);
        return got;
    }

    static (ushort, byte[]) LoadUgcMapAsk(int portal) => ((ushort) Outbound.RequestLoadUgcMap, BitConverter.GetBytes(portal));
    static (ushort, byte[]) Banners(int map) => ((ushort) Outbound.Ugc, new byte[] { 0x12 }.Concat(BitConverter.GetBytes(map)).ToArray());
    static (ushort, byte[]) Answer(int portal) => ((ushort) Outbound.ResponseFieldEnter, BitConverter.GetBytes(portal));

    /// <summary>A field without cubes (the Maple2 server's LoadUgcMap mode is the plot cube count: 0): the field load sends
    /// RequestLoadUgcMap then Ugc 0x12 once (vfunc_1, scenestategame.cpp:1991-2011) and no answer; the answer goes out when
    /// LoadUgcMap mode 0 comes (OnLoadUgcMap :2283-2286), with the entry's portal. The same at login and on a move.</summary>
    [Test] public async Task AFieldWithoutCubesAnswersOnLoadUgcMapModeZero() {
        var (s, l, srv, e, game) = await Enter();
        using (game) {
            var entry = new FieldEntry(e.Field);
            await Loaded(game, entry);
            Assert.That(await UpToMarker(s, srv, game), Is.EqualTo(new[] { LoadUgcMapAsk(55), Banners(2000001) }), "the field load does not answer");
            await Serve(s, srv, game, entry, srv.Frame(Inbound.LoadUgcMap, UgcMap(0)));
            Assert.That(await UpToMarker(s, srv, game), Is.EqualTo(new[] { Answer(55) }));
            Assert.That(entry.Answered, Is.True);

            await Serve(s, srv, game, entry, srv.Frame(Inbound.RequestFieldEnter, FieldEnter(2000062, 77)));
            Assert.That((entry.Current.MapId, entry.Entries, entry.Answered), Is.EqualTo((2000062, 2, false)));
            await Loaded(game, entry);
            Assert.That(await UpToMarker(s, srv, game), Is.EqualTo(new[] { LoadUgcMapAsk(77), Banners(2000062) }));
            await Serve(s, srv, game, entry, srv.Frame(Inbound.LoadUgcMap, UgcMap(0)));
            Assert.That(await UpToMarker(s, srv, game), Is.EqualTo(new[] { Answer(77) }));
        }
        l.Stop();
    }

    /// <summary>A field with cubes: LoadUgcMap of another mode clears the timer flag and sends nothing (:2287-2320); the
    /// answer goes out once the cubes are made (CubeMapInformationService FUN_1410cf5c0, cubemapinformationservice_3.cpp:
    /// 5701-5705), once: the flag is set then, so later cubes send nothing.</summary>
    [Test] public async Task AFieldWithCubesAnswersOnceItsCubesAreMade() {
        var (s, l, srv, e, game) = await Enter();
        using (game) {
            var entry = new FieldEntry(e.Field);
            await Loaded(game, entry);
            await Serve(s, srv, game, entry, srv.Frame(Inbound.LoadUgcMap, UgcMap(2)));
            Assert.That(await UpToMarker(s, srv, game), Is.EqualTo(new[] { LoadUgcMapAsk(55), Banners(2000001) }), "LoadUgcMap with cubes does not answer");
            await Serve(s, srv, game, entry, srv.Frame(Inbound.LoadCubes, Cubes(2)));
            Assert.That(await UpToMarker(s, srv, game), Is.EqualTo(new[] { Answer(55) }));
            await Serve(s, srv, game, entry, srv.Frame(Inbound.LoadCubes, Cubes(1)));
            Assert.That(await UpToMarker(s, srv, game), Is.Empty, "the field entry was answered twice");
        }
        l.Stop();
    }

    /// <summary>A server that never sends LoadUgcMap: the client waits; it has no timeout and no fallback (the two senders
    /// are OnLoadUgcMap and the cube drain). A LoadUgcMap before the field has loaded is dropped (:2174, state 4 only), and
    /// after a mode-0 field the timer flag stays set (no field load resets it), so cubes made then do not answer either.</summary>
    [Test] public async Task WithoutLoadUgcMapTheFieldEntryWaits() {
        var (s, l, srv, e, game) = await Enter();
        using (game) {
            var entry = new FieldEntry(e.Field);
            await Serve(s, srv, game, entry, srv.Frame(Inbound.LoadUgcMap, UgcMap(0)));   // the field still loading
            await Loaded(game, entry);
            Assert.That(await UpToMarker(s, srv, game), Is.EqualTo(new[] { LoadUgcMapAsk(55), Banners(2000001) }));
            Assert.That(entry.Answered, Is.False);
            await Serve(s, srv, game, entry, srv.Frame(Inbound.LoadUgcMap, UgcMap(0)));
            Assert.That(await UpToMarker(s, srv, game), Is.EqualTo(new[] { Answer(55) }));

            await Serve(s, srv, game, entry, srv.Frame(Inbound.RequestFieldEnter, FieldEnter(2000062, 77)));
            await Loaded(game, entry);
            await Serve(s, srv, game, entry, srv.Frame(Inbound.LoadCubes, Cubes(1)));
            Assert.That(await UpToMarker(s, srv, game), Is.EqualTo(new[] { LoadUgcMapAsk(77), Banners(2000062) }), "no LoadUgcMap: no answer");
            Assert.That(entry.Answered, Is.False);
        }
        l.Stop();
    }

    /// <summary>The cube drain needs a clear timer flag only, not a LoadUgcMap: before any LoadUgcMap (the flag starts
    /// false) a cube made in the loaded field answers; a cube made while the field loads does not (FUN_1410cef70 takes
    /// FUN_1410cf160 then, cubemapinformationservice_3.cpp:2374-2377).</summary>
    [Test] public async Task CubesMadeBeforeAnyLoadUgcMapAnswerOnceTheFieldHasLoaded() {
        var (s, l, srv, e, game) = await Enter();
        using (game) {
            var entry = new FieldEntry(e.Field);
            await Serve(s, srv, game, entry, srv.Frame(Inbound.LoadCubes, Cubes(1)));
            await Loaded(game, entry);
            await Serve(s, srv, game, entry, srv.Frame(Inbound.LoadCubes, Cubes(0)), srv.Frame(Inbound.LoadCubes, Cubes(1)));
            Assert.That(await UpToMarker(s, srv, game), Is.EqualTo(new[] { LoadUgcMapAsk(55), Banners(2000001), Answer(55) }));
        }
        l.Stop();
    }

    /// <summary>LoadMap's early return (worldservice_1.cpp:3296-3297): a RequestFieldEnter for the map still loading
    /// (state 2) takes the entry's values but starts no load and keeps the first entry's portal (m_2c0 is set at :3319,
    /// after the check). So the field load sends RequestLoadUgcMap and Ugc 0x12 once, and the answer carries the first
    /// portal.</summary>
    [Test] public async Task TheLoadingMapEnteredAgainSendsTheFieldEnteredSendsOnce() {
        var (s, l, srv, e, game) = await Enter();
        using (game) {
            var entry = new FieldEntry(e.Field);
            await Serve(s, srv, game, entry, srv.Frame(Inbound.RequestFieldEnter, FieldEnter(2000001, 66)));
            Assert.That((entry.LoadingAlready, entry.Loading, entry.Portal, entry.Entries), Is.EqualTo((true, true, 55, 2)));
            await Loaded(game, entry);
            await Serve(s, srv, game, entry, srv.Frame(Inbound.LoadUgcMap, UgcMap(0)));
            Assert.That(await UpToMarker(s, srv, game), Is.EqualTo(new[] { LoadUgcMapAsk(55), Banners(2000001), Answer(55) }));
        }
        l.Stop();
    }

    /// <summary>Once the load is done (state 4) the same map entered again is a new field load (LoadMap past the check):
    /// the new portal, the field-loaded sends again and a new answer.</summary>
    [Test] public async Task TheLoadedMapEnteredAgainLoadsItAgain() {
        var (s, l, srv, e, game) = await Enter();
        using (game) {
            var entry = new FieldEntry(e.Field);
            await Loaded(game, entry);
            await Serve(s, srv, game, entry, srv.Frame(Inbound.LoadUgcMap, UgcMap(0)));
            Assert.That(await UpToMarker(s, srv, game), Is.EqualTo(new[] { LoadUgcMapAsk(55), Banners(2000001), Answer(55) }));
            await Serve(s, srv, game, entry, srv.Frame(Inbound.RequestFieldEnter, FieldEnter(2000001, 66)));
            Assert.That((entry.LoadingAlready, entry.Loading, entry.Answered, entry.Portal), Is.EqualTo((false, true, false, 66)));
            await Loaded(game, entry);
            await Serve(s, srv, game, entry, srv.Frame(Inbound.LoadUgcMap, UgcMap(0)));
            Assert.That(await UpToMarker(s, srv, game), Is.EqualTo(new[] { LoadUgcMapAsk(66), Banners(2000001), Answer(66) }));
        }
        l.Stop();
    }

    [Test] public async Task InGameFieldEnterErrorKeepsTheField() {
        var (s, l, srv, e, game) = await Enter();
        using (game) {
            var entry = new FieldEntry(e.Field);
            await s.WriteAsync(srv.Frame(Inbound.RequestFieldEnter, w => w.WriteByte(0x1d)));
            var p = await Dequeue(game);
            var f = RequestFieldEnter.Read(p.Reader());
            Assert.That(f.Error, Is.EqualTo(0x1d));
            Assert.That(entry.Enter(f), Is.False);
            Assert.That((entry.Current.MapId, entry.Portal, entry.Entries), Is.EqualTo((2000001, 55, 1)));
        }
        l.Stop();
    }

    /// <summary>LoadUgcMap's opcode (commonfiles/opcodes.h:133; the upstream GMS2 SendOp table has 0x7F) and layout
    /// (OnLoadUgcMap :2174-2240): the designs and blueprints are read for the client's own account only.</summary>
    [Test] public void LoadUgcMapLayout() {
        Assert.That((ushort) Inbound.LoadUgcMap, Is.EqualTo(0x7D));
        Assert.That(ProtocolProfile.Gms2.Number(Inbound.LoadUgcMap), Is.EqualTo(0x7F));
        static ByteReader Home(long owner, bool layouts) {
            var w = new ByteWriter();
            w.WriteInt(3); w.WriteInt(2000062); w.WriteByte(1);
            w.WriteLong(owner); w.WriteUnicodeString("n"); w.WriteUnicodeString("m"); w.WriteByte(4);
            w.WriteInt(5); w.WriteInt(6); w.WriteInt(7); w.WriteInt(8); w.WriteByte(9); w.WriteByte(10); w.WriteByte(11);
            w.WriteByte(1); w.WriteByte(2); w.WriteByte(3);
            w.WriteByte(3); w.WriteByte(1); w.WriteByte(4); w.WriteByte(0); w.WriteByte(2);   // only 1 is set (:2214): 0 and 2 carry no value
            if (layouts) {
                w.WriteByte(1); w.WriteInt(21); w.WriteUnicodeString("d"); w.WriteLong(22);
                w.WriteByte(2); w.WriteInt(31); w.WriteUnicodeString("b"); w.WriteLong(32); w.WriteInt(41); w.WriteUnicodeString("c"); w.WriteLong(42);
            }
            return new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
        }
        var m = LoadUgcMap.Read(Home(Account, true), Account);
        var h = m.Home;
        Assert.That((m.Mode, m.MapId, h.Owner, h.HeadByte, h.CurrentArchitectScore, h.ArchitectScore, h.PlotMapId, h.PlotNumber),
            Is.EqualTo((3, 2000062, Account, (byte) 4, 5, 6, 7, 8)));
        Assert.That((h.PlotMode, h.Area, h.Height, h.Background, h.Lighting, h.Camera), Is.EqualTo(((byte) 9, (byte) 10, (byte) 11, (byte) 1, (byte) 2, (byte) 3)));
        Assert.That(h.Permissions, Is.EqualTo(new[] { (true, (byte) 4), (false, (byte) 0), (false, (byte) 0) }));
        Assert.That(h.Designs, Is.EqualTo(new[] { new UgcMapLayout(21, "d", 22) }));
        Assert.That(h.Blueprints, Is.EqualTo(new[] { new UgcMapLayout(31, "b", 32), new UgcMapLayout(41, "c", 42) }));
        Assert.That(LoadUgcMap.Read(Home(Account + 1, false), Account).Home.Designs, Is.Empty, "another owner's house has no layouts");
        var bare = new ByteWriter(); bare.WriteInt(0); bare.WriteInt(0); bare.WriteByte(0);
        Assert.That(LoadUgcMap.Read(new ByteReader(bare.Buffer.AsSpan(0, bare.Length).ToArray()), Account), Is.EqualTo(new LoadUgcMap(0, 0, null)));
    }

    [Test] public void PortalMoveRequestLayout() {
        var w = new ByteWriter();
        new RequestMoveFieldPortal(2000001, 3).Write(w);
        var r = new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
        Assert.That(r.ReadByte(), Is.EqualTo(0));
        Assert.That(r.ReadInt(), Is.EqualTo(2000001)); Assert.That(r.ReadInt(), Is.EqualTo(3));
        Assert.That(r.ReadInt(), Is.EqualTo(0)); Assert.That(r.ReadInt(), Is.EqualTo(0));
        Assert.That(r.ReadUnicodeString(), Is.EqualTo("")); Assert.That(r.ReadUnicodeString(), Is.EqualTo(""));
        Assert.That(w.Length, Is.EqualTo(1 + 16 + 4));
        Assert.That(new RequestMoveFieldPortal(1, 2).Op, Is.EqualTo(Outbound.RequestMoveField));
        // the password portal's password is the second string (+0x18)
        w = new ByteWriter();
        new RequestMoveFieldPortal(1, 2, Password: "pw").Write(w);
        r = new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());
        r.ReadByte(); for (int i = 0; i < 4; i++) r.ReadInt();
        Assert.That(r.ReadUnicodeString(), Is.EqualTo("")); Assert.That(r.ReadUnicodeString(), Is.EqualTo("pw"));
    }
}
