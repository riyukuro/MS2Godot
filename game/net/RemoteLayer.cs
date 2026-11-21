using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Replay;
using Ms2.Audio;
using Ms2Godot.Movement;

namespace Ms2Godot.Net;

/// <summary>
/// The other players and the npcs of a live field: FieldTracker events (from the game connection) become remote PCs and NpcLayer
/// instances. The own character is skipped (the walkable Player is already in the field). Each remote PC replays its UserSync
/// records through its own state machine and controller (<see cref="RemotePc"/>, Ms2.Net.Replay.RemotePcStates), as the
/// client's PC::vfunc_114 does (scenestategame.cpp:2509-2517), so its states, animations, steps, landings and ground material
/// are its own; the casts are RemoteLayer.Casts.cs.
/// </summary>
public sealed partial class RemoteLayer : Node3D
{
    sealed class Remote
    {
        public RemotePc Pc; public Node3D Root, Visual;
        public string Name;
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
    readonly NpcTable npcTable;
    readonly CombatLayer combat;
    readonly LiveNameTags tags;
    readonly LiveHpBars bars;
    readonly PackSet packs;
    readonly Dictionary<int, Remote> users = new();
    /// <summary>Per gender: the body model, its action sequences and the animation library they are in, shared by the PCs
    /// of that gender (the first one's player loads them).</summary>
    readonly Dictionary<byte, (ModelData md, ActionAnims anims, AnimationLibrary lib)> bodies = new();
    readonly float gravity;
    GroundEffectTable groundTable;
    World.GroundEffects groundFx;
    readonly Random groundRng = new();
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
        npcTable = table;
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
        // GameObjectController::GetGravity: the field's scene gravity, as the field gives the local player
        // STILL-OPEN: a field without the local player (none in a live field) falls back to the default scene gravity
        gravity = field.GetNodeOrNull<Player>("Player")?.Gravity ?? MoveConfig.SceneGravity;
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
    /// <see cref="NpcLayer.HitCandidates"/>. A player is a pc of team 1 with its stat pair's hp, for
    /// <see cref="Ms2.Ui.Skills.SkillHit.FilterTarget"/>.
    /// Its party id is the own party's when CPartyManager has it as a member (PC::FUN_141661bc0, pc.cpp:4438), else 0.
    /// STILL-OPEN: the hidden / state 6-7 flag and HasPvPPoint (false here) are not fed; pvp on/off is unknown
    /// (<see cref="Ms2.Ui.Skills.HitCandidate.PvpUnknown"/>).
    /// </summary>
    public void HitCandidates(List<Ms2.Ui.Skills.HitCandidate> into)
    {
        var party = LiveSession.Instance?.Party;
        foreach (var (id, r) in users)
        {
            if (r.Pc == null) continue;
            var p = r.Pc.Position;
            long partyId = party is { PartyId: not 0 } && party.Find(r.CharacterId) != null ? party.PartyId : 0;
            into.Add(new Ms2.Ui.Skills.HitCandidate { Id = id, Position = p, Body = Ms2.Ui.Skills.SkillHit.PlayerBody(p, r.Pc.RotationZ),
                Kind = Ms2.Ui.Skills.HitKind.Pc, Team = 1, Hp = r.HpCur, PartyId = partyId, PvpUnknown = true });
        }
        npcs.HitCandidates(into);
    }

    /// <summary>An effect xml played at a point (CombatLayer.PlayEffect), the own cast's motion effect.</summary>
    public void PlayEffect(string effect, Vector3 at, float yawRadians) => combat.PlayEffect(effect, at, yawRadians);

    static string BodyDir(byte gender) => gender == 0 ? "male" : "female";

