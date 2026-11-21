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
/// The user handling is the ReplayField's, without the recording's camera; the casts are RemoteLayer.Casts.cs.
/// </summary>
public sealed partial class RemoteLayer : Node3D
{
    sealed class Remote
    {
        public Node3D Root, Visual; public AnimationPlayer Anim;
        public Vector3 Target; public float TargetYaw; public string Name, Current = "";
        public Vector3 LastPos; public float Speed;
        public int ObjectId; public long CharacterId; public string Picture = "";
        public long HpCur, HpMax; public bool InBattle;         // PCBase's stat pair and battle stance (m_unk_160)
        public PcHpBar Bar;
        public byte Gender;
        /// <summary>The actor's cast record and skill state (RemoteLayer.Casts.cs), and the seconds its motion still plays.</summary>
        public ActorCast Cast; public float CastLeft;
        public Func<Vector3> Anchor;
        /// <summary>FieldAddUser's worn gem items (PCGemEquip::ReadFromStream of the packet).</summary>
        public IReadOnlyList<Ms2.Net.Packets.EquipItem> Gems = Array.Empty<Ms2.Net.Packets.EquipItem>();
        /// <summary>FieldAddUser's premium club time and wedding state (PC m_n1190 / m_n1220) and the user block's PC bang
        /// grade (m_nFF8, scenestategame.cpp:11151), the PC::CanUseItem inputs of the balloon gem.</summary>
        public long PremiumTime; public short WeddingState; public byte PcBangGrade;
    }

    readonly ModelBuilder builder;
    readonly EquipApplier equip;
    readonly NpcLayer npcs;
    readonly CombatLayer combat;
    readonly LiveNameTags tags;
    readonly LiveHpBars bars;
    readonly PackSet packs;
    readonly Dictionary<int, Remote> users = new();
    readonly Dictionary<byte, (ModelData md, Godot.Animation idle, Godot.Animation walk, Godot.Animation run)> bodies = new();
    readonly long selfCharacter;
    public readonly FieldTracker Tracker = new();
    public int UsersSpawned, NpcsAdded;
    public int UserCount => users.Count;
    public int NpcCount => npcs.Count;
    /// <summary>The field's live npcs for the talk.</summary>
    public IEnumerable<World.FieldNpcs.Placed> TalkNpcs => npcs.Talkable;

    /// <summary>One other player in the field: its object, name, picture key, root, Eff_UI anchor, worn gems and the
    /// PC::CanUseItem state of its balloon gem (premium time, PC bang grade, wedding state).</summary>
    public readonly record struct Pc(int ObjectId, long CharacterId, string Name, string Picture, Node3D Root, Func<Vector3> Anchor,
        IReadOnlyList<Ms2.Net.Packets.EquipItem> Gems, long PremiumTime, byte PcBangGrade, short WeddingState);
    /// <summary>The other players shown now (the own PC is not among them).</summary>
    public IEnumerable<Pc> Pcs => users.Values.Select(r => new Pc(r.ObjectId, r.CharacterId, r.Name ?? "", r.Picture, r.Root, r.Anchor, r.Gems,
        r.PremiumTime, r.PcBangGrade, r.WeddingState));

