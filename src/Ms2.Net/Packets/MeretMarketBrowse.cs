// Layouts from the restored client: CMeratMarketMan::OnMeretMarket dispatch (meratmarketman.cpp:338) and its readers, CMeratMarketProduct::Read
// (meratmarketbasketinfo.cpp:2083), CDBPremiumProduct::Read (:523), CMeratMarketPremiumProduct::Read (:1127), CUgcMarketProduct::Read (ugcdef.cpp:2058),
// CMeratMarketSchCond::Write (meratmarketbasketinfo.cpp:1760). Sub-mode numbers are the same on GMS2 (checked against the recordings).
using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>Unk_MeratProductKey: i32 premium id, i64 ugc id.</summary>
public readonly record struct MeratKey(int PremiumId, long UgcId) {
    public static MeratKey Read(ByteReader r) => new(r.ReadInt(), r.ReadLong());
}

/// <summary>CDBPremiumProduct::Read plus the CMeratMarketPremiumProduct tail (buy count, sub / limit products, sold counts).</summary>
public sealed class MeratPremiumProduct {
    public int Id, Type, ChartOrder, ItemId, CategoryId, BuyCount, SoldCount, HomeSoldCount;
    public string Name = "", Category = "", Icon = "";
    public byte Currency, Grade, SaleTag, BuyLimit;
    public long Price, SalePrice, StartDate, EndDate;
    public int BuyLimitMax;
    public readonly List<MeratPremiumProduct> Subs = new();

    public static MeratPremiumProduct ReadBase(ByteReader r, bool gms2) {
        var p = new MeratPremiumProduct { Id = r.ReadInt() };
        r.ReadByte();
        p.Name = r.ReadUnicodeString();
        p.Type = r.ReadByte();
        r.ReadInt();
        p.ChartOrder = r.ReadInt();
        r.ReadInt();                                    // coupon flags
        r.ReadByte();                                   // visible
        p.SaleTag = r.ReadByte();
        p.Currency = r.ReadByte();
        p.Price = r.ReadLong(); p.SalePrice = r.ReadLong();
        r.ReadByte();
        p.StartDate = r.ReadLong(); p.EndDate = r.ReadLong();
        r.ReadInt(); r.ReadInt();
        p.BuyLimit = r.ReadByte(); p.BuyLimitMax = r.ReadInt();
        r.ReadByte(); r.ReadShort(); r.ReadShort(); r.ReadInt();
        p.ItemId = r.ReadInt(); p.Grade = r.ReadByte();
        r.ReadInt(); r.ReadInt(); r.ReadInt();
        p.CategoryId = r.ReadInt();
        r.ReadInt(); r.ReadByte(); r.ReadInt();
        p.Category = r.ReadString(); p.Icon = r.ReadString();
        r.ReadBool(); r.ReadByte(); r.ReadInt(); r.ReadBool(); r.ReadInt(); r.ReadInt(); r.ReadInt(); r.ReadByte();
        r.ReadBool(); r.ReadInt();                      // point flag, point
        if (r.ReadBool()) { r.ReadString(); r.ReadLong(); r.ReadLong(); }   // event category + period
        r.ReadByte();
        if (!gms2) r.ReadUnicodeString();            // GMS2 has no string here (every recorded product: 6 bytes where KMS has 8 before the sub count)
        r.ReadBool();
        if (p.Type == 6) { r.ReadByte(); r.ReadByte(); r.ReadInt(); r.ReadLong(); r.ReadLong(); r.ReadLong(); r.ReadInt(); }
        if (p.Type == 7 || p.Type == 8) { r.ReadInt(); r.ReadInt(); r.ReadInt(); r.ReadLong(); }
        return p;
    }

    public static MeratPremiumProduct Read(ByteReader r, bool gms2) {
        var p = ReadBase(r, gms2);
        p.BuyCount = r.ReadInt();
        if (p.Type == 1) p.ReadSubs(r, gms2);
        else if (p.Type == 3) { p.SoldCount = r.ReadInt(); p.ReadSubs(r, gms2); }
        else if (p.Type == 6) p.HomeSoldCount = r.ReadInt();
        return p;
    }

    void ReadSubs(ByteReader r, bool gms2) {
        int n = r.ReadByte();
        for (int i = 0; i < n; i++) if (r.ReadByte() == 1) Subs.Add(ReadBase(r, gms2));
    }
}

