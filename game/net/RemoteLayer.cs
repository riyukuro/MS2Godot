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
        public string Name, Motto;
        /// <summary>The minimap's view of it, made once.</summary>
        public ShownPc Shown;
        public int ObjectId; public long CharacterId; public string Picture = "";
        /// <summary>FieldAddUser's guild name (PC::GetGuildName). STILL-OPEN: the guild packets that change it later.</summary>
        public string GuildName = "";
        public long HpCur, HpMax; public bool InBattle; public int Emotion;         // PCBase's stat pair and battle stance (m_unk_160)
        public EquipApplier.WeaponRig Weapons;
        public PcHpBar Bar;
        public byte Gender;
        /// <summary>PC's invincible time (m_n1054/m_n1058/m_n105c, the effect m_n1060), from OnRevival.</summary>
        public readonly RemoteInvincible Invincible = new();
        /// <summary>The actor's cast record and skill state (RemoteLayer.Casts.cs), and the seconds its motion still plays.</summary>
        public ActorCast Cast;
        public Func<Vector3> Anchor;
        /// <summary>The player as a damage number's target (its Eff_Damage point, UIDamageText m_strDamageNode).</summary>
        public Ui.DamageTarget Damage;
        /// <summary>FieldAddUser's worn gem items (PCGemEquip::ReadFromStream of the packet).</summary>
        public IReadOnlyList<Ms2.Net.Packets.EquipItem> Gems = Array.Empty<Ms2.Net.Packets.EquipItem>();
        /// <summary>FieldAddUser's worn equips (the items ItemUpdate reads again).</summary>
        public IReadOnlyList<Ms2.Net.Packets.EquipItem> Equips = Array.Empty<Ms2.Net.Packets.EquipItem>();
        public IReadOnlyList<Ms2.Net.Packets.EquipItem> Outfit2 = Array.Empty<Ms2.Net.Packets.EquipItem>();
        /// <summary>The equipment's layer period (SetLayerPeriod: FieldAddUser's, then ItemUse mode 5's).</summary>
        public long CodiStart, CodiEnd; public bool CodiShown = true;
        /// <summary>FieldAddUser's premium club time and wedding state (PC m_n1190 / m_n1220) and the user block's PC bang
        /// grade (m_nFF8, scenestategame.cpp:11151), the PC::CanUseItem inputs of the balloon gem.</summary>
        public long PremiumTime; public short WeddingState; public byte PcBangGrade;
        /// <summary>The PC's level (FieldAddUser's, then LevelUp's) and its worn items' special abilities (CEquipment
        /// m_abilities), taken again after an equipment or level change; null until asked.</summary>
        public short Level; public Dictionary<int, int> WornAbilities;
        /// <summary>The PC's AdditionalCliList (FUN_1416749a0): FieldAddUser's list (DecodeListPacket, additionalcli_2.cpp:721,
        /// each one an add) and its OnBuff packets. Only what the target filter reads of it is used.</summary>
        public Ms2.Ui.Skills.AdditionalBuffList Buffs;
        /// <summary>The revival flag and count, the tomb and its bar.</summary>
        public readonly RemoteDeath Death = new();
        /// <summary>The pc's cube mode and craft (m_nCubeModeFFC, m_pCubeCraft): only the mode and the craft's record, the
        /// guide is the local pc's.</summary>
        public readonly Ms2.Ui.CubeCraftState Cube = new();
        /// <summary>The pc's held item (PC GetHeldItem).</summary>
        public readonly Ms2.Ui.HeldItem Held = new();
        /// <summary>The PC's mount (PCBase m_spB10, RemoteLayer.Ride.cs), null when not riding.</summary>
        public RemotePc Ridee;
        /// <summary>PCBase m_mapB28: the seat of each passenger's object id on this PC's mount (ResponseRide modes 3-5).</summary>
        public readonly Dictionary<int, int> Passengers = new();
        /// <summary>PC m_b1224 (PC::FUN_1416642c0, ResponseRide mode 6): no boarding this PC's mount.</summary>
        public bool NoBoarding;
    }

    /// <summary>CItem::FUN_140523c40's outcome for an item id: false when the item data has no such item (no function = true).</summary>
    public Func<int, bool> ItemExists;

    readonly ModelBuilder builder;
    readonly EquipApplier equip;
    readonly NpcLayer npcs;
    readonly NpcTable npcTable;
    /// <summary>FindNpcById(id)->GetNpcClass (@0x1410934c0): the npc row's class (CNpcData m_nClass), 0 without an npc or row.</summary>
    public int NpcClassOf(int objectId) => Tracker.NpcIdOf(objectId) is var id and not 0 && npcTable?.Find(id) is { } r ? r.Class : 0;
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
    /// <summary>SkillDataTableCli::FUN_140aafef0, the remote PCs' buff levels.</summary>
    Func<uint, ushort, Ms2.Ui.Skills.AdditionalLevelData> levelOf;
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
    /// <summary>Another pc as the minimap's proxy reads it (UIMinimapDialog::AddProxy): its character id, name, today memo
    /// and picture key, its position in the client's z-up coordinates and its dead flag (the proxy's m_68).</summary>
    public sealed record ShownPc(int ObjectId, long CharacterId, string Name, string Motto, string Picture, Func<System.Numerics.Vector3> Position, Func<bool> Dead);
    /// <summary>A pc was spawned (FieldAddUser, after the own one is skipped): UIMinimapDialog::vfunc_3's AddProxy.</summary>
    public event Action<ShownPc> PcShown;
    /// <summary>A pc left (FieldRemoveUser), by object id: UIMinimapDialog::vfunc_4's RemoveProxy.</summary>
    public event Action<int> PcLeft;
    /// <summary>The pcs spawned now, for a minimap built after them.</summary>
    public IEnumerable<ShownPc> ShownPcs => users.Values.Select(Shown);
    ShownPc Shown(Remote r) => r.Shown ??= new ShownPc(r.ObjectId, r.CharacterId, r.Name ?? "", r.Motto ?? "", r.Picture ?? "",
        () => r.Root != null && GodotObject.IsInstanceValid(r.Root) && r.Root.IsInsideTree() && r.Root.GlobalPosition is var g
            ? new System.Numerics.Vector3(g.X, -g.Z, g.Y) : default,
        () => r.Pc?.States.Dead ?? false);
    /// <summary>A shown player's position in the client's z-up coordinates (WorldService::FindPCById then vfunc_18); null when it is not here.</summary>
    public System.Numerics.Vector3? PositionOf(int objectId) =>
        users.TryGetValue(objectId, out var r) && r.Root != null && GodotObject.IsInstanceValid(r.Root) && r.Root.IsInsideTree()
            ? new System.Numerics.Vector3(r.Root.GlobalPosition.X, -r.Root.GlobalPosition.Z, r.Root.GlobalPosition.Y) : null;

    /// <summary>WorldService::FindPCById: a shown player as a damage number's target; null when it is not a player here.</summary>
    public Ui.DamageTarget DamageOf(int objectId) => users.TryGetValue(objectId, out var r) ? r.Damage : null;

    /// <summary>WorldService::FindNpcById: whether an npc of the object id is here.</summary>
    public bool HasNpc(int objectId) => npcs.Placed(objectId) != null;

    /// <summary>OnExpUp's gain of an npc (<see cref="NpcLayer.AddExp"/>); false when there is no npc of the id.</summary>
    public bool AddNpcExp(int objectId, long exp) => npcs.AddExp(objectId, exp);

    /// <summary>An npc's exp shown at the end of its death sequence (<see cref="NpcLayer.ExpShown"/>).</summary>
    public Action<World.FieldNpcs.Placed, long> NpcExpShown { set => npcs.ExpShown = value; }
    /// <summary>An npc made and an npc removed (<see cref="NpcLayer.Added"/>, <see cref="NpcLayer.Removed"/>).</summary>
    public Action<World.FieldNpcs.Placed> NpcAdded { set => npcs.Added = value; }
    public Action<int> NpcRemoved { set => npcs.Removed = value; }
    /// <summary>The own player's place for the npcs' hit sounds (<see cref="NpcLayer.Viewer"/>).</summary>
    public Func<Vector3?> Viewer { set => npcs.Viewer = value; }
    /// <summary>The field's npcs, dead or not.</summary>
    public IEnumerable<World.FieldNpcs.Placed> Npcs => npcs.All;
    public bool NpcDead(int objectId) => npcs.IsDead(objectId);

    /// <summary>WorldService::FindObjectById then vfunc_18 / vfunc_20 on a shown player or an npc: its position in the client's
    /// z-up coordinates and its rotation z in degrees; null when neither is here.</summary>
    public (System.Numerics.Vector3 Pos, float RotZ)? PoseOf(int objectId) =>
        users.TryGetValue(objectId, out var r) && r.Pc != null ? (r.Pc.Position, r.Pc.RotationZ) : npcs.PoseOf(objectId);

    /// <summary>WorldService::FindPCById then GetEquipByUid (OnItemUpdate): whether the player is here, and its worn equip of the uid (null when none).</summary>
    public (bool PcKnown, Ms2.Net.Packets.EquipItem Equip) EquipOf(int objectId, long uid)
    {
        if (!users.TryGetValue(objectId, out var r)) return (false, null);
        // CEquipment::FindItemByUid (equipment.cpp:789) looks through every equip layer
        for (int i = 0; i < r.Equips.Count; i++) if (r.Equips[i].Uid == uid) return (true, r.Equips[i]);
        for (int i = 0; i < r.Outfit2.Count; i++) if (r.Outfit2[i].Uid == uid) return (true, r.Outfit2[i]);
        return (true, null);
    }

    /// <summary>
    /// CSceneStateGame::OnItemPutOn (scenestategame.cpp:3080) for another player (FindPCById then PC::vfunc_127, pc.cpp:1400):
    /// the item goes into the player's lists and only its slot's model is rebuilt (EquipApplier.Reapply). False when the
    /// player is not here, the item has no data or it is not worn (no preset, kind above 2).
    /// </summary>
    public bool PutOn(Ms2.Net.Packets.ItemPutOn p, Func<int, int?> wornLayer, Func<int, bool> itemKnown = null)
    {
        if (!users.TryGetValue(p.ObjectId, out var r)) return false;
        if (itemKnown != null && !itemKnown(p.Item.Id)) return false;     // CItem::FUN_140523c40 (item.cpp:3096): an id without data is not put on
        var equips = r.Equips; var outfit = r.Outfit2;
        if (!Ms2.Net.Packets.WornEquips.PutOn(ref equips, ref outfit, p, wornLayer)) return false;
        r.Equips = equips; r.Outfit2 = outfit; r.WornAbilities = null;
        equip.Reapply(r.Visual, LookOf(r));
        return true;
    }

    /// <summary>CSceneStateGame::OnItemPutOff (scenestategame.cpp:3144) for another player (PC::vfunc_128, pc.cpp:1415): the
    /// worn item of that uid leaves the lists and its slot's model is taken off. False when nothing changed.</summary>
    public bool PutOff(Ms2.Net.Packets.ItemPutOff p, Func<int, int?> wornLayer)
    {
        if (!users.TryGetValue(p.ObjectId, out var r)) return false;
        var equips = r.Equips; var outfit = r.Outfit2;
        if (Ms2.Net.Packets.WornEquips.PutOff(ref equips, ref outfit, p.ItemUid, wornLayer) == null) return false;
        r.Equips = equips; r.Outfit2 = outfit; r.WornAbilities = null;
        equip.Reapply(r.Visual, LookOf(r));
        return true;
    }

    /// <summary>OnItemUpdate for another player's worn item (the packet read its body again, ItemUpdate.Updated):
    /// CEquipItem's model update (FUN_1411dd1c0) rebuilds that item's model; here the item's slot is rebuilt.</summary>
    public void ItemUpdated(int objectId, Ms2.Net.Packets.EquipItem item)
    {
        if (item == null || !users.TryGetValue(objectId, out var r)) return;
        r.WornAbilities = null;
        equip.Reapply(r.Visual, LookOf(r), item.Slot);
    }

    /// <summary>
    /// CSceneStateGame::OnSetCraftMode (@0x141723410, scenestategame.cpp:7827-7851) for another pc: nothing when it is missing or
    /// dead, else PC::vfunc_135 (@0x141659450, pc.cpp:3068-3082): a pc not in the cube mode yet goes to StateIdle (StateFall
    /// while its model is not in, FUN_14123bea0), then the mode and the craft (FUN_141659250), then the weapon hold
    /// (FUN_141659000: no weapons in the cube mode). False when no pc took it.
    /// STILL-OPEN: the craft's guide cube for another pc (Unk_PCCubeCraft::FUN_1410a8af0's RebuildGuides, cubecraft_2.cpp:340).
    /// </summary>
    public bool SetCraftMode(int objectId, int cubeMode, int craftMode, Ms2.Net.Packets.CubeItemInfo item)
    {
        if (!users.TryGetValue(objectId, out var r) || r.Pc.States.Dead) return false;
        // vfunc_59 (PC::vfunc_59: the vfunc_124 gate, and a mounted pc's state goes to its ridee)
        if (!r.Cube.InCubeMode) r.Pc.States.ServerState(r.Cube.ModelLoaded ? RemotePcStates.StateIdle : RemotePcStates.StateFall, r.HpCur, r.HpMax);
        r.Cube.Set(cubeMode, craftMode, item, false);
        RefreshWeapons(r);
        return true;
    }

    /// <summary>
    /// OnResponseCube's held-item modes for another pc (scenestategame.cpp:10145-10190): 0x11 holds the item when the cube
    /// has its weapon, 0x12 lets it go (<see cref="Ms2.Ui.HeldItem.ResponseCube"/>); errors are the local pc's notice only.
    /// False when no pc took it.
    /// STILL-OPEN: the held item's model (AvatarPC::vfunc_127, AddItemAndPreloadModels).
    /// </summary>
    public bool ResponseCube(Ms2.Net.Packets.ResponseCubeHeldItem m, Func<int, bool> cubeHasWeapon)
    {
        if (m == null || !users.TryGetValue(m.Id, out var r)) return false;
        return r.Held.ResponseCube(m, r.ObjectId, cubeHasWeapon, ItemExists?.Invoke(m.ItemId) ?? true);
    }

    /// <summary>Another pc's cube mode (0 = none, also without the pc), held item id (0 = none), whether its craft is made
    /// and its state id.</summary>
    public (int CubeMode, int HeldItem, bool Craft, int State) CubeStateOf(int objectId) =>
        users.TryGetValue(objectId, out var r) ? (r.Cube.CubeMode, r.Held.ItemId, r.Cube.Current != null, r.Pc.States.Cur) : (0, 0, false, 0);

    /// <summary>The other players shown now (the own PC is not among them).</summary>
    public IEnumerable<Pc> Pcs => users.Values.Select(r => new Pc(r.ObjectId, r.CharacterId, r.Name ?? "", r.Picture, r.Root, r.Anchor, r.Gems,
        r.PremiumTime, r.PcBangGrade, r.WeddingState));

    readonly NxController.FaceGrids faceGrids;   // the field's, shared with the player's controller

    public RemoteLayer(Field field, ModelBuilder mb, long selfCharacterId)
    {
        Name = "RemoteLayer";
        selfCharacter = selfCharacterId;
        field.AddChild(this);
        faceGrids = field.FaceGrids;
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

    /// <summary>A damage number to show (UIDamageText::FUN_141911e90): the number, the npc hit (null when the target is
    /// not an npc) and the other player hit (null when the target is not one).</summary>
    public Action<Ms2.Ui.Hud.DamageNumber, World.FieldNpcs.Placed, Ui.DamageTarget> Numbered;
    /// <summary>A PC's battle stance changed (OnUserBattle -> PC::vfunc_66, after SetEmotion's change guard).</summary>
    public Action<int, int> BattleStanceChanged;

    /// <summary>The PC's battle stance (vfunc_65, m_unk_160); false for an object not shown.</summary>
    public bool InBattle(int objectId) => users.TryGetValue(objectId, out var r) && r.InBattle;
    readonly List<Ms2.Ui.Hud.DamageNumber> numbers = new();
    Func<int, int> pcMask;
    Func<int, bool> isObject;
    Ms2.Ui.Skills.SkillDamageTable skillDamage;
    /// <summary>The skill attacks' damage blocks (read on the first hit).</summary>
    Func<int, int, int, int, Ms2.Ui.Skills.SkillAttackDamage> attackOf;

    /// <summary>The skill and level of a region skill object of the field by its id (OnRegionSkill mode 0); null for none.</summary>
    public Func<int, (int Skill, short Level)?> RegionSkillOf;

    Ms2.Ui.Skills.SkillDamageTable SkillDamage => skillDamage ??= Ms2.Ui.GameTables.Shared(packs, Ms2Godot.GameOptions.StringLanguage).SkillDamage;
    static readonly Ms2.Formats.Options.GameOption.Battle defaultBattle = new();
    /// <summary>CDamageRecord::GetAttackData then m_damage (<see cref="Ms2.Ui.Skills.SkillDamageTable.Get"/>); null when none.</summary>
    public Ms2.Ui.Skills.SkillAttackDamage AttackDamage(int skillId, int level, int motion, int attack) =>
        SkillDamage.Get(skillId, level, motion, attack);

    readonly Ms2.Ui.Skills.StateHitData remoteHitData = new();
    int hitMapId = -1, hitMapType;

    /// <summary>The current field's map type (CFieldData), read once per field.</summary>
    int HitMapType()
    {
        int id = LiveSession.Instance?.FieldId ?? 0;
        if (id != hitMapId)
        {
            hitMapId = id;
            hitMapType = FieldData.FindById(packs, id)?.MapType ?? 0;
        }
        return hitMapType;
    }

    /// <summary>
    /// OnSkillDamage mode 1 (FUN_14171cdf0, scenestategame.cpp:5607) for the records on the other PCs shown here: PC::vfunc_33
    /// (@0x141652080, pc.cpp:885) runs for every PC, so each record on one goes through
    /// <see cref="Ms2.Ui.Skills.HitReaction.OnSkillHit"/> on its body as LiveSession's does on the own PC (the record's
    /// position and direction, the hit facing, the owner's pose, the ground of a map type of 9 or 3), then StateHit with the
    /// data (vfunc_59(0xd, data, 0, duration > 0), pc.cpp:943) or StateIdle (pc.cpp:946-950). The record needs the attack
    /// (CDamageRecord::Init); the hits' count and HasDamage do not gate it.
    /// STILL-OPEN: the PC's buffs' super armor (AdditionalCliList::GetSuperArmor), its invincible buff and its equipment's
    /// push resistance (CEquipment::FUN_1411e0f90, ability 0x27) are not known for another PC and count as none; the
    /// magic service's delivery time (FUN_14146b440) is taken as at once; the record's invoke effects, colour boost and
    /// hit sound (pc.cpp:891-893, :951-955) are not played.
    /// </summary>
    void ReactToHits(SkillDamage d)
    {
        Ms2.Ui.Skills.SkillAttackDamage attack = null;
        bool looked = false;
        foreach (var t in d.Targets)
        {
            if (!users.TryGetValue(t.TargetId, out var r) || r.Pc == null) continue;
            if (!looked) { attack = AttackDamage(d.SkillId, d.Level, d.Motion, d.AttackPoint); looked = true; }
            if (attack == null) return;
            long total = 0;
            foreach (var h in t.Hits) total += h.Damage;
            int mapType = HitMapType();
            bool ground = mapType is 9 or 3;
            uint armor = Ms2.Ui.Skills.HitReaction.SuperArmor(r.Pc.ClientStateId, 0, 0);
            var recordDir = attack.AttackMethodType == 1 ? d.DirectionShort.ToVector3() : System.Numerics.Vector3.Zero;
            (System.Numerics.Vector3, System.Numerics.Vector3)? attacker = null;
            if (PoseOf(d.OwnerId) is { } pose)
            {
                Ms2.Ui.Skills.SkillHit.Axes(pose.RotZ, out var forward, out _);
                attacker = (pose.Pos, forward);
            }
            remoteHitData.Clear();
            var change = Ms2.Ui.Skills.HitReaction.OnSkillHit(r.Pc, attack, total, armor, false, ground, 0,
                d.Position.ToVector3(), recordDir, d.DirectionShort.Z, attacker, remoteHitData);
            if (change == Ms2.Ui.Skills.HitReaction.Change.Hit) r.Pc.EnterHit(remoteHitData, remoteHitData.Duration > 0f);
            else if (change == Ms2.Ui.Skills.HitReaction.Change.Idle) r.Pc.ReactIdle();
        }
    }

    /// <summary>
    /// PCBase::FUN_141671c20's vfunc_111 per hit (<see cref="Ms2.Ui.Hud.DamageNumbers.Records"/>) through UIDamageText's
    /// show test (FUN_141913bd0) with the battle options; the party mask is IsMemberByCharId on a player's character.
    /// </summary>
    void ShowNumbers(SkillDamage d)
    {
        numbers.Clear();
        pcMask ??= PcMask;
        isObject ??= Tracker.HasObject;
        Ms2.Ui.Hud.DamageNumbers.Records(d, numbers, isObject, SkillDamage);
        var o = GameOptions.Current?.BattleOption ?? defaultBattle;
        foreach (var n in numbers)
            if (Ms2.Ui.Hud.DamageNumbers.Shows(o, n.Grade, n.TargetId, n.CasterId, Tracker.SelfObjectId, pcMask))
                Numbered(n, npcs.Placed(n.TargetId), users.TryGetValue(n.TargetId, out var r) ? r.Damage : null);
    }

    /// <summary>One number on an actor of the field (CSceneStateGame::FUN_141731190's vfunc_111 call), through the same show
    /// test as a damage record's numbers; false when the id is no npc or player of the field.</summary>
    public bool ShowNumber(Ms2.Ui.Hud.DamageNumber n)
    {
        if (Numbered == null) return false;
        var npc = npcs.Placed(n.TargetId);
        users.TryGetValue(n.TargetId, out var r);
        if (npc == null && r == null) return false;
        pcMask ??= PcMask;
        var o = GameOptions.Current?.BattleOption ?? defaultBattle;
        if (Ms2.Ui.Hud.DamageNumbers.Shows(o, n.Grade, n.TargetId, n.CasterId, Tracker.SelfObjectId, pcMask)) Numbered(n, npc, r?.Damage);
        return true;
    }

    int PcMask(int id) => !users.TryGetValue(id, out var r) ? 0
        : LiveSession.Instance?.Party is { PartyId: not 0 } p && p.Find(r.CharacterId) != null ? 2 : 4;

    /// <summary>
    /// The other players' and the npcs' bodies for an own attack's area pick, in client space (z up): a player's body box is
    /// PC::SetDefaultBoundingBox's (<see cref="Ms2.Ui.Skills.SkillHit.PlayerBody"/>), the npcs' come from
    /// <see cref="NpcLayer.HitCandidates"/>. A player is a pc of team 1 with its stat pair's hp, for
    /// <see cref="Ms2.Ui.Skills.SkillHit.FilterTarget"/>.
    /// Its party id is the own party's when CPartyManager has it as a member (PC::FUN_141661bc0, pc.cpp:4438), else 0.
    /// Its pvp on/off, forceApplyTarget bits, hidden flag and invincible kind are its buff list's
    /// (<see cref="Ms2.Ui.Skills.AdditionalBuffList"/>); its state id is its state machine's current one.
    /// STILL-OPEN: the states 6 / 7 half of hidden and HasPvPPoint (false here) are not fed.
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
                Kind = Ms2.Ui.Skills.HitKind.Pc, Team = 1, Hp = r.HpCur, PartyId = partyId,
                PvpOnOff = r.Buffs.IsPvpOnOff,
                ForceApplyTarget = r.Buffs.ForceApplyTarget, Hidden = r.Buffs.IsHidden,
                StateId = r.Pc.States.Cur, Invincible = r.Buffs.Invincible });
        }
        npcs.HitCandidates(into);
    }

    /// <summary>An effect xml played at a point (CombatLayer.PlayEffect), the own cast's motion effect.</summary>
    public void PlayEffect(string effect, Vector3 at, float yawRadians, int casterKind = Ms2.Audio.SoundMusicEffectState.CasterOther)
        => combat.PlayEffect(effect, at, yawRadians, casterKind);

    /// <summary>The same turned by a full rotation (godot space).</summary>
    public void PlayEffect(string effect, Vector3 at, Basis rotation, int casterKind = Ms2.Audio.SoundMusicEffectState.CasterOther)
        => combat.PlayEffect(effect, at, rotation, casterKind);

    /// <summary>How long an effect xml's parts stay (CombatLayer.EffectLength).</summary>
    public float EffectLength(string effect) => combat.EffectLength(effect);

    /// <summary>Feature 0x8e and the server clock, which the layer switch (WornEquips.OutfitShown) reads.</summary>
    public Func<bool> Feature8e = () => false;
    public Func<long> ServerNow = () => 0;
    bool OutfitShown(long start, long end) => Ms2.Net.Packets.WornEquips.OutfitShown(Feature8e(), start, end, ServerNow());
    System.Collections.Generic.IEnumerable<Ms2.Net.Packets.EquipItem> LookOf(Remote r)
    {
        r.CodiShown = OutfitShown(r.CodiStart, r.CodiEnd);
        return Ms2.Net.Packets.WornEquips.Look(r.Equips, r.Outfit2, r.CodiShown);
    }

    // The client re-checks the layer period on a timer (CEquipment::FUN_1411ebde0 -> FUN_1411df910) and rebuilds the model
    // when the answer flips. One coarse timer here: armed at the earliest end among the players, tested once a second.
    long codiNext = long.MaxValue;
    float codiClock;
    void ArmCodi()
    {
        long now = ServerNow(), next = long.MaxValue;
        foreach (var r in users.Values) next = Math.Min(next, Ms2.Net.Packets.WornEquips.NextFlip(r.CodiStart, r.CodiEnd, now));
        codiNext = next;
    }
    void CodiTick(float dt)
    {
        codiClock += dt;
        if (codiClock < 1f) return;
        codiClock = 0;
        if (codiNext == long.MaxValue || ServerNow() < codiNext) return;
        foreach (var r in users.Values)
            if (OutfitShown(r.CodiStart, r.CodiEnd) != r.CodiShown) equip.Reapply(r.Visual, LookOf(r));
        ArmCodi();
    }

    /// <summary>ItemUse mode 5, OnActiveCodiTab (scenestategame.cpp:8663) for another player: SetLayerPeriod, then the
    /// model follows the layer switch. False when the player is not here.</summary>
    public bool ActiveCodiTab(Ms2.Net.Packets.CodiTab t)
    {
        if (!users.TryGetValue(t.ActorId, out var r)) return false;
        r.CodiStart = t.Start; r.CodiEnd = t.End;
        equip.Reapply(r.Visual, LookOf(r));
        ArmCodi();
        return true;
    }

    static string BodyDir(byte gender) => gender == 0 ? "male" : "female";


    /// The remote's weapon hold, from its state machine: State::vfunc_15 of the current state, sub state and StateWalk's run flag.
    /// Called on a key change (RemotePcStates.WeaponKeyChanged) and on a battle-stance change, not per frame.
    static void RefreshWeapons(Remote r)
    {
        var st = r.Pc.States;
        // vfunc_15 allows the hands, vfunc_16 forces them (StateHold not riding; ridees are not modelled, so never riding)
        r.Weapons?.SetState(r.InBattle, Ms2.Ui.Items.WeaponStance.StateHoldsWeapons(st.Cur, st.Sub, st.WalkRun),
            stateForces: Ms2.Ui.Items.WeaponStance.StateForces(st.Cur), inCubeMode: r.Cube.InCubeMode);
        r.Pc.SetWeaponPrefix(r.Weapons?.ActivePrefix);
    }
    Remote Spawn(FieldAddUser u)
    {
        byte gender = u.User.Gender;
        string dir = BodyDir(gender);
        bodies.TryGetValue(gender, out var body);
        var md = body.md ??= builder.LoadModel($"{dir}/{(gender == 0 ? "m_body.nif" : "f_body.nif")}");
        var visual = builder.Build(md, gender == 0 ? "Male" : "Female");
        equip.Apply(visual, md, gender, Ms2.Net.Packets.WornEquips.Look(u.Equips, u.Outfit2, OutfitShown(u.SkinTabStart, u.SkinTabEnd)), u.Skin.Primary, u.Skin.Secondary);
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
        var pc = RemotePc.Create($"Pc{u.ObjectId}", visual, anim, body.anims, gravity, SpawnPoint(G(u.Position)), u.Rotation.Z, faceGrids);
        pc.JobGroup = u.User.JobCode;          // the user block's job group (CharacterInfo maps JobGroup from it)
        pc.BattleStance = u.InBattle;
        pc.Effects = PlayGroundEffects;
        int asker = u.ObjectId;
        pc.HoldLookup = (id, node) => FindHoldTarget(asker, id, node);
        var root = pc.Body;
        AddChild(root);
        // the other player's CObjectShadow, of the PC row (PC::vfunc_49, pc.cpp:3028-3035; the same size as the player's)
        FieldShadows.Character(root, visual, Ms2.Formats.Options.GraphicOptionTable.PC, 100f, 3f);
        tags.AddPc(root, visual.GetNodeOrNull<Node3D>("Model"), md.RootName, u, false);
        var r = new Remote { Pc = pc, Root = root, Visual = visual, Name = u.User.Name, Motto = u.User.Motto };
        r.ObjectId = u.ObjectId; r.CharacterId = u.User.CharacterId; r.GuildName = u.User.GuildName ?? ""; r.Picture = u.User.Picture ?? ""; r.Gems = u.Badges; r.Equips = u.Equips; r.Outfit2 = u.Outfit2; r.CodiStart = u.SkinTabStart; r.CodiEnd = u.SkinTabEnd;
        r.Gender = u.User.Gender;
        r.PremiumTime = u.PremiumTime; r.WeddingState = u.WeddingState; r.PcBangGrade = u.User.PcBangGrade; r.Level = u.User.Level;
        r.HpCur = u.Stats[2].Health; r.HpMax = u.Stats[0].Health; r.InBattle = u.InBattle; r.Emotion = u.InBattle ? 1 : 0;
        // PC::vfunc_91 = vfunc_90's 0x23 block (pc.cpp:1645-1660): per kind health 4, attack speed 0xd, movement speed 0xe,
        // mount speed 0x20, jump height 0x16
        for (int k = 0; k < 3; k++)
        {
            var st = u.Stats[k];
            pc.SetStat(k, PcStat.Health, st.Health); pc.SetStat(k, PcStat.AttackSpeed, st.AttackSpeed);
            pc.SetStat(k, PcStat.MovementSpeed, st.MoveSpeed); pc.SetStat(k, PcStat.MountSpeed, st.MountSpeed);
            pc.SetStat(k, PcStat.JumpHeight, st.JumpHeight);
        }
        // OnAddUser (scenestategame.cpp:11185-11186): no current health after the stats -> FUN_14166ce50(1), the dead flag
        if (r.HpCur == 0) pc.States.SetDead(true);
        levelOf ??= Ms2.Ui.GameTables.Shared(packs, Ms2Godot.GameOptions.StringLanguage).Additional.GetLevel;
        r.Buffs = new Ms2.Ui.Skills.AdditionalBuffList(levelOf);
        r.Death.BuffsDisableRevival = () => r.Buffs.IsDisableRevivalHere;
        r.Death.Revival.Apply(u);   // OnAddUser (scenestategame.cpp:11280-11281): the confirm end tick and count, the flag
        foreach (var b in u.Buffs) r.Buffs.Apply(BuffPacket.FromAdd(b));
        // OnAddUser (scenestategame.cpp:11189-11192): the new PC's PC::vfunc_135 before its model, after the dead flag (a dead
        // pc gets no mode); its StateFall is the spawn's own. PC::vfunc_12 (pc.cpp:844-855) on the model: the waiting craft is
        // made and the AddUser i32 is held.
        r.Cube.NewPc(); r.Cube.Set(u.CubeMode, u.CraftMode, u.CubeItem, pc.States.Dead); r.Cube.ModelAttached();
        r.Held.AddUser(u.HeldObject, ItemExists?.Invoke(u.HeldObject) ?? true);
        r.Weapons = EquipApplier.RigOf(visual);
        r.Pc.States.WeaponKeyChanged = () => RefreshWeapons(r);
        RefreshWeapons(r);
        var dummies = Ms2.Formats.World.EffectDummyTable.Load(packs).Pc(u.User.Gender);
        var model = visual.GetNodeOrNull<Node3D>("Model");
        r.Anchor = () => World.DummyAnchor.Point(dummies, "Eff_UI", model, md.RootName) ?? root.GlobalPosition;
        r.Damage = new Ui.DamageTarget(root, () => World.DummyAnchor.Point(dummies, "Eff_Damage", model, md.RootName) ?? root.GlobalPosition, true);
        pc.States.DeadEntered = () => EnterDead(r);
        pc.States.DeadLeft = () => r.Pc.SetBodyShown(true);
        HookMagics(r);
        pc.Spawn();
        // PC::vfunc_10 (a hurt spawn) and OnAddUser's SetEmotion of the packet's emotion byte (scenestategame.cpp:11188,
        // a change from the ctor's 0): either runs UpdateHpBar, so a member's bar is made active at once (not shown)
        if (r.HpCur != r.HpMax || r.InBattle) EnsureBar(r)?.Spawn(r.HpCur, r.HpMax, r.InBattle);
        return r;
    }

    /// <summary>
    /// StateDead::vfunc_4 for another PC (<see cref="RemoteDeath.Enter"/>): the revive in place is the field's
    /// HasAutoRevivalType1 with MODEPVP_DEAD entry ShadowWorldAutoReviveDeadAction. STILL-OPEN: the PvP point case (the team
    /// as the entry).
    /// </summary>
    void EnterDead(Remote r)
    {
        bool inPlace = false; int anim = 1;
        try
        {
            if (LiveSession.Instance?.FieldId is int id) inPlace = FieldData.FindById(packs, id)?.HasAutoRevivalType1 == true;
            if (inPlace) anim = Ms2Godot.World.FieldContents.Constants(packs).Int("ShadowWorldAutoReviveDeadAction", 1);
        }
        catch (Exception ex) { GD.PushWarning($"remote death state: {ex.Message}"); }
        r.Death.Enter(this, packs, builder, r.Pc, r.Anchor, inPlace, anim);
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

    /// <summary>The field's RegionSkills node, the effect service the kept actor effects go through.</summary>
    World.RegionSkills RegionFx => GetParent()?.GetNodeOrNull<World.RegionSkills>(RegionSkillsName);
    static readonly NodePath RegionSkillsName = "RegionSkills";

    /// <summary>FUN_141661370(1, UserRevivalInvincibleTick, 100000004) on a remote PC (OnRevival, scenestategame.cpp:7408):
    /// the times, the common resource's Revival_Invincible effect at the actor (m_strInvincibleEffect), no buff icon.</summary>
    void StartInvincible(Remote r)
    {
        const int timer = Ms2.Ui.Hud.InvincibleBlink.RevivalTimer;
        var tables = Ms2.Ui.GameTables.Shared(packs, Ms2Godot.GameOptions.StringLanguage);
        r.Invincible.Start(Ms2.Ui.Hud.InvincibleBlink.TickFor(timer, World.FieldContents.Constants(packs)), timer, (uint)Time.GetTicksMsec(),
            r.Root, RegionFx, tables.CommonResource("Revival_Invincible"));
    }

    /// <summary>A remote PC's invincible time, by object id (tests and the debug endpoint).</summary>
    public RemoteInvincible InvincibleOf(int objectId) => users.TryGetValue(objectId, out var r) ? r.Invincible : null;

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
                    // a replaced or leaving PC: ~PC frees its kept invincible effect m_n1060 (pc.cpp:764-768)
                    if (users.Remove(add.User.ObjectId, out var old)) { remoteMagics.Remove(old.ObjectId); hitEffects.Remove(old.ObjectId); old.Invincible.End(null, RegionFx); tags.Remove(old.Root); bars.Remove(old.Root); old.Death.Free(); FreeRidee(old); old.Pc.Free(); }
                    if (old != null) PcLeft?.Invoke(old.ObjectId);
                    var added = users[add.User.ObjectId] = Spawn(add.User); UsersSpawned++; ArmCodi();
                    // FieldAddUser's ride (PCBase::SetRideOnAction) and its passengers, ridden at the PC's creation
                    // (PCBase::vfunc_10 -> vfunc_120(GetRideOnAction()), pcbase.cpp:921)
                    if (add.User.Ride != null) Ms2.Ui.RidePassengers.Load(add.User.Passengers, added.Passengers);
                    RideOn(added, add.User.Ride);
                    PcShown?.Invoke(Shown(added));
                    GD.Print($"live: other player '{add.User.User.Name}' object {add.User.ObjectId} shown at {add.User.Position}");
                    break;
                case UserRemovedEvent rem:
                    // PCBase::vfunc_14 rides a riding PC off (vfunc_121(null), pcbase.cpp:855-859), whose ride-off effect
                    // hangs on the PC's own model ("Scene Root", attachToNode: CNifEffect::AttachToModelNode,
                    // nifeffectparam.cpp:1079-1108), which leaves the scene with the PC, and its sound is onlyCasterMyPC:
                    // nothing shows, so FreeRidee plays no effect
                    if (users.Remove(rem.ObjectId, out var gone))
                    {
                        gone.Invincible.End(null, RegionFx);   // ~PC, pc.cpp:764-768
                        remoteMagics.Remove(rem.ObjectId);
                        hitEffects.Remove(rem.ObjectId);
                        tags.Remove(gone.Root); bars.Remove(gone.Root); gone.Death.Free(); FreeRidee(gone); gone.Pc.Free();
                        PcLeft?.Invoke(gone.ObjectId);
                        GD.Print($"live: other player {gone.Name} (object {rem.ObjectId}) left");
                    }
                    break;
                case UserMovedEvent mv:
                    // CSceneStateGame::OnUserSync -> FUN_1417127b0 -> PC::vfunc_114 (scenestategame.cpp:2509-2517): each record
                    // goes to the PC's state queue; a RideSync record goes to the PC's ridee (OnRideSync, scenestategame.cpp:2472),
                    // dropped when it has none
                    if (!users.TryGetValue(mv.ObjectId, out var r)) break;
                    var to = mv.Ride ? r.Ridee : r.Pc;
                    if (to == null) break;
                    to.States.Push(SyncRecord.From(mv.Record, Tracker.Profile));
                    if (++movedSeen <= 3 || movedSeen % 100 == 0)
                        GD.Print($"live: other player {r.Name} moved #{movedSeen} to {mv.Position} yaw {mv.YawDegrees}, state {to.States.Cur}, {to.States.Snaps} snap(s) (last {to.States.LastSnap:0})");
                    break;
                case UserStatEvent st:
                    // OnStat (vfunc_74 -> FUN_141992230): an hp change shows the member's bar
                    if (!users.TryGetValue(st.Stat.ObjectId, out var su)) break;
                    if (st.Stat.TryGet(PcStat.Health, out long hpMax, out long hpCur)) { su.HpMax = hpMax; su.HpCur = hpCur; }
                    foreach (var (id, v) in st.Stat.Values)
                        for (int k = 0; k < 3; k++) SetStat(su, k, id, v[k]);
                    EnsureBar(su)?.Stat(su.HpCur, su.HpMax);
                    break;
                case UserBattleEvent bat:
                    // OnUserBattle -> SetEmotion: the change guard (pcbase.cpp:1268) runs UpdateHpBar only on a change;
                    // the state 0xe skip (scenestategame.cpp:3269) is not ported (no remote state here). STILL-OPEN: the
                    // ridee's and the own target's emotion (spRidee / FUN_141bd3620) need ridees and pets.
                    if (!users.TryGetValue(bat.ObjectId, out var sb)) break;
                    if (sb.Emotion == bat.Emotion) break;
                    sb.Emotion = bat.Emotion;
                    sb.InBattle = bat.Emotion != 0;
                    sb.Pc.BattleStance = sb.InBattle;   // vfunc_65 (m_unk_160), PlayEffects' run test
                    RefreshWeapons(sb);
                    EnsureBar(sb)?.Stance(sb.InBattle, sb.HpCur, sb.HpMax);
                    BattleStanceChanged?.Invoke(bat.ObjectId, sb.Emotion);
                    break;
                case UserStateEvent us:
                    // OnUserState (scenestategame.cpp:5759-5771): vfunc_59(state) twice, or vfunc_137 after state 12
                    if (users.TryGetValue(us.ObjectId, out var uu)) uu.Pc.States.UserState(us.State, uu.HpCur, uu.HpMax);
                    break;
                case UserDeadEvent ud:
                    // OnDeadUser (scenestategame.cpp:6533-6544): a set flag is FUN_141661ba0(1), then vfunc_137, which sets
                    // m_n10f0 when it ran with a confirm count (pc.cpp:4598)
                    if (!users.TryGetValue(ud.ObjectId, out var du)) break;
                    if (ud.Flag) du.Death.Revival.Flag = true;
                    if (du.Pc.States.Die(du.HpCur)) du.Death.Revival.Died();
                    break;
                case UserRevivalEvent rv:
                    // OnRevival (scenestategame.cpp:7387-7409): FUN_141661ba0(flag), FUN_14166ce50(0); the 0x18 state is the own
                    // player's only; then any revived PC's FUN_141661370(1, UserRevivalInvincibleTick, 100000004)
                    if (!users.TryGetValue(rv.ObjectId, out var vu)) break;
                    vu.Death.Revival.Flag = rv.Flag != 0;
                    vu.Pc.States.SetDead(false);
                    StartInvincible(vu);
                    break;
                case UserRevivalConfirmEvent rc:
                    // OnRevivalConfirm (scenestategame.cpp:7356): m_n10ec and m_n10e8 for any PC (expired in _Process); the effect is MyPC's
                    if (users.TryGetValue(rc.ObjectId, out var cu)) cu.Death.Revival.Confirm(rc.EndTick, rc.Count);
                    break;
                case UserTombstoneEvent tb:
                    // OnTombstone (scenestategame.cpp:9644) -> PC::FUN_14165d300 (pc.cpp:3464): the tomb's hit or break
                    // and its bar's stage. STILL-OPEN: the hitter display of a break (kind 0) and the pet sound.
                    if (users.TryGetValue(tb.ObjectId, out var tu)) tu.Death.Hit(tb.Kind, tb.Value);
                    break;
                case SkillDamageEvent dmg:
                    // FUN_141671c20 (pcbase.cpp:3922) calls vfunc_112 whenever the target has an entry, a miss (type 2) too
                    // (AddDamage keeps every pair, skillrecord.cpp:554) and whatever the total, when the packet's attack
                    // has damage (HasDamage, skillattackdata.cpp:384; an unknown skill or level fails CDamageRecord::Init).
                    // A dot (mode 3, OnDotDamageRecord pcbase.cpp:3958) has no such gate. STILL-OPEN: modes 5 and 6
                    // (OnSplashDamageRecord :3970 tests its region attack's HasDamage, not read here)
                    bool byLocal = dmg.Damage.OwnerId == Tracker.SelfObjectId;
                    // what the owner id finds (WorldService::FUN_141bd1200): the hit sound's attack material and relation.
                    // STILL-OPEN: a field object that is neither a PC nor an npc (GetAttackMaterial takes its attack's
                    // material, its own object kind) counts as nothing here
                    var owner = byLocal || users.ContainsKey(dmg.Damage.OwnerId) ? Ms2.Ui.Skills.HitOwner.Pc
                        : npcs.Contains(dmg.Damage.OwnerId) ? Ms2.Ui.Skills.HitOwner.Npc : Ms2.Ui.Skills.HitOwner.None;
                    if (dmg.Damage.Mode == 1) ReactToHits(dmg.Damage);
                    if (dmg.Damage.Mode != 1 || Ms2.Ui.Hud.DamageNumbers.HasDamage(dmg.Damage, SkillDamage))
                    {
                        foreach (var t in dmg.Damage.Targets)
                        {
                            if (t.Hits.Length == 0) continue;
                            npcs.Damaged(t.TargetId, byLocal);
                            // CNpc::vfunc_33 for a cast's hit (mode 1), vfunc_34 for a dot's (3), vfunc_35 for a splash's (5, 6).
                            // STILL-OPEN: the client runs a cast's when its magic model lands the record
                            // (CMagicService::FUN_14146b440, scenestategame.cpp:5649), not when the packet comes in; and it
                            // runs for every record whose skill and level are found (CDamageRecord::Init, skillrecord.cpp:483),
                            // with no hits or without the attack's HasDamage too, which the gates above leave out
                            bool cast = dmg.Damage.Mode == 1;
                            var kind = cast ? NpcHitKind.Cast : dmg.Damage.Mode == 3 ? NpcHitKind.Dot : NpcHitKind.Splash;
                            attackOf ??= (s, l, m, a) => SkillDamage?.Get(s, l, m, a);
                            npcs.Hit(t.TargetId, kind, Ms2.Ui.Skills.NpcVibrate.TotalDamage(t.Hits), byLocal, owner,
                                Ms2.Ui.Skills.NpcHitSound.RecordAttack(dmg.Damage, RegionSkillOf, attackOf));
                            // PCBase::vfunc_112 goes through vfunc_80: a local hit makes the member's bar, then shows it
                            if (users.TryGetValue(t.TargetId, out var hit)) EnsureBar(hit)?.Hit(byLocal);
                        }
                    }
                    if (Numbered != null) ShowNumbers(dmg.Damage);
                    OnDamageHitEffects(dmg.Damage);
                    OnRemoteMagics(dmg.Damage);
                    break;
                case UserBuffEvent ub:
                    if (users.TryGetValue(ub.Buff.OwnerObjectId, out var rb)) rb.Buffs.Apply(ub.Buff);
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
                    // the corpse stays an object (and keeps its throttle) until the field goes
                    if (!nr.Dead) hitEffects.Remove(nr.ObjectId);
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

    /// <summary>The own tag areas for the other players' tombs (WorldService m_3f0, <see cref="Ms2.Ui.Hud.TombTagPass"/>).</summary>
    public readonly Ms2.Ui.Hud.TombTagPass TombTags = new();
    Ms2.Ui.Hud.TombTarget[] tombTargets = new Ms2.Ui.Hud.TombTarget[8];
    readonly List<Ms2.Ui.Hud.TombHit> tombHits = new();
    int? tombPkField;
    int tombPkMode;

    readonly List<Ms2.Ui.Hud.BreakableHit> markerHits = new();

    /// <summary>
    /// WorldService::FUN_141bc3560's named sensors' loop (worldservice_1.cpp:9262-9316), before the tombs' with the same areas:
    /// the field's breakable markers (LiveSession.Breakables, in field space as the field entities), each hit sending the
    /// Breakable request (0x27).
    /// STILL-OPEN: PlayHitSound of the marker's SoundMaterial.
    /// </summary>
    void RunMarkerTags()
    {
        var live = LiveSession.Instance;
        if (live == null) return;
        if (TombTags.RunMarkers(live.Breakables, markerHits) == 0) return;
        foreach (var h in markerHits) live.SendBreakable(h.Request);
    }

    /// <summary>
    /// WorldService::FUN_141bc3560's tomb loop (worldservice_1.cpp:9319-9375), run from the world tick only while an area is
    /// registered: every other PC's standing tomb, at its node's place in client space (z up, unrotated: PC::FUN_14165cac0 places
    /// it at the PC); each hit sends the Tombstone request (0x2A). A tomb is skipped while its PC is hostile to the local player
    /// (TombTagPass.HostileInField of the field's pkMode, read once per field).
    /// STILL-OPEN: PlayHitSound of the tomb's SoundMaterial.
    /// </summary>
    void RunTombTags()
    {
        int? fieldId = LiveSession.Instance?.FieldId;
        if (fieldId != tombPkField)
        {
            tombPkField = fieldId;
            tombPkMode = 0;
            try { if (fieldId is int fid) tombPkMode = FieldData.FindById(packs, fid)?.PkMode ?? 0; }
            catch (Exception ex) { GD.PushWarning($"tomb tags field data: {ex.Message}"); }
        }
        bool hostile = Ms2.Ui.Hud.TombTagPass.HostileInField(tombPkMode);
        int n = 0;
        foreach (var (id, r) in users)
        {
            var tomb = r.Death.Tomb;
            if (tomb == null || !IsInstanceValid(tomb) || !tomb.IsInsideTree() || tomb.State == null) continue;
            if (n == tombTargets.Length) Array.Resize(ref tombTargets, n * 2);
            var g = tomb.GlobalPosition;
            tombTargets[n++] = new Ms2.Ui.Hud.TombTarget(id, hostile, tomb.State, new System.Numerics.Vector3(g.X, -g.Z, g.Y), System.Numerics.Vector3.Zero);
        }
        int hits = TombTags.Run(new ReadOnlySpan<Ms2.Ui.Hud.TombTarget>(tombTargets, 0, n), tombHits);
        Array.Clear(tombTargets, 0, n);
        if (hits == 0) return;
        foreach (var h in tombHits) LiveSession.Instance?.SendRevival(h.Request);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        Drain();
        // PCBase::vfunc_53 for each PC, once per drawn frame with that frame's time (the local player's step)
        // and PC's update for each (pc.cpp:4468-4470): FUN_1416615f0, the invincible blink and end, then FUN_141661ac0, the
        // confirm count expiring at its end tick
        uint now = (uint)Time.GetTicksMsec();
        uint tick = (uint)(LiveSession.Instance?.ServerTick ?? 0);
        foreach (var a in users.Values)
        {
            a.Pc.Tick(dt);
            if (a.Ridee != null) TickRide(a, dt);
            if (a.Invincible.Blink.Active && a.Invincible.Tick(now, a.Visual)) a.Invincible.End(a.Visual, RegionFx);
            a.Death.Revival.Expire(tick);
        }
        TickRemoteMagics(now);
        LiveSession.Instance?.Breakables.Update(now);
        if (TombTags.Count > 0) { RunMarkerTags(); RunTombTags(); }
        CodiTick(dt);
        npcs.Process(dt);
        combat.Process(dt);
    }
}
