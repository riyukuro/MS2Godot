using Maple2.PacketLib.Tools;
using Ms2.Net;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The cube packet (FunctionCube, GMS2 0x4E, KMS 0x4D) is read by FunctionCubeReply (CFunctionCubeService::FUN_14122f270,
/// functioncubeobject.cpp:2411). The recorded cube ids are masked in the recordings; only their lengths are real.</summary>
public class RcFunctionCubeTests {
    const string Id8 = "0800" + "2A002A002A002A002A002A002A002A00";   // 8 characters

    static FunctionCubeReply Gms2(string hex, out ByteReader r) {
        r = new ByteReader(RcPkt.Hex(hex));
        return FunctionCubeReply.Read(r, ProtocolProfile.Gms2);
    }

    [Test] public void OneStateRecordIsIdStateFlag() {
        var m = Gms2("03" + Id8 + "01000000" + "01", out var r);   // gms2 recording: 24 bytes
        Assert.That(m.Kind, Is.EqualTo(FunctionCubeKind.State));
        Assert.That(m.States, Is.EqualTo(new[] { new FunctionCubeState("********", 1, true) }));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void StateListReadsEveryRecord() {
        var m = Gms2("02" + "02000000" + Id8 + "00000000" + "00" + Id8 + "01000000" + "01", out var r);
        Assert.That(m.Kind, Is.EqualTo(FunctionCubeKind.States));
        Assert.That(m.States.Select(s => (s.State, s.Flag)), Is.EqualTo(new[] { (0, false), (1, true) }));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void UseIsOwnerIdFlag() {
        var m = Gms2("05" + "5F03363A5D93172C" + Id8 + "01", out var r);   // gms2 recording: 28 bytes
        Assert.That((m.Kind, m.Owner, m.CubeId, m.Flag), Is.EqualTo((FunctionCubeKind.Use, 0x2C17935D3A36035FL, "********", true)));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void LifeSkillSuccessIsOwnerIdTimeCount() {
        var m = Gms2("08" + "D7B110EB11933B28" + Id8 + "D208795E00000000" + "01000000", out var r);   // gms2 recording: 39 bytes
        Assert.That((m.Kind, m.Time, m.Count), Is.EqualTo((FunctionCubeKind.LifeSkillSuccess, 0x5E7908D2L, 1)));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void ACountPastTheBodyIsRefused() {
        Assert.Throws<InvalidDataException>(() => Gms2("02" + "E8030000" + Id8 + "00000000" + "00", out _));
    }

    [Test] public void AGms2ModeTheTableDoesNotNameReadsNothingMore() {
        var m = Gms2("0B0102", out var r);
        Assert.That(m.Kind, Is.EqualTo(FunctionCubeKind.Unknown));
        Assert.That(r.Available, Is.EqualTo(2));
    }

    [Test] public void KmsModesKeepTheirNumbers() {
        foreach (var (mode, kind) in new[] {
            ((byte) 2, FunctionCubeKind.States), ((byte) 3, FunctionCubeKind.State), ((byte) 6, FunctionCubeKind.Use),
            ((byte) 9, FunctionCubeKind.LifeSkillSuccess), ((byte) 10, FunctionCubeKind.LifeSkillFail), ((byte) 0x0D, FunctionCubeKind.NurtureError) })
            Assert.That(FunctionCubeReply.KindOf(mode, ProtocolProfile.Kms2525), Is.EqualTo(kind), $"mode {mode}");
    }

    [Test] public void KmsUseIsMode6() {
        var r = new ByteReader(RcPkt.Hex("06" + "0100000000000000" + Id8 + "00"));
        var m = FunctionCubeReply.Read(r, ProtocolProfile.Kms2525);
        Assert.That((m.Kind, m.Owner, m.Flag), Is.EqualTo((FunctionCubeKind.Use, 1L, false)));
        Assert.That(r.Available, Is.EqualTo(0));
    }

    [Test] public void NurturingRecordCarriesItsFieldsBeforeTheFlag() {
        // gms2 recording: one record of a list (the 30 bytes after the state)
        var m = Gms2WithNurture("03" + Id8 + "01000000" + "A108795E00000000" + "BC02000000000000" + "0300" + "0400" + "0000" + "3CCBCF5B00000000" + "00", out var r);
        var n = m.States[0].Nurture;
        Assert.That(n, Is.EqualTo(new FunctionCubeNurture(0x5E7908A1L, 700, 3, 4, 0, 0x5BCFCB3CL)));
        Assert.That(m.States[0].Flag, Is.False);
        Assert.That(r.Available, Is.EqualTo(0));
    }

    static FunctionCubeReply Gms2WithNurture(string hex, out ByteReader r) {
        r = new ByteReader(RcPkt.Hex(hex));
        return FunctionCubeReply.Read(r, ProtocolProfile.Gms2, (i, id) => true);
    }

    // The recorded ids are masked, so the cube kind cannot be looked up: a record is taken as a nurturing one where the base
    // reading would leave the next id's length implausible and the nurturing reading would not.
    static bool PlausibleIdLength(byte[] b, int p) => p + 2 <= b.Length && b[p + 1] == 0 && b[p] >= 4 && b[p] <= 16;

    static HashSet<int> NurturingIndices(byte[] data) {
        var set = new HashSet<int>();
        int count = BitConverter.ToInt32(data, 1), p = 5;
        for (int i = 0; i < count; i++) {
            int len = BitConverter.ToUInt16(data, p) * 2, start = p + 2 + len + 4;
            bool lastBase = start + 1 == data.Length && i == count - 1;
            if (lastBase || (start + 1 < data.Length && PlausibleIdLength(data, start + 1))) p = start + 1;
            else { set.Add(i); p = start + 1 + 30; }
        }
        return set;
    }

    [Test] public void EveryRecordedGms2PacketReadsToTheEnd() {
        var all = Gms2Sniffs.Inbound("gms2", ProtocolProfile.Gms2.Number(Inbound.FunctionCube));
        Assert.That(all, Is.Not.Empty);
        int nurturing = 0;
        foreach (var (file, data) in all) {
            var r = new ByteReader(data);
            var set = data[0] == 2 ? NurturingIndices(data) : new HashSet<int>();
            nurturing += set.Count;
            var m = FunctionCubeReply.Read(r, ProtocolProfile.Gms2, (i, id) => set.Contains(i));
            Assert.That(m.Kind, Is.Not.EqualTo(FunctionCubeKind.Unknown), file);
            Assert.That(r.Available, Is.EqualTo(0), file);
        }
        Assert.That(nurturing, Is.GreaterThan(0));
    }

    [Test] public void NumberOnBothProfiles() {
        Assert.That(ProtocolProfile.Kms2525.Number(Inbound.FunctionCube), Is.EqualTo((ushort) 0x4D));
        Assert.That(ProtocolProfile.Gms2.Number(Inbound.FunctionCube), Is.EqualTo((ushort) 0x4E));
    }
}
