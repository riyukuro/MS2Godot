using System.Collections;
using System.Reflection;
using Ms2.Formats.Pack;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// G22 — pet data (D28): GMS2 table/petproperty.xml pet 60000001 has npcID="60000001" slotNum="14" and one
/// &lt;skill id="0" level="0"&gt;; PetDataTable keeps only code-&gt;type. KMS has no npcID (the code is the NPC id by
/// convention) and no skill, and its TypeOf is unchanged.
/// </summary>
public class RcG22PetDataTests
{
    static object Call(object target, string method, object arg)
    {
        var m = target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(x => x.Name == method && x.GetParameters().Length == 1);
        if (m == null) Assert.Fail($"G22: PetDataTable.{method} does not exist yet (the data gap notes)");
        var p = m.GetParameters()[0];
        object a = p.ParameterType == typeof(uint) ? (object)Convert.ToUInt32(arg)
            : p.ParameterType == typeof(int) ? Convert.ToInt32(arg) : arg;
        return m.Invoke(target, new[] { a });
    }

    [Test]
    public void TheKmsPetKeepsItsType()
    {
        using var packs = new PackSet(RcGap.Data("MS2_DATA"));
        var t = new PetDataTable(packs);
        Assert.That(t.TypeOf(60000001), Is.EqualTo(0), "kms table/petproperty.xml has no type attribute");
        Assert.That(t.Ids, Does.Contain(60000001u));
    }

    [Test]
    [Ignore("G22: PetDataTable keeps code->type only; npcID/slotNum/<skill> are unread")]
    public void TheGms2PetReadsItsRow()
    {
        using var packs = new PackSet(RcGap.Data("MS2_GMS2_DATA"));
        var t = new PetDataTable(packs);
        Assert.That(Call(t, "NpcIdOf", 60000001u), Is.EqualTo(60000001), "pets npcID=\"60000001\"");
        var skills = (IEnumerable)Call(t, "SkillsOf", 60000001u);
        Assert.That(skills, Is.Not.Null, "pets <skill id=\"0\" level=\"0\">");
        var first = skills.Cast<object>().FirstOrDefault();
        Assert.That(first, Is.Not.Null, "one <skill> row");
        Assert.That(RcGap.Member<int>(first, "SkillId"), Is.EqualTo(0));
    }
}
