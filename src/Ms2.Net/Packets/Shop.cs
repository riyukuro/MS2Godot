// Layout from the restored client's UIShopDialog::OnShopPacket (uishopdialog.cpp; the handler of RecvOp 0x51 its ctor registers),
// ReadShopInfo (0x141af88d0), ReadProducts (0x141af90f0), ReadProductUpdate (0x141af98d0), ReadResetResult (0x141af9ad0),
// ReadRepurchaseStart/Add/Removal (0x141af9ce0..0x141afa130), ReadPurchaseResult (0x141afa2f0), ReadNewStart/Products
// (0x141af9390..0x141af94e0), ReadShopError (0x141afa640), CNpcShopProductCli::ReadPacket modes 1 and 7
// (npcshopproductbase.cpp:139), CNpcShopItemNew::Read (npcshopdata.cpp:821), and the SendOp::Shop (0x28) writers
// (uishopdialog.cpp:750, 762, 2270, 2550, 2826, 2846; itemugcmesh.cpp:1097).
using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>The shop header (ReadShopInfo), in packet order; the names follow UIShopDialog's members.</summary>
public sealed record ShopInfo {
    public int NpcId, ShopId;
    /// <summary>m_tReset: the server time of the next list reset.</summary>
    public long ResetTime;
    /// <summary>m_280: the reset interval in seconds (0: the client constant ShopSeedResetTime).</summary>
    public int ResetInterval;
    /// <summary>m_2c4: how many products the list has; the list shows once that many arrived.</summary>
    public int ProductCount;
    /// <summary>m_284: the shop id the category tabs are built for (SetTab group 0).</summary>
    public int TabShopId;
    /// <summary>Opens the inventory (UI 0x4ece) beside the shop.</summary>
    public bool OpenInventory;
    public bool SellOnly, ResetEnable;
    /// <summary>m_295: the products are listed in a random order.</summary>
    public bool Shuffle;
    /// <summary>m_nShopType: 1 npc (talk image), 2 star, 3 PSSB, 4 gacha, else plain.</summary>
    public int ShopType;
    /// <summary>m_290: the "usable items only" box starts ticked.</summary>
    public bool CheckEnableItems;
    /// <summary>m_291: the tooltip's compare rows also for a shop item (vfunc_49: the 0x4800 flags).</summary>
    public bool ComparesItems;
    public bool ProbInfo;
    /// <summary>m_2c2: the shop has the new-items tab.</summary>
    public bool HasNewTab;
    public string Icon = "";
    /// <summary>The reset block, read only with <see cref="ResetEnable"/>: m_29c and m_2a4, the reset price's currency type
    /// and item (FUN_141349f30's price text); m_2a0, the currency of the reset count tooltip (GetCurrencyType);
    /// m_nResetPriceOrg.</summary>
    public int ResetCurrency, ResetTooltipCurrency, ResetCurrencyItem, ResetPrice;
    public bool ResetLimited;
    public int ResetCount, ResetType;
    /// <summary>m_bHideReset: true without the reset block.</summary>
    public bool HideReset = true;
    public bool ResetByAccount;

    public static ShopInfo Read(ByteReader r) {
        var s = new ShopInfo {
            NpcId = r.ReadInt(), ShopId = r.ReadInt(), ResetTime = r.ReadLong(), ResetInterval = r.ReadInt(),
            ProductCount = (ushort) r.ReadShort(), TabShopId = r.ReadInt(), OpenInventory = r.ReadByte() != 0,
            SellOnly = r.ReadBool(), ResetEnable = r.ReadBool(), Shuffle = r.ReadBool(), ShopType = r.ReadByte(),
            CheckEnableItems = r.ReadBool(),
        };
        s.ComparesItems = r.ReadBool(); s.ProbInfo = r.ReadBool(); s.HasNewTab = r.ReadBool(); s.Icon = r.ReadString();
        s.HideReset = !s.ResetEnable;
        if (s.ResetEnable) {
            s.ResetCurrency = r.ReadByte(); s.ResetTooltipCurrency = r.ReadByte(); s.ResetCurrencyItem = r.ReadInt(); s.ResetPrice = r.ReadInt();
            s.ResetLimited = r.ReadBool(); s.ResetCount = r.ReadInt(); s.ResetType = r.ReadByte();
            s.HideReset = r.ReadBool(); s.ResetByAccount = r.ReadBool();
        }
        return s;
    }

