using System.Numerics;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Maple2.PacketLib.Tools;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// <see cref="FunctionCubes"/>: CFunctionCubeService (functioncubeobject.cpp:2383-2720) over the placed cubes of
/// <see cref="CubeCellMap"/>, with the FunctionCube packets as both servers' FunctionCubePacket writers build them (an
/// InteractCube record is unicode id, i32 state, then the nurturing pet fields for a nest; then u8, 0 Add or 1 Update).
/// </summary>
public class FunctionCubesTests
{
    static ObjectData Obj(string attrs, string inner = "") =>
        ObjectData.Parse(XDocument.Parse($"<ms2><FunctionCube {attrs}>{inner}</FunctionCube></ms2>"), 1);

    // a kfm switch: Idle_A still, Dead_A on, the move into on is On_A; the move into off has no sequence
    static readonly ObjectData Switch = Obj("UseKfm=\"1\" ControlType=\"Switch\" StateChangeAction=\"ActionKey\" DefaultState=\"0\" " +
        "ZeroSeq=\"Idle_A\" FirstSeq=\"Dead_A\" FirstIngSeq=\"On_A\" ZeroEffect=\"snd0\" FirstIngEffect=\"snd4\"");
    static readonly ObjectData Bank = Obj("UseKfm=\"1\" ControlType=\"FunctionUI\" UseUiFunction=\"bank\" StateChangeAction=\"ActionKey\" DefaultState=\"0\"");
    static readonly ObjectData Chair = Obj("ControlType=\"Ride\" StateChangeAction=\"ActionKey\"", "<firstPCAnimation firstAnimation=\"Sit_A\"/>");
    static readonly ObjectData Web = Obj("ControlType=\"OpenWeb\" StateChangeAction=\"ActionKey\"", "<openWeb webURL=\"http://x\" opentype=\"0\"/>");
    static readonly ObjectData Nest = Obj("ControlType=\"Nurturing\"");

    const int Key = 0x010203;
    static readonly Vector3 At = new(3 * 150, 2 * 150, 1 * 150);

    static FunctionCubes.Cube Place(FunctionCubes t, ObjectData o, int subtype = 5, int key = Key) =>
        t.Place(key, subtype, o, 50100001, 0, At);

    static byte[] Bytes(Action<ByteWriter> write)
    {
        var w = new ByteWriter();
        write(w);
        return w.Buffer.AsSpan(0, w.Length).ToArray();
    }

    // FunctionCubePacket.WriteFunctionCube / SendCubes (both servers): mode, [count], id, state, [nest], u8
    static FunctionCubeReply Reply(ProtocolProfile profile, FunctionCubes t, Action<ByteWriter> write) =>
        FunctionCubeReply.Read(new ByteReader(Bytes(write)), profile, t.IsNurturing);

    static FunctionCubeReply Update(FunctionCubes t, string id, int state, byte addOrUpdate, ProtocolProfile profile = null) =>
        Reply(profile ?? ProtocolProfile.Kms2525, t, w => { w.WriteByte(3); w.WriteUnicodeString(id); w.WriteInt(state); w.WriteByte(addOrUpdate); });

    static void Set(FunctionCubes t, FunctionCubeReply r) => t.Apply(r, 0);

    // FunctionCubePacket.UseFurniture: mode (KMS 6, GMS2 5), i64 character, unicode id, bool InUse
    static FunctionCubeReply Use(FunctionCubes t, long owner, string id, bool inUse, ProtocolProfile profile = null)
    {
        profile ??= ProtocolProfile.Kms2525;
        return Reply(profile, t, w => { w.WriteByte((byte)(profile.IsGms2 ? 5 : 6)); w.WriteLong(owner); w.WriteUnicodeString(id); w.WriteBool(inUse); });
    }

    [TestCase(3, 1)] [TestCase(4, 2)] [TestCase(5, 4)] [TestCase(6, 4)] [TestCase(7, 4)] [TestCase(1, 0)] [TestCase(8, 0)]
    public void TheObjectCodeIsTheSubtypes(int subtype, int code) => Assert.That(FunctionCubes.ObjCodeOf(subtype), Is.EqualTo(code));

    [Test]
    public void APlacedCubeIsNamedByObjectCodeAndCellAndStartsInItsDefaultState()
    {
        var t = new FunctionCubes();
        var c = Place(t, Switch);
        Assert.That(c.Id, Is.EqualTo($"4_{Key}"));
        Assert.That(t.Find($"4_{Key}"), Is.SameAs(c));
        Assert.That((c.Kind, c.State, c.Sequence, c.Sound, c.Usable), Is.EqualTo((3, 0, "Idle_A", "snd0", true)));
        Assert.That(t.PendingCount, Is.EqualTo(0), "the pending twin is taken over");
    }

