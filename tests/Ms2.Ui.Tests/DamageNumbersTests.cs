using Ms2.Formats.Options;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using Ms2.Ui.Skills;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The damage numbers of a skill damage packet (CSceneStateGame::FUN_14171cdf0, CDamageRecord::AddDamage,
/// PCBase::FUN_141671c20 -> vfunc_111) and UIDamageText's show test (FUN_141913bd0).
/// </summary>
public class DamageNumbersTests
{
    [Test]
    public void EveryHitOfEveryTargetIsANumberWithTheOwnerAsCaster()
    {
        var d = new SkillDamage { Mode = 1, OwnerId = 77, SkillId = 100, Level = 1, Motion = 0, AttackPoint = 0 };
        d.Targets.Add(new HitTarget(5, new[] { new Hit(0, 120), new Hit(1, -300) }, default, default));
        d.Targets.Add(new HitTarget(6, new[] { new Hit(8, 40) }, default, default));
        d.Targets.Add(new HitTarget(7, new Hit[0], default, default));
        var list = new List<DamageNumber>();
        DamageNumbers.Records(d, list, null, Damage);
        Assert.That(list, Is.EqualTo(new[]
        {
            new DamageNumber(5, 120, 0, 77), new DamageNumber(5, 300, 1, 77), new DamageNumber(6, 40, 3, 77),
        }), "abs value, type 8 becomes 3, a target without hits gives none");
        list.Clear();
        DamageNumbers.Records(new SkillDamage { Mode = 3 }, list, null, Damage);
        Assert.That(list, Is.Empty);
    }

    /// <summary>A dot (mode 3, OnDotDamage scenestategame.cpp:5663 into PCBase::OnDotDamageRecord pcbase.cpp:3958) is one number of
    /// the record's first type on the object hit, the attacker as caster; the i64 damage reaches it whole.</summary>
    [Test]
    public void ADotIsOneNumberAndKeepsAnI64Damage()
    {
        var d = new SkillDamage { Mode = 3, OwnerId = 10, TargetId = 31, DotHit = new Hit(1, -3_000_000_000L) };
        var list = new List<DamageNumber>();
        DamageNumbers.Records(d, list, id => id == 31, Damage);
        Assert.That(list, Is.EqualTo(new[] { new DamageNumber(31, 3_000_000_000L, 1, 10) }));
        char[] buf = new char[40];
        Assert.That(new string(buf, 0, DamageTextNames.ValueChars(list[0].Value, list[0].Grade, buf)), Is.EqualTo("3000000000"), "the shown text");
        list.Clear();
        DamageNumbers.Records(d, list, id => id == 10, Damage);
        Assert.That(list, Is.Empty, "the object hit is none: the handler returns");
    }

    [Test]
    public void APlayerTargetOutsideTheSightRangeGivesNoNumbers()
    {
        var d = new SkillDamage { Mode = 1, OwnerId = 77, SkillId = 100, Level = 1, Motion = 0, AttackPoint = 0 };
        d.Targets.Add(new HitTarget(5, new[] { new Hit(0, 120) }, default, default));
        d.Targets.Add(new HitTarget(6, new[] { new Hit(0, 40) }, default, default));
        d.Targets.Add(new HitTarget(9, new[] { new Hit(0, 10) }, default, default));
        // 5 and 6 are players 1000 and 999 away from the local player; 9 is an npc far away
        System.Func<int, System.Numerics.Vector3?> pc = id => id == 5 ? new System.Numerics.Vector3(1000, 0, 0)
            : id == 6 ? new System.Numerics.Vector3(0, 999, 0) : null;
        var list = new List<DamageNumber>();
        DamageNumbers.Records(d, list, null, Damage, pc, new FieldSight(999f, System.Numerics.Vector3.Zero));
        Assert.That(list, Is.EqualTo(new[] { new DamageNumber(6, 40, 0, 77), new DamageNumber(9, 10, 0, 77) }),
            "a player farther than the range gives none, one at the range and an npc do");
        foreach (var sight in new[] { new FieldSight(null, System.Numerics.Vector3.Zero), new FieldSight(999f, null), default })
        {
            list.Clear();
            DamageNumbers.Records(d, list, null, Damage, pc, sight);
            Assert.That(list, Has.Count.EqualTo(3), "no range or no local player culls nothing");
        }
        list.Clear();
        DamageNumbers.Records(d, list, null, Damage, pc, new FieldSight(1000f, System.Numerics.Vector3.Zero));
        Assert.That(list, Has.Count.EqualTo(3), "at exactly the range is inside");
        // a dot on a player outside the range (OnDotDamageRecord, pcbase.cpp:3962), none on an npc there
        var dot = new SkillDamage { Mode = 3, OwnerId = 77, TargetId = 5, DotHit = new Hit(0, 50) };
        list.Clear();
        DamageNumbers.Records(dot, list, id => true, Damage, pc, new FieldSight(999f, System.Numerics.Vector3.Zero));
        Assert.That(list, Is.Empty, "a dot on a player outside the range");
        dot.TargetId = 6;
        DamageNumbers.Records(dot, list, id => true, Damage, pc, new FieldSight(999f, System.Numerics.Vector3.Zero));
        dot.TargetId = 9;
        DamageNumbers.Records(dot, list, id => true, Damage, pc, new FieldSight(999f, System.Numerics.Vector3.Zero));
        Assert.That(list, Is.EqualTo(new[] { new DamageNumber(6, 50, 0, 77), new DamageNumber(9, 50, 0, 77) }), "inside, and an npc");
    }

    /// <summary>Skill 100: level 1 with one motion of two attacks, the first with rate 1.5 and the second with a &lt;damage&gt; of no
    /// value; level 2 with an attack of no &lt;damage&gt;. Skill 101 has no &lt;basic&gt;, so no levels.</summary>
    internal static readonly SkillDamageTable Damage = new(() => new TextReader[] { new StringReader(@"<ms2>
  <skill id=""100""><basic/>
    <level value=""1""><motion><attack><damage rate=""1.5"" count=""3""/></attack><attack><damage push=""1""/></attack></motion></level>
    <level value=""2""><motion><attack/></motion></level>
  </skill>
  <skill id=""101""><level value=""1""><motion><attack><damage value=""9""/></attack></motion></level></skill>
</ms2>") }, rows => rows);

    [Test]
    public void OnlyAnAttackWithDamageGivesNumbers()
    {
        // CDamageRecord::Init fails for an unknown skill or level (the handler returns); FUN_141671c20 shows nothing for an
        // attack the level lacks or whose damage has no value, constant, rate or max-hp rate (HasDamage)
        SkillDamage Packet(int skill, short level, byte motion, byte attack)
        {
            var d = new SkillDamage { Mode = 1, OwnerId = 77, SkillId = skill, Level = level, Motion = motion, AttackPoint = attack };
            d.Targets.Add(new HitTarget(5, new[] { new Hit(0, 120) }, default, default));
            return d;
        }
        var list = new List<DamageNumber>();
        foreach (var (skill, level, motion, attack, numbers) in new (int, short, byte, byte, int)[]
                 { (100, 1, 0, 0, 1), (100, 1, 0, 1, 0), (100, 1, 0, 2, 0), (100, 1, 1, 0, 0), (100, 2, 0, 0, 0), (100, 3, 0, 0, 0), (101, 1, 0, 0, 0), (102, 1, 0, 0, 0) })
        {
            list.Clear();
            DamageNumbers.Records(Packet(skill, level, motion, attack), list, null, Damage);
            Assert.That(list.Count, Is.EqualTo(numbers), $"skill {skill} level {level} motion {motion} attack {attack}");
            Assert.That(DamageNumbers.HasDamage(Packet(skill, level, motion, attack), Damage), Is.EqualTo(numbers > 0));
        }
        Assert.That(DamageNumbers.HasDamage(Packet(100, 1, 0, 0), null), Is.False, "no table");
    }

    static int Pc(int id) => id == 20 ? 2 : id == 30 ? 4 : 0;   // 20 a party member, 30 another player, the rest npcs

    [Test]
    public void TheDefaultOptionsShowAllMyHitsAndPartyHitsTaken()
    {
        var o = new GameOption.Battle();   // give 7 (all), take 3 (mine, party)
        Assert.That(DamageNumbers.Shows(o, 0, 100, 1, 1, Pc), Is.True, "my hit on an npc");
        Assert.That(DamageNumbers.Shows(o, 0, 1, 100, 1, Pc), Is.True, "an npc's hit on me");
        Assert.That(DamageNumbers.Shows(o, 0, 100, 30, 1, Pc), Is.True, "another player's hit on an npc: give is all");
        Assert.That(DamageNumbers.Shows(o, 0, 30, 100, 1, Pc), Is.False, "an npc's hit on another player: take is mine, party");
        Assert.That(DamageNumbers.Shows(o, 0, 20, 100, 1, Pc), Is.True);
        Assert.That(DamageNumbers.Shows(o, 4, 100, 1, 1, Pc), Is.False, "grade 4 never shows");
        o.GiveDamage = 1;
        Assert.That(DamageNumbers.Shows(o, 0, 100, 30, 1, Pc), Is.False);
        Assert.That(DamageNumbers.Shows(o, 0, 30, 30, 1, Pc), Is.False, "a caster that is the target takes its mask");
    }

    [Test]
    public void RecoverGradesFollowTheRecoverOptions()
    {
        var o = new GameOption.Battle { DisplayMyTakeRecovery = false };
        Assert.That(DamageNumbers.Shows(o, 5, 1, 1, 1, Pc), Is.False, "the target bit is tested first");
        Assert.That(DamageNumbers.Shows(o, 6, 20, 1, 1, Pc), Is.True, "my heal on a party member");
        o.DisplayMyGiveRecovery = false;
        Assert.That(DamageNumbers.Shows(o, 7, 20, 1, 1, Pc), Is.False);
        Assert.That(DamageNumbers.Shows(o, 7, 20, 30, 1, Pc), Is.True, "party recovery");
        Assert.That(DamageNumbers.Shows(o, 7, 30, 30, 1, Pc), Is.False);
    }

    [Test]
    public void RecoveryValuesAreNumbersOnTheOwnerWithTheTargetAsCaster()
    {
        // FUN_14171d820: pOwner->vfunc_111(value, 5/6/7, nTargetId) for each nonzero value, only with the show byte and
        // an owner in the field
        var d = new SkillDamage { Mode = 4, TargetId = 31, OwnerId = 10, Value1 = 120, Value2 = 0, Value3 = 4294967295L, ShowValues = true };
        var list = new List<DamageNumber>();
        DamageNumbers.Records(d, list, id => id == 10, Damage);
        Assert.That(list, Is.EqualTo(new[] { new DamageNumber(10, 120, 5, 31), new DamageNumber(10, 4294967295L, 7, 31) }));
        list.Clear();
        DamageNumbers.Records(d, list, id => id == 31, Damage);
        Assert.That(list, Is.Empty, "no owner object: the client returns");
        d.ShowValues = false;
        DamageNumbers.Records(d, list, id => id == 10, Damage);
        Assert.That(list, Is.Empty, "no show byte");
    }
}
