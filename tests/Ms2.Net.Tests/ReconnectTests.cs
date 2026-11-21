using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The client's reconnect: its packets (0x05, 0x07, 0x08), the resend buffer (COutPacketBuffer) and the manager (CReconnectionManagerCli).</summary>
public class ReconnectTests {
    static byte[] Body(Action<ByteWriter> write) { var w = new ByteWriter(); write(w); return w.Buffer[..w.Length]; }
    static byte[] Bytes(IOutPacket p) => Body(p.Write);

    [Test] public void OpcodesMatchTheClientsTables() {
        Assert.That(new[] { (int) Inbound.Reconnect, (int) Inbound.ReconnectUnk, (int) Inbound.FinalizeReconnect }, Is.EqualTo(new[] { 5, 7, 8 }));
        Assert.That(new[] { (int) Outbound.Reconnect, (int) Outbound.ReconnectUnk, (int) Outbound.FinalizeReconnect }, Is.EqualTo(new[] { 5, 7, 8 }));
    }

    [Test] public void OptionsAreElevenIntsAndTheIndexIsChecked() {
        var o = ReconnectionOptions.Read(new ByteReader(Body(w => { for (int i = 0; i < 11; i++) w.WriteInt(i * 10); })));
        Assert.That(o[0], Is.EqualTo(0)); Assert.That(o[9], Is.EqualTo(90)); Assert.That(o[10], Is.EqualTo(100));
        Assert.That(o[11], Is.EqualTo(0)); Assert.That(o[-1], Is.EqualTo(0));
        Assert.That(Body(o.Write).Length, Is.EqualTo(44));
    }

    [Test] public void TheReconnectRequestWritesTheClientsFields() {
        var ctx = new ReconnectionContext(1, "h", 9);
        var b = Bytes(new ReconnectRequest(0x0102030405060708, 0x1112131415161718, 3, 77, "id", ctx));
        var r = new ByteReader(b);
        Assert.That(r.ReadByte(), Is.EqualTo(1)); Assert.That(r.ReadLong(), Is.EqualTo(0x0102030405060708)); Assert.That(r.ReadLong(), Is.EqualTo(0x1112131415161718));
        Assert.That(r.ReadInt(), Is.EqualTo(3)); Assert.That(r.ReadInt(), Is.EqualTo(77)); Assert.That(r.ReadUnicodeString(), Is.EqualTo("id"));
        Assert.That(ReconnectionContext.Read(r), Is.EqualTo(ctx)); r.ExpectEnd("t");
    }

    [Test] public void TheNoticeKindsReadTheirBodies() {
        var a = ReconnectNotice.Read(new ByteReader(Body(w => { w.WriteByte(0); w.WriteInt(1); w.WriteString("srv"); w.WriteInt(9000); })));
        Assert.That(a.Context, Is.EqualTo(new ReconnectionContext(1, "srv", 9000)));
        Assert.That(ReconnectNotice.Read(new ByteReader(Body(w => { w.WriteByte(2); w.WriteInt(1); }))).Result, Is.EqualTo(1));
        Assert.That(ReconnectNotice.Read(new ByteReader(Body(w => w.WriteByte(3)))).Kind, Is.EqualTo(3));
        Assert.Throws<PacketLayoutException>(() => ReconnectNotice.Read(new ByteReader(Body(w => { w.WriteByte(3); w.WriteByte(0); }))));
    }

    [Test] public void RetransmitReadsTwoIntsAndTheAnswersAreOneInt() {
        var m = RetransmitRequest.Read(new ByteReader(Body(w => { w.WriteInt(9); w.WriteInt(41); })));
        Assert.That(m.Sequence, Is.EqualTo(41));
        Assert.That(Bytes(new ReconnectAck(41)), Is.EqualTo(new byte[] { 41, 0, 0, 0 }));
        Assert.That(Bytes(new FinalizeReconnect(50)), Is.EqualTo(new byte[] { 50, 0, 0, 0 }));
        Assert.That(ReconnectUnk.Read(new ByteReader(new byte[] { 5, 0, 0, 0 })).Unused, Is.EqualTo(5));
    }