    public void Write(ByteWriter w) {
        w.WriteInt(NpcId); w.WriteInt(ShopId); w.WriteLong(ResetTime); w.WriteInt(ResetInterval); w.WriteShort((short) ProductCount);
        w.WriteInt(TabShopId); w.WriteByte((byte) (OpenInventory ? 1 : 0)); w.WriteBool(SellOnly); w.WriteBool(ResetEnable);
        w.WriteBool(Shuffle); w.WriteByte((byte) ShopType); w.WriteBool(CheckEnableItems); w.WriteBool(ComparesItems); w.WriteBool(ProbInfo);
        w.WriteBool(HasNewTab); w.WriteString(Icon);
        if (ResetEnable) {
            w.WriteByte((byte) ResetCurrency); w.WriteByte((byte) ResetTooltipCurrency); w.WriteInt(ResetCurrencyItem); w.WriteInt(ResetPrice);
            w.WriteBool(ResetLimited); w.WriteInt(ResetCount); w.WriteByte((byte) ResetType); w.WriteBool(HideReset); w.WriteBool(ResetByAccount);
        }
    }
}

/// <summary>CNpcShopBuyPeriod::Read (npcshopdata.cpp:459): the start and end times, then the part times of day and the days.</summary>
public sealed record ShopBuyPeriod {
    public bool HasTime;
    public long Start, End;
    public bool HasPart;
    /// <summary>(start, end) seconds of the day.</summary>
    public List<(int Start, int End)> PartTimes = new();
    public List<int> Days = new();

    public static ShopBuyPeriod Read(ByteReader r) {
        var period = new ShopBuyPeriod();
        if (period.HasTime = r.ReadBool()) { period.Start = r.ReadLong(); period.End = r.ReadLong(); }
        if (period.HasPart = r.ReadBool()) {
            int n = r.ReadByte();
            for (int i = 0; i < n; i++) period.PartTimes.Add((r.ReadInt(), r.ReadInt()));
            int days = r.ReadByte();
            for (int i = 0; i < days; i++) period.Days.Add(r.ReadByte());
        }
        return period;
    }

    public void Write(ByteWriter w) {
        w.WriteBool(HasTime);
        if (HasTime) { w.WriteLong(Start); w.WriteLong(End); }
        w.WriteBool(HasPart);
        if (HasPart) {
            w.WriteByte((byte) PartTimes.Count);
            foreach (var (s, e) in PartTimes) { w.WriteInt(s); w.WriteInt(e); }
            w.WriteByte((byte) Days.Count);
            foreach (var d in Days) w.WriteByte((byte) d);
        }
    }
}

/// <summary>NpcShopItemLife::Read (commonstruct_wa.cpp:508): u8 type; types 1, 3, 4 an i32 value, type 2 an i64 time.</summary>
public readonly record struct ShopItemLife(int Type, int Value, long Time);

