using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Net.Packets;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless census (res://tests/cap_census.tscn) of EquipApplier's cap placement, CEquipment::FUN_1411e5c10
/// (@0x1411e5c10, equipment.cpp:2794-2917), over the whole list: every cap whose item model has capAttach
/// (customize@capAttach, GMS2 customize/CP@attach; itemmodel 113) worn over every hair (itemmodel 102), on each body whose
/// gender both items fit (itemdata limit genderLimit). For each pair:
/// <list type="bullet">
/// <item>the cap is placed (EquipApplier.CapOf) and its roots are found (GetRoots, equipmodel.cpp:906: a nif model's self
/// nodes, a kfm model's self bones and its skinned self mesh, the CP of most kfm caps);</item>
/// <item>the hair has meshes to pick;</item>
/// <item>every root group either hit the hair or fell back to the hair's capTransform;</item>
/// <item>the cap's centre is inside the hair's bounds grown by half the cap's size (positions in the skeleton's space, a
/// frame after the change, so the cap's own idle animation has run);</item>
/// <item>a kfm cap's skinned self mesh is drawn where the client's skinning puts it, worked out here from the file's data
/// apart from the root math: the self node's world (attach node * Prefix * the placed local, CapPlacement.SkinnedSelf) *
/// the skin's Skeleton Transform * each bone's pose relative to the scene root * the skin's bind
/// (NiSkinningMeshModifier.cpp:891-908, NiCalculateBoneMatricesKernel.cpp:43-50), the bounds within 0.1 of the drawn ones.</item>
/// </list>
/// Left out, and printed: a cap item whose row redirects (tool itemPreset) to a model without capAttach, which FUN_1411e5c10
/// never places; a hair or cap whose model file is not in the data (EquipApplier.MissingIds); and the caps in
/// <see cref="Known"/>, with the reason.
/// Needs MS2_DATA and MS2_NIFXML (or local.cfg). Prints the counts and the failures grouped by cap. Quits with 0 when every
/// pair holds, else 1.
/// </summary>
public partial class CapCensusCheck : Node3D
{
    public override async void _Ready()
    {
        var fails = new List<string>();
        try { await Run(fails); }
        catch (Exception e) { fails.Add(e.ToString()); }
        GD.Print($"cap census: {(fails.Count == 0 ? "ok" : $"FAIL ({fails.Count})\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }

    /// <summary>Caps this census does not hold to, each with why. STILL-OPEN: 11300506 (KMS) is one mesh skinned on the cap's
    /// own bones (Bone06, Bone07, CP), none of them the body's; the skin swap (FUN_141421f30, itemugcmesh.cpp:271-290) keeps
    /// such bones as the item's nodes under its Scene Root self node, which the cap placement moves, but the port binds them
    /// to the body's bone 0, so the cap has no root to move.</summary>
    static readonly Dictionary<int, string> Known = new()
    {
        [11300506] = "skinned on its own bones (Scene Root self node): the skin keeps no item nodes",
    };

    static XDocument Models(PackSet packs, int prefix) =>
        packs.Exists("Xml", $"itemmodel/{prefix}.xml") ? PackXml.Parse(packs.Read("Xml", $"itemmodel/{prefix}.xml"))
            : Ms2.Formats.World.PerFileTable.ItemModels(packs, prefix);

    async System.Threading.Tasks.Task Run(List<string> fails)
    {
        var started = DateTime.UtcNow;
        var packs = PackSet.Shared(Settings.DataDir);
        var builder = new ModelBuilder(ModelBuilder.SharedSpec(Settings.NifXml), packs);
        var files = new Ms2.Ui.Items.ItemFiles(packs);
        int GenderOf(int id)
        {
            var e = files.Item(id);
            var limit = e?.Element("environment")?.Element("limit") ?? e?.Descendants("limit").FirstOrDefault();
            return int.TryParse((string)limit?.Attribute("genderLimit"), out int g) ? g : 2;
        }
        static bool CapAttach(XElement c) => (string)c?.Attribute("capAttach") == "1" || (string)c?.Element("CP")?.Attribute("attach") == "1";
        var hairs = (Models(packs, 102)?.Descendants("ItemModel") ?? Enumerable.Empty<XElement>()).Select(e => (int)e.Attribute("id")).Distinct().OrderBy(i => i).ToList();
        var caps = (Models(packs, 113)?.Descendants("ItemModel") ?? Enumerable.Empty<XElement>())
            .Where(e => CapAttach(e.Element("customize"))).Select(e => (int)e.Attribute("id")).Distinct().OrderBy(i => i).ToList();
        GD.Print($"cap census: {caps.Count} capAttach caps, {hairs.Count} hairs");
        var probe = new EquipApplier(packs, builder);
        var redirected = caps.Where(c => (int?)probe.WornModel(c)?.Attribute("id") != c && !CapAttach(probe.WornModel(c)?.Element("customize"))).ToList();
        foreach (var (c, why) in Known) if (caps.Contains(c)) GD.Print($"cap census: cap {c} left out: {why}");
        GD.Print($"cap census: {redirected.Count} caps wear a model without capAttach (itemPreset): {string.Join(",", redirected)}");
        caps = caps.Except(redirected).Where(c => !Known.ContainsKey(c)).ToList();
        var missing = new SortedSet<int>();
        if (caps.Count == 0) fails.Add("no capAttach cap in itemmodel 113");
        if (hairs.Count == 0) fails.Add("no hair in itemmodel 102");

        var byCap = new SortedDictionary<int, List<string>>();
        void Fail(int cap, string what) { if (!byCap.TryGetValue(cap, out var l)) byCap[cap] = l = new(); l.Add(what); }
        int pairs = 0, hits = 0, fallbacks = 0, skinned = 0;
        var palettes = new Look.ColorPalettes(packs);
        foreach (int gender in new[] { 0, 1 })
        {
            string dir = gender == 0 ? "male" : "female";
            var md = builder.LoadModel($"{dir}/{(gender == 0 ? "m_body.nif" : "f_body.nif")}");
            var v = builder.Build(md, dir);
            AddChild(v);
            var (items, _) = Look.StarterLook.Build(packs, palettes, gender, 10, new Dictionary<string, int>(), 0);
            for (int i = 0; i < items.Count; i++) if (items[i].Uid == 0) items[i].Uid = 2001 + i;
            var start = items.Where(i => i.Slot is not (EquipSlot.CP or EquipSlot.HR)).ToList();
            var equip = new EquipApplier(packs, builder);
            equip.Apply(v, md, gender, start);
            var skel = v.GetNode<Skeleton3D>("Model/" + ModelBuilder.SkeletonName);
            var gHairs = hairs.Where(h => GenderOf(h) is var g && (g == 2 || g == gender)).ToList();
            var gCaps = caps.Where(c => GenderOf(c) is var g && (g == 2 || g == gender)).ToList();
            GD.Print($"cap census {dir}: {gCaps.Count} caps x {gHairs.Count} hairs");
            foreach (int h in gHairs)
            {
                var hair = new EquipItem { Id = h, Uid = 1001, Slot = EquipSlot.HR };
                var worn = start.Append(hair).ToList();
                equip.Reapply(v, worn);
                await Frame();
                if (equip.MissingIds.Contains(h)) { missing.Add(h); continue; }
                var hairBox = Bounds(skel, Meshes(skel, h));
                if (hairBox == null) { Fail(0, $"{dir} hair {h}: no hair vertices under the skeleton"); continue; }
                foreach (int c in gCaps)
                {
                    pairs++;
                    var cap = new EquipItem { Id = c, Uid = 9001, Slot = EquipSlot.CP };
                    equip.Reapply(v, worn.Append(cap));
                    await Frame();
                    string at = $"{dir} hair {h}";
                    if (equip.MissingIds.Contains(c)) { missing.Add(c); continue; }
                    var o = equip.CapOf(v);
                    if (o == null) { Fail(c, $"{at}: not placed"); continue; }
                    hits += o.Hits; fallbacks += o.Fallbacks;
                    if (o.Roots == 0) { Fail(c, $"{at}: no roots"); continue; }
                    if (o.HairEmpty) Fail(c, $"{at}: the hair has no mesh to pick");
                    if (o.Hits + o.Fallbacks != o.Roots) Fail(c, $"{at}: {o.Roots} roots, {o.Hits} hits, {o.Fallbacks} fallbacks");
                    var capBox = Bounds(skel, Meshes(skel, c));
                    if (capBox == null) { Fail(c, $"{at}: no cap vertices under the skeleton"); continue; }
                    var centre = capBox.Value.GetCenter();
                    if (!hairBox.Value.Grow(capBox.Value.Size.Length() / 2).HasPoint(centre))
                        Fail(c, $"{at}: centre {centre} off the hair {hairBox.Value.Position}..{hairBox.Value.End}");
                    foreach (var (selfWorld, mesh, drawn) in o.SkinnedSelf)
                    {
                        skinned++;
                        var want = drawn == null ? null : ClientSkin(selfWorld, mesh, drawn.GetNodeOrNull<Skeleton3D>(drawn.Skeleton));
                        var got = drawn == null ? null : Bounds(skel, new List<MeshInstance3D> { drawn });
                        if (want == null || got == null) { Fail(c, $"{at}: self mesh {mesh.Name} not drawn or not skinned"); continue; }
                        if (want.Value.Position.DistanceTo(got.Value.Position) > 0.1f || want.Value.End.DistanceTo(got.Value.End) > 0.1f)
                            Fail(c, $"{at}: self mesh {mesh.Name} drawn at {got.Value.Position}..{got.Value.End}, the client's skin {want.Value.Position}..{want.Value.End}");
                    }
                }
                equip.Reapply(v, worn);
            }
            v.QueueFree();
            await Frame();
        }
        foreach (var (c, l) in byCap)
            fails.Add($"cap {c}: {l.Count} pairs, e.g. {string.Join("; ", l.Take(3))}");
        GD.Print($"cap census: {pairs} pairs, {hits} root hits, {fallbacks} capTransform fallbacks, {skinned} skinned self meshes against the client's skin, {byCap.Count} caps failing, {(DateTime.UtcNow - started).TotalSeconds:F0} s");
        GD.Print($"cap census: {missing.Count} items with no model file in the data: {string.Join(",", missing)}");
        if (pairs == 0) fails.Add("no cap x hair pair");
    }

    /// <summary>The client's skin of a self mesh, in the character skeleton's space: vertex = selfWorld * Skeleton Transform *
    /// sum w_j pose_j * bind_j * p, pose_j the bone's pose relative to the model's scene root (the kfm skeleton's space, its
    /// idle animation applied) and bind_j the skin's scene root to bone at bind (ModelMesh.SkinBinds; p is in scene root
    /// space). A joint without a bind is left out; null when no vertex has one.</summary>
    static Aabb? ClientSkin(Transform3D selfWorld, ModelMesh m, Skeleton3D bones)
    {
        if (bones == null || m.Joints == null || m.SkinBinds == null) return null;
        var frame = selfWorld * ModelBuilder.ToGodot(m.SkinTransform);
        var mats = new Transform3D?[bones.GetBoneCount()];
        Aabb? box = null;
        for (int v = 0; v * 3 + 2 < m.Positions.Length; v++)
        {
            var p = new Vector3(m.Positions[v * 3], m.Positions[v * 3 + 1], m.Positions[v * 3 + 2]);
            var q = Vector3.Zero; float used = 0;
            for (int k = 0; k < 4; k++)
            {
                float w = m.Weights[v * 4 + k]; int j = m.Joints[v * 4 + k];
                if (w == 0 || j < 0 || j >= mats.Length || !m.SkinBinds.TryGetValue(j, out var bind)) continue;
                mats[j] ??= bones.GetBoneGlobalPose(j) * ModelBuilder.ToGodot(bind);
                q += mats[j].Value * p * w; used += w;
            }
            if (used == 0) continue;
            var at = frame * (q / used);
            box = box is { } b ? b.Expand(at) : new Aabb(at, Vector3.Zero);
        }
        return box;
    }

    static List<MeshInstance3D> Meshes(Skeleton3D skel, int id) =>
        skel.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>()
            .Where(m => !m.IsQueuedForDeletion() && m.Visible && CapPlaceCheck.Under(m, skel, $"eq{id}_")).ToList();

    static Aabb? Bounds(Skeleton3D skel, List<MeshInstance3D> meshes) => CapPlaceCheck.Bounds(skel, meshes);

    async System.Threading.Tasks.Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
}
