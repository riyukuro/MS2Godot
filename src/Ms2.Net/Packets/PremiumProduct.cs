using System.Collections.Generic;
using Maple2.PacketLib.Tools;
using Ms2.Net.Wire;

namespace Ms2.Net.Packets;

/// <summary>
/// One premium (merat market) product: CDBPremiumProduct (meratmarketbasketinfo.h:67-147). <see cref="ReadBrief"/> is
/// CDBPremiumProduct::ReadBrief (meratmarketbasketinfo.cpp:635, what a star-shop item carries); <see cref="Read"/> is the full
/// CDBPremiumProduct::Read (:530) the sub products use.
/// </summary>
public sealed class PremiumProduct {
    // The fields without a name below are read and kept as the client's CDBPremiumProduct members (meratmarketbasketinfo.cpp:530 Read) that no port code uses yet;
    // they stay private until a client use shows their meaning.
    public int Id;                  // sn
    public int Kind;                // +0x1c (u8)
    public string Name = "";
    public int Type;                // 1 bundle, 3 limited, 6 home, 7/8 random sale
    public int Flags;               // +0x2c
    public int ChartOrder;          // full only
    public int CouponFlags;
    public int Visible;
    public int SaleTag;
    public int Currency;
    public long Price;
    public long SalePrice;
    int Field58;
    public long StartDate, EndDate; // full only (CMsTime ticks)
    int Field80, Field84;
    public int BuyLimit;            // u8
    public int BuyLimitMax;
    int Field90;             // u8
    public int MinLevel, MaxLevel;    // i16
    public int JobMask;
    public int ItemId;
    public int Grade;               // u8
    public int ItemCount, ItemDays, ItemBonus;
    public int CategoryId;
    public int AchieveId, AchieveGrade;   // +0xf8, +0xfc: the require-achieve pair (uishopdialog.cpp:1496)
    public int PcBangLevel;        // u8
    public bool PointPrice;
    public int Point;
    // full read only
    public int LimitCount;
    public int LimitFlag;
    int Field09C;
    public string Category = "";
    public string Icon = "";
    bool FieldF0;
    int FieldF4;             // u8
    int Field104;
    bool Field108;
    int Field100;
    public string EventCategory = "";
    public long EventStart, EventEnd;
    int Field158;            // u8
    public string Url = "";
    bool Field168;
    int Field16C, Field170, Field174, Field1A8;
    long Time178, Time188, Time198;
    int Field1AC, Field1B4;
    public int RandomSaleRate;      // the percent a type 7/8 product is cut by (GetSalePrice, meratmarketbasketinfo.cpp:685)
    public long RandomSaleDay;
    /// <summary>The bundle (type 1) or limited (type 3) children: CMeratMarketPremiumProduct::ReadSubProducts / ReadLimitProducts.</summary>
    public List<PremiumProduct> Sub = new();
    public List<PremiumProduct> Limit = new();
    public int BuyCount;            // CMeratMarketPremiumProduct::Read only
    public int SoldCount;           // type 3
    public int HomeSoldCount;       // type 6

    /// <summary>CDBPremiumProduct::ReadBrief (meratmarketbasketinfo.cpp:635).</summary>
    public static PremiumProduct ReadBriefBase(ByteReader r) {
        var p = new PremiumProduct();
        p.Id = r.ReadInt(); p.Kind = r.ReadByte(); p.Name = r.ReadUnicodeString(); p.Type = r.ReadByte();
        p.Flags = r.ReadInt(); p.CouponFlags = r.ReadInt(); p.Visible = r.ReadByte(); p.SaleTag = r.ReadByte(); p.Currency = r.ReadByte();
        p.Price = r.ReadLong(); p.SalePrice = r.ReadLong(); p.Field58 = r.ReadByte();
        p.Field80 = r.ReadInt(); p.Field84 = r.ReadInt(); p.BuyLimit = r.ReadByte(); p.BuyLimitMax = r.ReadInt(); p.Field90 = r.ReadByte();
        p.MinLevel = r.ReadShort(); p.MaxLevel = r.ReadShort(); p.JobMask = r.ReadInt(); p.ItemId = r.ReadInt(); p.Grade = r.ReadByte();
        p.ItemCount = r.ReadInt(); p.ItemDays = r.ReadInt(); p.ItemBonus = r.ReadInt(); p.CategoryId = r.ReadInt();
        p.AchieveId = r.ReadInt(); p.AchieveGrade = r.ReadInt(); p.PcBangLevel = r.ReadByte(); p.PointPrice = r.ReadBool(); p.Point = r.ReadInt();
        return p;
    }

