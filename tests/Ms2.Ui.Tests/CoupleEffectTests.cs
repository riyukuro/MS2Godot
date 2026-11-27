using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Maple2.PacketLib.Tools;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The buddy badge: the character effect gem table (CItemGemPropertyTable, itemgempropertytable.cpp:236-264), the couple effect
/// system (unit_140b0e5f0_coupleeffectsystem.cpp), the PC's hooks (pc.cpp:2261-2320) and the type 7 gem (itemgem.cpp:791-830).
/// </summary>
public class CoupleEffectTests
{
    sealed class World : ICoupleWorld
    {
        public readonly List<CouplePc> Pcs = new();
        public readonly List<IOutPacket> Sent = new();
        public readonly List<(int Id, string Effect, Vector3 At)> Spawned = new();
        public readonly List<int> Removed = new();
        public bool Limited;
        public uint Tick = 1;
        int next = 100;

        public CouplePc FindPc(long characterId) => Pcs.FirstOrDefault(p => p.CharacterId == characterId);
        public bool CoupleEffectLimited => Limited;
        public int SpawnEffect(string effect, Vector3 at) { int id = ++next; Spawned.Add((id, effect, at)); return id; }
        public void RemoveEffect(int id) => Removed.Add(id);
        public void Send(IOutPacket packet) => Sent.Add(packet);
        public uint CheckTick => 5000;
        public float CheckRadius => 150;
        public uint Now => Tick;
        public CharEffectGems Gems { get; } = CharEffectGems.Of(
            new CharEffectGems.Gem(70800001, "m.xml", "f.xml", "diff.xml", "mm.xml", "ff.xml"));

        public CouplePc Add(long charId, int objectId, int gender, string name, bool mine, Vector3 at)
        {
            var p = new CouplePc { CharacterId = charId, ObjectId = objectId, Gender = gender, Name = name, IsMine = mine };
            p.Position = () => at;
            Pcs.Add(p);
            return p;
        }

        public void Move(CouplePc p, Vector3 at) => p.Position = () => at;
    }

    const long Opener = 1001, Partner = 2002;
    const int BadgeItem = 20300501, Gem = 70800001;

    static EquipItem Badge(long coupleId, bool linked, string name) =>
        new() { Id = BadgeItem, Uid = 9000 + coupleId, CoupleId = coupleId, CoupleFlag = linked, CoupleName = name };

    static byte[] Bytes(IOutPacket p)
    {
        var w = new ByteWriter();
        p.Write(w);
        return w.Buffer.AsSpan(0, w.Length).ToArray();
    }

    static IEnumerable<string> Wire(World w) => w.Sent.Select(p => p switch
    {
        BuddyBadgeModeRequest m => $"mode{m.Mode}:{m.CharacterId}",
        BuddyBadgeNewcomerRequest n => $"new:{n.CharacterId}:{n.NewcomerId}",
        BuddyBadgeNameRequest r => $"name:{r.PartnerId}",
        _ => p.GetType().Name,
    });

