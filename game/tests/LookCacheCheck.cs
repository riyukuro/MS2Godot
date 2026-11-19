using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Ms2.Formats.Pack;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/look_cache.tscn) of the UI's kept character builder (<see cref="ModelBuilder.Ui"/>):
/// <list type="bullet">
/// <item>a look built on it is the look a new builder makes (the same nodes, meshes, surfaces, materials, textures by size and animations);</item>
/// <item>a second build of the same look uses the first one's meshes and animations;</item>
/// <item>a build of another look (the other gender) leaves none of the first look's models, meshes or animations kept;</item>
/// <item>after <see cref="ModelBuilder.DropUi"/> the UI gets a new builder.</item>
/// </list>
/// Needs MS2_DATA and MS2_NIFXML (or local.cfg). Quits with 0 when it holds, else 1.
/// </summary>
public partial class LookCacheCheck : Node
{
    public override void _Ready()
    {
        var fails = new List<string>();
        try { Run(fails); }
        catch (Exception e) { fails.Add(e.ToString()); }
        GD.Print($"look cache: {(fails.Count == 0 ? "ok" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    void Run(List<string> fails)
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var nifXml = Settings.NifXml;
        var list = Flow.Session.List();
        var female = list.First(c => c.User.Gender == 1);
        var male = list.First(c => c.User.Gender == 0);

        Flow.Session.Chosen = female;
        var fresh = Look.PlayerLook.Build(new ModelBuilder(ModelBuilder.SharedSpec(nifXml), packs), packs);
        ModelBuilder.DropUi();
        var ui = ModelBuilder.Ui(packs, nifXml);
        var a = Look.PlayerLook.Build(ui, packs);
        var b = Look.PlayerLook.Build(ModelBuilder.Ui(packs, nifXml), packs);
        if (ModelBuilder.Ui(packs, nifXml) != ui) fails.Add("Ui gave another builder without a drop");
        var newOrder = string.Join(",", new ModelBuilder(ModelBuilder.SharedSpec(nifXml), packs).TextureArchives);
        if (string.Join(",", ModelBuilder.Ui(packs, nifXml).TextureArchives) != newOrder)
            fails.Add("a kept builder starts a look with another texture archive order than a new builder");

        string want = Describe(fresh.visual), gotA = Describe(a.visual), gotB = Describe(b.visual);
        if (gotA != want) fails.Add("kept first build differs from a new builder's:\n" + FirstDiff(want, gotA));
        if (gotB != want) fails.Add("kept second build differs from a new builder's:\n" + FirstDiff(want, gotB));

        var meshesA = Meshes(a.visual); var meshesB = Meshes(b.visual);
        if (meshesA.Count == 0 || !meshesA.SetEquals(meshesB)) fails.Add($"second build did not reuse the meshes ({meshesA.Intersect(meshesB).Count()} of {meshesA.Count})");
        if (a.anim.GetAnimation("idle") != b.anim.GetAnimation("idle")) fails.Add("second build did not reuse the idle animation");

        Flow.Session.Chosen = male;
        var m = Look.PlayerLook.Build(ModelBuilder.Ui(packs, nifXml), packs);
        var meshesM = Meshes(m.visual);
        if (meshesA.Overlaps(meshesM)) fails.Add("the female look's meshes were reused for the male look");
        var kept = ui.KeptKeys().ToList();
        if (kept.Any(k => k.Contains("f_body.nif", StringComparison.OrdinalIgnoreCase))) fails.Add("the female body is still kept after the male look");
        var c = Look.PlayerLook.Build(ModelBuilder.Ui(packs, nifXml), packs);
        if (!Meshes(c.visual).SetEquals(meshesM)) fails.Add("male look not reused on its second build");
        if (ui.KeptKeys().Count() != kept.Count) fails.Add($"kept set grew on a repeat of the same look ({kept.Count} -> {ui.KeptKeys().Count()})");

        ModelBuilder.DropUi();
        if (ModelBuilder.Ui(packs, nifXml) == ui) fails.Add("DropUi kept the builder");
        GD.Print($"look cache: female {meshesA.Count} meshes, male {meshesM.Count}, kept after male {kept.Count}");
        foreach (var v in new[] { fresh.visual, a.visual, b.visual, m.visual, c.visual }) v.Free();
    }

    static HashSet<Mesh> Meshes(Node n)
    {
        var s = new HashSet<Mesh>();
        void Walk(Node x) { if (x is MeshInstance3D mi && mi.Mesh != null) s.Add(mi.Mesh); foreach (var ch in x.GetChildren()) Walk(ch); }
        Walk(n);
        return s;
    }

    static string Describe(Node root)
    {
        var sb = new System.Text.StringBuilder();
        void Walk(Node x, string path)
        {
            sb.Append(path).Append(' ').Append(x.GetClass());
            if (x is Node3D n3) sb.Append(' ').Append(n3.Transform).Append(n3.Visible ? "" : " hidden");
            if (x is MeshInstance3D mi && mi.Mesh is ArrayMesh am)
            {
                sb.Append($" skin {mi.Skin?.GetBindCount()} skel {mi.Skeleton}");
                for (int i = 0; i < am.GetSurfaceCount(); i++)
                {
                    sb.Append($" | s{i} {am.SurfaceGetArrayLen(i)}/{am.SurfaceGetArrayIndexLen(i)} fmt {am.SurfaceGetFormat(i)}");
                    Mat(sb, mi.GetSurfaceOverrideMaterial(i), "over");
                    Mat(sb, am.SurfaceGetMaterial(i), "base");
                }
                Mat(sb, mi.MaterialOverride, "mo");
            }
            if (x is AnimationPlayer ap)
                foreach (var name in ap.GetAnimationList()) { var an = ap.GetAnimation(name); sb.Append($" {name} {an.Length} tracks {an.GetTrackCount()}"); }
            sb.Append('\n');
            foreach (var ch in x.GetChildren()) Walk(ch, path + "/" + ch.Name);
        }
        Walk(root, root.Name);
        return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "@(\\w+)@\\d+", "@$1@");   // the engine numbers unnamed nodes per process
    }

    static void Mat(System.Text.StringBuilder sb, Material m, string tag)
    {
        if (m is not ShaderMaterial sm) { sb.Append($" {tag} {m?.GetClass()}"); return; }
        sb.Append($" {tag} {sm.Shader?.ResourcePath}");
        foreach (var p in sm.Shader?.GetShaderUniformList() ?? new Godot.Collections.Array())
        {
            var name = (string)p.AsGodotDictionary()["name"];
            var v = sm.GetShaderParameter(name);
            sb.Append(' ').Append(name).Append('=').Append(v.Obj is Texture2D t ? $"tex{t.GetWidth()}x{t.GetHeight()}" : v.ToString());
        }
    }

    static string FirstDiff(string a, string b)
    {
        var la = a.Split('\n'); var lb = b.Split('\n');
        for (int i = 0; i < Math.Max(la.Length, lb.Length); i++)
        {
            string x = i < la.Length ? la[i] : "<end>", y = i < lb.Length ? lb[i] : "<end>";
            if (x != y) return $"    line {i}: want {x}\n    line {i}: got  {y}";
        }
        return "";
    }
}
