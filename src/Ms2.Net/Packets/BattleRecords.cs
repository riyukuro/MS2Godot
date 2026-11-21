// CBattleRecordManager (client/ms2/battlerecordmanager.cpp): the damage, exp and meso tallies the DPS meter windows read.
using System.Globalization;

namespace Ms2.Net.Packets;

/// <summary>
/// CBattleRecord: one character's tallies. The slots (AddAmount's type): 0 damage dealt, 1 damage taken, 2 damage values
/// dealt, 3 hits, 4 critical hits, 5 exp, 6 meso, 7 damage dealt to boss npcs (class 6).
/// </summary>
public sealed class BattleRecord {
    public const int Dealt = 0, Taken = 1, Attacks = 2, Hits = 3, Crits = 4, Exp = 5, Meso = 6, BossDealt = 7;
    readonly long[] counts = new long[8];
    public BattleRecord(long id) { Id = id; }
    /// <summary>GetId: the character id.</summary>
    public long Id { get; }
    /// <summary>GetAmount.</summary>
    public long Amount(int type) => counts[type];
    /// <summary>AddAmount.</summary>
    public void Add(int type, long amount) => counts[type] += amount;
    /// <summary>Reset: every slot zeroed.</summary>
    public void Reset() => Array.Clear(counts);
}

/// <summary>
/// CBattleRecordGroup: the records of one meter (0 individual, 1 party, 2 chaos raid) and its run state. The log file the client
/// writes with the debug flag DAT_143ab5b00 (log\battlerecord_*.txt) is not written.
/// </summary>
public sealed class BattleRecordGroup {
    public const int Individual = 0, Party = 1, Chaos = 2;
    readonly Dictionary<long, BattleRecord> records = new();
    readonly BattleRecordManager owner;
    internal BattleRecordGroup(BattleRecordManager owner, int type) { this.owner = owner; Type = type; }
    public int Type { get; }
    /// <summary>m_active (read raw; <see cref="IsActive"/> adds the chaos room test).</summary>
    public bool Running { get; private set; }
    /// <summary>FUN_140a03600 (@0x140a03600): the start tick, 0 when stopped.</summary>
    public uint StartTick { get; private set; }
    public IReadOnlyCollection<BattleRecord> Records => records.Values;

    /// <summary>Clear (@0x140a03300): stopped, with no record.</summary>
    public void Clear() { Running = false; StartTick = 0; records.Clear(); }
    /// <summary>StartIfInactive (@0x140a03350): the raw flag, not the chaos test.</summary>
    public void StartIfInactive() { if (!Running) Start(); }
    /// <summary>Start (@0x140a03380): running from now, every record reset.</summary>
    public void Start() { Running = true; StartTick = owner.Tick(); ResetAll(); }
    /// <summary>Stop (@0x140a03460): stopped, every record reset (the records stay).</summary>
    public void Stop() { Running = false; StartTick = 0; ResetAll(); }
    void ResetAll() { foreach (var r in records.Values) r.Reset(); }
    /// <summary>IsActive (@0x140a03530): the chaos group also needs a current dungeon room with chaosDamageMeter.</summary>
    public bool IsActive => (Type != Chaos || owner.ChaosRoomMeter()) && Running;
    /// <summary>AddRecord (@0x140a03620): a new zeroed record unless the id has one.</summary>
    public void AddRecord(long id) { if (!records.ContainsKey(id)) records[id] = new BattleRecord(id); }
    /// <summary>RemoveRecord (@0x140a036d0).</summary>
    public void RemoveRecord(long id) => records.Remove(id);
    /// <summary>HasRecord (@0x140a03700).</summary>
    public bool HasRecord(long id) => records.ContainsKey(id);
    /// <summary>GetRecord (@0x140a03750): null without one.</summary>
    public BattleRecord GetRecord(long id) => records.TryGetValue(id, out var r) ? r : null;
    /// <summary>RecordAmountById (@0x140a03870).</summary>
    public void RecordAmount(long id, int type, long amount) => GetRecord(id)?.Add(type, amount);
}

/// <summary>
/// CBattleRecordManager (TSingleton, made in ms2.cpp:2031): three record groups fed by every damage record the field applies
/// (PCBase::vfunc_113 @0x141674ec0, the end of PC's and CNpc's vfunc_33/34/35 on the object hit), the exp packet and the
/// meso packet. CPartyManager adds and removes the party members' records (groups 1 and 2).
/// The world lookups are delegates: <see cref="CharacterIdOf"/> is FUN_141bd1980 (a player object's character id, else 0),
/// <see cref="PetOwnerOf"/> FindPetById(id)->GetOwnerId (0 without a pet; field pets are not tracked yet, STILL-OPEN),
/// <see cref="NpcClassOf"/> FindNpcById(id)->GetNpcClass (CNpcData m_nClass, 0 without an npc).
/// </summary>
public sealed class BattleRecordManager {
    readonly BattleRecordGroup[] groups;
    public BattleRecordManager() {
        groups = new[] { new BattleRecordGroup(this, 0), new BattleRecordGroup(this, 1), new BattleRecordGroup(this, 2) };
    }

