using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2Godot.World;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/field_fade.tscn) of the camera fade (world/FieldFade, BGTransparencyMgr):
/// <list type="bullet">
/// <item>a fade whose 300 ms run out before any frame stepped it ends at the 0.3 floor (UpdateFadeOut's last running frame
/// sets it in the client), on the 02000110_bf fade targets;</item>
/// <item>only a mesh whose active material is an MS2StandardMaterial is faded (bgtransparencymgr.cpp:517-520): the
/// ke_fi_prop_light_a01 glass on 61000031_me keeps its own draw;</item>
/// <item>the animated models world/FieldProps draws are faded too, skinned (the he_fi_nature_reed_a02 reeds on 52000094_qd)
/// and rigid (the clockwork on 02000161_bf): the 300 ms test with a camera and feet on either side of one takes it, its
/// meshes draw the fade material at the floor, and a test that misses it fades it back in and gives each mesh its own
/// material again;</item>
/// <item>a faded part draws as the client's capture of the 02000110 tree shows (captures/02000110_450_1650_4350_treefade):
/// a depth-only pass first (AlphaShellComposite::MarkScene, ModelBuilder.FadeShell) with the fade material as its next pass,
/// each held entity in its own pair of render priorities, the farther one first;</item>
/// <item>the fade back in, 250 ms after it starts, is at 250 / 300 = 5/6, the 0.8333 of the capture's fade-in frame
/// (UpdateFadeIn, Timer::GetRatio).</item>
/// </list>
/// Needs MS2_DATA and MS2_NIFXML (or local.cfg). Quits with 0 when it holds, else 1.
/// </summary>
public partial class FieldFadeCheck : Node
{
    const float Floor = 0.3f;
    const ulong Past = 400;   // past the 300 ms timer

    ModelBuilder builder;
    PackSet packs;
    readonly List<string> fails = new();
    bool done;

    Camera3D cam;
    Vector3? feet;

    sealed class Built
    {
        public FieldMap Map;
        public FieldBuilder Fb;
        public FieldFade Fade;
        public FieldProps Props;
        public Node Root;
    }

    /// <summary>A field as Field builds it for the fade: the animated props taken by world/FieldProps, the fade given them.</summary>
    Built BuildField(string xblock)
    {
        var b = new Built { Map = FieldMap.Load(packs, new FlatLibrary(packs), xblock), Root = new Node3D { Name = xblock } };
        AddChild(b.Root);
        b.Props = new FieldProps(builder);
        b.Props.Clock = () => 0;
        b.Fb = new FieldBuilder(packs, AssetIndex.For(packs), builder) { DrawsInstance = b.Props.TakesModel };
        foreach (var _ in b.Fb.BuildSteps(b.Map)) { }
        b.Root.AddChild(b.Fb.World);
        b.Fade = new FieldFade(b.Fb.FadeDraws, () => feet, () => cam);
        b.Root.AddChild(b.Fade);
        b.Fade.UseProps(b.Props, b.Fb.DrawnElsewhere, b.Fb.ReactableElsewhere);
        b.Props.Entities.AddRange(b.Fb.DrawnElsewhere);
        b.Props.NotReceiving.UnionWith(b.Fb.NotReceivingElsewhere);
        foreach (var (e, off) in b.Fb.LightOffElsewhere) b.Props.LightOff[e] = off;
        b.Root.AddChild(b.Props.Build());
        return b;
    }

    static float Alpha(ShaderMaterial m) => m.GetShaderParameter(ShaderParam.FadeAlpha).AsSingle();

    /// <summary>Every part of the entity's fade at the floor (its material alpha times 0.3).</summary>
    void AtFloor(Built b, FieldEntity e, string what)
    {
        foreach (var (mat, mesh) in b.Fade.FadeMaterials(e))
            if (Math.Abs(Alpha(mat) - mesh.MaterialAlpha * Floor) > 1e-4f)
            {
                fails.Add($"{what}: a faded part is at {Alpha(mat):0.###}, not the floor {mesh.MaterialAlpha * Floor:0.###}");
                return;
            }
    }

    /// <summary>Every part of the entity's fade drawn as a depth pass with its fade material next, above the plain transparent draws.</summary>
    void TwoPass(Built b, FieldEntity e, string what)
    {
        foreach (var (drawn, fade) in b.Fade.FadePasses(e))
        {
            if (drawn is not ShaderMaterial shell || shell == fade || !shell.Shader.Code.Contains("#define SHELL\n"))
            { fails.Add($"{what}: a faded part draws {(drawn == fade ? "its fade material" : drawn?.GetType().Name ?? "nothing")} without a depth pass first"); return; }
            if (shell.NextPass != fade) { fails.Add($"{what}: a faded part's depth pass is not followed by its fade material"); return; }
            if (!(0 < shell.RenderPriority && shell.RenderPriority < fade.RenderPriority))
            { fails.Add($"{what}: a faded part's passes draw at priorities {shell.RenderPriority} / {fade.RenderPriority}, not after the transparent draws, depth first"); return; }
        }
    }

