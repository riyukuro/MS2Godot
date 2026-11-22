using System.Numerics;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>EffectSkipMgr's view distances (LoadOption, IsOutOfView) and FUN_1411a5110's sound checks.</summary>
public class EffectSkipTableTests
{
    const string Xml = """
        <ms2>
          <option2>
            <sound>
              <note caster="PC" viewDistance="9"/>
              <element caster="myPC" viewDistance="0"/>
              <element caster="PC" viewDistance="3000"/>
              <other caster="monster" viewDistance="1500"/>
              <element caster="Boss" viewDistance="7"/>
              <element caster="background"/>
            </sound>
            <nif><element caster="PC" viewDistance="2500"/></nif>
          </option2>
          <option10><sound><element caster="PC" viewDistance="111"/></sound></option10>
        </ms2>
        """;

    [Test]
    public void TheOptionIsTheQualityPlusOneInBaseEightAndItsElementsFromTheFirstElementOn()
    {
        var t = EffectSkipTable.Parse(Xml, 1);
        Assert.That(t.ViewDistance(EffectSkipTable.RowSound, 3), Is.EqualTo(3000f));
        Assert.That(t.ViewDistance(EffectSkipTable.RowSound, 5), Is.EqualTo(1500f), "any sibling element after the first element");
        Assert.That(t.ViewDistance(EffectSkipTable.RowSound, 6), Is.EqualTo(0f), "an unknown caster name");
        Assert.That(t.ViewDistance(EffectSkipTable.RowSound, 7), Is.EqualTo(0f), "no viewDistance");
        Assert.That(t.ViewDistance(EffectSkipTable.RowNif, 3), Is.EqualTo(2500f));
        Assert.That(EffectSkipTable.Parse(Xml, 7).ViewDistance(EffectSkipTable.RowSound, 3), Is.EqualTo(111f), "_itoa(8, 8) is \"10\"");
        Assert.That(EffectSkipTable.Parse(Xml, 2).ViewDistance(EffectSkipTable.RowSound, 3), Is.EqualTo(0f), "no option3");
    }

    [Test]
    public void ThePartIsOutOfViewPastANonZeroDistanceOfItsCaster()
    {
        var t = EffectSkipTable.Parse(Xml, 1);
        var eye = new Vector3(100, 200, 0);
        Assert.That(t.IsOutOfView(2, 3, eye + new Vector3(0, 2999, 0), eye), Is.False);
        Assert.That(t.IsOutOfView(2, 3, eye + new Vector3(0, 3000, 0), eye), Is.True, "not nearer than the distance");
        Assert.That(t.IsOutOfView(2, 1, eye + new Vector3(0, 99999, 0), eye), Is.False, "myPC's 0 never skips");
        Assert.That(t.IsOutOfView(2, 0, eye + new Vector3(0, 99999, 0), eye), Is.False, "caster 0");
        Assert.That(t.IsOutOfView(2, 3, eye + new Vector3(0, 99999, 0), null), Is.False, "no camera actor");
        Assert.That(t.IsOutOfView(2, 3, eye + new Vector3(0, 50, 0), eye, 40), Is.True, "the check's own distance");
    }

    [Test]
    public void ASoundEventIsHiddenByThePetOptionsOrOutOfViewAndScaledForOtherPlayers()
    {
        var t = EffectSkipTable.Parse(Xml, 1);
        var eye = Vector3.Zero;
        var near = new Vector3(10, 0, 0);
        Assert.That(EffectSkipTable.SoundEventVolume(t, 1, near, eye, 0.5f, 0.9f, false, false), Is.EqualTo(0.5f));
        Assert.That(EffectSkipTable.SoundEventVolume(t, 3, near, eye, 0.5f, 0.9f, false, false), Is.EqualTo(0.45f).Within(1e-6f));
        Assert.That(EffectSkipTable.SoundEventVolume(t, 3, new Vector3(5000, 0, 0), eye, 1, 0.9f, false, false), Is.EqualTo(-1f));
        Assert.That(EffectSkipTable.SoundEventVolume(t, 4, near, eye, 1, 1, true, false), Is.EqualTo(-1f), "DisplayShowOnlyMyPet hides 4");
        Assert.That(EffectSkipTable.SoundEventVolume(t, 2, near, eye, 1, 1, false, true), Is.EqualTo(-1f), "DisplayHideMyPet hides 2");
        Assert.That(EffectSkipTable.SoundEventVolume(t, 2, near, eye, 1, 1, true, false), Is.EqualTo(1f));
        Assert.That(EffectSkipTable.SoundEventVolume(null, 3, new Vector3(5000, 0, 0), eye, 1, 1, false, false), Is.EqualTo(1f));
    }

    /// <summary>The shipped table never skips an own sound: myPC's sound distance is 0 for every quality.</summary>
    [Test]
    public void TheShippedTableNeverSkipsAnOwnSound()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA not set");
        string xml = System.Text.Encoding.UTF8.GetString(new PackSet(data).Read("Xml", "table/effectoption.xml"));
        for (int q = 0; q <= 3; q++)
        {
            var t = EffectSkipTable.Parse(xml, q);
            Assert.That(t.ViewDistance(EffectSkipTable.RowSound, 1), Is.EqualTo(0f), $"quality {q}");
            Assert.That(t.ViewDistance(EffectSkipTable.RowSound, 3), Is.GreaterThan(0f), $"quality {q}: the option is read");
        }
    }
}