/// <summary>
/// One product, CNpcShopProductCli::ReadPacket mode 1 or mode 7 (repurchase), in packet order (the client's member in
/// each comment). Mode 1 reads CItem::FUN_140523c40(item id, rarity, count) and CItem::ReadFromStream, then SetCount(bundle).
/// Mode 7 (npcshopproductbase.cpp:191-202) reads serial, itemId, rarity, i64 price, and the item stream.
/// </summary>
public sealed record ShopProduct {
    public int Serial;            // m_18
    public int ItemId;
    public int Currency;          // m_88: the currency type (1 an item: GetMaxAffordableCount; 7 skips the usable check:
                                  // PopulateProduct; 0xd star points: CheckStarPointEvent)
    public int CurrencyItemId;    // m_8c
    public int CurrencyItemTag;   // m_90: paid with any item of this tag (GetMaxAffordableCount counts by tag)
    public long Price;            // m_a0: the sale price (u32 in mode 1, i64 in mode 7)
    public long OriginalPrice;    // m_a8
    public int Rarity;
    public int Count;
    public int Limit;             // m_5c: how many one may buy (0: no limit)
    public int Bought;            // SetBuyItemCount
    public int GuildTrophy;       // m_60
    public string Categories = "";  // m_b0, split at ','
    public int Achieve, AchieveGrade;   // m_64, m_68
    public int ChampionshipRank;  // m_6c (u8)
    public int ChampionshipScore; // m_70 (u16)
    public int GuildNpc;          // m_74 (u8)
    public int GuildNpcLevel;     // m_78 (u16)
    public bool PremiumSlot;      // m_84 (FUN_140db6fd0)
    public int Bundle = 1;        // m_b8: items per purchase
    public int Label;             // m_bc (vfunc_1): the row background (SetProductBackground)
    public int SaleTag;           // m_c0 (vfunc_2)
    public string IconTag = "";   // m_98: the price text's icon tag
    public int Alliance;          // m_7c (u16)
    public int FameGrade;         // m_80
    public bool MainItem;         // m_c4
    public ShopBuyPeriod Period;  // m_c8
    public ShopItemLife? Life;    // m_d8
    public EquipItem Item;

    /// <summary>m_58 after SetBuyItemCount: the purchases made (bought items over the bundle size).</summary>
    public int BuyCount => Bundle > 1 ? Bought / Bundle : Bought;
    /// <summary>m_54 (UpdateRemainingCount): max(limit - purchases, 0).</summary>
    public int Remaining => Math.Max(0, Limit - BuyCount);
    /// <summary>GetRemainingItemCount (npcshopproductbase.cpp:236): remaining purchases times bundle when bundle &gt; 1.</summary>
    public int RemainingItemCount => Bundle > 1 ? Remaining * Bundle : Remaining;
    /// <summary>IsSoldOut: a limit and none left.</summary>
    public bool SoldOut => Limit > 0 && Remaining <= 0;

    public static ShopProduct Read(ByteReader r) => Read(r, ProtocolProfile.Kms2525);

    /// <summary>The GMS2 list entry (upstream ShopItem.WriteTo, read from the recordings): the same fields in a different order and width, a restricted-buy
    /// block that always carries both times and a u16 count of day ranges, no life block, and the item body of the GMS2 profile.</summary>
    public static ShopProduct Read(ByteReader r, ProtocolProfile profile) {
        if (profile.IsGms2) return ReadGms2(r, profile);
        var p = new ShopProduct();
        p.Serial = r.ReadInt(); p.ItemId = r.ReadInt(); p.Currency = r.ReadByte(); p.CurrencyItemId = r.ReadInt(); p.CurrencyItemTag = r.ReadInt();
        p.Price = (uint) r.ReadInt(); p.OriginalPrice = (uint) r.ReadInt(); p.Rarity = r.ReadByte(); p.Count = r.ReadInt(); p.Limit = r.ReadInt();
        p.Bought = r.ReadInt(); p.GuildTrophy = r.ReadInt(); p.Categories = r.ReadString(); p.Achieve = r.ReadInt(); p.AchieveGrade = r.ReadInt();
        p.ChampionshipRank = r.ReadByte(); p.ChampionshipScore = (ushort) r.ReadShort(); p.GuildNpc = r.ReadByte(); p.GuildNpcLevel = (ushort) r.ReadShort();
        p.PremiumSlot = r.ReadBool(); p.Bundle = (ushort) r.ReadShort(); p.Label = r.ReadByte(); p.SaleTag = r.ReadByte();
        p.IconTag = r.ReadString(); p.Alliance = (ushort) r.ReadShort(); p.FameGrade = r.ReadInt();
        p.MainItem = r.ReadBool();
        if (r.ReadBool()) p.Period = ShopBuyPeriod.Read(r);
        if (r.ReadBool()) {
            int type = r.ReadByte();
            p.Life = type switch {
                1 or 3 or 4 => new ShopItemLife(type, r.ReadInt(), 0),
                2 => new ShopItemLife(type, 0, r.ReadLong()),
                _ => new ShopItemLife(type, 0, 0),
            };
        }
        p.Item = ItemReader.ReadItem(r, p.ItemId, p.Rarity);
        return p;
    }

