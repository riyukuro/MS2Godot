// Inbound 0x12A Wedding (KMS 0x128, commonfiles/opcodes.h:294): CWeddingManager::OnPacket (weddingmanager.cpp:198).
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>Unk_WeddingPartner::Read (weddingproxy.cpp:77): i64 character id, i64 account id, string name, bool online.</summary>
public readonly record struct WeddingPartner(long CharacterId, long AccountId, string Name, bool Online)
{
    public static WeddingPartner Read(ByteReader r) => new(r.ReadLong(), r.ReadLong(), r.ReadUnicodeString(), r.ReadBool());
}

/// <summary>Unk_WeddingLogList entry (weddingproxy.cpp:860): u16 type, i64 amount, i64 time.</summary>
public readonly record struct WeddingLogEntry(ushort Type, long Amount, long Time);

/// <summary>
/// WeddingProxy::Read (weddingproxy.cpp:227): i64, u8 state, i64, i64, groom, bride, i64 meso, two unicode and two ansi messages, i32 count and
/// count log entries. The two i64 after the id and the state are unnamed by the client.
/// </summary>
public sealed record WeddingProxyState(long N18, byte State, long N28, long N30, WeddingPartner Groom, WeddingPartner Bride, long Meso,
    string MessageW0, string MessageW1, string MessageA0, string MessageA1, IReadOnlyList<WeddingLogEntry> Log)
{
    public static WeddingProxyState Read(ByteReader r)
    {
        long n18 = r.ReadLong();
        byte state = r.ReadByte();
        long n28 = r.ReadLong(), n30 = r.ReadLong();
        var groom = WeddingPartner.Read(r);
        var bride = WeddingPartner.Read(r);
        long meso = r.ReadLong();
        string w0 = r.ReadUnicodeString(), w1 = r.ReadUnicodeString(), a0 = r.ReadString(), a1 = r.ReadString();
        int count = r.ReadInt();
        if (count < 0 || count > r.Available / 18) throw new InvalidDataException($"Wedding log count {count} with {r.Available} bytes left");
        var log = new List<WeddingLogEntry>(count);
        for (int i = 0; i < count; i++) log.Add(new WeddingLogEntry((ushort) r.ReadShort(), r.ReadLong(), r.ReadLong()));
        return new WeddingProxyState(n18, state, n28, n30, groom, bride, meso, w0, w1, a0, a1, log);
    }
}

/// <summary>WeddingHallReservationFee::ReadFrom (commonstruct_wa.cpp:436): four i64 values then the i64 total.</summary>
public readonly record struct WeddingHallFee(long V0, long V1, long V2, long V3, long Total)
{
    public static WeddingHallFee Read(ByteReader r) => new(r.ReadLong(), r.ReadLong(), r.ReadLong(), r.ReadLong(), r.ReadLong());
}

/// <summary>WeddingHallTicket::Read (weddingproxy.cpp:387): i64 hall sn, i32 package, i32 hall, i64 time, bool public, i64 booking account, i64 booking character, groom, bride, fee, i32 coupon.</summary>
public sealed record WeddingHallTicket(long HallSn, int PackageId, int HallId, long Time, bool Public, long BookingAccSn, long BookingCharSn,
    string Groom, string Bride, WeddingHallFee Fee, int ReservationCouponId)
{
    public static WeddingHallTicket Read(ByteReader r) => new(r.ReadLong(), r.ReadInt(), r.ReadInt(), r.ReadLong(), r.ReadBool(), r.ReadLong(), r.ReadLong(),
        r.ReadUnicodeString(), r.ReadUnicodeString(), WeddingHallFee.Read(r), r.ReadInt());
}

