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

    /// <summary>Names compare namespaces by reference (Ns.IsPublicLike, the binding lookups): the lock-free interning hands every
    /// caller of one kind and URI the same object, also when threads race to add it.</summary>
    [Test]
    public void InternedNamespaceIsOneObjectAcrossThreads()
    {
        var uris = new string[64];
        for (int i = 0; i < uris.Length; i++) uris[i] = "test.intern.race" + i;
        var seen = new Ns[8][];
        System.Threading.Tasks.Parallel.For(0, seen.Length, t =>
        {
            seen[t] = new Ns[uris.Length];
            for (int i = 0; i < uris.Length; i++) seen[t][i] = Ns.Get(NsKind.Internal, uris[i]);
        });
        for (int i = 0; i < uris.Length; i++)
        {
            for (int t = 1; t < seen.Length; t++) Assert.That(seen[t][i], Is.SameAs(seen[0][i]), uris[i]);
            Assert.That(Ns.Get(NsKind.Internal, uris[i]), Is.SameAs(seen[0][i]));
            Assert.That(Ns.Get(NsKind.Explicit, uris[i]), Is.Not.SameAs(seen[0][i]), "a kind is part of the identity");
        }
        Assert.That(Ns.Get(NsKind.Public, "http://adobe.com/AS3/2006/builtin"), Is.SameAs(Ns.AS3));
    }
}