    [Test] public void ServerListWithNoServersIsARefusal() {
        var empty = ServerList.Read(new ByteReader(Body(w => { w.WriteString("env"); w.WriteByte(0); })));
        Assert.That(empty.Usable, Is.False);
    }

    // ---- the resend buffer ----

    static OutPacketBuffer Capturing(int limit = 1000) {
        var b = new OutPacketBuffer(); var o = new ReconnectionOptions(); o[0] = 1; o[1] = limit; b.Configure(o); return b;
    }

    [Test] public void ControlPacketsAreNumberedButNeverKept() {
        var b = Capturing();
        b.Capture(6, new byte[] { 1 }); b.Capture(0x20, new byte[] { 2 });
        Assert.That(b.Sequence, Is.EqualTo(2)); Assert.That(b.CapturedCount, Is.EqualTo(1));
        Assert.That(OutPacketBuffer.IsControl(8), Is.True); Assert.That(OutPacketBuffer.IsControl(9), Is.False); Assert.That(OutPacketBuffer.IsControl(12, true), Is.True);
    }

    [Test] public void NothingIsKeptWhenCaptureIsOff() {
        var b = new OutPacketBuffer(); b.Capture(0x20, new byte[] { 1 });
        Assert.That(b.Sequence, Is.EqualTo(1)); Assert.That(b.CapturedCount, Is.EqualTo(0));
    }

    [Test] public void DiscardAckedKeepsTheNewestAcknowledgedCopy() {
        var b = Capturing();
        for (int i = 0; i < 5; i++) b.Capture(0x20, new byte[] { (byte) i });   // sequences 1..5
        b.DiscardAcked(3);
        Assert.That(b.CapturedCount, Is.EqualTo(3));   // 3, 4, 5 remain: 1 and 2 had a successor at or below 3
        b.DiscardAcked(99);
        Assert.That(b.CapturedCount, Is.EqualTo(1));   // the last one is never dropped
    }

    [Test] public void RetransmitSendsTheAckThenTheMissedPacketsRenumberedThenFinalize() {
        var b = Capturing();
        for (int i = 1; i <= 5; i++) b.Capture((ushort) (0x20 + i), new byte[] { (byte) i });
        var sent = new List<(ushort, byte[])>();
        b.Retransmit(3, (t, d) => sent.Add((t, d)));
        Assert.That(sent.Select(s => (int) s.Item1), Is.EqualTo(new[] { 7, 0x24, 0x25, 8 }));
        Assert.That(sent[0].Item2, Is.EqualTo(new byte[] { 3, 0, 0, 0 }));
        Assert.That(sent[3].Item2, Is.EqualTo(new byte[] { 5, 0, 0, 0 }));   // the sequence is untouched, nothing was numbered while resending
        Assert.That(b.Sequence, Is.EqualTo(5));
        Assert.That(b.Capture(0x30, new byte[0]), Is.EqualTo(6));            // numbering is back on afterwards
    }

    [Test] public void SizeCountsTheTwoTypeBytesOfEachPacket() {
        var b = Capturing(limit: 24);
        for (int i = 0; i < 5; i++) b.Capture(0x20, new byte[2]);   // 4 per packet: 20 < 24, nothing shrinks
        b.Shrink(); Assert.That(b.CapturedCount, Is.EqualTo(5));
        b.Capture(0x20, new byte[2]);                                 // 24 reaches the limit, down to 12 = 3 packets
        b.Shrink(); Assert.That(b.CapturedCount, Is.EqualTo(3));
        b.DiscardAcked(99); Assert.That(b.CapturedCount, Is.EqualTo(1));
        b.Capture(0x20, new byte[2]); b.Capture(0x20, new byte[2]); b.Capture(0x20, new byte[2]); b.Capture(0x20, new byte[2]);
        b.Capture(0x20, new byte[2]); b.Shrink();                      // 6 kept = 24: only right if DiscardAcked subtracted 4, not 2
        Assert.That(b.CapturedCount, Is.EqualTo(3));
    }

