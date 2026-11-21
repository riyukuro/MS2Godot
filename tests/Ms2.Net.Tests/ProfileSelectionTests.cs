using Maple2.PacketLib.Crypto;
using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The channel takes its protocol profile from the server hello's version.</summary>
public class ProfileSelectionTests {
    /// <summary>A server hello (plain frame) of the given version, built like the Maple2 server's handshake: op 1, version, riv, siv, block iv, patch type.</summary>
    static byte[] Hello(uint version, uint siv = 1, uint riv = 2, uint block = 23) {
        var send = new MapleCipher.Encryptor(version, siv, block);
        var w = new ByteWriter();
        w.WriteShort(1); w.WriteInt((int) version); w.WriteInt((int) riv); w.WriteInt((int) siv); w.WriteInt((int) block); w.WriteByte(2);
        var f = send.WriteHeader(w.Buffer, 0, w.Length);
        return f.Buffer.AsSpan(0, f.Length).ToArray();
    }

    static MapleChannel Channel(byte[] hello) { var io = new MemoryStream(); io.Write(hello); io.Position = 0; return new MapleChannel(io); }

    [Test]
    public async Task Version12SelectsGms2() {
        var ch = Channel(Hello(12));
        var hs = await ch.ReadHandshakeAsync();
        Assert.That(hs.Version, Is.EqualTo(12u));
        Assert.That(ch.Profile, Is.SameAs(ProtocolProfile.Gms2));
    }

    [Test]
    public async Task Version2525SelectsKms() {
        var ch = Channel(Hello(2525));
        await ch.ReadHandshakeAsync();
        Assert.That(ch.Profile, Is.SameAs(ProtocolProfile.Kms2525));
    }

    [Test]
    public void UnknownVersionIsRefusedWithItsNumber() {
        var ch = Channel(Hello(99));
        var e = Assert.ThrowsAsync<InvalidDataException>(async () => await ch.ReadHandshakeAsync());
        Assert.That(e.Message, Does.Contain("99").And.Contain("not supported"));
    }

    [Test]
    public async Task AProfileSetBeforeTheHelloIsKept() {
        var ch = Channel(Hello(99));
        ch.Profile = ProtocolProfile.Kms2525;
        await ch.ReadHandshakeAsync();
        Assert.That(ch.Profile, Is.SameAs(ProtocolProfile.Kms2525));
    }

    [Test]
    public async Task Gms2SessionAnswersWithVersion12AndTheProfileNumbers() {
        var io = new MemoryStream();
        io.Write(Hello(12));
        io.Position = 0;
        var ch = new MapleChannel(io);
        var session = new ClientSession(ch);
        using var cts = new CancellationTokenSource(500);
        long start = io.Length;
        try { await session.RunAsync(cts.Token); } catch (Exception) { }   // the stream ends after the hello
        Assert.That(ch.Profile, Is.SameAs(ProtocolProfile.Gms2));
        var sent = io.ToArray().AsSpan((int) start).ToArray();
        var dec = new MapleCipher.Decryptor(12, 2, 23);
        Assert.That(dec.TryDecrypt(new System.Buffers.ReadOnlySequence<byte>(sent), out var r), Is.EqualTo(sent.Length));
        using (r) {
            Assert.That((ushort) r.ReadShort(), Is.EqualTo(ProtocolProfile.Gms2.Number(Outbound.ResponseVersion)));
            Assert.That(r.ReadUInt(), Is.EqualTo(12u));
        }
    }
}