    static ShopProduct ReadGms2(ByteReader r, ProtocolProfile profile) {
        var p = new ShopProduct();
        p.Serial = r.ReadInt(); p.ItemId = r.ReadInt();
        p.Currency = r.ReadByte(); p.CurrencyItemId = r.ReadInt(); p.CurrencyItemTag = r.ReadInt(); p.Price = (uint) r.ReadInt(); p.OriginalPrice = (uint) r.ReadInt();
        p.Rarity = r.ReadByte();
        r.ReadInt();                                                   // the constant 500
        p.Limit = r.ReadInt(); p.Bought = r.ReadInt(); p.GuildTrophy = r.ReadInt(); p.Categories = r.ReadString();
        p.Achieve = r.ReadInt(); p.AchieveGrade = r.ReadInt();
        p.ChampionshipRank = r.ReadByte(); p.ChampionshipScore = (ushort) r.ReadShort(); p.GuildNpc = r.ReadByte(); p.GuildNpcLevel = (ushort) r.ReadShort();
        p.PremiumSlot = r.ReadBool(); p.Bundle = (ushort) r.ReadShort(); p.SaleTag = r.ReadByte(); p.Label = r.ReadByte();
        p.IconTag = r.ReadString(); p.Alliance = (ushort) r.ReadShort(); p.FameGrade = r.ReadInt();
        p.MainItem = r.ReadBool();
        if (r.ReadBool()) {                                            // RestrictedBuyData: the times are always written
            var period = new ShopBuyPeriod { HasTime = r.ReadBool() };
            period.Start = r.ReadLong(); period.End = r.ReadLong();
            int ranges = (ushort) r.ReadShort();
            for (int i = 0; i < ranges; i++) period.PartTimes.Add((r.ReadInt(), r.ReadInt()));
            period.HasPart = ranges > 0;
            int days = r.ReadByte();
            for (int i = 0; i < days; i++) period.Days.Add(r.ReadByte());
            p.Period = period;
        }
        p.Item = new EquipItem { Id = p.ItemId, Rarity = p.Rarity, Slot = ItemReader.SlotOf?.Invoke(p.ItemId) ?? EquipSlot.Unknown };
        ItemReader.ReadBodyInto(r, p.Item, profile);
        p.Count = p.Item.Amount;
        return p;
    }

    /// <summary>CNpcShopProductCli::ReadPacket mode 7 (npcshopproductbase.cpp:191-202): i32 serial, i32 itemId, u8 rarity,
    /// i64 price, then the item stream (m_54 = item.GetCount(), m_a0 = price).</summary>
    public static ShopProduct ReadRepurchase(ByteReader r) => ReadRepurchase(r, ProtocolProfile.Kms2525);

    /// <summary><see cref="ReadRepurchase(ByteReader)"/> for a profile: the GMS2 entry has the same head and the GMS2 item body.</summary>
    public static ShopProduct ReadRepurchase(ByteReader r, ProtocolProfile profile) {
        var p = new ShopProduct {
            Serial = r.ReadInt(),
            ItemId = r.ReadInt(),
            Rarity = r.ReadByte(),
            Price = r.ReadLong(),
        };
        p.Item = ItemReader.ReadItem(r, p.ItemId, p.Rarity, profile);
        p.Count = p.Item?.Amount ?? 0;
        return p;
    }

