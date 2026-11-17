using System.Xml.Linq;
using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;

namespace Ms2Godot.Net;

/// <summary>
/// Puts worn items on a body built by ModelBuilder. The mapping comes from the player's own Xml pack:
/// itemmodel/{id / 100000}.xml, ItemModel id -> slots -> asset (a NIF in the Item pack, one per gender for clothes). Rules, from the data:
/// an asset with replace="1" takes the place of the body meshes named like its slot (CL replaces CL_Skin and CL_Bra, FA replaces FA, FA_Skin and
/// FA_EA, HR the body's hair mesh); a cutting/mesh entry hides meshes by name; skinned meshes (clothes, shoes, gloves, face) are bound to the
/// body skeleton by bone name; rigid meshes (hair, caps, weapons, ears) hang from the body bone of the target node, placed by their own
/// transform in item space. A later item in a slot replaces an earlier one (the outfit list follows the gear list in the packet).
/// Colours: an item's dye (its EquipColor, or the item model's default palette row when the record carries none) goes to the
/// dye colours of its meshes; the skin colour pair goes to every mesh drawn with the skin material, the body's and the items'
/// (the client walks the whole character for that material).
/// Not done: decal items (face makeup), item animations, cape physics.
/// </summary>
public sealed class EquipApplier
{
    public const string ItemArchive = "Resource/Model/Item";

    readonly PackSet packs;
    readonly ModelBuilder mb;
    readonly Dictionary<int, Dictionary<int, XElement>> docs = new();
    readonly Dictionary<string, ModelData> nifs = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<ModelMesh, ArrayMesh> meshes = new();
    public int Resolved, NoModelEntry, NoAsset, Loaded, Failed, MissingItemIds;
    public readonly HashSet<int> MissingIds = new();

    public EquipApplier(PackSet packs, ModelBuilder mb)
    {
        this.packs = packs; this.mb = mb;
        if (!mb.TextureArchives.Contains(ItemArchive)) mb.TextureArchives = mb.TextureArchives.Prepend(ItemArchive).ToArray();
    }

    readonly Dictionary<int, Dictionary<int, int>> presets = new();

    /// <summary>
    /// The item model of an item: itemdata/{id / 100000}.xml can name another item's model (tool itemPreset, as the
    /// female copies of the starter outfits do); CItemData loads that model instead.
    /// </summary>
    XElement FindItem(int id)
    {
        int prefix = id / 100000;
        if (!presets.TryGetValue(prefix, out var byItem))
        {
            byItem = new Dictionary<int, int>();
            string entry = $"itemdata/{prefix}.xml";
            if (packs.Exists("Xml", entry))
            {
                var x = XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", entry)).TrimStart('\uFEFF'));
                foreach (var e in x.Root.Elements("item"))
                    if (int.TryParse((string)e.Attribute("id"), out int i)
                        && int.TryParse((string)e.Descendants("tool").FirstOrDefault()?.Attribute("itemPreset"), out int p) && p != 0)
                        byItem[i] = p;
            }
            presets[prefix] = byItem;
        }
        return FindModel(byItem.TryGetValue(id, out int preset) ? preset : id);
    }

    XElement FindModel(int id)
    {
        int prefix = id / 100000;
        if (!docs.TryGetValue(prefix, out var byId))
        {
            byId = new Dictionary<int, XElement>();
            string entry = $"itemmodel/{prefix}.xml";
            if (packs.Exists("Xml", entry))
            {
                var x = XDocument.Parse(System.Text.Encoding.UTF8.GetString(packs.Read("Xml", entry)).TrimStart('﻿'));
                foreach (var e in x.Descendants("ItemModel"))
                    if (int.TryParse((string)e.Attribute("id"), out int i)) byId[i] = e;
            }
            docs[prefix] = byId;
        }
        return byId.GetValueOrDefault(id);
    }

    ModelData Nif(string xmlPath)
    {
        const string prefix = "Data/Resource/Model/Item/";
        string entry = xmlPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? xmlPath[prefix.Length..] : xmlPath;
        entry = entry.ToLowerInvariant();
        if (nifs.TryGetValue(entry, out var md)) return md;
        md = packs.Exists(ItemArchive, entry) ? mb.LoadModel(ItemArchive, entry) : null;
        return nifs[entry] = md;
    }

