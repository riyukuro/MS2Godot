// Inbound 0x11B Survival (KMS 0x119, commonfiles/opcodes.h:280). The decompiled client names no handler for this opcode, so the layouts come from the GMS2 recordings.
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>One medal row of the medal list: i32 id, i64 expiry (the recordings hold long.MaxValue).</summary>
public readonly record struct SurvivalMedal(int Id, long Expiry);

/// <summary>One medal type of the medal list: i32 equipped medal id, i32 count, then count medals.</summary>
public sealed record SurvivalMedalType(int EquippedId, IReadOnlyList<SurvivalMedal> Medals);

/// <summary>
/// Inbound Survival: u8 mode. Mode 0x17 is the pass stats: i64 account id, i32, u8 gold pass active, i64 exp, i32 level, i32 silver level
/// reward claimed, i32 gold level reward claimed, i64 exp gained (41 bytes). Mode 0x1E is the medal list: u8 type count, per type i32
/// equipped id, i32 count, count × (i32 id, i64 expiry). Mode 0x16 is recorded with a 46-byte body of zeros except one byte (1 or 4) after
/// a zero i32; its meaning is not known and the body is kept raw. Any other mode reads nothing more. STILL-OPEN: no client handler is
/// named for this opcode, so the field names are the Maple2 server's, the unnamed i32 after the account id and the mode 0x16 body are
/// unknown, and the other server commands (results, last standing, kill notices, matching) are not recorded.
/// </summary>
public sealed record SurvivalPacket(byte Mode)
{
    public const byte Unknown22Mode = 0x16, UpdateStatsMode = 0x17, LoadMedalsMode = 0x1E;

    public long AccountId { get; init; }
    public int Unknown { get; init; }
    public bool GoldPass { get; init; }
    public long Exp { get; init; }
    public int Level { get; init; }
    public int SilverClaimed { get; init; }
    public int GoldClaimed { get; init; }
    public long ExpGained { get; init; }
    public IReadOnlyList<SurvivalMedalType> Medals { get; init; } = Array.Empty<SurvivalMedalType>();
    public byte[] RawBody { get; init; } = Array.Empty<byte>();

    public static SurvivalPacket Read(ByteReader r)
    {
        byte mode = r.ReadByte();
        var o = new SurvivalPacket(mode);
        switch (mode) {
            case UpdateStatsMode:
                o = o with {
                    AccountId = r.ReadLong(), Unknown = r.ReadInt(), GoldPass = r.ReadBool(), Exp = r.ReadLong(), Level = r.ReadInt(),
                    SilverClaimed = r.ReadInt(), GoldClaimed = r.ReadInt(), ExpGained = r.ReadLong(),
                };
                break;
            case LoadMedalsMode: {
                int types = r.ReadByte();
                var list = new List<SurvivalMedalType>(types);
                for (int i = 0; i < types; i++) {
                    int equipped = r.ReadInt();
                    int count = r.ReadInt();
                    if (count < 0 || count > r.Available / 12) throw new InvalidDataException($"Survival medal count {count} with {r.Available} bytes left");
                    var medals = new List<SurvivalMedal>(count);
                    for (int j = 0; j < count; j++) medals.Add(new SurvivalMedal(r.ReadInt(), r.ReadLong()));
                    list.Add(new SurvivalMedalType(equipped, medals));
                }
                o = o with { Medals = list };
                break;
            }
            case Unknown22Mode:
                o = o with { RawBody = r.ReadBytes(r.Available) };
                return o;
            default:
                return o;
        }
        r.ExpectEnd("Survival " + mode);
        return o;
    }
}