/// <summary>CUgcMarketProduct::Read: a user-designed product (kind 1).</summary>
public sealed class MeratUgcProduct {
    public int ItemId, CategoryId, SellKind, Type;
    public long Uid, Price, SaleEnd;
    public byte State;
    public string Title = "", Tags = "", Seller = "";

    public static MeratUgcProduct Read(ByteReader r) {
        var p = new MeratUgcProduct();
        r.ReadInt(); p.Uid = r.ReadLong(); p.State = r.ReadByte(); p.ItemId = r.ReadInt(); p.CategoryId = r.ReadInt();
        r.ReadByte(); r.ReadInt(); p.Price = r.ReadLong();
        r.ReadInt(); r.ReadInt(); r.ReadInt(); r.ReadInt();
        r.ReadLong(); r.ReadLong(); p.SaleEnd = r.ReadLong(); p.SellKind = r.ReadInt();
        r.ReadLong(); r.ReadLong(); r.ReadLong();
        ReadUser(r, out p.Seller);
        p.Title = r.ReadUnicodeString(); p.Tags = r.ReadUnicodeString(); r.ReadUnicodeString();
        // CUgcItemLook
        r.ReadLong(); r.ReadUnicodeString(); r.ReadUnicodeString(); r.ReadByte(); r.ReadInt(); r.ReadLong(); r.ReadLong();
        r.ReadUnicodeString(); r.ReadLong(); r.ReadUnicodeString(); r.ReadByte();
        // CUgcItemMusicNote
        r.ReadLong(); r.ReadInt(); r.ReadInt(); r.ReadUnicodeString(); r.ReadUnicodeString(); r.ReadString();
        r.ReadInt(); r.ReadLong(); r.ReadLong(); r.ReadUnicodeString();
        // CBlueprintItemData
        r.ReadLong(); r.ReadInt(); r.ReadInt(); r.ReadInt(); r.ReadLong(); r.ReadInt(); r.ReadLong(); r.ReadLong(); r.ReadUnicodeString();
        p.Type = r.ReadByte();
        return p;
    }

    /// <summary>CUgcUserProxy::Read: i32 type, i64 account, i64 character, wstr object name, wstr name.</summary>
    internal static void ReadUser(ByteReader r, out string name) {
        r.ReadInt(); r.ReadLong(); r.ReadLong(); r.ReadUnicodeString(); name = r.ReadUnicodeString();
    }
}

/// <summary>CMeratMarketProduct::Read: u8 kind (0 premium, else ugc), the key, then the product.</summary>
public sealed class MeratProduct {
    public int Kind;
    public MeratKey Key;
    public MeratPremiumProduct Premium;
    public MeratUgcProduct Ugc;

    public static MeratProduct Read(ByteReader r, bool gms2) {
        var p = new MeratProduct { Kind = r.ReadByte() };
        p.Key = MeratKey.Read(r);
        if (p.Kind == 0) p.Premium = MeratPremiumProduct.Read(r, gms2); else p.Ugc = MeratUgcProduct.Read(r);
        return p;
    }

    /// <summary>u8 exists, then the product when it is set (the list entries of every page).</summary>
    public static MeratProduct ReadOptional(ByteReader r, bool gms2) => r.ReadByte() != 0 ? Read(r, gms2) : null;
}

/// <summary>CMeratMarketSchCond::Write, the search request of Outbound mode 0x1b.</summary>
public sealed record MeratSearchCond(int A, byte Kind, int Mask, byte B, string Text, int Page, int PageCopy, byte C, byte D, byte E) {
    public static MeratSearchCond Read(ByteReader r) =>
        new(r.ReadInt(), r.ReadByte(), r.ReadInt(), r.ReadByte(), r.ReadUnicodeString(), r.ReadInt(), r.ReadInt(), r.ReadByte(), r.ReadByte(), r.ReadByte());

    public void Write(ByteWriter w) {
        w.WriteInt(A); w.WriteByte(Kind); w.WriteInt(Mask); w.WriteByte(B); w.WriteUnicodeString(Text);
        w.WriteInt(Page); w.WriteInt(PageCopy); w.WriteByte(C); w.WriteByte(D); w.WriteByte(E);
    }
}