    ArrayMesh Mesh(ModelMesh m) => meshes.TryGetValue(m, out var am) ? am : meshes[m] = mb.BuildMesh(m);

    /// <summary>The visible item per slot: the last one listed for each slot.</summary>
    public static List<EquipItem> Visible(IEnumerable<EquipItem> items)
    {
        var bySlot = new Dictionary<EquipSlot, EquipItem>();
        foreach (var it in items) bySlot[it.Slot] = it;
        return bySlot.Values.ToList();
    }

    public Look.ColorPalettes Palettes;   // optional: default dye of items whose record has no colour

    public void Apply(Node3D visual, ModelData body, int gender, IEnumerable<EquipItem> items, Rgba? skinColor = null, Rgba? skinShade = null)
    {
        var skel = visual.GetNode<Skeleton3D>("Model/" + ModelBuilder.SkeletonName);
        var bodyNodes = skel.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>().ToList();
        var cut = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var added = new List<MeshInstance3D>();

        foreach (var item in Visible(items))
        {
            if (item.Slot == EquipSlot.SK || item.Slot == EquipSlot.FD) continue;     // skin (the body) and face decals
            var im = FindItem(item.Id);
            if (im == null) { NoModelEntry++; MissingIds.Add(item.Id); continue; }
            foreach (var slot in im.Element("slots")?.Elements("slot") ?? Enumerable.Empty<XElement>())
            {
                var asset = slot.Elements("asset").FirstOrDefault(a => (string)a.Attribute("gender") == null || (string)a.Attribute("gender") == gender.ToString());
                if (asset == null) { NoAsset++; continue; }
                ModelData md;
                try { md = Nif((string)asset.Attribute("name")); }
                catch (Exception ex) { Failed++; GD.PushWarning($"item {item.Id}: {ex.Message}"); continue; }
                if (md == null) { NoAsset++; MissingIds.Add(item.Id); continue; }
                Loaded++;
                string slotName = (string)slot.Attribute("name");
                if ((string)asset.Attribute("replace") == "1")
                    foreach (var n in bodyNodes)
                        if (n.Name.ToString().StartsWith(slotName, StringComparison.OrdinalIgnoreCase) && (n.Name.ToString().Length == slotName.Length || n.Name.ToString()[slotName.Length] == '_'))
                            n.Visible = false;
                foreach (var c in im.Element("cutting")?.Elements("mesh") ?? Enumerable.Empty<XElement>())
                {
                    var g = (string)c.Attribute("gender");
                    if (g == null || g == gender.ToString()) cut.Add((string)c.Attribute("name"));
                }
                var made = Attach(skel, body, md, asset, item, gender);
                if (DyeOf(item, im) is { } dye)
                    foreach (var n in made) SetColors(n, false, dye.Primary, dye.Secondary, dye.Tertiary);
                added.AddRange(made);
            }
        }
        foreach (var n in bodyNodes) if (cut.Contains(BaseName(n.Name))) n.Visible = false;
        foreach (var n in added) if (cut.Contains((string)n.GetMeta("mesh"))) n.Visible = false;
        if (skinColor is { } sc)
            foreach (var n in bodyNodes.Concat(added))
                SetColors(n, true, sc, null, skinShade ?? sc);
    }

    /// <summary>The record's dye, else the item model's default (customize colorPalette / defaultColorIndex).</summary>
    EquipColor? DyeOf(EquipItem item, XElement im)
    {
        var c = item.Color;
        if (c.Primary.A != 0 || c.Secondary.A != 0 || c.Tertiary.A != 0) return c;
        var cust = im.Element("customize");
        if (Palettes == null || cust == null || (string)cust.Attribute("color") != "1") return null;
        if (!int.TryParse((string)cust.Attribute("colorPalette"), out int pal)) return null;
        int.TryParse((string)cust.Attribute("defaultColorIndex"), out int idx);
        return Palettes.Equip(pal, idx);
    }

    static Vector3 V(Rgba c) => new(c.R / 255f, c.G / 255f, c.B / 255f);

