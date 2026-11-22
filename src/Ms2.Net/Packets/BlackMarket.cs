using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

// The black market (the player item market for mesos): RecvOp::BlackMarket 0x98, which networkservice.cpp:690 hands to
// CBlackMarketMan::OnBlackMarket (blackmarketman.cpp:115), and SendOp::BlackMarket 0x5F, which CBlackMarketMan's
// SendBlackMarketMode* build (blackmarketman.cpp:154-222). The layouts of one product and of the search condition are
// CBlackMarketProduct::Read and CBlackMarketSchCond::WriteStream (commonfiles/blackmarketitemoption.cpp:564, :747).

/// <summary>
/// One product, CBlackMarketProduct::Read (@0x1401bdf60, blackmarketitemoption.cpp:564): i64 the listing id (m_qw18, the
/// key of both lists), i64 the listing time (m_qw20; the register page shows it as "createtime", uiblackmarketpage.cpp:1717),
/// i64 m_qw28 (STILL-OPEN), i64 the end of the sale (m_nExpire), i32 count, i32 bought, i64 the unit price, u8 m_nStateB
/// (GetState(true) gives 5 while it is set; STILL-OPEN what sets it), i64 the item uid, i32 the item id, u8 the rarity, i64
/// the seller's account id (m_qw68: the search page refuses to buy when it equals the own account id, :983), then the item
/// body. The client reads the body only when FUN_140523c40 knows the item id (<see cref="ItemReader.Known"/>); for an
/// unknown id <see cref="Item"/> stays null and nothing more is read, as there.
/// </summary>
public sealed class BlackMarketProduct {
    public long Id, ListedTime, Value28, ExpireTime;
    public int Count, Bought;
    public long UnitPrice;
    public byte StateB;
    public long ItemUid; public int ItemId; public byte Rarity;
    public long SellerAccountId;
    public EquipItem Item;

    /// <summary>GetRemain (@0x1401be3b0): count - bought.</summary>
    public int Remain => Count - Bought;
    /// <summary>TotalPrice (@0x1401be2f0): count × unit price.</summary>
    public long TotalPrice => (long)Count * UnitPrice;
    /// <summary>PriceFor (@0x1401be310): n × unit price.</summary>
    public long PriceFor(int n) => (long)n * UnitPrice;

    public static BlackMarketProduct Read(ByteReader r) => Read(r, ProtocolProfile.Kms2525);

    public static BlackMarketProduct Read(ByteReader r, ProtocolProfile profile) {
        var p = new BlackMarketProduct {
            Id = r.ReadLong(), ListedTime = r.ReadLong(), Value28 = r.ReadLong(), ExpireTime = r.ReadLong(),
            Count = r.ReadInt(), Bought = r.ReadInt(), UnitPrice = r.ReadLong(), StateB = r.ReadByte(),
            ItemUid = r.ReadLong(), ItemId = r.ReadInt(), Rarity = r.ReadByte(), SellerAccountId = r.ReadLong(),
        };
        if (ItemReader.Known == null || ItemReader.Known(p.ItemId)) {
            p.Item = ItemReader.ReadItem(r, p.ItemId, p.Rarity, profile);
            p.Item.Uid = p.ItemUid;                 // SetItemUid
        }
        return p;
    }

    /// <summary>i32 n, then n products (modes 1 and 4).</summary>
    internal static List<BlackMarketProduct> ReadList(ByteReader r, ProtocolProfile profile) {
        int n = r.ReadInt();
        var list = new List<BlackMarketProduct>();
        for (int i = 0; i < n; i++) list.Add(Read(r, profile));
        return list;
    }
}

/// <summary>
/// The search condition, CBlackMarketSchCond (blackmarketitemoption.cpp), with Reset's defaults (:599) and WriteStream's
/// order (:747). The search page fills it from the movie's search data (uiblackmarketpage.cpp:875-939): the major and minor
/// category (FUN_1401bd000 / FUN_1401bd010 of the category), the level range, the job group (FUN_1402227e0), the rank, the
/// item name, the page, the sort (FUN_140f156d0 of the sort name and direction), the option search flag, the limit (1), the
/// enchant and socket ranges and up to three stat ranges. m_dw4c, m_dw50 and m_dw58 are never set by the page (STILL-OPEN).
/// </summary>
public sealed class BlackMarketSearch {
    public int MajorCategory, MinorCategory, MinLevel, MaxLevel, JobGroup, Rank;
    public int EnchantMin, EnchantMax = 15;
    public byte SocketMin, SocketMax = 3;
    public string Name = "";
    public int Page;
    public byte Sort, Value58;
    public int Value50, Value4c;
    public byte OptionSearch, Limit = 1;
    public readonly int[] StatMin = new int[3], StatMax = new int[3];

