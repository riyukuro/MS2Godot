using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// FieldPickupItem (0x2D on KMS2 and GMS2): CSceneStateGame::OnFieldPickupItem (scenestategame.cpp:2420).
/// Decodes item, meso, EP-ball and refusal frames from both regions and verifies the handler helpers
/// (<see cref="FieldPickupItem.MesoShown"/>, <see cref="FieldPickupItem.EpGauge"/>, and unknown-drop early return).
/// </summary>
public class FieldPickupItemRecordedTests {
    // gms2 recording: 0x2D item pickup, packet 3008, 9 bytes (extracted with extract.py): object 2510027, picker 2509810
    static readonly byte[] Gms2Item = Convert.FromHexString("01CB4C2600F24B2600");
    // gms2 recording: 0x2D meso pickup, packet 3011, 17 bytes (extracted with extract.py): object 2510025, picker 2509810, meso 26
    static readonly byte[] Gms2Meso = Convert.FromHexString("01C94C2600F24B26001A00000000000000");
    // gms2 recording: 0x2D EP-ball pickup, packet 4894, 13 bytes (extracted with extract.py): object 3640622, picker 3639778, ep 40
    static readonly byte[] Gms2Ep = Convert.FromHexString("012E8D3700E289370028000000");
    // gms2 recording: 0x2D refusal (mode 1), packet 20782, 9 bytes (extracted with extract.py): object 3642602, mode 1
    static readonly byte[] Gms2Refused = Convert.FromHexString("00EA94370001000000");

    // kms2 recording: 0x2D item pickup, packet 1993, 9 bytes (extracted with extract.py): object 3639352, picker 3638027
    static readonly byte[] KmsItem = Convert.FromHexString("01388837000B833700");
    // kms2 recording: 0x2D meso pickup, packet 7960, 17 bytes (extracted with extract.py): object 3593834, picker 3591850, meso 92
    static readonly byte[] KmsMeso = Convert.FromHexString("016AD63600AACE36005C00000000000000");
    // kms2 recording: 0x2D EP-ball pickup, packet 16793, 13 bytes (extracted with extract.py): object 3549369, picker 3541717, ep 40
    static readonly byte[] KmsEp = Convert.FromHexString("01B9283600D50A360028000000");
    // kms2 recording: 0x2D refusal (mode 1), packet 16107, 9 bytes (extracted with extract.py): object 3549133, mode 1
    static readonly byte[] KmsRefused = Convert.FromHexString("00CD27360001000000");

    [Test] public void Gms2ItemMesoEpAndRefusalDecodeAndApplyEffects() {
        var ri = new ByteReader(Gms2Item);
        var item = FieldPickupItem.Read(ri, id => (id == 2510027, false, false));
        Assert.That((item.DropKnown, item.Picked, item.ObjectId, item.PickerId), Is.EqualTo((true, true, 2510027, 2509810)));
        Assert.That(ri.Available, Is.EqualTo(0));

        var rm = new ByteReader(Gms2Meso);
        var meso = FieldPickupItem.Read(rm, id => (id == 2510025, true, false));
        Assert.That((meso.DropKnown, meso.Picked, meso.ObjectId, meso.PickerId, meso.Meso),
            Is.EqualTo((true, true, 2510025, 2509810, 26L)));
        Assert.That(meso.MesoShown(added: 10, isMeso: true), Is.EqualTo(26L));
        Assert.That(rm.Available, Is.EqualTo(0));

        var re = new ByteReader(Gms2Ep);
        var ep = FieldPickupItem.Read(re, id => (id == 3640622, false, true));
        Assert.That((ep.DropKnown, ep.Picked, ep.EpBall, ep.ObjectId, ep.PickerId, ep.Ep),
            Is.EqualTo((true, true, true, 3640622, 3639778, 40)));
        Assert.That(ep.EpGauge(current: 80, total: 120), Is.EqualTo((120L, 120L)));
        Assert.That(re.Available, Is.EqualTo(0));

        var rf = new ByteReader(Gms2Refused);
        var refused = FieldPickupItem.Read(rf, id => (id == 3642602, false, false));
        Assert.That((refused.DropKnown, refused.Picked, refused.ObjectId, refused.Mode),
            Is.EqualTo((true, false, 3642602, 1)));
        Assert.That(rf.Available, Is.EqualTo(0));

        // Unknown drop: client stops after object id and leaves the rest unread
        var ru = new ByteReader(Gms2Meso);
        var unk = FieldPickupItem.Read(ru, _ => (false, false, false));
        Assert.That((unk.DropKnown, unk.ObjectId), Is.EqualTo((false, 2510025)));
        Assert.That(ru.Available, Is.GreaterThan(0));
    }

    [Test] public void KmsItemMesoEpAndRefusalDecodeAndApplyEffects() {
        var ri = new ByteReader(KmsItem);
        var item = FieldPickupItem.Read(ri, id => (id == 3639352, false, false));
        Assert.That((item.DropKnown, item.Picked, item.ObjectId, item.PickerId), Is.EqualTo((true, true, 3639352, 3638027)));
        Assert.That(ri.Available, Is.EqualTo(0));

        var rm = new ByteReader(KmsMeso);
        var meso = FieldPickupItem.Read(rm, id => (id == 3593834, true, false));
        Assert.That((meso.DropKnown, meso.Picked, meso.ObjectId, meso.PickerId, meso.Meso),
            Is.EqualTo((true, true, 3593834, 3591850, 92L)));
        Assert.That(meso.MesoShown(added: 50, isMeso: true), Is.EqualTo(92L));
        Assert.That(rm.Available, Is.EqualTo(0));

        var re = new ByteReader(KmsEp);
        var ep = FieldPickupItem.Read(re, id => (id == 3549369, false, true));
        Assert.That((ep.DropKnown, ep.Picked, ep.EpBall, ep.ObjectId, ep.PickerId, ep.Ep),
            Is.EqualTo((true, true, true, 3549369, 3541717, 40)));
        Assert.That(ep.EpGauge(current: 60, total: 120), Is.EqualTo((100L, 120L)));
        Assert.That(re.Available, Is.EqualTo(0));

        var rf = new ByteReader(KmsRefused);
        var refused = FieldPickupItem.Read(rf, id => (id == 3549133, false, false));
        Assert.That((refused.DropKnown, refused.Picked, refused.ObjectId, refused.Mode),
            Is.EqualTo((true, false, 3549133, 1)));
        Assert.That(rf.Available, Is.EqualTo(0));
    }
}
