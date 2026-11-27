using Ms2.Formats.Pack;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// UIDamageText's model: the names it passes the movie (GetGradeName, GetDamageValueText, GetTargetKindName,
/// FUN_141914890, FUN_1419149d0), the movie factory's picks, InsertDamageInfo's list order and counters, and the
/// UpdateNormals stacking pass.
/// </summary>
public class DamageTextRulesTests
{
    [Test]
    public void GradesAndValues()
    {
        Assert.That(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }.Select(DamageTextNames.GradeName),
            Is.EqualTo(new[] { "Normal", "Critical", "Normal", "Normal", "", "Recover", "Recover", "Recover" }));
        Assert.That(DamageTextNames.ValueText(120, 0), Is.EqualTo("120"));
        Assert.That(DamageTextNames.ValueText(120, 1), Is.EqualTo("120"));
        Assert.That(DamageTextNames.ValueText(120, 5), Is.EqualTo("+120"));
        Assert.That(DamageTextNames.ValueText(120, 2), Is.EqualTo("Miss"));
        Assert.That(DamageTextNames.ValueText(120, 3), Is.EqualTo("Block"));
        Assert.That(DamageTextNames.ValueText(120, 6), Is.EqualTo(""));
        Assert.That(DamageTextNames.TargetKindName(0, true), Is.EqualTo("User"));
        Assert.That(DamageTextNames.TargetKindName(0, false), Is.EqualTo("Monster"));
        Assert.That(DamageTextNames.TargetKindName(1, true), Is.EqualTo("Monster"));
        Assert.That(DamageTextNames.GainValueText(7, 2), Is.EqualTo("+7"));
        Assert.That(DamageTextNames.GainGradeName(1), Is.EqualTo("Meso"));
        Assert.That(DamageTextNames.GainShows(2, false, false), Is.True);
        Assert.That(DamageTextNames.GainShows(1, false, true), Is.False);
        Assert.That(DamageTextNames.GainShows(0, false, true), Is.True);
        Assert.That(DamageTextNames.QuestExp(0xc1f) && DamageTextNames.QuestExp(0xc22), Is.True);
        Assert.That(DamageTextNames.QuestExp(0xc1e) || DamageTextNames.QuestExp(0xc23) || DamageTextNames.QuestExp(0), Is.False);
    }

    /// <summary>
    /// A number on another player names its target User or Monster by bUser (uidamagetext.cpp:356-372): the target's
    /// PC::vfunc_122 on a PC caster (pc.cpp:3112, pcbase.cpp:2784) and the caster's forceApplyTarget bit 2, the own PC always
    /// User. Every branch of both, with the field's pkMode on and off.
    /// </summary>
    [Test]
    public void TargetUserEveryCase()
    {
        static Ms2.Ui.Skills.HitCandidate Pc(int id, long party = 0, bool pvp = false, bool point = false, int team = 1, int fat = 0) =>
            new() { Id = id, Kind = Ms2.Ui.Skills.HitKind.Pc, Team = team, PartyId = party, PvpOnOff = pvp, PvPPoint = point, ForceApplyTarget = fat };
        string Name(Ms2.Ui.Skills.HitCandidate target, Ms2.Ui.Skills.HitCandidate caster, bool pk, bool targetMe = false, bool casterPc = true)
        {
            bool hostile = Ms2.Ui.Skills.SkillHit.PcHostile(target, caster, pk);
            return DamageTextNames.TargetKindName(DamageTextNames.TargetKind(true),
                DamageTextNames.TargetUser(targetMe, casterPc, hostile, caster.ForceApplyTarget));
        }
        var cases = new (string what, Ms2.Ui.Skills.HitCandidate target, Ms2.Ui.Skills.HitCandidate caster, bool pk, bool targetMe, bool casterPc, string name)[]
        {
            ("same team, no pk", Pc(1), Pc(2), false, false, true, "User"),
            ("forceApplyTarget 2", Pc(1), Pc(2, fat: 2), false, false, true, "Monster"),
            ("forceApplyTarget 1", Pc(1), Pc(2, fat: 1), false, false, true, "User"),
            ("forceApplyTarget 3", Pc(1), Pc(2, fat: 3), false, false, true, "Monster"),
            ("the target's forceApplyTarget is not read", Pc(1, fat: 2), Pc(2), false, false, true, "User"),
            ("other team, no pk", Pc(1, team: 0), Pc(2), false, false, true, "Monster"),
            ("other PvP point, no pk", Pc(1, point: true, team: 0), Pc(2), false, false, true, "User"),
            ("pk, no party", Pc(1), Pc(2), true, false, true, "Monster"),
            ("pk, both party 0", Pc(1, party: 0), Pc(2, party: 0), true, false, true, "Monster"),
            ("pk, same party", Pc(1, party: 7), Pc(2, party: 7), true, false, true, "User"),
            ("pk, other party", Pc(1, party: 7), Pc(2, party: 8), true, false, true, "Monster"),
            ("pk, target pvp on/off", Pc(1, pvp: true), Pc(2), true, false, true, "User"),
            ("pk, caster pvp on/off", Pc(1), Pc(2, pvp: true), true, false, true, "User"),
            ("pk, the PC itself", Pc(1), Pc(1), true, false, true, "User"),
            ("pk, a PvP point mismatch does not matter", Pc(1, point: true), Pc(2), true, false, true, "Monster"),
            ("pk, same party with forceApplyTarget 2", Pc(1, party: 7), Pc(2, party: 7, fat: 2), true, false, true, "Monster"),
            ("the own PC, hostile", Pc(1), Pc(2, fat: 2), true, true, true, "User"),
            ("a caster that is no PC", Pc(1), Pc(2, fat: 2), true, false, false, "User"),
        };
        foreach (var c in cases)
            Assert.That(Name(c.target, c.caster, c.pk, c.targetMe, c.casterPc), Is.EqualTo(c.name), c.what);
        Assert.That(DamageTextNames.TargetKindName(DamageTextNames.TargetKind(false), false), Is.EqualTo("Monster"), "an npc target");
    }

    [Test]
    public void FactoryPicks()
    {
        var npc = DamageLook.For("120", "Monster", "Normal", "", "", myDamage: true);
        Assert.That((npc.Glyphs, npc.Ani, npc.Container), Is.EqualTo(("Monster", DamageAni.NpcNormal, DamageLook.MyNormal)));
        var crit = DamageLook.For("120", "Monster", "Critical", "", "7", myDamage: true);
        Assert.That((crit.Glyphs, crit.Ani, crit.CritText, crit.Container, crit.NumberSpacing),
            Is.EqualTo(("MonsterCri", DamageAni.NpcCritical, "dmg_mc_monstercri", DamageLook.MyCritical, -17)));
        var otherCrit = DamageLook.For("120", "Monster", "Critical", "User");
        Assert.That((otherCrit.CritText, otherCrit.Container), Is.EqualTo(("dmg_mc_monstercri", DamageLook.ListCritical)), "caster dropped");
        var miss = DamageLook.For("Miss", "Monster", "Normal");
        Assert.That((miss.Ani, miss.ExEffect), Is.EqualTo((DamageAni.Miss, "dmg_mc_miss_monster")));
        var block = DamageLook.For("Block", "User", "Normal");
        Assert.That((block.Ani, block.ExEffect), Is.EqualTo((DamageAni.BlockPc, "dmg_mc_block_pc")));
        Assert.That(DamageLook.For("+50", "User", "Recover").Ani, Is.EqualTo(DamageAni.Recover));
        Assert.That(DamageLook.For("", "Monster", "Recover").Glyphs, Is.Null, "no MonsterRecover array: no number");
        var meso = DamageLook.ForItem("300", "User", "Meso");
        Assert.That((meso.Glyphs, meso.Ani, meso.Container, meso.SignSpacing), Is.EqualTo(("Meso", DamageAni.Meso, DamageLook.MyNormal, -14)));
        var item = DamageLook.ForItem("+2", "User", "Item");
        Assert.That((item.Glyphs, item.Ani, item.NumberSpacing), Is.EqualTo(("Monster", DamageAni.ItemDown, -11)));
        Assert.That(DamageLook.GlyphKey('+'), Is.EqualTo("Plus"));
        Assert.That(DamageLook.GlyphKey('7'), Is.EqualTo("7"));
        Assert.That(DamageLook.GlyphKey(','), Is.Null);
    }

    sealed class Info : DamageInfo { public Info(object owner, int grade = 0) { Owner = owner; Grade = grade; } }

    static int[] Order(DamageList<Info> l, Dictionary<Info, int> ids)
    {
        var r = new List<int>();
        for (var n = l.First; n != null; n = n.Next) r.Add(ids[n.Value]);
        return r.ToArray();
    }

    [Test]
    public void AppendKeepsTheHeadAndDropsAtTheLastPosition()
    {
        var l = new DamageList<Info>();
        var a = new object();
        var e = Enumerable.Range(0, 4).Select(_ => new Info(a)).ToArray();
        var ids = e.Select((x, i) => (x, i)).ToDictionary(t => t.x, t => t.i);
        Assert.That(l.Insert(true, e[0], 1), Is.Null);
        Assert.That(l.Insert(true, e[1], 1), Is.Null);
        Assert.That(Order(l, ids), Is.EqualTo(new[] { 1, 0 }));
        Assert.That(l.Insert(true, e[2], 1), Is.SameAs(e[0]), "over: the first appended (the last position) leaves");
        Assert.That(Order(l, ids), Is.EqualTo(new[] { 2, 1 }));
        Assert.That(l.Insert(true, e[3], 1), Is.SameAs(e[1]), "the position moved to the one before");
        Assert.That(e.Select(x => x.Index), Is.EqualTo(new[] { 0, 1, 2, 3 }));
        Assert.That(l.TryCounter(a, out var c) ? c.Count : -1, Is.EqualTo(2));
    }

    [Test]
    public void InsertAfterDropsTheTailButCountsTheNewOwner()
    {
        var l = new DamageList<Info>();
        object a = new(), b = new();
        var x0 = new Info(a); var x1 = new Info(b); var x2 = new Info(b); var x3 = new Info(a);
        var ids = new Dictionary<Info, int> { [x0] = 0, [x1] = 1, [x2] = 2, [x3] = 3 };
        l.Insert(true, x0, 1);
        l.Insert(false, x1, 1);
        Assert.That(Order(l, ids), Is.EqualTo(new[] { 0, 1 }), "after the last position");
        Assert.That(l.Insert(false, x2, 1), Is.SameAs(x1), "the tail leaves");
        Assert.That(Order(l, ids), Is.EqualTo(new[] { 0, 2 }));
        l.TryCounter(b, out var cb);
        Assert.That(cb.Count, Is.EqualTo(1), "b's counter dropped to 0 and left, then a new one for the new entry");
        l.Insert(false, x3, 1);
        Assert.That(Order(l, ids), Is.EqualTo(new[] { 0, 3 }), "b's entry was the tail");
        Assert.That(l.TryCounter(a, out var ca) ? ca.Count : -1, Is.EqualTo(1), "a's count dropped instead of b's: 1 - 1, then 1");
        Assert.That(l.TryCounter(b, out var cb2) ? cb2.Count : -1, Is.EqualTo(1), "b keeps a count with no entry left");
    }

    [Test]
    public void CounterIndexWrapsAndDecrementDrops()
    {
        var l = new DamageList<Info>(maxIndex: 3);
        var a = new object();
        var e = Enumerable.Range(0, 4).Select(_ => new Info(a)).ToArray();
        foreach (var x in e) l.Insert(true, x, 100);
        Assert.That(e.Select(x => x.Index), Is.EqualTo(new[] { 0, 1, 2, 0 }));
        l.Decrement(a, 3);
        Assert.That(l.TryCounter(a, out _), Is.True);
        l.Decrement(a, 1);
        Assert.That(l.TryCounter(a, out _), Is.False);
        l.Insert(true, new Info(a), 100);
        l.Decrement(a, -1);
        Assert.That(l.HasCounters, Is.False);
    }

    [Test]
    public void InfoClock()
    {
        var i = new Info(null);
        i.Tick(500);
        Assert.That(i.Elapsed, Is.EqualTo(0u));
        i.Begin(500);
        i.Begin(900);
        i.Tick(650);
        Assert.That((i.Elapsed, i.MayJump), Is.EqualTo((150u, true)));
        i.Tick(1501);
        Assert.That(i.Expired, Is.True);
        i.Tick(100);
        Assert.That((i.Start, i.Elapsed), Is.EqualTo((100u, 0u)), "a clock that went back restarts");
        i.State = 3;
        Assert.That(i.RemovesClipOnDrop, Is.True);
        i.State = 1;
        Assert.That(i.RemovesClipOnDrop, Is.False);
    }

    static readonly DamageTextLayout Layout = new(30, 3, 25, 35, 1000, 0.8f, 0.5f, 2500);

    [Test]
    public void LineHeights()
    {
        Assert.That(Layout.Lines(100, 0), Is.EqualTo((30, 30)));
        Assert.That(Layout.Lines(150, 2), Is.EqualTo((45, 45)));
        Assert.That(Layout.Lines(50, 3), Is.EqualTo((15, 24)));
    }

    [Test]
    public void NewestThreeShowAndOlderOnesLift()
    {
        var l = new DamageList<Info>();
        var a = new object();
        var e = Enumerable.Range(0, 5).Select(_ => new Info(a) { Mine = true }).ToArray();
        foreach (var x in e) l.Insert(true, x, 35);
        var pass = new DamageNormalsPass(Layout, 100, 0);
        var hidden = new List<bool>();
        var lifts = new List<float>();
        for (var n = l.First; n != null; n = n.Next)
        {
            bool h = pass.Hidden(n.Value, l);
            hidden.Add(h);
            if (!h) lifts.Add(pass.Place(n.Value).Lift);
        }
        Assert.That(hidden, Is.EqualTo(new[] { false, false, false, true, true }), "head first: indices 4, 3, 2 show");
        Assert.That(lifts, Is.EqualTo(new[] { 0f, 0f, 30f }), "behind 0, 1, 2: lifted max(0, behind - 1) lines");
    }

    [Test]
    public void CriticalLiftsTwoLinesAndTakesTheLineBehindIt()
    {
        var l = new DamageList<Info>();
        var a = new object();
        var n0 = new Info(a) { Mine = true };
        var c1 = new Info(a, grade: 1);
        l.Insert(true, n0, 35);
        l.Insert(true, c1, 35);
        c1.Begin(1); c1.Tick(500);
        n0.Begin(1); n0.Tick(500);
        var pass = new DamageNormalsPass(Layout, 100, 1);
        Assert.That(pass.Hidden(c1, l), Is.False);
        var crit = pass.Place(c1);
        Assert.That(crit, Is.EqualTo((48f, false)), "another's crit: two lines of gap x DamageOtherScale (24)");
        Assert.That(pass.Hidden(n0, l), Is.False);
        var norm = pass.Place(n0);
        Assert.That(norm, Is.EqualTo((0f, false)), "one behind, but the stacked crit takes it: behind 2 - 1 - 0 - 1 = 0");
    }

    [Test]
    public void TableLayout()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        using var packs = new PackSet(d);
        var l = DamageTextLayout.From(FeatureElements.Constants(packs));
        // the constants table of KR Live and of the restore data: DamageGap 30, DamageRenderCount 3, DamageRenderTotalCount 25,
        // DamageOtherScale 0.5, DamageOtherAlpha 0.3, DamageDistance 2500
        Assert.That(l, Is.EqualTo(new DamageTextLayout(30, 3, 25, 35, 1000, 0.5f, 0.3f, 2500f)), $"layout {l}");
    }
}
