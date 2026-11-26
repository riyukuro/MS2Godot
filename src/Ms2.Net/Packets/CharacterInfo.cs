using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// AttributePoint (0x45; CSceneStateGame::OnAttributePoint, scenestategame.cpp:4270, which hands it to the local player's
/// CStatPointManager::OnPacket, statpointmanager.cpp:33): mode 0 the reward points (LoadExtra), mode 1 the points put into
/// stats (LoadStats). Other modes are ignored by the client and read as null here.
/// </summary>
public abstract record AttributePointMessage {
    /// <summary>LoadExtra (:49): i32 total, i32 count, then per entry i32 key, i32 value.</summary>
    public sealed record Extra(int Total, IReadOnlyList<(int Key, uint Value)> Values) : AttributePointMessage;
    /// <summary>LoadStats (:66): i32 total, i32 count, then per entry u8 stat, i32 points.</summary>
    public sealed record Stats(int Total, IReadOnlyList<(byte Stat, int Points)> Points) : AttributePointMessage;

    public static AttributePointMessage Read(ByteReader r) {
        switch (r.ReadByte()) {
            case 0: {
                int total = r.ReadInt(); uint n = (uint) r.ReadInt();
                var v = new List<(int, uint)>();
                for (uint i = 0; i < n; i++) v.Add((r.ReadInt(), (uint) r.ReadInt()));
                return new Extra(total, v);
            }
            case 1: {
                int total = r.ReadInt(); uint n = (uint) r.ReadInt();
                var v = new List<(byte, int)>();
                for (uint i = 0; i < n; i++) v.Add((r.ReadByte(), r.ReadInt()));
                return new Stats(total, v);
            }
        }
        return null;
    }
}

/// <summary>
/// CStatPointManager (client/ms2/statpointmanager.cpp), the local player's attribute points as the packets leave them.
/// Its requests are <see cref="RequestAdd"/> and <see cref="RequestReset"/> (SendOp::AttributePoint modes 2 and 3).
/// </summary>
public sealed class StatPointBook {
    readonly Dictionary<int, int> points = new();
    readonly Dictionary<int, uint> extra = new();
    public int Total { get; private set; }
    public int Used { get; private set; }
    public int Remaining { get; private set; }
    /// <summary>NotifyStatusChanged(0x10) after either load.</summary>
    public event Action Changed;

    public void Apply(AttributePointMessage m) {
        switch (m) {
            case AttributePointMessage.Extra e:              // LoadExtra (:49): the remaining from the used before it
                Total = e.Total;
                Remaining = Total - Used;
                extra.Clear();
                foreach (var (k, v) in e.Values) extra[k] = v;
                break;
            case AttributePointMessage.Stats s:              // LoadStats (:66): used is the sum of the entries
                points.Clear();
                Total = s.Total;
                Used = 0;
                foreach (var (stat, p) in s.Points) { points[stat] = p; Used += p; }
                Remaining = Total - Used;
                break;
            default: return;
        }
        Changed?.Invoke();
    }

    /// <summary>GetStatPoints (:99): 0 for a stat without an entry.</summary>
    public int GetStatPoints(int stat) => points.TryGetValue(stat, out var p) ? p : 0;
    /// <summary>GetExtra (:108).</summary>
    public uint GetExtra(int key) => extra.TryGetValue(key, out var v) ? v : 0;

    /// <summary>GetCostIds (unit_140837660_idcosttable.cpp:59): the stats with a point cost (InitCosts, :28), in key order.</summary>
    public static readonly int[] CostIds = { 0, 1, 2, 3, 4, 0x11 };

    /// <summary>FUN_140837770 (unit_140837660_idcosttable.cpp:49): what <paramref name="count"/> points add to the stat, the
    /// stat's cost (InitCosts :28: 1 for str, dex, int and luk, 10 for health, 3 for 0x11) times the count; 0 for a stat
    /// without a cost.</summary>
    public static ulong PointValue(int stat, uint count) => stat switch
    {
        0 or 1 or 2 or 3 => count,
        4 => 10UL * count,
        0x11 => 3UL * count,
        _ => 0,
    };

    /// <summary>
    /// RequestAdd @0x140caa280 (statpointmanager.cpp:124): nothing with no points remaining; when the stat's points plus
    /// <paramref name="amount"/> pass its cap <paramref name="limit"/> (Client::Constants StatPointLimit[stat], both unsigned)
    /// no request and <paramref name="overLimit"/> (the caller's system notice s_char_info_limit_stat_point); else mode 2.
    /// </summary>
    public IOutPacket RequestAdd(int stat, int amount, int limit, out bool overLimit) {
        overLimit = false;
        if (Remaining == 0) return null;
        if ((uint)(GetStatPoints(stat) + amount) > (uint)limit) { overLimit = true; return null; }
        return new AttributePointAddRequest((byte)stat, amount);
    }

    /// <summary>RequestReset @0x140caa3b0 (statpointmanager.cpp:144): nothing with no points used; else mode 3.</summary>
    public IOutPacket RequestReset() => Used == 0 ? null : new AttributePointResetRequest();
}

/// <summary>Outbound AttributePoint (SendOp 0x99) mode 2, CStatPointManager::RequestAdd (statpointmanager.cpp:134-137): u8 2,
/// u8 the stat key, i32 the points.</summary>
public sealed record AttributePointAddRequest(byte Stat, int Amount) : IOutPacket {
    public Outbound Op => Outbound.AttributePoint;
    public void Write(ByteWriter w) { w.WriteByte(2); w.WriteByte(Stat); w.WriteInt(Amount); }
}

/// <summary>Outbound AttributePoint (SendOp 0x99) mode 3, CStatPointManager::RequestReset (statpointmanager.cpp:148-150): u8 3.</summary>
public sealed record AttributePointResetRequest : IOutPacket {
    public Outbound Op => Outbound.AttributePoint;
    public void Write(ByteWriter w) => w.WriteByte(3);
}