    /// <summary>CMeratMarketPremiumProduct::ReadBrief (:1240): the base block, then the bundle (type 1) or limited (type 3) children.</summary>
    public static PremiumProduct ReadBrief(ByteReader r) {
        var p = ReadBriefBase(r);
        if (p.Type == 1) ReadChildren(r, p.Sub);
        else if (p.Type == 3) ReadChildren(r, p.Limit);
        return p;
    }

    /// <summary>ReadSubProducts / ReadLimitProducts (:1486, :1524): u8 count, each u8 exists + a full product when 1.</summary>
    static void ReadChildren(ByteReader r, List<PremiumProduct> into) {
        int n = r.ReadByte();
        for (int i = 0; i < n; i++)
            if (r.ReadByte() == 1) into.Add(Read(r));
    }

    /// <summary>CDBPremiumProduct::Read (:530).</summary>
    public static PremiumProduct Read(ByteReader r) {
        var p = new PremiumProduct();
        p.Id = r.ReadInt(); p.Kind = r.ReadByte(); p.Name = r.ReadUnicodeString(); p.Type = r.ReadByte();
        p.Flags = r.ReadInt(); p.ChartOrder = r.ReadInt(); p.CouponFlags = r.ReadInt(); p.Visible = r.ReadByte(); p.SaleTag = r.ReadByte();
        p.Currency = r.ReadByte(); p.Price = r.ReadLong(); p.SalePrice = r.ReadLong(); p.Field58 = r.ReadByte();
        p.StartDate = r.ReadLong(); p.EndDate = r.ReadLong(); p.Field80 = r.ReadInt(); p.Field84 = r.ReadInt();
        p.BuyLimit = r.ReadByte(); p.BuyLimitMax = r.ReadInt(); p.Field90 = r.ReadByte(); p.MinLevel = r.ReadShort(); p.MaxLevel = r.ReadShort();
        p.JobMask = r.ReadInt(); p.ItemId = r.ReadInt(); p.Grade = r.ReadByte(); p.ItemCount = r.ReadInt(); p.ItemDays = r.ReadInt();
        p.ItemBonus = r.ReadInt(); p.CategoryId = r.ReadInt(); p.LimitCount = r.ReadInt(); p.LimitFlag = r.ReadByte(); p.Field09C = r.ReadInt();
        p.Category = r.ReadString(); p.Icon = r.ReadString(); p.FieldF0 = r.ReadBool(); p.FieldF4 = r.ReadByte(); p.Field104 = r.ReadInt();
        p.Field108 = r.ReadBool(); p.AchieveId = r.ReadInt(); p.AchieveGrade = r.ReadInt(); p.Field100 = r.ReadInt(); p.PcBangLevel = r.ReadByte();
        p.PointPrice = r.ReadBool(); p.Point = r.ReadInt();
        if (r.ReadBool()) { p.EventCategory = r.ReadString(); p.EventStart = r.ReadLong(); p.EventEnd = r.ReadLong(); }
        p.Field158 = r.ReadByte(); p.Url = r.ReadUnicodeString(); p.Field168 = r.ReadBool();
        if (p.Type == 6) {
            p.Field16C = r.ReadByte(); p.Field170 = r.ReadByte(); p.Field174 = r.ReadInt();
            p.Time178 = r.ReadLong(); p.Time188 = r.ReadLong(); p.Time198 = r.ReadLong(); p.Field1A8 = r.ReadInt();
        }
        if (p.Type == 7 || p.Type == 8) {
            p.Field1AC = r.ReadInt(); p.RandomSaleRate = r.ReadInt(); p.Field1B4 = r.ReadInt(); p.RandomSaleDay = r.ReadLong();
        }
        return p;
    }
}

/// <summary>
/// One star-shop item (inbound 0x51 cmd 12): CNpcShopMeratItemCli::vfunc_2 (npcshopproductbase.cpp:556) = CNpcShopMeratItemBase::vfunc_2
/// (npcshopdata.cpp:2357: the brief product, u8 display order) then two ints the product keeps as its +0x1c8 / +0x1cc.
/// </summary>
public sealed class ShopMeratItem {
    public PremiumProduct Product;
    public int DisplayOrder;
    public int ExtraA, ExtraB;

    public static ShopMeratItem Read(ByteReader r) {
        var m = new ShopMeratItem { Product = PremiumProduct.ReadBrief(r), DisplayOrder = r.ReadByte() };
        m.ExtraA = r.ReadInt(); m.ExtraB = r.ReadInt();
        return m;
    }
}