    /// <summary>GetTickCountMs2: the client's millisecond tick (unsigned, wraps).</summary>
    public Func<uint> Tick = () => (uint) Environment.TickCount64;
    /// <summary>The chaos group's room test: DAT_143ab2884 (the dungeon room id) set, its DungeonRoomTable row found, and the
    /// row's chaosDamageMeter.</summary>
    public Func<bool> ChaosRoomMeter = () => false;
    public Func<int, long> CharacterIdOf = _ => 0;
    public Func<int, int> PetOwnerOf = _ => 0;
    public Func<int, int> NpcClassOf = _ => 0;

    /// <summary>GetGroup (@0x140a040a0).</summary>
    public BattleRecordGroup Group(int type) => groups[type];
    /// <summary>StartGroupIfInactive (@0x140a03f60).</summary>
    public void StartGroupIfInactive(int type) => groups[type].StartIfInactive();
    /// <summary>StartGroup (@0x140a03fa0).</summary>
    public void StartGroup(int type) => groups[type].Start();
    /// <summary>StopGroup (@0x140a03fe0).</summary>
    public void StopGroup(int type) => groups[type].Stop();
    /// <summary>IsGroupActive (@0x140a04020).</summary>
    public bool IsGroupActive(int type) => groups[type].IsActive;
    /// <summary>GetGroupStartTick (@0x140a04060).</summary>
    public uint GroupStartTick(int type) => groups[type].StartTick;
    /// <summary>AddRecord (@0x140a04100).</summary>
    public void AddRecord(int type, long id) => groups[type].AddRecord(id);
    /// <summary>RemoveRecord (@0x140a04150).</summary>
    public void RemoveRecord(int type, long id) => groups[type].RemoveRecord(id);
    /// <summary>HasRecord (@0x140a041a0).</summary>
    public bool HasRecord(int type, long id) => groups[type].HasRecord(id);
    /// <summary>GetRecord (@0x140a041f0).</summary>
    public BattleRecord GetRecord(int type, long id) => groups[type].GetRecord(id);

    /// <summary>ClearPartyGroups (@0x140a05610): CPartyManager::ClearParty.</summary>
    public void ClearPartyGroups() { groups[1].Clear(); groups[2].Clear(); }
    /// <summary>AddPartyMemberRecord (@0x140a05670): OnPartyInfo, OnPartyMemberJoin and a new member's OnPartyMemberOnline.</summary>
    public void AddPartyMemberRecord(long id) { AddRecord(1, id); AddRecord(2, id); }
    /// <summary>RemovePartyMemberRecord (@0x140a056c0): RemoveMemberAt.</summary>
    public void RemovePartyMemberRecord(long id) { RemoveRecord(1, id); RemoveRecord(2, id); }

    /// <summary>FUN_140a05490 (@0x140a05490): exp (slot 5) in every active group; OnExpUp (scenestategame.cpp:4174) with the
    /// own character's id for a mode 0 gain above 0 with a local player, whoever keeps the number.</summary>
    public void RecordExp(long id, long amount) { foreach (var g in groups) if (g.IsActive) g.RecordAmount(id, BattleRecord.Exp, amount); }
    /// <summary>FUN_140a05550 (@0x140a05550): meso (slot 6) in every active group; OnMeso (scenestategame.cpp:7016) with the
    /// own character's id for a balance rise.</summary>
    public void RecordMeso(long id, long amount) { foreach (var g in groups) if (g.IsActive) g.RecordAmount(id, BattleRecord.Meso, amount); }

    /// <summary>
    /// RecordDamage (@0x140a04ef0) for one damage record: <paramref name="source"/> is the record's +0x2c (the attacker object),
    /// <paramref name="target"/> its +0x28 (the object hit). The source is a player's character id, else a pet's owner's. In each
    /// active group the source's record takes the value count (slot 2), |total| (slot 0, and slot 7 on a class 6 npc), a hit per
    /// value of kind 0 and a hit and a critical per kind 1; the target's record takes |total| (slot 1). The total is signed.
    /// </summary>
    public void RecordDamage(int source, int target, IReadOnlyList<Hit> hits) {
        long sourceId = CharacterIdOf(source);
        if (sourceId == 0 && PetOwnerOf(source) is var owner and not 0) sourceId = CharacterIdOf(owner);
        long targetId = CharacterIdOf(target);
        long total = 0;
        foreach (var h in hits) total += h.Damage;
        total = Math.Abs(total);
        foreach (var g in groups) {
            if (!g.IsActive) continue;
            if (g.GetRecord(sourceId) is { } rec) {
                rec.Add(BattleRecord.Attacks, hits.Count);
                rec.Add(BattleRecord.Dealt, total);
                if (NpcClassOf(target) == 6) rec.Add(BattleRecord.BossDealt, total);
                foreach (var h in hits) {
                    if (h.Flag == 0) rec.Add(BattleRecord.Hits, 1);
                    else if (h.Flag == 1) { rec.Add(BattleRecord.Hits, 1); rec.Add(BattleRecord.Crits, 1); }
                }
            }
            g.GetRecord(targetId)?.Add(BattleRecord.Taken, total);
        }
    }

