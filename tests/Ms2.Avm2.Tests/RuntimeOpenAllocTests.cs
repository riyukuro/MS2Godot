using System.Diagnostics;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2Godot.Tests;
using NUnit.Framework;

namespace Ms2.Avm2.Tests;

/// <summary>
/// Every dialog open makes a new runtime: the flash classes are bound into it and the movie's and its imports' ABC blocks
/// are loaded. These count what that costs on its own, after the per-process caches are warm: the bytes and time of a new
/// runtime with the flash classes, and of a second open of a dialog movie (the first one fills the caches).
/// </summary>
public class RuntimeOpenAllocTests
{
    [Test]
    public void NewRuntimeWithFlashClasses()
    {
        static void New() { var rt = new Runtime(); Player.InstallFlash(rt); }
        New();
        long bytes = Alloc.Least(New);
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 20; i++) New();
        double ms = sw.Elapsed.TotalMilliseconds / 20;
        TestContext.WriteLine($"new runtime + flash classes: {bytes} B, {ms:F3} ms");
        Assert.That(bytes, Is.LessThan(1_100_000));   // 1051424 B (Release); each native global and constant copied every slot before
    }

    [Test]
    public void SecondOpenOfADialog()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var packs = new PackSet(d);
        var src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
        void Open() => src.Open("uiquestdialog", r => r.Budget = 20_000_000);
        Open();
        long bytes = Alloc.Least(Open);
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 10; i++) Open();
        double ms = sw.Elapsed.TotalMilliseconds / 10;
        TestContext.WriteLine($"second open of uiquestdialog: {bytes} B, {ms:F3} ms");
        Assert.That(bytes, Is.LessThan(2_650_000));   // 2589464 B (Release)
    }

    [Test]
    public void ABlockLoadedTwiceIntoOneRuntimeHasItsOwnPrivateNamespaces()
    {
        // the namespaces are kept per block for the process; a second load into the same runtime must not share the first
        // load's private namespace (each load's private members are its own), while a load into another runtime may
        var abc = new Ms2.Swf.Abc.AbcFile
        {
            Ints = new int[1], Uints = new uint[1], Doubles = new double[1], Strings = new[] { "", "p", "pkg" },
            Namespaces = new[] { new Ms2.Swf.Abc.Namespace { Name = "*" }, new Ms2.Swf.Abc.Namespace { Kind = 0x05, Name = "p" },
                                 new Ms2.Swf.Abc.Namespace { Kind = 0x16, Name = "pkg" } },
            NsSets = new[] { System.Array.Empty<int>() }, Multinames = new[] { new Ms2.Swf.Abc.Multiname { Kind = 0, Name = "*" } },
            Methods = new Ms2.Swf.Abc.MethodInfo[0], Instances = new Ms2.Swf.Abc.InstanceInfo[0],
            Classes = new Ms2.Swf.Abc.ClassInfo[0], Scripts = new Ms2.Swf.Abc.ScriptInfo[0], Hash = "load-twice-test",
        };
        var rt = new Runtime();
        var first = rt.Load(abc, "m:a");
        var second = rt.Load(abc, "m:b");
        Assert.That(first.NsAt(1).Kind, Is.EqualTo(NsKind.Private));
        Assert.That(second.NsAt(1), Is.Not.SameAs(first.NsAt(1)));
        Assert.That(second.NsAt(2), Is.SameAs(first.NsAt(2)));
        var other = new Runtime().Load(abc, "m:a");
        Assert.That(other.NsAt(1).Kind, Is.EqualTo(NsKind.Private));
    }
}