    [Test]
    public void AnObjectWithoutAFunctionCubeMakesNoCube()
    {
        var t = new FunctionCubes();
        Assert.That(t.Place(Key, 5, ObjectData.Parse(XDocument.Parse("<ms2/>"), 1), 1, 0, At), Is.Null);
        Assert.That(t.Place(Key, 5, null, 1, 0, At), Is.Null);
        Assert.That(t.Count, Is.EqualTo(0));
    }

    [Test]
    public void ANifCubePlaysNoSequenceButItsSound()
    {
        var o = Obj("UseKfm=\"0\" ControlType=\"Switch\" ZeroSeq=\"Idle_A\" ZeroEffect=\"snd0\"");
        var c = Place(new FunctionCubes(), o);
        Assert.That((c.Sequence, c.Sound), Is.EqualTo(("", "snd0")));
    }

    [Test]
    public void AFlaggedRecordPlaysTheMoveAndItsEndSettles()
    {
        var t = new FunctionCubes();
        var c = Place(t, Switch);
        Set(t, Update(t, c.Id, 1, 1));                                   // the server's Update after a use: InUse, flag 1
        Assert.That((c.State, c.Sequence, c.Sound), Is.EqualTo((4, "On_A", "snd4")));
        FunctionCubes.EndOfSequence(c);
        Assert.That((c.State, c.Sequence), Is.EqualTo((1, "Dead_A")));
        Set(t, Update(t, c.Id, 0, 1));                                   // the move into 0 has no sequence: straight to 0
        Assert.That((c.State, c.Sequence), Is.EqualTo((0, "Idle_A")));
        Set(t, Update(t, c.Id, 1, 0));                                   // an Add record (flag 0): the still state
        Assert.That((c.State, c.Sequence), Is.EqualTo((1, "Dead_A")));
    }

    [Test]
    public void AStateOutsideTheSixIsRefused()
    {
        var t = new FunctionCubes();
        var c = Place(t, Switch);
        Set(t, Update(t, c.Id, 7, 0));
        Assert.That(c.State, Is.EqualTo(0));
    }

    [Test]
    public void TheCubeListSwitchesEveryKnownCubeAndSkipsTheRest()
    {
        var t = new FunctionCubes();
        var a = Place(t, Switch);
        var b = Place(t, Switch, key: 0x010204);
        var m = Reply(ProtocolProfile.Kms2525, t, w =>
        {
            w.WriteByte(2); w.WriteInt(3);
            foreach (var (id, s) in new[] { (a.Id, 1), ("4_999", 1), (b.Id, 2) }) { w.WriteUnicodeString(id); w.WriteInt(s); w.WriteByte(0); }
        });
        t.Apply(m, 1);
        Assert.That((a.State, b.State), Is.EqualTo((1, 2)));
    }

    [Test]
    public void ANestsRecordIsReadWithItsPetFieldsByTheTablesKind()
    {
        var t = new FunctionCubes();
        var n = Place(t, Nest);
        var s = Place(t, Switch, key: 0x010204);
        var m = Reply(ProtocolProfile.Kms2525, t, w =>
        {
            w.WriteByte(2); w.WriteInt(2);
            w.WriteUnicodeString(n.Id); w.WriteInt(0);
            w.WriteLong(1700000000); w.WriteLong(5); w.WriteShort(2); w.WriteShort(3); w.WriteShort(0); w.WriteLong(9);   // the pet
            w.WriteByte(0);
            w.WriteUnicodeString(s.Id); w.WriteInt(1); w.WriteByte(0);
        });
        Assert.That(m.States[0].Nurture, Is.EqualTo(new FunctionCubeNurture(1700000000, 5, 2, 3, 0, 9)));
        Assert.That(m.States[1].Nurture, Is.Null);
        t.Apply(m, 1);
        Assert.That(s.State, Is.EqualTo(1));
    }

    [Test]
    public void TheBankOpensItsStoreOnlyInTheSecondState()
    {
        var t = new FunctionCubes();
        var c = Place(t, Bank);
        // both servers' use: the cube goes to InUse (1) with flag 1 (state 4), then the use reply
        Set(t, Update(t, c.Id, 1, 1));
        Assert.That(t.Apply(Use(t, 7, c.Id, true), 7).Action, Is.EqualTo(FunctionCubes.UseAction.None), "state 4: OpenSubKindDialog does nothing");
        Set(t, Update(t, c.Id, 2, 1));
        var r = t.Apply(Use(t, 7, c.Id, true), 7);
        Assert.That((r.Action, r.Dialog, r.Cube), Is.EqualTo((FunctionCubes.UseAction.OpenDialog, 4, c)));
        Assert.That(t.Apply(Use(t, 8, c.Id, true), 7).Action, Is.EqualTo(FunctionCubes.UseAction.None), "another pc's use");
        Assert.That(t.Apply(Use(t, 7, c.Id, false), 7).Action, Is.EqualTo(FunctionCubes.UseAction.None), "no flag");
        Assert.That(t.Apply(Use(t, 7, c.Id, true), 7, hasOwnPc: false).Action, Is.EqualTo(FunctionCubes.UseAction.None), "no own pc");
    }