    Remote Spawn(FieldAddUser u)
    {
        byte gender = u.User.Gender;
        string dir = BodyDir(gender);
        bodies.TryGetValue(gender, out var body);
        var md = body.md ??= builder.LoadModel($"{dir}/{(gender == 0 ? "m_body.nif" : "f_body.nif")}");
        var visual = builder.Build(md, gender == 0 ? "Male" : "Female");
        equip.Apply(visual, md, gender, u.Equips, u.Skin.Primary, u.Skin.Secondary);
        var anim = visual.GetNode<AnimationPlayer>("Model/AnimationPlayer");
        if (body.anims == null)
        {
            // the action sequences of table/pcactiondata.xml (PCBase::vfunc_123), loaded once per body
            body.anims = ActionAnims.Load(packs, builder, md, anim, dir);
            body.lib = anim.GetAnimationLibrary("");
            bodies[gender] = body;
        }
        else
        {
            anim.RemoveAnimationLibrary("");
            anim.AddAnimationLibrary("", body.lib);
        }
        var pc = RemotePc.Create($"Pc{u.ObjectId}", visual, anim, body.anims, gravity, SpawnPoint(G(u.Position)), u.Rotation.Z);
        pc.JobGroup = u.User.JobCode;          // the user block's job group (CharacterInfo maps JobGroup from it)
        pc.BattleStance = u.InBattle;
        pc.Effects = PlayGroundEffects;
        var root = pc.Body;
        AddChild(root);
        // the other player's CObjectShadow, of the PC row (PC::vfunc_49, pc.cpp:3028-3035; the same size as the player's)
        FieldShadows.Character(root, visual, Ms2.Formats.Options.GraphicOptionTable.PC, 100f, 3f);
        tags.AddPc(root, visual.GetNodeOrNull<Node3D>("Model"), md.RootName, u, false);
        var r = new Remote { Pc = pc, Root = root, Visual = visual, Name = u.User.Name };
        r.ObjectId = u.ObjectId; r.CharacterId = u.User.CharacterId; r.Picture = u.User.Picture ?? ""; r.Gems = u.Badges.ToList();
        r.Gender = u.User.Gender;
        r.PremiumTime = u.PremiumTime; r.WeddingState = u.WeddingState; r.PcBangGrade = u.User.PcBangGrade;
        r.HpCur = u.Stats[2].Health; r.HpMax = u.Stats[0].Health; r.InBattle = u.InBattle;
        var dummies = Ms2.Formats.World.EffectDummyTable.Load(packs).Pc(u.User.Gender);
        var model = visual.GetNodeOrNull<Node3D>("Model");
        r.Anchor = () => World.DummyAnchor.Point(dummies, "Eff_UI", model, md.RootName) ?? root.GlobalPosition;
        pc.Spawn();
        // PC::vfunc_10 (a hurt spawn) and OnAddUser's SetEmotion of the packet's emotion byte (scenestategame.cpp:11188,
        // a change from the ctor's 0): either runs UpdateHpBar, so a member's bar is made active at once (not shown)
        if (r.HpCur != r.HpMax || r.InBattle) EnsureBar(r)?.Spawn(r.HpCur, r.HpMax, r.InBattle);
        return r;
    }

    /// <summary>
    /// OnAddUser's spawn point (scenestategame.cpp:11174-11181): RayCastFixedDirection down from the packet's position;
    /// the hit raised by 25 when the capsule there overlaps nothing, else the packet's position.
    /// </summary>
    Vector3 SpawnPoint(Vector3 at)
    {
        var space = GetWorld3D().DirectSpaceState;
        using var q = PhysicsRayQueryParameters3D.Create(at, at + Vector3.Down * MoveConfig.UnlimitedRay, MoveConfig.DownRayGroups);
        q.HitBackFaces = false;
        using var hit = space.IntersectRay(q);
        if (hit.Count == 0) return at;
        var p = (Vector3)hit[PhysicsKeys.Position] + Vector3.Up * 25f;
        // CPhysXHelper::CheckCapsuleOverlap: the PC capsule at the point on 0x389
        using var shape = new CapsuleShape3D { Radius = MoveConfig.Radius, Height = MoveConfig.Height };
        using var sq = new PhysicsShapeQueryParameters3D { Shape = shape, CollisionMask = MoveConfig.DownRayGroups,
            Transform = new Transform3D(Basis.Identity, p + Vector3.Up * (MoveConfig.Height / 2)) };
        using var over = (Godot.Collections.Array)space.IntersectShape(sq, 1);
        return over.Count == 0 ? p : at;
    }

