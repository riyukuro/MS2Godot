using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;

namespace Ms2Godot.World;

/// <summary>
/// WorldService::FadeEntity (@0x141bd8e30, worldservice_1.cpp:8393-8424) for the field's trigger meshes, called by
/// TriggerObjectMesh::vfunc_2 (triggeractioncli.cpp:651) with the event's Fade times 0.1 seconds. Showing makes a
/// CEntityShower (alpha 0 to 1) unless one runs for the entity; hiding a CEntityHider (1 to 0) unless one runs.
/// FUN_141bd8860 (worldservice_1.cpp:8299-8330) and FUN_141bd8ae0 (:8346-8388) start it: a running fade the other way is
/// dropped and its alpha becomes the new one's start (CEntityShower::SetAlpha, alphaanimation.cpp:660, sets the
/// interpolator's from). WorldService's tick (worldservice_1.cpp:9633-9646) runs the hiders, then the showers;
/// CEntityShower::vfunc_2 / CEntityHider::vfunc_2 (alphaanimation.cpp:627-657, 674-699) take the alpha from a
/// TLinearInterpolator (elapsed += delta; past the duration the end value, alphaanimation.h:108-118), set it on the scene
/// graph (SetNodeAlpha: the material alpha times it, blending on) and finish once the game time is past the start plus
/// the duration, after setting that frame's alpha. Here a fading mesh's drawn instances leave their MultiMesh and draw
/// alone with a blended copy of their material whose material alpha is the mesh's own times the fade (no vertex colour
/// swap and no alpha test change, which SetNodeAlpha does not make); a finished hider leaves every instance hidden
/// (zero basis), a finished shower puts every instance back. The node ticks only while a fade runs.
/// STILL-OPEN: the start gates read property 0xa0b6b179 (set while the entity is out of its instancing) and the
/// instancing's hidden state (0x23f9860e, HardwareInstancingService); here a hide starts only on a shown mesh and a show
/// only on a hidden one, so a show of a shown mesh does not fade it in again from 0. The shadow of a fading mesh stays as
/// it was until the fade ends. The blend mode of a mesh with blending off whose NiAlphaProperty's destination is ONE
/// (SetNodeAlpha only turns blending on) follows ModelBuilder.FadeMaterial's choice.
/// </summary>
public sealed partial class TriggerFade : Node
{
    /// <summary>The name of a fading draw's own node.</summary>
    public const string CopyName = "trigger_fade";
    static readonly StringName ReceiveParam = "ms2_receive";
    static readonly Basis Zero = new(Vector3.Zero, Vector3.Zero, Vector3.Zero);

    /// <summary>
    /// One trigger mesh: every instance with its own transform (drawn and shadow), the drawn ones a fade draws alone, and
    /// whether it is shown (the entity's state, as the client keeps it on the entity rather than reading its instances).
    /// </summary>
    public sealed class Target
    {
        public readonly List<(MultiMesh Mm, int Index, Transform3D Xf)> All = new();
        public readonly List<FieldBuilder.FadeDraw> Draws = new();
        public bool Shown = true;
    }

    sealed class Part { public FieldBuilder.FadeDraw Draw; public MultiMeshInstance3D Node; public ShaderMaterial Mat; }

    sealed class Run
    {
        public Target Target;
        public float Alpha, From, To, Duration, Elapsed, End;
        public readonly List<Part> Parts = new();

        /// <summary>TLinearInterpolator&lt;float&gt;::Get (alphaanimation.h:108-118).</summary>
        public float Get(float delta)
        {
            Elapsed += delta;
            float ratio = Elapsed / Duration;
            if (ratio > 1f) return To;
            return From * (1f - ratio) + To * ratio;
        }
    }

    readonly Dictionary<int, Run> showers = new(), hiders = new();
    readonly List<int> done = new();
    float clock;

    public TriggerFade()
    {
        Name = "TriggerFade";
        SetProcess(false);
    }

    /// <summary>Whether a fade runs for the object.</summary>
    public bool Fading(int id) => showers.ContainsKey(id) || hiders.ContainsKey(id);

    /// <summary>Shows or hides every instance of the mesh at once: its own transform, or a zero basis.</summary>
    public static void Switch(Target t, bool show)
    {
        foreach (var (mm, i, xf) in t.All)
        {
            mm.SetInstanceTransform(i, show ? xf : new Transform3D(Zero, xf.Origin));
            var set = hidden.GetOrCreateValue(mm);
            if (show) set.Remove(i); else set.Add(i);
            Switched?.Invoke(mm, i);
        }
        t.Shown = show;
    }