    public void Write(ByteWriter w) {
        w.WriteInt(MajorCategory); w.WriteInt(MinorCategory); w.WriteInt(MinLevel); w.WriteInt(MaxLevel);
        w.WriteInt(JobGroup); w.WriteInt(Rank); w.WriteInt(EnchantMin); w.WriteInt(EnchantMax);
        w.WriteByte(SocketMin); w.WriteByte(SocketMax);
        w.WriteUnicodeString(Name);
        w.WriteInt(Page);
        w.WriteByte(Sort); w.WriteByte(Value58);
        w.WriteInt(Value50); w.WriteInt(Value4c);
        w.WriteByte(OptionSearch); w.WriteByte(Limit);
        for (int i = 0; i < 3; i++) { w.WriteInt(StatMin[i]); w.WriteInt(StatMax[i]); }
    }

    /// <summary>
    /// Compact (@0x1401bf1d0): without the option search, true and nothing changes. With it, the stat ranges that have a min
    /// or a max move to the front in their order and the rest are zeroed; false (no search is sent) when none is used.
    /// </summary>
    public bool Compact() {
        if (OptionSearch == 0) return true;
        var used = Enumerable.Range(0, 3).Where(i => StatMin[i] != 0 || StatMax[i] != 0).Select(i => (StatMin[i], StatMax[i])).ToList();
        if (used.Count == 0) return false;
        for (int k = 0; k < 3; k++) (StatMin[k], StatMax[k]) = k < used.Count ? used[k] : (0, 0);
        return true;
    }
}

/// <summary>
/// BlackMarket (0x98), CBlackMarketMan::OnBlackMarket (@0x140a20650, blackmarketman.cpp:115): u8 mode, then per mode. Modes
/// the client ignores read as <see cref="Other"/>.
/// </summary>
public abstract record BlackMarketMessage {
    /// <summary>
    /// Mode 0, FUN_140a21ca0 (:423): u8 type, i32 result, i64 value, i32 an id the client looks up in a string table
    /// (FUN_141811980(1, id), results 43 and 44), i32 a number it keeps as a u16. The manager shows the notice by result,
    /// then the search page's OnItemSearchNotice(type, result, value).
    /// </summary>
    public sealed record Error(byte Type, int Result, long Value, int TableId, ushort Number) : BlackMarketMessage;
    /// <summary>Mode 1, OnOwnProductList (@0x140a211a0, :312): i32 n × product, the whole own list.</summary>
    public sealed record OwnList(IReadOnlyList<BlackMarketProduct> Products) : BlackMarketMessage;
    /// <summary>Mode 2, OnOwnProductAdded (@0x140a21340, :329): one product added to the own list.</summary>
    public sealed record OwnAdded(BlackMarketProduct Product) : BlackMarketMessage;
    /// <summary>
    /// Mode 3, OnOwnProductRemoved (@0x140a21460, :342): i64 listing id, u8 reason (the register page's FUN_140f13030,
    /// uiblackmarketpage.cpp:1401: 0 shows the string 0x944, else s_msg_blackmarket_complete_remove_already).
    /// </summary>
    public sealed record OwnRemoved(long Id, byte Reason) : BlackMarketMessage;
    /// <summary>Mode 4, OnSearchResult (@0x140a216b0, :360): i32 n × product, one page of the search.</summary>
    public sealed record SearchResult(IReadOnlyList<BlackMarketProduct> Products) : BlackMarketMessage;
    /// <summary>Mode 5, FUN_140a21930 (:387): i64 listing id, i32 the count bought.</summary>
    public sealed record Bought(long Id, int Count) : BlackMarketMessage;
    /// <summary>
    /// Mode 6 (:117): u8 flag (m_nF8), i64 two times (m_tmA, m_tmB, kept as CMsTime). STILL-OPEN: nothing in the client reads
    /// the three back in the black market sources.
    /// </summary>
    public sealed record Status(byte Flag, long TimeA, long TimeB) : BlackMarketMessage;
    /// <summary>Mode 7 (:137): an own product sold: the sound BlackMarket_Sell, the chat line 0x925 and the own list asked again (mode 1).</summary>
    public sealed record Sold : BlackMarketMessage;
    /// <summary>
    /// Mode 8, FUN_140a21be0 (:412): i32 item id, i32 rarity, i64 the reference price; the register page's FUN_140f137a0
    /// (uiblackmarketpage.cpp:1468) shows it when it is for the selected item.
    /// </summary>
    public sealed record ReferencePrice(int ItemId, int Rarity, long Price) : BlackMarketMessage;
    /// <summary>Mode 9 (:143): open the black market dialog (0x4e97) unless it is open.</summary>
    public sealed record Open : BlackMarketMessage;
    public sealed record Other : BlackMarketMessage;

    public byte Mode { get; init; }

    public static BlackMarketMessage Read(ByteReader r) => Read(r, ProtocolProfile.Kms2525);

