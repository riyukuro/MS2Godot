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
        public uint LastRequest;     // CDropItem m_nTick (FUN_141191030)
        public int PickerId;         // CDropItem m_n188
        public uint FadeUntil;       // CDropItem m_nFadeTime
        public bool Held;            // state 6 kept until FadeUntil (vfunc_53, dropitem.cpp:1137-1150): not launched yet
        public bool Hidden;          // vfunc_23(1) until FadeUntil
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
    readonly PickupPlanner planner = new();
    readonly List<Drop> planned = new();
    readonly List<int> mesoIds = new();
    uint globalUntil;                 // s_kPickTimer: the 10 ms it starts with, 150 ms after each pickup
    bool keyWasDown;

    /// <summary>The local player's position in client coordinates; null without one.</summary>
    public Func<System.Numerics.Vector3?> PlayerPosition = () => null;
    /// <summary>An actor's position by object id (the picker a drop flies to); null when it is gone.</summary>
    public Func<int, System.Numerics.Vector3?> ActorPosition = _ => null;
    public Action<Ms2.Net.Wire.IOutPacket> Send = _ => { };
    /// <summary>A drop reached its picker (CDropItem::SetState(3), the hover's start): the drop's packet and the picker's object id.</summary>
    public Action<FieldAddItem, int> Reached;
    /// <summary>The "get" action key is held.</summary>
    public Func<bool> GetKeyDown = () => false;
    public Func<int, int> TabOf = _ => 7;
    public Func<int, bool> HasEmptyCell = _ => true;
    public Func<FieldAddItem, bool> CanStack = _ => false;
    /// <summary>The player cannot pick up (dead, or riding a mount that disables it): MyPC::FUN_141530db0's first tests.</summary>
    public Func<bool> PickupBlocked = () => false;
    public Func<uint> Clock = () => (uint)Time.GetTicksMsec();
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
        globalUntil = Clock() + DropPickup.AfterCreateMs;   // the CDropItem ctor: s_kPickTimer.Start(10), whether or not the drop is made
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
        uint fade = DropPickup.FadeUntil(Clock(), d.DropKind, pet, d.PetTrap, constants.PetTrapVisibleDelayMs);
        var drop = new Drop { Root = root, Motion = motion, Packet = d, FadeUntil = fade, Held = fade != 0 && !pet, Hidden = fade != 0 };
        root.Visible = !drop.Hidden;
        if (drop.Held) root.Position = G(DropMotion.Created70(d.Position));
        else LaunchDrop(drop);
        drops[d.ObjectId] = drop;
    }

    /// <summary>SetState(1): the throw from the packet position raised by 70.</summary>
    void LaunchDrop(Drop drop)
    {
        drop.Motion.Launch(DropMotion.Created70(drop.Packet.Position), DropLaunch.Default, drop.Packet.FixedThrow, drop.Packet.IsMeso, ground);
        drop.Root.Position = G(drop.Motion.Position);
    }

    /// <summary>OnFieldRemoveItem (scenestategame.cpp:2402): a drop being picked up is kept, any other leaves.</summary>
    public void OnRemovePacket(int objectId)
    {
        if (drops.TryGetValue(objectId, out var d) && DropPickup.KeepsOnRemove(d.PickerId, d.Motion.State)) return;
        Remove(objectId);
    }

    /// <summary>What OnFieldPickupItem reads the packet by: whether the field has the drop, and its kind.</summary>
    public (bool Known, bool IsMeso, bool IsEp) Describe(int objectId) =>
        drops.TryGetValue(objectId, out var d) ? (true, d.Packet.IsMeso, d.Packet.ItemId == FieldPickupItem.EpItemId) : (false, false, false);

    /// <summary>
    /// OnFieldPickupItem (scenestategame.cpp:2420) as far as it moves the drop: a pickup sends a resting drop to its picker
    /// (CDropItem::OnPickedUp, which also starts the shared pick timer); a refusal changes nothing the drop shows.
    /// </summary>
    public void OnPickupPacket(FieldPickupItem p)
    {
        if (!p.DropKnown || !p.Picked || !drops.TryGetValue(p.ObjectId, out var d)) return;
        if (d.Motion.State != DropMotion.Rest) return;
        d.PickerId = p.PickerId;
        globalUntil = Clock() + DropPickup.AfterPickupMs;
        var at = ActorPosition(p.PickerId) ?? d.Motion.Position;
        d.Motion.PickedUp(at, (uint)Random.Shared.NextInt64(0, uint.MaxValue));
    }

    /// <summary>OnFieldRemoveItem (also the internal removal): the object leaves the world.</summary>
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

    /// <summary>CDropItem::SetIconTexture (dropitem.cpp:386): the item's icon on the model's "Item_Icon:0" mesh.</summary>
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
        for (int i = 0, count = n.GetChildCount(); i < count; i++) if (Find(n.GetChild(i)) is { } f) return f;
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
            if (d.Hidden)
            {
                // a pet trap's drop: hidden, and a non-pet one not even thrown, until its delay ends (vfunc_53)
                if (DropPickup.StillHidden(Clock(), d.FadeUntil)) { if (d.Held) continue; }
                else { d.Hidden = false; d.Root.Visible = true; if (d.Held) { d.Held = false; LaunchDrop(d); } }
            }
            System.Numerics.Vector3? picker = d.PickerId != 0 && d.Motion.State is DropMotion.FlyToPicker or DropMotion.Hover or DropMotion.Sink
                ? ActorPosition(d.PickerId) : null;
            int was = d.Motion.State;
            d.Motion.Advance(dt, picker);
            if (was != DropMotion.Hover && d.Motion.State == DropMotion.Hover && d.PickerId != 0) Reached?.Invoke(d.Packet, d.PickerId);
            if (d.Motion.Done) { gone.Add(d); continue; }
            d.Root.Position = G(d.Motion.Position);
            var s = d.Motion.Spin;
            d.Root.Rotation = new Vector3(Mathf.DegToRad(s.X), Mathf.DegToRad(s.Z), Mathf.DegToRad(-s.Y));
        }
        bool down = GetKeyDown();
        if (down && drops.Count > 0) TryPickup(!keyWasDown);
        keyWasDown = down;
        if (gone.Count == 0) return;
        foreach (var d in gone) Remove(d.Packet.ObjectId);
        gone.Clear();
    }

    /// <summary>
    /// MyPC::FUN_141530db0 with the "get" key down (mypc.cpp:4740): every meso in range goes in one RequestMesoPickup, then the nearest
    /// drop in item range is requested when the bag takes it (or the key was just pressed). Auto loot (the option-driven flagging) is not ported.
    /// </summary>
    void TryPickup(bool pressed)
    {
        if (PlayerPosition() is not { } me || PickupBlocked()) return;
        uint now = Clock();
        planner.Clear(); planned.Clear();
        foreach (var d in drops.Values)
        {
            planner.Candidates.Add(new PickupCandidate
            {
                ObjectId = d.Packet.ObjectId, State = d.Motion.State, LastRequest = d.LastRequest, FadeUntil = d.FadeUntil,
                Distance = (me - d.Motion.Position).Length(), Meso = d.Packet.IsMeso, Owner = d.Packet.IsPickupAllowed(ownCharacterId),
            });
            planned.Add(d);
        }
        planner.Plan(now, globalUntil, constants.MaxLength, constants.MoneyMaxLength, RequestMesoPickup.MaxCount);
        if (planner.Mesos.Count > 0)
        {
            mesoIds.Clear();
            foreach (int i in planner.Mesos) { planned[i].LastRequest = now; mesoIds.Add(planned[i].Packet.ObjectId); }
            Send(new RequestMesoPickup(mesoIds.ToArray()));
        }
        if (planner.Nearest >= 0)
        {
            var nearest = planned[planner.Nearest];
            if (DropPickup.BagAllows(HasEmptyCell(0), CanStack(nearest.Packet), pressed)) Request(nearest, now);
        }
        else if (planner.Hop >= 0) planned[planner.Hop].Motion.PickFailed();   // FUN_1411911a0 (the 0x45 notice is not ported)
    }

    /// <summary>CDropItem::FUN_141190900 (dropitem.cpp:950): the request, the drop hopping back when the bag cannot take it.</summary>
    void Request(Drop d, uint now)
    {
        d.LastRequest = now;
        // a meso id with no amount still takes the client's currency branch: no hop
        if (!FieldAddItem.IsMesoId(d.Packet.ItemId) && !HasEmptyCell(TabOf(d.Packet.ItemId)) && !CanStack(d.Packet)) d.Motion.PickFailed();
        Send(new RequestItemPickup(d.Packet.ObjectId, 0));
    }
}
