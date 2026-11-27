using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2Godot.World;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/field_fade.tscn) of the camera fade (world/FieldFade, BGTransparencyMgr):
/// <list type="bullet">
/// <item>the fades run on a clock that moves at most 250 ms a frame, as the client's game clock (GameTimeClock's
/// m_maxDelta): a fade whose first frame comes 400 ms after its Apply is stepped 250 ms, to 5/6, so it reaches the 0.3
/// floor, and a second such frame leaves it there, on the 02000110_bf fade targets; a fade stepped 100 ms and then 400 ms
/// stays at 1 - 1/3, its last running frame's value (UpdateFadeOut skips an element whose timer has run out);</item>
/// <item>only a mesh whose active material is an MS2StandardMaterial is faded (bgtransparencymgr.cpp:517-520): the
/// ke_fi_prop_light_a01 glass on 61000031_me keeps its own draw;</item>
/// <item>the animated models world/FieldProps draws are faded too, skinned (the he_fi_nature_reed_a02 reeds on 52000094_qd)
/// and rigid (the clockwork on 02000161_bf): the 300 ms test with a camera and feet on either side of one takes it, its
/// meshes draw the fade material at the floor, and a test that misses it fades it back in and gives each mesh its own
/// material again;</item>
/// <item>a faded part draws as the client's capture of the 02000110 tree shows (captures/02000110_450_1650_4350_treefade):
/// a depth-only pass first (AlphaShellComposite::MarkScene, ModelBuilder.FadeShell) with the fade material as its next pass,
/// each held entity in its own pair of render priorities, the farther one first;</item>
/// <item>the fade back in, its first frame 400 ms after it starts, is at 250 / 300 = 5/6, the 0.8333 of the capture's
/// fade-in frame (UpdateFadeIn, Timer::GetRatio), and still held; the next frame ends it.</item>
/// </list>
/// Needs MS2_DATA and MS2_NIFXML (or local.cfg). Quits with 0 when it holds, else 1.
/// </summary>
public partial class FieldFadeCheck : Node
{
    const float Floor = 0.3f;
    const double Past = 400;   // a frame longer than the 300 ms timer (the clock moves 250 ms of it)

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
        // the fade passes warmed as Field does at load; a second call builds no more copies
        b.Fade.Warm();
        int warmed = b.Fade.Warmed;
        b.Fade.Warm();
        if (b.Fade.Warmed != warmed) fails.Add($"warm: {xblock}: a second Warm built {b.Fade.Warmed - warmed} more copies");
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
        if (fMax >= nMin) fails.Add($"order: {far.Name} (deeper) draws its colour at {fMax}, not before {near.Name} (nearer) from {nMin}");
    }

    /// <summary>The mean of the entity's drawn standard-material draws' box centres, as the fade's Apply holds them: the
    /// centres Sort keys by.</summary>
    static Vector3? DrawCentre(Built b, FieldEntity e)
    {
        Vector3 sum = Vector3.Zero; int n = 0;
        foreach (var d in b.Fb.FadeDraws[e])
        {
            var xf = d.Mm.GetInstanceTransform(d.Index);
            if (TriggerFade.Hidden(d.Mm, d.Index) || xf.Basis.Determinant() == 0 || !ModelData.IsStandardMaterial(d.Mesh.ActiveMaterial)) continue;
            sum += (d.Parent.GlobalTransform * xf * d.Mm.Mesh.GetAabb()).GetCenter(); n++;
        }
        return n == 0 ? null : sum / n;
    }

    void Look(Vector3 from, Vector3 dir) => cam.LookAtFromPosition(from, from + dir, Math.Abs(dir.Normalized().Y) > 0.99f ? Vector3.Forward : Vector3.Up);

    void Settle()
    {
        var b = BuildField("02000110_bf");
        if (b.Fade.Warmed == 0) fails.Add("warm: 02000110_bf: no hidden copies of the fade passes");
        Cost(b, "02000110_bf");
        int n = 0;
        foreach (var (e, draws) in b.Fb.FadeDraws)
        {
            if (!draws.Any(d => ModelData.IsStandardMaterial(d.Mesh.ActiveMaterial))) continue;
            b.Fade.FadeOutNow(e);
            if (b.Fade.FadeMaterials(e).Count == 0) { fails.Add($"settle: {e.Name} was not faded"); continue; }
            // the first frame after the Apply comes 400 ms later: the clock moves 250 ms, 5/6 of the way, past the floor
            b.Fade.StepFrame(Past);
            AtFloor(b, e, $"settle: {e.Name} first stepped 400 ms after its Apply");
            b.Fade.StepFrame(Past);
            AtFloor(b, e, $"settle: {e.Name} stepped again after its timer ran out");
            TwoPass(b, e, $"settle: {e.Name}");
            if (++n == 40) break;
        }
        // two held at once (the second's Apply sends the first back in): each in its own priorities, the deeper along the
        // view first (AlphaShellComposite::Update's key, CompareAlpha's largest first)
        var four = b.Fb.FadeDraws.Where(kv => !b.Fade.Holds(kv.Key) && DrawCentre(b, kv.Key) != null).Select(kv => kv.Key).Take(4).ToList();
        var two = four.Take(2).ToList();
        if (four.Count == 4)
        {
            // the pair where depth and distance disagree: the camera looks along D, at 0.29 s more depth for four[3], so
            // four[3] draws first though four[2] is the farther from the eye (1.19 s against s)
            Vector3 m2 = DrawCentre(b, four[2]).Value, m3 = DrawCentre(b, four[3]).Value;
            float s = m2.DistanceTo(m3);
            var u = (m3 - m2) / s;
            var p = u.Cross(Math.Abs(u.Y) < 0.9f ? Vector3.Up : Vector3.Right).Normalized();
            var dir = (p + 0.3f * u).Normalized();
            var eye = m3 - dir * s;
            Look(eye, dir);
            b.Fade.FadeOutNow(four[2]);
            b.Fade.FadeOutNow(four[3]);
            if (b.Fade.FadingProps(four[2]).Count + b.Fade.FadingProps(four[3]).Count > 0) fails.Add("order: the depth pair holds prop parts");
            else if (!(eye.DistanceTo(m2) > eye.DistanceTo(m3) && (m3 - eye).Dot(dir) > (m2 - eye).Dot(dir))) fails.Add("order: the depth pair's camera is not placed as meant");
            Order(b, four[3], four[2]);
            // the camera turned round, and one 16 ms frame with no test (no feet): the order is keyed again in the frame,
            // four[2] now the deeper (PreRenderProcessList updates every group each frame)
            feet = null;
            Look(m2 + dir * (2 * s), -dir);
            b.Fade.StepFrame(16);
            Order(b, four[2], four[3]);
        }
        else fails.Add("order: fewer than four fade targets with a drawn standard material");
        if (two.Count == 2)
        {
            Vector3 c0 = DrawCentre(b, two[0]).Value, c1 = DrawCentre(b, two[1]).Value;
            Look(c1 + (c1 - c0).Normalized() * 500f, c0 - c1);   // two[0] the deeper
            b.Fade.FadeOutNow(two[0]);
            b.Fade.FadeOutNow(two[1]);
            Order(b, two[0], two[1]);
            TwoPass(b, two[0], $"order: {two[0].Name} fading back in");
            // the fade back in, its first frame 400 ms on: the clock moves 250 ms, 5/6, and the fade is still held
            if (b.Fade.StartOf(two[0]) is { } start && start == b.Fade.Now)
            {
                b.Fade.StepFrame(Past);
                if (!b.Fade.Holds(two[0])) fails.Add($"fade-in: {two[0].Name} released at its first frame, 400 ms on");
                foreach (var (mat, mesh) in b.Fade.FadeMaterials(two[0]))
                    if (Math.Abs(Alpha(mat) - mesh.MaterialAlpha * 5f / 6f) > 1e-4f)
                    { fails.Add($"fade-in: {two[0].Name} at {Alpha(mat):0.####} at its first frame, 400 ms on, not 5/6"); break; }
                // a 16 ms frame: 266 ms on the clock, still running; then a 400 ms frame ends it
                b.Fade.StepFrame(16);
                if (!b.Fade.Holds(two[0])) fails.Add($"fade-in: {two[0].Name} released at 266 ms on the clock");
                b.Fade.StepFrame(Past);
                if (b.Fade.Holds(two[0])) fails.Add($"fade-in: {two[0].Name} still held after its timer ran out");
            }
            else fails.Add($"fade-in: {two[0].Name} is not held from the next fade's Apply");
            cam.Transform = Transform3D.Identity;
        }
        // a fade stepped 100 ms, then a 400 ms frame: its timer runs out in that frame, which it skips, so it keeps the
        // 1 - 1/3 of the frame before (UpdateFadeOut, Timer::IsRunning)
        var late = b.Fb.FadeDraws.Where(kv => kv.Value.Any(d => ModelData.IsStandardMaterial(d.Mesh.ActiveMaterial)) && !b.Fade.Holds(kv.Key)).Select(kv => kv.Key).FirstOrDefault();
        if (late != null)
        {
            b.Fade.FadeOutNow(late);
            b.Fade.StepFrame(100);
            b.Fade.StepFrame(Past);
            var mats = b.Fade.FadeMaterials(late);
            if (mats.Count == 0) fails.Add($"late: {late.Name} was not faded");
            foreach (var (mat, mesh) in mats)
            {
                float want = mesh.MaterialAlpha * Math.Max(1f - 1f / 3f, Floor);
                if (Math.Abs(Alpha(mat) - want) > 1e-4f) { fails.Add($"late: {late.Name} at {Alpha(mat):0.####} after 100 ms and a 400 ms frame, not {want:0.####}"); break; }
            }
        }
        else fails.Add("late: no fade target left that is not held");
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
            b.Fade.StepFrame(Past);
            AtFloor(b, e, $"{kind}: {e.Name}");
            TwoPass(b, e, $"{kind}: {e.Name}");
            // the camera turned away: the next test takes nothing, the fade goes back in and ends
            feet = bd.Centre + Vector3.Up * 100000f;
            cam.LookAtFromPosition(eye + Vector3.Up * 200000f, eye + Vector3.Up * 300000f, Vector3.Forward);
            b.Fade.TestNow();
            b.Fade.StepFrame(Past);   // 5/6 back in
            b.Fade.StepFrame(Past);   // over
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
