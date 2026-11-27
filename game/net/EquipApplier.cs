using System.Xml.Linq;
using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Formats.Text;

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
        public readonly Dictionary<int, int> Subtypes = new();                         // item -> limit type==1 subtype (weapon kind)
        public readonly Dictionary<int, string> CharacterIdle = new();                 // item -> its <effect characterIdle>
        public Ms2.Ui.Skills.WeaponActionTable WeaponActions;                          // table/weaponaction.xml
        public readonly HashSet<int> RowsRead = new();                                 // GMS2: items whose per-item file was read
        Ms2.Ui.Items.ItemFiles files;
        public Ms2.Ui.Items.ItemFiles Files(PackSet packs) => files ??= new Ms2.Ui.Items.ItemFiles(packs);
    }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PackSet, ItemTables> tables = new();
    readonly ItemTables shared;
    Dictionary<int, Dictionary<int, XElement>> docs => shared.Models;
    Dictionary<int, Dictionary<int, int>> presets => shared.Presets;
    Dictionary<int, string> characterIdle => shared.CharacterIdle;
    Ms2.Formats.World.EffectTable effects;
    readonly List<Ms2.Formats.World.EffectPart> idleParts = new();
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
                var x = PackXml.Parse(packs.Read("Xml", entry));
                foreach (var e in x.Root.Elements("item"))
                    if (int.TryParse((string)e.Attribute("id"), out int i)) ReadItemRow(i, e, byItem);
            }
            presets[prefix] = byItem;
        }
        // GMS2 keeps one file per item (item/{a}/{bb}/{id}.xml), no itemdata/{prefix}.xml: read the row of this item once.
        if (!packs.Exists("Xml", $"itemdata/{prefix}.xml") && shared.RowsRead.Add(id) && shared.Files(packs).Item(id) is { } row)
            ReadItemRow(id, row, byItem, true);
        return FindModel(byItem.TryGetValue(id, out int preset) ? preset : id);
    }

    /// <summary>The tool itemPreset redirect, the weapon subtype and the characterIdle effect of one item row.</summary>
    void ReadItemRow(int i, XElement e, Dictionary<int, int> byItem, bool perItemFile = false)
    {
        if (int.TryParse((string)e.Descendants("tool").FirstOrDefault()?.Attribute("itemPreset"), out int p) && p != 0)
            byItem[i] = p;
        var prop = e.Descendants("property").FirstOrDefault();
        if ((string)prop?.Attribute("type") == "1" && int.TryParse((string)prop.Attribute("subtype"), out int st))
            shared.Subtypes[i] = perItemFile ? Ms2.Ui.Items.ItemData.Gms2EquipSubtype(i, 1, st) : st;   // the GMS2 file's subtype is not the kind
        // ItemEffect::Load (itemdatastruct.cpp:1609-1625): <effect characterIdle> of the item itself
        if ((string)e.Descendants("effect").FirstOrDefault()?.Attribute("characterIdle") is { Length: > 0 } fx)
            characterIdle[i] = fx;
    }

    /// <summary>The weapon kind of an item (its subtype; on GMS2 the id-group kind, not the file's 0 or 16).</summary>
    public int WeaponSubtype(int id)
    {
        if (!shared.Subtypes.TryGetValue(id, out int st)) { FindItem(id); shared.Subtypes.TryGetValue(id, out st); }
        return st;
    }

    Ms2.Ui.Skills.WeaponActionTable WeaponActions => shared.WeaponActions ??= Ms2.Ui.Skills.WeaponActionTable.Load(packs);

    XElement FindModel(int id)
    {
        int prefix = id / 100000;
        if (!docs.TryGetValue(prefix, out var byId))
        {
            byId = new Dictionary<int, XElement>();
            string entry = $"itemmodel/{prefix}.xml";
            var x = packs.Exists("Xml", entry) ? PackXml.Parse(packs.Read("Xml", entry)) : Ms2.Formats.World.PerFileTable.ItemModels(packs, prefix);   // GMS2: itempreset/
            if (x != null)
            {
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

    /// <summary>
    /// CEquipItem::FUN_1411dcda0 (@0x1411dcda0, equipitem.cpp:394) for an item not yet worn through this applier: some model of the
    /// item answers CNifModel / CKfmModel::vfunc_15 (<see cref="ModelData.HasShaderTexture(NifFile)"/>). The models are the item's
    /// ItemModel assets for the wearer's gender (as Apply picks them); a model that does not load answers false.
    /// </summary>
    readonly ModelTextureCache assetTexture = new();   // an asset name's answer: the nif is parsed once, scanned and dropped

    /// <summary>The parsed NIF of an item asset (a .nif, or the model a kfm names), for the shader texture test; null when it is not packed.</summary>
    NifFile TextureNif(string name)
    {
        string entry;
        if (name.EndsWith(".nif", StringComparison.Ordinal))
        {
            const string prefix = "Data/Resource/Model/Item/";
            entry = (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? name[prefix.Length..] : name).ToLowerInvariant();
        }
        else
        {
            string kfmName = name.Replace("urn:gamebryo-animation:", "").Replace("urn:", "");
            string kfmEntry = packs.FindByFileName(ItemArchive, kfmName.ToLowerInvariant() + ".kfm");
            if (kfmEntry == null) return null;
            var kfm = KfmFile.Load(packs.Read(ItemArchive, kfmEntry));
            entry = kfmEntry[..(kfmEntry.LastIndexOf('/') + 1)] + System.IO.Path.GetFileName(kfm.Nif.Replace('\\', '/')).ToLowerInvariant();
        }
        return packs.Exists(ItemArchive, entry) ? NifFile.Load(mb.Spec, packs.Read(ItemArchive, entry)) : null;
    }

    public bool HasModelTexture(int itemId, int gender)
    {
        var im = FindItem(itemId);
        foreach (var slot in im?.Element("slots")?.Elements("slot") ?? Enumerable.Empty<XElement>())
        {
            var asset = slot.Elements("asset").FirstOrDefault(a => ForGender(a, gender));
            if (asset == null) continue;
            string name = ((string)asset.Attribute("name") ?? "").Trim();
            try
            {
                if (assetTexture.Has(name, () => TextureNif(name))) return true;
            }
            catch (Exception ex) { GD.PushWarning($"item {itemId}: {ex.Message}"); }
        }
        return false;
    }

    ArrayMesh Mesh(ModelMesh m) => meshes.TryGetValue(m, out var am) ? am : meshes[m] = mb.SharedMesh(m);

    static bool ForGender(XElement asset, int gender) => AssetGender.Matches((string)asset.Attribute("gender"), gender);

    /// <summary>The visible item per slot: the last one listed for each slot.</summary>
    public static List<EquipItem> Visible(IEnumerable<EquipItem> items)
    {
        var bySlot = new Dictionary<EquipSlot, EquipItem>();
        foreach (var it in items) bySlot[it.Slot] = it;
        return bySlot.Values.ToList();
    }

    public Look.ColorPalettes Palettes;   // optional: default dye of items whose record has no colour

    /// <summary>One worn item's part of a character: the nodes it hung on the skeleton, what it hides on the body.</summary>
    sealed class Part
    {
        public EquipItem Item;
        public readonly List<MeshInstance3D> Meshes = new();
        public readonly List<string> Replaces = new();     // body mesh groups it takes the place of (replace="1" slot names)
        public readonly List<string> Cuts = new();         // meshes it hides by name (itemmodel cutting)
        public readonly List<string> NodeNames = new();
        public readonly List<WeaponBinding> Bindings = new();   // its weapon hooks (a weapon's rig)
        public int Subtype;                                 // the weapon subtype of an RH / LH item
        public bool LinksLh;                                // an RH item whose model also has an LH group (bows, cannons, knuckles)    // node names of its models (a parent node of an idle effect can be one of these)
        public readonly List<SelfRoot> Roots = new();       // the nodes its self nodes place (GetRoots, equipmodel.cpp:906)
        public readonly List<(MeshInstance3D Node, ModelMesh Mesh)> Picks = new();   // its meshes with their file triangles (the cap pick)
        public float? CapScale;                             // the first model part's capscale (ModelPart +0x40, itemdatastruct.cpp:311)
    }

    /// <summary>
    /// A node a worn model's self node (a root of the model, GetRoots @equipmodel.cpp:906) places: the node's transform on its
    /// attach node is <c>Prefix * Local * Rel</c>, where Prefix is the target node's offset and the dummy (the attach node),
    /// Local the root's own local transform and Rel the node relative to the root. Nodes of one root share its Key.
    /// </summary>
    sealed class SelfRoot
    {
        public string Key; public Node3D Node; public Transform3D Prefix, Local, Rel;
    }

    /// <summary>What a built character keeps so one slot can change without rebuilding the body.</summary>
    sealed class BuiltLook
    {
        public Skeleton3D Skel; public ModelData Body; public int Gender;
        public Rgba? Skin, Shade;
        public List<MeshInstance3D> BodyNodes; public Dictionary<MeshInstance3D, bool> BodyShown;
        public readonly Dictionary<EquipSlot, Part> Parts = new();
        public Dictionary<EquipSlot, WornEquips.SlotKey> Keys = new();
    }
    readonly System.Runtime.CompilerServices.ConditionalWeakTable<Node3D, BuiltLook> looks = new();

    /// <summary>Models hung on a character (and their idle effects) that the last Apply / Reapply left: for the leak checks.</summary>
    public int HungNodes(Node3D visual) => looks.TryGetValue(visual, out var l) ? l.Parts.Values.Sum(p => p.Meshes.Count) : 0;

    /// <summary>The worn face item (slot FA) when its model hung parts on the look (PC::vfunc_69's test, pc.cpp:3096-3101),
    /// else 0.</summary>
    public int FaceItem(Node3D visual) =>
        looks.TryGetValue(visual, out var l) && l.Parts.TryGetValue(EquipSlot.FA, out var p) && p.Meshes.Count > 0 ? p.Item.Id : 0;

    public void Apply(Node3D visual, ModelData body, int gender, IEnumerable<EquipItem> items, Rgba? skinColor = null, Rgba? skinShade = null)
    {
        var skel = visual.GetNode<Skeleton3D>("Model/" + ModelBuilder.SkeletonName);
        var bodyNodes = skel.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>().ToList();
        var look = new BuiltLook { Skel = skel, Body = body, Gender = gender, Skin = skinColor, Shade = skinShade, BodyNodes = bodyNodes,
            BodyShown = bodyNodes.ToDictionary(n => n, n => n.Visible) };
        looks.AddOrUpdate(visual, look);
        Skin(look, bodyNodes);       // the body takes the skin colour pair too (every mesh drawn with the skin material)
        var list = Visible(items);
        foreach (var item in list) AddPart(look, item);
        Hide(look);
        PlaceCap(look);
        look.Keys = WornEquips.Shown(list);
        SaveRig(visual, look);
        PlayIdleEffects(visual, look, items);
    }

    /// <summary>
    /// The per-slot path for a put-on / put-off of an applied character: only the slots whose item changed are rebuilt
    /// (CEquipment::FUN_1411e36e0 resets and re-stores the slot's items, FUN_1411e3bd0 clears them, then RefreshModel
    /// rebuilds the merged model; here the other slots keep their nodes). The old nodes are freed (QueueFree), the body
    /// is not rebuilt, and the hidden body and item meshes are worked out again from the parts that remain.
    /// Returns the number of slots rebuilt; false when the character was not applied by this applier.
    /// </summary>
    public bool Reapply(Node3D visual, IEnumerable<EquipItem> items, EquipSlot? force = null)
    {
        if (!looks.TryGetValue(visual, out var look) || !GodotObject.IsInstanceValid(visual)) return false;
        var list = Visible(items);
        var now = WornEquips.Shown(list);
        var slots = WornEquips.Changed(look.Keys, now);
        if (force is { } f && WornEquips.DrawsModel(f) && !slots.Contains(f)) slots.Add(f);
        if (slots.Count == 0) return true;
        var model = visual.GetNodeOrNull<Node3D>("Model");
        var namesBefore = NodeNamesOf(look);
        foreach (var slot in slots)
        {
            if (look.Parts.Remove(slot, out var old)) FreeNodes(look, old);
            // the effects go by the id the slot was built from: an item with no model entry has no Part but may have an idle effect
            if (model != null && look.Keys.TryGetValue(slot, out var was)) FreeEffects(model, was.Id);
            var item = list.FirstOrDefault(i => i.Slot == slot);
            if (item != null) AddPart(look, item);
        }
        Hide(look);
        PlaceCap(look);
        // the effects after every changed slot is built, against this character's nodes only (as a fresh Apply)
        if (model != null)
        {
            var names = NodeNamesOf(look);
            if (!names.SetEquals(namesBefore)) PlayIdleEffects(visual, look, list);     // an unchanged item's parentNode may have come or gone
            else foreach (var slot in slots)
                if (list.FirstOrDefault(i => i.Slot == slot) is { } it) IdleEffectsOf(model, look.Skel, it, names);
        }
        SaveRig(visual, look);
        look.Keys = now;
        return true;
    }

    /// <summary>
    /// CEquipment::FUN_1411e5c10 (@0x1411e5c10, equipment.cpp:2794-2917), which FUN_1411e6ab0 (equipment.cpp:1716-1726) runs
    /// each time the character's models are on: a cap (slot CP) whose item model's customize has capAttach
    /// (ItemModelHead::Load @0x140546a90, itemdatastruct.cpp:944; IsCapAttach @0x1405471b0) sits on the worn hair (slot HR).
    /// <list type="number">
    /// <item>Each root of each cap model takes the local transform {scale: the capscale of the hair's first model part
    /// (ModelPart +0x40, itemdatastruct.cpp:311); rotation: FromEulerAnglesXYZ of the hair's customize capTransform rotation, as
    /// stored (radians); translate: its position}. The cap record's rotation (degrees) and position (CCapExtraData +0x80 and
    /// +0x74, the item's appearance floats 9-11 and 6-8) replace those when not zero. A hair without a capTransform leaves the
    /// roots as they are.</item>
    /// <item>From each root a ray goes along the root's world -Z, starting 400 units out on +Z (kOrigin = T - kDir * 400), and
    /// picks the hair's meshes (FUN_141424d00 @0x141424d00, itemugcmesh.cpp:846: front faces only, triangles, world
    /// coordinates; NiCollisionUtils::IntersectTriangle). The root moves to the hit nearest to the ray's origin, keeping its
    /// world rotation and scale (SetLocalFromWorldTransform). A hair with meshes and no hit puts the root back at the
    /// hair's capTransform, without the record's override; a hair with no mesh leaves step 1's transform.</item>
    /// </list>
    /// The pick here tests every mesh of the hair's models (the client walks the meshes under the hair's roots), posed as
    /// drawn: a skinned mesh through its skeleton's current pose.
    /// GMS2's item files keep the flag and the transform as customize/CP@attach and customize/CP/transform (the layout of
    /// that data; there is no GMS2 client to cite).
    /// STILL-OPEN: a cap without capAttach (FUN_1411ef2e0, equipment.cpp:2450: the record's position and rotation, the
    /// capscale through vfunc_22, the record's "Editable Mesh" morph weight) keeps its own transform.
    /// </summary>
    void PlaceCap(BuiltLook look)
    {
        if (!look.Parts.TryGetValue(EquipSlot.CP, out var cap) || cap.CapScale == null) return;
        if (!look.Parts.TryGetValue(EquipSlot.HR, out var hair) || hair.CapScale is not { } fScale) return;
        if (!CapAttach(FindItem(cap.Item.Id)?.Element("customize"))) return;
        var capTransform = CapTransformOf(FindItem(hair.Item.Id)?.Element("customize"));
        var roots = cap.Roots.Where(r => GodotObject.IsInstanceValid(r.Node)).GroupBy(r => r.Key).ToList();
        if (capTransform is { } ct)
        {
            var k = CapLocal(fScale, ct.Rot, ct.Pos);
            var a = cap.Item.Appearance;
            if (a is { Length: >= 12 })
            {
                if (a[9] != 0 || a[10] != 0 || a[11] != 0)
                    k.Basis = ModelBuilder.ToGodot(new Xform(new Vec3(0, 0, 0), DummyWorld.EulerXyz(a[9], a[10], a[11]), fScale)).Basis;
                if (a[6] != 0 || a[7] != 0 || a[8] != 0) k.Origin = new Vector3(a[6], a[7], a[8]);
            }
            foreach (var g in roots) foreach (var r in g) r.Node.Transform = r.Prefix * k * r.Rel;
        }
        foreach (var g in roots)
        {
            var first = g.First();
            var onAttach = InSkeleton(first.Node.GetParent(), look.Skel) * first.Prefix;
            var world = onAttach * first.Prefix.AffineInverse() * first.Node.Transform * first.Rel.AffineInverse();
            var dir = world.Basis.Orthonormalized() * new Vector3(0, 0, -1);
            var origin = world.Origin - dir * 400f;
            Transform3D? local = null;
            if (PickMeshes(hair, look.Skel, origin, dir, out var hit, out bool empty))
                local = onAttach.AffineInverse() * new Transform3D(world.Basis, hit);
            else if (!empty)
                local = capTransform is { } ct2 ? CapLocal(fScale, ct2.Rot, ct2.Pos) : new Transform3D(Basis.Identity.Scaled(new Vector3(fScale, fScale, fScale)), Vector3.Zero);
            if (local is { } l) foreach (var r in g) r.Node.Transform = r.Prefix * l * r.Rel;
        }
    }

    /// <summary>{fScale, FromEulerAnglesXYZ(rotation in radians), position}: the root transform of a cap on a hair.</summary>
    static Transform3D CapLocal(float scale, double[] rot, double[] pos)
    {
        const double toDeg = 180.0 / 3.1415927f;     // EulerXyz turns degrees back with the client's float pi
        return ModelBuilder.ToGodot(new Xform(new Vec3(pos[0], pos[1], pos[2]), DummyWorld.EulerXyz(rot[0] * toDeg, rot[1] * toDeg, rot[2] * toDeg), scale));
    }

    /// <summary>ItemModelHead capAttach (customize@capAttach; GMS2 customize/CP@attach).</summary>
    static bool CapAttach(XElement customize) =>
        (string)customize?.Attribute("capAttach") == "1" || (string)customize?.Element("CP")?.Attribute("attach") == "1";

    /// <summary>The customize capTransform (MsTransform::LoadXml @0x1405468d0: position, rotation; GMS2 customize/CP/transform), null when absent.</summary>
    static (double[] Pos, double[] Rot)? CapTransformOf(XElement customize)
    {
        var t = customize?.Element("capTransform") ?? customize?.Element("CP")?.Element("transform");
        return t == null ? null : (Floats((string)t.Attribute("position")), Floats((string)t.Attribute("rotation")));
    }

    /// <summary>ModelPart's capscale (itemdatastruct.cpp:311), 1 when the asset has none.</summary>
    static float CapScaleOf(XElement asset) =>
        float.TryParse((string)asset.Attribute("capscale"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) ? f : 1f;

    /// <summary>A node's transform in the space of <paramref name="skel"/>, a bone attachment standing at its bone's pose.</summary>
    static Transform3D InSkeleton(Node n, Skeleton3D skel)
    {
        var t = Transform3D.Identity;
        while (n != null && n != skel)
        {
            if (n is BoneAttachment3D ba && ba.GetParent() is Skeleton3D s)
            {
                int b = s.FindBone(ba.BoneName);
                if (b >= 0) t = s.GetBoneGlobalPose(b) * t;
                n = s;
                continue;
            }
            if (n is Node3D n3) t = n3.Transform * t;
            n = n.GetParent();
        }
        return t;
    }

    /// <summary>
    /// FUN_141424d00's pick over a part's meshes in the skeleton's space: the front-facing triangle hits of the ray, the nearest
    /// to the origin in <paramref name="nearest"/> (the first of equals, as equipment.cpp:2878-2884 keeps it).
    /// <paramref name="empty"/> is false when the part has any mesh to test.
    /// </summary>
    static bool PickMeshes(Part part, Skeleton3D skel, Vector3 origin, Vector3 dir, out Vector3 nearest, out bool empty)
    {
        empty = true; nearest = default;
        bool any = false; float best = 0;
        foreach (var (mi, m) in part.Picks)
        {
            if (!GodotObject.IsInstanceValid(mi) || mi.IsQueuedForDeletion()) continue;
            empty = false;
            if (m.Positions == null || m.Indices == null) continue;
            var p = SkeletonPositions(mi, m, skel);
            for (int i = 0; i + 2 < m.Indices.Length; i += 3)
            {
                if (!IntersectFront(origin, dir, p[m.Indices[i]], p[m.Indices[i + 1]], p[m.Indices[i + 2]], out var hit)) continue;
                float d = (hit - origin).Length();
                if (!any || best > d) { nearest = hit; best = d; }
                any = true;
            }
        }
        return any;
    }

    /// <summary>A mesh's vertices in the skeleton's space as drawn (a skinned one through its skeleton's pose and binds).</summary>
    static Vector3[] SkeletonPositions(MeshInstance3D mi, ModelMesh m, Skeleton3D skel)
    {
        int nv = m.Positions.Length / 3;
        var o = new Vector3[nv];
        if (m.Skinned && mi.Skin is { } skin && mi.GetNodeOrNull<Skeleton3D>(mi.Skeleton) is { } s && m.Joints != null && m.Weights != null)
        {
            var toSkel = InSkeleton(s, skel);
            var mats = new Transform3D[skin.GetBindCount()];
            for (int j = 0; j < mats.Length; j++)
            {
                int b = skin.GetBindBone(j);
                mats[j] = toSkel * (b >= 0 && b < s.GetBoneCount() ? s.GetBoneGlobalPose(b) : Transform3D.Identity) * skin.GetBindPose(j);
            }
            for (int v = 0; v < nv; v++)
            {
                var pos = new Vector3(m.Positions[v * 3], m.Positions[v * 3 + 1], m.Positions[v * 3 + 2]);
                var acc = Vector3.Zero;
                for (int k = 0; k < 4; k++)
                {
                    float w = m.Weights[v * 4 + k];
                    int j = m.Joints[v * 4 + k];
                    if (w != 0 && j >= 0 && j < mats.Length) acc += mats[j] * pos * w;
                }
                o[v] = acc;
            }
            return o;
        }
        var t = InSkeleton(mi, skel);
        for (int v = 0; v < nv; v++) o[v] = t * new Vector3(m.Positions[v * 3], m.Positions[v * 3 + 1], m.Positions[v * 3 + 2]);
        return o;
    }

    /// <summary>NiCollisionUtils::IntersectTriangle (NiCollisionUtils.inl:73) with culling: a front face (det at least the
    /// tolerance) hit at r >= 0 along the ray.</summary>
    static bool IntersectFront(Vector3 origin, Vector3 dir, Vector3 v1, Vector3 v2, Vector3 v3, out Vector3 hit)
    {
        const float tolerance = 1e-05f, epsilon = 1e-10f;
        hit = default;
        var e1 = v2 - v1; var e2 = v3 - v1;
        var pt = dir.Cross(e2);
        float det = e1.Dot(pt);
        if (det < tolerance) return false;
        var sv = origin - v1;
        float s = sv.Dot(pt);
        if (s < -epsilon || s > det) return false;
        var q = sv.Cross(e1);
        float t = dir.Dot(q);
        if (t < -epsilon || s + t > det) return false;
        float r = e2.Dot(q);
        if (r < -epsilon) return false;
        hit = origin + (r * (1.0f / det)) * dir;
        return true;
    }

    /// <summary>The weapon rig of the parts that are worn: the RH / LH subtypes, the LH link of a two-handed RH model (FUN_1411ec190), every part's hooks.</summary>
    void SaveRig(Node3D visual, BuiltLook look)
    {
        look.Parts.TryGetValue(EquipSlot.RH, out var rh);
        look.Parts.TryGetValue(EquipSlot.LH, out var lh);
        int rhSubtype = rh?.Subtype ?? 0, lhSubtype = lh?.Subtype ?? 0;
        bool linkedLh = false;
        if (lhSubtype == 0 && rh != null && rh.LinksLh) { lhSubtype = rhSubtype; linkedLh = true; }
        var bindings = new List<WeaponBinding>();
        foreach (var part in look.Parts.Values) bindings.AddRange(part.Bindings);
        SaveWeaponRig(visual, bindings, rhSubtype, lhSubtype, linkedLh);
    }

    /// <summary>The nodes under the skeleton a part's meshes belong to (a skinned mesh itself, a rigid one's attach node, a kfm's).</summary>
    static void FreeNodes(BuiltLook look, Part part)
    {
        foreach (var mi in part.Meshes)
        {
            if (!GodotObject.IsInstanceValid(mi)) continue;
            Node top = mi;
            while (top.GetParent() is { } p && p != look.Skel) top = p;
            if (top.IsQueuedForDeletion()) continue;
            top.Name = "old_" + top.Name;        // a rebuilt model of the same item keeps its name
            top.QueueFree();
        }
    }

    static void FreeEffects(Node3D model, int itemId)
    {
        foreach (var fx in model.FindChildren($"itemfx_{itemId}_*", "", true, false)) { fx.Name = "old_" + fx.Name; fx.QueueFree(); }
    }

    /// <summary>Builds one worn item's models and records what it hides; false when the item draws nothing.</summary>
    void AddPart(BuiltLook look, EquipItem item)
    {
        var skel = look.Skel; var body = look.Body; int gender = look.Gender;
        if (item.Slot == EquipSlot.SK || item.Slot == EquipSlot.FD) return;     // skin (the body) and face decals
        var im = FindItem(item.Id);
        var part = new Part { Item = item, Subtype = WeaponSubtype(item.Id) };
        look.Parts[item.Slot] = part;
        if (im == null) { NoModelEntry++; MissingIds.Add(item.Id); return; }
        int subtype = part.Subtype;
        foreach (var slot in im.Element("slots")?.Elements("slot") ?? Enumerable.Empty<XElement>())
        {
            var asset = slot.Elements("asset").FirstOrDefault(a => ForGender(a, gender));
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
            // CEquipment::FUN_1411ec190 (@0x1411ec190, equipment.cpp:2181-2201): an RH item whose ItemModel also
            // defines a <slot name="LH"> group (bows, cannons, knuckles) links into LH too when LH is empty
            if (item.Slot == EquipSlot.RH && string.Equals(slotName, "LH", StringComparison.OrdinalIgnoreCase)) part.LinksLh = true;
            if ((string)asset.Attribute("replace") == "1") part.Replaces.Add(slotName);
            foreach (var c in im.Element("cutting")?.Elements("mesh") ?? Enumerable.Empty<XElement>())
            {
                var g = (string)c.Attribute("gender");
                if (AssetGender.Matches(g, gender)) part.Cuts.Add((string)c.Attribute("name"));
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
                List<WeaponHook> hooks = null;
                var made = isNif ? Attach(part, skel, body, md, asset, item, gender, subtype, out hooks) : AttachKfm(part, skel, body, asset, item, gender, -1);
                if (made == null) { MissingIds.Add(item.Id); continue; }
                part.CapScale ??= CapScaleOf(asset);
                if (hooks != null) AddWeaponBindings(part.Bindings, skel, body, asset, slotName, item, gender, subtype, hooks);
                if (md != null) NoteNodes(part, md);
                if (DyeOf(item, im) is { } dye)
                    foreach (var n in made) SetColors(n, false, dye.Primary, dye.Secondary, dye.Tertiary);
                Skin(look, made);
                part.Meshes.AddRange(made);
            }
            int ponyIndex = 0;
            foreach (var pony in slot.Elements("asset").Where(a => a != asset && (string)a.Attribute("pony") == "1"
                         && ForGender(a, gender)))
            {
                var p = AttachKfm(part, skel, body, pony, item, gender, ponyIndex++);
                if (p == null) continue;
                part.CapScale ??= CapScaleOf(pony);
                if (DyeOf(item, im) is { } pd)
                    foreach (var n in p) SetColors(n, false, pd.Primary, pd.Secondary, pd.Tertiary);
                Skin(look, p);
                part.Meshes.AddRange(p);
            }
        }
    }

    /// <summary>The skin colour pair goes to every mesh drawn with the skin material (the client walks the whole character for it).</summary>
    static void Skin(BuiltLook look, IEnumerable<MeshInstance3D> nodes)
    {
        if (look.Skin is not { } sc) return;
        foreach (var n in nodes) SetColors(n, true, sc, null, look.Shade ?? sc);
    }

    /// <summary>Which body and item meshes show, from the parts that are on: the body as built, minus what a replace asset
    /// takes the place of and what an item cuts by name.</summary>
    static void Hide(BuiltLook look)
    {
        var cut = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in look.Parts.Values) foreach (var c in part.Cuts) cut.Add(c);
        foreach (var n in look.BodyNodes)
        {
            bool shown = look.BodyShown[n];
            string name = n.Name.ToString();
            if (shown)
                foreach (var part in look.Parts.Values)
                    foreach (var r in part.Replaces)
                        if (WornEquips.Replaces(r, name)) { shown = false; break; }
            if (shown && cut.Contains(BaseName(n.Name))) shown = false;
            n.Visible = shown;
        }
        foreach (var part in look.Parts.Values)
            foreach (var n in part.Meshes)
                if (GodotObject.IsInstanceValid(n)) n.Visible = !cut.Contains((string)n.GetMeta("mesh"));
    }

    static void NoteNodes(Part part, ModelData md)
    {
        foreach (var b in md.Bones) part.NodeNames.Add(b.FileName ?? b.Name);
        foreach (var m in md.Meshes) part.NodeNames.Add(m.FileName ?? m.Name);
    }

    static HashSet<string> NodeNamesOf(BuiltLook look)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in look.Parts.Values) foreach (var n in part.NodeNames) set.Add(n);
        return set;
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
    void PlayIdleEffects(Node3D visual, BuiltLook look, IEnumerable<EquipItem> items)
    {
        var model = visual.GetNodeOrNull<Node3D>("Model");
        if (model == null) return;
        foreach (var old in model.FindChildren("itemfx_*", "", true, false)) { old.Name = "old_" + old.Name; old.QueueFree(); }
        effects ??= new Ms2.Formats.World.EffectTable(packs);
        var names = NodeNamesOf(look);
        foreach (var item in Visible(items)) IdleEffectsOf(model, look.Skel, item, names);
    }

    /// <summary>One item's idle effects (PlayIdleEffects).</summary>
    void IdleEffectsOf(Node3D model, Skeleton3D skel, EquipItem item, HashSet<string> itemNodes)
    {
        {
            if (!characterIdle.TryGetValue(item.Id, out var xml) && (FindItem(item.Id) == null || !characterIdle.TryGetValue(item.Id, out xml))) return;
            effects ??= new Ms2.Formats.World.EffectTable(packs);
            // a random part makes one child (CRandomEffect::vfunc_3, randomeffectparam.cpp:143-178)
            foreach (var part in Ms2.Formats.World.EffectRandom.PlayList(effects.Get(xml), 0, Ms2.Formats.World.EffectRandom.Tick, idleParts))
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
        // a preset that names no palette: the item's own customize (GMS2's item files; ItemModelTable.ForItem)
        // (only the per-item layout has such rows: the KMS itemdata files are not parsed again here)
        var cust = Ms2.Ui.Items.ItemFiles.ColourHead(im, () => packs.Exists("Xml", $"itemdata/{item.Id / 100000}.xml") ? null : shared.Files(packs).Item(item.Id));
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

    /// <summary>
    /// CEquipModel::GetPartName (@0x1412029d0, equipmodel.cpp:1084-1102): the sheathed target node of an asset. When a
    /// dual-wield OH item (CItemData::FUN_14053d510 @0x14053d510, itemdata.cpp:925-934: subtype 31 dagger or 34 star) is
    /// worn in LH (slot 4), "_L_"/" L " and "_R_"/" R " in targetnode are swapped so the left-hand weapon sheathes on the
    /// opposite side (Weapon_Side_R_Point).
    /// </summary>
    static string SheathedTarget(XElement asset, EquipSlot equippedSlot, int subtype)
    {
        string target = (string)asset.Attribute("targetnode");
        if (string.IsNullOrEmpty(target) || equippedSlot != EquipSlot.LH || subtype is not (31 or 34)) return target;
        if (target.Contains("_L_", StringComparison.Ordinal)) return target.Replace("_L_", "_R_", StringComparison.Ordinal);
        if (target.Contains("_R_", StringComparison.Ordinal)) return target.Replace("_R_", "_L_", StringComparison.Ordinal);
        if (target.Contains(" L ", StringComparison.Ordinal)) return target.Replace(" L ", " R ", StringComparison.Ordinal);
        if (target.Contains(" R ", StringComparison.Ordinal)) return target.Replace(" R ", " L ", StringComparison.Ordinal);
        return target;
    }

    /// <summary>
    /// The asset's dummy offset for a gender (gender 2 = both) on <paramref name="targetNode"/>: ModelPart::Load
    /// (@0x140543ca0, itemdatastruct.cpp:393-394) defaults an omitted dummy targetnode to the asset's sheathed targetnode,
    /// and FUN_141200390 (@0x141200390, equipmodel.cpp:563-604) resets the attach node to identity before applying the
    /// first dummy whose sequence is empty and whose targetnode matches the current parent node.
    /// </summary>
    static Transform3D DummyOf(XElement asset, int gender, string targetNode = null)
    {
        // The one copy of the client's rule: ItemModelTable.Asset.DummyFor (last matching entry wins, sequence entries need the
        // character's sequence, which a static attach never has). degrees to radians, then efd::Matrix3::FromEulerAnglesXYZ
        // (equipmodel.cpp:589-591): Gamebryo's rotations turn clockwise about each axis
        var parsed = Ms2.Ui.Items.ItemModelTable.ParseAsset(0, "", asset);
        var (t, r) = parsed.DummyFor(gender, targetNode ?? parsed.TargetNode, "");
        return ModelBuilder.ToGodot(new Xform(new Vec3(t.X, t.Y, t.Z), DummyWorld.EulerXyz(r.X, r.Y, r.Z), 1));
    }

    static double[] Floats(string s)
    {
        var v = new double[3];
        var parts = (s ?? "").Split(',');
        for (int i = 0; i < 3 && i < parts.Length; i++)
            double.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[i]);
        return v;
    }

    List<MeshInstance3D> Attach(Part part, Skeleton3D skel, ModelData body, ModelData md, XElement asset, EquipItem item, int gender,
        int subtype, out List<WeaponHook> weaponHooks)
    {
        var made = new List<MeshInstance3D>();
        weaponHooks = item.Slot is EquipSlot.RH or EquipSlot.LH ? new() : null;
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
        string sheathedTarget = SheathedTarget(asset, item.Slot, subtype);
        var dummy = DummyOf(asset, gender, sheathedTarget);
        // the item's Point01 node (CEquipItem::FindPoint01Node, equipitem.cpp:604-613), where attach type 3 / 4 effect parts hang
        int pointBone = md.Bones.FindIndex(b => (b.FileName ?? b.Name) == Ms2.Formats.World.EffectAttachRule.WeaponPointNode);
        bool selfNodesMade = false;
        foreach (var m in md.Meshes)
        {
            string meshName = $"eq{item.Id}_{m.Name}";
            if (skel.GetNodeOrNull(meshName) != null || skel.GetNodeOrNull(meshName + "_attach") != null)
                meshName = $"eq{item.Id}_{item.Slot}_{m.Name}";
            var mi = new MeshInstance3D { Name = meshName, Mesh = Mesh(m) };
            // SetShadowCaster @0x141567330 (unit_141567000.cpp; for a merged mesh the call is CEquipment::MergeMeshes, equipment.cpp:2436) skips a "NoShadow"-tagged subtree (ModelMesh.NoShadow)
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
                string target = sheathedTarget ?? "Bip01 Head";
                string bone = target;
                var targetLocal = Transform3D.Identity;     // the target node relative to its bone
                var root = SelfRootOf(md, m, selfName);
                Transform3D? rel = root is { } sr ? sr.Local * sr.Rel : null;
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
                var baseLocal = rel is { } r ? r : (selfInv ?? selfFallback) * parentWorld * ModelBuilder.ToGodot(m.Local);
                mi.Transform = targetLocal * dummy * baseLocal;
                att.AddChild(mi);
                if (root is { } ro) part.Roots.Add(new SelfRoot { Key = ro.Key, Node = mi, Prefix = targetLocal * dummy, Local = ro.Local, Rel = ro.Rel });
                if (!selfNodesMade && rel != null)
                {
                    selfNodesMade = true;
                    SelfNodes(part, att, md, selfName, targetLocal * dummy);
                }
                Transform3D? point = pointBone >= 0
                    ? (parentWorld * ModelBuilder.ToGodot(m.Local)).AffineInverse() * ModelBuilder.ToGodot(md.Bones[pointBone].World) : null;
                weaponHooks?.Add(new WeaponHook(att, mi, bone, targetLocal, baseLocal, point));
            }
            ModelBuilder.Attached(mi, m, skel, b => skel.FindBone(md.Bones[b].FileName ?? md.Bones[b].Name), mb);
            made.Add(mi);
            part.Picks.Add((mi, m));
        }
        return made;
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Node3D, WeaponRig> rigs = new();
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Skeleton3D, Dictionary<EquipSlot, Skeleton3D>> kfmWeapons = new();

    /// <summary>
    /// The Point01 node of a kfm weapon (an orb: no rigid mesh, so not on the <see cref="WeaponRig"/>) worn in
    /// <paramref name="slot"/> on the body skeleton <paramref name="body"/>: a bone of the weapon model's own skeleton
    /// (CEquipItem::FindPoint01Node, equipitem.cpp:604-613, asks every model of the item). False when none is worn there.
    /// </summary>
    public static bool KfmWeaponPoint(Skeleton3D body, EquipSlot slot, out Skeleton3D weapon, out int bone)
    {
        weapon = null; bone = -1;
        if (body == null || !kfmWeapons.TryGetValue(body, out var bySlot) || !bySlot.TryGetValue(slot, out var w)) return false;
        if (!GodotObject.IsInstanceValid(w) || !w.IsInsideTree()) { bySlot.Remove(slot); return false; }
        bone = w.FindBone(Ms2.Formats.World.EffectAttachRule.WeaponPointNode);
        weapon = bone >= 0 ? w : null;
        return bone >= 0;
    }
    /// <summary>The weapon stance rig built for a character visual during <see cref="Apply"/>; null when it wears no weapon.</summary>
    public static WeaponRig RigOf(Node3D visual) => visual != null && rigs.TryGetValue(visual, out var r) ? r : null;

    /// <summary>One rigid weapon mesh attached during Attach; a class so the list shares the canonical generic code.</summary>
    sealed class WeaponHook
    {
        public readonly BoneAttachment3D Att;
        public readonly MeshInstance3D Mesh;
        public readonly string SheathedBone;
        public readonly Transform3D SheathedTargetLocal, BaseLocal;
        /// <summary>The item's Point01 node relative to this mesh; null when the item has none.</summary>
        public readonly Transform3D? Point01;

        public WeaponHook(BoneAttachment3D att, MeshInstance3D mesh, string sheathedBone, Transform3D sheathedTargetLocal, Transform3D baseLocal, Transform3D? point01)
        {
            Att = att; Mesh = mesh; SheathedBone = sheathedBone; SheathedTargetLocal = sheathedTargetLocal; BaseLocal = baseLocal; Point01 = point01;
        }
    }

    internal sealed record WeaponBinding(
        BoneAttachment3D Attach,
        MeshInstance3D Mesh,
        EquipSlot EffectiveSlot,
        bool HasWorldDummy,
        int Subtype,
        string SheathedBone,
        string HeldBone,
        Transform3D SheathedTransform,
        Transform3D HeldTransform,
        EquipSlot ItemSlot,
        Transform3D? Point01);

    /// <summary>
    /// Precomputes the sheathed and held bone attachments and transforms of a rigid weapon asset:
    /// <list type="bullet">
    /// <item>effective slot (CEquipModel::FUN_141202940 @0x141202940, equipmodel.cpp:1071-1080): for a dual-wield OH item
    /// (subtype 31/34), the equipped slot (RH or LH); otherwise the &lt;slot&gt; group in the ItemModel (RH or LH, so
    /// two-handed bows and cannons in &lt;slot name="LH"&gt; take LH and knuckles with both groups take RH and LH);</item>
    /// <item>held target (PC::FUN_141658150 @0x141658150, pc.cpp:3962-4033): asset attachnode when non-empty, else
    /// Weapon_Hand_R_Point for RH and Weapon_Hand_L_Point for LH;</item>
    /// <item>dummy transform (FUN_141200390 @0x141200390, equipmodel.cpp:563-604): matched against the held target node
    /// (identity when the asset's dummy is for the sheathed targetnode).</item>
    /// </list>
    /// </summary>
    static void AddWeaponBindings(
        List<WeaponBinding> list,
        Skeleton3D skel,
        ModelData body,
        XElement asset,
        string slotName,
        EquipItem item,
        int gender,
        int subtype,
        List<WeaponHook> hooks)
    {
        if (hooks.Count == 0) return;
        EquipSlot effectiveSlot = subtype is 31 or 34
            ? item.Slot
            : string.Equals(slotName, "LH", StringComparison.OrdinalIgnoreCase) ? EquipSlot.LH : EquipSlot.RH;
        string attachAttr = (string)asset.Attribute("attachnode");
        string heldTarget = !string.IsNullOrEmpty(attachAttr)
            ? attachAttr
            : effectiveSlot == EquipSlot.LH ? "Weapon_Hand_L_Point" : "Weapon_Hand_R_Point";
        string heldBone = heldTarget;
        var heldTargetLocal = Transform3D.Identity;
        if (skel.FindBone(heldBone) < 0)
        {
            var bodyMesh = body.Meshes.FirstOrDefault(x => x.FileName == heldTarget && x.ParentBone >= 0);
            heldBone = bodyMesh != null ? body.Bones[bodyMesh.ParentBone].Name : "Bip01 Head";
            if (bodyMesh != null) heldTargetLocal = ModelBuilder.ToGodot(bodyMesh.Local);
        }
        var heldDummy = DummyOf(asset, gender, heldTarget);
        bool hasWorldDummy = Ms2.Ui.Items.ItemModelTable.ParseAsset(0, "", asset).HasWorldDummy(gender);
        foreach (var h in hooks)
        {
            list.Add(new WeaponBinding(
                h.Att,
                h.Mesh,
                effectiveSlot,
                hasWorldDummy,
                subtype,
                h.SheathedBone,
                heldBone,
                h.Mesh.Transform,
                heldTargetLocal * heldDummy * h.BaseLocal,
                item.Slot,
                h.Point01));
        }
    }

    void SaveWeaponRig(Node3D visual, List<WeaponBinding> bindings, int rhSubtype, int lhSubtype, bool linkedLh)
    {
        rigs.Remove(visual);
        if (bindings.Count == 0) return;
        // PC::UpdateWeaponAction (@0x141653f00, pc.cpp:2838-2874): left hand counts 0 when it is the right hand's linked
        // item or when IsAllowed rejects the pair; then FindAction(right, left)
        int left = linkedLh || !WeaponActions.IsAllowed(rhSubtype, lhSubtype) ? 0 : lhSubtype;
        var wa = WeaponActions.FindAction(rhSubtype, left) ?? WeaponActions.FindAction(0, 0);
        rigs.Add(visual, new WeaponRig(bindings.ToArray(), WeaponActions, wa, rhSubtype, lhSubtype));
    }

    /// <summary>
    /// Moves a character's equipped weapons between their sheathed nodes and hand nodes on battle stance and movement
    /// state transitions (PC::FUN_141659000 @0x141659000, pc.cpp:2491-2511; PC::FUN_141657c80 @0x141657c80, :2473-2488;
    /// PC::FUN_141658150 @0x141658150, :3962-4033; FUN_141201d50 @0x141201d50, equipmodel.cpp:931-965). Plain C# state
    /// (not a Godot Node) so the scene tree node and object counts are unchanged.
    /// </summary>
    public sealed class WeaponRig
    {
        readonly WeaponBinding[] bindings;
        readonly Ms2.Ui.Skills.WeaponActionTable table;
        readonly int rhSubtype, lhSubtype;
        public readonly string Prefix;
        public readonly int RightHand;
        // fields, not properties: the game assembly runs under the JIT (it is not precompiled), and each accessor is a
        // method compiled on the first walk. Only SetState writes them.
        public bool InBattle, WeaponsHeld, StateAllowsWeapons = true;

        internal WeaponRig(WeaponBinding[] bindings, Ms2.Ui.Skills.WeaponActionTable table, Ms2.Ui.Skills.WeaponAction action, int rhSubtype, int lhSubtype)
        {
            this.bindings = bindings;
            this.table = table;
            this.rhSubtype = rhSubtype;
            this.lhSubtype = lhSubtype;
            Prefix = action?.Prefix ?? "";
            RightHand = action?.RightHand ?? 0;
        }

        /// <summary>
        /// PC::vfunc_60 (@0x141658a30, pc.cpp:1737-1749): the weapon animation prefix while weapons are held. STILL-OPEN: when
        /// they are not held the client returns the worn equipment's set name (CEquipment::GetSetName) when it has one, and only
        /// then ""; no set name is read here, so "" stands in.
        /// </summary>
        public string ActivePrefix => WeaponsHeld ? Prefix : "";

        /// <summary>The Point01 node of the weapon worn in <paramref name="slot"/> (FUN_1411e1860 then FindPoint01Node,
        /// nifeffectparam.cpp:1146-1150): the first of its meshes whose item has one, and the node relative to that mesh.</summary>
        public bool WeaponPoint(EquipSlot slot, out MeshInstance3D mesh, out Transform3D point)
        {
            foreach (var b in bindings)
                if (b.ItemSlot == slot && b.Point01 is { } p && GodotObject.IsInstanceValid(b.Mesh)) { mesh = b.Mesh; point = p; return true; }
            mesh = null; point = default;
            return false;
        }

        /// <summary>
        /// PC::FUN_141659000 (@0x141659000, pc.cpp:2491-2511): recomputes m_nD80 = vfunc_65() &amp;&amp; GetState()-&gt;vfunc_15()
        /// (cleared when m_spWeaponActionD70-&gt;m_nRightHand == 0) and runs FUN_141657c80(bActive) on a change.
        /// <paramref name="inCubeMode"/> is IsInCubeMode, which keeps the hands empty unless the state forces them.
        /// </summary>
        public bool SetState(bool inBattle, bool stateAllowsWeapons, bool force = false, bool stateForces = false, bool inCubeMode = false)
        {
            InBattle = inBattle;
            StateAllowsWeapons = stateAllowsWeapons;
            bool held = Ms2.Ui.Items.WeaponStance.HoldsWeapons(inBattle, stateAllowsWeapons, RightHand, inCubeMode, stateForces);
            if (!force && held == WeaponsHeld) return false;
            WeaponsHeld = held;
            Apply(held);
            return true;
        }

        void Apply(bool primary)
        {
            // PC::FUN_141658150 (@0x141658150, pc.cpp:3996): without a right-hand weapon, bPrimary is forced to 0
            if (rhSubtype == 0) primary = false;
            for (int i = 0; i < bindings.Length; i++)
            {
                var b = bindings[i];
                if (!GodotObject.IsInstanceValid(b.Attach) || !GodotObject.IsInstanceValid(b.Mesh)) continue;
                bool toHand = Ms2.Ui.Items.WeaponStance.UsesHandNode(primary, b.EffectiveSlot == EquipSlot.LH, b.HasWorldDummy,
                    b.EffectiveSlot != EquipSlot.LH || table.IsAllowed(rhSubtype, b.Subtype));
                string bone = toHand ? b.HeldBone : b.SheathedBone;
                if (b.Attach.BoneName != bone) b.Attach.BoneName = bone;
                b.Mesh.Transform = toHand ? b.HeldTransform : b.SheathedTransform;
            }
        }
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
    /// Not done: PhysX swing (the NiPhysX joints in the nif), Attack_Idle_A in battle stance (vfunc_32).
    /// STILL-OPEN: FUN_1411e99a0, the inner allow check the replace path calls, is not recovered; the gate itself is the asset's replace attribute
    /// (CKfmModel::vfunc_13), which is what is read here.
    /// </summary>
    List<MeshInstance3D> AttachKfm(Part part, Skeleton3D skel, ModelData body, XElement asset, EquipItem item, int gender, int index)
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
        NoteNodes(part, md);
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
        if (skel.FindBone(bone) < 0)   // a replace asset hangs on Bip01 Head only when its target is not a bone of the body (as before)
        {
            // a replace asset hangs on Bip01 Head with its self node's local moved by kXf, the body mesh's local
            // (ModelData.FindBodyAttach: CKfmModel::vfunc_13 @0x14143e900, kfmmodel.cpp:548-585); for the female
            // body's HR that is the offset from the head bone back to the model root the hair is authored in
            var (kb, kl) = body.FindBodyAttach(target, (string)asset.Attribute("replace") == "1");
            bone = kb;
            if (kl != null) targetLocal = ModelBuilder.ToGodot(kl.Value);
        }
        string tag = pony ? $"pony{index}" : "kfm";
        var att = new BoneAttachment3D { Name = $"eq{item.Id}_{tag}_attach", BoneName = bone };
        skel.AddChild(att);
        var root = mb.Build(md, $"eq{item.Id}_{tag}");
        // the body skeleton is in file space (Z-up), so the built model's own Z-up turn is undone
        root.Transform = new Transform3D(new Basis(Vector3.Right, -Mathf.Pi / 2), Vector3.Zero).AffineInverse();
        att.AddChild(root);
        var ps = root.GetNode<Skeleton3D>("Model/" + ModelBuilder.SkeletonName);
        if (!pony && item.Slot is EquipSlot.RH or EquipSlot.LH)
            kfmWeapons.GetOrCreateValue(skel)[item.Slot] = ps;
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
        // ModelBuilder.Build names each mesh node after its ModelMesh
        foreach (var mi in made)
            if (md.Meshes.FirstOrDefault(x => x.Name == mi.Name.ToString()) is { } mm) part.Picks.Add((mi, mm));
        return made;
    }

    /// <summary>The marker a worn item's self node carries: the node's name in the item file (<see cref="SelfNodes"/>).</summary>
    public static readonly StringName ItemNodeMeta = "item_node";

    /// <summary>
    /// The item's self nodes that are not meshes (CP of a cap whose meshes hang under it), each an empty node under the attach
    /// node at its local transform there (<paramref name="onAttach"/> times the node's own, as its meshes are placed),
    /// named in <see cref="ItemNodeMeta"/>: an attach type 2 effect part names such a node (FUN_14154d410 walks the
    /// owner's nodes for the name) and hangs on its parent at it. Freed with the attach node.
    /// </summary>
    static void SelfNodes(Part part, BoneAttachment3D att, ModelData md, string selfName, Transform3D onAttach)
    {
        bool Self(int b) => md.Bones[b].Name != null && md.Bones[b].Name.StartsWith(selfName, StringComparison.Ordinal);
        bool Under(int b) { for (int u = md.Bones[b].Parent; u >= 0; u = md.Bones[u].Parent) if (Self(u)) return true; return false; }
        for (int b = 0; b < md.Bones.Count; b++)
        {
            if (!Self(b) || Under(b)) continue;
            string name = md.Bones[b].FileName ?? md.Bones[b].Name;
            var n = new Node3D { Name = $"{att.Name}_node_{name}".ValidateNodeName(), Transform = onAttach * ModelBuilder.ToGodot(md.Bones[b].Rest) };
            n.SetMeta(ItemNodeMeta, name);
            att.AddChild(n);
            part.Roots.Add(new SelfRoot { Key = "bone:" + md.Bones[b].Name, Node = n, Prefix = onAttach, Local = ModelBuilder.ToGodot(md.Bones[b].Rest), Rel = Transform3D.Identity });
        }
    }

    /// <summary>
    /// A rigid mesh relative to its self node's attach point (the client's self-node collection): the outermost node on
    /// the mesh's path, the mesh included, whose name starts with selfName keeps its local transform and carries the
    /// rest: the root (Key), its local transform and the mesh relative to it (the placement is Local * Rel). Null when no
    /// node on the path matches.
    /// </summary>
    static (string Key, Transform3D Local, Transform3D Rel)? SelfRootOf(ModelData md, ModelMesh m, string selfName)
    {
        if (string.IsNullOrEmpty(selfName)) return null;
        var local = ModelBuilder.ToGodot(m.Local);
        var parentWorld = m.ParentBone >= 0 ? ModelBuilder.ToGodot(md.Bones[m.ParentBone].World) : Transform3D.Identity;
        if (md.RootName == selfName) return ("root", ModelBuilder.ToGodot(md.RootLocal), parentWorld * local);
        int top = -1;
        for (int b = m.ParentBone; b >= 0; b = md.Bones[b].Parent)
            if (md.Bones[b].Name != null && md.Bones[b].Name.StartsWith(selfName, StringComparison.Ordinal)) top = b;
        if (top >= 0)
            return ("bone:" + md.Bones[top].Name, ModelBuilder.ToGodot(md.Bones[top].Rest),
                ModelBuilder.ToGodot(md.Bones[top].World).AffineInverse() * parentWorld * local);
        if (m.FileName != null && m.FileName.StartsWith(selfName, StringComparison.Ordinal)) return ("mesh:" + m.Name, local, Transform3D.Identity);
        return null;
    }
}