    /// <summary>
    /// Raised for each instance a <see cref="Switch"/> or a fade's start sets. The camera fade (world/FieldFade) holding the
    /// instance in a copy of its own drops that copy, without putting its transform back: the switch decided what the instance
    /// draws (else a hidden prop kept its faded copy, and a shown one drew in both).
    /// </summary>
    public static event Action<MultiMesh, int> Switched;

    // the instances a switch left hidden, by MultiMesh: what the slot holds without reading it back from the renderer (the
    // headless one keeps no instance transforms)
    static readonly ConditionalWeakTable<MultiMesh, HashSet<int>> hidden = new();

    /// <summary>Whether a <see cref="Switch"/> left the instance hidden.</summary>
    public static bool Hidden(MultiMesh mm, int index) => mm != null && hidden.TryGetValue(mm, out var set) && set.Contains(index);

    /// <summary>FadeEntity: true when a fade starts.</summary>
    public bool FadeEntity(int id, Target t, bool show, float fade)
    {
        var mine = show ? showers : hiders;
        var other = show ? hiders : showers;
        if (mine.ContainsKey(id)) return false;
        var run = new Run { Target = t, From = show ? 0f : 1f, To = show ? 1f : 0f, Duration = fade, End = clock + fade };
        run.Alpha = run.From;
        if (other.Remove(id, out var was))
        {
            run.From = run.Alpha = was.Alpha;     // CEntityShower::SetAlpha(the other's GetAlpha)
            run.Parts.AddRange(was.Parts);
        }
        else if (t.Shown != show) Begin(run);
        else return false;
        mine[id] = run;
        SetProcess(true);
        return true;
    }

    /// <summary>Drops any fade of the object and shows or hides it at once (a breakable's state change, world/FieldBreakables).</summary>
    public void Stop(int id, Target t, bool show)
    {
        if (showers.Remove(id, out var a)) foreach (var p in a.Parts) p.Node.QueueFree();
        if (hiders.Remove(id, out var b)) foreach (var p in b.Parts) p.Node.QueueFree();
        Switch(t, show);
    }

    void Begin(Run run)
    {
        foreach (var d in run.Target.Draws)
        {
            var mesh = d.Mm.Mesh;
            var colour = (ShaderMaterial)mesh.SurfaceGetMaterial(0);
            if (colour.NextPass is ShaderMaterial next && colour.Shader != next.Shader) colour = next;   // a fluid shell's colour pass
            var mat = ModelBuilder.FadeMaterial(colour, d.Mesh);
            mat.SetShaderParameter(ShaderParam.FadeVcSwap, false);
            Transform3D xf = default;
            foreach (var (mm, i, x) in run.Target.All) if (mm == d.Mm && i == d.Index) { xf = x; break; }
            var one = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh, InstanceCount = 1 };
            one.SetInstanceTransform(0, xf);
            one.SetInstanceColor(0, d.Mm.GetInstanceColor(d.Index));
            var node = new MultiMeshInstance3D { Name = CopyName, Multimesh = one, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            if (d.NotReceiving) node.SetInstanceShaderParameter(ReceiveParam, 0f);
            d.Parent.AddChild(node, forceReadableName: true);   // the name stays findable beside its siblings
            d.Mm.SetInstanceTransform(d.Index, new Transform3D(Zero, xf.Origin));
            Switched?.Invoke(d.Mm, d.Index);
            var part = new Part { Draw = d, Node = node, Mat = mat };
            run.Parts.Add(part);
        }
        SetAlpha(run);
    }

    static void SetAlpha(Run run)
    {
        foreach (var p in run.Parts) p.Mat.SetShaderParameter(ShaderParam.FadeAlpha, p.Draw.Mesh.MaterialAlpha * run.Alpha);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        clock += dt;
        Tick(hiders, dt, false);
        Tick(showers, dt, true);
        if (showers.Count == 0 && hiders.Count == 0) SetProcess(false);
    }

    void Tick(Dictionary<int, Run> runs, float dt, bool show)
    {
        done.Clear();
        foreach (var (id, run) in runs)
        {
            run.Alpha = run.Get(dt);
            SetAlpha(run);
            if (clock > run.End) done.Add(id);
        }
        foreach (int id in done)
        {
            runs.Remove(id, out var run);
            Switch(run.Target, show);
            foreach (var p in run.Parts) p.Node.QueueFree();
        }
    }
}
