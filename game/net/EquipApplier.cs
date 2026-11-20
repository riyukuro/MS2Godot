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
    readonly Dictionary<string, ModelData> nifs = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<ModelMesh, ArrayMesh> meshes = new();
    public int Resolved, NoModelEntry, NoAsset, Loaded, Failed, MissingItemIds, NotAttached;
    public readonly HashSet<int> MissingIds = new();

    public EquipApplier(PackSet packs, ModelBuilder mb)
    {
        this.packs = packs; this.mb = mb;
        shared = tables.GetValue(packs, _ => new ItemTables());
        if (!mb.TextureArchives.Contains(ItemArchive)) mb.TextureArchives = mb.TextureArchives.Prepend(ItemArchive).ToArray();
    }

    /// <summary>
    /// The item tables read from the pack set, kept for the process (main thread): every character build (the field's
    /// players, the HUD portrait, each open of a dialog's avatar) makes its own EquipApplier, and reading itemdata and
    /// itemmodel per applier parsed the same files again per build.
    /// </summary>
    sealed class ItemTables
    {
        public readonly Dictionary<int, Dictionary<int, XElement>> Models = new();     // itemmodel/{prefix}.xml: ItemModel by id
        public readonly Dictionary<int, Dictionary<int, int>> Presets = new();         // itemdata/{prefix}.xml: item -> tool itemPreset
        public readonly Dictionary<int, string> CharacterIdle = new();                 // item -> its <effect characterIdle>
    }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PackSet, ItemTables> tables = new();
    readonly ItemTables shared;
    Dictionary<int, Dictionary<int, XElement>> docs => shared.Models;
    Dictionary<int, Dictionary<int, int>> presets => shared.Presets;
    Dictionary<int, string> characterIdle => shared.CharacterIdle;
    readonly HashSet<string> itemNodes = new(StringComparer.Ordinal);    // node names of the models hung on the character
    Ms2.Formats.World.EffectTable effects;
    public int IdleEffects, IdleEffectsOpen;

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
                {
                    if (!int.TryParse((string)e.Attribute("id"), out int i)) continue;
                    if (int.TryParse((string)e.Descendants("tool").FirstOrDefault()?.Attribute("itemPreset"), out int p) && p != 0)
                        byItem[i] = p;
                    // ItemEffect::Load (itemdatastruct.cpp:1609-1625): <effect characterIdle> of the item itself
                    if ((string)e.Descendants("effect").FirstOrDefault()?.Attribute("characterIdle") is { Length: > 0 } fx)
                        characterIdle[i] = fx;
                }
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

    ArrayMesh Mesh(ModelMesh m) => meshes.TryGetValue(m, out var am) ? am : meshes[m] = mb.SharedMesh(m);

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
        itemNodes.Clear();

        foreach (var item in Visible(items))
        {
            if (item.Slot == EquipSlot.SK || item.Slot == EquipSlot.FD) continue;     // skin (the body) and face decals
            var im = FindItem(item.Id);
            if (im == null) { NoModelEntry++; MissingIds.Add(item.Id); continue; }
            foreach (var slot in im.Element("slots")?.Elements("slot") ?? Enumerable.Empty<XElement>())
            {
                var asset = slot.Elements("asset").FirstOrDefault(a => (string)a.Attribute("gender") == null || (string)a.Attribute("gender") == gender.ToString());
                if (asset == null) { NoAsset++; continue; }
                // ModelPart::Load (itemdatastruct.cpp:287-303): a name not ending in ".nif" is a kfm asset, a CKfmModel
                bool isNif = ((string)asset.Attribute("name") ?? "").TrimEnd().EndsWith(".nif", StringComparison.Ordinal);
                ModelData md = null;
                if (isNif)
                {
                    try { md = Nif((string)asset.Attribute("name")); }
                    catch (Exception ex) { Failed++; GD.PushWarning($"item {item.Id}: {ex.Message}"); continue; }
                    if (md == null) { NoAsset++; MissingIds.Add(item.Id); continue; }
                    Loaded++;
                }
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
                // FUN_141200ca0 (equipmodel.cpp:714-720) hangs a replace="1" part through vfunc_13 and every other part through
                // vfunc_12, which returns before CreateAttachNode when the part name is empty (CNifModel, nifmodel.cpp:232-238;
                // CKfmModel, kfmmodel.cpp:518-524). The part name is the asset's targetnode (CEquipModel::GetPartName,
                // equipmodel.cpp:1084-1101, ModelPart +0x10). Such a model loads, but nothing of it is hung on the character.
                // The 10 CP assets with no targetnode and no selfnode (11301083 penguin, 11300895 Angel Pinkbean, ...) show
                // their creature through the item's characterIdle effect instead (itemdata <effect characterIdle>).
                if (string.IsNullOrEmpty((string)asset.Attribute("targetnode")) && (string)asset.Attribute("replace") != "1") NotAttached++;
                else
                {
                    var made = isNif ? Attach(skel, body, md, asset, item, gender) : AttachKfm(skel, body, asset, item, gender, -1);
                    if (made == null) { MissingIds.Add(item.Id); continue; }
                    if (md != null) NoteNodes(md);
                    if (DyeOf(item, im) is { } dye)
                        foreach (var n in made) SetColors(n, false, dye.Primary, dye.Secondary, dye.Tertiary);
                    added.AddRange(made);
                }
                int ponyIndex = 0;
                foreach (var pony in slot.Elements("asset").Where(a => a != asset && (string)a.Attribute("pony") == "1"
                             && ((string)a.Attribute("gender") == null || (string)a.Attribute("gender") == gender.ToString())))
                {
                    var p = AttachKfm(skel, body, pony, item, gender, ponyIndex++);
                    if (p == null) continue;
                    if (DyeOf(item, im) is { } pd)
                        foreach (var n in p) SetColors(n, false, pd.Primary, pd.Secondary, pd.Tertiary);
                    added.AddRange(p);
                }
            }
        }
        foreach (var n in bodyNodes) if (cut.Contains(BaseName(n.Name))) n.Visible = false;
        foreach (var n in added) if (cut.Contains((string)n.GetMeta("mesh"))) n.Visible = false;
        if (skinColor is { } sc)
            foreach (var n in bodyNodes.Concat(added))
                SetColors(n, true, sc, null, skinShade ?? sc);
        PlayIdleEffects(visual, skel, body, items);
    }

    void NoteNodes(ModelData md)
    {
        foreach (var b in md.Bones) itemNodes.Add(b.FileName ?? b.Name);
        foreach (var m in md.Meshes) itemNodes.Add(m.FileName ?? m.Name);
    }

    /// <summary>
    /// The items' character idle effects. FUN_141202bd0 (equipmodel.cpp:1113-1131) gives each equip model two effect slots:
    /// - slot 0: the item's idle effect on the model;
    /// - slot 1: the item's characterIdle effect (characterBattleIdle in battle stance).
    /// It runs after the model loads (FUN_141200ca0, equipmodel.cpp:730). Slot 1 is started with no node
    /// (FUN_141203570 / FUN_141203660, equipmodel.cpp:1241-1280), so NoticeMessage::Bind hangs it on the owner's own scene
    /// graph (effectservice.cpp:288). A nif part with attachToNode="true" (attach type 1) goes to AttachToModelNode
    /// (nifeffectparam.cpp:1079-1112): GetObjectByName(parentNode) over that graph, the graph root when there is none.
    /// FUN_14154c580 (1158-1164) wraps it in a SeparateNode when its update bits are not all set.
    /// A loop effect stays; another one goes after its duration (CEffect::vfunc_9).
    /// Not done:
    /// - battle stance (characterBattleIdle);
    /// - parent nodes that only an item model has (wings, kfm bodies): counted in IdleEffectsOpen;
    /// - attachType "Mesh" and attach type 0, applyNodeTransform="false", and the non-nif kinds (swoosh, sound).
    /// </summary>
    void PlayIdleEffects(Node3D visual, Skeleton3D skel, ModelData body, IEnumerable<EquipItem> items)
    {
        var model = visual.GetNodeOrNull<Node3D>("Model");
        if (model == null) return;
        foreach (var old in model.FindChildren("itemfx_*", "", true, false)) old.QueueFree();
        effects ??= new Ms2.Formats.World.EffectTable(packs);
        foreach (var item in Visible(items))
        {
            if (!characterIdle.TryGetValue(item.Id, out var xml) && (FindItem(item.Id) == null || !characterIdle.TryGetValue(item.Id, out xml))) continue;
            foreach (var part in effects.Get(xml) ?? new List<Ms2.Formats.World.EffectPart>())
            {
                if (part.Type != "nif" || string.IsNullOrEmpty(part.FilePath)) continue;
                if (!part.AttachToNode || !part.ApplyNodeTransform) { IdleEffectsOpen++; continue; }
                Node3D parent;
                string name = part.ParentNode ?? "";
                int bone = skel.FindBone(name);
                if (bone >= 0)
                {
                    var att = new BoneAttachment3D { Name = $"itemfx_{item.Id}_attach", BoneName = name };
                    skel.AddChild(att);
                    parent = att;
                }
                else if (itemNodes.Contains(name)) { IdleEffectsOpen++; continue; }
                else parent = model;    // no node of that name anywhere: the graph root
                Node3D fx;
                try { fx = Ui.EffectNif.Build(mb, packs, part); }
                catch (Exception ex) { GD.PushWarning($"item {item.Id} idle effect {part.FilePath}: {ex.Message}"); continue; }
                Node3D holder = parent;
                if (!(part.UpdateTranslate && part.UpdateRotation && part.UpdateScale))
                {
                    holder = new Ui.SeparateNode3D(part.UpdateTranslate, part.UpdateRotation, part.UpdateScale);
                    parent.AddChild(holder);
                }
                fx.Name = $"itemfx_{item.Id}_{fx.Name}";
                if (holder is Ui.SeparateNode3D) holder.Name = $"itemfx_{item.Id}_separate";
                fx.Visible = part.StartDelay <= 0;
                holder.AddChild(fx);
                IdleEffects++;
                var tree = (Engine.GetMainLoop() as SceneTree);
                if (part.StartDelay > 0 && tree != null)
                    tree.CreateTimer(part.StartDelay).Timeout += () => { if (GodotObject.IsInstanceValid(fx)) fx.Visible = true; };
                if (!part.Loop && part.Duration > 0 && tree != null)
                    tree.CreateTimer(part.StartDelay + part.Duration).Timeout += () => { if (GodotObject.IsInstanceValid(fx)) fx.QueueFree(); };
            }
        }
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
            // degrees to radians, then efd::Matrix3::FromEulerAnglesXYZ (equipmodel.cpp:589-591): Gamebryo's rotations
            // turn clockwise about each axis
            return ModelBuilder.ToGodot(new Xform(new Vec3(t[0], t[1], t[2]), DummyWorld.EulerXyz(r[0], r[1], r[2]), 1));
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
            var sm = md.Meshes.FirstOrDefault(x => x.FileName == selfName && !x.Skinned);
            if (sb >= 0) selfInv = ModelBuilder.ToGodot(md.Bones[sb].World).AffineInverse();
            else if (sm != null)
                selfInv = ((sm.ParentBone >= 0 ? ModelBuilder.ToGodot(md.Bones[sm.ParentBone].World) : Transform3D.Identity) * ModelBuilder.ToGodot(sm.Local)).AffineInverse();
        }
        var dummy = DummyOf(asset, gender);
        foreach (var m in md.Meshes)
        {
            var mi = new MeshInstance3D { Name = $"eq{item.Id}_{m.Name}", Mesh = Mesh(m) };
            // SetShadowCaster(mesh, true) (equipment.cpp:2436) skips a "NoShadow"-tagged subtree (ModelMesh.NoShadow)
            if (m.NoShadow) mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            mi.SetMeta("mesh", m.FileName ?? m.Name);
            if (m.Skinned)
            {
                // FUN_141421f30 (itemugcmesh.cpp:271-290) swaps the skin's root parent and bones for the body's nodes of the
                // same names and keeps the skin's bone transforms, so each mesh binds through its own skin bone transforms
                // (as ModelBuilder.SkinOf), not through the item file's node pose; by the file's name: an item holding a
                // second copy of the skeleton (11400479_f_strikerredtop has three Bip01 trees) binds every copy to the
                // body's bone of that name
                var own = ModelBuilder.SkinOf(md, m);
                var skin = new Skin();
                for (int i = 0; i < md.Bones.Count; i++)
                {
                    int bi = skel.FindBone(md.Bones[i].FileName ?? md.Bones[i].Name);
                    skin.AddBind(bi >= 0 ? bi : 0, own.GetBindPose(i));
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
                    var bodyMesh = body.Meshes.FirstOrDefault(x => x.FileName == target && x.ParentBone >= 0);
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
            ModelBuilder.Attached(mi, m, skel, b => skel.FindBone(md.Bones[b].FileName ?? md.Bones[b].Name));
            made.Add(mi);
        }
        return made;
    }

    /// <summary>
    /// A kfm asset (CKfmModel), a model of its own hung on the asset's target node: capes, many caps and helmets, and a
    /// hair's pony part (asset pony="1", index = its pony index; -1 for the other kfm assets):
    /// <list type="bullet">
    /// <item>its name is not a .nif, so it is the kfm asset "urn:gamebryo-animation:" + name (ModelPart::Load @0x140543ca0,
    /// itemdatastruct.cpp:287-303): the kfm of that name in the Item pack, whose nif sits beside it;</item>
    /// <item>the self nodes (the outermost nodes named like its selfnode, FUN_141422110) hang under the attach node on the
    /// target node and keep their local transform (CKfmModel::vfunc_12, kfmmodel.cpp:518-544), turned and moved by the
    /// asset's dummy as a nif asset's are; the model's skinned meshes follow its own bones, so a cape skinned to MT_R and
    /// MT_L under MT_Point01 hangs from the spine;</item>
    /// <item>once loaded the model plays its Idle_A sequence (CKfmModel::vfunc_6, kfmmodel.cpp:403-408), looping;</item>
    /// <item>a pony part's self nodes take the hair data of its pony index instead (CKfmModel::FUN_141440bc0,
    /// kfmmodel.cpp:943): translation m_aPt1[i], rotation Euler XYZ degrees m_aPt2[i], uniform scale m_fScale[0]
    /// (CHairExtraData, read in the packet order of vfunc_6 @0x14059af30: per index a length, Pt1, Pt2); without hair
    /// data from the server, the values the item gives a new hair: Pt1 and Pt2 from the part's &lt;custom&gt;
    /// (CHairExtraData::vfunc_4 @0x14059aaa0; the client picks one custom at random, the hair items hold one) and the
    /// length from the slot's &lt;scale&gt; (the client picks at random between min and max; min here).</item>
    /// </list>
    /// Not done: PhysX swing (the NiPhysX joints in the nif), Attack_Idle_A in battle stance (vfunc_32), and the
    /// replace path to Bip01 Head (vfunc_13).
    /// </summary>
    List<MeshInstance3D> AttachKfm(Skeleton3D skel, ModelData body, XElement asset, EquipItem item, int gender, int index)
    {
        string name = ((string)asset.Attribute("name") ?? "").Replace("urn:gamebryo-animation:", "").Replace("urn:", "");
        string kfmEntry = packs.FindByFileName(ItemArchive, name.ToLowerInvariant() + ".kfm");
        if (kfmEntry == null) { NoAsset++; return null; }
        ModelData md;
        KfmFile kfmSeqs;
        try
        {
            var kfm = kfmSeqs = KfmFile.Load(packs.Read(ItemArchive, kfmEntry));
            string nif = System.IO.Path.GetFileName(kfm.Nif.Replace('\\', '/')).ToLowerInvariant();
            md = Nif(kfmEntry[..(kfmEntry.LastIndexOf('/') + 1)] + nif);
        }
        catch (Exception ex) { Failed++; GD.PushWarning($"item {item.Id} kfm: {ex.Message}"); return null; }
        if (md == null) { NoAsset++; return null; }
        Loaded++;
        NoteNodes(md);
        bool pony = index >= 0;
        double[] pos = null, rot = null; double scale = 1;
        var a = item.Appearance;
        if (!pony) { }
        else if (a != null && a.Length >= 14)
        {
            int o = index * 7;
            pos = new double[] { a[o + 1], a[o + 2], a[o + 3] };
            rot = new double[] { a[o + 4], a[o + 5], a[o + 6] };
            scale = a[0];
        }
        else
        {
            var custom = asset.Element("custom");
            pos = Floats((string)custom?.Attribute("position"));
            rot = Floats((string)custom?.Attribute("rotation"));
            var sc = asset.Parent?.Element("scale");
            scale = double.TryParse((string)sc?.Attribute("min"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double mn) ? mn : 1;
        }
        string target = (string)asset.Attribute("targetnode") ?? "Bip01 Head";
        string bone = target;
        var targetLocal = Transform3D.Identity;     // the target node relative to its bone
        if (skel.FindBone(bone) < 0)
        {
            // as Attach: a replace asset on a node the body lacks goes to Bip01 Head, else the body's rigid mesh of that name
            var bodyMesh = (string)asset.Attribute("replace") == "1" ? null : body.Meshes.FirstOrDefault(x => x.FileName == target && x.ParentBone >= 0);
            bone = bodyMesh != null ? body.Bones[bodyMesh.ParentBone].Name : "Bip01 Head";
            if (bodyMesh != null) targetLocal = ModelBuilder.ToGodot(bodyMesh.Local);
        }
        string tag = pony ? $"pony{index}" : "kfm";
        var att = new BoneAttachment3D { Name = $"eq{item.Id}_{tag}_attach", BoneName = bone };
        skel.AddChild(att);
        var root = mb.Build(md, $"eq{item.Id}_{tag}");
        // the body skeleton is in file space (Z-up), so the built model's own Z-up turn is undone
        root.Transform = new Transform3D(new Basis(Vector3.Right, -Mathf.Pi / 2), Vector3.Zero).AffineInverse();
        att.AddChild(root);
        var ps = root.GetNode<Skeleton3D>("Model/" + ModelBuilder.SkeletonName);
        string selfName = (string)asset.Attribute("selfnode");
        // efd::Matrix3::FromEulerAnglesXYZ = MakeXRotation(x) * MakeYRotation(y) * MakeZRotation(z), and Gamebryo's
        // Make*Rotation turn clockwise (MakeXRotation puts +sin at [1][2]: Foundation/efd/Win32/Matrix3_Win32.cpp:76-107,
        // 1146-1154), so each angle is the negated counter-clockwise turn
        var dummy = pony ? Transform3D.Identity : DummyOf(asset, gender);
        Transform3D? xf = pony ? ModelBuilder.ToGodot(new Xform(new Vec3(pos[0], pos[1], pos[2]), DummyWorld.EulerXyz(rot[0], rot[1], rot[2]), scale)) : null;
        // the self nodes: the outermost nodes whose name starts with selfnode (Point01, not its child "Point01 NonAccum")
        bool Self(int b) => selfName != null && md.Bones[b].Name != null && md.Bones[b].Name.StartsWith(selfName, StringComparison.Ordinal);
        bool Under(int b) { for (int u = md.Bones[b].Parent; u >= 0; u = md.Bones[u].Parent) if (Self(u)) return true; return false; }
        for (int b = 0; b < md.Bones.Count; b++)
        {
            if (!Self(b) || Under(b)) continue;
            // the self node hangs under the attach node (CKfmModel::vfunc_12, kfmmodel.cpp:518-544) keeping its local
            // transform, or takes the hair data's (pony, FUN_141440bc0)
            var x = targetLocal * dummy * (xf ?? ModelBuilder.ToGodot(md.Bones[b].Rest));
            ps.SetBoneParent(b, -1);
            ps.SetBoneRest(b, x);
            ps.SetBonePosePosition(b, x.Origin);
            ps.SetBonePoseRotation(b, x.Basis.GetRotationQuaternion());
            ps.SetBonePoseScale(b, x.Basis.Scale);
        }
        // CKfmModel::vfunc_6 (kfmmodel.cpp:403-408) plays the model's Idle_A once it has loaded
        if (kfmSeqs.ByName("Idle_A") is { } idle)
        {
            string kfEntry = kfmEntry[..(kfmEntry.LastIndexOf('/') + 1)] + System.IO.Path.GetFileName(idle.KfPath.Replace('\\', '/')).ToLowerInvariant();
            if (packs.Exists(ItemArchive, kfEntry))
            {
                try
                {
                    var anim = ModelBuilder.BuildAnimation(KfAnimation.Load(Ms2.Formats.Nif.NifFile.Load(mb.Spec, packs.Read(ItemArchive, kfEntry))), md);
                    anim.LoopMode = Animation.LoopModeEnum.Linear;
                    var player = root.GetNode<AnimationPlayer>("Model/AnimationPlayer");
                    player.GetAnimationLibrary("").AddAnimation("Idle_A", anim);
                    player.Play("Idle_A");
                }
                catch (Exception ex) { GD.PushWarning($"item {item.Id} Idle_A: {ex.Message}"); }
            }
        }
        var made = root.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>().ToList();
        foreach (var mi in made) mi.SetMeta("mesh", mi.Name.ToString());
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
        if (m.FileName != null && m.FileName.StartsWith(selfName, StringComparison.Ordinal)) return local;
        return null;
    }
}
