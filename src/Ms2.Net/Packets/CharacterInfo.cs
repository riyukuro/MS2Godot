using Maple2.PacketLib.Tools;

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
/// Its requests (RequestAdd / RequestReset, SendOp::AttributePoint modes 2 and 3) are not here: nothing is sent.
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
}

/// <summary>
/// UserEnv (0xA8; CSceneStateGame::OnUserEnv, scenestategame.cpp:4414), the modes the character window reads: 0 a title
/// gained (added to the title list DAT_143ab3268), 1 a player's shown title changed (PC::FUN_141661c70, m_n10f4), 2 the
/// title list. The other modes (achievement grades, templates, masteries, fame) are read as null.
/// </summary>
public abstract record UserEnvMessage {
    public sealed record TitleAdded(int Id) : UserEnvMessage;
    public sealed record TitleChanged(int ObjectId, int Title) : UserEnvMessage;
    public sealed record Titles(IReadOnlyList<uint> Ids) : UserEnvMessage;

    public static UserEnvMessage Read(ByteReader r) {
        switch (r.ReadByte()) {
            case 0: return new TitleAdded(r.ReadInt());
            case 1: { int id = r.ReadInt(); return new TitleChanged(id, r.ReadInt()); }
            case 2: {
                int n = r.ReadInt();
                var a = new List<uint>();
                for (int i = 0; i < n; i++) a.Add((uint) r.ReadInt());
                return new Titles(a);
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
    public long HomeCommendMonth; public int HomeCommendMonthly, HomeCommendPoint;
    public readonly long[] Stats0 = new long[PcInfo.StatCount], Stats1 = new long[PcInfo.StatCount], Stats2 = new long[PcInfo.StatCount];
    /// <summary>The proxies' trophy triples by proxy id (ProxyGameObj AddPlayer, scenestategame.cpp:11358; removed by mode 4),
    /// with their character ids: the record reads the own one (FindProxyObject; none = 0).</summary>
    readonly Dictionary<int, (long CharacterId, int Combat, int Adventure, int Lifestyle)> proxies = new();
    public readonly List<uint> Titles = new();
    /// <summary>The own FieldAddUser's title layer period (SetLayerPeriod, scenestategame.cpp:11248) and its layer-2
    /// outfits (SetVariant(2), :11250-11264).</summary>
    public long CodiStart, CodiEnd; public int CodiItems;
    public readonly StatPointBook StatPoints = new();
    public event Action Changed;

    /// <summary>The own FieldAddUser: object id, user block, the title (PC::FUN_141661c00 = m_n10f4) and the architect score
    /// values after the ride block (scenestategame.cpp:11220-11224).</summary>
    public void Apply(FieldAddUser u) {
        ObjectId = u.ObjectId; User = u.User; Title = u.Title;
        HomeCommendMonth = u.HomeCommendMonth; HomeCommendMonthly = u.HomeCommendMonthly; HomeCommendPoint = u.HomeCommendPoint;
        CodiStart = u.SkinTabStart; CodiEnd = u.SkinTabEnd; CodiItems = u.Outfit2.Count;
        Changed?.Invoke();
    }

    /// <summary>The own Stat (0x2E): the values of the stats it carries (PC's m_unk_1c0[0..2]).</summary>
    public void Apply(PlayerStat s) {
        foreach (var (stat, v) in s.Values) {
            if (stat >= PcInfo.StatCount) continue;
            Stats0[stat] = v[0]; Stats1[stat] = v[1]; Stats2[stat] = v[2];
        }
        Changed?.Invoke();
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
            Picture = User.Picture ?? "", Name = User.Name ?? "", GuildName = User.GuildName ?? "", Motto = User.Motto ?? "",
            PrestigeLevel = User.PrestigeLevel, CodiStart = CodiStart, CodiEnd = CodiEnd, CodiItems = CodiItems,
        };
        foreach (var t in proxies.Values)
            if (t.CharacterId == User.CharacterId) { i.Trophy = t.Combat + t.Adventure + t.Lifestyle; break; }
        Array.Copy(Stats0, i.Stats0, PcInfo.StatCount); Array.Copy(Stats1, i.Stats1, PcInfo.StatCount); Array.Copy(Stats2, i.Stats2, PcInfo.StatCount);
        i.Titles.AddRange(Titles);
        return i;
    }
}