    [Test]
    public void TheGms2UseReplyIsModeFive()
    {
        var t = new FunctionCubes();
        var c = Place(t, Bank);
        Set(t, Update(t, c.Id, 2, 0, ProtocolProfile.Gms2));
        Assert.That(t.Apply(Use(t, 7, c.Id, true, ProtocolProfile.Gms2), 7).Action, Is.EqualTo(FunctionCubes.UseAction.OpenDialog));
    }

    [Test]
    public void AFurnitureIsTakenIntoUseAndLeftByItsOwnPcOnly()
    {
        var t = new FunctionCubes();
        var c = Place(t, Chair);
        Assert.That(c.HasModel, Is.True);
        Assert.That(t.Apply(Use(t, 7, c.Id, true), 7).Action, Is.EqualTo(FunctionCubes.UseAction.UseFurniture));
        Assert.That(t.Apply(Use(t, 7, c.Id, false), 7, inFurniture: true).Action, Is.EqualTo(FunctionCubes.UseAction.LeaveFurniture));
        Assert.That(t.Apply(Use(t, 7, c.Id, false), 7).Action, Is.EqualTo(FunctionCubes.UseAction.None), "not in the furniture state");
        Assert.That(t.Apply(Use(t, 8, c.Id, true), 7).Action, Is.EqualTo(FunctionCubes.UseAction.None), "another pc");
    }

    [Test]
    public void AWebCubeOpensItsLinkForAnyone()
    {
        var t = new FunctionCubes();
        var c = Place(t, Web);
        Assert.That(t.Apply(Use(t, 8, c.Id, false), 7).Action, Is.EqualTo(FunctionCubes.UseAction.OpenWeb));
    }

    [Test]
    public void OnlyKindThreeAnswersAUse()
    {
        var t = new FunctionCubes();
        var c = Place(t, Obj("ControlType=\"PVP\" StateChangeAction=\"ActionKey\"", "<firstPCAnimation firstAnimation=\"Sit_A\"/>"));
        Assert.That(c.Usable, Is.True);
        Assert.That(t.Apply(Use(t, 7, c.Id, true), 7).Action, Is.EqualTo(FunctionCubes.UseAction.None));
    }

    [Test]
    public void TheNearestUsableCubeIsWithinRange()
    {
        var t = new FunctionCubes();
        Place(t, Switch, key: 0);
        var near = t.Place(1, 5, Switch, 1, 0, new Vector3(150, 0, 0));
        t.Place(2, 5, Obj("ControlType=\"Switch\" StateChangeAction=\"Attack\""), 1, 0, new Vector3(100, 0, 0));   // not by the key
        Assert.That(t.NearestUsable(new Vector3(140, 0, 0), 200), Is.SameAs(near));
        Assert.That(t.NearestUsable(new Vector3(1000, 0, 0), 200), Is.Null);
    }

    [Test]
    public void TheCubeMapPlacesAndRemovesTheFunctionCubeWithItsCell()
    {
        var map = new CubeCellMap { InstallObjectOf = id => id == 50100001 ? (5, Switch) : null };
        Assert.That(map.Place(Key, new CubeItemInfo { ItemId = 50100001 }, _ => true), Is.True);
        Assert.That(map.Place(0x010204, new CubeItemInfo { ItemId = 50100002 }, _ => true), Is.True);
        Assert.That(map.Functions.All.Select(c => c.Id), Is.EqualTo(new[] { $"4_{Key}" }));
        Assert.That(map.Functions.Find($"4_{Key}").Position, Is.EqualTo(At));
        Assert.That(map.RemoveCube(Key), Is.True);
        Assert.That(map.Functions.Count, Is.EqualTo(0));
    }

    [Test]
    public void TheUseRequestIsModeFiveCountAndRecordOnKmsAndModeFourOnGms2()
    {
        var req = new FunctionCubeUseRequest("4_66051");
        var kms = Bytes(w => req.Write(w, ProtocolProfile.Kms2525));
        var gms2 = Bytes(w => req.Write(w, ProtocolProfile.Gms2));
        string id = "0700" + string.Concat("4_66051".Select(ch => $"{(int)ch:X2}00"));
        Assert.That(Convert.ToHexString(kms), Is.EqualTo("05" + "01000000" + id + "01"));
        Assert.That(Convert.ToHexString(gms2), Is.EqualTo("04" + id + "01"));
        Assert.That(req.Op, Is.EqualTo(Outbound.FunctionCube));
    }

