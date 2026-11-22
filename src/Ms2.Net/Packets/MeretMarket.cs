// Layouts from the restored client: CMeratMarketPacket::BuyPacket::Write / Read (meratmarketbasketinfo.cpp:2745, :2769),
// Unk_MeratProductKey (meratproductkey.h:22), CMeratMarketMan::SendMeretMarketMode30 (meratmarketman.cpp:1576, SendOp 0x39),
// CMeratMarketMan::OnBuySucceeded / OnBuyFailed (meratmarketman.cpp:2616, :2669; RecvOp 0x6C, sub-modes 0x1e / 0x1f).
using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// CMeratMarketPacket::BuyPacket: the request the client sends and the server echoes in the answer. u8 kind, i32 premium id
/// + i64 ugc id (the product key), i32 quantity, i32 option, i64 bookmark id, i32 index, i32 total, i32 shop id, u8 gift,
/// string receiver, string message, i64 price, bool star point, u8 pay type, i32 home sale a, b, remaining count, i32 random rate.
/// </summary>
public sealed class MeretMarketBuy {
    public int Kind;            // m_nKind: where the purchase starts (4 = the shop dialog)
    public int PremiumId;       // m_key.m_nPremiumId
    public long UgcId;          // m_key.m_nUgcId
    public int Quantity;        // m_n34 (the flag FUN_140d604c0 gets, 1)
    public int Option;          // m_n30: the selected option of the product
    public long BookmarkId;     // m_n38
    public int Index;           // m_n40: this row's place among the queued purchases
    public int Total;           // m_n44: how many are queued
    public int ShopId;          // m_n48
    public int Gift;            // m_n4c (u8)
    public string Receiver = "";    // m_str50
    public string Message = "";     // m_str58
    public long Price;          // m_n60
    public bool StarPoint;      // m_b68
    public int PayType;         // m_n6c (u8): 0x11 when unset
    public int HomeA, HomeB, HomeCount;   // m_n70, m_n74, m_n78 (type 6 products)
    public int RandomRate;      // m_n88 (type 7 / 8 products)

    /// <summary>BuyPacket::Write.</summary>
    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);

    /// <summary>GMS2 stops after the pay type: the four home sale / random rate ints are not sent.</summary>
    public void Write(ByteWriter w, ProtocolProfile profile) {
        w.WriteByte((byte)Kind);
        w.WriteInt(PremiumId); w.WriteLong(UgcId);
        w.WriteInt(Quantity); w.WriteInt(Option); w.WriteLong(BookmarkId);
        w.WriteInt(Index); w.WriteInt(Total); w.WriteInt(ShopId);
        w.WriteByte((byte)Gift);
        w.WriteUnicodeString(Receiver); w.WriteUnicodeString(Message);
        w.WriteLong(Price); w.WriteBool(StarPoint); w.WriteByte((byte)PayType);
        if (profile.IsGms2) return;
        w.WriteInt(HomeA); w.WriteInt(HomeB); w.WriteInt(HomeCount); w.WriteInt(RandomRate);
    }

    /// <summary>BuyPacket::Read.</summary>
    public static MeretMarketBuy Read(ByteReader r) => Read(r, ProtocolProfile.Kms2525);

    public static MeretMarketBuy Read(ByteReader r, ProtocolProfile profile) {
        var b = new MeretMarketBuy { Kind = r.ReadByte() };
        b.PremiumId = r.ReadInt(); b.UgcId = r.ReadLong();
        b.Quantity = r.ReadInt(); b.Option = r.ReadInt(); b.BookmarkId = r.ReadLong();
        b.Index = r.ReadInt(); b.Total = r.ReadInt(); b.ShopId = r.ReadInt();
        b.Gift = r.ReadByte();
        b.Receiver = r.ReadUnicodeString(); b.Message = r.ReadUnicodeString();
        b.Price = r.ReadLong(); b.StarPoint = r.ReadBool(); b.PayType = r.ReadByte();
        if (profile.IsGms2) return b;
        b.HomeA = r.ReadInt(); b.HomeB = r.ReadInt(); b.HomeCount = r.ReadInt(); b.RandomRate = r.ReadInt();
        return b;
    }
}

/// <summary>Outbound 0x39 mode 0x1e (SendMeretMarketMode30): u8 0x1e, then the <see cref="MeretMarketBuy"/>.</summary>
public sealed record MeretMarketBuyRequest(MeretMarketBuy Buy) : IProfileOutPacket {
    public Outbound Op => Outbound.MeretMarket;
    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);
    public void Write(ByteWriter w, ProtocolProfile profile) { w.WriteByte(0x1e); Buy.Write(w, profile); }
}

/// <summary>
/// Inbound 0x6C, the two buy answers: u8 mode, the echoed <see cref="MeretMarketBuy"/>, then mode 0x1e (bought): i32 buy count,
/// i32 sold count, i32 ugc 3c, i32 ugc 40; mode 0x1f (failed): i32 error. Other modes are not read here.
/// </summary>
public sealed class MeretMarketPacket {
    public byte Mode;
    public MeretMarketBuy Buy;
    public int BuyCount, SoldCount, Ugc3c, Ugc40;   // mode 0x1e
    public int Error;                                // mode 0x1f

    public bool Succeeded => Mode == 0x1e;

    /// <summary>Null for a mode other than 0x1e / 0x1f.</summary>
    public static MeretMarketPacket Read(ByteReader r) => Read(r, ProtocolProfile.Kms2525);

    public static MeretMarketPacket Read(ByteReader r, ProtocolProfile profile) {
        byte mode = r.ReadByte();
        if (mode != 0x1e && mode != 0x1f) return null;
        var p = new MeretMarketPacket { Mode = mode, Buy = MeretMarketBuy.Read(r, profile) };
        if (mode == 0x1e) { p.BuyCount = r.ReadInt(); p.SoldCount = r.ReadInt(); p.Ugc3c = r.ReadInt(); p.Ugc40 = r.ReadInt(); }
        else p.Error = r.ReadInt();
        return p;
    }

    static byte[] Bytes(ByteWriter w) => w.Buffer.AsSpan(0, w.Length).ToArray();

    public static byte[] WriteSucceeded(MeretMarketBuy buy, int buyCount, int soldCount, int ugc3c, int ugc40) {
        var w = new ByteWriter(); w.WriteByte(0x1e); buy.Write(w);
        w.WriteInt(buyCount); w.WriteInt(soldCount); w.WriteInt(ugc3c); w.WriteInt(ugc40);
        return Bytes(w);
    }

    public static byte[] WriteFailed(MeretMarketBuy buy, int error) {
        var w = new ByteWriter(); w.WriteByte(0x1f); buy.Write(w); w.WriteInt(error);
        return Bytes(w);
    }
}