    /// <summary>
    /// CGroundEffectDataManager::PlayEffects past its tests (groundeffectdata.cpp:293-305): the ground's effect files for the
    /// action (GetEffectFiles), each inserted at the PC (CEffectService::InsertEffect): the nif parts through GroundEffects, the
    /// sound parts as positional events whose own distances attenuate them.
    /// </summary>
    void PlayGroundEffects(string action, string ground, Vector3 at, float yaw)
    {
        groundTable ??= new GroundEffectTable(p => { try { return System.Text.Encoding.UTF8.GetString(packs.Read("Xml", p)); } catch { return null; } });
        if (groundFx == null) AddChild(groundFx = new World.GroundEffects(packs, builder));
        foreach (var f in groundTable.PickFiles(ground, action, groundRng))
        {
            groundFx.Play(f, at, yaw);
            if (FieldAudio.Instance is { } audio)
                foreach (var ev in groundTable.SoundsOf(f)) audio.PlayEventAt(ev, at);
        }
        GroundEffectsPlayed++;
    }

    /// <summary>The ground effect actions the remote PCs played (steps and landings).</summary>
    public int GroundEffectsPlayed;

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
                    if (users.Remove(add.User.ObjectId, out var old)) { tags.Remove(old.Root); bars.Remove(old.Root); old.Pc.Free(); }
                    users[add.User.ObjectId] = Spawn(add.User); UsersSpawned++;
                    GD.Print($"live: other player '{add.User.User.Name}' object {add.User.ObjectId} shown at {add.User.Position}");
                    break;
                case UserRemovedEvent rem:
                    if (users.Remove(rem.ObjectId, out var gone))
                    {
                        tags.Remove(gone.Root); bars.Remove(gone.Root); gone.Pc.Free();
                        GD.Print($"live: other player {gone.Name} (object {rem.ObjectId}) left");
                    }
                    break;
                case UserMovedEvent mv:
                    // CSceneStateGame::OnUserSync -> FUN_1417127b0 -> PC::vfunc_114 (scenestategame.cpp:2509-2517): each record
                    // goes to the PC's state queue; a RideSync record goes to the PC's ridee (OnRideSync), which is not here
                    if (mv.Ride || !users.TryGetValue(mv.ObjectId, out var r)) break;
                    r.Pc.States.Push(SyncRecord.From(mv.Record));
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
                    sb.Pc.BattleStance = sb.InBattle;   // vfunc_65 (m_unk_160), PlayEffects' run test
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
                case NpcHpEvent hp:
                    npcs.SetHp(hp.ObjectId, hp.Current, hp.Max);
                    if (hp.Max > 0 && hp.Current <= 0) LiveSession.Instance?.InteractNpcs?.SetDead(hp.ObjectId, true);
                    break;
                case NpcStanceEvent st: npcs.Stance(st.ObjectId, st.Battle); break;
                case NpcAddedEvent na:
                    npcs.Add(na); NpcsAdded++;
                    // the talkable byte FieldAddNpc reads (FUN_14174a560, scenestategame.cpp:3561) for InteractNpcAction
                    LiveSession.Instance?.InteractNpcs?.Add(na.Npc.ObjectId, npcTable.Find(na.Npc.NpcId), na.Npc.Talkable,
                        new Vec3(na.Npc.Position.X, na.Npc.Position.Y, na.Npc.Position.Z));
                    break;
                case NpcRemovedEvent nr:
                    npcs.Remove(nr.ObjectId, nr.Dead);
                    if (nr.Dead) LiveSession.Instance?.InteractNpcs?.SetDead(nr.ObjectId, true);
                    else LiveSession.Instance?.InteractNpcs?.Remove(nr.ObjectId);
                    break;
                case NpcMovedEvent nm:
                    npcs.Move(nm);
                    {
                        var mp = nm.Sync.Position.ToVector3();
                        LiveSession.Instance?.InteractNpcs?.Move(nm.Sync.ObjectId, new Vec3(mp.X, mp.Y, mp.Z));
                    }
                    break;
            }
        }
    }

    int movedSeen;

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        Drain();
        // PCBase::vfunc_53 for each PC, once per drawn frame with that frame's time (the local player's step); a cast's
        // motion holds the body's animation until it ends (CastHolds)
        foreach (var a in users.Values) { CastHolds(a, dt); a.Pc.Tick(dt); }
        npcs.Process(dt);
        combat.Process(dt);
    }
}
