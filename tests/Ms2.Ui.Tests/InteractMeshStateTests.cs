using System.Numerics;
using Ms2.Formats.Model;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The mesh objects' state draw (CInteractMeshObject::vfunc_1 and vfunc_3) and the state effect
/// (CInteractObject::UpdateStateEffect).</summary>
public class InteractMeshStateTests
{
    static FieldEntity Entity(string id, int rowId, string model, params (string Key, string Value)[] props)
    {
        var raw = new XEntity { Id = id, ModelName = model, Name = "obj_" + id };
        raw.Props["interactID"] = rowId.ToString();
        raw.Props["Position"] = "100, 200, 300";
        foreach (var (k, v) in props) raw.Props[k] = v;
        var flat = new FlatModel { Name = model };
        flat.Mixins.Add("MS2InteractObject");
        return new FieldEntity { Raw = raw, Flat = flat };
    }

    static InteractObjectTable Table(params InteractObjectRow[] rows) => new(rows.ToDictionary(r => r.Id));

    static (LiveInteractObjectsTests.Host Host, LiveInteractObjects Live) Field(InteractObjectTable t, params FieldEntity[] e)
    {
        var host = new LiveInteractObjectsTests.Host();
        var live = new LiveInteractObjects(host, t);
        live.Attach(e);
        return (host, live);
    }

    [Test]
    public void AMeshTakesItsModelsAssetsAndEachStateDrawsAsTheClient()
    {
        var t = Table(new InteractObjectRow { Id = 1, Type = InteractObjectMessage.KindMesh, NormalCollision = 0, ReactableCollision = 1 },
                      new InteractObjectRow { Id = 2, Type = InteractObjectMessage.KindTelescope });
        var (host, live) = Field(t,
            Entity("M1", 1, "MS2InteractMesh", ("NifAsset", "urn:llid:aa"), ("reactableAssetName", "urn:llid:bb")),
            Entity("A1", 1, "MS2InteractActor", ("InitialSequence", "On"), ("reactableSequenceName", "Off")),
            Entity("T1", 2, "MS2InteractMesh", ("NifAsset", "urn:llid:cc")),
            Entity("X1", 1, "MS2InteractObject"));
        var mesh = live.ByName("m1");
        Assert.That((mesh.AssetType, mesh.AssetA, mesh.AssetB), Is.EqualTo(((byte)1, "urn:llid:aa", "urn:llid:bb")));
        var actor = live.ByName("a1");
        Assert.That((actor.AssetType, actor.AssetA, actor.AssetB), Is.EqualTo(((byte)2, "On", "Off")));
        Assert.That(live.ByName("x1"), Is.Null, "a mesh kind of neither model does not register");
        Assert.That(live.ByName("t1").AssetType, Is.Zero, "a telescope is not a mesh object");
        Assert.That(host.StateChanges, Is.EqualTo(new[] { ("m1", (byte)0), ("a1", (byte)0), ("t1", (byte)0) }), "registering runs vfunc_3(0)");

        Assert.That(LiveInteractObjects.MeshDraw(mesh, out bool v, out string a, out int c), Is.True);
        Assert.That((v, a, c), Is.EqualTo((true, "urn:llid:aa", 0)));
        live.Apply(new InteractObjectMessage { Mode = InteractObjectMessage.ModeState, Name = "M1", State = 1, Kind = 1 });
        LiveInteractObjects.MeshDraw(mesh, out v, out a, out c);
        Assert.That((v, a, c), Is.EqualTo((true, "urn:llid:bb", 1)));
        live.Apply(new InteractObjectMessage { Mode = InteractObjectMessage.ModeState, Name = "A1", State = 2, Kind = 1 });
        LiveInteractObjects.MeshDraw(actor, out v, out a, out c);
        Assert.That((v, a, c), Is.EqualTo((false, (string)null, -1)), "state 2 hides and leaves the asset and the box");
        Assert.That(LiveInteractObjects.MeshDraw(live.ByName("t1"), out _, out _, out _), Is.False);
    }

