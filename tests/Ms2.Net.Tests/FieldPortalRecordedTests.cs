using System.Numerics;
using Maple2.PacketLib.Tools;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Ui;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>
/// FieldPortal (KMS 0x48, GMS2 0x49): CSceneStateGame::OnFieldPortal (scenestategame.cpp:7227), handled by
/// <see cref="LivePortals.Apply"/> (registers spawned or bound portals, updates flags on mode 2, removes on mode 1).
/// </summary>
public class FieldPortalRecordedTests {
    // gms2: 0x49 mode 0 (Add spawned type 6), 128 bytes (model masked with '*')
    static readonly byte[] Gms2AddSpawned = Convert.FromHexString(
        "00A043F6050101E8A7B9C168034842002083440000000000000000000034C200004843000048420000AF4316002A002A" +
        "002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A002A00AD841E00745026" +
        "000000000000000000000000000006DACD92100000523C8E1000000000000000");
    // gms2: 0x49 mode 1 (Remove), 5 bytes: portal 100025248
    static readonly byte[] Gms2Remove = Convert.FromHexString("01A043F605");
    // gms2: 0x49 mode 0 (Add bound type 0), 84 bytes: portal 1, target 2000001
    static readonly byte[] Gms2AddBound = Convert.FromHexString(
        "00010000000101000016430080BBC400006144000000000000000000003443000048430000484300007A43000081841E" +
        "0080C0060000000000010000000000000000000000000000000000000000000000000000");
    // gms2: 0x49 mode 2 (Update flags), 10 bytes: portal 2, vis/en/mm = true
    static readonly byte[] Gms2Update = Convert.FromHexString("02020000000101010000");

    // kms2: 0x48 mode 0 (Add spawned type 0x0B), 84 bytes: portal 103483385, target 2000062
    static readonly byte[] KmsAddSpawned = Convert.FromHexString(
        "00F9072B06010100404EC500A057C50000964200000000000000000000B4420000164300001643000016430000BE841E" +
        "004260AB00000000000000000000000000000B0000000000000000000000000000000000");
    // kms2: 0x48 mode 1 (Remove), 5 bytes: portal 103483385
    static readonly byte[] KmsRemove = Convert.FromHexString("01F9072B06");
    // kms2: 0x48 mode 2 (Update flags), 10 bytes: portal 1, vis/en/mm = false
    static readonly byte[] KmsUpdate = Convert.FromHexString("02010000000000000000");

    static LivePortals AttachedPortals(params FieldEntity[] entities) {
        var portals = new LivePortals(2000001, null, null, null);
        portals.Attach(entities);
        return portals;
    }

    static FieldEntity PortalEntity(int id) => new() {
        Raw = new XEntity {
            ModelName = "Portal",
            Name = "Portal_" + id,
            Props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                ["PortalID"] = id.ToString(),
                ["Position"] = "150, -1500, 900",
                ["Rotation"] = "0, 0, 180",
                ["PortalDimension"] = "200, 200, 250",
                ["IsVisible"] = "True",
                ["PortalEnable"] = "True",
            },
        },
        Flat = new FlatModel { Name = "Portal" },
    };

    [Test] public void Gms2AddRemoveAndUpdateApplyToLivePortals() {
        var portals = AttachedPortals(PortalEntity(1), PortalEntity(2));

        var ra = new ByteReader(Gms2AddSpawned);
        var add = FieldPortalMessage.Read(ra);
        Assert.That((add.Mode, add.Id, add.Type, add.TargetMap, add.Visible, add.Enabled, add.Minimap),
            Is.EqualTo(((byte)0, 100025248, (byte)6, 2000045, true, true, false)));
        Assert.That(add.Model, Has.Length.EqualTo(22));
        Assert.That(ra.Available, Is.EqualTo(0));
        portals.Apply(add);
        var p = portals.ById(100025248);
        Assert.That(p, Is.Not.Null);
        Assert.That((p.TargetMap, p.Shown, p.Glow, p.HasBox), Is.EqualTo((2000045, true, true, true)));

        var rr = new ByteReader(Gms2Remove);
        var rem = FieldPortalMessage.Read(rr);
        Assert.That((rem.Mode, rem.Id), Is.EqualTo(((byte)1, 100025248)));
        Assert.That(rr.Available, Is.EqualTo(0));
        portals.Apply(rem);
        Assert.That(portals.ById(100025248), Is.Null);

        var rb = new ByteReader(Gms2AddBound);
        var bound = FieldPortalMessage.Read(rb);
        Assert.That((bound.Mode, bound.Id, bound.Type, bound.TargetMap, bound.Minimap),
            Is.EqualTo(((byte)0, 1, (byte)0, 2000001, true)));
        Assert.That(rb.Available, Is.EqualTo(0));
        portals.Apply(bound);
        Assert.That(portals.ById(1)?.TargetMap, Is.EqualTo(2000001));

        var ru = new ByteReader(Gms2Update);
        var upd = FieldPortalMessage.Read(ru);
        Assert.That((upd.Mode, upd.Id, upd.Visible, upd.Enabled, upd.Minimap, upd.Extra),
            Is.EqualTo(((byte)2, 2, true, true, true, (ushort)0)));
        Assert.That(ru.Available, Is.EqualTo(0));
        portals.Apply(new FieldPortalMessage { Mode = 0, Id = 2, Type = 0, Visible = false, Enabled = false, Minimap = false });
        portals.Apply(upd);
        Assert.That((portals.ById(2).Shown, portals.ById(2).Glow, portals.ById(2).Minimap), Is.EqualTo((true, true, true)));
    }

    [Test] public void KmsAddRemoveAndUpdateApplyToLivePortals() {
        var portals = AttachedPortals(PortalEntity(1));

        var ra = new ByteReader(KmsAddSpawned);
        var add = FieldPortalMessage.Read(ra);
        Assert.That((add.Mode, add.Id, add.Type, add.TargetMap, add.Visible, add.Enabled),
            Is.EqualTo(((byte)0, 103483385, (byte)0x0B, 2000062, true, true)));
        Assert.That(add.Position, Is.EqualTo(new Vector3(-3300f, -3450f, 75f)));
        Assert.That(ra.Available, Is.EqualTo(0));
        portals.Apply(add);
        var p = portals.ById(103483385);
        Assert.That(p, Is.Not.Null);
        Assert.That((p.TargetMap, p.Shown, p.Glow, p.Kind), Is.EqualTo((2000062, true, true, 0)));

        var rr = new ByteReader(KmsRemove);
        var rem = FieldPortalMessage.Read(rr);
        Assert.That((rem.Mode, rem.Id), Is.EqualTo(((byte)1, 103483385)));
        Assert.That(rr.Available, Is.EqualTo(0));
        portals.Apply(rem);
        Assert.That(portals.ById(103483385), Is.Null);

        var ru = new ByteReader(KmsUpdate);
        var upd = FieldPortalMessage.Read(ru);
        Assert.That((upd.Mode, upd.Id, upd.Visible, upd.Enabled, upd.Minimap),
            Is.EqualTo(((byte)2, 1, false, false, false)));
        Assert.That(ru.Available, Is.EqualTo(0));
        portals.Apply(new FieldPortalMessage { Mode = 0, Id = 1, Type = 0, Visible = true, Enabled = true, Minimap = true });
        portals.Apply(upd);
        Assert.That((portals.ById(1).Shown, portals.ById(1).Glow, portals.ById(1).Minimap), Is.EqualTo((false, false, false)));
    }
}