    /// <summary>The far entity's colour pass draws before the near one's depth pass.</summary>
    void Order(Built b, FieldEntity far, FieldEntity near)
    {
        var f = b.Fade.FadePasses(far); var n = b.Fade.FadePasses(near);
        if (f.Count == 0 || n.Count == 0) { fails.Add("order: an entity of the pair is not held"); return; }
        int fMax = f.Max(x => x.Fade.RenderPriority), nMin = n.Min(x => x.Drawn.RenderPriority);
        if (fMax >= nMin) fails.Add($"order: {far.Name} (far) draws its colour at {fMax}, not before {near.Name} (near) from {nMin}");
    }

    void Settle()
    {
        var b = BuildField("02000110_bf");
        Cost(b, "02000110_bf");
        int n = 0;
        foreach (var (e, draws) in b.Fb.FadeDraws)
        {
            if (!draws.Any(d => ModelData.IsStandardMaterial(d.Mesh.ActiveMaterial))) continue;
            b.Fade.FadeOutNow(e);
            if (b.Fade.FadeMaterials(e).Count == 0) { fails.Add($"settle: {e.Name} was not faded"); continue; }
            // no frame between the Apply and the step: the 300 ms are over at the first one
            b.Fade.StepTo(Time.GetTicksMsec() + Past);
            AtFloor(b, e, $"settle: {e.Name} stepped first after its timer ran out");
            TwoPass(b, e, $"settle: {e.Name}");
            if (++n == 40) break;
        }
        // two held at once (the second's Apply sends the first back in): each in its own priorities, the farther first
        var two = b.Fb.FadeDraws.Where(kv => kv.Value.Any(d => ModelData.IsStandardMaterial(d.Mesh.ActiveMaterial))).Select(kv => kv.Key).Take(2).ToList();
        if (two.Count == 2)
        {
            var c0 = b.Fade.TargetBound(two[0]).Value.Centre; var c1 = b.Fade.TargetBound(two[1]).Value.Centre;
            cam.Position = c1 + (c1 - c0).Normalized() * 500f;   // two[0] the farther
            b.Fade.FadeOutNow(two[0]);
            b.Fade.FadeOutNow(two[1]);
            Order(b, two[0], two[1]);
            TwoPass(b, two[0], $"order: {two[0].Name} fading back in");
            // the fade back in, 250 ms on: 5/6
            if (b.Fade.StartOf(two[0]) is { } start)
            {
                b.Fade.StepTo(start + 250);
                foreach (var (mat, mesh) in b.Fade.FadeMaterials(two[0]))
                    if (Math.Abs(Alpha(mat) - mesh.MaterialAlpha * 5f / 6f) > 1e-4f)
                    { fails.Add($"fade-in: {two[0].Name} at {Alpha(mat):0.####} 250 ms on, not 5/6"); break; }
            }
            else fails.Add($"fade-in: {two[0].Name} is not held after the next fade's Apply");
            cam.Position = Vector3.Zero;
        }
        GD.Print($"field fade check: settle {n} fade targets on 02000110_bf");
        if (n == 0) fails.Add("settle: no fade target with a standard material on 02000110_bf");
        Drop(b);
    }

    void Gate()
    {
        var b = BuildField("61000031_me");
        int glass = 0, kept = 0;
        foreach (var (e, draws) in b.Fb.FadeDraws)
        {
            var others = draws.Where(d => !ModelData.IsStandardMaterial(d.Mesh.ActiveMaterial)).ToList();
            if (others.Count == 0) continue;
            var before = others.Select(d => d.Mm.GetInstanceTransform(d.Index)).ToList();
            b.Fade.FadeOutNow(e);
            glass += others.Count;
            var fading = b.Fade.Fading(e);
            for (int i = 0; i < others.Count; i++)
            {
                if (fading.Contains(others[i])) { fails.Add($"gate: {e.Name} {others[i].Mesh.ActiveMaterial ?? "(no material)"} mesh was faded"); continue; }
                if (others[i].Mm.GetInstanceTransform(others[i].Index) != before[i]) { fails.Add($"gate: {e.Name} {others[i].Mesh.ActiveMaterial} mesh's own draw was hidden"); continue; }
                kept++;
            }
            if (fading.Count != draws.Count - others.Count) fails.Add($"gate: {e.Name} fades {fading.Count} of its {draws.Count - others.Count} standard meshes");
        }
        GD.Print($"field fade check: gate {glass} non-standard meshes of fade targets on 61000031_me, {kept} kept their draw");
        if (glass == 0) fails.Add("gate: no fade target with a non-standard mesh on 61000031_me");
        Drop(b);
    }

