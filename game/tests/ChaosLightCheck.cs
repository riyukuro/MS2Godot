using System.Collections.Generic;
using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2Godot.Net;
using Ms2Godot.World;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/chaos_light.tscn, game data) of the chaoslight parts (<see cref="ChaosLightEffect"/>) and the
/// layer manager (<see cref="ChaosLayers"/>) on built item models:
/// - a character material mesh is marked to take layers; the fishing rod mesh whose glow map a texture transform controller
///   drives is not;
/// - the bleed part (layer 1, no delay) puts a layer on every marked mesh of the owner at once: chaos_on and the part's values
///   are set, the mesh's glow pass twin draws the chaos glow (the layer's white glow map) and a marked mesh without a twin
///   gets one shown meanwhile; a layer 0 part on top of it is not shown;
/// - the top layer rule: a layer of a greater or equal id is shown, a lower one is not and keeps its values for later;
///   removing the top shows the next; removing the last turns chaos off, the own twin draws its glow map again and a made
///   twin is hidden; a layer id below the base's is never shown; the twins switch only when a layer goes on or the last
///   comes off;
/// - the effect leaving the tree takes its layers off;
/// - a part with a start delay puts its layers on when it starts, a region skill spawn makes the part on the actor, and
///   1000 frames of a running part allocate 0 B.
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class ChaosLightCheck : Node3D
{
    const string Items = "Resource/Model/Item", Rod = "1/90/19000025_fishingrod025.nif", RodMesh = "19000025_fishingRod01";
    const string Bleed = "additional/bleed/eff_additional_bleed_keep.xml", Burn = "additional/burn/eff_additional_burn_keep.xml",
        Confusion = "additional/confusion/eff_additional_confusion_keep.xml";
    readonly List<string> fails = new();
    void Check(bool ok, string what) { if (!ok) fails.Add(what); }

    static bool ChaosGlow(MeshInstance3D twin) => twin.MaterialOverride is ShaderMaterial sm && sm.GetShaderParameter(ShaderParam.ChaosGlow).AsBool();

    static float F(MeshInstance3D mi, StringName p) => mi.GetInstanceShaderParameter(p).VariantType == Variant.Type.Nil ? float.NaN : mi.GetInstanceShaderParameter(p).AsSingle();

    public override void _Ready() => Run();

    async void Run()
    {
        GlowPass.Wanted = true;
        var packs = PackSet.Shared(Settings.DataDir);
        var spec = ModelBuilder.SharedSpec(Settings.NifXml);
        var builder = new ModelBuilder(spec, packs);
        var effects = new EffectTable(packs);
        var skills = new RegionSkills(packs, builder);
        AddChild(skills);

        // the rod: its glow driven mesh takes no layer
        var rodMd = ModelData.Load(NifFile.Load(spec, packs.Read(Items, Rod)));
        var rod = builder.Build(rodMd, "Rod");
        AddChild(rod);
        var rodMi = Find(rod, RodMesh);
        Check(rodMi != null && !rodMi.HasMeta(ChaosLayers.Meta), $"the rod mesh {(rodMi == null ? "is missing" : "is marked")}");

        // an item with a glowing character mesh
        Node3D actor = null;
        foreach (var entry in packs.List(Items))
        {
            if (!entry.StartsWith("1/90/") || entry == Rod || !entry.EndsWith(".nif", System.StringComparison.OrdinalIgnoreCase)) continue;
            var md = ModelData.Load(NifFile.Load(spec, packs.Read(Items, entry)));
            // one marked mesh with a glow map and one without (and blending as the glow pass takes)
            if (!md.Meshes.Exists(m => ChaosLightEffect.TakesLayer(m) && m.Textures.ContainsKey("glow"))
                || !md.Meshes.Exists(m => ChaosLightEffect.TakesLayer(m) && !m.Textures.ContainsKey("glow") && !m.AlphaBlendOn)) continue;
            actor = builder.Build(md, "Actor");
            AddChild(actor);
            GD.Print($"chaos light: the owner is {entry}");
            foreach (var m in md.Meshes)
                if (Find(actor, m.Name) is { } mi)
                    Check(mi.HasMeta(ChaosLayers.Meta) == (ChaosLightEffect.TakesLayer(m) && mi.Mesh.SurfaceGetMaterial(0) is ShaderMaterial sm
                        && ModelBuilder.IsCharacterShader(sm.Shader)), $"{entry} {m.Name}: marked {mi.HasMeta(ChaosLayers.Meta)}");
            break;
        }
        Check(actor != null, "no item with a glowing character mesh");
        if (actor == null) { Done(); return; }
        var marked = new List<MeshInstance3D>();
        Marked(actor, marked);
        var glowing = marked.Find(mi => mi.GetParent().GetNodeOrNull<MeshInstance3D>(mi.Name + "_glow") != null);
        Check(marked.Count > 0 && glowing != null, $"{marked.Count} marked meshes, a glow twin {glowing != null}");
        if (glowing == null) { Done(); return; }
        var twin = glowing.GetParent().GetNode<MeshInstance3D>(glowing.Name + "_glow");
        Check(twin.Visible && !ChaosGlow(twin), "before a layer: the twin draws its glow map");
        var bare = marked.Find(mi => mi.GetParent().GetNodeOrNull(mi.Name + "_glow") == null);
        Check(bare != null && bare.HasMeta(ChaosLayers.GlowSource) && bare.GetNodeOrNull(bare.Name + "_chaosglow") == null,
            $"a marked mesh without a twin {(bare == null ? "is missing" : "has no glow source or a twin already")}");

        // the bleed part: on at once with its values
        var bleed = Part(effects, Bleed);
        Check(bleed != null && bleed.StartDelay == 0 && bleed.ChaosLight.Layer == 1, $"{Bleed}: the part");
        if (bleed == null) { Done(); return; }
        var fx = ChaosLightEffect.Make(bleed, actor);
        actor.AddChild(fx);
        Check(fx.LayerCount == marked.Count && ChaosLayers.Meshes == marked.Count, $"bleed: {fx.LayerCount} layers, {ChaosLayers.Meshes} meshes for {marked.Count}");
        var v = bleed.ChaosLight.Values;
        foreach (var mi in marked)
        {
            var c = mi.GetInstanceShaderParameter(ShaderParam.ChaosColor);
            Check(F(mi, ShaderParam.ChaosOn) == 1f && F(mi, ShaderParam.ChaosBoost) == v.Boost && F(mi, ShaderParam.ChaosExponent) == v.Exponent
                && F(mi, ShaderParam.ChaosDimmer) == v.Dimmer && c.VariantType == Variant.Type.Vector3 && c.AsVector3() == new Vector3(v.R, v.G, v.B),
                $"bleed: {mi.Name} shows on {F(mi, ShaderParam.ChaosOn)} boost {F(mi, ShaderParam.ChaosBoost)}");
        }
        Check(twin.Visible && ChaosGlow(twin), $"bleed: the glow twin draws the chaos glow ({twin.Visible}, {ChaosGlow(twin)})");
        var madeTwin = bare?.GetNodeOrNull<MeshInstance3D>(bare.Name + "_chaosglow");
        Check(madeTwin != null && madeTwin.Visible && ChaosGlow(madeTwin) && madeTwin.Layers == GlowPass.Bit && madeTwin.Mesh == bare.Mesh && madeTwin.Skin == bare.Skin,
            $"bleed: the mesh without a twin got a chaos glow twin ({madeTwin != null})");
        var burn = Part(effects, Burn);
        Check(burn != null && burn.ChaosLight.Layer == 0, $"{Burn}: the part");
        if (burn != null)
        {
            var under = ChaosLightEffect.Make(burn, actor);
            actor.AddChild(under);
            Check(under.LayerCount == marked.Count && F(glowing, ShaderParam.ChaosBoost) == v.Boost,
                $"burn under bleed: {under.LayerCount} layers, boost {F(glowing, ShaderParam.ChaosBoost)}");
            actor.RemoveChild(fx);
            Check(F(glowing, ShaderParam.ChaosOn) == 1f && F(glowing, ShaderParam.ChaosBoost) == burn.ChaosLight.Values.Boost,
                $"burn after bleed left: boost {F(glowing, ShaderParam.ChaosBoost)}");
            actor.RemoveChild(under);
            under.Free();
        }
        else actor.RemoveChild(fx);
        fx.Free();
        Check(ChaosLayers.Meshes == 0 && F(glowing, ShaderParam.ChaosOn) == 0f && twin.Visible && !ChaosGlow(twin),
            $"all parts left: {ChaosLayers.Meshes} meshes, on {F(glowing, ShaderParam.ChaosOn)}, twin {twin.Visible} {ChaosGlow(twin)}");
        Check(madeTwin != null && IsInstanceValid(madeTwin) && !madeTwin.Visible, "all parts left: the made twin is hidden");

        // the top layer rule
        ChaosLightValues Val(float boost) => new() { Boost = boost, Exponent = 2, Dimmer = 0.5f, R = 1, G = 0.5f, B = 0.25f };
        var a = ChaosLayers.Add(glowing, 5, Val(1));
        var b = ChaosLayers.Add(glowing, 3, Val(2));
        Check(ChaosLayers.Top(glowing) == a && F(glowing, ShaderParam.ChaosBoost) == 1, "a lower id is shown");
        var c5 = ChaosLayers.Add(glowing, 5, Val(3));
        Check(ChaosLayers.Top(glowing) == c5 && F(glowing, ShaderParam.ChaosBoost) == 3, "an equal id is not shown");
        ChaosLayers.Set(b, Val(4));
        Check(F(glowing, ShaderParam.ChaosBoost) == 3, "a layer under the top pushed its values");
        ChaosLayers.Remove(c5);
        Check(ChaosLayers.Top(glowing) == a && F(glowing, ShaderParam.ChaosBoost) == 1, "the top left: the next is not shown");
        ChaosLayers.Remove(a);
        Check(ChaosLayers.Top(glowing) == b && F(glowing, ShaderParam.ChaosBoost) == 4 && twin.Visible && ChaosGlow(twin), "the next left: the lower layer is not shown with its values");
        ChaosLayers.Remove(b);
        Check(ChaosLayers.Top(glowing) == null && ChaosLayers.Meshes == 0 && F(glowing, ShaderParam.ChaosOn) == 0f && twin.Visible && !ChaosGlow(twin), "the last left: chaos is still on");

        // a layer id below the base's: under the base, never shown; the base keeps the twin
        {
            var under = ChaosLayers.Add(glowing, -2, Val(7));
            Check(ChaosLayers.Top(glowing) == null && F(glowing, ShaderParam.ChaosOn) == 0f && !ChaosGlow(twin), "an id below the base is shown");
            var over = ChaosLayers.Add(glowing, 0, Val(8));
            Check(ChaosLayers.Top(glowing) == over && ChaosLayers.GlowOf(glowing) == twin && ChaosGlow(twin), "a layer over one below the base: the twin does not glow");
            ChaosLayers.Remove(over);
            Check(ChaosLayers.Top(glowing) == null && !ChaosGlow(twin), "the layer over one below the base left: the twin still glows");
            ChaosLayers.Remove(under);
            Check(ChaosLayers.Meshes == 0, "the layer below the base left: the entry stays");
        }

        // a frame whose values change: every value pushed to the mesh
        {
            var top = ChaosLayers.Add(glowing, 0, Val(1));
            var one = Val(1); var two = Val(2);
            for (int i = 0; i < 50; i++) ChaosLayers.Set(top, (i & 1) == 0 ? two : one);
            int switches = ChaosLayers.GlowSwitches;
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int i = 0; i < 1000; i++) ChaosLayers.Set(top, (i & 1) == 0 ? two : one);
            var took = System.Diagnostics.Stopwatch.GetElapsedTime(t0);
            long bytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Check(bytes == 0 && F(glowing, ShaderParam.ChaosBoost) == 1, $"changing values: {bytes} B, boost {F(glowing, ShaderParam.ChaosBoost)}");
            Check(ChaosLayers.GlowSwitches == switches, $"changing values: the twin switched {ChaosLayers.GlowSwitches - switches} times");
            GD.Print($"chaos light: a changed layer pushed in {took.TotalMilliseconds:F3} us, {bytes} B");
            ChaosLayers.Remove(top);
        }

        // a start delay: the layers go on when the part starts
        var conf = Part(effects, Confusion);
        Check(conf != null && conf.StartDelay > 0 && conf.ChaosLight.Keys.Length > 0, $"{Confusion}: the part");
        if (conf != null)
        {
            var late = ChaosLightEffect.Make(conf, actor);
            actor.AddChild(late);
            Check(late.LayerCount == 0, $"confusion: {late.LayerCount} layers before its start");
            ulong until = Time.GetTicksMsec() + 3000;
            while (!late.Run.Started && Time.GetTicksMsec() < until) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(late.Run.Started && late.LayerCount == marked.Count, $"confusion: started {late.Run.Started}, {late.LayerCount} layers");
            late._Process(0);
            var lv = late.Run.Layer;
            Check(F(glowing, ShaderParam.ChaosBoost) == lv.Boost && F(glowing, ShaderParam.ChaosDimmer) == lv.Dimmer, "confusion: the mesh does not show the run's values");
            for (int i = 0; i < 50; i++) late._Process(1 / 60.0);
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int i = 0; i < 1000; i++) late._Process(1 / 60.0);
            var took = System.Diagnostics.Stopwatch.GetElapsedTime(t0);
            long bytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Check(bytes == 0, $"confusion: 1000 frames allocated {bytes} B");
            GD.Print($"chaos light: {marked.Count} layers, {took.TotalMilliseconds:F3} us per frame (values unchanged), {bytes} B");
            actor.RemoveChild(late);
            late.Free();
            Check(ChaosLayers.Meshes == 0, "confusion left: layers stay");
        }

        // a region skill spawn makes the part on its actor
        int made = ChaosLightEffect.Made;
        int h = skills.SpawnOn(Bleed, actor, 0);
        Check(h != 0 && ChaosLightEffect.Made == made + 1 && ChaosLayers.Meshes == marked.Count, $"spawn: handle {h}, made {ChaosLightEffect.Made - made}, {ChaosLayers.Meshes} meshes");
        Done();
    }

    static EffectPart Part(EffectTable effects, string file)
    {
        var list = effects.Get(file);
        if (list != null) foreach (var p in list) if (p.TypeId == EffectSkipTable.TypeChaosLight && p.ChaosLight != null) return p;
        return null;
    }

    static MeshInstance3D Find(Node n, string name)
    {
        if (n is MeshInstance3D mi && mi.Name == name) return mi;
        for (int i = 0; i < n.GetChildCount(); i++) if (Find(n.GetChild(i), name) is { } r) return r;
        return null;
    }

    static void Marked(Node n, List<MeshInstance3D> into)
    {
        if (n is MeshInstance3D mi && mi.HasMeta(ChaosLayers.Meta)) into.Add(mi);
        for (int i = 0; i < n.GetChildCount(); i++) Marked(n.GetChild(i), into);
    }

    void Done()
    {
        foreach (var f in fails) GD.PrintErr("chaos light: " + f);
        GD.Print(fails.Count == 0 ? "chaos light: PASS" : $"chaos light: FAIL ({fails.Count})");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
