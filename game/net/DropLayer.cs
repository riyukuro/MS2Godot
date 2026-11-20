using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;

namespace Ms2Godot.Net;

/// <summary>
/// The dropped items and meso of a live field (the client's CDropItem objects): FieldAddItem makes one drop (its model, its icon, its throw),
/// FieldRemoveItem frees it. A drop is built once; each frame moves the nodes through <see cref="DropMotion"/> without allocating.
/// </summary>
public sealed partial class DropLayer : Node3D
{
    static readonly string[] ModelArchives = { "Resource/Model/Item", "Resource/Model/Effect", "Resource/Model/Map" };
    const string IconArchive = "Resource/Image";
    const string UrnPrefix = "urn:gamebryo-scenegraph:";

    sealed class Drop
    {
        public Node3D Root;
        public DropMotion Motion;
        public FieldAddItem Packet;
    }

    readonly PackSet packs;
    readonly ModelBuilder builder;
    readonly ItemDropTable table;
    readonly DropConstants constants;
    readonly long ownCharacterId;
    readonly Func<int, bool> known, isPet;
    readonly Func<int, string> iconPath;
    readonly Dictionary<int, Drop> drops = new();
    readonly Dictionary<string, ModelData> models = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Texture2D> icons = new();
    readonly Func<System.Numerics.Vector3, float?> ground;
    readonly List<Drop> gone = new();
    PhysicsRayQueryParameters3D ray;
    public int Count => drops.Count;
    /// <summary>FieldAddItems the client would not make a drop of (not the owner, no item data, no style, no model).</summary>
    public int Skipped, NoModel;

    public DropLayer(Field field, ModelBuilder builder, long ownCharacterId, Func<int, bool> known, Func<int, bool> isPet, Func<int, string> iconPath)
    {
        Name = "DropLayer";
        packs = field.Packs;
        this.builder = builder; this.ownCharacterId = ownCharacterId; this.known = known; this.isPet = isPet; this.iconPath = iconPath;
        table = new ItemDropTable(packs);
        constants = DropConstants.From(Ms2.Ui.FeatureElements.Constants(packs));
        ground = GroundAt;
        field.AddChild(this);
    }

    static Vector3 G(System.Numerics.Vector3 p) => new(p.X, p.Z, -p.Y);

    /// <summary>The downward ray cast of the drop's throw (CPhysXHelper::CastRay): the ground height (client z) under a client-space point, or null.</summary>
    float? GroundAt(System.Numerics.Vector3 p)
    {
        var from = G(p);
        ray ??= PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 4000f);
        ray.From = from; ray.To = from + Vector3.Down * 4000f;
        using var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
        return hit.Count == 0 ? null : ((Vector3)hit[PhysicsKeys.Position]).Y;
    }

    /// <summary>CSceneStateGame::OnFieldAddItem: InitDrop's tests, then the object at the packet position raised by 70.</summary>
    public void Add(FieldAddItem d)
    {
        Remove(d.ObjectId);
        if (!d.IsPickupAllowed(ownCharacterId) || !known(d.ItemId)) { Skipped++; return; }
        bool pet = !d.IsMeso && isPet(d.ItemId);
        var look = table.Look(d.ItemId, d.Amount, d.Rarity, pet);
        if (look == null) { Skipped++; return; }
        var md = Model(look.Asset);
        if (md == null) { NoModel++; return; }
        var visual = builder.Build(md, "DropItemNIF");
        float scale = look.Style.Scale;
        visual.Scale = new Vector3(scale, scale, scale);
        if (!d.IsMeso) ApplyIcon(visual, iconPath(d.ItemId));
        var root = new Node3D { Name = $"Drop{d.ObjectId}" };
        root.AddChild(visual);
        AddChild(root);
        var motion = new DropMotion(constants, look.Style, d.ObjectId, pet);
        motion.Launch(DropMotion.Created70(d.Position), DropLaunch.Default, d.FixedThrow, d.IsMeso, ground);
        root.Position = G(motion.Position);
        drops[d.ObjectId] = new Drop { Root = root, Motion = motion, Packet = d };
    }

    /// <summary>OnFieldRemoveItem: the object leaves the world.</summary>
    public void Remove(int objectId)
    {
        if (!drops.Remove(objectId, out var drop)) return;
        drop.Root.QueueFree();
    }

    ModelData Model(string asset)
    {
        if (string.IsNullOrEmpty(asset)) return null;
        if (models.TryGetValue(asset, out var md)) return md;
        md = null;
        string file = asset + ".nif";
        foreach (var archive in ModelArchives)
        {
            string entry = packs.Exists(archive, file.ToLowerInvariant()) ? file.ToLowerInvariant() : packs.FindByFileName(archive, file);
            if (entry == null) continue;
            try { md = builder.LoadModel(archive, entry); break; }
            catch (Exception e) { GD.PushWarning($"drop model {asset}: {e.Message}"); }
        }
        return models[asset] = md;
    }

    /// <summary>CDropItem::SetIconTexture (dropitem.cpp:1011): the item's icon on the model's "Item_Icon:0" mesh.</summary>
    void ApplyIcon(Node3D visual, string path)
    {
        var mesh = Find(visual);
        if (mesh == null || string.IsNullOrEmpty(path)) return;
        var tex = Icon(path);
        if (tex == null) return;
        mesh.MaterialOverride = new StandardMaterial3D { AlbedoTexture = tex, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor };
    }

    static MeshInstance3D Find(Node n)
    {
        if (n is MeshInstance3D m && m.Name.ToString().StartsWith("Item_Icon", StringComparison.OrdinalIgnoreCase)) return m;
        foreach (var c in n.GetChildren()) if (Find(c) is { } f) return f;
        return null;
    }

    Texture2D Icon(string path)
    {
        string entry = path.ToLowerInvariant();
        int at = entry.IndexOf("resource/image/", StringComparison.Ordinal);
        if (at >= 0) entry = entry[(at + "resource/image/".Length)..];
        if (icons.TryGetValue(entry, out var t)) return t;
        t = null;
        try
        {
            if (packs.Exists(IconArchive, entry))
            {
                var img = new Image();
                if (img.LoadPngFromBuffer(packs.Read(IconArchive, entry)) == Error.Ok) t = ImageTexture.CreateFromImage(img);
            }
        }
        catch (Exception e) { GD.PushWarning($"drop icon {entry}: {e.Message}"); }
        return icons[entry] = t;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        foreach (var d in drops.Values)
        {
            d.Motion.Advance(dt, null);
            if (d.Motion.Done) { gone.Add(d); continue; }
            d.Root.Position = G(d.Motion.Position);
            var s = d.Motion.Spin;
            d.Root.Rotation = new Vector3(Mathf.DegToRad(s.X), Mathf.DegToRad(s.Z), Mathf.DegToRad(-s.Y));
        }
        if (gone.Count == 0) return;
        foreach (var d in gone) Remove(d.Packet.ObjectId);
        gone.Clear();
    }
}