    [Test] public void NoReconnectWithoutTheNetworkService() {
        var (m, h) = Ready(); h.HasNetworkService = false; h.Log.Clear();
        m.Begin(1); Assert.That(h.Log, Is.Empty); Assert.That(m.TryConnect(), Is.False); Assert.That(h.Log, Is.Empty);
    }

    [Test] public void ShrinkDropsToHalfTheLimitOnceTheLimitIsReached() {
        var b = Capturing(limit: 50);
        for (int i = 0; i < 10; i++) b.Capture(0x20, new byte[3]);   // 5 each with the type
        b.Shrink();
        Assert.That(b.CapturedCount, Is.EqualTo(5));
        b.SetCaptureEnabled(false);
        Assert.That(b.CapturedCount, Is.EqualTo(0));
    }

    [Test] public void SubmitSendsPendingPacketsFirstAndNumbersEachOnce() {
        var b = Capturing();
        b.PushPending(0x21, new byte[] { 1 }); b.PushPending(0x22, new byte[] { 2 });
        var sent = new List<ushort>();
        b.Submit(0x23, new byte[] { 3 }, (t, _) => sent.Add(t));
        Assert.That(sent, Is.EqualTo(new ushort[] { 0x21, 0x22, 0x23 })); Assert.That(b.Sequence, Is.EqualTo(3)); Assert.That(b.PendingCount, Is.EqualTo(0));
    }

    // ---- the manager ----

    sealed class Host : IReconnectHost {
        public uint Now { get; set; } = 100_000; public bool InGame { get; set; } = true; public bool HasConnection { get; set; } = true; public bool HasNetworkService { get; set; } = true;
        public bool SocketDisconnected { get; set; } public bool Closing { get; set; }
        public string SocketHost { get; set; } = "game"; public int SocketPort { get; set; } = 9000; public int SocketSequence { get; set; } = 12;
        public bool IsConnected { get; set; } public bool ConnectResult = true;
        public List<string> Log = new(); public ReconnectRequest Sent;
        public bool Connect(string host, int port) { Log.Add($"connect {host}:{port}"); return ConnectResult; }
        public void Close() { Log.Add("close"); IsConnected = false; }
        public void SendReconnectRequest(ReconnectRequest r) => Sent = r;
        public void ShowProgress(int a, int b) => Log.Add($"show {a}/{b}"); public void HideProgress() => Log.Add("hide");
        public void Notice(string t) { } public void PostAttempt(int c) => Log.Add($"attempt {c}"); public void PostGaveUp(int c) => Log.Add($"gaveup {c:x}");
        public void EndSession() => Log.Add("end");
    }

    static (ReconnectionManager, Host) Ready() {
        var h = new Host(); var m = new ReconnectionManager(h);
        var o = new ReconnectionOptions(); o[0] = 1; o[1] = 500; o[2] = 30; o[7] = 4; o[8] = 5; m.Options.Assign(o);
        m.OnPacket(new ReconnectNotice(0, new ReconnectionContext(1, "srv", 9000), 0));
        return (m, h);
    }

    [Test] public void ShouldReconnectNeedsEveryCondition() {
        var (m, h) = Ready();
        Assert.That(m.ShouldReconnect(0x2745), Is.True);
        Assert.That(m.ShouldReconnect(0), Is.False);
        h.InGame = false; Assert.That(m.ShouldReconnect(1), Is.False); h.InGame = true;
        h.SocketDisconnected = true; Assert.That(m.ShouldReconnect(1), Is.False); h.SocketDisconnected = false;
        h.Closing = true; Assert.That(m.ShouldReconnect(1), Is.False); h.Closing = false;
        m.Options[0] = 0; Assert.That(m.ShouldReconnect(1), Is.False); m.Options[0] = 1;
        m.Reset(); Assert.That(m.ShouldReconnect(1), Is.False);   // the context is gone: mode 0
    }

