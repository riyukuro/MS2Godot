using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// SkillDamage modes 5 (region skill) and 6 (splash): CSceneStateGame::OnRegionSkillDamage (scenestategame.cpp:7766-7827) and
/// OnSplashDamage (:5730-5756) make one CSplashDamageRecord per target (mode 5) or per owner (mode 6); PCBase's
/// OnSplashDamageRecord (pcbase.cpp:3970-4006) gives one vfunc_111 per hit with the absolute damage and the hit's type.
/// DamageNumbers.Records lists modes 1/3/4 only today. Modes 5/6 carry the targets and hits the reader already fills
/// (Combat.cs:123-140).
/// </summary>
public class RcDamageNumbersSplashTests
{
    static SkillDamage Damage(byte mode) => new()
    {
        Mode = mode,
        OwnerId = 900,
        SkillId = 80_010_051,
        Level = 1,
        Targets =
        {
            new HitTarget(10, new[] { new Hit(0, 100), new Hit(2, -40) }, default, default),
            new HitTarget(11, new[] { new Hit(8, 7) }, default, default),
        },
    };

    [Ignore("mode 5 makes no numbers")]
    [Test]
    public void Gms2Mode5ShowsEveryRegionHit()
    {
        var numbers = new List<DamageNumber>();
        DamageNumbers.Records(Damage(5), numbers, _ => true, null);
        Assert.That(numbers, Is.EqualTo(new[]
        {
            new DamageNumber(10, 100, 0, 900), new DamageNumber(10, 40, 2, 900), new DamageNumber(11, 7, 3, 900),
        }));
    }

    [Ignore("mode 6 makes no numbers")]
    [Test]
    public void Gms2Mode6ShowsEverySplashHit()
    {
        var numbers = new List<DamageNumber>();
        DamageNumbers.Records(Damage(6), numbers, _ => true, null);
        // OnSplashDamage's record is Init(ownerId, 0, ...): the target is the entry's id and the caster 0
        Assert.That(numbers, Is.EqualTo(new[]
        {
            new DamageNumber(10, 100, 0, 0), new DamageNumber(10, 40, 2, 0), new DamageNumber(11, 7, 3, 0),
        }));
    }
}
