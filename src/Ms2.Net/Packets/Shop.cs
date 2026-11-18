// Layout from the restored client's UIShopDialog::OnShopPacket (uishopdialog.cpp; the handler of RecvOp 0x51 its ctor registers),
// ReadShopInfo (0x141af88d0), ReadProducts (0x141af90f0) and CNpcShopProductCli::ReadPacket mode 1 (npcshopproductbase.cpp,
// 0x140db61a0).
using Maple2.PacketLib.Tools;

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
}

/// <summary>NpcShopItemLife::Read (commonstruct_wa.cpp:508): u8 type; types 1, 3, 4 an i32 value, type 2 an i64 time.</summary>
public readonly record struct ShopItemLife(int Type, int Value, long Time);

/// <summary>
/// One product, CNpcShopProductCli::ReadPacket mode 1, in packet order (the client's member in each comment). The item is
/// CItem::FUN_140523c40(item id, rarity, count) and CItem::ReadFromStream, then SetCount(bundle).
/// </summary>
public sealed record ShopProduct {
    public int Serial;            // m_18
    public int ItemId;
    public int Currency;          // m_88: the currency type (1 an item: GetMaxAffordableCount; 7 skips the usable check:
                                  // PopulateProduct; 0xd star points: CheckStarPointEvent)
    public int CurrencyItemId;    // m_8c
    public int CurrencyItemTag;   // m_90: paid with any item of this tag (GetMaxAffordableCount counts by tag)
    public int Price;             // m_a0: the sale price
    public int OriginalPrice;     // m_a8
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
    /// <summary>IsSoldOut: a limit and none left.</summary>
    public bool SoldOut => Limit > 0 && Remaining <= 0;

    public static ShopProduct Read(ByteReader r) {
        var p = new ShopProduct();
        p.Serial = r.ReadInt(); p.ItemId = r.ReadInt(); p.Currency = r.ReadByte(); p.CurrencyItemId = r.ReadInt(); p.CurrencyItemTag = r.ReadInt();
        p.Price = r.ReadInt(); p.OriginalPrice = r.ReadInt(); p.Rarity = r.ReadByte(); p.Count = r.ReadInt(); p.Limit = r.ReadInt();
        p.Bought = r.ReadInt(); p.GuildTrophy = r.ReadInt(); p.Categories = r.ReadString(); p.Achieve = r.ReadInt(); p.AchieveGrade = r.ReadInt();
        p.ChampionshipRank = r.ReadByte(); p.ChampionshipScore = (ushort) r.ReadShort(); p.GuildNpc = r.ReadByte(); p.GuildNpcLevel = (ushort) r.ReadShort();
        p.PremiumSlot = r.ReadBool(); p.Bundle = (ushort) r.ReadShort(); p.Label = r.ReadByte(); p.SaleTag = r.ReadByte();
        p.IconTag = r.ReadString(); p.Alliance = (ushort) r.ReadShort(); p.FameGrade = r.ReadInt();
        p.MainItem = r.ReadBool();
        if (r.ReadBool()) {
            var period = p.Period = new ShopBuyPeriod();
            if (period.HasTime = r.ReadBool()) { period.Start = r.ReadLong(); period.End = r.ReadLong(); }
            if (period.HasPart = r.ReadBool()) {
                int n = r.ReadByte();
                for (int i = 0; i < n; i++) period.PartTimes.Add((r.ReadInt(), r.ReadInt()));
                int days = r.ReadByte();
                for (int i = 0; i < days; i++) period.Days.Add(r.ReadByte());
            }
        }
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

    /// <summary>Writes this product with an item body of default blocks and <see cref="Count"/> as the amount (fixtures).</summary>
    public void Write(ByteWriter w) {
        w.WriteInt(Serial); w.WriteInt(ItemId); w.WriteByte((byte) Currency); w.WriteInt(CurrencyItemId); w.WriteInt(CurrencyItemTag);
        w.WriteInt(Price); w.WriteInt(OriginalPrice); w.WriteByte((byte) Rarity); w.WriteInt(Count); w.WriteInt(Limit); w.WriteInt(Bought);
        w.WriteInt(GuildTrophy); w.WriteString(Categories); w.WriteInt(Achieve); w.WriteInt(AchieveGrade);
        w.WriteByte((byte) ChampionshipRank); w.WriteShort((short) ChampionshipScore); w.WriteByte((byte) GuildNpc); w.WriteShort((short) GuildNpcLevel);
        w.WriteBool(PremiumSlot); w.WriteShort((short) Bundle); w.WriteByte((byte) Label); w.WriteByte((byte) SaleTag);
        w.WriteString(IconTag); w.WriteShort((short) Alliance); w.WriteInt(FameGrade); w.WriteBool(MainItem);
        w.WriteBool(Period != null);
        if (Period != null) {
            w.WriteBool(Period.HasTime);
            if (Period.HasTime) { w.WriteLong(Period.Start); w.WriteLong(Period.End); }
            w.WriteBool(Period.HasPart);
            if (Period.HasPart) {
                w.WriteByte((byte) Period.PartTimes.Count);
                foreach (var (s, e) in Period.PartTimes) { w.WriteInt(s); w.WriteInt(e); }
                w.WriteByte((byte) Period.Days.Count);
                foreach (var d in Period.Days) w.WriteByte((byte) d);
            }
        }
        w.WriteBool(Life != null);
        if (Life is { } life) {
            w.WriteByte((byte) life.Type);
            if (life.Type is 1 or 3 or 4) w.WriteInt(life.Value);
            else if (life.Type == 2) w.WriteLong(life.Time);
        }
        ItemReader.WriteEmptyBody(w, Count);
    }
}

/// <summary>
/// Inbound 0x51 (Shop): u8 command. 0 the shop header (<see cref="ShopInfo"/>), 1 a batch of products (u8 count of
/// <see cref="ShopProduct"/>). The other commands (2 product update, 4 purchase result, 6-8 repurchase, 9 reset, 11-12 star
/// items, 13-14 new items, 15 error) are not read yet: <see cref="Command"/> alone is set.
/// </summary>
public sealed class ShopPacket {
    public int Command { get; private init; }
    public ShopInfo Info { get; private init; }
    public IReadOnlyList<ShopProduct> Products { get; private init; }

    public static ShopPacket Read(ByteReader r) {
        int cmd = r.ReadByte();
        switch (cmd) {
            case 0: return new ShopPacket { Command = cmd, Info = ShopInfo.Read(r) };
            case 1: {
                int n = r.ReadByte();
                var list = new List<ShopProduct>(n);
                for (int i = 0; i < n; i++) list.Add(ShopProduct.Read(r));
                return new ShopPacket { Command = cmd, Products = list };
            }
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
}