    /// <summary>Writes this product with an item body of default blocks and <see cref="Count"/> as the amount (fixtures).</summary>
    public void Write(ByteWriter w) {
        w.WriteInt(Serial); w.WriteInt(ItemId); w.WriteByte((byte) Currency); w.WriteInt(CurrencyItemId); w.WriteInt(CurrencyItemTag);
        w.WriteInt((int) Price); w.WriteInt((int) OriginalPrice); w.WriteByte((byte) Rarity); w.WriteInt(Count); w.WriteInt(Limit); w.WriteInt(Bought);
        w.WriteInt(GuildTrophy); w.WriteString(Categories); w.WriteInt(Achieve); w.WriteInt(AchieveGrade);
        w.WriteByte((byte) ChampionshipRank); w.WriteShort((short) ChampionshipScore); w.WriteByte((byte) GuildNpc); w.WriteShort((short) GuildNpcLevel);
        w.WriteBool(PremiumSlot); w.WriteShort((short) Bundle); w.WriteByte((byte) Label); w.WriteByte((byte) SaleTag);
        w.WriteString(IconTag); w.WriteShort((short) Alliance); w.WriteInt(FameGrade); w.WriteBool(MainItem);
        w.WriteBool(Period != null);
        Period?.Write(w);
        w.WriteBool(Life != null);
        if (Life is { } life) {
            w.WriteByte((byte) life.Type);
            if (life.Type is 1 or 3 or 4) w.WriteInt(life.Value);
            else if (life.Type == 2) w.WriteLong(life.Time);
        }
        ItemReader.WriteEmptyBody(w, Count);
    }

    /// <summary>Writes a mode 7 repurchase product with an empty item body of <see cref="Count"/> (tests and fixtures).</summary>
    public void WriteRepurchase(ByteWriter w) {
        w.WriteInt(Serial); w.WriteInt(ItemId); w.WriteByte((byte) Rarity); w.WriteLong(Price);
        ItemReader.WriteEmptyBody(w, Count);
    }
}

/// <summary>Inbound 0x51 command 2 (UIShopDialog::ReadProductUpdate, uishopdialog.cpp:1740): i32 serial, i32 buyCount.</summary>
public sealed record ShopProductUpdate(int Serial, int BuyCount);

/// <summary>Inbound 0x51 command 4 (UIShopDialog::ReadPurchaseResult, uishopdialog.cpp:1834): u32 itemId, i32 count,
/// u32 price, u8 rank, bool guildStorage.</summary>
public sealed record ShopPurchaseResult(int ItemId, int Count, uint Price, byte Rank, bool GuildStorage);

/// <summary>Inbound 0x51 command 9 (UIShopDialog::ReadResetResult, uishopdialog.cpp:1756): bool success; when true, u32
/// refundItemId and u32 refundCount.</summary>
public sealed record ShopResetResult(bool Success, int RefundItemId, int RefundCount);

/// <summary>Inbound 0x51 command 13 (UIShopDialog::ReadNewStart, uishopdialog.cpp:1664): i32 npcId, i32 shopId, i32
/// tabShopId, u16 totalCount.</summary>
public sealed record ShopNewStart(int NpcId, int ShopId, int TabShopId, int TotalCount);

/// <summary>CNpcShopItemNew::Read (npcshopdata.cpp:821): i32 itemId, bool flag, u8 rarity, string categories, u8 productType,
/// u8 productKind, bool hasPeriod (+ CNpcShopBuyPeriod).</summary>
public sealed record ShopNewProduct {
    public int ItemId;
    public bool Flag;
    public byte Rarity;
    public string Categories = "";
    public byte ProductType;
    public byte ProductKind;
    public ShopBuyPeriod Period;

    public static ShopNewProduct Read(ByteReader r) {
        var p = new ShopNewProduct {
            ItemId = r.ReadInt(),
            Flag = r.ReadBool(),
            Rarity = r.ReadByte(),
            Categories = r.ReadString(),
            ProductType = r.ReadByte(),
            ProductKind = r.ReadByte(),
        };
        if (r.ReadBool()) p.Period = ShopBuyPeriod.Read(r);
        return p;
    }

