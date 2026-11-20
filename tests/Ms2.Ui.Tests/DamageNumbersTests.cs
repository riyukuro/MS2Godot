using Ms2.Formats.Options;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
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
        var d = new SkillDamage { Mode = 1, OwnerId = 77 };
        d.Targets.Add(new HitTarget(5, new[] { new Hit(0, 120), new Hit(1, -300) }, default, default));
        d.Targets.Add(new HitTarget(6, new[] { new Hit(8, 40) }, default, default));
        d.Targets.Add(new HitTarget(7, new Hit[0], default, default));
        var list = new List<DamageNumber>();
        DamageNumbers.Records(d, list);
        Assert.That(list, Is.EqualTo(new[]
        {
            new DamageNumber(5, 120, 0, 77), new DamageNumber(5, 300, 1, 77), new DamageNumber(6, 40, 3, 77),
        }), "abs value, type 8 becomes 3, a target without hits gives none");
        list.Clear();
        DamageNumbers.Records(new SkillDamage { Mode = 3 }, list);
        Assert.That(list, Is.Empty);
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
}