    public RemoteLayer(Field field, ModelBuilder mb, long selfCharacterId)
    {
        Name = "RemoteLayer";
        selfCharacter = selfCharacterId;
        field.AddChild(this);
        builder = mb;
        // the field build searched map textures only; the character bodies need the character pack's own textures too
        if (!mb.TextureArchives.Contains(ModelBuilder.ModelArchive)) mb.TextureArchives = mb.TextureArchives.Prepend(ModelBuilder.ModelArchive).ToArray();
        var packs = this.packs = field.Packs;
        equip = new EquipApplier(packs, mb) { Palettes = new Look.ColorPalettes(packs) };
        var table = new Ms2.Formats.World.NpcTable(packs);
        Tracker.NpcLayoutOf = id => { var r = table.Find(id); return r == null ? default : new NpcLayout(r.IsBoss, r.HasDamagedHpBarTag); };
        npcs = new NpcLayer(packs, mb, table, this);
        combat = new CombatLayer(packs, mb, new EffectTable(packs), this);
        npcs.Combat = combat;
        field.AddChild(tags = new LiveNameTags(packs)
        {
            InParty = id => LiveSession.Instance?.Party is { } p && p.PartyId != 0 && p.Find(id) != null,
        });
        npcs.Tags = tags;
        field.AddChild(bars = new LiveHpBars(packs)
        {
            // FUN_1419923f0 re-reads the party per frame for a player's bar; the same lookup the client's IsMemberByCharId does
            InParty = id => LiveSession.Instance?.Party is { PartyId: not 0 } p && p.Find(id) != null,
        });
        npcs.Bars = bars;
    }

    static Vector3 G(System.Numerics.Vector3 p) => new(p.X, p.Z, -p.Y);

    /// <summary>A damage number to show (UIDamageText::FUN_141911e90): the number and the npc hit, null when the target is
    /// not an npc.</summary>
    public Action<Ms2.Ui.Hud.DamageNumber, World.FieldNpcs.Placed> Numbered;
    readonly List<Ms2.Ui.Hud.DamageNumber> numbers = new();
    Func<int, int> pcMask;
    static readonly Ms2.Formats.Options.GameOption.Battle defaultBattle = new();

    /// <summary>
    /// PCBase::FUN_141671c20's vfunc_111 per hit (<see cref="Ms2.Ui.Hud.DamageNumbers.Records"/>) through UIDamageText's
    /// show test (FUN_141913bd0) with the battle options; the party mask is IsMemberByCharId on a player's character.
    /// STILL-OPEN: a number on another player (DamageText has no remote-PC target yet) is dropped.
    /// </summary>
    void ShowNumbers(SkillDamage d)
    {
        numbers.Clear();
        pcMask ??= PcMask;
        Ms2.Ui.Hud.DamageNumbers.Records(d, numbers);
        var o = GameOptions.Current?.BattleOption ?? defaultBattle;
        foreach (var n in numbers)
            if (Ms2.Ui.Hud.DamageNumbers.Shows(o, n.Grade, n.TargetId, n.CasterId, Tracker.SelfObjectId, pcMask))
                Numbered(n, npcs.Placed(n.TargetId));
    }

    int PcMask(int id) => !users.TryGetValue(id, out var r) ? 0
        : LiveSession.Instance?.Party is { PartyId: not 0 } p && p.Find(r.CharacterId) != null ? 2 : 4;

    /// <summary>
    /// The other players' and the npcs' bodies for an own attack's area pick, in client space (z up): a player's body box is
    /// PC::SetDefaultBoundingBox's (<see cref="Ms2.Ui.Skills.SkillHit.PlayerBody"/>), the npcs' come from
    /// <see cref="NpcLayer.HitCandidates"/>.
    /// </summary>
    public void HitCandidates(List<Ms2.Ui.Skills.HitCandidate> into)
    {
        foreach (var (id, r) in users)
        {
            if (r.Root == null) continue;
            var g = r.Root.GlobalPosition;
            var p = new System.Numerics.Vector3(g.X, -g.Z, g.Y);
            into.Add(new Ms2.Ui.Skills.HitCandidate { Id = id, Position = p, Body = Ms2.Ui.Skills.SkillHit.PlayerBody(p, r.TargetYaw) });
        }
        npcs.HitCandidates(into);
    }

