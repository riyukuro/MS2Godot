using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using Ms2.Formats.Pack;
using Ms2.Formats.World;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/nif_cache.tscn) of the process's model and texture cache (<see cref="NifCache"/>, the
/// client's CNifLoadingService):
/// <list type="bullet">
/// <item>a second field build of the same field uses the first one's model data, and a second builder the first one's
/// textures (the old per-field parse fails here);</item>
/// <item>the sweep runs once a minute and drops what only the cache holds once unused for 120 s: class 0 at once,
/// classes 1 and 2 never; a field's lease keeps its models, a field change does not clear them; a texture a material
/// holds stays and counts as used;</item>
/// <item>a failed load is not kept, a cache hit lowers the class, the per-frame tick allocates nothing.</item>
/// </list>
/// Needs MS2_DATA and MS2_NIFXML (or local.cfg). Quits with 0 when it holds, else 1.
/// </summary>
public partial class NifCacheCheck : Node
{
    public override void _Ready()
    {
        var fails = new List<string>();
        try { Field(fails); }
        catch (Exception e) { fails.Add(e.ToString()); }
        try { Rules(fails); }
        catch (Exception e) { fails.Add(e.ToString()); }
        GD.Print($"nif cache: {(fails.Count == 0 ? "ok" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    const string FieldName = "02000001_tw_tria";

    static (object models, Node3D world, double ms) BuildField(PackSet packs, ModelBuilder builder, object lease)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var map = FieldMap.Load(packs, new FlatLibrary(packs), FieldName);
        var fb = new FieldBuilder(packs, AssetIndex.For(packs), builder);
        typeof(FieldBuilder).GetField("Lease")?.SetValue(fb, lease);
        foreach (var _ in fb.BuildSteps(map)) { }
        var models = typeof(FieldBuilder).GetField("models", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(fb);
        return (models, fb.World, sw.Elapsed.TotalMilliseconds);
    }

    static object Data(object entry) => entry?.GetType().GetField("Data")!.GetValue(entry);

    void Field(List<string> fails)
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var spec = ModelBuilder.SharedSpec(Settings.NifXml);
        var leaseType = typeof(FieldBuilder).GetField("Lease")?.FieldType;
        object l1 = leaseType != null ? Activator.CreateInstance(leaseType) : null, l2 = leaseType != null ? Activator.CreateInstance(leaseType) : null;
        var b1 = new ModelBuilder(spec, packs);
        var (m1, w1, ms1) = BuildField(packs, b1, l1);
        (l1 as IDisposable)?.Dispose();             // the first field is gone (a field change)
        var b2 = new ModelBuilder(spec, packs);
        var (m2, w2, ms2) = BuildField(packs, b2, l2);
        int n = 0, same = 0;
        foreach (DictionaryEntry kv in (IDictionary)m1)
        {
            var d1 = Data(kv.Value);
            if (d1 == null) continue;
            n++;
            var d2 = Data(((IDictionary)m2)[kv.Key]);
            if (ReferenceEquals(d1, d2)) same++;
        }
        if (n == 0) fails.Add("the field built no models");
        else if (same != n) fails.Add($"the second build of the field parsed {n - same} of its {n} models again");
        string texName = null;
        foreach (var k in KeysOf(b1)) { texName = k; break; }
        if (texName == null) fails.Add("the field loaded no textures");
        else if (!ReferenceEquals(b1.Texture(texName), b2.Texture(texName))) fails.Add($"a second builder decoded {texName} again");
        GD.Print($"nif cache: field {FieldName} {n} models, first build {ms1:0} ms, second {ms2:0} ms");
        w1?.Free(); w2?.Free();
        (l2 as IDisposable)?.Dispose();
    }

    static IEnumerable<string> KeysOf(ModelBuilder b)
    {
        var d = (IDictionary)typeof(ModelBuilder).GetField("textures", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(b);
        foreach (DictionaryEntry kv in d) if (kv.Value != null) yield return (string)kv.Key;
    }

    void Rules(List<string> fails)
    {
        double now = 1000;
        var clock = NifCache.Clock;
        NifCache.Clock = () => now;
        try
        {
            NifCache.Clear();
            void Check(bool c, string what) { if (!c) fails.Add(what); }
            object Load(string key, NifCache.CacheClass cls, NifCache.Lease l = null) => NifCache.Model<object>(null, key, cls, l, () => new object());

            NifCache.Tick();                            // the sweep timer starts
            var lease = new NifCache.Lease();
            var held = Load("held", NifCache.CacheClass.DependTime, lease);
            Load("time", NifCache.CacheClass.DependTime);
            Load("keep", NifCache.CacheClass.ForceKeep);
            Load("skill", NifCache.CacheClass.MySkill);
            Load("once", NifCache.CacheClass.NotCache);
            Load("npc", NifCache.CacheClass.Npc);
            Check(NifCache.Model<object>(null, "failed", NifCache.CacheClass.DependTime, null, () => null) == null && NifCache.Count.Models == 6, "a failed load is kept");
            Check(ReferenceEquals(Load("held", NifCache.CacheClass.DependTime), held), "a hit loaded again");

            var tex = ImageTexture.CreateFromImage(Image.CreateEmpty(4, 4, false, Image.Format.Rgba8));
            var mat = new StandardMaterial3D { AlbedoTexture = tex };
            NifCache.Texture(null, "used", NifCache.CacheClass.DependTime, () => tex);
            NifCache.Texture(null, "free", NifCache.CacheClass.DependTime, () => ImageTexture.CreateFromImage(Image.CreateEmpty(4, 4, false, Image.Format.Rgba8)));
            Check(NifCache.Texture(null, "none", NifCache.CacheClass.DependTime, () => null) == null && NifCache.Count.Textures == 2, "a failed texture load is kept");

            now += 59; NifCache.Tick();
            Check(NifCache.Count.Models == 6, "the sweep ran before its minute");
            now += 2; NifCache.Tick();                  // 61 s: the first sweep, nothing unused for 120 s yet
            Check(NifCache.Count.Models == 5, $"after the first sweep: only class 0 goes, {NifCache.Count.Models} models left");
            Load("npc", NifCache.CacheClass.Npc);       // npc used again at 61 s
            Load("time", NifCache.CacheClass.ForceKeep); // a hit lowers the class: kept from now on
            now += 60; NifCache.Tick();                 // 121 s
            Check(NifCache.Count.Models == 5, "an entry went before its 120 s");
            now += 60; NifCache.Tick();                 // 181 s: 'held' is leased, 'time' is class 1 now, 'npc' was used at 61 s
            Check(NifCache.Count.Models == 5, $"181 s: {NifCache.Count.Models} models, want 5");
            Check(NifCache.Count.Textures == 1, $"181 s: {NifCache.Count.Textures} textures, want 1 (the material's)");
            lease.Dispose();                            // the field changes: nothing is cleared
            Check(NifCache.Count.Models == 5, "a field change cleared the cache");
            now += 60; NifCache.Tick();                 // 241 s: npc unused for 180 s goes; 'held' was last used at 0 s
            Check(NifCache.Count.Models == 3, $"241 s: {NifCache.Count.Models} models, want 3 (keep, skill, time)");
            Check(NifCache.Count.Textures == 1, "a texture a material holds went");
            mat.AlbedoTexture = null;
            now += 60; NifCache.Tick();                 // 301 s: the texture was marked used at 241 s
            Check(NifCache.Count.Textures == 1, "a texture went before 120 s after its last hold");
            now += 120; NifCache.Tick();
            Check(NifCache.Count.Textures == 0, "an unheld texture stayed past 120 s");

            for (int i = 0; i < 100; i++) NifCache.Tick();
            long bytes = CheckAlloc.Least(() => { for (int i = 0; i < 10000; i++) NifCache.Tick(); });
            Check(bytes == 0, $"tick allocates {bytes} B over 10000 frames");
        }
        finally
        {
            NifCache.Clear();
            NifCache.Clock = clock;
        }
    }
}