/// <summary>
/// UserEnv (0xA8; CSceneStateGame::OnUserEnv, scenestategame.cpp:4414), the modes the character window reads: 0 a title
/// gained (added to the title list DAT_143ab3268), 1 a player's shown title changed (PC::FUN_141661c70, m_n10f4), 2 the
/// title list, 3 the collection grades (DAT_143ab30e0: i32 count, then i32 id, u8 grade each; the trophy dialog redraws
/// after it), 8 the gathering counts (g_143ab3150: i32 count, then i32 id, i32 count each, the mastery recipes' success
/// odds), 9 the mastery grade rewards received (DAT_143ab31a0: i32 count, then i32 id, u8 value each;
/// scenestategame.cpp:4469-4490), 4 the template ids (DAT_143ab3128: i32 count, i32 each), 0xa the fame per alliance
/// (DAT_143ab3220: i32 count, then u16 alliance, i32 fame), 0xb one alliance's fame, 0xc the fame per character (DAT_143ab31e8:
/// i32 count, then i32 fame, i64 character id), 0xd one character's fame. Every mode is read to its end. Mode 5 has no handler in
/// the client (its packet is an i32 count, 0 in the sniffs): it is read as null with the rest skipped. Other modes are read as null.
/// </summary>
public abstract record UserEnvMessage {
    public sealed record TitleAdded(int Id) : UserEnvMessage;
    public sealed record TitleChanged(int ObjectId, int Title) : UserEnvMessage;
    public sealed record Titles(IReadOnlyList<uint> Ids) : UserEnvMessage;
    public sealed record Collection(IReadOnlyDictionary<uint, byte> Grades) : UserEnvMessage;
    public sealed record GatheringCounts(IReadOnlyDictionary<int, int> Counts) : UserEnvMessage {
        /// <summary>GMS2 writes a second table of the same (i32 id, i32 count) pairs after the first (KMS2: the trailing i32 is its count, 0); empty without one.</summary>
        public IReadOnlyDictionary<int, int> Second { get; init; } = new Dictionary<int, int>();
    }
    public sealed record MasteryRewards(IReadOnlyDictionary<uint, byte> Received) : UserEnvMessage;
    public sealed record Templates(IReadOnlyList<int> Ids) : UserEnvMessage;
    /// <summary>Fame by alliance (modes 0xa, 0xb) or by character id (0xc, 0xd); Replace is the 0xa/0xc whole-table forms.</summary>
    public sealed record FameByAlliance(bool Replace, IReadOnlyDictionary<ushort, int> Fame) : UserEnvMessage;
    public sealed record FameByCharacter(bool Replace, IReadOnlyDictionary<long, int> Fame) : UserEnvMessage;

    public static UserEnvMessage Read(ByteReader r) {
        var m = ReadBody(r);
        r.ExpectEnd("UserEnv");
        return m;
    }

    static UserEnvMessage ReadBody(ByteReader r) {
        switch (r.ReadByte()) {
            case 0: return new TitleAdded(r.ReadInt());
            case 1: { int id = r.ReadInt(); return new TitleChanged(id, r.ReadInt()); }
            case 4: {
                uint n = (uint) r.ReadInt();
                var a = new List<int>();
                for (uint i = 0; i < n; i++) a.Add(r.ReadInt());
                return new Templates(a);
            }
            case 5: { int n = r.ReadInt(); if (n != 0) r.ReadBytes(r.Available); return null; }  // STILL-OPEN: the entry layout of a non-empty list (every sniffed packet is count 0)   // no client handler (OnUserEnv has no mode 5)
            case 0xa: {
                uint n = (uint) r.ReadInt();
                var f = new Dictionary<ushort, int>();
                for (uint i = 0; i < n; i++) { ushort a = r.ReadUShort(); f[a] = r.ReadInt(); }
                return new FameByAlliance(true, f);
            }
            case 0xb: { ushort a = r.ReadUShort(); return new FameByAlliance(false, new Dictionary<ushort, int> { [a] = r.ReadInt() }); }
            case 0xc: {
                uint n = (uint) r.ReadInt();
                var f = new Dictionary<long, int>();
                for (uint i = 0; i < n; i++) { int fame = r.ReadInt(); f[r.ReadLong()] = fame; }
                return new FameByCharacter(true, f);
            }
            case 0xd: { int fame = r.ReadInt(); return new FameByCharacter(false, new Dictionary<long, int> { [r.ReadLong()] = fame }); }
            case 2: {
                int n = r.ReadInt();
                var a = new List<uint>();
                for (int i = 0; i < n; i++) a.Add((uint) r.ReadInt());
                return new Titles(a);
            }
            case 3: {
                uint n = (uint) r.ReadInt();
                var g = new Dictionary<uint, byte>();
                for (uint i = 0; i < n; i++) { uint id = (uint) r.ReadInt(); g[id] = r.ReadByte(); }   // DAT_143ab30e0.SetAt
                return new Collection(g);
            }
            case 8: {
                uint n = (uint) r.ReadInt();
                var g = new Dictionary<int, int>();
                for (uint i = 0; i < n; i++) { int id = r.ReadInt(); g[id] = r.ReadInt(); }     // g_143ab3150.SetAt
                // The Maple2 server's writer adds a second count after the pairs (0 on KMS2); the client stops (:4455). The GMS2 recordings
                // carry a second table of the same pairs there (count 9 after 13 pairs, ...).
                var second = new Dictionary<int, int>();
                if (r.Available >= 4) { uint m = (uint) r.ReadInt(); for (uint i = 0; i < m; i++) { int id = r.ReadInt(); second[id] = r.ReadInt(); } }
                return new GatheringCounts(g) { Second = second };
            }
            case 9: {
                uint n = (uint) r.ReadInt();
                var g = new Dictionary<uint, byte>();
                for (uint i = 0; i < n; i++) { uint id = (uint) r.ReadInt(); g[id] = r.ReadByte(); }   // DAT_143ab31a0.SetAt
                return new MasteryRewards(g);
            }
        }
        return null;
    }
}

/// <summary>
/// HomeCommand (0xB7; CSceneStateGame::OnHomeCommand, scenestategame.cpp:7975) mode 1: a player's architect score: i32 object
/// id, i64 the month it counts in (PC m_n1140), i32 the month's count (m_n1148), i32 the score (m_n114c). Modes 0 and 2
/// (an account value) are read as null.
/// </summary>
public sealed record HomeCommend(int ObjectId, long MonthBase, int MonthlyCount, int Point) {
    public static HomeCommend Read(ByteReader r) {
        if (r.ReadByte() != 1) return null;
        int id = r.ReadInt(); long month = r.ReadLong(); int count = r.ReadInt();
        return new HomeCommend(id, month, count, r.ReadInt());
    }
}