    public void Write(ByteWriter w) {
        w.WriteInt(ItemId); w.WriteBool(Flag); w.WriteByte(Rarity); w.WriteString(Categories);
        w.WriteByte(ProductType); w.WriteByte(ProductKind);
        w.WriteBool(Period != null);
        Period?.Write(w);
    }
}

/// <summary>Inbound 0x51 command 15 (UIShopDialog::ReadShopError, uishopdialog.cpp:1858): i32 error, u8 cmd, i32 value.</summary>
public sealed record ShopError(int Error, byte Command, int Value);

/// <summary>
/// Inbound 0x51 (Shop): u8 command (UIShopDialog::OnShopPacket @0x141af8710, uishopdialog.cpp:1524).
/// 0 header, 1 normal products, 2 product buy-count update, 4 purchase result, 6 repurchase start, 7 repurchase add,
/// 8 repurchase removal, 9 reset result, 11 merat start, 12 merat items, 13 new start, 14 new products, 15 shop error.
/// </summary>
public sealed class ShopPacket {
    public int Command { get; private init; }
    public ShopInfo Info { get; private init; }
    public IReadOnlyList<ShopProduct> Products { get; private init; }
    public ShopProductUpdate ProductUpdate { get; private init; }
    public ShopPurchaseResult PurchaseResult { get; private init; }
    public int RepurchaseCount { get; private init; }
    public int RemoveSerial { get; private init; }
    public ShopResetResult ResetResult { get; private init; }
    public int MeratCount { get; private init; }
    public IReadOnlyList<ShopMeratItem> MeratItems { get; private init; }
    public ShopNewStart NewStart { get; private init; }
    public IReadOnlyList<ShopNewProduct> NewProducts { get; private init; }
    public ShopError Error { get; private init; }

    public static ShopPacket Read(ByteReader r) => Read(r, ProtocolProfile.Kms2525);

    public static ShopPacket Read(ByteReader r, ProtocolProfile profile) {
        int cmd = r.ReadByte();
        switch (cmd) {
            case ShopModes.RecvInfo: return new ShopPacket { Command = cmd, Info = ShopInfo.Read(r) };
            case ShopModes.RecvProducts: {
                int n = r.ReadByte();
                var list = new List<ShopProduct>(n);
                for (int i = 0; i < n; i++) list.Add(ShopProduct.Read(r, profile));
                return new ShopPacket { Command = cmd, Products = list };
            }
            case ShopModes.RecvProductUpdate: return new ShopPacket { Command = cmd, ProductUpdate = new ShopProductUpdate(r.ReadInt(), r.ReadInt()) };
            case ShopModes.RecvPurchaseResult: return new ShopPacket { Command = cmd, PurchaseResult = new ShopPurchaseResult(r.ReadInt(), r.ReadInt(), (uint) r.ReadInt(), r.ReadByte(), r.ReadBool()) };
            case ShopModes.RecvRepurchaseStart: return new ShopPacket { Command = cmd, RepurchaseCount = (ushort) r.ReadShort() };
            case ShopModes.RecvRepurchaseAdd: {
                int n = (ushort) r.ReadShort();
                var list = new List<ShopProduct>(n);
                for (int i = 0; i < n; i++) list.Add(ShopProduct.ReadRepurchase(r, profile));
                return new ShopPacket { Command = cmd, Products = list };
            }
            case ShopModes.RecvRepurchaseRemoval: return new ShopPacket { Command = cmd, RemoveSerial = r.ReadInt() };
            case ShopModes.RecvResetResult: {
                bool ok = r.ReadBool();
                int itemId = ok ? r.ReadInt() : 0;
                int count = ok ? r.ReadInt() : 0;
                return new ShopPacket { Command = cmd, ResetResult = new ShopResetResult(ok, itemId, count) };
            }
            case ShopModes.RecvMeratStart: return new ShopPacket { Command = cmd, MeratCount = (ushort) r.ReadShort() };
            case ShopModes.RecvMeratItems: {   // ReadMeratItems (uishopdialog.cpp:1722): u8 count, each CNpcShopMeratItemCli::vfunc_2
                int n = r.ReadByte();
                var list = new List<ShopMeratItem>(n);
                for (int i = 0; i < n; i++) list.Add(ShopMeratItem.Read(r));
                return new ShopPacket { Command = cmd, MeratItems = list };
            }
            case ShopModes.RecvNewStart: return new ShopPacket { Command = cmd, NewStart = new ShopNewStart(r.ReadInt(), r.ReadInt(), r.ReadInt(), (ushort) r.ReadShort()) };
            case ShopModes.RecvNewProducts: {
                int n = r.ReadByte();
                var list = new List<ShopNewProduct>(n);
                for (int i = 0; i < n; i++) list.Add(ShopNewProduct.Read(r));
                return new ShopPacket { Command = cmd, NewProducts = list };
            }
            case ShopModes.RecvError: return new ShopPacket { Command = cmd, Error = new ShopError(r.ReadInt(), r.ReadByte(), r.ReadInt()) };
            default: return new ShopPacket { Command = cmd };
        }
    }