    /// <summary>
    /// Every item of MS2_DATA and MS2_GMS2_DATA with an install object that has a function cube, placed in a cube map: its
    /// cube is made under "objCode_cell" with the object's kind and a state in 0-5; a kind 3 cube with a sub-kind answers
    /// its own use reply with its dialog in state 2 and with nothing after the servers' InUse update (state 4); a furniture
    /// cube with the furniture use; then the cell's removal takes the cube away. Prints the counts by kind.
    /// </summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryInstallItemsFunctionCubeIsPlacedSwitchedAndRemoved(string variable)
    {
        using var packs = new PackSet(RcGap.Data(variable));
        var items = new ItemDataTable(packs);
        var objects = new Dictionary<int, ObjectData>();
        ObjectData ObjectOf(int code) => objects.TryGetValue(code, out var o) ? o : objects[code] = ObjectData.Load(packs, (uint)code);
        var ids = new SortedSet<int>();
        var names = packs.List("Xml").ToList();
        var idAttr = new Regex("<item id=\"(\\d+)\"", RegexOptions.Compiled);
        foreach (var n in names.Where(n => n.StartsWith("itemdata/", StringComparison.Ordinal)))
            foreach (Match m in idAttr.Matches(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", n))))
                ids.Add(int.Parse(m.Groups[1].Value));
        foreach (var n in names.Where(n => n.StartsWith("item/", StringComparison.Ordinal)))
            if (Regex.Match(n, @"/(\d+)\.xml$") is { Success: true } m) ids.Add(int.Parse(m.Groups[1].Value));
        Assert.That(ids, Is.Not.Empty);

        var kinds = new SortedDictionary<int, int>();
        var bad = new List<string>();
        int placed = 0, dialogs = 0, furniture = 0, codes4 = 0;
        int key = 0;
        foreach (int id in ids)
        {
            var d = items.Get(id);
            if (d == null || d.InstallObjCode == 0) continue;
            var o = ObjectOf(d.InstallObjCode);
            if (o == null || !o.Cube.Enabled) continue;
            var map = new CubeCellMap { InstallObjectOf = i => i == id ? (d.Subtype, o) : null };
            key = (key + 1) & 0xffff;
            if (!map.Place(key, new CubeItemInfo { ItemId = id }, _ => true)) { bad.Add($"{id}: not placed"); continue; }
            var c = map.Functions.All.SingleOrDefault();
            if (c == null) { bad.Add($"{id}: no function cube"); continue; }
            placed++;
            int code = FunctionCubes.ObjCodeOf(d.Subtype);
            if (code == 4) codes4++;
            if (c.Id != $"{code}_{key}") bad.Add($"{id}: id {c.Id}");
            if (c.Kind != o.Cube.ControlType) bad.Add($"{id}: kind {c.Kind}");
            if (c.State is < 0 or > 5) bad.Add($"{id}: state {c.State}");
            kinds[c.Kind] = kinds.GetValueOrDefault(c.Kind) + 1;
            var t = map.Functions;
            if (c.Kind == 3 && c.SubKind is 1 or 4 or 5 or 6 or 7)
            {
                Set(t, Update(t, c.Id, 1, 1));
                if (t.Apply(Use(t, 7, c.Id, true), 7).Action != FunctionCubes.UseAction.None) bad.Add($"{id}: dialog in state {c.State}");
                Set(t, Update(t, c.Id, 2, 0));
                var r = t.Apply(Use(t, 7, c.Id, true), 7);
                if (r.Action != FunctionCubes.UseAction.OpenDialog || r.Dialog != c.SubKind) bad.Add($"{id}: {r.Action} {r.Dialog} in state 2");
                else dialogs++;
            }
            else if (c.Kind == 3 && c.SubKind == 0 && c.HasModel)
            {
                if (t.Apply(Use(t, 7, c.Id, true), 7).Action != FunctionCubes.UseAction.UseFurniture) bad.Add($"{id}: furniture");
                else furniture++;
            }
            if (!map.RemoveCube(key) || map.Functions.Count != 0) bad.Add($"{id}: not removed");
        }
        TestContext.Progress.WriteLine($"{variable}: {placed} install items with a function cube ({codes4} of object code 4), " +
            $"{dialogs} dialog cubes, {furniture} furniture, kinds " + string.Join(" ", kinds.Select(k => $"{k.Key}:{k.Value}")));
        Assert.That(bad, Is.Empty);
        Assert.That(placed, Is.GreaterThan(300));
        Assert.That(dialogs, Is.GreaterThan(0), "the bank and trigger tool items");
        Assert.That(furniture, Is.GreaterThan(0));
    }
}
