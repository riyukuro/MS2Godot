using System.Numerics;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The field's properties by kind (WorldService m_780: ReadFieldProperties, AddFieldProperty, RemoveFieldProperty,
/// ClearFieldProperties, worldservice_1.cpp:5322-5378), the sight range FUN_141bd6810 copies (worldservice_1.cpp:7783) and
/// IsOutsideSightRange (worldservice_1.cpp:7956).</summary>
public class FieldPropertiesTests
{
    static FieldProperty.SightRange Sight(float range) => new(range, range, range, 115, true, 45, false);

    [Test]
    public void AListOrAnAddSetsEachKindAndARemoveDropsIt()
    {
        var f = new FieldProperties();
        Assert.That((f.SightRange, f.Weather), Is.EqualTo(((float?)null, (byte)0)));
        f.Apply(new FieldPropertyMessage.List(new FieldProperty[] { Sight(900), new FieldProperty.Weather(3), new FieldProperty.Gravity(2) }));
        Assert.That((f.SightRange, f.Weather, f.Count), Is.EqualTo(((float?)900, (byte)3, 3)));
        // a later list or add replaces the kind's entry and keeps the others (the list does not clear first)
        f.Apply(new FieldPropertyMessage.List(new FieldProperty[] { new FieldProperty.Weather(5) }));
        f.Apply(new FieldPropertyMessage.Add(Sight(1500)));
        Assert.That((f.SightRange, f.Weather, f.Count), Is.EqualTo(((float?)1500, (byte)5, 3)));
        Assert.That(f.Find(1), Is.EqualTo(new FieldProperty.Gravity(2)));
        f.Apply(new FieldPropertyMessage.Remove(6));
        f.Apply(new FieldPropertyMessage.Remove(7));
        f.Apply(new FieldPropertyMessage.Remove(9));   // not there: nothing
        f.Apply(null);
        Assert.That((f.SightRange, f.Weather, f.Count), Is.EqualTo(((float?)null, (byte)0, 1)));
        f.Clear();
        Assert.That(f.Count, Is.EqualTo(0));
    }

    [Test]
    public void OutsideTheSightRangeIsFartherThanTheRangeFromTheLocalPlayer()
    {
        var at = new Vector3(300, 400, 0);   // 500 from the origin
        Assert.That(new FieldSight(499, Vector3.Zero).IsOutside(at), Is.True);
        Assert.That(new FieldSight(500, Vector3.Zero).IsOutside(at), Is.False, "farther than, not as far as");
        Assert.That(new FieldSight(null, Vector3.Zero).IsOutside(at), Is.False, "no range");
        Assert.That(new FieldSight(1, null).IsOutside(at), Is.False, "no local player");
        Assert.That(default(FieldSight).IsOutside(at), Is.False);
    }

    sealed class Lights : IFieldLights
    {
        public Vector3 Ambient { get; set; } = new(0.5f, 0.5f, 0.5f);
        public Vector3 Sun { get; set; } = new(1, 1, 1);
    }

    static FieldProperty.AmbientLight Amb(byte r, byte g, byte b, bool fade, float seconds) => new(r, g, b, fade, seconds);
    static FieldProperty.DirectionalLight Dir(byte r, byte g, byte b, bool fade, float seconds) => new(r, g, b, 9, 9, 9, fade, seconds);

    [Test]
    public void ALightRecordWithoutTheFlagSetsTheColourAtOnce()
    {
        var f = new FieldProperties { Lights = new Lights() };
        var l = (Lights)f.Lights;
        f.Apply(new FieldPropertyMessage.Add(Amb(255, 51, 0, false, 5)));
        Assert.That(l.Ambient, Is.EqualTo(new Vector3(1f, 0.2f, 0f)));
        // the sun takes the first rgb; the second is unused (fieldpropertycli.cpp:235-236)
        f.Apply(new FieldPropertyMessage.Add(new FieldProperty.DirectionalLight(0, 255, 102, 200, 200, 200, false, 0)));
        Assert.That(l.Sun, Is.EqualTo(new Vector3(0f, 1f, 0.4f)));
        f.StepLights(10);
        Assert.That((l.Ambient, l.Sun), Is.EqualTo((new Vector3(1f, 0.2f, 0f), new Vector3(0f, 1f, 0.4f))), "no fade runs");
    }

