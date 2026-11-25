using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// FieldPickupItem (0x2D on KMS2 and GMS2): CSceneStateGame::OnFieldPickupItem (scenestategame.cpp:2420).
/// Decodes item, meso, EP-ball and refusal frames from both regions and verifies the handler helpers
/// (<see cref="FieldPickupItem.MesoShown"/>, <see cref="FieldPickupItem.EpGauge"/>, refusal mode and unknown-drop guard).
/// </summary>
public class FieldPickupItemRecordedTests {
    // gms2: 0x2D item pickup (9 bytes), meso pickup (17 bytes), EP-ball pickup (13 bytes), refusal mode 1 (9 bytes)
    static readonly byte[] Gms2Item = Convert.FromHexString("01CB4C2600F24B2600");
    static readonly byte[] Gms2Meso = Convert.FromHexString("01C94C2600F24B26001A00000000000000");
    static readonly byte[] Gms2Ep = Convert.FromHexString("012E8D3700E289370028000000");
    static readonly byte[] Gms2Refused = Convert.FromHexString("00EA94370001000000");

    // kms2: 0x2D item pickup (9 bytes), meso pickup (17 bytes), EP-ball pickup (13 bytes), refusal mode 1 (9 bytes)
    static readonly byte[] KmsItem = Convert.FromHexString("01388837000B833700");
    static readonly byte[] KmsMeso = Convert.FromHexString("016AD63600AACE36005C00000000000000");
    static readonly byte[] KmsEp = Convert.FromHexString("01B9283600D50A360028000000");
    static readonly byte[] KmsRefused = Convert.FromHexString("00CD27360001000000");

    static Func<int, (bool Known, bool IsMeso, bool IsEp)> DescribeFrom(Dictionary<int, (bool IsMeso, bool IsEp)> drops) =>
        id => drops.TryGetValue(id, out var d) ? (true, d.IsMeso, d.IsEp) : (false, false, false);

    [Test] public void Gms2ItemMesoEpAndRefusalDecodeAndApplyEffects() {
        var drops = new Dictionary<int, (bool IsMeso, bool IsEp)> {
            [2510027] = (false, false),
            [2510025] = (true, false),
            [3640622] = (false, true),
            [3642602] = (false, false),
        };
        var describe = DescribeFrom(drops);

        var ri = new ByteReader(Gms2Item);
        var item = FieldPickupItem.Read(ri, describe);
        Assert.That((item.DropKnown, item.Picked, item.ObjectId, item.PickerId), Is.EqualTo((true, true, 2510027, 2509810)));
        Assert.That(item.MesoShown(added: 10, isMeso: drops[item.ObjectId].IsMeso), Is.EqualTo(10L));
        Assert.That(ri.Available, Is.EqualTo(0));

        var rm = new ByteReader(Gms2Meso);
        var meso = FieldPickupItem.Read(rm, describe);
        Assert.That((meso.DropKnown, meso.Picked, meso.ObjectId, meso.PickerId, meso.Meso),
            Is.EqualTo((true, true, 2510025, 2509810, 26L)));
        Assert.That(meso.MesoShown(added: 10, isMeso: drops[meso.ObjectId].IsMeso), Is.EqualTo(meso.Meso));
        Assert.That(rm.Available, Is.EqualTo(0));

        var re = new ByteReader(Gms2Ep);
        var ep = FieldPickupItem.Read(re, describe);
        Assert.That((ep.DropKnown, ep.Picked, ep.EpBall, ep.ObjectId, ep.PickerId, ep.Ep),
            Is.EqualTo((true, true, true, 3640622, 3639778, 40)));
        Assert.That(ep.EpGauge(current: 80, total: 120), Is.EqualTo((80L + ep.Ep, 120L)));
        Assert.That(re.Available, Is.EqualTo(0));

        var rf = new ByteReader(Gms2Refused);
        var refused = FieldPickupItem.Read(rf, describe);
        Assert.That((refused.DropKnown, refused.Picked, refused.ObjectId, refused.Mode),
            Is.EqualTo((true, false, 3642602, 1)));
        Assert.That(refused.MesoShown(added: 10, isMeso: true), Is.EqualTo(10L));
        Assert.That(rf.Available, Is.EqualTo(0));

        // Unknown drop: client stops after object id and leaves the rest unread
        var ru = new ByteReader(Gms2Meso);
        var unk = FieldPickupItem.Read(ru, _ => (false, false, false));
        Assert.That((unk.DropKnown, unk.ObjectId), Is.EqualTo((false, meso.ObjectId)));
        Assert.That(ru.Available, Is.GreaterThan(0));
    }

    [Test] public void KmsItemMesoEpAndRefusalDecodeAndApplyEffects() {
        var drops = new Dictionary<int, (bool IsMeso, bool IsEp)> {
            [3639352] = (false, false),
            [3593834] = (true, false),
            [3549369] = (false, true),
            [3549133] = (false, false),
        };
        var describe = DescribeFrom(drops);

        var ri = new ByteReader(KmsItem);
        var item = FieldPickupItem.Read(ri, describe);
        Assert.That((item.DropKnown, item.Picked, item.ObjectId, item.PickerId), Is.EqualTo((true, true, 3639352, 3638027)));
        Assert.That(item.MesoShown(added: 50, isMeso: drops[item.ObjectId].IsMeso), Is.EqualTo(50L));
        Assert.That(ri.Available, Is.EqualTo(0));

        var rm = new ByteReader(KmsMeso);
        var meso = FieldPickupItem.Read(rm, describe);
        Assert.That((meso.DropKnown, meso.Picked, meso.ObjectId, meso.PickerId, meso.Meso),
            Is.EqualTo((true, true, 3593834, 3591850, 92L)));
        Assert.That(meso.MesoShown(added: 50, isMeso: drops[meso.ObjectId].IsMeso), Is.EqualTo(meso.Meso));
        Assert.That(rm.Available, Is.EqualTo(0));

        var re = new ByteReader(KmsEp);
        var ep = FieldPickupItem.Read(re, describe);
        Assert.That((ep.DropKnown, ep.Picked, ep.EpBall, ep.ObjectId, ep.PickerId, ep.Ep),
            Is.EqualTo((true, true, true, 3549369, 3541717, 40)));
        Assert.That(ep.EpGauge(current: 60, total: 120), Is.EqualTo((60L + ep.Ep, 120L)));
        Assert.That(re.Available, Is.EqualTo(0));

        var rf = new ByteReader(KmsRefused);
        var refused = FieldPickupItem.Read(rf, describe);
        Assert.That((refused.DropKnown, refused.Picked, refused.ObjectId, refused.Mode),
            Is.EqualTo((true, false, 3549133, 1)));
        Assert.That(refused.MesoShown(added: 50, isMeso: true), Is.EqualTo(50L));
        Assert.That(rf.Available, Is.EqualTo(0));
    }
}