    // table/itemgemproperty_chareffect.xml: every <charEffect> row with all five effects, each a file of the effect pack
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryCharEffectGemReadsItsEffects(string data)
    {
        var packs = TestData.Packs(RcGap.Data(data));
        var gems = CharEffectGems.Load(packs);
        TestContext.Out.WriteLine($"{data}: {gems.Count} character effect gems");
        Assert.That(gems.Count, Is.GreaterThanOrEqualTo(14), "KMS 15, GMS2 14");
        foreach (var id in Enumerable.Range(70800001, 12).Concat(new[] { 70810002, 70810003 }))
            Assert.That(gems.Find(id), Is.Not.Null, $"{id}");
        bool Has(string path) => packs.Exists("Xml", "effect/" + path.ToLowerInvariant());
        var wearerFiles = new List<int>();
        foreach (var g in gems.All)
        {
            foreach (var path in new[] { g.Male, g.Female, g.CoupleDiff, g.CoupleSameM, g.CoupleSameF })
                Assert.That(path, Is.Not.Empty, $"{g.Id}");
            // every couple effect ships
            foreach (var path in new[] { g.CoupleDiff, g.CoupleSameM, g.CoupleSameF })
                Assert.That(Has(path), Is.True, $"{g.Id}: {path}");
            // the wearer's own effect ships for both genders or for neither (a missing file spawns nothing, as in the client)
            Assert.That(Has(g.Male), Is.EqualTo(Has(g.Female)), $"{g.Id}");
            if (Has(g.Male)) wearerFiles.Add(g.Id);
        }
        // both data sets ship the wearer's effects of 70800001-70800004 and 70800006 only
        Assert.That(wearerFiles.OrderBy(i => i).ToArray(), Is.EqualTo(new[] { 70800001, 70800002, 70800003, 70800004, 70800006 }));
        // the wedding wreath row carries feature="Wedding": on in the KMS data, off in the GMS2 data (told apart by the data's
        // own Korean string folder, not by the variable that named it)
        bool kms = packs.Exists("Xml", "string/kr/stringcommon.xml");
        Assert.That(gems.Find(70800015) != null, Is.EqualTo(kms), "70800015 (feature Wedding)");
        Assert.That(gems.Find(0), Is.Null);
    }

    [Test]
    public void TheGenderPicksTheEffect()
    {
        var g = new CharEffectGems.Gem(1, "m", "f", "diff", "mm", "ff");
        Assert.That(new[] { 0, 1, 2, -1 }.Select(g.EffectByGender), Is.EqualTo(new[] { "m", "f", "", "" }));
        var pairs = new[] { (0, 0), (0, 1), (1, 0), (1, 1), (2, 0), (0, 2), (2, 2), (1, 2) };
        Assert.That(pairs.Select(p => g.CoupleEffect(p.Item1, p.Item2)),
            Is.EqualTo(new[] { "mm", "diff", "diff", "ff", "diff", "diff", "diff", "diff" }));
    }

    [Test]
    public void TheOpenerAsksForTheNameOnceWhenItDiffers()
    {
        var w = new World();
        var me = w.Add(Opener, 1, 0, "Me", true, Vector3.Zero);
        var other = w.Add(Partner, 2, 1, "Other", false, new Vector3(100, 0, 0));
        // the badge's partner name is stale: mode 3 with the partner, once
        me.CreateCouple(w, Badge(Partner, true, "OldName"), Gem);
        Assert.That(Wire(w), Is.EqualTo(new[] { "name:2002" }));
        Assert.That(me.Couple.Active, Is.True);
        me.UserAdded(3003);
        Assert.That(Wire(w), Is.EqualTo(new[] { "name:2002" }), "SendStartRequest is once per system");
        // the same name: no request; another PC's system never asks
        var w2 = new World();
        var me2 = w2.Add(Opener, 1, 0, "Me", true, Vector3.Zero);
        w2.Add(Partner, 2, 1, "Other", false, Vector3.Zero);
        me2.CreateCouple(w2, Badge(Partner, true, "Other"), Gem);
        Assert.That(w2.Sent, Is.Empty);
        var w3 = new World();
        w3.Add(Opener, 1, 0, "Me", true, Vector3.Zero);
        var them = w3.Add(Partner, 2, 1, "Other", false, Vector3.Zero);
        them.CreateCouple(w3, Badge(Opener, false, "Old"), Gem);
        Assert.That(w3.Sent, Is.Empty);
        Assert.That(them.CoupleCharacterId, Is.EqualTo(Opener), "the partner's system names the opener");
        // without the partner in the field: inactive, nothing sent
        var w4 = new World();
        var alone = w4.Add(Opener, 1, 0, "Me", true, Vector3.Zero);
        alone.CreateCouple(w4, Badge(Partner, true, "Old"), Gem);
        Assert.That((alone.Couple.Active, w4.Sent.Count), Is.EqualTo((false, 0)));
        _ = other;
    }