/// <summary>The browse answers of Inbound MeretMarket (GMS2 0x6E): one case per sub-mode the client's dispatcher handles and a recording covers.</summary>
public sealed class MeretMarketBrowse {
    public byte Mode;
    public List<MeratProduct> Products = new();
    public int Page, Value, Total, Count;
    public long Time;
    public MeratKey Key;
    public byte State, SaleKind;
    public int[] Settings;
    /// <summary>The unread mode of a reply this reader does not know (STILL-OPEN: no client handler / no recording).</summary>
    public bool Known = true;

    public static MeretMarketBrowse Read(ByteReader r, ProtocolProfile profile) {
        bool gms = profile.IsGms2;
        var m = new MeretMarketBrowse { Mode = r.ReadByte() };
        switch (m.Mode) {
            case 0x65 when !gms: {                                    // OnMainPageProducts
                m.Page = r.ReadByte(); m.Value = r.ReadByte();
                if (m.Page == 0) {
                    for (int g = 0; g < 3; g++) { int n = r.ReadByte(); for (int i = 0; i < n; i++) m.Products.Add(MeratProduct.Read(r, gms)); }
                    r.ReadByte();
                } else r.ReadByte();
                if (m.Page <= 2) { int n = r.ReadByte(); for (int i = 0; i < n; i++) Add(m, MeratProduct.ReadOptional(r, gms)); }
                break;
            }
            case 0x66: { int n = r.ReadInt(); m.Count = n; for (int i = 0; i < n; i++) Add(m, MeratProduct.ReadOptional(r, gms)); break; }   // OnUgcProductsPacket
            case 0x67: { int n = r.ReadByte(); m.Count = n; for (int i = 0; i < n; i++) Add(m, MeratProduct.ReadOptional(r, gms)); break; }   // OnAdProducts
            case 0x0b: { m.Time = r.ReadLong(); int n = r.ReadInt(); m.Count = n; for (int i = 0; i < n; i++) Add(m, MeratProduct.ReadOptional(r, gms)); break; }   // OnMySaleProducts
            case 0x0c: {                                    // OnSellerList: i32 count, each CUgcMarketSeller
                int n = r.ReadInt(); m.Count = n;
                for (int i = 0; i < n; i++) {
                    r.ReadLong(); r.ReadLong(); r.ReadUnicodeString(); r.ReadInt(); MeratUgcProduct.ReadUser(r, out _);
                    r.ReadInt(); r.ReadLong(); r.ReadLong(); r.ReadLong();
                }
                break;
            }
            case 0x0d: m.Products.Add(MeratProduct.Read(r, gms)); break;   // OnProductRegistered
            case 0x15: m.Key = MeratKey.Read(r); r.ReadLong(); m.State = r.ReadByte(); m.SaleKind = r.ReadByte(); m.Time = r.ReadLong(); break;   // OnProductSaleStateChanged
            case 0x16: {                                    // OnBookMarkList: u8 count, each i64, i64, wstr name, wstr url
                int n = r.ReadByte(); m.Count = n;
                for (int i = 0; i < n; i++) { r.ReadLong(); r.ReadLong(); r.ReadUnicodeString(); r.ReadUnicodeString(); }
                break;
            }
            case 0x1b: {                                    // OnSearchResultPage
                m.Count = r.ReadInt(); m.Total = r.ReadInt(); m.Page = r.ReadInt();
                for (int i = 0; i < m.Count; i++) Add(m, MeratProduct.ReadOptional(r, gms));
                break;
            }
            case 0x6b: m.Settings = new[] { r.ReadInt(), r.ReadInt(), r.ReadInt(), r.ReadInt() }; break;   // OnSettleSettings
            default: m.Known = false; break;
        }
        return m;
    }

    static void Add(MeretMarketBrowse m, MeratProduct p) { if (p != null) m.Products.Add(p); }
}

/// <summary>C>S MeretMarket mode 13, register a UGC product: the client's UgcAddProduct write (product id, then id, enabled, name, description, price).</summary>
public sealed record MeratAddProduct(long Product, long Id, bool Enabled, string Name, string Description, long Price) {
    public const byte Mode = 0x0d;

    public static MeratAddProduct Read(ByteReader r) =>
        new(r.ReadLong(), r.ReadLong(), r.ReadByte() == 1, r.ReadUnicodeString(), r.ReadUnicodeString(), r.ReadLong());

    public void Write(ByteWriter w) {
        w.WriteLong(Product); w.WriteLong(Id); w.WriteByte((byte)(Enabled ? 1 : 0));
        w.WriteUnicodeString(Name); w.WriteUnicodeString(Description); w.WriteLong(Price);
    }
}