    static byte[] Bytes(ByteWriter w) => w.Buffer.AsSpan(0, w.Length).ToArray();

    /// <summary>A command 0 body (tests and offline fixtures).</summary>
    public static byte[] WriteInfo(ShopInfo info) {
        var w = new ByteWriter(); w.WriteByte(0); info.Write(w); return Bytes(w);
    }

    /// <summary>A command 1 body (tests and offline fixtures).</summary>
    public static byte[] WriteProducts(IReadOnlyList<ShopProduct> products) {
        var w = new ByteWriter(); w.WriteByte(1); w.WriteByte((byte) products.Count);
        foreach (var p in products) p.Write(w);
        return Bytes(w);
    }

    /// <summary>A command 2 body (tests).</summary>
    public static byte[] WriteProductUpdate(int serial, int buyCount) {
        var w = new ByteWriter(); w.WriteByte(2); w.WriteInt(serial); w.WriteInt(buyCount); return Bytes(w);
    }

    /// <summary>A command 4 body (tests).</summary>
    public static byte[] WritePurchaseResult(ShopPurchaseResult r) {
        var w = new ByteWriter(); w.WriteByte(4); w.WriteInt(r.ItemId); w.WriteInt(r.Count); w.WriteInt((int) r.Price);
        w.WriteByte(r.Rank); w.WriteBool(r.GuildStorage); return Bytes(w);
    }

    /// <summary>A command 6 body (tests).</summary>
    public static byte[] WriteRepurchaseStart(int count) {
        var w = new ByteWriter(); w.WriteByte(6); w.WriteShort((short) count); return Bytes(w);
    }

    /// <summary>A command 7 body (tests).</summary>
    public static byte[] WriteRepurchaseAdd(IReadOnlyList<ShopProduct> products) {
        var w = new ByteWriter(); w.WriteByte(7); w.WriteShort((short) products.Count);
        foreach (var p in products) p.WriteRepurchase(w);
        return Bytes(w);
    }

    /// <summary>A command 8 body (tests).</summary>
    public static byte[] WriteRepurchaseRemoval(int serial) {
        var w = new ByteWriter(); w.WriteByte(8); w.WriteInt(serial); return Bytes(w);
    }

    /// <summary>A command 9 body (tests).</summary>
    public static byte[] WriteResetResult(ShopResetResult r) {
        var w = new ByteWriter(); w.WriteByte(9); w.WriteBool(r.Success);
        if (r.Success) { w.WriteInt(r.RefundItemId); w.WriteInt(r.RefundCount); }
        return Bytes(w);
    }

    /// <summary>A command 13 body (tests).</summary>
    public static byte[] WriteNewStart(ShopNewStart s) {
        var w = new ByteWriter(); w.WriteByte(13); w.WriteInt(s.NpcId); w.WriteInt(s.ShopId);
        w.WriteInt(s.TabShopId); w.WriteShort((short) s.TotalCount); return Bytes(w);
    }

