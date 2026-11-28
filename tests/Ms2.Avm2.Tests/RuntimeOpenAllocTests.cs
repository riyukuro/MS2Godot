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
        Assert.That(bytes, Is.LessThan(3_000_000));   // 2894848 B (Release)
    }
}