/// <summary>
/// The pc record UIPCInfoDialog shows (its m_spInfo), filled by PC::vfunc_125 (pc.cpp:4114) and, for the local player,
/// MyPC::vfunc_125 (m_3c0 = 1, the title list, the wedding state). Fields are named by what the dialog reads.
/// </summary>
public sealed class PcInfo {
    public const int StatCount = 0x23;
    /// <summary>m_3c0: the local player's record (FUN_140dba1c0 / 220 / 240 / 260 all read it).</summary>
    public bool IsLocal;
    public ushort Level;                        // m_18: vfunc_78
    public int JobGroup;                        // m_1c: GetJobGroupId
    public int SubJob;                          // m_20: GetSubJob
    public long AccountId, CharacterId;         // m_28, m_30
    public int Title;                           // m_38: FUN_141661c50 = m_n10f4
    /// <summary>m_aStats0 / 1 / 2: the Stat packet's three values per stat (PCInfo::ReadStats, pcinfo.cpp:66).</summary>
    public readonly long[] Stats0 = new long[StatCount], Stats1 = new long[StatCount], Stats2 = new long[StatCount];
    /// <summary>m_3b0: CEquipment::GetAbilityTotal per special ability of the worn equipment; null while the equipment's
    /// abilities are not read (the rows that need them are left open).</summary>
    public Dictionary<int, int> Special;
    public byte Gender;                         // m_3c4
    public int Trophy;                          // m_3d4: the proxy's GetTrophyTotal (proxygameobject.cpp:347)
    public int HomeCommendPoint;                // m_3d8: FUN_141662730 = m_n114c
    public int HomeCommendMonthly;              // m_3dc: GetMonthlyCount
    public string Picture = "";                 // m_3e0: FUN_1416612e0 (the profile image key)
    public string Name = "";                    // m_3e8: vfunc_77
    public string HomeAddress = "";             // m_3f0: the proxy's FUN_1416c2600 (MyPC::GetHomeAddress for the local player)
    public string GuildName = "";               // m_3f8
    public string GuildGrade = "";              // m_400: CGuildManager::GetGradeName of the member's grade
    public string Motto = "";                   // m_408: GetName (the proxy's motto)
    public readonly List<uint> Titles = new();  // m_aIds (MyPC: DAT_143ab3268)
    public int Wedding;                         // the wedding state vfunc_106 reads (MyPC fills it from WeddingManager)
    public bool CodiActive;                     // m_508: the title layer's IsActive
    public long CodiStart, CodiEnd;             // m_510 / m_520: the title layer's period (CEquipmentSlotLayerInfo m_start / m_end)
    /// <summary>m_mapEquip2's count: the shown items of equip layer 2 (PC::vfunc_126(spInfo, 2), pc.cpp:4180).</summary>
    public int CodiItems;
    public int PrestigeLevel;                   // m_530: vfunc_141
    /// <summary>The worn items the record's equip maps (m_mapEquip0 / 1 / 2, PC::vfunc_126) are built from: the own
    /// FieldAddUser's equips (CItem variant unset: their layer comes from the item data, CItem::FUN_14052a170) and its
    /// layer-2 outfits (SetVariant(2)); null while not known.</summary>
    public IReadOnlyList<EquipItem> Equips, Outfit2;
    /// <summary>The gem items (m_aItems: PCGemEquip::GetItems, pc.cpp:4160) from the own FieldAddUser's gem block; null while
    /// not known.</summary>
    public IReadOnlyList<EquipItem> Gems;

    /// <summary>CEquipmentSlotLayerInfo::GetPeriodState (equipment.cpp:432) of the title layer (kind 2): 0 without feature
    /// 0x8e (FUN_1411df7d0), 4 never started, 2 no end, 3 ended (FUN_1411df700), else 1.</summary>
    public int GetCodiPeriodState(bool feature8e, long serverTime) {
        if (!feature8e) return 0;
        if (CodiStart == 0) return 4;
        if (CodiEnd == 0) return 2;
        if (CodiEnd > 0 && serverTime >= CodiEnd) return 3;
        return 1;
    }
}

/// <summary>
/// The local player's state the character window's record comes from, as the packets leave it: the own FieldAddUser (its
/// user block, title and architect score), the Stat packet (all three values per stat), the own AddPlayer proxy's trophy
/// counts, UserEnv's title list and HomeCommand.
/// </summary>
public sealed class MyCharacter {
    public int ObjectId = -1;
    public UserBlock User;
    public int Title;
    /// <summary>The name tag symbol (PC::FUN_141661d40 = m_n10f8, read after the title in the own FieldAddUser).</summary>
    public ushort Insignia;
    /// <summary>Whether the symbol is shown (PC m_n10fc, FUN_141661e20; the own FieldAddUser's flag after the insignia).</summary>
    public bool InsigniaShown;
    /// <summary>The guild name over the own head (PC m_strNameDC8, GetGuildName): the own FieldAddUser's (vfunc_129,
    /// scenestategame.cpp:11148), then the guild packets' (<see cref="ApplyHeadGuildName"/>).</summary>
    public string GuildName = "";
    /// <summary>The own FieldAddUser's PvP point and mode (CPvPInfo) and team byte (m_unk_af8), the name tag's team symbols' inputs.</summary>
    public int PvpPoint; public byte PvpMode, Team;
    public long HomeCommendMonth; public int HomeCommendMonthly, HomeCommendPoint;
    public readonly long[] Stats0 = new long[PcInfo.StatCount], Stats1 = new long[PcInfo.StatCount], Stats2 = new long[PcInfo.StatCount];
    /// <summary>The proxies' trophy triples by proxy id (ProxyGameObj AddPlayer, scenestategame.cpp:11358; removed by mode 4),
    /// with their character ids: the record reads the own one (FindProxyObject; none = 0).</summary>
    readonly Dictionary<int, (long CharacterId, int Combat, int Adventure, int Lifestyle)> proxies = new();
    public readonly List<uint> Titles = new();
    /// <summary>The own FieldAddUser's title layer period (SetLayerPeriod, scenestategame.cpp:11248) and its layer-2
    /// outfits (SetVariant(2), :11250-11264).</summary>
    public long CodiStart, CodiEnd; public int CodiItems;
    /// <summary>The own FieldAddUser's premium club time and wedding state (PC m_n1190 / m_n1220,
    /// scenestategame.cpp:11287-11291), the VIP and wedding inputs of PC::CanUseItem (pc.cpp:4683, ported in Ms2.Ui's
    /// PcItemUse).</summary>
    public long PremiumTime; public short WeddingState;
    /// <summary>The own FieldAddUser's equips, layer-2 outfits and gems (PCGemEquip::ReadFromStream, gemslot_2.cpp:92).</summary>
    public IReadOnlyList<EquipItem> Equips, Outfit2, Gems;
    /// <summary>The summoned pet from FieldAddUser (PC::ReadPetItem, scenestategame.cpp:11286; STILL-OPEN: post-enter
    /// pet summon/unsummon packets).</summary>
    public EquipItem Pet;

