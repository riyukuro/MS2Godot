using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Replay;

namespace Ms2Godot.Net;

/// <summary>
/// The other players and the npcs of a live field: FieldTracker events (from the game connection) become stand-in bodies that glide to
/// the latest UserSync position, and NpcLayer instances. The own character is skipped (the walkable Player is already in the field).
/// The user handling is the ReplayField's, without skills or the recording's camera.
/// </summary>
public sealed partial class RemoteLayer : Node3D
{
    sealed class Remote
    {
        public Node3D Root, Visual; public AnimationPlayer Anim;
        public Vector3 Target; public float TargetYaw; public string Name, Current = "";
        public Vector3 LastPos; public float Speed;
    }

    readonly ModelBuilder builder;
    readonly EquipApplier equip;
    readonly NpcLayer npcs;
    readonly CombatLayer combat;
    readonly Dictionary<int, Remote> users = new();
    readonly Dictionary<byte, (ModelData md, Godot.Animation idle, Godot.Animation walk, Godot.Animation run)> bodies = new();
    readonly long selfCharacter;
    public readonly FieldTracker Tracker = new();
    public int UsersSpawned, NpcsAdded;
    public int UserCount => users.Count;
    public int NpcCount => npcs.Count;

    public RemoteLayer(Field field, ModelBuilder mb, long selfCharacterId)
    {
        Name = "RemoteLayer";
        selfCharacter = selfCharacterId;
        field.AddChild(this);
        builder = mb;
        // the field build searched map textures only; the character bodies need the character pack's own textures too
        if (!mb.TextureArchives.Contains(ModelBuilder.ModelArchive)) mb.TextureArchives = mb.TextureArchives.Prepend(ModelBuilder.ModelArchive).ToArray();
        var packs = field.Packs;
        equip = new EquipApplier(packs, mb) { Palettes = new Look.ColorPalettes(packs) };
        var table = new Ms2.Formats.World.NpcTable(packs);
        Tracker.NpcLayoutOf = id => { var r = table.Find(id); return r == null ? default : new NpcLayout(r.IsBoss, r.HasDamagedHpBarTag); };
        npcs = new NpcLayer(packs, mb, table, this);
        combat = new CombatLayer(packs, mb, new EffectTable(packs), this);
        npcs.Combat = combat;
    }

    static Vector3 G(System.Numerics.Vector3 p) => new(p.X, p.Z, -p.Y);

    (ModelData md, Godot.Animation idle, Godot.Animation walk, Godot.Animation run) Body(byte gender)
    {
        if (bodies.TryGetValue(gender, out var b)) return b;
        string dir = gender == 0 ? "male" : "female", nif = gender == 0 ? "m_body.nif" : "f_body.nif";
        var md = builder.LoadModel($"{dir}/{nif}");
        return bodies[gender] = (md, ModelBuilder.BuildAnimation(builder.LoadKf($"{dir}/idle_a.kf"), md), ModelBuilder.BuildAnimation(builder.LoadKf($"{dir}/walk_a.kf"), md),
                                 ModelBuilder.BuildAnimation(builder.LoadKf($"{dir}/run_a.kf"), md));
    }

    Remote Spawn(FieldAddUser u)
    {
        var (md, idle, walk, run) = Body(u.User.Gender);
        var visual = builder.Build(md, u.User.Gender == 0 ? "Male" : "Female");
        equip.Apply(visual, md, u.User.Gender, u.Equips, u.Skin.Primary, u.Skin.Secondary);
        var anim = visual.GetNode<AnimationPlayer>("Model/AnimationPlayer");
        var lib = anim.GetAnimationLibrary("");
        lib.AddAnimation("idle", idle); lib.AddAnimation("walk", walk); lib.AddAnimation("run", run);
        var root = new Node3D { Name = $"Pc{u.ObjectId}" };
        root.AddChild(visual);
        AddChild(root);
        combat.NameTag(root, u.User.Name, 135, Colors.White);
        var r = new Remote { Root = root, Visual = visual, Anim = anim, Name = u.User.Name, Target = G(u.Position), TargetYaw = u.Rotation.Z };
        root.Position = r.Target; r.LastPos = r.Target;
        visual.Rotation = new Vector3(0, Mathf.DegToRad(r.TargetYaw), 0);
        anim.Play("idle"); r.Current = "idle";
        return r;
    }

    void Drain()
    {
        int budget = 200;
        while (budget-- > 0 && Tracker.Events.TryDequeue(out var ev))
        {
            switch (ev)
            {
                case UserAddedEvent add:
                    if (add.User.User.CharacterId == selfCharacter) break;
                    if (users.Remove(add.User.ObjectId, out var old)) old.Root.QueueFree();
                    users[add.User.ObjectId] = Spawn(add.User); UsersSpawned++;
                    GD.Print($"live: other player '{add.User.User.Name}' object {add.User.ObjectId} shown at {add.User.Position}");
                    break;
                case UserRemovedEvent rem:
                    if (users.Remove(rem.ObjectId, out var gone)) { gone.Root.QueueFree(); GD.Print($"live: other player {gone.Name} (object {rem.ObjectId}) left"); }
                    break;
                case UserMovedEvent mv:
                    if (!users.TryGetValue(mv.ObjectId, out var r)) break;
                    r.Target = G(mv.Position); r.TargetYaw = mv.YawDegrees;
                    if (++movedSeen <= 3 || movedSeen % 100 == 0) GD.Print($"live: other player {r.Name} moved #{movedSeen} to {mv.Position} yaw {mv.YawDegrees}");
                    break;
                case NpcHpEvent hp: npcs.SetHp(hp.ObjectId, hp.Current, hp.Max); break;
                case NpcAddedEvent na: npcs.Add(na); NpcsAdded++; break;
                case NpcRemovedEvent nr: npcs.Remove(nr.ObjectId, nr.Dead); break;
                case NpcMovedEvent nm: npcs.Move(nm); break;
            }
        }
    }

    int movedSeen;

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        Drain();
        foreach (var a in users.Values)
        {
            var d = a.Target - a.Root.Position;
            if (d.Length() > 400) a.Root.Position = a.Target;
            else a.Root.Position += d * Mathf.Min(1f, dt * 12f);
            float cur = a.Visual.Rotation.Y;
            a.Visual.Rotation = new Vector3(0, Mathf.LerpAngle(cur, Mathf.DegToRad(a.TargetYaw), Mathf.Min(1f, dt * 14f)), 0);
            float sp = (a.Root.Position - a.LastPos).Length() / Mathf.Max(dt, 1e-4f);
            a.Speed = Mathf.Lerp(a.Speed, sp, Mathf.Min(1f, dt * 8f));
            a.LastPos = a.Root.Position;
            string anim = a.Speed < 25 ? "idle" : a.Speed < 220 ? "walk" : "run";
            if (anim != a.Current) { a.Anim.Play(anim, 0.15); a.Current = anim; }
        }
        npcs.Process(dt);
        combat.Process(dt);
    }
}
