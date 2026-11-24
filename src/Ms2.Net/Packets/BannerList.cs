// Inbound 0xB3 (KMS) / 0xB5 (GMS2) BannerList: CAdBannerManagerCli::OnBannerList (@0x14096b3a0, adbannermanagercli.cpp:31) -> ReadBanners (@0x14016e060, adbannerdata.cpp:176).
using Maple2.PacketLib.Tools;

namespace Ms2.Net.Packets;

/// <summary>
/// One AdBannerData (AdBannerData::Read @0x14016dc60, adbannerdata.cpp:100): i32 type (the client's EAdBannerType: 1 cash, 2 merat,
/// 3 pcbang, 4 playgift, 5 title), five strings (type name, name, then the two the client keeps at +0x30 and +0x38, then +0x40), i32
/// slot (-1 for any), i64 begin and i64 end (unix seconds).
/// <para>STILL-OPEN: the client names only the type name and name; the three other strings are unnamed (the recordings carry empty
/// ones and an image address in the last).</para>
/// </summary>
public sealed record AdBannerData(int Type, string TypeName, string Name, string Str30, string Str38, string Str40, int Slot, long Begin, long End)
{
    public static AdBannerData Read(ByteReader r) =>
        new(r.ReadInt(), r.ReadUnicodeString(), r.ReadUnicodeString(), r.ReadUnicodeString(), r.ReadUnicodeString(), r.ReadUnicodeString(),
            r.ReadInt(), r.ReadLong(), r.ReadLong());
}

/// <summary>
/// Inbound BannerList (ReadBanners, adbannerdata.cpp:176): u16 count (there is no mode byte), then that many <see cref="AdBannerData"/>.
/// The client clears its banner list first (FUN_14016df70), so a packet replaces the whole list. The wire is the same on KMS and GMS2.
/// </summary>
public sealed record BannerList(IReadOnlyList<AdBannerData> Banners)
{
    /// <summary>The smallest record: i32 type, five empty strings (2 bytes each), i32 slot, two i64.</summary>
    const int MinRecord = 4 + 5 * 2 + 4 + 16;

    public static BannerList Read(ByteReader r)
    {
        int count = r.ReadShort() & 0xFFFF;
        if (count > r.Available / MinRecord) throw new InvalidDataException($"BannerList count {count}");
        var list = new List<AdBannerData>(count);
        for (int i = 0; i < count; i++) list.Add(AdBannerData.Read(r));
        r.ExpectEnd("BannerList");
        return new BannerList(list);
    }
}