    [Test]
    public void ALightRecordWithTheFlagFadesFromTheColourItHadOverTheSeconds()
    {
        var f = new FieldProperties { Lights = new Lights() };
        var l = (Lights)f.Lights;
        f.Apply(new FieldPropertyMessage.Add(Amb(255, 255, 255, true, 4)));
        Assert.That(l.Ambient, Is.EqualTo(new Vector3(0.5f, 0.5f, 0.5f)), "the apply itself changes nothing");
        f.StepLights(1);   // a quarter: 0.5 + 0.25 * (1 - 0.5)
        Assert.That(l.Ambient.X, Is.EqualTo(0.625f).Within(1e-6));
        f.StepLights(2);   // three quarters
        Assert.That(l.Ambient.X, Is.EqualTo(0.875f).Within(1e-6));
        f.StepLights(1);   // seconds == elapsed: the target is set, not the lerp at 1
        Assert.That(l.Ambient, Is.EqualTo(Vector3.One));
        l.Ambient = new Vector3(0.1f, 0.2f, 0.3f);
        f.StepLights(1);
        Assert.That(l.Ambient, Is.EqualTo(new Vector3(0.1f, 0.2f, 0.3f)), "the fade has ended: nothing moves it again");
    }

    [Test]
    public void ASunFadeMovesTheSunAndLeavesTheAmbientAlone()
    {
        var f = new FieldProperties { Lights = new Lights() };
        var l = (Lights)f.Lights;
        f.Apply(new FieldPropertyMessage.List(new FieldProperty[] { Dir(0, 0, 0, true, 2), Amb(0, 0, 0, false, 0) }));
        Assert.That((l.Sun, l.Ambient), Is.EqualTo((Vector3.One, Vector3.Zero)));
        f.StepLights(0.5f);
        Assert.That(l.Sun, Is.EqualTo(new Vector3(0.75f)));
        f.StepLights(5);
        Assert.That(l.Sun, Is.EqualTo(Vector3.Zero));
    }

    [Test]
    public void AFlagWithZeroSecondsSetsTheColourOnTheFirstStep()
    {
        var f = new FieldProperties { Lights = new Lights() };
        f.Apply(new FieldPropertyMessage.Add(Amb(255, 0, 0, true, 0)));
        f.StepLights(0.016f);
        Assert.That(((Lights)f.Lights).Ambient, Is.EqualTo(new Vector3(1, 0, 0)));
    }

    [Test]
    public void ARemoveStopsARunningFadeAndKeepsTheColourItHas()
    {
        // neither light class overrides the unapply: vfunc_5 is FieldProperty's empty one (fieldpropertycli.h:12-22)
        var f = new FieldProperties { Lights = new Lights() };
        var l = (Lights)f.Lights;
        f.Apply(new FieldPropertyMessage.Add(Amb(255, 255, 255, true, 4)));
        f.StepLights(1);
        f.Apply(new FieldPropertyMessage.Remove(8));
        f.StepLights(10);
        Assert.That(l.Ambient.X, Is.EqualTo(0.625f).Within(1e-6));
        Assert.That(f.Find(8), Is.Null);
    }

    [Test]
    public void ANewRecordStartsANewFadeFromTheColourOfTheMoment()
    {
        var f = new FieldProperties { Lights = new Lights() };
        var l = (Lights)f.Lights;
        f.Apply(new FieldPropertyMessage.Add(Amb(255, 255, 255, true, 4)));
        f.StepLights(2);   // 0.75
        f.Apply(new FieldPropertyMessage.Add(Amb(0, 0, 0, true, 2)));
        f.StepLights(1);
        Assert.That(l.Ambient.X, Is.EqualTo(0.375f).Within(1e-6), "from 0.75 to 0 over 2 s, half way");
    }

    [Test]
    public void ALightRecordWithoutAFieldDoesNothingUntilTheLightsAreSet()
    {
        var f = new FieldProperties();
        f.Apply(new FieldPropertyMessage.Add(Amb(255, 0, 0, false, 0)));
        f.StepLights(1);
        Assert.That(f.Find(8), Is.Not.Null, "the record is kept");
        var l = new Lights();
        f.Lights = l;   // the field is built: the stored record applies
        Assert.That(l.Ambient, Is.EqualTo(new Vector3(1, 0, 0)));
        f.Clear();
        Assert.That((f.Lights, f.Count), Is.EqualTo(((IFieldLights)null, 0)));
        Assert.That(l.Ambient, Is.EqualTo(new Vector3(1, 0, 0)), "a clear leaves the light as it is");
    }
}