    /// <summary>PC::GetEquipByUid (OnItemUpdate): the worn equip or outfit of the uid; null when none.</summary>
    public EquipItem EquipByUid(long uid) => (Equips ?? Array.Empty<EquipItem>()).Concat(Outfit2 ?? Array.Empty<EquipItem>()).FirstOrDefault(e => e.Uid == uid);
    public readonly StatPointBook StatPoints = new();
    public event Action Changed;
    /// <summary>CSceneStateGame::OnStat (scenestategame.cpp:9446-9491): the own Stat changed a stat other than health, spirit and
    /// stamina (bOther), so UIStatusEventDispatcher::FUN_141b0e050 calls each listener's vfunc_oa0_2.</summary>
    public event Action OtherStatsChanged;
    /// <summary>A put-on / put-off changed the equip lists (before <see cref="Changed"/>): the item event dispatcher's
    /// put-on / put-off (Unk_SSGItemHub vfunc_20 / vfunc_21, scenestategame.cpp:3117 / 3162).</summary>
    public event Action EquipChanged;
    /// <summary>Every own put-on or put-off with an item, after the lists changed or not: MyPC::vfunc_127 / vfunc_128
    /// (mypc.cpp:4882-4905) re-sample DAT_143aea120 (GetAbilityRate(0x23)) on each call, whether or not the item has a model
    /// preset and was worn or taken off.</summary>
    public event Action PutOnOrOff;
    /// <summary>The item event hub's vfunc_20 (scenestategame.cpp:3117, :3123) after the own PC wore an item (PC::vfunc_127): the item
    /// event listeners' vfunc_21 (the beauty shop, uibeautyshopdialog.cpp:896) take the item. The client raises it on the same
    /// calls as <see cref="PutOnOrOff"/>, whether or not the item has a model preset; the beauty shop sub scene's slot filter
    /// (:3114, slots 1, 2 and 3 only) belongs to the host, which knows whether that shop is open.</summary>
    public event Action<EquipItem> OwnItemPutOn;
    /// <summary>MyPC::vfunc_128 (mypc.cpp:4895-4905) after the own PC took off an item with a model preset (PC::vfunc_128):
    /// the item's uid, for the inventory's ExcludeFromAutoEquip (the auto-equip does not put it on again).</summary>
    public event Action<long> OwnItemTakenOff;

    /// <summary>The own FieldAddUser: object id, user block, the title (PC::FUN_141661c00 = m_n10f4) and the architect score
    /// values after the ride block (scenestategame.cpp:11220-11224).</summary>
    public void Apply(FieldAddUser u) {
        ObjectId = u.ObjectId; User = u.User; Title = u.Title; Insignia = (ushort)u.Insignia; InsigniaShown = u.InsigniaShown;
        GuildName = u.User?.GuildName ?? "";
        PvpPoint = u.PvpPoint; PvpMode = u.PvpMode; Team = u.Team;
        HomeCommendMonth = u.HomeCommendMonth; HomeCommendMonthly = u.HomeCommendMonthly; HomeCommendPoint = u.HomeCommendPoint;
        CodiStart = u.SkinTabStart; CodiEnd = u.SkinTabEnd; CodiItems = u.Outfit2.Count;
        PremiumTime = u.PremiumTime; WeddingState = u.WeddingState;
        Equips = u.Equips.ToList(); Outfit2 = u.Outfit2.ToList(); Gems = u.Badges.ToList();
        Pet = u.HasPet ? u.Pet : null;
        for (int i = 0; i < 3; i++) StatsOf(i)[PlayerStat.Health] = u.Stats[i].Health;   // MyPC::vfunc_91 = PC::vfunc_90 (pc.cpp:1644): SetStat(kind, 4, health)
        State = 0;
        SetRevivalEndTick(u.RevivalEndTick); RevivalConfirmCount = u.RevivalCount; RevivalFlag = u.RevivalFlag;   // scenestategame.cpp:11280-11281
        MesoRevivalsUsed = 0;   // the own FieldAddUser makes a new MyPC (scenestategame.cpp:11117-11120), whose ctor zeroes m_n1644 (mypc.cpp:880)
        Dead = u.Stats[2].Health == 0;                                  // scenestategame.cpp:11185: FUN_141670a00(4) == 0 -> FUN_14166ce50(1)
        Changed?.Invoke();
    }