    [Test] public void BeginPostsAnAttemptShowsProgressAndTimesOutAfterTheGiveUpSeconds() {
        var (m, h) = Ready();
        m.Begin(0x2745);
        Assert.That(h.Log, Is.EqualTo(new[] { "attempt 10053", "show 1/4" }));
        Assert.That(m.IsReconnecting, Is.True); Assert.That(m.IsTimedOut(), Is.False);
        h.Now += 30_001; Assert.That(m.IsTimedOut(), Is.True);
        Assert.That(m.ShouldReconnect(1), Is.False);
    }

    [Test] public void TryConnectCountsAttemptsAndHonoursTheDelay() {
        var (m, h) = Ready();
        m.Begin(1); h.Log.Clear();
        Assert.That(m.TryConnect(), Is.True);
        Assert.That(h.Log, Is.EqualTo(new[] { "show 1/4", "connect game:9000" }));
        m.DisconnectAndDelay(10); h.Log.Clear();
        Assert.That(m.TryConnect(), Is.False);                      // inside the delay: no connect
        Assert.That(h.Log.Any(l => l.StartsWith("connect")), Is.False);
        h.Now += 10_001; Assert.That(m.TryConnect(), Is.True);
        Assert.That(m.Attempt, Is.EqualTo(3));
    }

    [Test] public void AFailureWaitsForTheRetryDelayThenPostsAgainAndAFatalCodeEndsIt() {
        var (m, h) = Ready();
        m.Begin(7); m.TryConnect(); h.Log.Clear();
        m.OnFailed(555);
        Assert.That(m.Pending, Is.True); Assert.That(m.LastError, Is.EqualTo(555));
        h.Now += 1000; m.Tick(); Assert.That(h.Log, Is.Empty);        // 5 s retry delay not over
        h.Now += 5000; m.Tick(); Assert.That(h.Log, Is.EqualTo(new[] { "attempt 7" })); Assert.That(m.Pending, Is.False);
        h.Log.Clear(); h.IsConnected = true;
        m.OnFailed(403);
        Assert.That(h.Log, Is.EqualTo(new[] { "hide", "close", "gaveup 7" }));
        Assert.That(ReconnectionManager.IsFatalError(100) && ReconnectionManager.IsFatalError(408) && !ReconnectionManager.IsFatalError(406), Is.True);
    }

    [Test] public void TickFailsAReconnectThatRanOutOfTimeWithC9AndRunsOncePerSecond() {
        var (m, h) = Ready();
        m.Begin(1); h.Now += 31_000; h.Log.Clear();
        m.Tick(); Assert.That(h.Log.Last(), Is.EqualTo("gaveup 1"));
        Assert.That(m.LastError, Is.EqualTo(0xc9));
        h.Log.Clear(); h.Now += 500; m.Tick(); Assert.That(h.Log, Is.Empty);
    }

    [Test] public void ResultKindsAndTheEndOfTheSession() {
        var (m, h) = Ready();
        m.Begin(1); h.Log.Clear();
        m.OnPacket(new ReconnectNotice(2, null, 1));
        Assert.That(h.Log, Is.EqualTo(new[] { "hide" })); Assert.That(m.IsReconnecting, Is.False);
        m.Begin(1); h.InGame = false; h.Log.Clear();
        m.OnPacket(new ReconnectNotice(2, null, 1));
        Assert.That(m.LastError, Is.EqualTo(0xcc));
        h.InGame = true; h.Log.Clear();
        m.OnPacket(new ReconnectNotice(3, null, 0));
        Assert.That(h.Log, Is.EqualTo(new[] { "end" })); Assert.That(m.Context.Mode, Is.EqualTo(0));
    }

    [Test] public void TheRequestCarriesTheAttemptAndTheSocketSequenceFromBegin() {
        var (m, h) = Ready();
        m.SetIdentity(7, 8, "mid"); m.Begin(1); m.TryConnect(); m.SendRequest();
        Assert.That(h.Sent, Is.EqualTo(new ReconnectRequest(7, 8, 1, 12, "mid", new ReconnectionContext(1, "srv", 9000))));
    }
}