    [Test]
    public void TheOpenerTurnsTheEffectOnInRangeAndOffBeyond()
    {
        var w = new World();
        var me = w.Add(Opener, 1, 0, "Me", true, Vector3.Zero);
        var other = w.Add(Partner, 2, 1, "Other", false, new Vector3(400, 0, 0));
        other.CreateCouple(w, Badge(Opener, false, "Me"), Gem);
        me.CreateCouple(w, Badge(Partner, true, "Other"), Gem);
        Assert.That(w.Sent, Is.Empty);
        // beyond the radius: nothing on, a mode 1 only when an effect is on
        me.Update();
        Assert.That((w.Spawned.Count, w.Sent.Count, me.Couple.InRange), Is.EqualTo((0, 0, false)));
        // in range, but the tick re-arms once a second
        w.Move(other, new Vector3(100, 0, 0));
        w.Tick += 999;
        me.Update();
        Assert.That(w.Spawned, Is.Empty, "Check(1000) is not due yet");
        w.Tick += 1;
        me.Update();
        Assert.That(w.Spawned.Select(s => (s.Effect, s.At)), Is.EqualTo(new[] { ("diff.xml", new Vector3(50, 0, 0)) }));
        Assert.That(Wire(w), Is.EqualTo(new[] { "mode0:1001" }));
        Assert.That(me.Couple.InRange, Is.True);
        // in range and still: nothing more, even after CheckTick
        w.Tick += 6000;
        me.Update();
        Assert.That(w.Sent.Count, Is.EqualTo(1));
        // the middle moved 50 or less: kept; more than 50 after CheckTick: off, and the next second on at the new middle
        w.Move(me, new Vector3(100, 0, 0));
        w.Move(other, new Vector3(100, 0, 0));
        w.Tick += 1000;
        me.Update();
        Assert.That(w.Sent.Count, Is.EqualTo(1), "moved exactly 50");
        w.Move(me, new Vector3(102, 0, 0));
        w.Tick += 1000;
        me.Update();
        Assert.That(Wire(w).Last(), Is.EqualTo("mode1:1001"));
        Assert.That(w.Removed, Is.EqualTo(new[] { w.Spawned[0].Id }));
        Assert.That(me.Couple.InRange, Is.False);
        w.Tick += 1000;
        me.Update();
        Assert.That(w.Spawned.Last().At, Is.EqualTo(new Vector3(101, 0, 0)));
        Assert.That(Wire(w).Last(), Is.EqualTo("mode0:1001"));
        // beyond the radius: off
        w.Move(other, new Vector3(400, 0, 0));
        w.Tick += 1000;
        me.Update();
        Assert.That(Wire(w).Last(), Is.EqualTo("mode1:1001"));
        Assert.That(me.Couple.EffectId, Is.EqualTo(0));
    }

    [Test]
    public void TheMoveCheckWaitsForCheckTick()
    {
        var w = new World();
        var me = w.Add(Opener, 1, 0, "Me", true, Vector3.Zero);
        var other = w.Add(Partner, 2, 0, "Other", false, new Vector3(100, 0, 0));
        other.CreateCouple(w, Badge(Opener, false, "Me"), Gem);
        me.CreateCouple(w, Badge(Partner, true, "Other"), Gem);
        me.Update();
        Assert.That(w.Spawned.Single().Effect, Is.EqualTo("mm.xml"), "two males");
        w.Move(me, new Vector3(-200, 0, 0));
        w.Move(other, new Vector3(-100, 0, 0));
        w.Tick += 4000;
        me.Update();
        Assert.That(w.Sent.Count, Is.EqualTo(1), "moved 150 before CheckTick passed");
        w.Tick += 1000;
        me.Update();
        Assert.That(Wire(w).Last(), Is.EqualTo("mode1:1001"));
    }

    [Test]
    public void OnlyTheOwnLinkedActivePairUpdates()
    {
        // the partner's own system (not linked), another PC's linked one, a limited field, another badge type, and a partner
        // whose system names someone else: no update
        foreach (var c in new[] { "unlinked", "notmine", "limited", "type", "other" })
        {
            var w = new World();
            var me = w.Add(Opener, 1, 0, "Me", c != "notmine", Vector3.Zero);
            var other = w.Add(Partner, 2, 1, "Other", false, new Vector3(10, 0, 0));
            var theirs = Badge(c == "other" ? 5005 : Opener, false, "Me");
            if (c == "type") theirs.Id = BadgeItem + 1;
            other.CreateCouple(w, theirs, Gem);
            me.CreateCouple(w, Badge(Partner, c != "unlinked", "Other"), Gem);
            w.Limited = c == "limited";
            me.Update();
            Assert.That((w.Spawned.Count, w.Sent.Count), Is.EqualTo((0, 0)), c);
        }
    }