    /// <summary>
    /// Every damage record of a SkillDamage packet the field applies, fed to <see cref="RecordDamage"/>. A record reaches the
    /// tally only through the hit object's vf34 (CMagicService::FUN_14146bc20, FindObjectById(+0x28)), so its target must be a
    /// field object. Mode 1 (FUN_14171cdf0): the packet's owner on each target. Mode 3 (OnDotDamage): the owner on the target,
    /// and only when the target is a PCBase (FUN_141bd1200). Mode 5 (scenestategame.cpp:7784): only with the region skill
    /// object. Mode 6 (OnSplashDamage): no owner (0), so only damage taken. Modes 0, 4, 7 and 8 carry no damage record.
    /// </summary>
    public void Record(SkillDamage d, Func<int, bool> objectExists, Func<int, bool> isPcBaseObject, Func<int, bool> isRegionSkill) {
        foreach (var (source, target, hits) in DamageRecords(d, objectExists, isPcBaseObject, isRegionSkill))
            RecordDamage(source, target, hits);
    }

    /// <summary>The (attacker, object hit, hits) of each record <see cref="Record"/> feeds.</summary>
    public static List<(int Source, int Target, Hit[] Hits)> DamageRecords(SkillDamage d, Func<int, bool> objectExists,
        Func<int, bool> isPcBaseObject, Func<int, bool> isRegionSkill) {
        var list = new List<(int, int, Hit[])>();
        if (d == null || d.Raw != null) return list;
        int source;
        switch (d.Mode) {
            case 1: source = d.OwnerId; break;
            case 3: if (!isPcBaseObject(d.TargetId)) return list; source = d.OwnerId; break;
            case 5: if (!isRegionSkill(d.RegionId)) return list; source = d.OwnerId; break;
            case 6: source = 0; break;
            default: return list;
        }
        foreach (var t in d.Targets)
            if (objectExists(t.TargetId)) list.Add((source, t.TargetId, t.Hits));
        return list;
    }

    /// <summary>
    /// GetRecordValueText (@0x140a04260): the meter value of a mode, empty without a record. 0 damage dealt, 1 boss damage,
    /// 2 and 3 their per second over the run (float seconds, 0 for no damage or no time), 4 hit rate and 5 critical rate
    /// (%f, float), 6 damage taken, 7 exp, 8 meso; integers as %I64d.
    /// </summary>
    public string RecordValueText(int group, long id, int type) {
        var r = groups[group].GetRecord(id);
        if (r == null) return "";
        switch (type) {
            case 0: return Int(r.Amount(BattleRecord.Dealt));
            case 1: return Int(r.Amount(BattleRecord.BossDealt));
            case 2: return Int(PerSecond(group, r.Amount(BattleRecord.Dealt)));
            case 3: return Int(PerSecond(group, r.Amount(BattleRecord.BossDealt)));
            case 4: { long total = r.Amount(BattleRecord.Attacks), hits = r.Amount(BattleRecord.Hits);
                return Real(total != 0 ? 100.0f / total * hits : 0.0f); }
            case 5: { long hits = r.Amount(BattleRecord.Hits), crits = r.Amount(BattleRecord.Crits);
                return Real(hits != 0 ? 100.0f / hits * crits : 0.0f); }
            case 6: return Int(r.Amount(BattleRecord.Taken));
            case 7: return Int(r.Amount(BattleRecord.Exp));
            case 8: return Int(r.Amount(BattleRecord.Meso));
        }
        return "";
    }

    long PerSecond(int group, long damage) {
        float seconds = ElapsedMs(group) * 0.001f;                       // FUN_14028d280
        return damage == 0 || seconds == 0.0f ? 0 : (long) ((float) damage / seconds);
    }

    /// <summary>The run's milliseconds: GetTickCountMs2() - start, unsigned.</summary>
    public uint ElapsedMs(int group) => unchecked(Tick() - groups[group].StartTick);

    /// <summary>GetElapsedTimeText (@0x140a04b00): FUN_141b70810, hh:mm:ss (hours not wrapped).</summary>
    public string ElapsedTimeText(int group) {
        int s = (int) (ElapsedMs(group) / 1000);
        return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", s / 3600, s / 60 % 60, s % 60);
    }

    /// <summary>FUN_140a04bc0 (@0x140a04bc0): the common string key naming a mode, empty for others.</summary>
    public static string ModeKey(int type) => type switch {
        0 => "s_dps_type_caster_damage",
        1 => "s_dps_type_caster_damage_boss",
        2 => "s_dps_type_dps",
        3 => "s_dps_type_dps_boss",
        4 => "s_dps_type_hit_rate",
        5 => "s_dps_type_critical_rate",
        6 => "s_dps_type_target_damage",
        7 => "s_dps_type_gain_exp",
        8 => "s_dps_type_gain_meso",
        _ => "",
    };

    static string Int(long v) => v.ToString(CultureInfo.InvariantCulture);
    static string Real(float v) => ((double) v).ToString("F6", CultureInfo.InvariantCulture);
}
