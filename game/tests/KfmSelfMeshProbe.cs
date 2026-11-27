using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless probe (res://tests/kfm_self_mesh_probe.tscn): every kfm asset of every item model (all slots, pony parts
/// included, each gender it fits) whose self node is a skinned mesh and not a bone, the case EquipApplier.AttachKfm now
/// hangs at the attach node * targetLocal * dummy * (hair data or the mesh's local) * Skeleton Transform
/// (CKfmModel::vfunc_12, kfmmodel.cpp:533-537; NiSkinningMeshModifier.cpp:891-908), where it was the model's root local.
/// For each it prints the old and new skeleton origin in attach space and how far the mesh's bind-pose vertices move (the
/// positions are in scene root space, so a vertex draws at the skeleton frame times it). Prints only; quits with 0.
/// </summary>
public partial class KfmSelfMeshProbe : Node
{
    static XDocument Models(PackSet packs, int prefix) =>
        packs.Exists("Xml", $"itemmodel/{prefix}.xml") ? PackXml.Parse(packs.Read("Xml", $"itemmodel/{prefix}.xml"))
            : Ms2.Formats.World.PerFileTable.ItemModels(packs, prefix);

    static double[] Floats(string s)
    {
        var v = new double[3];
        var parts = (s ?? "").Split(',');
        for (int i = 0; i < 3 && i < parts.Length; i++)
            double.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[i]);
        return v;
    }

    static string Shown(Transform3D t) => t.IsEqualApprox(Transform3D.Identity) ? "identity" : t.ToString();

    public override void _Ready()
    {
        try { Run(); }
        catch (Exception e) { GD.PrintErr("kfm probe: " + e); }
        GetTree().Quit(0);
    }

    void Run()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var bodies = new[] { builder.LoadModel("male/m_body.nif"), builder.LoadModel("female/f_body.nif") };
        var nifs = new Dictionary<string, ModelData>();
        int assets = 0, kfms = 0, selfMeshes = 0, moved = 0;
        var seen = new HashSet<string>();
        for (int prefix = 100; prefix < 1000; prefix++)
        {
            var doc = Models(packs, prefix);
            if (doc == null) continue;
            foreach (var im in doc.Descendants("ItemModel"))
            {
                int id = (int)im.Attribute("id");
                foreach (var slot in im.Element("slots")?.Elements("slot") ?? Enumerable.Empty<XElement>())
                    foreach (int gender in new[] { 0, 1 })
                    {
                        var list = slot.Elements("asset").Where(a => AssetGender.Matches((string)a.Attribute("gender"), gender)).ToList();
                        var main = list.FirstOrDefault();
                        int ponyIndex = 0;
                        foreach (var asset in list)
                        {
                            bool pony = asset != main;
                            if (pony && (string)asset.Attribute("pony") != "1") continue;
                            int index = pony ? ponyIndex++ : -1;
                            assets++;
                            string name = ((string)asset.Attribute("name") ?? "").TrimEnd();
                            if (name.EndsWith(".nif", StringComparison.Ordinal)) continue;
                            name = name.Replace("urn:gamebryo-animation:", "").Replace("urn:", "");
                            string kfmEntry = packs.FindByFileName(EquipApplier.ItemArchive, name.ToLowerInvariant() + ".kfm");
                            if (kfmEntry == null) continue;
                            kfms++;
                            if (!nifs.TryGetValue(kfmEntry, out var md))
                            {
                                try
                                {
                                    var kfm = KfmFile.Load(packs.Read(EquipApplier.ItemArchive, kfmEntry));
                                    string nif = kfmEntry[..(kfmEntry.LastIndexOf('/') + 1)] + System.IO.Path.GetFileName(kfm.Nif.Replace('\\', '/')).ToLowerInvariant();
                                    md = packs.Exists(EquipApplier.ItemArchive, nif) ? builder.LoadModel(EquipApplier.ItemArchive, nif) : null;
                                }
                                catch (Exception) { md = null; }
                                nifs[kfmEntry] = md;
                            }
                            if (md == null) continue;
                            string selfName = (string)asset.Attribute("selfnode");
                            if (selfName == null) continue;
                            bool Self(int b) => md.Bones[b].Name != null && md.Bones[b].Name.StartsWith(selfName, StringComparison.Ordinal);
                            if (Enumerable.Range(0, md.Bones.Count).Any(Self)) continue;
                            var mesh = md.Meshes.FirstOrDefault(m => m.Skinned && m.FileName != null && m.FileName.StartsWith(selfName, StringComparison.Ordinal));
                            if (mesh == null) continue;
                            selfMeshes++;
                            // the Prefix AttachKfm builds: targetLocal (a target that is not a body bone) times the dummy
                            string target = (string)asset.Attribute("targetnode") ?? "Bip01 Head";
                            var targetLocal = Transform3D.Identity;
                            if (bodies[gender].BoneIndex(target) < 0)
                            {
                                var (_, kl) = bodies[gender].FindBodyAttach(target, (string)asset.Attribute("replace") == "1");
                                if (kl != null) targetLocal = ModelBuilder.ToGodot(kl.Value);
                            }
                            var dummy = Transform3D.Identity;
                            Transform3D? xf = null;
                            if (!pony)
                            {
                                var parsed = Ms2.Ui.Items.ItemModelTable.ParseAsset(0, "", asset);
                                var (t, r) = parsed.DummyFor(gender, parsed.TargetNode, "");
                                dummy = ModelBuilder.ToGodot(new Xform(new Vec3(t.X, t.Y, t.Z), DummyWorld.EulerXyz(r.X, r.Y, r.Z), 1));
                            }
                            else
                            {
                                var custom = asset.Element("custom");
                                var pos = Floats((string)custom?.Attribute("position"));
                                var rot = Floats((string)custom?.Attribute("rotation"));
                                var sc = asset.Parent?.Element("scale");
                                double scale = double.TryParse((string)sc?.Attribute("min"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double mn) ? mn : 1;
                                xf = ModelBuilder.ToGodot(new Xform(new Vec3(pos[0], pos[1], pos[2]), DummyWorld.EulerXyz(rot[0], rot[1], rot[2]), scale));
                            }
                            var oldFrame = ModelBuilder.ToGodot(md.RootLocal);
                            var newFrame = targetLocal * dummy * (xf ?? ModelBuilder.ToGodot(mesh.SkinMeshLocal)) * ModelBuilder.ToGodot(mesh.SkinTransform);
                            float most = 0;
                            var p = mesh.Positions;
                            for (int i = 0; i + 2 < p.Length; i += 3)
                            {
                                var v = new Vector3(p[i], p[i + 1], p[i + 2]);
                                most = Math.Max(most, (newFrame * v).DistanceTo(oldFrame * v));
                            }
                            float d = newFrame.Origin.DistanceTo(oldFrame.Origin);
                            float reach = 0;
                            for (int i = 0; i + 2 < p.Length; i += 3) reach = Math.Max(reach, (oldFrame * new Vector3(p[i], p[i + 1], p[i + 2])).Length());
                            if (d > 0.5f || most > 0.5f) moved++;
                            string key = $"{id} {(string)slot.Attribute("name")} {(pony ? $"pony{index}" : "main")} {name} g{gender}";
                            if (!seen.Add(key)) continue;
                            GD.Print($"kfm probe: {key} self {selfName} mesh {mesh.FileName} target {target}: old {oldFrame.Origin} new {newFrame.Origin} origin moves {d:F3}, vertices up to {most:F3}; S {Shown(ModelBuilder.ToGodot(mesh.SkinTransform))}, L {Shown(ModelBuilder.ToGodot(mesh.SkinMeshLocal))}, prefix {Shown(targetLocal * dummy)}{(d > 0.5f || most > 0.5f ? $"  MOVED (mesh reach {reach:F1}; old basis {oldFrame.Basis}, new basis {newFrame.Basis}; L {mesh.SkinMeshLocal.T} s {mesh.SkinMeshLocal.S}, S {mesh.SkinTransform.T} s {mesh.SkinTransform.S}, root {md.RootLocal.T} s {md.RootLocal.S})" : "")}");
                        }
                    }
            }
        }
        GD.Print($"kfm probe: {assets} assets, {kfms} kfm, {selfMeshes} with a skinned self mesh and no self bone, {moved} move more than 0.5");
    }
}
