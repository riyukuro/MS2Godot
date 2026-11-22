using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>GMS2 BadgeEquip: Outbound 0x48 modes 0 (equip), 1 (unequip), 3 (transparency setting) and Inbound 0x81 modes 0, 1, 4, checked against the recordings.</summary>
public class Gms2BadgeEquipTests {
    static byte[] Bytes(IOutPacket p) { var w = new ByteWriter(); p.Write(w); return w.Buffer.AsSpan(0, w.Length).ToArray(); }

    [Test]
    public void OpcodesAreTheRecordedOnes() {
        Assert.That(ProtocolProfile.Gms2.Number(Outbound.BadgeEquip), Is.EqualTo(0x48));
        Assert.That(ProtocolProfile.Gms2.Number(Inbound.BadgeEquip), Is.EqualTo(0x81));
    }

    [Test]
    public void RequestsWriteTheirModes() {
        Assert.That(Bytes(new GemUnequipRequest(5)), Is.EqualTo(new byte[] { 1, 5 }));
        var hidden = new bool[10]; hidden[0] = true; hidden[9] = true;
        Assert.That(Bytes(new GemSettingRequest(1, hidden)), Is.EqualTo(new byte[] { 3, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 1 }));
        Assert.That(Bytes(new GemEquipRequest(0x29C194EF24C847FAL, 0xFF)).Length, Is.EqualTo(10));
    }

    [Test]
    public void EveryRecordedGms2RequestIsOneOfThreeModes() {
        var all = Gms2Sniffs.Outbound("gms2", 0x48);
        Assert.That(all, Is.Not.Empty);
        int equip = 0, off = 0, setting = 0;
        foreach (var (file, d) in all) {
            switch (d[0]) {
                case 0: Assert.That(d.Length, Is.EqualTo(10), file); Assert.That(d[9], Is.EqualTo((byte) 0xFF).Or.LessThan((byte) 10), file); equip++; break;
                case 1: Assert.That(d.Length, Is.EqualTo(2), file); Assert.That(Bytes(new GemUnequipRequest(d[1])), Is.EqualTo(d), file); off++; break;
                case 3:
                    Assert.That(d.Length, Is.EqualTo(12), file);
                    var hidden = d.Skip(2).Select(b => b != 0).ToArray();
                    Assert.That(d.Skip(2).All(b => b <= 1), file);
                    Assert.That(Bytes(new GemSettingRequest(d[1], hidden)), Is.EqualTo(d), file); setting++; break;
                default: Assert.Fail($"{file}: unrecorded mode {d[0]}"); break;
            }
        }
        Assert.That(equip + off + setting, Is.EqualTo(all.Count));
    }

    [Test]
    public void EveryRecordedGms2AnswerReadsWhole() {
        var all = Gms2Sniffs.Inbound("gms2", 0x81);
        Assert.That(all, Is.Not.Empty);
        foreach (var (file, d) in all) {
            var r = new ByteReader(d);
            var m = BadgeEquipMessage.Read(r, ProtocolProfile.Gms2);
            Assert.That(m.Mode, Is.AnyOf((byte) 0, (byte) 1, (byte) 4), file);
            Assert.That(r.Available, Is.EqualTo(0), $"{file} mode {m.Mode}: bytes left");
            if (m.Mode == 0) { Assert.That(m.Item, Is.Not.Null, file); Assert.That(m.Item.IsBadge, Is.True, file); }
        }
    }
}