/// <summary>
/// Inbound Wedding: u8 mode. 0 proxy, 6 propose accepted (proxy); 1 ticket + i32 count + count × (i64, i64) invited; 2 result u8; 3 propose
/// (i64 id, name); 0xD reservation request, 0xF reserve ok, 0x18 invitation (i64 id first) are tickets; 0x14 and 0x16 ticket + i64 cost;
/// 0x11 u16 reason; 0x19 i64 id; 0x1E u8 step result (3 adds a partner name); 0x1F u8; 0x27 u8 emotion result (2 adds i32, bool, bool, i32 if the
/// second bool); modes 8, 0x10, 0x13, 0x17, 0x1D, 0x23 have no body. Mode 0x1B (the guest finder answer, read through a generic found record
/// by the guest dialog) is kept raw. STILL-OPEN: the wedding UI and manager are not ported (apply = ReadNotApplied), mode 0x1B's layout is not
/// read, only modes 0, 1 and 0x17 are recorded on GMS2, the rest follow the client's handlers, feature 0x104 gate is not modelled.
/// </summary>
public sealed record WeddingPacket(byte Mode)
{
    public WeddingProxyState Proxy { get; init; }
    public WeddingHallTicket Ticket { get; init; }
    public IReadOnlyList<KeyValuePair<long, long>> Invited { get; init; } = Array.Empty<KeyValuePair<long, long>>();
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public byte Result { get; init; }
    public ushort Reason { get; init; }
    public long Cost { get; init; }
    public int EmotionId { get; init; }
    public bool EmotionFlagB2D { get; init; }
    public bool EmotionFlagB2C { get; init; }
    public int EmotionValue { get; init; }
    public byte[] RawBody { get; init; } = Array.Empty<byte>();

    public static WeddingPacket Read(ByteReader r)
    {
        byte mode = r.ReadByte();
        var o = new WeddingPacket(mode);
        switch (mode) {
            case 0x00: case 0x06: o = o with { Proxy = WeddingProxyState.Read(r) }; break;
            case 0x01: {
                var ticket = WeddingHallTicket.Read(r);
                int count = r.ReadInt();
                if (count < 0 || count > r.Available / 16) throw new InvalidDataException($"Wedding invited count {count} with {r.Available} bytes left");
                var list = new List<KeyValuePair<long, long>>(count);
                for (int i = 0; i < count; i++) list.Add(new KeyValuePair<long, long>(r.ReadLong(), r.ReadLong()));
                o = o with { Ticket = ticket, Invited = list };
                break;
            }
            case 0x02: case 0x1F: o = o with { Result = r.ReadByte() }; break;
            case 0x03: o = o with { Id = r.ReadLong(), Name = r.ReadUnicodeString() }; break;
            case 0x0D: case 0x0F: o = o with { Ticket = WeddingHallTicket.Read(r) }; break;
            case 0x11: o = o with { Reason = (ushort) r.ReadShort() }; break;
            case 0x14: case 0x16: o = o with { Ticket = WeddingHallTicket.Read(r), Cost = r.ReadLong() }; break;
            case 0x18: o = o with { Id = r.ReadLong(), Ticket = WeddingHallTicket.Read(r) }; break;
            case 0x19: o = o with { Id = r.ReadLong() }; break;
            case 0x1E: {
                byte result = r.ReadByte();
                o = o with { Result = result };
                if (result == 3) o = o with { Name = r.ReadUnicodeString() };
                break;
            }
            case 0x27: {
                byte result = r.ReadByte();
                o = o with { Result = result };
                if (result == 2) {
                    o = o with { EmotionId = r.ReadInt(), EmotionFlagB2D = r.ReadBool(), EmotionFlagB2C = r.ReadBool() };
                    if (o.EmotionFlagB2C) o = o with { EmotionValue = r.ReadInt() };
                }
                break;
            }
            case 0x08: case 0x10: case 0x13: case 0x17: case 0x1D: case 0x23: break;
            case 0x1B: return o with { RawBody = r.ReadBytes(r.Available) };
            default: return o;
        }
        r.ExpectEnd("Wedding " + mode);
        return o;
    }
}
