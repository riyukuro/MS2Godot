using System;
using System.Collections.Generic;

namespace Ms2.Ui.Skills;

/// <summary>
/// The field PvP ranking (the client's CPVPManager part Unk_PvpTimer, unit_141017950_pvprankinglist.cpp): one row per
/// character, kept in rank order, from Pvp 0x67 (score row) and 0x6a (damage); Pvp 'f' (CPVPManager::Clear,
/// pvpmanager.cpp) empties it. The ranking and result dialogs (UIPvPRankingDialog 0x4e9a, UIPvPResultDialog 0x4e9b) that
/// read it are not ported.
/// </summary>
public sealed class PvpRankingList
{
    /// <summary>A row (Unk_PvpTimerItem @0x14101a120): the name is "Unknown" unless the character's PC was in the field
    /// when the row was made (PCBase::vfunc_77).</summary>
    public sealed class Row
    {
        public long CharacterId { get; internal set; }
        public string Name { get; internal set; } = "";
        public int Score { get; internal set; }
        public int Kill { get; internal set; }
        public int Death { get; internal set; }
        public uint N1c { get; internal set; }
        public ulong Damage { get; internal set; }
        public bool Online { get; internal set; }
    }

    /// <summary>The name of a row whose character has no PC in the field (unit_141017950_pvprankinglist.cpp:272).</summary>
    public const string UnknownName = "Unknown";

    readonly List<Row> rows = new();
    readonly Dictionary<long, int> ranks = new();

    /// <summary>WorldService::FUN_141bd1530 (worldservice_1.cpp:6211) then vfunc_77: the name of the field's PC with this
    /// character id, null when none is in the field.</summary>
    public Func<long, string> FieldPcName = _ => null;
    /// <summary>The local PC's CPvPInfo mode (m_nMode), which picks the sort; null without a local PC (no sort, no new ranks).</summary>
    public Func<int?> OwnMode = () => null;

    /// <summary>GetRowCount.</summary>
    public int Count => rows.Count;

    /// <summary>FUN_141017f40: the character's rank (1-based), 0 when it has none.</summary>
    public int RankOf(long characterId) => ranks.TryGetValue(characterId, out int r) ? r : 0;

    /// <summary>FUN_141017fb0: the row at a rank, null past the list.</summary>
    public Row At(int rank) => rank >= 1 && rank <= rows.Count ? rows[rank - 1] : null;

    /// <summary>Unk_PvpTimer::Clear (@0x141017e80): the rows and the ranks go.</summary>
    public void Clear()
    {
        rows.Clear();
        ranks.Clear();
    }

    /// <summary>
    /// FUN_141018260 (pvp 0x67, :261-291): a ranked character's row takes the new numbers, any other gets a new row at the
    /// end; then every row's online flag is whether its PC is in the field, and the list is sorted. Returns the rank before
    /// and after (OnMessage's nFrom / nTo for the result dialog).
    /// </summary>
    public (int From, int To) SetScore(long characterId, int score, int kill, int death, uint n1c)
    {
        int from = RankOf(characterId);
        if (from != 0)
        {
            var row = rows[from - 1];
            row.Score = score; row.Kill = kill; row.Death = death; row.N1c = n1c;
        }
        else rows.Add(new Row { CharacterId = characterId, Name = FieldPcName(characterId) ?? UnknownName, Score = score, Kill = kill, Death = death, N1c = n1c });
        foreach (var row in rows) row.Online = FieldPcName(row.CharacterId) != null;
        Sort();
        return (from, RankOf(characterId));
    }

    /// <summary>
    /// FUN_141018540 (pvp 0x6a, :294-315): a ranked character's row takes the damage, any other gets a new zero row with it;
    /// the online flags are not refreshed (a new row's flag is left unset by the ctor; here false). Then the list is sorted.
    /// </summary>
    public (int From, int To) SetDamage(long characterId, ulong damage)
    {
        int from = RankOf(characterId);
        if (from != 0) rows[from - 1].Damage = damage;
        else rows.Add(new Row { CharacterId = characterId, Name = FieldPcName(characterId) ?? UnknownName, Damage = damage });
        Sort();
        return (from, RankOf(characterId));
    }

    /// <summary>
    /// Pvp 0x68 (OnMessage :166-178): the first row is set again with its own numbers (FUN_141018260: the online flags and
    /// the sort run again); the rank read after it goes to the result dialog (SetLastResult).
    /// </summary>
    public void RefreshFirst()
    {
        if (At(1) is { } first) SetScore(first.CharacterId, first.Score, first.Kill, first.Death, first.N1c);
    }

    /// <summary>
    /// FUN_141018010 (:240-258): with a local PC, its mode 2 sorts by FUN_141018720 (online first, score, kill, fewer deaths,
    /// damage, then the higher character id) and mode 5 by FUN_141018870 (score, kill, fewer deaths, the lower n1c, then the
    /// higher character id); any other mode keeps the order. The ranks are rebuilt from the order. No local PC: nothing.
    /// </summary>
    void Sort()
    {
        if (OwnMode() is not int mode) return;
        if (mode == 2) StableSort(FfaBefore);
        else if (mode == 5) StableSort(PunchBefore);
        ranks.Clear();
        for (int i = 0; i < rows.Count; i++) ranks[rows[i].CharacterId] = i + 1;
    }

    // std::sort is not stable, but the orders end on the character id, so only rows equal in every key (the same id, or
    // two offline rows in mode 2) can tie; an insertion sort keeps their order.
    void StableSort(Func<Row, Row, bool> before)
    {
        for (int i = 1; i < rows.Count; i++)
        {
            var r = rows[i];
            int j = i - 1;
            while (j >= 0 && before(r, rows[j])) { rows[j + 1] = rows[j]; j--; }
            rows[j + 1] = r;
        }
    }

    /// <summary>FUN_141018720 (:318-345).</summary>
    static bool FfaBefore(Row a, Row b)
    {
        if (!a.Online) return false;
        if (!b.Online) return true;
        if (a.Score != b.Score) return a.Score > b.Score;
        if (a.Kill != b.Kill) return a.Kill > b.Kill;
        if (a.Death != b.Death) return a.Death < b.Death;
        if (a.Damage != b.Damage) return a.Damage > b.Damage;
        return (ulong)a.CharacterId > (ulong)b.CharacterId;
    }

    /// <summary>FUN_141018870 (:348-371).</summary>
    static bool PunchBefore(Row a, Row b)
    {
        if (a.Score != b.Score) return a.Score > b.Score;
        if (a.Kill != b.Kill) return a.Kill > b.Kill;
        if (a.Death != b.Death) return a.Death < b.Death;
        if (a.N1c != b.N1c) return a.N1c < b.N1c;
        return (ulong)a.CharacterId > (ulong)b.CharacterId;
    }
}