    [Test]
    public void ANewcomerGetsModeTwoWhileTheEffectIsOn()
    {
        var w = new World();
        var me = w.Add(Opener, 1, 1, "Me", true, Vector3.Zero);
        var other = w.Add(Partner, 2, 1, "Other", false, new Vector3(10, 0, 0));
        other.CreateCouple(w, Badge(Opener, false, "Me"), Gem);
        me.CreateCouple(w, Badge(Partner, true, "Other"), Gem);
        me.UserAdded(3003);
        Assert.That(w.Sent, Is.Empty, "no effect on yet");
        me.Update();
        Assert.That(w.Spawned.Single().Effect, Is.EqualTo("ff.xml"), "two females");
        me.UserAdded(3003);
        Assert.That(Wire(w).Last(), Is.EqualTo("new:1001:3003"));
        Assert.That(Bytes(w.Sent.Last()), Is.EqualTo(new byte[] { 2, 0xE9, 3, 0, 0, 0, 0, 0, 0, 0xBB, 0x0B, 0, 0, 0, 0, 0, 0 }));
        // the partner's (unlinked) system never sends mode 2
        int sent = w.Sent.Count;
        other.UserAdded(3003);
        Assert.That(w.Sent.Count, Is.EqualTo(sent));
    }

    [Test]
    public void ThePartnerLeavingTurnsTheEffectOff()
    {
        var w = new World();
        var me = w.Add(Opener, 1, 0, "Me", true, Vector3.Zero);
        var other = w.Add(Partner, 2, 1, "Other", false, new Vector3(10, 0, 0));
        var third = w.Add(3003, 3, 1, "Third", false, Vector3.Zero);
        other.CreateCouple(w, Badge(Opener, false, "Me"), Gem);
        me.CreateCouple(w, Badge(Partner, true, "Other"), Gem);
        me.Update();
        // someone else leaves: kept
        me.UserRemoved(third.ObjectId);
        Assert.That((me.Couple.Active, me.Couple.EffectId != 0), Is.EqualTo((true, true)));
        // the partner leaves: off and inactive, no update after
        me.UserRemoved(other.ObjectId);
        Assert.That(Wire(w).Last(), Is.EqualTo("mode1:1001"));
        Assert.That((me.Couple.Active, me.Couple.EffectId), Is.EqualTo((false, 0)));
        int sent = w.Sent.Count;
        w.Tick += 1000;
        me.Update();
        Assert.That(w.Sent.Count, Is.EqualTo(sent));
        // it comes back: the newcomer hook starts the system again
        me.UserAdded(Partner);
        w.Tick += 1000;
        me.Update();
        Assert.That(Wire(w).Last(), Is.EqualTo("mode0:1001"));
    }

    [Test]
    public void TheServerShowsAndHidesThePartnersEffect()
    {
        var w = new World();
        var me = w.Add(Opener, 1, 0, "Me", true, Vector3.Zero);
        var other = w.Add(Partner, 2, 1, "Other", false, new Vector3(10, 0, 0));
        // a show before the badge was applied is kept and spawns at the creation
        other.Show();
        Assert.That(other.Pending, Is.True);
        other.Hide();
        Assert.That(other.Pending, Is.False);
        other.Show();
        other.CreateCouple(w, Badge(Opener, false, "Me"), Gem);
        Assert.That(other.Pending, Is.False);
        Assert.That(w.Spawned.Single().At, Is.EqualTo(new Vector3(5, 0, 0)));
        Assert.That(w.Sent, Is.Empty, "a show spawns without a send");
        other.Show();
        Assert.That(w.Spawned.Count, Is.EqualTo(1), "one effect at a time");
        other.Hide();
        Assert.That(w.Removed, Is.EqualTo(new[] { w.Spawned[0].Id }));
        Assert.That(w.Sent, Is.Empty);
        _ = me;
    }