    public static BlackMarketMessage Read(ByteReader r, ProtocolProfile profile) {
        byte mode = r.ReadByte();
        BlackMarketMessage m = mode switch {
            0 => new Error(r.ReadByte(), r.ReadInt(), r.ReadLong(), r.ReadInt(), (ushort)r.ReadInt()),
            1 => new OwnList(BlackMarketProduct.ReadList(r, profile)),
            2 => new OwnAdded(BlackMarketProduct.Read(r, profile)),
            3 => new OwnRemoved(r.ReadLong(), r.ReadByte()),
            4 => new SearchResult(BlackMarketProduct.ReadList(r, profile)),
            5 => new Bought(r.ReadLong(), r.ReadInt()),
            6 => new Status(r.ReadByte(), r.ReadLong(), r.ReadLong()),
            7 => new Sold(),
            8 => new ReferencePrice(r.ReadInt(), r.ReadInt(), r.ReadLong()),
            9 => new Open(),
            _ => new Other(),
        };
        return m with { Mode = mode };
    }
}

/// <summary>SendBlackMarketMode1 (@0x140a20940, blackmarketman.cpp:155): ask for the own listings (u8 1). The dialog sends it
/// when it opens (uiblackmarketdialog.cpp:126) and the manager after a mode 7 (sold).</summary>
public sealed record BlackMarketOwnListRequest : IOutPacket {
    public Outbound Op => Outbound.BlackMarket;
    public void Write(ByteWriter w) => w.WriteByte(1);
}

/// <summary>SendBlackMarketMode6 (@0x140a209a0, :163): u8 6 alone. Nothing in the client calls it (STILL-OPEN what it asks).</summary>
public sealed record BlackMarketMode6Request : IOutPacket {
    public Outbound Op => Outbound.BlackMarket;
    public void Write(ByteWriter w) => w.WriteByte(6);
}

/// <summary>SendBlackMarketMode2 (@0x140a20a00, :171): register a listing (u8 2, i64 item uid, i64 price, i32 count), from
/// BlackMarketPageItemRegister::SendRegisterRequest (@0x140f13d80, uiblackmarketpage.cpp:1541). An item and money send.</summary>
public sealed record BlackMarketRegisterRequest(long ItemUid, long Price, int Count) : IOutPacket {
    public Outbound Op => Outbound.BlackMarket;
    public void Write(ByteWriter w) { w.WriteByte(2); w.WriteLong(ItemUid); w.WriteLong(Price); w.WriteInt(Count); }
}

/// <summary>SendBlackMarketMode3 (@0x140a20aa0, :182): cancel an own listing (u8 3, i64 listing id), from the register page's
/// confirm box answer FUN_140f13950 (uiblackmarketpage.cpp:1483). An item send.</summary>
public sealed record BlackMarketCancelRequest(long Id) : IOutPacket {
    public Outbound Op => Outbound.BlackMarket;
    public void Write(ByteWriter w) { w.WriteByte(3); w.WriteLong(Id); }
}

/// <summary>SendBlackMarketMode4 (@0x140a20b10, :191): search (u8 4, the condition). The client sends nothing when
/// <see cref="BlackMarketSearch.Compact"/> fails; the caller compacts first (<see cref="TryCreate"/>). From the search page
/// (uiblackmarketpage.cpp:535).</summary>
public sealed record BlackMarketSearchRequest(BlackMarketSearch Condition) : IOutPacket {
    public Outbound Op => Outbound.BlackMarket;
    public void Write(ByteWriter w) { w.WriteByte(4); Condition.Write(w); }

    /// <summary>The send's gate: null when the condition does not compact.</summary>
    public static BlackMarketSearchRequest TryCreate(BlackMarketSearch c) => c != null && c.Compact() ? new(c) : null;
}

/// <summary>SendBlackMarketMode5 (@0x140a20bf0, :206): buy (u8 5, i64 listing id, i32 count), from
/// BlackMarketPageItemSearch::BuyProduct (@0x140f107a0, uiblackmarketpage.cpp:1008). A money send.</summary>
public sealed record BlackMarketBuyRequest(long Id, int Count) : IOutPacket {
    public Outbound Op => Outbound.BlackMarket;
    public void Write(ByteWriter w) { w.WriteByte(5); w.WriteLong(Id); w.WriteInt(Count); }
}

/// <summary>SendBlackMarketMode8 (@0x140a20c70, :216): ask the reference price of an item (u8 8, i32 item id, i32 rarity),
/// from BlackMarketPageItemRegister::SelectItem (@0x140f13ce0, uiblackmarketpage.cpp:1530); answered by mode 8.</summary>
public sealed record BlackMarketReferencePriceRequest(int ItemId, int Rarity) : IOutPacket {
    public Outbound Op => Outbound.BlackMarket;
    public void Write(ByteWriter w) { w.WriteByte(8); w.WriteInt(ItemId); w.WriteInt(Rarity); }
}