    /// <summary>An effect xml played at a point (CombatLayer.PlayEffect), the own cast's motion effect.</summary>
    public void PlayEffect(string effect, Vector3 at, float yawRadians) => combat.PlayEffect(effect, at, yawRadians);

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
        // the other player's CObjectShadow, of the PC row (PC::vfunc_49, pc.cpp:3028-3035; the same size as the player's)
        FieldShadows.Character(root, visual, Ms2.Formats.Options.GraphicOptionTable.PC, 100f, 3f);
        tags.AddPc(root, visual.GetNodeOrNull<Node3D>("Model"), md.RootName, u, false);
        var r = new Remote { Root = root, Visual = visual, Anim = anim, Name = u.User.Name, Target = G(u.Position), TargetYaw = u.Rotation.Z };
        r.ObjectId = u.ObjectId; r.CharacterId = u.User.CharacterId; r.Picture = u.User.Picture ?? ""; r.Gems = u.Badges.ToList();
        r.Gender = u.User.Gender;
        r.PremiumTime = u.PremiumTime; r.WeddingState = u.WeddingState; r.PcBangGrade = u.User.PcBangGrade;
        r.HpCur = u.Stats[2].Health; r.HpMax = u.Stats[0].Health; r.InBattle = u.InBattle;
        var dummies = Ms2.Formats.World.EffectDummyTable.Load(packs).Pc(u.User.Gender);
        var model = visual.GetNodeOrNull<Node3D>("Model");
        r.Anchor = () => World.DummyAnchor.Point(dummies, "Eff_UI", model, md.RootName) ?? root.GlobalPosition;
        root.Position = r.Target; r.LastPos = r.Target;
        visual.Rotation = new Vector3(0, Mathf.DegToRad(r.TargetYaw), 0);
        anim.Play("idle"); r.Current = "idle";
        // PC::vfunc_10 (a hurt spawn) and OnAddUser's SetEmotion of the packet's emotion byte (scenestategame.cpp:11188,
        // a change from the ctor's 0): either runs UpdateHpBar, so a member's bar is made active at once (not shown)
        if (r.HpCur != r.HpMax || r.InBattle) EnsureBar(r)?.Spawn(r.HpCur, r.HpMax, r.InBattle);
        return r;
    }

    /// <summary>PC::vfunc_80's gate: the party gauge option on (the status option the client reads at +0xc), in a party,
    /// and one of its members (MyPC::IsMemberByCharId).</summary>
    bool CanPcBar(long characterId) =>
        bars.PartyGauge && LiveSession.Instance?.Party is { PartyId: not 0 } p && p.Find(characterId) != null;

    /// <summary>The member's bar, made on demand (PC::vfunc_80 is called from UpdateHpBar, OnStat and a hit, so the
    /// entity is made at the first of those, not at the spawn).</summary>
    PcHpBar EnsureBar(Remote r) => r.Bar ??= CanPcBar(r.CharacterId) ? bars.AddPc(r.Root, r.Anchor, r.CharacterId) : null;

    void Drain()
    {
        int budget = 200;
        while (budget-- > 0 && Tracker.Events.TryDequeue(out var ev))
        {
            switch (ev)
            {
                case UserAddedEvent add:
                    if (add.User.User.CharacterId == selfCharacter) break;
                    if (users.Remove(add.User.ObjectId, out var old)) { tags.Remove(old.Root); bars.Remove(old.Root); old.Root.QueueFree(); }
                    users[add.User.ObjectId] = Spawn(add.User); UsersSpawned++;
                    GD.Print($"live: other player '{add.User.User.Name}' object {add.User.ObjectId} shown at {add.User.Position}");
                    break;
                case UserRemovedEvent rem:
                    if (users.Remove(rem.ObjectId, out var gone))
                    {
                        tags.Remove(gone.Root); bars.Remove(gone.Root); gone.Root.QueueFree();
                        GD.Print($"live: other player {gone.Name} (object {rem.ObjectId}) left");
                    }
                    break;
                case UserMovedEvent mv:
                    if (!users.TryGetValue(mv.ObjectId, out var r)) break;
                    r.Target = G(mv.Position); r.TargetYaw = mv.YawDegrees;
                    if (++movedSeen <= 3 || movedSeen % 100 == 0) GD.Print($"live: other player {r.Name} moved #{movedSeen} to {mv.Position} yaw {mv.YawDegrees}");
                    break;
                case UserStatEvent st:
                    // OnStat (vfunc_74 -> FUN_141992230): an hp change shows the member's bar
                    if (!users.TryGetValue(st.Stat.ObjectId, out var su)) break;
                    if (st.Stat.TryGet(PcStat.Health, out long hpMax, out long hpCur)) { su.HpMax = hpMax; su.HpCur = hpCur; }
                    EnsureBar(su)?.Stat(su.HpCur, su.HpMax);
                    break;
                case UserBattleEvent bat:
                    // OnUserBattle -> SetEmotion: the change guard (pcbase.cpp:1268) runs UpdateHpBar only on a change;
                    // the state 0xe skip (scenestategame.cpp:3269) is not ported (no remote state here). STILL-OPEN: the
                    // ridee's and the own target's emotion (spRidee / FUN_141bd3620) need ridees and pets.
                    if (!users.TryGetValue(bat.ObjectId, out var sb)) break;
                    if (sb.InBattle == (bat.Emotion != 0)) break;
                    sb.InBattle = bat.Emotion != 0;
                    EnsureBar(sb)?.Stance(sb.InBattle, sb.HpCur, sb.HpMax);
                    break;
                case SkillDamageEvent dmg:
                    // FUN_141671c20 (pcbase.cpp:3922) calls vfunc_112 whenever the target has an entry, a miss (type 2) too
                    // (AddDamage keeps every pair, skillrecord.cpp:554) and whatever the total; the skill's HasDamage
                    // (skillattackdata.cpp:384) is the real gate and is not read here (STILL-OPEN)
                    bool byLocal = dmg.Damage.OwnerId == Tracker.SelfObjectId;
                    foreach (var t in dmg.Damage.Targets)
                    {
                        if (t.Hits.Length == 0) continue;
                        npcs.Damaged(t.TargetId, byLocal);
                        // PCBase::vfunc_112 goes through vfunc_80: a local hit makes the member's bar, then shows it
                        if (users.TryGetValue(t.TargetId, out var hit)) EnsureBar(hit)?.Hit(byLocal);
                    }
                    if (Numbered != null) ShowNumbers(dmg.Damage);
                    break;
                case SkillUsedEvent used: OnSkillUse(used.Use); break;
                case SkillSyncedEvent ss: OnSkillSync(ss.Sync); break;
                case SkillCancelledEvent sc: OnSkillCancel(sc); break;
                case NpcHpEvent hp: npcs.SetHp(hp.ObjectId, hp.Current, hp.Max); break;
                case NpcStanceEvent st: npcs.Stance(st.ObjectId, st.Battle); break;
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
            else a.Root.Position += d * FrameRate.Ease(12f / FrameRate.Reference, dt);   // the share dt * 12 gave at 60 fps, per second at any rate
            float cur = a.Visual.Rotation.Y;
            a.Visual.Rotation = new Vector3(0, Mathf.LerpAngle(cur, Mathf.DegToRad(a.TargetYaw), FrameRate.Ease(14f / FrameRate.Reference, dt)), 0);
            float sp = (a.Root.Position - a.LastPos).Length() / Mathf.Max(dt, 1e-4f);
            a.Speed = Mathf.Lerp(a.Speed, sp, FrameRate.Ease(8f / FrameRate.Reference, dt));
            a.LastPos = a.Root.Position;
            if (CastHolds(a, dt)) continue;
            string anim = a.Speed < 25 ? "idle" : a.Speed < 220 ? "walk" : "run";
            if (anim != a.Current) { a.Anim.Play(anim, 0.15); a.Current = anim; }
        }
        npcs.Process(dt);
        combat.Process(dt);
    }
}