    [Test]
    public void ReleasingSendsModeOneForAnySystem()
    {
        // the destructor's SetEffectMode(1): the opener's removes its effect and sends; the partner's (unlinked) only sends,
        // with the opener's id
        var w = new World();
        var me = w.Add(Opener, 1, 0, "Me", true, Vector3.Zero);
        var other = w.Add(Partner, 2, 1, "Other", false, new Vector3(10, 0, 0));
        other.CreateCouple(w, Badge(Opener, false, "Me"), Gem);
        me.CreateCouple(w, Badge(Partner, true, "Other"), Gem);
        me.Update();
        int effect = me.Couple.EffectId;
        me.Release();
        Assert.That(me.Couple, Is.Null);
        Assert.That(w.Removed, Is.EqualTo(new[] { effect }));
        other.Release();
        Assert.That(Wire(w), Is.EqualTo(new[] { "mode0:1001", "mode1:1001", "mode1:1001" }));
        Assert.That(Bytes(w.Sent.Last()), Is.EqualTo(new byte[] { 1, 0xE9, 3, 0, 0, 0, 0, 0, 0 }));
        // a new badge replaces the system: the new one is made first, then the old one released
        w.Sent.Clear();
        me.CreateCouple(w, Badge(Partner, true, "Stale"), Gem);
        me.CreateCouple(w, Badge(Partner, true, "Other"), Gem);
        Assert.That(Wire(w), Is.EqualTo(new[] { "name:2002", "mode1:1001" }));
    }

    [Test]
    public void TheBadgeGemPlaysTheWearersEffectAndMakesTheCouple()
    {
        var w = new World();
        var me = w.Add(Opener, 1, 1, "Me", true, Vector3.Zero);
        w.Add(Partner, 2, 0, "Other", false, new Vector3(10, 0, 0));
        var onPc = new List<string>();
        var stopped = new List<int>();
        var gem = new CharEffectGem(w, me, e => { onPc.Add(e); return 50 + onPc.Count; }, stopped.Add);
        var c = new ChatBalloonController();
        var g = new GemEquip(c, new ChatBalloonController.Actor { ObjectId = 1, IsPc = true }) { CharEffect = gem };
        var badge = Badge(Partner, true, "Other");
        // an unknown gem id: nothing
        g.Equip(GemEquip.CharEffectType, new GemEquip.Gem(BadgeItem, 123) { Record = badge });
        Assert.That((onPc.Count, me.Couple), Is.EqualTo((0, (CoupleEffect)null)));
        g.Equip(GemEquip.CharEffectType, new GemEquip.Gem(BadgeItem, Gem) { Record = badge });
        Assert.That(onPc, Is.EqualTo(new[] { "f.xml" }));
        Assert.That((me.Couple?.PartnerId, me.Couple?.CoupleType, me.Couple?.Linked), Is.EqualTo(((long?)Partner, (int?)BadgeItem, (bool?)true)));
        // the name the server sends goes to the worn item of that uid, not to the system
        Assert.That(g.SetCoupleName(badge.Uid + 1, "New"), Is.False);
        Assert.That(g.SetCoupleName(badge.Uid, "New"), Is.True);
        Assert.That((badge.CoupleName, me.CoupleName), Is.EqualTo(("New", "Other")));
        // another gem type does not touch it; the removal stops the effects and clears the couple with a mode 1
        g.Remove(1);
        Assert.That(me.Couple, Is.Not.Null);
        g.Remove(GemEquip.CharEffectType);
        Assert.That(stopped, Is.EqualTo(new[] { 51 }));
        Assert.That((me.Couple, gem.EffectCount), Is.EqualTo(((CoupleEffect)null, 0)));
        Assert.That(Wire(w).Last(), Is.EqualTo("mode1:1001"));
        Assert.That(g.SetCoupleName(badge.Uid, "X"), Is.False, "no badge worn");
    }
}
