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
}
