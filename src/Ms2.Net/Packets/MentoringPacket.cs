// Inbound 0x10C Mentoring (KMS 0x10A): CMentoringManager::OnPacket (@0x140b719e0, mentoringmanager.cpp:255) and the payload readers of mentor.cpp.
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>One CMentor (CMentor::Read, mentor.cpp @0x14069af00): i64 a, b, c, string name, u16 level, string guild, u32 x, u8 online, u32 y.
/// The client names none of a, b, c, x, y (a is the id the approve and decline notices match on, c the id of the pair-mode list).</summary>
public sealed record MentorEntry(long A, long B, long C, string Name, ushort Level, string Guild, int X, bool Online, int Y)
{
    public const int MinSize = 8 * 3 + 2 + 2 + 2 + 4 + 1 + 4;

    public static MentorEntry Read(ByteReader r) =>
        new(r.ReadLong(), r.ReadLong(), r.ReadLong(), r.ReadUnicodeString(), (ushort) r.ReadShort(), r.ReadUnicodeString(), r.ReadInt(), r.ReadBool(), r.ReadInt());
}

/// <summary>Pair mode of one mentee (CMentorPairModePacket::CPairMode, @0x14069abe0): i64 id, u8 enabled; the client stores it in the mentee's online flag.</summary>
public readonly record struct MentorPairMode(long Id, bool Enabled);

/// <summary>
/// Inbound Mentoring: u8 mode, and the payload readers of mentor.cpp. Layouts by mode: 1 result (u8 result, u32 player id), 2 mentor list and
/// 3 mentee list (u32 count, entries), 15 pair modes (u32 count, i64 + u8 each), 5 invite (i64 id, string name of at most 24 characters),
/// 6 approved and 7 declined (i64 id), 10 date (u32 value, i64 id), 11 counts (u32 a, u32 b, i64 id), 12 ids (i64 a, i64 b), 14 request
/// (string name, entry), 16 error (u8 result, u32 value); modes 4, 8, 9 and any other read nothing more, as the client's handlers do.
/// Recorded on GMS2: mode 3 (empty lists) and mode 12; the other layouts follow the client and have no recording.
/// </summary>
public sealed record MentoringPacket(byte Mode)
{
    public const int MaxNameLength = 24;

    public byte Result { get; init; }
    public int Value { get; init; }
    public int ValueB { get; init; }
    public long Id { get; init; }
    public long IdB { get; init; }
    public string Name { get; init; } = "";
    public IReadOnlyList<MentorEntry> Entries { get; init; } = Array.Empty<MentorEntry>();
    public IReadOnlyList<MentorPairMode> PairModes { get; init; } = Array.Empty<MentorPairMode>();

    public static MentoringPacket Read(ByteReader r)
    {
        byte mode = r.ReadByte();
        var o = new MentoringPacket(mode);
        switch (mode) {
            case 1: case 16:
                o = o with { Result = r.ReadByte(), Value = r.ReadInt() };
                break;
            case 2: case 3: {
                int count = Count(r, MentorEntry.MinSize);
                var list = new List<MentorEntry>(count);
                for (int i = 0; i < count; i++) list.Add(MentorEntry.Read(r));
                o = o with { Entries = list };
                break;
            }
            case 15: {
                int count = Count(r, 9);
                var list = new List<MentorPairMode>(count);
                for (int i = 0; i < count; i++) list.Add(new MentorPairMode(r.ReadLong(), r.ReadBool()));
                o = o with { PairModes = list };
                break;
            }
            case 5:
                o = o with { Id = r.ReadLong(), Name = r.ReadUnicodeString() };
                if (o.Name.Length > MaxNameLength) throw new InvalidDataException($"Mentoring invite name of {o.Name.Length} characters");
                break;
            case 6: case 7:
                o = o with { Id = r.ReadLong() };
                break;
            case 10:
                o = o with { Value = r.ReadInt(), Id = r.ReadLong() };
                break;
            case 11:
                o = o with { Value = r.ReadInt(), ValueB = r.ReadInt(), Id = r.ReadLong() };
                break;
            case 12:
                o = o with { Id = r.ReadLong(), IdB = r.ReadLong() };
                break;
            case 14: {
                string name = r.ReadUnicodeString();
                o = o with { Name = name, Entries = new[] { MentorEntry.Read(r) } };
                break;
            }
            default:
                return o;
        }
        r.ExpectEnd("Mentoring " + mode);
        return o;
    }

    static int Count(ByteReader r, int minRow)
    {
        long count = (uint) r.ReadInt();
        if (count > r.Available / minRow) throw new InvalidDataException($"Mentoring count {count} with {r.Available} bytes left");
        return (int) count;
    }
}
