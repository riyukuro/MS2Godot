using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>Which entities get an environment sound at the field load (<see cref="EnvironmentSounds.MadeAtLoad"/>).</summary>
public class EnvironmentSoundsTests
{
    static FieldEntity Entity(string model, string[] mixins, string flatTimeShow = "False", string ownTimeShow = null)
    {
        var raw = new XEntity { ModelName = model };
        if (ownTimeShow != null) raw.Props[EnvironmentSounds.TimeShow] = ownTimeShow;
        var flat = new FlatModel { Name = model };
        foreach (var m in mixins) flat.Mixins.Add(m);
        flat.Props[EnvironmentSounds.TimeShow] = flatTimeShow;
        return new FieldEntity { Raw = raw, Flat = flat };
    }

    [Test]
    public void TriggerObjectsAndTimeShowSoundsGetNoSoundAtLoad()
    {
        Assert.That(EnvironmentSounds.MadeAtLoad(Entity("MS2Sound", new[] { "MS2TimeAmbSetting" })), Is.True);
        Assert.That(EnvironmentSounds.MadeAtLoad(Entity("AMB_3D_Stream_", new[] { "MS2Sound", "MS2TimeAmbSetting" })), Is.True);
        Assert.That(EnvironmentSounds.MadeAtLoad(Entity("AMB_3D_Stream_", new[] { "MS2Sound", "MS2TriggerObject" })), Is.False);
        Assert.That(EnvironmentSounds.MadeAtLoad(Entity("AMB_3D_Stream_", new[] { "MS2Sound" }, ownTimeShow: "True")), Is.False);
        Assert.That(EnvironmentSounds.MadeAtLoad(Entity("AMB_3D_Stream_", new[] { "MS2Sound" }, flatTimeShow: "True")), Is.False);
        Assert.That(EnvironmentSounds.MadeAtLoad(Entity("AMB_3D_Stream_", new[] { "MS2Sound" }, flatTimeShow: "True", ownTimeShow: "False")), Is.True);
        Assert.That(EnvironmentSounds.MadeAtLoad(Entity("eff_firefly_01_", new[] { "MS2TimeAmbSetting" }, flatTimeShow: "True")), Is.False);
        Assert.That(EnvironmentSounds.MadeAtLoad(new FieldEntity { Raw = new XEntity { ModelName = "x" } }), Is.False);
    }

    [Test] public void KmsNoSoundSetsTimeShow() => Census("MS2_DATA");
    [Test] public void Gms2NoSoundSetsTimeShow() => Census("MS2_GMS2_DATA");

    /// <summary>
    /// Every field's MS2Sound entities: none sets timeShowEnabled, so the skip changes no field's sounds. The flat models
    /// that set it (fireflies, torchlight, smoke, butterflies ...) have no MS2Sound mixin.
    /// </summary>
    static void Census(string variable)
    {
        string data = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(data) || !Directory.Exists(data)) Assert.Ignore($"{variable} not set");
        using var packs = new PackSet(data);
        var flats = new FlatLibrary(packs);
        var timeShowFlats = new List<string>();
        foreach (var x in packs.List(XBlock.Archive))
        {
            if (!x.StartsWith("flat/", StringComparison.OrdinalIgnoreCase) || !x.EndsWith(".flat", StringComparison.OrdinalIgnoreCase)) continue;
            var f = flats.Get(Path.GetFileNameWithoutExtension(x));
            if (f == null || !f.Props.TryGetValue(EnvironmentSounds.TimeShow, out var v) || !v.Equals("True", StringComparison.OrdinalIgnoreCase)) continue;
            timeShowFlats.Add(f.Name);
            Assert.That(f.Mixins, Has.No.Member(EnvironmentSounds.Model), f.Name);
        }
        Assert.That(timeShowFlats, Is.Not.Empty);
        int fields = 0, sounds = 0, made = 0, timeShow = 0;
        foreach (var x in packs.List(XBlock.Archive))
        {
            if (!x.StartsWith("xblock/", StringComparison.OrdinalIgnoreCase) || !x.EndsWith(".xblock", StringComparison.OrdinalIgnoreCase)) continue;
            var map = FieldMap.Load(packs, flats, Path.GetFileNameWithoutExtension(x));
            fields++;
            foreach (var e in map.Entities)
            {
                if (!e.HasMixin(EnvironmentSounds.Model)) continue;
                sounds++;
                if (e.Bool(EnvironmentSounds.TimeShow)) timeShow++;
                if (EnvironmentSounds.MadeAtLoad(e)) made++;
            }
        }
        TestContext.Progress.WriteLine($"{variable}: {fields} fields, {sounds} sound entities, {made} made at load, {timeShow} with timeShowEnabled; {timeShowFlats.Count} flats set it");
        Assert.That(sounds, Is.GreaterThan(3000));
        Assert.That(timeShow, Is.Zero);
    }
}
