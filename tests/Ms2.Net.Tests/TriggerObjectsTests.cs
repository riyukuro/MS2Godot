using Maple2.PacketLib.Tools;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Net.Tests;

public class TriggerObjectsTests
{
    static ByteReader R(ByteWriter w) => new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray());

    static readonly Dictionary<int, TriggerObjectKind> Field = new()
    {
        [3001] = TriggerObjectKind.Mesh, [7] = TriggerObjectKind.Cube, [8] = TriggerObjectKind.Actor, [9] = TriggerObjectKind.Rope,
        [10] = TriggerObjectKind.Effect, [11] = TriggerObjectKind.Camera, [12] = TriggerObjectKind.Plain,
    };
    static TriggerObjectKind? KindOf(int id) => Field.TryGetValue(id, out var k) ? k : null;

    [Test]
    public void Gms2TypesFromEightAreOneLower()
    {
        foreach (byte t in new byte[] { 2, 3, 5, 9, 0xf })
            Assert.That(TriggerObjects.ClientType(t, ProtocolProfile.Kms2525), Is.EqualTo(t));
        foreach (byte t in new byte[] { 0, 1, 2, 3, 5, 7 })
            Assert.That(TriggerObjects.ClientType(t, ProtocolProfile.Gms2), Is.EqualTo(t));
        Assert.That(TriggerObjects.ClientType(8, ProtocolProfile.Gms2), Is.EqualTo(TriggerObjects.Guide));
        Assert.That(TriggerObjects.ClientType(0xe, ProtocolProfile.Gms2), Is.EqualTo(0xf));
        Assert.That(TriggerObjects.ClientType(0x12, ProtocolProfile.Gms2), Is.EqualTo(0x13));
        Assert.That(TriggerObjects.ClientType(0x14, ProtocolProfile.Gms2), Is.EqualTo(0x15));
    }

    [Test]
    public void LoadReadsEachObjectByItsKind()
    {
        var w = new ByteWriter();
        w.WriteByte(TriggerObjects.Load); w.WriteInt(7);
        w.WriteInt(3001); w.WriteBool(false); w.WriteBool(true); w.WriteInt(5); w.WriteUnicodeString("m"); w.WriteFloat(1.005f);
        w.WriteInt(7); w.WriteBool(true);
        w.WriteInt(8); w.WriteBool(true); w.WriteUnicodeString("Idle_A");
        w.WriteInt(9); w.WriteBool(true); w.WriteBool(false); w.WriteInt(3);
        w.WriteInt(10); w.WriteBool(true); w.WriteByte(2); w.WriteInt(4);
        w.WriteInt(11); w.WriteByte(1);
        w.WriteInt(12);                                    // a plain TriggerObject reads nothing more
        var r = R(w);
        var ev = TriggerObjects.Read(r, KindOf);
        Assert.That(r.Available, Is.EqualTo(0));
        Assert.That(ev.Select(e => e.Kind), Is.EqualTo(new[] { TriggerObjectKind.Mesh, TriggerObjectKind.Cube, TriggerObjectKind.Actor,
            TriggerObjectKind.Rope, TriggerObjectKind.Effect, TriggerObjectKind.Camera, TriggerObjectKind.Plain }));
        Assert.That((ev[0].Id, ev[0].Show, ev[0].Flag, ev[0].Fade, ev[0].Text, ev[0].Scale), Is.EqualTo((3001, false, (byte)1, 5, "m", 1.005f)));
        Assert.That((ev[2].Show, ev[2].Text), Is.EqualTo((true, "Idle_A")));
        Assert.That((ev[3].Show, ev[3].Flag2, ev[3].Fade), Is.EqualTo((true, false, 3)));
        Assert.That((ev[4].Flag, ev[4].Fade), Is.EqualTo(((byte)2, 4)));
        Assert.That(ev[5].Flag, Is.EqualTo(1));
    }

    // FUN_141863a90: an id the service does not hold is logged and its fields are not read
    [Test]
    public void UnknownIdReadsOnlyTheId()
    {
        var w = new ByteWriter();
        w.WriteByte(TriggerObjects.Load); w.WriteInt(2);
        w.WriteInt(555);
        w.WriteInt(7); w.WriteBool(true);
        var ev = TriggerObjects.Read(R(w), KindOf);
        Assert.That(ev.Select(e => (e.Id, e.Known)), Is.EqualTo(new[] { (555, false), (7, true) }));
    }

    [Test]
    public void UpdateIsOneEventAndOtherTypesNone()
    {
        var w = new ByteWriter();
        w.WriteByte(TriggerObjects.Update); w.WriteInt(7); w.WriteBool(false);
        Assert.That(TriggerObjects.Read(R(w), KindOf).Single().Show, Is.False);
        var c = new ByteWriter(); c.WriteByte(5); c.WriteByte(0);
        Assert.That(TriggerObjects.Read(R(c), KindOf), Is.Empty);
    }

    // TriggerObject::Create's order; no MS2TriggerObject mixin: no trigger object (MS2TriggerSound's flat lacks it)
    [Test]
    public void KindFollowsCreateOrder()
    {
        Func<string, bool> Has(params string[] m) => n => m.Contains(n);
        Assert.That(TriggerObjects.KindOf(Has("MS2TriggerMesh", "MS2TriggerObject")), Is.EqualTo(TriggerObjectKind.Mesh));
        Assert.That(TriggerObjects.KindOf(Has("MS2TriggerActorCube", "MS2TriggerActor", "MS2TriggerObject")), Is.EqualTo(TriggerObjectKind.Actor));
        Assert.That(TriggerObjects.KindOf(Has("MS2TriggerModel", "MS2TriggerObject")), Is.EqualTo(TriggerObjectKind.Plain));
        Assert.That(TriggerObjects.KindOf(Has("MS2TriggerSound")), Is.Null);
        Assert.That(TriggerObjects.KindOf(Has("MS2TriggerMesh")), Is.Null);
    }

    /// <summary>Each Ui sub reads exactly the fields its handler reads (triggerservice.cpp:632-671, :963-1096).</summary>
    [Test]
    public void TheUiSubsReadTheirFields()
    {
        TriggerUiCommand Read(System.Action<ByteWriter> body)
        {
            var w = new ByteWriter(); body(w);
            var r = R(w); var c = TriggerObjects.ReadUi(r);
            Assert.That(r.Available, Is.Zero, $"sub {c.Sub} left bytes");
            return c;
        }
        Assert.That(Read(w => { w.WriteByte(1); w.WriteInt(260); }).Id, Is.EqualTo(260));
        var show = Read(w => { w.WriteByte(2); w.WriteInt(7); w.WriteInt(8); w.WriteInt(6000); });
        Assert.That((show.Id, show.Value, show.Duration), Is.EqualTo((7, 8, 6000)));
        var movie = Read(w => { w.WriteByte(4); w.WriteString("a.usm"); w.WriteInt(3); });
        Assert.That((movie.Path, movie.Id), Is.EqualTo(("a.usm", 3)));
        var list = Read(w => { w.WriteByte(7); w.WriteInt(2); w.WriteUnicodeString("Bore_A"); w.WriteUnicodeString("Bore_B"); });
        Assert.That(list.Emotions, Is.EqualTo(new[] { "Bore_A", "Bore_B" }));
        var one = Read(w => { w.WriteByte(8); w.WriteByte(1); w.WriteInt(5000); w.WriteUnicodeString("Sit_Down_A"); });
        Assert.That((one.Flag, one.Value, one.Emotions[0]), Is.EqualTo(((byte)1, 5000, "Sit_Down_A")));
        var talk = Read(w => { w.WriteByte(9); w.WriteInt(11); w.WriteString("down_Idle"); });
        Assert.That((talk.Id, talk.Text), Is.EqualTo((11, "down_Idle")));
        var turn = Read(w => { w.WriteByte(10); w.WriteShort(1); w.WriteShort(-2); w.WriteShort(90); w.WriteBool(true); });
        Assert.That((turn.Point, turn.Move), Is.EqualTo((new System.Numerics.Vector3(1, -2, 90), true)));
        var game = Read(w => { w.WriteByte(12); w.WriteInt(1000); w.WriteInt(30000); });
        Assert.That((game.Id, game.Value), Is.EqualTo((1000, 30000)));
        Assert.That(Read(w => w.WriteByte(6)).Sub, Is.EqualTo(6), "a sub without a case reads nothing more");
    }

    /// <summary>The Dialog kinds (triggerservice.cpp:527-629) and the PvP match blocks (pvpinfo.cpp:193, :222).</summary>
    [Test]
    public void TheDialogKindsReadTheTimerAndTheirBlock()
    {
        TriggerDialogCommand Read(System.Action<ByteWriter> body)
        {
            var w = new ByteWriter(); body(w);
            var r = R(w); var c = TriggerObjects.ReadDialog(r);
            Assert.That(r.Available, Is.Zero, $"kind {c.Kind} left bytes");
            return c;
        }
        void Timer(ByteWriter w, byte kind) { w.WriteByte(kind); w.WriteInt(100); w.WriteInt(60000); w.WriteByte(1); }
        void Side(ByteWriter w, long id, bool duel) { w.WriteLong(id); w.WriteInt(5); w.WriteInt(9); w.WriteByte(2); if (duel) w.WriteInt(40); }
        var t = Read(w => { Timer(w, 1); w.WriteInt(-30); w.WriteUnicodeString("TR"); });
        Assert.That((t.Start, t.Duration, t.AutoRemove, t.OffsetY, t.Align), Is.EqualTo((100, 60000, (byte)1, -30, "TR")));
        var normal = Read(w => { Timer(w, 2); Side(w, 1, false); Side(w, 2, false); });
        Assert.That((normal.Match.First.CharacterId, normal.Match.Second.CharacterId, normal.Match.Second.Wins), Is.EqualTo((1L, 2L, (byte)2)));
        var duel = Read(w => { Timer(w, 5); Side(w, 1, true); Side(w, 2, true); w.WriteInt(77); });
        Assert.That((duel.Match.First.Points, duel.Match.Last), Is.EqualTo((40, 77)));
        Assert.That(Read(w => Timer(w, 3)).Duration, Is.EqualTo(60000));
        Assert.That(Read(w => Timer(w, 4)).Start, Is.EqualTo(100));
        Assert.That(Read(w => w.WriteByte(9)).Kind, Is.EqualTo(9), "an unknown kind reads nothing more");
    }

    [Test]
    public void TheSetVisibleUiAndDuelBarRead()
    {
        var w = new ByteWriter(); w.WriteByte(1); w.WriteString("GuideEventDialog,QuestDialog");
        var r = R(w); var v = TriggerObjects.ReadSetVisibleUi(r);
        Assert.That((v.State, v.Names, r.Available), Is.EqualTo(((byte)1, "GuideEventDialog,QuestDialog", 0)));
        w = new ByteWriter(); w.WriteBool(true); w.WriteInt(50); w.WriteInt(90000); w.WriteInt(3);
        r = R(w); var on = TriggerObjects.ReadDuelHpBar(r);
        Assert.That((on.Show, on.ObjectId, on.DurationMs, on.BarCount, r.Available), Is.EqualTo((true, 50, 90000, 3, 0)));
        w = new ByteWriter(); w.WriteBool(false);
        r = R(w);
        Assert.That((TriggerObjects.ReadDuelHpBar(r).Show, r.Available), Is.EqualTo((false, 0)));
    }

    /// <summary>CTriggerService::FUN_141861840's switch (triggerservice.cpp:366): every type 0-0x1f is a case the port applies,
    /// one it logs as not ported, or one without a client case.</summary>
    [Test]
    public void EveryClientTypeIsAppliedLoggedOrIgnoredAsTheClientsSwitch()
    {
        var applied = new byte[] { 2, 3, 5, 7, 9, 0xf, 0x10, 0x11, 0x12, 0x13, 0x14, 0x15 };
        var notPorted = new byte[] { 6, 0xc, 0x16, 0x17, 0x18, 0x19 };
        for (int t = 0; t < 0x20; t++)
        {
            byte b = (byte)t;
            bool ignored = !applied.Contains(b) && !notPorted.Contains(b);
            Assert.That(TriggerObjects.NotPorted(b), Is.EqualTo(notPorted.Contains(b)), $"type 0x{t:x}");
            Assert.That(TriggerObjects.ClientIgnores(b), Is.EqualTo(ignored), $"type 0x{t:x}");
        }
        // GMS2 sends 0x15 as 0x14 and 0x16 as 0x15: the box result is logged, its wire 0x15 is not the duel bar
        Assert.That(TriggerObjects.NotPorted(TriggerObjects.ClientType(0x15, ProtocolProfile.Gms2)), Is.True);
    }

    /// <summary>Types 0x10 and 0x11 read GameObjectEffectNifCtx::Read's fields (gameobjecteffectnifctx.cpp:68) in the order the
    /// server writes them; 0x10's entries each carry their key first, and 0x12 is the id alone.</summary>
    [Test]
    public void EffectNifBodiesReadAsTheClient()
    {
        var w = new ByteWriter();
        w.WriteInt(2);
        w.WriteInt(71); w.WriteInt(71); w.WriteString("Map/Royalcity/Indoor/ry_in_prop_amp_A01.nif"); w.WriteBool(true); w.WriteFloat(0.5f); w.WriteFloat(45f);
        w.WriteInt(72); w.WriteInt(73); w.WriteString("Npc/Etc/a.nif"); w.WriteBool(false); w.WriteFloat(1f); w.WriteFloat(0f);
        var r = R(w);
        var load = TriggerObjects.ReadLoadEffects(r);
        Assert.That(load.Select(e => (e.Key, e.Effect.ObjectId, e.Effect.NifPath, e.Effect.Outline, e.Effect.Scale, e.Effect.RotateZ)),
            Is.EqualTo(new[] { (71, 71, "Map/Royalcity/Indoor/ry_in_prop_amp_A01.nif", true, 0.5f, 45f), (72, 73, "Npc/Etc/a.nif", false, 1f, 0f) }));
        Assert.That(r.Available, Is.Zero);

        // a count past the body reads the entries that are there
        w = new ByteWriter(); w.WriteInt(-1); w.WriteInt(5); w.WriteInt(5); w.WriteString("x.nif"); w.WriteBool(false); w.WriteFloat(1f); w.WriteFloat(0f);
        r = R(w);
        Assert.That(TriggerObjects.ReadLoadEffects(r).Count, Is.EqualTo(1));

        w = new ByteWriter(); w.WriteInt(9); w.WriteString("y.nif"); w.WriteBool(true); w.WriteFloat(2f); w.WriteFloat(-90f);
        r = R(w);
        Assert.That(TriggerEffectNif.Read(r), Is.EqualTo(new TriggerEffectNif(9, "y.nif", true, 2f, -90f)));
        Assert.That(r.Available, Is.Zero);

        w = new ByteWriter(); w.WriteInt(9);
        r = R(w);
        Assert.That((TriggerObjects.ReadRemoveEffect(r), r.Available), Is.EqualTo((9, 0)));
    }
}