    [Test]
    public void AWatchTowerIsLeftToItsOwnStateChange()
    {
        var t = Table(new InteractObjectRow { Id = 9, Type = InteractObjectMessage.KindWatchTower });
        var (_, live) = Field(t, Entity("W", 9, "MS2InteractActor", ("InitialSequence", "Idle")));
        Assert.That(live.ByName("w").AssetType, Is.EqualTo(2));
        Assert.That(LiveInteractObjects.MeshDraw(live.ByName("w"), out _, out _, out _), Is.False);
    }

    [Test]
    public void ASpawnedMeshTakesBothUrnsOnlyWhenBothAreSent()
    {
        var t = Table(new InteractObjectRow { Id = 1, Type = InteractObjectMessage.KindMesh });
        var (_, live) = Field(t);
        live.Apply(new InteractObjectMessage { Mode = InteractObjectMessage.ModeAdd, Name = "D1", Kind = 1, RowId = 1, Model = "MS2InteractMesh", AssetA = "a.nif", AssetB = "b.nif" });
        live.Apply(new InteractObjectMessage { Mode = InteractObjectMessage.ModeAdd, Name = "D2", Kind = 1, RowId = 1, Model = "MS2InteractMesh", AssetA = "a.nif" });
        live.Apply(new InteractObjectMessage { Mode = InteractObjectMessage.ModeAdd, Name = "D3", Kind = 1, RowId = 1, Model = "MS2InteractActor", AssetA = "On", AssetB = "Off" });
        live.Apply(new InteractObjectMessage { Mode = InteractObjectMessage.ModeAdd, Name = "D4", Kind = 1, RowId = 1, Model = "MS2InteractActor", Anim = "x.kfm", AssetA = "On", AssetB = "Off" });
        Assert.That((live.ByName("d1").AssetType, live.ByName("d1").AssetA, live.ByName("d1").AssetB),
            Is.EqualTo(((byte)1, "urn:gamebryo-scenegraph:a.nif", "urn:gamebryo-scenegraph:b.nif")));
        Assert.That((live.ByName("d2").AssetA, live.ByName("d2").AssetB), Is.EqualTo(("", "")));
        Assert.That((live.ByName("d3").AssetType, live.ByName("d3").AssetA), Is.EqualTo(((byte)2, "")), "no animation file: no sequences");
        Assert.That((live.ByName("d4").AssetA, live.ByName("d4").AssetB), Is.EqualTo(("On", "Off")));
    }

    [Test]
    public void TheStateEffectFollowsTheStateAndItsMasks()
    {
        var hidden = new QuestMaskTable.Mask();   // quest 100 not taken: the range is skipped, so the mask fails
        hidden.Ranges.Add(new QuestMaskTable.Range(100, 1, 0, 0));
        var row = new InteractObjectRow { Id = 1, Type = InteractObjectMessage.KindMesh, EffectNormal = "fx_n.xml", EffectReactable = "", MaskReactable = new() };
        var (host, live) = Field(Table(row), Entity("M", 1, "MS2InteractMesh", ("NifAsset", "urn:llid:aa")));
        var o = live.ByName("m");
        Assert.That(live.StateEffect(o, out var fx), Is.True);
        Assert.That((fx, o.ShownState), Is.EqualTo(("fx_n.xml", (byte)0)));
        Assert.That(live.StateEffect(o, out _), Is.False, "the same state keeps its effect");
        o.State = 1;
        Assert.That(live.StateEffect(o, out fx), Is.True, "no reactable effect: the normal one is cleared");
        Assert.That((fx, o.ShownState), Is.EqualTo(("", (byte)3)));
        Assert.That(live.StateEffect(o, out _), Is.False, "nothing up, nothing to clear");
        o.State = 0;
        Assume.That(QuestMaskTable.Check(hidden, host.QuestStatusOf), Is.False);
        row.MaskNormal = hidden;
        Assert.That(live.StateEffect(o, out fx), Is.False, "the state's mask fails: nothing shows");
        row.MaskNormal = new();
        live.StateEffect(o, out _);
        row.MaskUse = hidden;
        Assert.That(live.StateEffect(o, out fx), Is.True, "the use mask fails: the effect is cleared");
        Assert.That(fx, Is.Empty);
    }
}