    /// <summary>A command 14 body (tests).</summary>
    public static byte[] WriteNewProducts(IReadOnlyList<ShopNewProduct> products) {
        var w = new ByteWriter(); w.WriteByte(14); w.WriteByte((byte) products.Count);
        foreach (var p in products) p.Write(w);
        return Bytes(w);
    }

    /// <summary>A command 15 body (tests).</summary>
    public static byte[] WriteError(ShopError e) {
        var w = new ByteWriter(); w.WriteByte(15); w.WriteInt(e.Error); w.WriteByte(e.Command); w.WriteInt(e.Value); return Bytes(w);
    }
}

// ---- Outbound 0x28 (SendOp::Shop) ----------------------------------------------------------------------------------

/// <summary>Outbound 0x28 mode 3 (UIShopDialog::RequestRepurchase @0x141affeb0, uishopdialog.cpp:2826): u8 3, i32 serial.</summary>
public sealed record ShopRepurchaseRequest(int Serial) : IOutPacket {
    public Outbound Op => Outbound.Shop;
    public void Write(ByteWriter w) { w.WriteByte(ShopModes.SendRepurchase); w.WriteInt(Serial); }
}

/// <summary>Outbound 0x28 mode 4 (UIShopDialog::SendPurchase @0x141b00060, uishopdialog.cpp:2846): u8 4, i32 serial, i32 quantity, i32 shopId.</summary>
public sealed record ShopBuyRequest(int Serial, int Quantity, int ShopId) : IProfileOutPacket {
    public Outbound Op => Outbound.Shop;
    public void Write(ByteWriter w) => Write(w, ProtocolProfile.Kms2525);
    /// <summary>GMS2 sends no shop id: u8 4, i32 serial, i32 quantity (every recorded request, 9 bytes).</summary>
    public void Write(ByteWriter w, ProtocolProfile profile) { w.WriteByte(ShopModes.SendBuy); w.WriteInt(Serial); w.WriteInt(Quantity); if (!profile.IsGms2) w.WriteInt(ShopId); }
}

/// <summary>Outbound 0x28 mode 5 (RequestSellItem @0x141425fc0, itemugcmesh.cpp:1097): u8 5, i64 itemUid, i32 count.</summary>
public sealed record ShopSellRequest(long ItemUid, int Count) : IOutPacket {
    public Outbound Op => Outbound.Shop;
    public void Write(ByteWriter w) { w.WriteByte(ShopModes.SendSell); w.WriteLong(ItemUid); w.WriteInt(Count); }
}

/// <summary>Outbound 0x28 mode 9 (UIShopDialog::ResetList @0x141afe8b0, uishopdialog.cpp:2550): u8 9, i32 resetPrice.</summary>
public sealed record ShopResetListRequest(int ResetPrice) : IOutPacket {
    public Outbound Op => Outbound.Shop;
    public void Write(ByteWriter w) { w.WriteByte(ShopModes.SendReset); w.WriteInt(ResetPrice); }
}

/// <summary>Outbound 0x28 mode 10 (UIShopDialog::vfunc_72 @0x141af3460, uishopdialog.cpp:750, :762): u8 10. Request the
/// product list after the reset timer expires or the day rolls over.</summary>
public sealed record ShopRequestListRequest : IOutPacket {
    public Outbound Op => Outbound.Shop;
    public void Write(ByteWriter w) { w.WriteByte(ShopModes.SendRequestList); }
}

/// <summary>Outbound 0x28 mode 13 (UIShopDialog::FillNewProducts @0x141afc9d0, uishopdialog.cpp:2270): u8 13. Request the
/// new-items tab list.</summary>
public sealed record ShopRequestNewItemsRequest : IOutPacket {
    public Outbound Op => Outbound.Shop;
    public void Write(ByteWriter w) { w.WriteByte(ShopModes.SendRequestNewItems); }
}
