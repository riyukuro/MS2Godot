using Ms2.Net.Packets;

namespace Ms2.Ui;

/// <summary>
/// The lapenshards the local player wears: MyPC's slot to item id table, which CSceneStateGame::OnLapenshard
/// (@0x14173d2e0, scenestategame.cpp:8743) fills. Mode 0 clears it and sets every listed slot (ClearLapenshards, then
/// EquipLapenshard(slot, id, 1)); mode 1 sets one (EquipLapenshard(slot, id, 0)); mode 2 removes one
/// (UnequipLapenshard). A live session owns one and applies every packet to it on arrival; the dialog reads
/// <see cref="GetItemId"/> as MyPC::GetLapenshardItemId. Modes 4 to 6 belong to the dialog's upgrade tab and leave it alone.
/// The client keeps two tables, m_map16b0 (slot to id) and m_map16f8 (id to how many times it was equipped): EquipLapenshard
/// (@0x14152f530, mypc.cpp:4195) ignores an id without a lapenshard table row and counts the id up, and never counts a replaced id
/// down; UnequipLapenshard (@0x14152f620) counts the removed id down (the key goes at 1). The packets are kept in order and
/// <see cref="Replay"/> runs them through those two rules with the caller's table, so a packet that arrives before the table
/// is known is still read right. STILL-OPEN: UpdateLapenshardGrades (the grade effect after mode 0) and the HUD refresh FUN_1419f37f0(5) are not ported.
/// </summary>
public sealed class LapenshardData
{
    readonly SortedDictionary<int, int> worn = new();
    readonly List<LapenshardMessage> log = new();

    public int Count => worn.Count;
    /// <summary>MyPC::GetLapenshardItemId: the item worn in the slot, 0 for none.</summary>
    public int GetItemId(int slot) => worn.TryGetValue(slot, out var id) ? id : 0;

    /// <summary>MyPC::GetLapenshardItemId over the client's slot table: an id the table has no row for was never stored.</summary>
    public int GetItemId(int slot, Func<int, bool> hasRow) => Replay(hasRow).Slots.TryGetValue(slot, out var id) ? id : 0;

    /// <summary>MyPC::HasEquippedLapenshardOfSameGroup (@0x14152f8b0): some id of the count table (m_map16f8) has the group of
    /// <paramref name="itemId"/>, the group 0 of a row-less id included. <paramref name="group"/> is GetGroup.</summary>
    public bool HasSameGroup(int itemId, Func<int, bool> hasRow, Func<int, int> group)
    {
        int g = group(itemId);
        foreach (var id in Replay(hasRow).Counts.Keys) if (group(id) == g) return true;
        return false;
    }

    /// <summary>The slot table and the count table after every packet, with the client's EquipLapenshard / UnequipLapenshard.</summary>
    public (Dictionary<int, int> Slots, Dictionary<int, int> Counts) Replay(Func<int, bool> hasRow)
    {
        var slots = new Dictionary<int, int>();
        var counts = new Dictionary<int, int>();
        void Equip(int slot, int id)
        {
            if (hasRow != null && !hasRow(id)) return;
            slots[slot] = id;
            counts[id] = (counts.TryGetValue(id, out var n) ? n : 0) + 1;     // the replaced id is not counted down
        }
        foreach (var m in log)
            switch (m)
            {
                case LapenshardMessage.Worn w:
                    slots.Clear(); counts.Clear();
                    foreach (var (slot, id) in w.Entries) Equip(slot, id);
                    break;
                case LapenshardMessage.Equipped e: Equip(e.Slot, e.ItemId); break;
                case LapenshardMessage.Unequipped u:
                    if (slots.Remove(u.Slot, out var gone))
                    {
                        counts.TryGetValue(gone, out var n);
                        if (n == 1) counts.Remove(gone); else counts[gone] = Math.Max(n - 1, 0);
                    }
                    break;
            }
        return (slots, counts);
    }

    /// <summary>Whether <paramref name="m"/> changed the worn table (modes 0 to 2).</summary>
    public bool Apply(LapenshardMessage m)
    {
        switch (m)
        {
            case LapenshardMessage.Worn w:
                worn.Clear(); log.Clear(); log.Add(m);
                foreach (var (slot, id) in w.Entries) worn[slot] = id;
                return true;
            case LapenshardMessage.Equipped e:
                worn[e.Slot] = e.ItemId; log.Add(m);
                return true;
            case LapenshardMessage.Unequipped u:
                worn.Remove(u.Slot); log.Add(m);
                return true;
        }
        return false;
    }
}