    void Props(string xblock, string model, string kind)
    {
        var b = BuildField(xblock);
        var ents = b.Fb.DrawnElsewhere.Where(e => b.Fade.TargetBound(e) != null && (e.Get("NifAsset") ?? "") != "").ToList();
        int taken = 0, tried = 0;
        Cost(b, xblock);
        foreach (var e in ents)
        {
            var bound = b.Fade.TargetBound(e);
            if (bound is not { } bd || bd.Radius < 0) continue;
            var nodes = b.Props.MeshesOf(e);
            if (nodes.Count == 0) continue;
            if (!nodes.Any(m => m.Mesh.Skinned == (kind == "skinned"))) continue;
            tried++;
            // the camera on one side of the prop, the feet on the other, the first ray (feet + RayLow) through its centre
            var dir = new Vector3(0.6f, 0.3f, 0.74f).Normalized();
            var eye = bd.Centre + dir * (bd.Radius + 600);
            feet = bd.Centre - dir * (bd.Radius + 150) - Vector3.Up * 20f;
            cam.Position = eye;
            cam.LookAtFromPosition(eye, bd.Centre, Vector3.Up);
            b.Fade.TestNow();
            var fading = b.Fade.FadingProps(e);
            if (fading.Count == 0) { fails.Add($"{kind}: {xblock} {e.Name}: the test between camera and feet did not take it"); feet = null; break; }
            foreach (var (node, mat) in fading)
                if (node.MaterialOverride != mat) fails.Add($"{kind}: {e.Name}: a faded mesh does not draw its faded passes");
            b.Fade.StepTo(Time.GetTicksMsec() + Past);
            AtFloor(b, e, $"{kind}: {e.Name}");
            TwoPass(b, e, $"{kind}: {e.Name}");
            // the camera turned away: the next test takes nothing, the fade goes back in and ends
            feet = bd.Centre + Vector3.Up * 100000f;
            cam.LookAtFromPosition(eye + Vector3.Up * 200000f, eye + Vector3.Up * 300000f, Vector3.Forward);
            b.Fade.TestNow();
            b.Fade.StepTo(Time.GetTicksMsec() + Past);
            if (b.Fade.Holds(e)) fails.Add($"{kind}: {e.Name}: still held after the fade back in");
            foreach (var (node, _) in fading)
                if (GodotObject.IsInstanceValid(node) && node.MaterialOverride != null) fails.Add($"{kind}: {e.Name}: a mesh kept the fade material after the fade back in");
            taken++;
            if (taken == 5) break;
        }
        GD.Print($"field fade check: {kind} props on {xblock} ({model}): {ents.Count} targets, {taken} of {tried} tried faded and back");
        if (tried == 0) fails.Add($"{kind}: no {kind} animated fade target drawn on {xblock}");
        feet = null;
        Drop(b);
    }

    /// <summary>The 300 ms test's cost on the field (printed, MS2_FADE_COST=1): the camera over the field's middle, the feet below it.</summary>
    void Cost(Built b, string xblock)
    {
        if (System.Environment.GetEnvironmentVariable("MS2_FADE_COST") != "1") return;
        var all = b.Fb.FadeDraws.Keys.Concat(b.Fb.DrawnElsewhere).Select(e => b.Fade.TargetBound(e)).Where(x => x is { Radius: >= 0 }).Select(x => x.Value.Centre).ToList();
        if (all.Count == 0) return;
        var mid = all.Aggregate(Vector3.Zero, (s, c) => s + c) / all.Count;
        feet = mid;
        cam.LookAtFromPosition(mid + new Vector3(0, 900, 900), mid, Vector3.Up);
        for (int i = 0; i < 20; i++) b.Fade.TestNow();
        const int n = 300;
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < n; i++) b.Fade.TestNow();
        sw.Stop();
        bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
        GD.Print($"field fade check: cost {xblock}: test {sw.Elapsed.TotalMilliseconds * 1000 / n:0.0} us, {bytes / n} B per test, {b.Fade.Faded} fading");
        b.Fade.Release(null);
        feet = null;
    }

    void Drop(Built b)
    {
        RemoveChild(b.Root);
        b.Root.QueueFree();
    }

    public override void _Ready()
    {
        try
        {
            packs = PackSet.Shared(Settings.DataDir);
            builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
            cam = new Camera3D { Far = 1000000f, Near = 1f };
            AddChild(cam);
            cam.MakeCurrent();
        }
        catch (Exception e) { fails.Add(e.ToString()); }
    }

    public override void _Process(double delta)
    {
        if (done) return;
        done = true;
        if (fails.Count == 0)
            foreach (var step in new Action[] { Settle, Gate,
                () => Props("52000094_qd", "he_fi_nature_reed_a02", "skinned"),
                () => Props("02000161_bf", "clockwork", "rigid") })
                try { step(); }
                catch (Exception e) { fails.Add(e.ToString()); }
        GD.Print($"field fade check: {(fails.Count == 0 ? "OK" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
