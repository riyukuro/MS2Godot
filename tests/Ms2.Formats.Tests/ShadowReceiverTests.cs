using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>ShadowReceivers: IsReceivingShadow off never receives; a UseShellRender instancing batch receives only at level 4.</summary>
public class ShadowReceiverTests
{
    static FieldEntity Entity(string asset, params (string, string)[] props)
    {
        var raw = new XEntity { ModelName = "m" };
        raw.Props["NifAsset"] = asset;
        foreach (var (k, v) in props) raw.Props[k] = v;
        return new FieldEntity { Raw = raw, Flat = new FlatModel { Name = "m" } };
    }

    static readonly (string, string) Inst = ("UseInstancing", "True"), Shell = ("UseShellRender", "True");

    [Test]
    public void OwnFlagOff()
    {
        var off = Entity("urn:llid:a", ("IsReceivingShadow", "False"));
        var on = Entity("urn:llid:a");
        foreach (var level in new[] { 0, 2, 3, 4 })
            Assert.That(ShadowReceivers.NotReceiving(new[] { off, on }, level), Is.EquivalentTo(new[] { off }), $"level {level}");
    }

    [Test]
    public void ShellBatchReceivesOnlyAtLevelFour()
    {
        var a = Entity("urn:llid:s", Inst, Shell);
        var b = Entity("urn:llid:S", Inst, Shell);          // asset ids compare without case
        var plain1 = Entity("urn:llid:p", Inst);
        var plain2 = Entity("urn:llid:p", Inst);
        var all = new[] { a, b, plain1, plain2 };
        Assert.That(ShadowReceivers.NotReceiving(all, 2), Is.EquivalentTo(new[] { a, b }));
        Assert.That(ShadowReceivers.NotReceiving(all, 3), Is.EquivalentTo(new[] { a, b }));
        Assert.That(ShadowReceivers.NotReceiving(all, 4), Is.Empty);
    }

    [Test]
    public void ShellBatchOfOneDrawsAsItself()
    {
        var a = Entity("urn:llid:s", Inst, Shell);
        var other = Entity("urn:llid:s", Inst, Shell, ("MaterialColor", "1, 0, 0"));   // another config: a batch of its own
        var hidden = Entity("urn:llid:s", Inst, Shell, ("IsVisible", "False"));          // not instanced
        var notInst = Entity("urn:llid:s", Shell);
        Assert.That(ShadowReceivers.NotReceiving(new[] { a, other, hidden, notInst }, 2), Is.Empty);
    }

    [Test]
    public void ShellBatchSplitsOnCasting()
    {
        var a = Entity("urn:llid:s", Inst, Shell);
        var b = Entity("urn:llid:s", Inst, Shell, ("IsCastingShadow", "False"));
        Assert.That(ShadowReceivers.NotReceiving(new[] { a, b }, 2), Is.Empty);
    }

    /// <summary>Tria: 657 shell-rendered copies of one model in one batch (the water, co_fluid_water_B02_), and 25 copies placed with IsReceivingShadow off
    /// (torch effects and portals).</summary>
    [Test]
    public void Tria()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA not set");
        var packs = new Ms2.Formats.Pack.PackSet(data);
        var map = FieldMap.Load(packs, new FlatLibrary(packs), "02000001_tw_tria");
        var visible = map.Entities.Where(e => e.Bool("IsVisible", true) && e.Get("NifAsset") is { } l && l.StartsWith("urn:llid:")).ToList();
        Assert.That(ShadowReceivers.NotReceiving(visible, 2).Count, Is.EqualTo(657 + 25));
        Assert.That(ShadowReceivers.NotReceiving(visible, 4).Count, Is.EqualTo(25));
        Assert.That(ShadowReceivers.NotReceiving(visible, 2).Count(e => e.Get("NifAsset") == "urn:llid:b6df34ae-0000-0000-0000-000000000000"), Is.EqualTo(657));
    }
}