    /// <summary>Sets the dye colours of a mesh drawn with the character shader: skin=true for the skin material only, false for the rest.</summary>
    static void SetColors(MeshInstance3D n, bool skin, Rgba? c0, Rgba? c1, Rgba? c2)
    {
        if (n.GetActiveMaterial(0) is not ShaderMaterial sm || sm.GetShaderParameter(ShaderParam.Skin).AsBool() != skin) return;
        var copy = (ShaderMaterial)sm.Duplicate();
        if (c0 is { } a) copy.SetShaderParameter(ShaderParam.Color0, V(a));
        if (c1 is { } b) copy.SetShaderParameter(ShaderParam.Color1, V(b));
        if (c2 is { } c) copy.SetShaderParameter(ShaderParam.Color2, V(c));
        n.SetSurfaceOverrideMaterial(0, copy);
    }

    static string BaseName(StringName n) => n.ToString();

    /// <summary>The asset's dummy offset for a gender (gender 2 = both), the first one with no animation sequence.</summary>
    static Transform3D DummyOf(XElement asset, int gender)
    {
        foreach (var d in asset.Elements("dummy"))
        {
            var g = (string)d.Attribute("gender");
            if (g != null && g != "2" && g != gender.ToString()) continue;
            if (!string.IsNullOrEmpty((string)d.Attribute("sequence"))) continue;
            var node = (string)d.Attribute("targetnode");
            if (!string.IsNullOrEmpty(node) && node != (string)asset.Attribute("targetnode")) continue;    // for another node (held)
            var t = Floats((string)d.Attribute("translation"));
            var r = Floats((string)d.Attribute("rotation"));
            const double d2r = Math.PI / 180;
            var q = Quat.FromAxis(0, r[0] * d2r) * Quat.FromAxis(1, r[1] * d2r) * Quat.FromAxis(2, r[2] * d2r);
            return ModelBuilder.ToGodot(new Xform(new Vec3(t[0], t[1], t[2]), q.ToMatrix(), 1));
        }
        return Transform3D.Identity;
    }

