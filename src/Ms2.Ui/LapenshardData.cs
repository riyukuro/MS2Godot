using Ms2.Net.Packets;

namespace Ms2.Ui;

/// <summary>
/// The lapenshards the local player wears: MyPC's slot to item id table, which CSceneStateGame::OnLapenshard
/// (@0x14173d2e0, scenestategame.cpp:8743) fills. Mode 0 clears it and sets every listed slot (ClearLapenshards, then
/// EquipLapenshard(slot, id, 1)); mode 1 sets one (EquipLapenshard(slot, id, 0)); mode 2 removes one
/// (UnequipLapenshard). A live session owns one and applies every packet to it on arrival; the dialog reads
/// <see cref="GetItemId"/> as MyPC::GetLapenshardItemId. Modes 4 to 6 belong to the dialog's upgrade tab and leave it alone.
/// STILL-OPEN: UpdateLapenshardGrades (the grade effect after mode 0) and the HUD refresh FUN_1419f37f0(5) are not ported.
/// </summary>
public sealed class LapenshardData
{
    readonly SortedDictionary<int, int> worn = new();

    public int Count => worn.Count;
    /// <summary>MyPC::GetLapenshardItemId: the item worn in the slot, 0 for none.</summary>
    public int GetItemId(int slot) => worn.TryGetValue(slot, out var id) ? id : 0;

    /// <summary>Whether <paramref name="m"/> changed the worn table (modes 0 to 2).</summary>
    public bool Apply(LapenshardMessage m)
    {
        switch (m)
        {
            case LapenshardMessage.Worn w:
                worn.Clear();
                foreach (var (slot, id) in w.Entries) worn[slot] = id;
                return true;
            case LapenshardMessage.Equipped e:
                worn[e.Slot] = e.ItemId;
                return true;
            case LapenshardMessage.Unequipped u:
                worn.Remove(u.Slot);
                return true;
        }
        return false;
    }
}
