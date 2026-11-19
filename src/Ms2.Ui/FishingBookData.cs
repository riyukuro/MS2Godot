using Ms2.Net.Packets;

namespace Ms2.Ui;

/// <summary>
/// CFishingManager's caught fish (m_fishes, fishingmanager.cpp; RTTI CFish): the records the Fishing Book shows. It outlives
/// the book; a live session owns one and applies every Fishing packet to it (<see cref="Apply"/>).
/// </summary>
public sealed class FishingBookData
{
    /// <summary>CFish: id (+0x18), the total catch count (+0x1c), the big catch count (+0x20), the best size (+0x24) and
    /// whether this is a first catch the book has not shown (+0x2c m_bNew).</summary>
    public sealed class Fish
    {
        public uint Id;
        public int TotalCount, BigCount;
        public uint Size;
        public bool IsNew;
    }

    readonly Dictionary<uint, Fish> fishes = new();

    /// <summary>FindFish @0x140cdba00: the record of a fish id, null without one.</summary>
    public Fish Find(uint id) => fishes.TryGetValue(id, out var f) ? f : null;

    /// <summary>GetAllFish @0x140cdba90.</summary>
    public IEnumerable<Fish> All => fishes.Values;

    public int Count => fishes.Count;

    /// <summary>
    /// FUN_140cd9840 (@0x140cd9840) modes 7 and 8 when feature 0xb (Fishing) is enabled: mode 7 replaces the list (and ends
    /// the fishing state, which is not kept here); mode 8 with a success makes the record of the fish (a new one is marked
    /// m_bNew) and reads its values over it. True when the fish list changed.
    /// </summary>
    public bool Apply(FishingMessage m, bool featureEnabled)
    {
        if (!featureEnabled) return false;
        switch (m)
        {
            case FishingMessage.List l:
                fishes.Clear();
                foreach (var r in l.Fishes) fishes[r.Id] = From(new Fish(), r);   // m_fishes.SetAt(fish->m_nId, fish)
                return true;
            case FishingMessage.Result { Success: true, Fish: { } r } res:
                if (!fishes.TryGetValue(res.FishId, out var f))
                {
                    f = new Fish { IsNew = true };
                    fishes[res.FishId] = f;                                      // SetAt(nFishId, ...): keyed by the packet's id
                }
                From(f, r);                                                      // FUN_1403b0bf0 overwrites id and the three values
                return true;
        }
        return false;
    }

    static Fish From(Fish f, FishingMessage.Record r)
    {
        f.Id = r.Id; f.TotalCount = r.TotalCount; f.BigCount = r.BigCount; f.Size = r.Size;
        return f;
    }

    /// <summary>GetFishSizeGrade @0x140cdc280: 0 without fish data or a record, else the data's grade of the record's size.</summary>
    public int SizeGrade(FishTable table, uint fishId)
        => table.Get(fishId) is { } data && Find(fishId) is { } f ? data.GetSizeGrade(f.Size) : 0;

    /// <summary>FUN_140cdc380 @0x140cdc380: a record of the fish or of its companion exists.</summary>
    public bool Has(FishTable table, uint fishId)
        => Find(fishId) != null || (table.Get(fishId) is { Companion: not 0 } d && Find(d.Companion) != null);

    /// <summary>UIFishingBookDialog::vfunc_67 (@0x140f41940): the book was closed, so every record is no longer new.</summary>
    public void ClearNew()
    {
        foreach (var f in fishes.Values) f.IsNew = false;
    }
}
