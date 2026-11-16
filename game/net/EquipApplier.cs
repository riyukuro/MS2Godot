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
/// Not done: item colors (palette), decal items (face makeup), skin color, item animations, cape physics.
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

    XElement FindItem(int id)
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

    public void Apply(Node3D visual, ModelData body, int gender, IEnumerable<EquipItem> items, Rgba? skinColor = null)
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
                added.AddRange(Attach(skel, body, md, asset, item));
            }
        }
        foreach (var n in bodyNodes) if (cut.Contains(BaseName(n.Name))) n.Visible = false;
        foreach (var n in added) if (cut.Contains((string)n.GetMeta("mesh"))) n.Visible = false;
        if (skinColor is { } sc)
        {
            // the skin parts (meshes named *_Skin and the ears) are drawn with a grey-white texture and take the player's skin color
            var tint = new Color(sc.R / 255f, sc.G / 255f, sc.B / 255f);
            foreach (var n in bodyNodes.Concat(added))
            {
                string name = n.HasMeta("mesh") ? (string)n.GetMeta("mesh") : n.Name.ToString();
                if (!(name.EndsWith("_Skin", StringComparison.OrdinalIgnoreCase) || name.EndsWith("_EA", StringComparison.OrdinalIgnoreCase))) continue;
                if (n.GetActiveMaterial(0) is StandardMaterial3D sm)
                {
                    var copy = (StandardMaterial3D)sm.Duplicate();
                    copy.AlbedoColor = tint;
                    n.SetSurfaceOverrideMaterial(0, copy);
                }
            }
        }
    }

    static string BaseName(StringName n) => n.ToString();

    List<MeshInstance3D> Attach(Skeleton3D skel, ModelData body, ModelData md, XElement asset, EquipItem item)
    {
        var made = new List<MeshInstance3D>();
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
                // rigid: hangs from the body bone of the target node, at its own place in item space
                string target = (string)asset.Attribute("targetnode") ?? "Bip01 Head";
                string bone = target;
                if (skel.FindBone(bone) < 0)
                {
                    var bodyMesh = body.Meshes.FirstOrDefault(x => x.Name == target && x.ParentBone >= 0);
                    bone = bodyMesh != null ? body.Bones[bodyMesh.ParentBone].Name : "Bip01 Head";
                }
                var att = new BoneAttachment3D { Name = mi.Name + "_attach", BoneName = bone };
                skel.AddChild(att);
                var parentWorld = m.ParentBone >= 0 ? ModelBuilder.ToGodot(md.Bones[m.ParentBone].World) : Transform3D.Identity;
                mi.Transform = parentWorld * ModelBuilder.ToGodot(m.Local);
                att.AddChild(mi);
            }
            made.Add(mi);
        }
        return made;
    }
}