    /// <summary>
    /// A guild packet's name over a player's head (PC::vfunc_129, pc.cpp:1511): CGuildManager's FUN_1413c7700 / FUN_1413c7810
    /// (guildmanager.cpp:3108 / :3121) find the PC by name among all the field's PCs (WorldService::FUN_141bd1420, the own one
    /// included) and OnPacketGuildRenamed (:2211) takes the local player. True when the name is the own character's; the
    /// MyInfo dialog's PC info reads it (PC::vfunc_125's m_3f8), so <see cref="Changed"/> follows a change.
    /// </summary>
    public bool ApplyHeadGuildName(string name, string guildName) {
        if (User == null || name == null || name != User.Name) return false;
        guildName ??= "";
        if (guildName == GuildName) return true;
        GuildName = guildName;
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// The Insignia packet on the own player (CSceneStateGame::OnNametag, scenestategame.cpp:7314-7331): PC::FUN_141661dc0
    /// sets the symbol and the shown flag; then, the actor being the local MyPC, the MyInfo dialog (0x2711) is marked changed
    /// (UIMyInfoDialog::FUN_141a5a560), which <see cref="Changed"/> does here. False when it is another player's.
    /// </summary>
    public bool Apply(Nametag n) {
        if (n.ObjectId != ObjectId || ObjectId < 0) return false;
        Insignia = n.Symbol; InsigniaShown = n.Shown;
        Changed?.Invoke();
        return true;
    }

    long[] StatsOf(int kind) => kind == 0 ? Stats0 : kind == 1 ? Stats1 : Stats2;

    /// <summary>PCBase::IsDead (pcbase.cpp:1256, m_unk_15c) of the local player: set by the own FieldAddUser with no current
    /// health and by <see cref="Die"/>, cleared by <see cref="Revive"/>.</summary>
    public bool Dead;
    /// <summary>
    /// CStateManager m_nCur (GetCurStateId) of the local player as the inbound packets set it: 0 after FieldAddUser (a new
    /// MyPC, scenestategame.cpp:11119), then <see cref="EnterState"/>; left for the body's own state by
    /// <see cref="LeaveFallOnGround"/> and <see cref="LeaveForBodyState"/>. STILL-OPEN: a server state the body has no move
    /// state for (emotion, react, talk ...) is left only when the body itself changes state, not at the end of its sequence.
    /// </summary>
    public int State { get; private set; }
    /// <summary>The server put the player into <see cref="State"/> (the client's state manager left the one it was in).</summary>
    public event Action<int> StateEntered;

    /// <summary>The ids PC registers in its state manager (pc.cpp:668-721); ChangeState ignores any other.</summary>
    static readonly HashSet<int> RegisteredStates = new() {
        1, 2, 3, 4, 5, 6, 7, 8, 9, 0xb, 0xc, 0xd, 0xe, 0xf, 0x10, 0x11, 0x12, 0x14, 0x15, 0x16, 0x18, 0x19, 0x1a, 0x1b, 0x1c,
        0x1d, 0x1e, 0x1f, 0x20, 0x21, 0x22, 0x25, 0x26, 0x28, 0x29, 0x2a, 0x2b, 0x2c, 0x2d, 0x2f, 0x35, 0x36, 0x37, 0x39, 0x3a,
        0x3b, 0x3c, 0x3d, 0x3e, 0x3f, 0x40, 0x45, 0x46, 0x47,
    };

    /// <summary>PC::vfunc_124 (pc.cpp:1199) refuses every state but death 0xc, revival 0x18 and observer 0x3f: the dead flag
    /// is set, or the health is 0 with a max health above 0. A player the own FieldAddUser added dead stays in state 0 with the
    /// flag (scenestategame.cpp:11185), no state of its own, until the server's Revival clears it.</summary>
    public bool DeathOnlyStates {
        get { long hp = Stats2[PlayerStat.Health], maxHp = Stats0[PlayerStat.Health]; return Dead || (maxHp > 0 && hp == 0); }
    }

    /// <summary>
    /// MyPC::vfunc_59 (mypc.cpp:1239) -> PC::vfunc_59 (pc.cpp:1187): PC::vfunc_124 (pc.cpp:1199) lets the state through when
    /// the player is not dead and has health (or no max health, FUN_141670a30(4) = m_unk_1c0[0][4]), or for the death 0xc,
    /// revival 0x18 and observer 0x3f states; then PCBase::vfunc_59 (pcbase.cpp:1098) -> CStateManager::ChangeState
    /// (statemanager.cpp:291), which takes only a registered state. Returns whether the state was entered.
    /// STILL-OPEN: an object that is not loaded yet (GameObject m_unk_b4 0: the state is queued in m_unk_c48) and the ride
    /// state 0x21 with m_spB10 set (the change goes to m_spB10) are not modelled; both are taken as a loaded player on foot.
    /// </summary>
    bool EnterState(int state) {
        if (DeathOnlyStates && !(state == 0xc || state == 0x18 || state == 0x3f)) return false;
        if (!RegisteredStates.Contains(state)) return false;
        State = state;
        StateEntered?.Invoke(state);
        return true;
    }

    /// <summary>
    /// OnUserState (scenestategame.cpp:5758) for the local player: vfunc_59(state) first; then state 12 runs vfunc_137
    /// (<see cref="Die"/>) and any other state runs vfunc_59(state) again. A loaded player that takes state 12 is already in
    /// the death state when vfunc_137 runs, so the dead flag is not set by it. Other objects: nothing here. Returns whether
    /// the dead flag changed.
    /// </summary>
    public bool ApplyUserState(int objectId, int state) {
        if (objectId != ObjectId || User == null) return false;
        EnterState(state);
        if (state == 12) return Die(objectId);
        EnterState(state);
        return false;
    }

    /// <summary>
    /// The fall (5) the server put the player into is left by the client on its own once the body stands: StateFall::vfunc_9
    /// (statefall.cpp:192-221) enters StateLand (4) when the owner is on the ground (FUN_141671a30), and StateLand goes on to
    /// StateIdle; from then the body's own move state is the state (0 here, as after FieldAddUser). The server sends this fall
    /// at every field entry (OnUserState), so without it the put-down and the other polls that want the standing state (1)
    /// never see it. Returns whether the fall was left.
    /// STILL-OPEN: the chair, bed and ride-off take at the touchdown (statefall.cpp:200-216) is not modelled here.
    /// </summary>
    public bool LeaveFallOnGround(bool grounded) {
        if (State != 5 || !grounded) return false;
        State = 0;
        return true;
    }

    /// <summary>
    /// CStateManager::ChangeState (statemanager.cpp:291) replaces the current state whoever asks, so a state the server put
    /// the player into (other than the spawn, idle and walk ids 0, 1, 2) is left once the body enters a move state of another
    /// id (<paramref name="bodyStateId"/>): the client's own exits to StateIdle (StateFallDamage::EndOfSequence
    /// statefalldamage.cpp:108, StateRevival::EndOfSequence statedead.cpp:337, StateHold's let go), StateFall's landing
    /// (statefall.cpp:220) and a move the input starts. From then the body's move state is the state (0 here). Returns whether
    /// the state was left.
    /// </summary>
    public bool LeaveForBodyState(int bodyStateId) {
        if (State is 0 or 1 or 2 || State == bodyStateId) return false;
        State = 0;
        return true;
    }

    /// <summary>
    /// The own client's vfunc_59(state) (the stun buff's StateStun, AdditionalCliList::FUN_1409713b0,
    /// additionalcli_2.cpp:831), with PC::vfunc_124's and ChangeState's checks as <see cref="EnterState"/>. Returns whether
    /// the state was entered.
    /// </summary>
    public bool EnterOwnState(int state) => EnterState(state);

    /// <summary>
    /// The state's own exit to StateIdle (vfunc_59(1)) for a state the server put the player into that the body has no move
    /// state for (Ms2.Ui.OwnStateExit: at once, the input step, a timer or the played sequence's end). From then the body's
    /// move state is the state (0 here). Returns whether <paramref name="state"/> was the state and was left.
    /// </summary>
    public bool LeaveServerState(int state) {
        if (State != state || state is 0 or 1 or 2) return false;
        State = 0;
        return true;
    }

    /// <summary>
    /// vfunc_137 for the local player (OnUserState state 12; OnDeadUser, scenestategame.cpp:6532): PC::vfunc_137 (pc.cpp:4593)
    /// not in the death state and with no current health (FUN_141670a00(4) = m_unk_1c0[2][4]): FUN_14166ce50(1); then
    /// MyPC::vfunc_137 (mypc.cpp:1343) with the dead flag set enters the death state (vfunc_59(0xc)). Other objects: nothing.
    /// MyPC::vfunc_137's dead branch (mypc.cpp:1346-1355) runs whenever the player is dead at the end, not only when the flag
    /// flips, so <see cref="Died"/> is raised each time (a player who entered dead gets the dialog from a later DeadUser).
    /// Returns whether the dead flag changed. STILL-OPEN: the PvP time scale.
    /// </summary>
    public bool Die(int objectId, bool flag = false) {
        if (objectId != ObjectId || User == null) return false;
        if (flag) RevivalFlag = true;      // OnDeadUser's flag: FUN_14028d780 -> PC::FUN_141661ba0(1)
        bool changed = false;
        if (State != 0xc && Stats2[PlayerStat.Health] == 0 && !Dead)
        {
            Dead = true; changed = true;
            if (RevivalConfirmCount > 0) RevivalFlag = true;      // PC::vfunc_137 (pc.cpp:4598-4599): m_n10f0 = 1 when m_n10ec > 0
        }
        if (Dead) { EnterState(0xc); Died?.Invoke(); }
        if (changed) Changed?.Invoke();
        return changed;
    }

    /// <summary>Raised when MyPC::vfunc_137's dead branch runs (mypc.cpp:1346): each DeadUser or user state 12 that leaves the own
    /// player dead, also when already dead; not from a FieldAddUser of a dead player, which does not run it.</summary>
    public Action Died;

    /// <summary>The revival end tick (PC m_n10e8, FUN_1416619d0) and the revival count (m_n10ec, FUN_141661a60) of RevivalConfirm,
    /// and the revival flag (m_n10f0, FUN_141661ba0) of Revival and DeadUser.</summary>
    public uint RevivalEndTick; public int RevivalConfirmCount; public bool RevivalFlag;
    /// <summary>The meso revivals used today (MyPC m_n1644, OnRevivalCount) and the revivals used in the dungeon room (DAT_143ab2950).</summary>
    public int MesoRevivalsUsed, DungeonRevivalsUsed;

    /// <summary>PC::IsRevivalBlocked (pc.cpp:2009) without the buff test: a revival count above 0 or the revival flag. The buffs'
    /// IsDisableRevivalHere is ORed in by the revival dialog's wiring (RevivalWiring), which holds the buff list.</summary>
    public bool RevivalBlocked => RevivalConfirmCount > 0 || RevivalFlag;

    /// <summary>OnRevivalConfirm (scenestategame.cpp:7356) for the local player: the end tick, then the count. Returns the client's
    /// follow-up: <c>Effect</c> when the count was set and falls to 0 (the revival effect of the common resource), <c>Guide</c> when
    /// the new count is not 0 (guide trigger RevivalPanelty). Other objects: nothing.
    /// The end tick goes through <see cref="SetRevivalEndTick"/>, which raises <see cref="RevivalEndTickSet"/> for the penalty icon.</summary>
    public (bool Effect, bool Guide) ApplyRevivalConfirm(RevivalConfirm c) {
        if (c.ObjectId != ObjectId || User == null) return (false, false);
        bool effect = RevivalConfirmCount != 0 && c.Count == 0;
        RevivalConfirmCount = c.Count;
        SetRevivalEndTick(c.EndTick);
        Changed?.Invoke();
        return (effect, !effect && c.Count != 0);
    }

    /// <summary>PC::FUN_1416619d0 (pc.cpp:2018) for the local player: m_n10e8 set, then <see cref="RevivalEndTickSet"/> with it, for
    /// the MyPC part (pc.cpp:2021-2030: the player-info UI 0x4e5f adds the custom buff 0x5f5e103 ending at it when above 0, else
    /// removes it). Run by FieldAddUser (FUN_141661b20), OnRevivalConfirm and the expiry (FUN_141661ac0).</summary>
    void SetRevivalEndTick(uint endTick) {
        RevivalEndTick = endTick;
        RevivalEndTickSet?.Invoke(endTick);
    }

    /// <summary>Raised with each revival end tick set (<see cref="SetRevivalEndTick"/>), also an unchanged one or 0.</summary>
    public Action<uint> RevivalEndTickSet;

    /// <summary>
    /// OnStateFallDamage (scenestategame.cpp:5815 @0x141723050) for the local player: vfunc_59(0x1f) when not dead, then the guide
    /// trigger FallDamage (0xf) with value 1 when dead, else -1. Returns that value; other objects: 0 (nothing). The damage
    /// effect dialog's call (0x4e3c, UIDamageEffect::FUN_141910b10) is an empty hook; the rider's voice is
    /// <see cref="FallDamageVoice"/>.
    /// </summary>
    public int ApplyFallDamage(StateFallDamage f) {
        if (f.ObjectId != ObjectId || User == null) return 0;
        if (!Dead) EnterState(0x1f);
        return Dead ? 1 : -1;
    }

    /// <summary>CommonResource female_DropDamg_Voice (+0x2d0) and male_DropDamg_Voice (+0x2d8).</summary>
    public const string FemaleDropDamgVoiceKey = "female_DropDamg_Voice", MaleDropDamgVoiceKey = "male_DropDamg_Voice";

    /// <summary>
    /// OnStateFallDamage's last step (scenestategame.cpp:5830-5840) for the local player: while it rides (PCBase::GetRidee,
    /// m_spB10, not null) the CommonResource key of its gender's voice effect (PC::GetGender 1 female_DropDamg_Voice, 0
    /// male_DropDamg_Voice), inserted on the own PC; null for another object, without a mount, or for another gender (the
    /// query's empty name).
    /// </summary>
    public string FallDamageVoice(StateFallDamage f, bool riding) {
        if (f.ObjectId != ObjectId || User == null || !riding) return null;
        return User.Gender switch { 1 => FemaleDropDamgVoiceKey, 0 => MaleDropDamgVoiceKey, _ => null };
    }

    /// <summary>The last Tombstone update for the own player (OnTombstone): kind, value and hitter; <c>TombstoneHidden</c> after kind 0.
    /// The game's tombstone object (PC m_spFC8) and bar are built and hit outside this model.</summary>
    public byte TombstoneKind, TombstoneValue; public int TombstoneHitter; public bool TombstoneHidden;

    public bool Apply(TombstoneUpdate t) {
        if (t.ObjectId != ObjectId || User == null) return false;
        TombstoneKind = t.Kind; TombstoneValue = t.Value; TombstoneHitter = t.Hitter; TombstoneHidden = t.Kind == 0;
        return true;
    }

    /// <summary>PC::FUN_141661ac0 (pc.cpp:2043): once the server tick passes the end tick, both are cleared. Returns whether they were.</summary>
    public bool ExpireRevival(uint serverTick) {
        if (RevivalEndTick == 0 || serverTick < RevivalEndTick) return false;
        RevivalConfirmCount = 0; SetRevivalEndTick(0);
        Changed?.Invoke();
        return true;
    }

    /// <summary>OnRevivalCount (scenestategame.cpp:4092): MyPC::FUN_14152a2f0 (mypc.cpp:3187) sets m_n1644; without a local
    /// player (before the first own FieldAddUser) the packet does nothing.</summary>
    public void Apply(RevivalCount c) { if (User == null) return; MesoRevivalsUsed = c.Used; Changed?.Invoke(); }

    /// <summary>OnDungeonReviveCount (scenestategame.cpp:5074).</summary>
    public void Apply(DungeonReviveCount c) { DungeonRevivalsUsed = c.Used; Changed?.Invoke(); }

    /// <summary>OnRevival (scenestategame.cpp:7387) for the local player: FUN_14166ce50(0) and the revival state
    /// (vfunc_59(0x18)). Returns whether the dead flag changed.</summary>
    public bool Revive(int objectId, byte flag = 0) {
        if (objectId != ObjectId || User == null) return false;
        RevivalFlag = flag != 0;           // PC::FUN_141661ba0(flag), m_n10f0 (scenestategame.cpp:7399)
        bool changed = Dead;
        Dead = false;
        EnterState(0x18);
        if (changed) Changed?.Invoke();
        return changed;
    }

    /// <summary>
    /// CSceneStateGame::OnItemPutOn (scenestategame.cpp:3080) for the own object: put-on kind 0 / 1 / 2 (FUN_140222f60;
    /// others are dropped); kind 2 sets the item's variant 2, the second outfit layer. PC::vfunc_127 (pc.cpp:1400) wears
    /// only an item with a model preset: <paramref name="wornLayer"/> gives the item's layer (CItem::FUN_14052a170 without
    /// a variant) or null when the item is unknown or has no preset. CEquipment::FUN_1411ec190 puts the item into the
    /// slots of its model groups at its layer; here the item replaces the one in the same slot and layer (the multi-slot
    /// groups of a model are not modelled). The item goes after the last one of its layer or a lower one, so the list keeps
    /// FieldAddUser's order (gear, then outfits) and a look built from it in order shows each slot's top layer
    /// (CEquipment::GetTopLayerItem @0x1411e1a50, equipment.cpp:835: layer 2, then 1, then 0). Returns whether the lists
    /// changed. <paramref name="itemKnown"/> is CItem::FUN_140523c40 (:3096): an item id without data makes the client
    /// return before vfunc_127; null takes every id as known. Each call that reaches vfunc_127 raises
    /// <see cref="PutOnOrOff"/> after the lists.
    /// </summary>
    public bool Apply(ItemPutOn p, Func<int, int?> wornLayer, Func<int, bool> itemKnown = null) {
        if (User == null || p.ObjectId != ObjectId || p.Kind > 2) return false;
        if (itemKnown != null && !itemKnown(p.Item.Id)) return false;
        var equips = Equips; var outfit = Outfit2;
        bool changed = WornEquips.PutOn(ref equips, ref outfit, p, wornLayer);
        if (changed) {
            Equips = equips; Outfit2 = outfit;
            EquipChanged?.Invoke();
            Changed?.Invoke();
        }
        PutOnOrOff?.Invoke();
        OwnItemPutOn?.Invoke(p.Item);
        return changed;
    }

    /// <summary>
    /// Replaces the own worn lists with another gear / outfit set and raises <see cref="EquipChanged"/> and <see cref="Changed"/>,
    /// so a view built from them redraws (the beauty shop's preview puts items on the own PC with PC::vfunc_127 / vfunc_128 and
    /// takes them off again; the packets' <see cref="PutOnOrOff"/> is not raised, no put-on / put-off packet came).
    /// </summary>
    public void ReplaceWorn(IReadOnlyList<EquipItem> equips, IReadOnlyList<EquipItem> outfit2)
    {
        Equips = equips ?? Array.Empty<EquipItem>(); Outfit2 = outfit2 ?? Array.Empty<EquipItem>();
        EquipChanged?.Invoke();
        Changed?.Invoke();
    }

    /// <summary>
    /// CSceneStateGame::OnItemPutOff (scenestategame.cpp:3144) for the own object: the worn item of that uid
    /// (PC::FUN_141655b80) is taken off when it has a model preset (PC::vfunc_128, pc.cpp:1415; CEquipment::FUN_1411e3960
    /// finds it by uid). Returns whether the lists changed. A worn uid raises <see cref="PutOnOrOff"/> after the lists, with
    /// or without a preset (vfunc_128 runs for any item FUN_141655b80 finds).
    /// </summary>
    public bool Apply(ItemPutOff p, Func<int, int?> wornLayer) {
        if (User == null || p.ObjectId != ObjectId) return false;
        if (!WornEquips.IsWorn(Equips, Outfit2, p.ItemUid)) return false;
        var equips = Equips; var outfit = Outfit2;
        bool changed = WornEquips.PutOff(ref equips, ref outfit, p.ItemUid, wornLayer) != null;
        if (changed) {
            Equips = equips; Outfit2 = outfit;
            EquipChanged?.Invoke();
            Changed?.Invoke();
            OwnItemTakenOff?.Invoke(p.ItemUid);
        }
        PutOnOrOff?.Invoke();
        return changed;
    }

    /// <summary>
    /// CSceneStateGame::OnBadgeEquip (scenestategame.cpp:11006) for the own object's PCGemEquip: mode 0 EquipItem
    /// (gemslot_2.cpp:270): a gem item (GetGem() != 0) goes in by its gem type (EquipByGemType), replacing the one of that
    /// type; mode 1 RemoveByGemType (:257) of the packet's u8. Returns whether the gems changed.
    /// The damage-font gem load (scenestategame.cpp:11031-11039) is the field PCs' (NpcEmoticons.OnBadgeEquip).
    /// STILL-OPEN: mode 0's FUN_140523c40 (the item made from its id and rarity; an unknown id stops it) is not checked;
    /// the actor hooks (pPC->FUN_14166ccb0()->vfunc_29 / vfunc_30) and the own ItemEventDispatcher's vfunc_29 / vfunc_30;
    /// other PCs' PCGemEquip (their packets are dropped here).
    /// </summary>
    public bool Apply(BadgeEquipMessage b) {
        if (User == null || b.ObjectId != ObjectId) return false;
        var gems = Gems ?? Array.Empty<EquipItem>();
        if (b.Mode == 0 && b.Item != null && b.Item.GemType != 0)
            Gems = gems.Where(g => g.GemType != b.Item.GemType).Append(b.Item).ToList();
        else if (b.Mode == 1 && gems.Any(g => g.GemType == b.GemType))
            Gems = gems.Where(g => g.GemType != b.GemType).ToList();
        else return false;
        Changed?.Invoke();
        return true;
    }

    /// <summary>The own Stat (0x2E): the values of the stats it carries (PC's m_unk_1c0[0..2]).</summary>
    /// <summary>The exp counter in this level (DAT_143ab28a0) and the rest exp pool (DAT_143ab28a8): ServerEnter
    /// (scenestategame.cpp:2085-2086), then ExpUp (scenestategame.cpp:4140-4141, mode 1 :4184).</summary>
    public long Exp, RestExp;

    /// <summary>The meso balance (DAT_143ab2898): ServerEnter (scenestategame.cpp:2087), then Meso (OnMeso, :7006-7009).</summary>
    public long Meso;

    public void Apply(ServerEnter e) {
        if (e == null) return;
        Exp = e.Exp; RestExp = e.RestExp; Meso = e.Meso;
        Changed?.Invoke();
    }

    /// <summary>OnMeso (scenestategame.cpp:7006): the new balance; returns the change (new - old, :7008).</summary>
    public long Apply(MesoUpdate m) {
        long delta = m.Balance - Meso;
        Meso = m.Balance;
        Changed?.Invoke();
        return delta;
    }

    public void Apply(ExpUp e) {
        if (e.Mode == 0) { Exp = e.Total; RestExp = e.Rest; }
        else if (e.Mode == 1) RestExp = e.Rest;
        else return;
        Changed?.Invoke();
    }

    /// <summary>OnLevelUp (scenestategame.cpp:9511) for the own object: PC::vfunc_131(level). Returns whether it is the
    /// own object.</summary>
    public bool Apply(LevelUp l) {
        if (User == null || l.ObjectId != ObjectId) return false;
        User.Level = (short) l.Level;
        Changed?.Invoke();
        return true;
    }

    public void Apply(PlayerStat s) {
        bool other = false;
        foreach (var (stat, v) in s.Values) {
            if (stat >= PcInfo.StatCount) continue;
            if (stat is not (PlayerStat.Health or PlayerStat.Spirit or PlayerStat.Stamina)
                && (Stats0[stat] != v[0] || Stats1[stat] != v[1] || Stats2[stat] != v[2])) other = true;
            Stats0[stat] = v[0]; Stats1[stat] = v[1]; Stats2[stat] = v[2];
        }
        Changed?.Invoke();
        if (other) OtherStatsChanged?.Invoke();
    }

    /// <summary>OnStateConsumeEp (scenestategame.cpp:5799-5810): SetCurrentStat(10, ep), the value taken as absolute and clamped to
    /// [0, total] (pcbase.cpp:2375-2386); raises Changed like <see cref="Apply(PlayerStat)"/>. Returns the stored EP.</summary>
    public long Apply(StateConsumeEp c) {
        long ep = Math.Clamp((long) c.Ep, 0L, Stats0[PlayerStat.Stamina]);
        Stats2[PlayerStat.Stamina] = ep;
        Changed?.Invoke();
        return ep;
    }

    public void Apply(UserEnvMessage m) {
        switch (m) {
            case UserEnvMessage.TitleAdded a: Titles.Add((uint) a.Id); break;
            case UserEnvMessage.TitleChanged c when c.ObjectId == ObjectId: Title = c.Title; break;
            case UserEnvMessage.Titles t: Titles.Clear(); Titles.AddRange(t.Ids); break;
            default: return;
        }
        Changed?.Invoke();
    }

    public void Apply(HomeCommend h) {
        if (h.ObjectId != ObjectId) return;
        HomeCommendMonth = h.MonthBase; HomeCommendMonthly = h.MonthlyCount; HomeCommendPoint = h.Point;
        Changed?.Invoke();
    }

    /// <summary>ProxyGameObj: AddPlayer keeps the proxy's trophy counts (its m_d0), Remove drops it.</summary>
    public void Apply(ProxyGameObj p) {
        if (p.Command == 3) proxies[p.ObjectId] = (p.CharacterId, p.AchievementCombat, p.AchievementAdventure, p.AchievementLifestyle);
        else if (p.Command == 4) proxies.Remove(p.ObjectId);
        else return;
        Changed?.Invoke();
    }

    /// <summary>PC::GetMonthlyCount (pc.cpp:2120): a month base from another month than the server's (IsMonthChanged,
    /// timeutil.cpp:361, CMsTime calendar months) resets it to the server time and the count to 0.</summary>
    public int GetMonthlyCount(long serverTime) {
        var a = MsTime.LocalTm(HomeCommendMonth); var b = MsTime.LocalTm(serverTime);
        if (a.Year * 100 + a.Month != b.Year * 100 + b.Month) { HomeCommendMonth = serverTime; HomeCommendMonthly = 0; }
        return HomeCommendMonthly;
    }

    /// <summary>MyPC::vfunc_125 / PC::vfunc_125 (pc.cpp:4114) for the local player: null before the own FieldAddUser.
    /// Not filled yet: the equipment abilities (m_3b0), the guild grade name, the home address, the wedding state.</summary>
    public PcInfo ToPcInfo(long serverTime) {
        if (User == null) return null;
        var i = new PcInfo {
            IsLocal = true, Level = (ushort) User.Level, JobGroup = User.JobCode, SubJob = User.Job,
            AccountId = User.AccountId, CharacterId = User.CharacterId, Title = Title, Gender = User.Gender,
            HomeCommendPoint = HomeCommendPoint, HomeCommendMonthly = GetMonthlyCount(serverTime),
            Picture = User.Picture ?? "", Name = User.Name ?? "", GuildName = this.GuildName, Motto = User.Motto ?? "",
            PrestigeLevel = User.PrestigeLevel, CodiStart = CodiStart, CodiEnd = CodiEnd, CodiItems = CodiItems,
            Equips = Equips, Outfit2 = Outfit2, Gems = Gems,
        };
        foreach (var t in proxies.Values)
            if (t.CharacterId == User.CharacterId) { i.Trophy = t.Combat + t.Adventure + t.Lifestyle; break; }
        Array.Copy(Stats0, i.Stats0, PcInfo.StatCount); Array.Copy(Stats1, i.Stats1, PcInfo.StatCount); Array.Copy(Stats2, i.Stats2, PcInfo.StatCount);
        i.Titles.AddRange(Titles);
        return i;
    }
}