    static double[] Floats(string s)
    {
        var v = new double[3];
        var parts = (s ?? "").Split(',');
        for (int i = 0; i < 3 && i < parts.Length; i++)
            double.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[i]);
        return v;
    }

    List<MeshInstance3D> Attach(Skeleton3D skel, ModelData body, ModelData md, XElement asset, EquipItem item, int gender)
    {
        var made = new List<MeshInstance3D>();
        // Rigid items, as the client attaches them: the item's self nodes are every node whose name starts with the
        // asset's selfnode, the outermost on each path (CP, CPA; HR0 and HR1 for "HR"; a node named exactly selfnode
        // too): FUN_141422110, itemugcmesh.cpp:295-320 (GetObjectByName, then a breadth-first walk that stops at the
        // first prefix match). Each one hangs under an attach node on the target node ("Equip_Attach_Transform" + part
        // name, CreateAttachNode, equipmodel.cpp:1165) and keeps its own local transform (CNifModel::vfunc_12,
        // nifmodel.cpp:232-249), so a node that carries the head-relative offset (CP, HR0) keeps it. The attach node is
        // turned and moved by the asset's dummy for the gender (Euler XYZ degrees, then a translation); dummies tied to an
        // animation sequence are left out (observed, not cited).
        // A replace item whose target is not a body node (hair on "HR") hangs on Bip01 Head instead. Observed rule, not
        // the client's: CNifModel::vfunc_13 (nifmodel.cpp:260-283) attaches the self nodes of any replace="1" asset to
        // Bip01 Head (to the NIF root when the asset's ani is set) when CEquipment::FUN_1411e99a0 allows it; it does not
        // test whether the target is a body node.
        string selfName = (string)asset.Attribute("selfnode");
        bool replace = (string)asset.Attribute("replace") == "1";
        Transform3D? selfInv = null;
        if (selfName != null)
        {
            int sb = md.Bones.FindIndex(b => b.Name == selfName);
            var sm = md.Meshes.FirstOrDefault(x => x.Name == selfName && !x.Skinned);
            if (sb >= 0) selfInv = ModelBuilder.ToGodot(md.Bones[sb].World).AffineInverse();
            else if (sm != null)
                selfInv = ((sm.ParentBone >= 0 ? ModelBuilder.ToGodot(md.Bones[sm.ParentBone].World) : Transform3D.Identity) * ModelBuilder.ToGodot(sm.Local)).AffineInverse();
        }
        var dummy = DummyOf(asset, gender);
        Skin skin = null;
        foreach (var m in md.Meshes)
        {
            var mi = new MeshInstance3D { Name = $"eq{item.Id}_{m.Name}", Mesh = Mesh(m) };
            mi.SetMeta("mesh", m.Name);
            if (m.Skinned)
            {
                if (skin == null)
                {
                    skin = new Skin();
                    foreach (var b in md.Bones)
                    {
                        int bi = skel.FindBone(b.Name);
                        skin.AddBind(bi >= 0 ? bi : 0, ModelBuilder.ToGodot(b.World).AffineInverse());
                    }
                }
                skel.AddChild(mi);
                mi.Skin = skin; mi.Skeleton = "..";
            }
            else
            {
                // rigid: the item's self node sits on the body's target node (a bone, or a rigid body mesh under a bone)
                string target = (string)asset.Attribute("targetnode") ?? "Bip01 Head";
                string bone = target;
                var targetLocal = Transform3D.Identity;     // the target node relative to its bone
                var rel = SelfRelative(md, m, selfName);
                if (skel.FindBone(bone) < 0 && replace && rel != null)
                    bone = "Bip01 Head";
                else if (skel.FindBone(bone) < 0)
                {
                    var bodyMesh = body.Meshes.FirstOrDefault(x => x.Name == target && x.ParentBone >= 0);
                    bone = bodyMesh != null ? body.Bones[bodyMesh.ParentBone].Name : "Bip01 Head";
                    if (bodyMesh != null) targetLocal = ModelBuilder.ToGodot(bodyMesh.Local);
                }
                var att = new BoneAttachment3D { Name = mi.Name + "_attach", BoneName = bone };
                skel.AddChild(att);
                var parentWorld = m.ParentBone >= 0 ? ModelBuilder.ToGodot(md.Bones[m.ParentBone].World) : Transform3D.Identity;
                // a mesh outside the self nodes keeps the earlier placement: on the self node's frame, or on its own parent
                // node when the declared self node is not in the item; no selfnode keeps identity
                var selfFallback = selfName != null ? parentWorld.AffineInverse() : Transform3D.Identity;
                mi.Transform = rel is { } r ? targetLocal * dummy * r
                    : targetLocal * dummy * (selfInv ?? selfFallback) * parentWorld * ModelBuilder.ToGodot(m.Local);
                att.AddChild(mi);
            }
            made.Add(mi);
        }
        return made;
    }

    /// <summary>
    /// A rigid mesh relative to its self node's attach point (the client's self-node collection): the outermost node on
    /// the mesh's path, the mesh included, whose name starts with selfName keeps its local transform and carries the
    /// rest. Null when no node on the path matches.
    /// </summary>
    static Transform3D? SelfRelative(ModelData md, ModelMesh m, string selfName)
    {
        if (string.IsNullOrEmpty(selfName)) return null;
        var local = ModelBuilder.ToGodot(m.Local);
        var parentWorld = m.ParentBone >= 0 ? ModelBuilder.ToGodot(md.Bones[m.ParentBone].World) : Transform3D.Identity;
        if (md.RootName == selfName) return ModelBuilder.ToGodot(md.RootLocal) * parentWorld * local;
        int top = -1;
        for (int b = m.ParentBone; b >= 0; b = md.Bones[b].Parent)
            if (md.Bones[b].Name != null && md.Bones[b].Name.StartsWith(selfName, StringComparison.Ordinal)) top = b;
        if (top >= 0)
            return ModelBuilder.ToGodot(md.Bones[top].Rest) * ModelBuilder.ToGodot(md.Bones[top].World).AffineInverse() * parentWorld * local;
        if (m.Name != null && m.Name.StartsWith(selfName, StringComparison.Ordinal)) return local;
        return null;
    }
}
