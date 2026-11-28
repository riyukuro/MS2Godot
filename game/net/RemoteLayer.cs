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
        /// <summary>The PC's CPvPInfo: FieldAddUser's (scenestategame.cpp:11282), then Pvp 'd' (<see cref="SetPvpInfo"/>).</summary>
        public Ms2.Ui.Skills.PvpInfo Pvp;
        /// <summary>The guild name over the head (PC::GetGuildName): FieldAddUser's, then the guild packets' (<see cref="SetGuildName"/>).</summary>
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
        /// <summary>vf36_GetNodePosition (client space): the effect dummy of that name, else the root's position.</summary>
        public Func<string, System.Numerics.Vector3> NodePosition;
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
        /// <summary>The PC's adventure level (vfunc_141: the user block's m_530, then OnPrestige kind 2's vfunc_140).</summary>
        public int AdventureLevel;
        /// <summary>The user block's job code (PC::GetSubJob), the popup menu header's job icon.</summary>
        public int Job;
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
    /// <summary>The pc whose npc force zones (Player.ForceNpcs) the layer fills each frame: the live session's own player.</summary>
    public Func<Player> ForcedPc = static () => LiveSession.Instance?.OwnPlayer;

    readonly ModelBuilder builder;
    readonly EquipApplier equip;
    readonly NpcLayer npcs;
    readonly NpcTable npcTable;
    /// <summary>FindNpcById(id)->GetNpcClass (@0x1410934c0): the npc row's class (CNpcData m_nClass), 0 without an npc or row.</summary>
    public int NpcClassOf(int objectId) => Tracker.NpcIdOf(objectId) is var id and not 0 && npcTable?.Find(id) is { } r ? r.Class : 0;
    readonly CombatLayer combat;
    readonly LiveNameTags tags;
    readonly LiveHpBars bars;
    readonly LiveBuffBalloons balloons;
    readonly PackSet packs;
    readonly Dictionary<int, Remote> users = new();
    /// <summary>Per gender: the body model, its action sequences and the animation library they are in, shared by the PCs
    /// of that gender (the first one's player loads them).</summary>
    readonly Dictionary<byte, (ModelData md, ActionAnims anims, AnimationLibrary lib)> bodies = new();
    float gravity;
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
    /// <summary><see cref="NpcLayer.NearestTalkable"/>.</summary>
    public World.FieldNpcs.Placed NearestTalkNpc(int friendly, Vector3 me, Vector3 face, float range, bool frontOnly) =>
        npcs.NearestTalkable(friendly, me, face, range, frontOnly);

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
    /// <summary>A field pet made (OnFieldAddPet once OnObjectCreated took it): the owner part runs in the session.</summary>
    public Action<Ms2.Net.Packets.FieldAddPet> PetAdded;
    /// <summary>WorldService::FindPCById finds another player of the id.</summary>
    public bool HasUser(int objectId) => users.ContainsKey(objectId);
    /// <summary>AddHideReason / RemoveHideReason on a field npc or pet; false when it is not here.</summary>
    public bool SetNpcHideReason(int objectId, int reason, bool on) => npcs.SetHideReason(objectId, reason, on);
    /// <summary>PCBase::IsHidden of a field npc or pet.</summary>
    public bool NpcHidden(int objectId) => npcs.IsHidden(objectId);
    /// <summary>A shown player's position in the client's z-up coordinates (WorldService::FindPCById then vfunc_18); null when it is not here.</summary>
    public System.Numerics.Vector3? PositionOf(int objectId) =>
        users.TryGetValue(objectId, out var r) && r.Root != null && GodotObject.IsInstanceValid(r.Root) && r.Root.IsInsideTree()
            ? new System.Numerics.Vector3(r.Root.GlobalPosition.X, -r.Root.GlobalPosition.Z, r.Root.GlobalPosition.Y) : null;

    /// <summary>The first other player's tomb that stands in the field (in the tree, with its state), the one the tomb
    /// pass would test (<see cref="RunTombTags"/>); null without one. For the scripted live runs.</summary>
    public PlayerTombstone StandingTomb()
    {
        foreach (var r in users.Values)
            if (r.Death.Tomb is { } t && IsInstanceValid(t) && t.IsInsideTree() && t.State != null) return t;
        return null;
    }

    /// <summary>The Insignia packet on another player (CSceneStateGame::OnNametag, scenestategame.cpp:7314-7331: FindPCById, then
    /// PC::FUN_141661dc0 and the name tag's refresh, <see cref="LiveNameTags.SetSymbol"/>); false when the player is not here.</summary>
    public bool SetSymbol(int objectId, ushort symbol, bool shown)
    {
        CatchUp(objectId, npc: false);
        return users.TryGetValue(objectId, out var r) && tags.SetSymbol(r.Root, symbol, shown);
    }

    /// <summary>
    /// A guild packet's name over another player's head (CGuildManager FUN_1413c7700 / FUN_1413c7810, guildmanager.cpp:3108 /
    /// :3121): the first PC of that name in the field (WorldService::FUN_141bd1420, an exact name match) takes it
    /// (PC::vfunc_129) and its name tag is set again (<see cref="LiveNameTags.SetGuild"/>). False when no other player here has the name.
    /// </summary>
    public bool SetGuildName(string name, string guildName)
    {
        if (string.IsNullOrEmpty(name)) return false;
        int pending = -1;                     // a player of the name still queued is built first, as SetSymbol does
        foreach (var ev in Tracker.Events)
            if (ev is UserAddedEvent u && u.User.User?.Name == name) pending = u.User.ObjectId;
        if (pending >= 0) CatchUp(pending, npc: false);
        foreach (var r in users.Values)
        {
            if (r.Name != name) continue;
            r.GuildName = guildName ?? "";
            tags.SetGuild(r.Root, r.GuildName);
            return true;
        }
        return false;
    }

    /// <summary>The guild line another player's name tag shows (the live run's view), null when no player here has the name.</summary>
    public string ShownGuild(string name)
    {
        foreach (var r in users.Values) if (r.Name == name) return tags.ShownGuild(r.Root);
        return null;
    }

    /// <summary>NpcNotice mode 1 on an npc (<see cref="NpcLayer.SetOwnerEffect"/>); false when there is no npc of the id.</summary>
    public bool SetOwnerEffect(int objectId, string effect)
    {
        CatchUp(objectId, npc: true);
        return npcs.SetOwnerEffect(objectId, effect);
    }

    /// <summary>NpcNotice mode 2 on an npc (<see cref="NpcLayer.ChangeModel"/>); false when there is no npc of the id.</summary>
    public bool ChangeNpcModel(int objectId, string kfm)
    {
        CatchUp(objectId, npc: true);
        return npcs.ChangeModel(objectId, kfm);
    }

    /// <summary>NpcRide on an npc (<see cref="NpcLayer.Ride"/>); false when there is no npc of the id.</summary>
    public bool NpcRide(Ms2.Net.Packets.NpcRide p)
    {
        CatchUp(p.ObjectId, npc: true);
        return npcs.Ride(p);
    }

    /// <summary>An npc's ride state (<see cref="NpcLayer.RideOf"/>).</summary>
    public (int Owner, long AccumDamage, Vector3 At)? NpcRideOf(int id) => npcs.RideOf(id);

    /// <summary>
    /// Applies the queued tracker events up to the last one that adds or removes the object a packet names, before that packet
    /// runs. The client handles each packet in order, so a packet that names an object an earlier packet made finds it even when
    /// both came in one frame: OnFollowNpc (scenestategame.cpp:5862-5869, then StateTriggerFollowNpc::vfunc_4's FindNpcById,
    /// stateemotionidle.cpp:497-519), OnNpcNotice mode 1 (FindNpcById, :9158-9166) and OnNametag (FindPCById, :7314-7331).
    /// Here the FieldAddNpc or FieldAddUser before it went to the tracker's queue, which _Process empties only on the next frame
    /// (<see cref="Drain"/>'s budget of 200 a frame). The Maple2 server sends the follow npc's FieldAddNpc right before its
    /// FollowNpc. Only the events before that add run now (they came first, so they apply first); the rest of a field entry's
    /// burst stays for the frames' budget, and with no queued event naming the object nothing runs.
    /// </summary>
    public void CatchUp(int objectId, bool npc)
    {
        int n = 0, upTo = 0;
        foreach (var ev in Tracker.Events)
        {
            n++;
            if (npc ? ev is NpcAddedEvent a && a.Npc.ObjectId == objectId || ev is NpcRemovedEvent r && r.ObjectId == objectId
                    : ev is UserAddedEvent u && u.User.ObjectId == objectId || ev is UserRemovedEvent g && g.ObjectId == objectId)
                upTo = n;
        }
        if (upTo > 0) Drain(upTo);
    }

    /// <summary>An npc for the own pc's StateTriggerFollowNpc (<see cref="NpcLayer.FollowPoseOf"/>); null when it is not here.</summary>
    public (System.Numerics.Vector3 Pos, float RotZ, short AnimKey)? FollowPoseOf(int objectId) => npcs.FollowPoseOf(objectId);

    /// <summary>The symbol another player's name tag shows (<see cref="LiveNameTags.ShownSymbol"/>); 0 when none or not here.</summary>
    /// <summary>WorldService::FUN_141bcabf0 as field property 5 applies: every pc's tag takes its symbol again.</summary>
    public void RefreshFieldSymbols() => tags.RefreshFieldSymbols();

    public ushort ShownSymbol(int objectId) => users.TryGetValue(objectId, out var r) ? tags.ShownSymbol(r.Root) : (ushort)0;

    /// <summary>WorldService::FindPCById: a shown player as a damage number's target; null when it is not a player here.</summary>
    public Ui.DamageTarget DamageOf(int objectId) => users.TryGetValue(objectId, out var r) ? r.Damage : null;

    /// <summary>
    /// WorldService::FUN_141bd19d0 (GetObjectKind, worldservice_1.cpp:6304) of an object id (DamageHitEffects.KindOf's
    /// kinds): 1 the own PC, 3 another player, 6 a boss npc, 5 any other npc, 0 none here.
    /// STILL-OPEN: a pet (4, or 2 for the own player's) is 0 here; the field's pets are not looked up by object id.
    /// </summary>
    public int ObjectKind(int objectId, int selfObjectId)
    {
        if (objectId == selfObjectId && selfObjectId != 0) return 1;
        if (users.ContainsKey(objectId)) return 3;
        if (npcs.Placed(objectId) == null) return 0;
        return Tracker.NpcLayoutOf(Tracker.NpcIdOf(objectId)).IsBoss ? 6 : 5;
    }

    /// <summary>WorldService::FindNpcById: whether an npc of the object id is here.</summary>
    public bool HasNpc(int objectId) => npcs.Placed(objectId) != null;
    /// <summary>WorldService::FUN_141bd3ae0: every npc's quest mask again (the quest manager's dirty flag).</summary>
    public void UpdateNpcQuestVisibility() => npcs.UpdateQuestVisibility();
    /// <summary>A placed npc's or pet's position in Godot coordinates; null when it is not here. For the scripted live runs.</summary>
    public Vector3? NpcGodotPosition(int objectId) =>
        npcs.Placed(objectId)?.Root is { } r && GodotObject.IsInstanceValid(r) && r.IsInsideTree() ? r.GlobalPosition : null;
    /// <summary>The trigger's face emotion on the npc of that object id (<see cref="NpcLayer.TriggerFace"/>); false when none.</summary>
    public bool TriggerNpcFace(int objectId, string name) => npcs.TriggerFace(objectId, name);
    /// <summary>The npc's face emotion by object id (a live check's view); null when there is none.</summary>
    public Ms2.Ui.FaceEmotion NpcFace(int objectId) => npcs.Face(objectId);
    /// <summary>The npc's built body by object id; null when there is none.</summary>
    public Node3D NpcBody(int objectId) => npcs.ModelOf(objectId)?.Body;
    /// <summary>An npc's id and hp pair by object id (<see cref="NpcLayer.HpOf"/>); null when the field has none.</summary>
    public (int NpcId, long Hp, long MaxHp)? NpcHp(int objectId) => npcs.HpOf(objectId);

    /// <summary>OnExpUp's gain of an npc (<see cref="NpcLayer.AddExp"/>); false when there is no npc of the id.</summary>
    public bool AddNpcExp(int objectId, long exp) => npcs.AddExp(objectId, exp);

    /// <summary>An npc's exp shown at the end of its death sequence (<see cref="NpcLayer.ExpShown"/>).</summary>
    public Action<World.FieldNpcs.Placed, long> NpcExpShown { set => npcs.ExpShown = value; }
    /// <summary>An npc made and an npc removed (<see cref="NpcLayer.Added"/>, <see cref="NpcLayer.Removed"/>).</summary>
    public Action<World.FieldNpcs.Placed> NpcAdded { set => npcs.Added = value; }
    public Action<int> NpcRemoved { set => npcs.Removed = value; }

    /// <summary>
    /// The field's effect nif table (WorldService m_848) on this layer's npcs: an npc of the id is the loaded object, the attach
    /// and detach are the npc's (<see cref="NpcLayer.AttachEffectNif"/>), and an npc added later gets its entry. A freed layer
    /// finds no object.
    /// </summary>
    public void UseEffectNifs(Ms2.Ui.FieldEffectNifs table)
    {
        table.Loaded = id => IsInstanceValid(this) && npcs.Has(id);
        table.Attach = (id, e) => { if (IsInstanceValid(this)) npcs.AttachEffectNif(id, e); };
        table.Detach = id => { if (IsInstanceValid(this)) npcs.DetachEffectNif(id); };
        npcs.ActorAdded = table.ActorAdded;
    }
    /// <summary>The own player's place for the npcs' hit sounds (<see cref="NpcLayer.Viewer"/>).</summary>
    public Func<Vector3?> Viewer { set => npcs.Viewer = value; }
    /// <summary>The viewer and the s_random stream the npcs' hit sounds use, for the tombs' (FUN_140ade030).</summary>
    internal Vector3? ViewerPosition => npcs.Viewer?.Invoke();
    internal Func<uint> HitSoundRand => npcs.HitSoundRand;
    /// <summary>The field's npcs, dead or not.</summary>
    public IEnumerable<World.FieldNpcs.Placed> Npcs => npcs.All;
    public bool NpcDead(int objectId) => npcs.IsDead(objectId);

    /// <summary>WorldService::FindObjectById then vfunc_18 / vfunc_20 on a shown player or an npc: its position in the client's
    /// z-up coordinates and its rotation z in degrees; null when neither is here.</summary>
    public (System.Numerics.Vector3 Pos, float RotZ)? PoseOf(int objectId) =>
        users.TryGetValue(objectId, out var r) && r.Pc != null ? (r.Pc.Position, r.Pc.RotationZ) : npcs.PoseOf(objectId);

    /// <summary>WorldService::FindObjectById's node: the local player's, a shown player's body or an npc's root; null when
    /// none is here.</summary>
    public Node3D NodeOf(int objectId) =>
        objectId == Tracker.SelfObjectId ? LiveSession.Instance?.OwnPlayer
        : users.TryGetValue(objectId, out var r) && r.Pc != null ? r.Pc.Body : npcs.RootOf(objectId);

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
        Redress(r);
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
        Redress(r);
        return true;
    }

    /// <summary>
    /// The pc's model follows a change of its worn items (EquipApplier.Reapply, only the changed slots), then takes the weapon rig
    /// the new items give and the hold its state asks for: Reapply makes a new rig (SaveRig), and a put-on or put-off of a
    /// weapon rebuilds the hands (PC::vfunc_127 / vfunc_128, pc.cpp:1400-1415, through CEquipment's RefreshModel and
    /// PC::UpdateWeaponAction, pc.cpp:2838-2874), as the own body does (PlayerLook.Redress).
    /// </summary>
    void Redress(Remote r, EquipSlot? force = null)
    {
        equip.Reapply(r.Visual, LookOf(r), force);
        r.Weapons = EquipApplier.RigOf(r.Visual);
        RefreshWeapons(r);
    }

    /// <summary>Another pc's weapon rig (null without one or without the pc).</summary>
    public EquipApplier.WeaponRig WeaponsOf(int objectId) => users.TryGetValue(objectId, out var r) ? r.Weapons : null;

    /// <summary>OnItemUpdate for another player's worn item (the packet read its body again, ItemUpdate.Updated):
    /// CEquipItem's model update (FUN_1411dd1c0) rebuilds that item's model; here the item's slot is rebuilt.</summary>
    public void ItemUpdated(int objectId, Ms2.Net.Packets.EquipItem item)
    {
        if (item == null || !users.TryGetValue(objectId, out var r)) return;
        r.WornAbilities = null;
        Redress(r, item.Slot);
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

    /// <summary>PremiumClub mode 0 for another player (PC::FUN_141662d60 sets m_n1190): false when no such player is shown.</summary>
    public bool SetPremiumTime(int objectId, long endTime)
    {
        if (!users.TryGetValue(objectId, out var r)) return false;
        r.PremiumTime = endTime;
        return true;
    }

    /// <summary>The other players shown now (the own PC is not among them).</summary>
    public IEnumerable<Pc> Pcs => users.Values.Select(r => new Pc(r.ObjectId, r.CharacterId, r.Name ?? "", r.Picture, r.Root, r.Anchor, r.Gems,
        r.PremiumTime, r.PcBangGrade, r.WeddingState));

    readonly NxController.FaceGrids faceGrids;   // the field's, shared with the player's controller

    /// <summary>The npc layer's talk test (UITalkManager::GetTalking) and the local player's client position, for the talk look.</summary>
    public Func<bool> NpcTalking { set => npcs.Talking = value; }
    public Func<System.Numerics.Vector3?> OwnPosition { set => npcs.OwnPosition = value; }

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
        var table = Ms2Godot.World.FieldContents.Npcs(packs);   // the string language, as every npc table user asks (NpcTable.For)
        Tracker.NpcLayoutOf = id => { var r = table.Find(id); return r == null ? default : new NpcLayout(r.IsBoss, r.HasDamagedHpBarTag); };
        npcs = new NpcLayer(packs, mb, table, this)
        {
            SpawnAnimation = id => field.SpawnPoints?.Find(id),
            QuestMasks = () => LiveSession.Instance?.NpcQuestMasks,
            CameraEye = () => IsInsideTree() && GetViewport().GetCamera3D() is { } cam ? cam.GlobalPosition : null,
            // CNpc::AttachOwnerEffect: FindPCById (the own pc's visual or another player's), kept in the region skill node
            FindPc = id => id == 0 ? null : id == Tracker.SelfObjectId ? LiveSession.Instance?.OwnPlayer?.Visual
                : users.TryGetValue(id, out var r) ? r.Visual : null,
            SpawnKept = (fx, pc, kind) => SpawnKeptEffect(fx, pc, kind),
            StopKept = StopKeptEffect,
            RideNodeOf = RideNodeOf,
            ActorPosOf = ActorPosOf,
        };
        npcTable = table;
        combat = new CombatLayer(packs, mb, new EffectTable(packs), this);
        npcs.Combat = combat;
        field.AddChild(tags = new LiveNameTags(packs)
        {
            InParty = id => LiveSession.Instance?.Party is { } p && p.PartyId != 0 && p.Find(id) != null,
            FieldSymbol = () => LiveSession.Instance?.FieldProperties.UserTagSymbol,
            Hostile = PcHostileToOwn,
        });
        npcs.Tags = tags;
        field.AddChild(bars = new LiveHpBars(packs)
        {
            // FUN_1419923f0 re-reads the party per frame for a player's bar; the same lookup the client's IsMemberByCharId does
            InParty = id => LiveSession.Instance?.Party is { PartyId: not 0 } p && p.Find(id) != null,
            Hostile = PcHostileToOwn,
            HasPvpPoint = id => users.TryGetValue(id, out var u) && u.Pvp.HasPoint,
            BuffHidden = id => users.TryGetValue(id, out var u) && (u.Buffs?.IsHidden ?? false),
        });
        npcs.Bars = bars;
        // UIBuffBalloon (0x4ea4): each actor's balloon, asked for by its buff list (PC::vfunc_84, CNpc::vfunc_84)
        field.AddChild(balloons = new LiveBuffBalloons(packs)
        {
            Resolve = BalloonTarget,
            SelfObjectId = () => Tracker.SelfObjectId,
            Viewer = () => LiveSession.Instance?.OwnPlayer,
        });
        if (balloons.Controller is { } balloonCtl) balloonCtl.CameraReady = OwnPcInField;
        // GameObjectController::GetGravity: the field's scene gravity, as the field gives the local player
        // STILL-OPEN: a field without the local player (none in a live field) falls back to the default scene gravity
        gravity = field.GetNodeOrNull<Player>("Player")?.Gravity ?? MoveConfig.SceneGravity;
        field.SceneGravitySet = SetGravity;
    }

    static Vector3 G(System.Numerics.Vector3 p) => new(p.X, p.Z, -p.Y);

    /// <summary>The field's scene gravity changed (a field property of kind 1): the PCs and mounts here and the ones made later
    /// read it, as every controller reads the one NxScene's gravity.</summary>
    void SetGravity(float g)
    {
        gravity = g;
        foreach (var r in users.Values)
        {
            r.Pc.Gravity = g;
            if (r.Ridee != null) r.Ridee.Gravity = g;
        }
    }

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
    /// <summary>The skill attacks' damage blocks (read on the first hit).</summary>
    Ms2.Ui.Skills.SkillDamageTable skillDamage;
    /// <summary>The attack's damage block by skill, level, motion and attack (CDamageRecord::GetAttackData then m_damage),
    /// which NpcHitSound.RecordAttack takes; made on the first hit, null when the attack has none.</summary>
    Func<int, int, int, int, Ms2.Ui.Skills.SkillAttackDamage> attackOf;

    /// <summary>The skill and level of a region skill object of the field by its id (OnRegionSkill mode 0); null for none.</summary>
    public Func<int, (int Skill, short Level)?> RegionSkillOf;

    Ms2.Ui.Skills.SkillDamageTable SkillDamage => skillDamage ??= Ms2.Ui.GameTables.Shared(packs, Ms2Godot.GameOptions.StringLanguage).SkillDamage;
    static readonly Ms2.Formats.Options.GameOption.Battle defaultBattle = new();
    /// <summary>CDamageRecord::GetAttackData then m_damage (<see cref="Ms2.Ui.Skills.SkillDamageTable.Get"/>); null when none.</summary>
    public Ms2.Ui.Skills.SkillAttackDamage AttackDamage(int skillId, int level, int motion, int attack) =>
        SkillDamage.Get(skillId, level, motion, attack);
    /// <summary>CDamageRecord::GetAttackData's attack (SkillLevelData's motion's attack); null when none. Its
    /// <see cref="Ms2.Ui.Skills.SkillAttackData.ArrowRecord"/> is FUN_14175acc0's test.</summary>
    public Ms2.Ui.Skills.SkillAttackData AttackData(int skillId, int level, int motion, int attack)
    {
        var motions = Ms2.Ui.GameTables.Shared(packs, Ms2Godot.GameOptions.StringLanguage).Skills.GetSkillData((uint)skillId)?.GetLevelData(level)?.Motions;
        var m = motions != null && (uint)motion < (uint)motions.Count ? motions[motion] : null;
        return m != null && (uint)attack < (uint)m.Attacks.Count ? m.Attacks[attack] : null;
    }

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
        bool looked = false, arrowRecord = false;
        foreach (var t in d.Targets)
        {
            if (!users.TryGetValue(t.TargetId, out var r) || r.Pc == null) continue;
            if (!looked)
            {
                attack = AttackDamage(d.SkillId, d.Level, d.Motion, d.AttackPoint);
                arrowRecord = AttackData(d.SkillId, d.Level, d.Motion, d.AttackPoint)?.ArrowRecord ?? false;
                looked = true;
            }
            if (attack == null) return;
            long total = 0;
            foreach (var h in t.Hits) total += h.Damage;
            int mapType = HitMapType();
            bool ground = mapType is 9 or 3;
            uint armor = Ms2.Ui.Skills.HitReaction.SuperArmor(r.Pc.ClientStateId, 0, 0);
            var recordDir = arrowRecord ? d.DirectionShort.ToVector3() : System.Numerics.Vector3.Zero;
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
    /// A player target outside the field's sight range gives none (pcbase.cpp:3930); the local player is never outside.
    /// </summary>
    void ShowNumbers(SkillDamage d)
    {
        numbers.Clear();
        pcMask ??= PcMask;
        isObject ??= Tracker.HasObject;
        UpdateMagicPlayerPos();
        pcPosition ??= PcPosition;
        Ms2.Ui.Hud.DamageNumbers.Records(d, numbers, isObject, SkillDamage, pcPosition, MagicSight());
        var o = GameOptions.Current?.BattleOption ?? defaultBattle;
        foreach (var n in numbers)
            if (Ms2.Ui.Hud.DamageNumbers.Shows(o, n.Grade, n.TargetId, n.CasterId, Tracker.SelfObjectId, pcMask))
                Numbered(n, npcs.Placed(n.TargetId), users.TryGetValue(n.TargetId, out var r) ? r.Damage : null);
    }

    Func<int, System.Numerics.Vector3?> pcPosition;

    /// <summary>A player's position in client space (vfunc_18), the local player's too; null for any other id.</summary>
    System.Numerics.Vector3? PcPosition(int id) =>
        users.TryGetValue(id, out var r) && r.Pc != null ? r.Pc.Position : id == Tracker.SelfObjectId ? magicPlayerPos : null;

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
    /// <see cref="NpcLayer.HitCandidates"/>. A player is a pc of its PvP team (PC::vfunc_116, <see cref="Ms2.Ui.Skills.PvpInfo.PcTeam"/>)
    /// and HasPvPPoint with its stat pair's hp, for <see cref="Ms2.Ui.Skills.SkillHit.FilterTarget"/>.
    /// Its party id is the own party's when CPartyManager has it as a member (PC::FUN_141661bc0, pc.cpp:4438), else 0.
    /// Its pvp on/off, forceApplyTarget bits, hidden flag and invincible kind are its buff list's
    /// (<see cref="Ms2.Ui.Skills.AdditionalBuffList"/>); its state id is its state machine's current one.
    /// STILL-OPEN: the states 6 / 7 half of hidden are not fed.
    /// </summary>
    public void HitCandidates(List<Ms2.Ui.Skills.HitCandidate> into)
    {
        var party = LiveSession.Instance?.Party;
        foreach (var (id, r) in users)
            if (r.Pc != null) into.Add(PcCandidate(id, r, party));
        npcs.HitCandidates(into);
    }

    static Ms2.Ui.Skills.HitCandidate PcCandidate(int id, Remote r, PartyBook party)
    {
        var p = r.Pc.Position;
        long partyId = party is { PartyId: not 0 } && party.Find(r.CharacterId) != null ? party.PartyId : 0;
        return new Ms2.Ui.Skills.HitCandidate { Id = id, Position = p, Body = Ms2.Ui.Skills.SkillHit.PlayerBody(p, r.Pc.RotationZ),
            Kind = Ms2.Ui.Skills.HitKind.Pc, Team = r.Pvp.PcTeam, PvPPoint = r.Pvp.HasPoint, Hp = r.HpCur, PartyId = partyId,
            PvpOnOff = r.Buffs.IsPvpOnOff,
            ForceApplyTarget = r.Buffs.ForceApplyTarget, Hidden = r.Buffs.IsHidden,
            StateId = r.Pc.States.Cur, Invincible = r.Buffs.Invincible };
    }

    /// <summary>Another player of the field as <see cref="HitCandidates"/> has it (WorldService::FUN_141bd1200 finding a PC);
    /// false for any other id.</summary>
    public bool TryPcCandidate(int id, out Ms2.Ui.Skills.HitCandidate c)
    {
        if (users.TryGetValue(id, out var r) && r.Pc != null) { c = PcCandidate(id, r, LiveSession.Instance?.Party); return true; }
        c = default;
        return false;
    }

    /// <summary>The object kind of a remote object id: PCBase 3 for a user (pcbase.cpp:1092), an npc's 5 or 6, else 0.</summary>
    public int ObjectKind(int objectId) =>
        users.ContainsKey(objectId) ? Ms2.Audio.SoundMusicEffectState.CasterOtherPc : npcs.CasterKind(objectId);

    /// <summary>An effect xml played at a point (CombatLayer.PlayEffect), the own cast's motion effect.</summary>
    public void PlayEffect(string effect, Vector3 at, float yawRadians, int casterKind)
        => combat.PlayEffect(effect, at, yawRadians, casterKind);

    /// <summary>The same turned by a full rotation (godot space).</summary>
    public void PlayEffect(string effect, Vector3 at, Basis rotation, int casterKind)
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
            if (OutfitShown(r.CodiStart, r.CodiEnd) != r.CodiShown) Redress(r);
        ArmCodi();
    }

    /// <summary>ItemUse mode 5, OnActiveCodiTab (scenestategame.cpp:8663) for another player: SetLayerPeriod, then the
    /// model follows the layer switch. False when the player is not here.</summary>
    public bool ActiveCodiTab(Ms2.Net.Packets.CodiTab t)
    {
        if (!users.TryGetValue(t.ActorId, out var r)) return false;
        r.CodiStart = t.Start; r.CodiEnd = t.End;
        Redress(r);
        ArmCodi();
        return true;
    }

    static string BodyDir(byte gender) => gender == 0 ? "male" : "female";

    Ms2.Formats.World.EmotionTable emotionTable;
    readonly int[] defaultFace = { -1, -1 };

    /// <summary>PC::vfunc_69 (pc.cpp:3094-3108) and CEmotionManager::GetGroup: the worn face item when its model has parts,
    /// else the gender's base face item; its emotion group, null without one.</summary>
    Ms2.Formats.World.EmotionGroup EmotionGroup(int wornFace, byte gender)
    {
        int g = gender == 0 ? 0 : 1;
        int id = wornFace;
        if (id == 0)
        {
            if (defaultFace[g] < 0) defaultFace[g] = Ms2.Ui.FaceEmotion.DefaultFace(packs, g);
            id = defaultFace[g];
        }
        if (id <= 0) return null;
        emotionTable ??= new Ms2.Formats.World.EmotionTable(packs, Ms2.Formats.World.EmotionTable.Item);
        return emotionTable.Group((uint)id);
    }

    /// <summary>SkillDataTableCli::GetSkillData for StateEmotion / StateEmotionIdle: main type, paramStr, offsetNameTag.</summary>
    Ms2.Net.Replay.EmotionRow EmotionRow(int id)
    {
        var s = Ms2.Ui.GameTables.Shared(packs, Ms2Godot.GameOptions.StringLanguage).Skills.GetSkillData((uint)id);
        return s == null ? null : new Ms2.Net.Replay.EmotionRow(s.MainType, s.ParamStr, s.OffsetNameTag, s.OffsetHpBar);
    }


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
        pc.Platforms = LiveSession.Instance?.Breakables;   // the ride on a moving marker (PCBase::vfunc_68)
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
        r.PremiumTime = u.PremiumTime; r.WeddingState = u.WeddingState; r.PcBangGrade = u.User.PcBangGrade; r.Level = u.User.Level; r.Job = u.User.Job;
        r.AdventureLevel = u.User.PrestigeLevel;
        r.Pvp = new Ms2.Ui.Skills.PvpInfo(u.PvpPoint, u.PvpTeam, u.PvpMode);
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
        r.Buffs = new Ms2.Ui.Skills.AdditionalBuffList(levelOf) { Effects = BuffEffects, ServerTick = BuffTick, Balloon = BalloonOf(r.ObjectId) };
        r.Death.BuffsDisableRevival = () => r.Buffs.IsDisableRevivalHere;
        r.Death.Revival.Apply(u);   // OnAddUser (scenestategame.cpp:11280-11281): the confirm end tick and count, the flag
        // OnAddUser (scenestategame.cpp:11189-11192): the new PC's PC::vfunc_135 before its model, after the dead flag (a dead
        // pc gets no mode); its StateFall is the spawn's own. PC::vfunc_12 (pc.cpp:844-855) on the model: the waiting craft is
        // made and the AddUser i32 is held.
        // RebuildGuides' carried model (cubecraft_2.cpp:523-531) on every craft change
        r.Cube.CraftChanged = () =>
        {
            bool built = Look.CarriedModel.Sync(r.Visual, r.Cube.Current, builder, packs, LiveSession.ItemDataOf);
            pc.CarriesGuide2 = r.Cube.InCubeMode && built;
            LiveSession.Instance?.RefreshLiftMarkers();   // cubecraft_2.cpp:416, any pc's craft
        };
        r.Cube.NewPc(); r.Cube.Set(u.CubeMode, u.CraftMode, u.CubeItem, pc.States.Dead); r.Cube.ModelAttached();
        r.Held.AddUser(u.HeldObject, ItemExists?.Invoke(u.HeldObject) ?? true);
        r.Weapons = EquipApplier.RigOf(visual);
        r.Pc.States.WeaponKeyChanged = () => RefreshWeapons(r);
        RefreshWeapons(r);
        var dummies = Ms2.Formats.World.EffectDummyTable.Load(packs).Pc(u.User.Gender);
        EffectAttach.SetDummies(visual, dummies, md.RootName);
        var model = visual.GetNodeOrNull<Node3D>("Model");
        r.Anchor = () => World.DummyAnchor.Point(dummies, "Eff_UI", model, md.RootName) ?? root.GlobalPosition;
        r.NodePosition = node =>
        {
            var at = string.IsNullOrEmpty(node) ? root.GlobalPosition
                : World.DummyAnchor.Point(dummies, node, model, md.RootName) ?? root.GlobalPosition;
            return new System.Numerics.Vector3(at.X, -at.Z, at.Y);
        };
        r.Damage = new Ui.DamageTarget(root, () => World.DummyAnchor.Point(dummies, "Eff_Damage", model, md.RootName) ?? root.GlobalPosition, true);
        pc.States.DeadEntered = () => EnterDead(r);
        // StateEmotion / StateEmotionIdle: the emotion skill's row, the face item's group (PC::vfunc_69), the tag offset
        pc.States.Emotion = pc;
        pc.EmotionRowOf = EmotionRow;
        pc.EmotionGroupOf = () => EmotionGroup(equip.FaceItem(visual), r.Gender);
        pc.NameTagOffset = v => tags.SetExtraOffset(root, v);
        pc.HpBarOffset = v => bars.SetExtraOffset(root, v);
        if (LiveSession.LiveCheck)
            pc.States.StateChanged = s =>
            {
                if (s is RemotePcStates.StateEmotion or RemotePcStates.StateEmotionIdle)
                    GD.Print($"live: other player {r.Name} emotion state {s:x} sub {pc.States.Sub:x}");
            };
        pc.States.DeadLeft = pc.LeaveDead;
        // StateCoupleEmotion: the partner (any PC, the own one too), the genders, the row, the table and the path nifs
        pc.CoupleObjectId = r.ObjectId;
        pc.CoupleGender = r.Gender;
        pc.CouplePartnerOf = AnyCouplePartner;
        pc.CoupleGenderOf = AnyGender;
        pc.CoupleSkillRow = id => Ms2.Ui.GameTables.Shared(packs, GameOptions.StringLanguage).Skills.GetSkillData(id);
        pc.CoupleTable = () => Ms2.Ui.GameTables.Shared(packs, GameOptions.StringLanguage).CoupleEmotions;
        pc.CouplePathLoader = CouplePathLoader;
        pc.HookCouple();
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
    /// OnAddUser's spawn point: the packet's position raised by 25 (kPos.z += 25, scenestategame.cpp:11138, set on the PC by
    /// vfunc_19 at :11146), then RayCastFixedDirection down from there (:11174-11181); the hit raised by 25 when the capsule
    /// there overlaps nothing, else the raised position.
    /// </summary>
    internal Vector3 SpawnPoint(Vector3 packet)
    {
        var at = packet + Vector3.Up * 25f;
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
        if (groundFx == null) AddChild(groundFx = new World.GroundEffects(packs, builder) { CasterKind = 3 });   // another PC's
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

    /// <summary>The Job packet's mode 2 (scenestategame.cpp:7476-7481): the common resource's changeJobs effect
    /// (m_strChangeJobs, CH/Common/EFF_ChangeJobs.xml) on the PC (SetActor), its object kind GetObjectKind: the local
    /// player's 1, another PC's 3. A PC FindPCById does not find plays nothing (OnJob returns at its start). Returns
    /// whether it played.</summary>
    public bool PlayJobChangeEffect(int objectId)
    {
        bool self = objectId == Tracker.SelfObjectId;
        if (!self && !users.ContainsKey(objectId)) return false;
        var actor = NodeOf(objectId);
        if (actor == null) return false;
        string fx = Ms2.Ui.GameTables.Shared(packs, Ms2Godot.GameOptions.StringLanguage).CommonResource("changeJobs");
        combat.PlayEffectOn(fx, actor, self ? Ms2.Audio.SoundMusicEffectState.CasterMyPc : Ms2.Audio.SoundMusicEffectState.CasterOtherPc);
        return true;
    }

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

    /// <summary>PC::vfunc_80's gate (<see cref="Ms2.Ui.Hud.PcHpBarRules.Makes"/>): the party gauge option on (the status
    /// option the client reads at +0xc), and the PC has a PvP point, is a party member (MyPC::IsMemberByCharId) or is
    /// hostile to the local player.</summary>
    bool CanPcBar(Remote r) =>
        Ms2.Ui.Hud.PcHpBarRules.Makes(bars.PartyGauge, r.Pvp.HasPoint,
            LiveSession.Instance?.Party is { PartyId: not 0 } p && p.Find(r.CharacterId) != null, PcHostileToOwn(r));

    /// <summary>The player's bar, made on demand (PC::vfunc_80 is called from UpdateHpBar, OnStat and a hit, so the
    /// entity is made at the first of those, not at the spawn).</summary>
    PcHpBar EnsureBar(Remote r)
    {
        if (r.Bar != null || !CanPcBar(r)) return r.Bar;
        r.Bar = bars.AddPc(r.Root, r.Anchor, r.CharacterId, r.ObjectId);
        bars.Ride(r.Root, r.Ridee?.Mount?.Data);
        bars.SetExtraOffset(r.Root, r.Pc?.HpBarExtra ?? 0);
        return r.Bar;
    }

    void Drain(int budget = 200)
    {
        while (budget-- > 0 && Tracker.Events.TryDequeue(out var ev))
        {
            switch (ev)
            {
                case UserAddedEvent add:
                    if (add.User.User.CharacterId == selfCharacter) break;
                    // a replaced or leaving PC: ~PC frees its kept invincible effect m_n1060 (pc.cpp:764-768)
                    if (users.Remove(add.User.ObjectId, out var old)) { old.Buffs?.ClearAll(); remoteMagics.Remove(old.ObjectId); hitEffects.Remove(old.ObjectId); old.Invincible.End(null, RegionFx); tags.Remove(old.Root); bars.Remove(old.Root); old.Death.Free(); FreeRidee(old); old.Pc.Free(); }
                    if (old != null) PcLeft?.Invoke(old.ObjectId);
                    var added = users[add.User.ObjectId] = Spawn(add.User); UsersSpawned++; ArmCodi();
                    // FieldAddUser's ride (PCBase::SetRideOnAction) and its passengers, ridden at the PC's creation
                    // (PCBase::vfunc_10 -> vfunc_120(GetRideOnAction()), pcbase.cpp:921)
                    if (add.User.Ride != null) Ms2.Ui.RidePassengers.Load(add.User.Passengers, added.Passengers);
                    RideOn(added, add.User.Ride);
                    AddUserBuffs(added, add.User);
                    PcShown?.Invoke(Shown(added));
                    if (LiveSession.LiveCheck) GD.Print($"live: other player '{add.User.User.Name}' object {add.User.ObjectId} shown at {add.User.Position}");
                    break;
                case UserRemovedEvent rem:
                    // PCBase::vfunc_14 rides a riding PC off (vfunc_121(null), pcbase.cpp:855-859), whose ride-off effect
                    // hangs on the PC's own model ("Scene Root", attachToNode: CNifEffect::AttachToModelNode,
                    // nifeffectparam.cpp:1079-1108), which leaves the scene with the PC, and its sound is onlyCasterMyPC:
                    // nothing shows, so FreeRidee plays no effect
                    if (users.Remove(rem.ObjectId, out var gone))
                    {
                        gone.Invincible.End(null, RegionFx);   // ~PC, pc.cpp:764-768
                        gone.Buffs?.ClearAll();                  // ~AdditionalCliList: the keep effects removed
                        remoteMagics.Remove(rem.ObjectId);
                        hitEffects.Remove(rem.ObjectId);
                        tags.Remove(gone.Root); bars.Remove(gone.Root); gone.Death.Free(); FreeRidee(gone); gone.Pc.Free();
                        PcLeft?.Invoke(gone.ObjectId);
                        if (LiveSession.LiveCheck) GD.Print($"live: other player {gone.Name} (object {rem.ObjectId}) left");
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
                    if (++movedSeen <= 3 || movedSeen % 100 == 0) if (LiveSession.LiveCheck)
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
                    // OnUserBattle -> SetEmotion, skipped in StateWaitingSelect (GetCurStateId() != 0xe, scenestategame.cpp:3276):
                    // the change guard (pcbase.cpp:1268) runs UpdateHpBar only on a change. STILL-OPEN: the
                    // ridee's and the own target's emotion (spRidee / FUN_141bd3620) need ridees and pets.
                    if (!users.TryGetValue(bat.ObjectId, out var sb)) break;
                    if (sb.Pc.States.Cur == 0xe || sb.Emotion == bat.Emotion) break;
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
                    // and its bar's stage, after FUN_140ade030's hit sound (relation 3: not the local player, so no event voice).
                    // STILL-OPEN: the pet part of OnTombstone (the pet's vfunc_59(1, 0, 0x11, 0) and the system sound 02100201
                    // while CPet::vfunc_127 holds): the field's pets have no object here, and vfunc_127 is not recovered.
                    if (users.TryGetValue(tb.ObjectId, out var tu)) tu.Death.Hit(tb.Kind, tb.Value, tb.Hitter, World.FieldContents.Constants(packs), ViewerPosition, HitSoundRand);
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
                    bool ownerBoss = owner == Ms2.Ui.Skills.HitOwner.Npc && npcs.CasterKind(dmg.Damage.OwnerId) == Ms2.Audio.SoundMusicEffectState.CasterBoss;
                    if (dmg.Damage.Mode == 1) ReactToHits(dmg.Damage);
                    if (dmg.Damage.Mode != 1 || Ms2.Ui.Hud.DamageNumbers.HasDamage(dmg.Damage, SkillDamage))
                    {
                        foreach (var t in dmg.Damage.Targets)
                        {
                            if (t.Hits.Length == 0) continue;
                            npcs.Damaged(t.TargetId, byLocal);
                            attackOf ??= (s, l, m, a) => SkillDamage?.Get(s, l, m, a);
                            PlayHitInvokeEffects(t.TargetId, dmg.Damage.Mode, dmg.Damage.Mode == 3 ? null
                                : Ms2.Ui.Skills.NpcHitSound.RecordAttack(dmg.Damage, RegionSkillOf, attackOf));
                            // CNpc::vfunc_33 for a cast's hit (mode 1), vfunc_34 for a dot's (3), vfunc_35 for a splash's (5, 6).
                            // STILL-OPEN: the client runs a cast's when its magic model lands the record
                            // (CMagicService::FUN_14146b440, scenestategame.cpp:5649), not when the packet comes in; and it
                            // runs for every record whose skill and level are found (CDamageRecord::Init, skillrecord.cpp:483),
                            // with no hits or without the attack's HasDamage too, which the gates above leave out
                            bool cast = dmg.Damage.Mode == 1;
                            var kind = cast ? NpcHitKind.Cast : dmg.Damage.Mode == 3 ? NpcHitKind.Dot : NpcHitKind.Splash;
                            attackOf ??= (s, l, m, a) => SkillDamage?.Get(s, l, m, a);
                            npcs.Hit(t.TargetId, kind, Ms2.Ui.Skills.NpcVibrate.TotalDamage(t.Hits), byLocal, owner,
                                Ms2.Ui.Skills.NpcHitSound.RecordAttack(dmg.Damage, RegionSkillOf, attackOf), ownerBoss);
                            // PCBase::vfunc_112 goes through vfunc_80: a local hit makes the member's bar, then shows it
                            if (users.TryGetValue(t.TargetId, out var hit)) EnsureBar(hit)?.Hit(byLocal);
                            PlayRecordSounds(dmg.Damage, t, owner, byLocal, ownerBoss);
                        }
                    }
                    if (Numbered != null) ShowNumbers(dmg.Damage);
                    OnDamageHitEffects(dmg.Damage);
                    OnRemoteMagics(dmg.Damage);
                    break;
                case UserBuffEvent ub:
                    // OnBuff for another player or an npc (FUN_141bd1200 finds both); the own PC's list took it at once
                    if (ub.Buff.OwnerObjectId == Tracker.SelfObjectId) { if (ub.Buff.Mode == Ms2.Net.Packets.BuffPacket.Remove) ownRemovals.Drained(ub.Buff.Key); }
                    else if (users.TryGetValue(ub.Buff.OwnerObjectId, out var rb)) rb.Buffs.Apply(ub.Buff);
                    else if (npcBuffs.TryGetValue(ub.Buff.OwnerObjectId, out var nb)) nb.Apply(ub.Buff);
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
                    AddNpcBuffs(na.Npc);
                    // the talkable byte FieldAddNpc reads (FUN_14174a560, scenestategame.cpp:3561) for InteractNpcAction
                    LiveSession.Instance?.InteractNpcs?.Add(na.Npc.ObjectId, npcTable.Find(na.Npc.NpcId), na.Npc.Talkable,
                        new Vec3(na.Npc.Position.X, na.Npc.Position.Y, na.Npc.Position.Z));
                    if (na.Pet != null && npcs.Placed(na.Npc.ObjectId) != null) PetAdded?.Invoke(na.Pet);   // OnFieldAddPet after OnObjectCreated (scenestategame.cpp:9710-9731)
                    break;
                case NpcRemovedEvent nr:
                    npcs.Remove(nr.ObjectId, nr.Dead);
                    // FieldDeadNpc keeps the corpse's buffs and their keep effects (CNpc::vfunc_125 leaves the AdditionalCliList)
                    if (!nr.Dead) DropNpcBuffs(nr.ObjectId);
                    // the corpse stays an object (and keeps its throttle) until a later FieldRemoveNpc removes the body, or the field goes
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
    /// Breakable request (0x27), after the marker's hit sound (<see cref="PlayTagHitSound"/>).
    /// </summary>
    void RunMarkerTags()
    {
        var live = LiveSession.Instance;
        if (live == null) return;
        if (TombTags.RunMarkers(live.Breakables, markerHits) == 0) return;
        foreach (var h in markerHits)
        {
            PlayTagHitSound(h.HitSound, h.Position);   // FUN_140ade030(1, 1) before the packet (worldservice_1.cpp:9301)
            live.SendBreakable(h.Request);
        }
    }

    readonly List<Ms2.Ui.Hud.VibrateHit> vibrateHits = new();

    /// <summary>
    /// WorldService::FUN_141bc3560's vibrate objects' loop (worldservice_1.cpp:9181-9240), the first of the pass: the field's
    /// vibrate objects (LiveSession.Vibrates, in field space), each hit sending the Vibrate request (0x26) with the local
    /// player's position.
    /// STILL-OPEN: PlayHitSound of the object's SoundMaterial.
    /// </summary>
    void RunVibrateTags()
    {
        var live = LiveSession.Instance;
        if (live == null || live.Vibrates.Count == 0) return;
        var own = live.OwnPlayer;
        if (own == null || !IsInstanceValid(own)) return;
        var g = own.GlobalPosition;
        if (TombTags.RunVibrates(live.Vibrates, new System.Numerics.Vector3(g.X, -g.Z, g.Y), vibrateHits) == 0) return;
        foreach (var h in vibrateHits) live.SendVibrate(h.Request);
    }

    /// <summary>
    /// WorldService::FUN_141bc3560's tomb loop (worldservice_1.cpp:9319-9375), run from the world tick only while an area is
    /// registered: every other PC's standing tomb, at its node's place in client space (z up, unrotated: PC::FUN_14165cac0 places
    /// it at the PC); each hit sends the Tombstone request (0x2A). A tomb is skipped while its PC is hostile to the local player
    /// (TombTagPass.HostileInField of the field's pkMode, read once per field), after the tomb's hit sound
    /// (<see cref="PlayTagHitSound"/>).
    /// </summary>
    void RunTombTags()
    {
        bool hostile = Ms2.Ui.Hud.TombTagPass.HostileInField(FieldPkMode());
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
        foreach (var h in tombHits)
        {
            PlayTagHitSound(h.HitSound, h.Position);   // FUN_140ade030(1, 1) before the packet (worldservice_1.cpp:9367)
            LiveSession.Instance?.SendRevival(h.Request);
        }
    }

    /// <summary>
    /// CBreakableObject::FUN_140ade030(1, 1) (breakableobject.cpp:233) for a tag area's hit on a tomb or a field marker
    /// (worldservice_1.cpp:9301, :9367): PlayHitSound of attack material 1 and the object's SoundMaterial, relation 1 (the
    /// local player's hit), at VolumeMyPcToBreakableObject, at the object's place (client space, z up).
    /// </summary>
    void PlayTagHitSound(int soundMaterial, System.Numerics.Vector3 clientPosition)
    {
        float volume = Ms2.Ui.Hud.Tombstone.HitVolume(1, World.FieldContents.Constants(packs));
        var at = new Vector3(clientPosition.X, clientPosition.Z, -clientPosition.Y);
        bool played = FieldAudio.Instance?.PlayHitSound(1, soundMaterial, 1, at, volume, ViewerPosition, HitSoundRand) ?? false;
        if (LiveSession.LiveCheck) GD.Print($"tomb tags: hit sound attack 1 defence {soundMaterial} relation 1 volume {volume:F2} played {played}");
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
        // the markers' Update(GetTickCountMs2()) (worldservice_1.cpp:9650-9655): the fades and the move cycles, before the PCs
        // step and ride them (the actor loop, gameobject.cpp:307-312); done already when the field's MarkerStep ran first
        LiveSession.Instance?.Breakables.UpdateOnce(Engine.GetProcessFrames(), now, unchecked((int)tick));
        foreach (var a in users.Values)
        {
            a.Pc.Tick(dt);
            if (a.Ridee != null) TickRide(a, dt);
            if (a.Invincible.Blink.Active && a.Invincible.Tick(now, a.Visual)) a.Invincible.End(a.Visual, RegionFx);
            a.Death.Revival.Expire(tick);
        }
        TickBuffEffects();
        TickRemoteMagics(now);
        LiveSession.Instance?.Vibrates.Update(tick);   // the world tick's vibrators (worldservice_1.cpp:9243-9250)
        if (TombTags.Count > 0) { RunVibrateTags(); RunMarkerTags(); RunTombTags(); }
        CodiTick(dt);
        npcs.Process(dt);
        if (ForcedPc() is { } own) npcs.ForceSources(own.ForceNpcs);   // FUN_141bc8420's npc zones
        combat.Process(dt);
    }
}
