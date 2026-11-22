using Ms2.Audio;
using NUnit.Framework;

namespace Ms2.Audio.Tests;

public class HitSoundTableTests
{
    const string Xml = """
        <ms2>
          <note AttackMaterial="9" DefenceMeterial="9" soundID="1"/>
          <hitSound AttackMaterial="1" DefenceMeterial="1" soundID="10100101"/>
          <hitSound AttackMaterial="1" DefenceMeterial="1" soundID="10100102"/>
          <other AttackMaterial="2" DefenceMeterial="3" soundID="7"/>
          <hitSound DefenceMeterial="4" soundID="8"/>
        </ms2>
        """;

    [Test]
    public void APairPicksOneOfItsSoundsByTheRandomNumber()
    {
        var t = HitSoundTable.Parse(Xml);
        Assert.That(t.Get(1, 1, () => 0), Is.EqualTo(10100101));
        Assert.That(t.Get(1, 1, () => 3), Is.EqualTo(10100102));
        Assert.That(t.Get(1, 2, () => 0), Is.EqualTo(0), "no sounds for the pair");
    }

    [Test]
    public void EveryElementFromTheFirstHitSoundOnCountsAndAMissingAttributeIsZero()
    {
        var t = HitSoundTable.Parse(Xml);
        Assert.That(t.Get(9, 9, () => 0), Is.EqualTo(0), "before the first hitSound");
        Assert.That(t.Get(2, 3, () => 0), Is.EqualTo(7), "a sibling of another name after it");
        Assert.That(t.Get(0, 4, () => 0), Is.EqualTo(8));
    }

    [Test]
    public void TheEventPathIsTheAttackHitCategoryAndTheEightDigitName()
    {
        Assert.That(HitSoundTable.EventPath(1234), Is.EqualTo("ms2sound/sound/attack/hit/00001234"));
    }
}
