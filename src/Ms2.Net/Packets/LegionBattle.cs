// Inbound 0xF6 LegionBattle (KMS 0xF4): CSceneStateGame::OnLegionBattle (@0x14173a720, scenestategame.cpp:8480).
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>
/// One guaranteed NPC spawn (CFieldMetaTable::SetSpawnNpcGuarantee): i32 field id, i32 NPC id, i64 time (CMsTime). 16 bytes.
/// </summary>
public readonly record struct SpawnGuarantee(int FieldId, int NpcId, long Time)
{
    public const int Size = 16;

    public static SpawnGuarantee Read(ByteReader r) => new(r.ReadInt(), r.ReadInt(), r.ReadLong());
}

/// <summary>
/// Inbound LegionBattle: u8 mode. Mode 0 replaces the list: u16 count, then count rows. Mode 1 is one row. Mode 2 is an i32 field id whose
/// guarantee is removed. Any other mode reads nothing more, as the client's if-chain does. Recorded on GMS2: mode 0 (an empty list and
/// a 19-row list) and mode 1; mode 2 is not recorded (read from the client).
/// </summary>
public sealed record LegionBattle(byte Mode, IReadOnlyList<SpawnGuarantee> Rows, int RemovedFieldId)
{
    public const byte Replace = 0, Set = 1, Remove = 2;

    public static LegionBattle Read(ByteReader r)
    {
        byte mode = r.ReadByte();
        var rows = new List<SpawnGuarantee>();
        int removed = 0;
        switch (mode) {
            case Replace: {
                int count = (ushort) r.ReadShort();
                if (count > r.Available / SpawnGuarantee.Size) throw new InvalidDataException($"LegionBattle count {count} with {r.Available} bytes left");
                for (int i = 0; i < count; i++) rows.Add(SpawnGuarantee.Read(r));
                r.ExpectEnd("LegionBattle list");
                break;
            }
            case Set:
                rows.Add(SpawnGuarantee.Read(r));
                r.ExpectEnd("LegionBattle set");
                break;
            case Remove:
                removed = r.ReadInt();
                r.ExpectEnd("LegionBattle remove");
                break;
        }
        return new LegionBattle(mode, rows, removed);
    }
}
