using Ms2.Avm2;
using Ms2Godot.Tests;
using NUnit.Framework;

namespace Ms2.Avm2.Tests;

/// <summary>A global lookup by qualified name (every hover's tooltip, event and display object resolves its classes this way)
/// splits the name once and reads the interned namespace without a lock: a repeat lookup allocates nothing.</summary>
public class GlobalLookupAllocTests
{
    [Test]
    public void RepeatGlobalLookupAllocatesNothing()
    {
        var rt = new Runtime();
        string[] names = { "flash.utils.Dictionary", "flash.utils.getTimer", "Math", "Object" };
        foreach (var n in names) Assert.That(rt.GetGlobal(n), Is.Not.EqualTo(Rt.Undef), n);
        long bytes = Alloc.Least(() => { for (int i = 0; i < 200; i++) foreach (var n in names) rt.GetGlobal(n); });
        Assert.That(bytes, Is.EqualTo(0));
    }
}
