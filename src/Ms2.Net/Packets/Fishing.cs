using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// Fishing (0x73; CFishingManager::FUN_140cd9840 @0x140cd9840, fishingmanager.cpp:546), the part the Fishing Book reads: a
/// u8 mode, then 7 (the caught fish list): an i32 count of records; 8 (a catch result): i32 fish id, i32 size, u8 success,
/// u8 auto fishing, then the record when it succeeded. A record (CFishCube::FUN_1403b0bf0 @0x1403b0bf0, fishcube.cpp:74) is
/// four i32: the fish id, the total catch count (+0x1c), the big catch count (+0x20) and the best size (+0x24).
/// STILL-OPEN: the other modes (0 1 2 3 4 5 6 9 10 0xb) are the casting, the notices and the cubes of fishing itself, not the
/// book's; they are read as null.
/// </summary>
public abstract record FishingMessage {
    /// <summary>One CFish record.</summary>
    public readonly record struct Record(uint Id, int TotalCount, int BigCount, uint Size);

    /// <summary>Mode 7: the whole list, which replaces the player's.</summary>
    public sealed record List(IReadOnlyList<Record> Fishes) : FishingMessage;
    /// <summary>Mode 8: a result. <see cref="Fish"/> is set when it succeeded.</summary>
    public sealed record Result(uint FishId, uint Size, bool Success, bool Auto, Record? Fish) : FishingMessage;

    // No end check: OnFishing (fishingmanager.cpp) reads the fields it needs and does not test the packet's end.
    public static FishingMessage Read(ByteReader r) {
        byte mode = r.ReadByte();
        switch (mode) {
            case 7: {
                int n = r.ReadInt();
                var list = new List<Record>();
                for (int i = 0; i < n; i++) list.Add(ReadRecord(r));
                return new List(list);
            }
            case 8: {
                uint id = (uint) r.ReadInt();
                uint size = (uint) r.ReadInt();
                bool ok = r.ReadByte() != 0;
                bool auto = r.ReadByte() != 0;
                Record? rec = ok ? ReadRecord(r) : null;
                return new Result(id, size, ok, auto, rec);
            }
        }
        return null;
    }

    static Record ReadRecord(ByteReader r) => new((uint) r.ReadInt(), r.ReadInt(), r.ReadInt(), (uint) r.ReadInt());
}
