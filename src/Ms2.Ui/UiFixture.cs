namespace Ms2.Ui;

/// <summary>
/// FIXTURE character values for running the HUD without a server: real-shaped numbers (a level 21 Knight at 38% exp with
/// a rest pool), not a recorded character. The tables (next level exp, job names, strings) are the client's own. Replace
/// with the ServerEnter / stat packets once a session is wired.
/// </summary>
public static class UiFixture
{
    public const string Label = "fixture character (not live data)";

    public static void Apply(UiModel m)
    {
        var p = m.Player;
        p.Name = "Fixture"; p.Title = "";
        p.Level = 21; p.SubJob = 100;                              // jobname.xml 100 = Knight
        p.HpCur = 1630; p.HpMax = 2100;
        p.SpCur = 82; p.SpMax = 100;
        p.EpCur = 100; p.EpMax = 120;
        long next = m.Tables.NextExp(p.Level);
        p.Exp = next > 0 ? next * 38 / 100 : 0;
        p.RestExp = next > 0 ? next * 12 / 100 : 0;
        p.Mesos = 1_284_530; p.Merats = 350;
        p.Channel = 3;
        m.Field.Channel = 3; m.Field.Channels = new[] { 1, 2, 3, 4, 5 };
        m.ServerKeys = NewCharacterKeyTable();
        m.Keys.Apply(m.ServerKeys);
        m.FixtureLabel = Label;
    }

    /// <summary>
    /// FIXTURE bag for the inventory without a server: items in the parsed shape of the item packet the server sends at login
    /// (ItemInventory mode 8, LoadTab: id, uid, slot, amount; Ms2.Net ItemMessage), with real item ids, not a recorded bag.
    /// </summary>
    public static IReadOnlyList<Ms2.Net.Packets.EquipItem> InventoryItems() => new Ms2.Net.Packets.EquipItem[]
    {
        new() { Id = 15000001, Uid = 1001, InventorySlot = 0, Amount = 1, Rarity = 2 },
        new() { Id = 20000004, Uid = 1002, InventorySlot = 0, Amount = 120, Rarity = 1 },
        new() { Id = 20000013, Uid = 1003, InventorySlot = 1, Amount = 5, Rarity = 1 },
        new() { Id = 30000001, Uid = 1004, InventorySlot = 0, Amount = 3, Rarity = 1 },
    };

    /// <summary>
    /// FIXTURE npc shop in the shape of the shop packet (Inbound 0x51 commands 0 and 1): Surnuny's gear shop (npc 11000004,
    /// shop 106) with the twelve products a capture of it listed, their serial, item, grade, price and category as the
    /// server's shop table gives them (one in stock each, paid in mesos). The restock fields are the shop table's (restock on,
    /// 1,000 mesos); the restock time and interval are made up (the server's are not known): due in 30 s of the client's
    /// default interval.
    /// </summary>
    public static (Ms2.Net.Packets.ShopInfo Info, Ms2.Net.Packets.ShopProduct[] Products) GearShop(long now)
    {
        var rows = new (int Serial, int Item, int Grade, int Price, string Category)[]
        {
            (16, 11400003, 1, 2360, "CL"), (24, 11400012, 1, 4470, "CL"), (25, 11400014, 1, 5160, "CL"),
            (34, 11500006, 1, 4470, "PA"), (46, 11600002, 1, 3560, "GL"), (74, 11700027, 1, 3030, "SH"),
            (143, 15100002, 1, 2790, "BH"), (146, 15100005, 1, 4470, "BH"), (158, 15300005, 1, 4470, "BH"),
            (172, 11500399, 1, 5160, "PA"), (198, 11300009, 2, 8230, "CP"), (377, 15500003, 2, 8230, "BH"),
        };
        var info = new Ms2.Net.Packets.ShopInfo
        {
            NpcId = 11000004, ShopId = 106, ResetTime = now + 30, ProductCount = rows.Length, TabShopId = 5,
            ResetEnable = true, ResetPrice = 1000, HideReset = false, Icon = "",
        };
        var products = rows.Select(r => new Ms2.Net.Packets.ShopProduct
        {
            Serial = r.Serial, ItemId = r.Item, Rarity = r.Grade, Price = r.Price, OriginalPrice = r.Price, Count = 1, Limit = 1,
            Categories = r.Category,
        }).ToArray();
        return (info, products);
    }

    /// <summary>
    /// FIXTURE character window record without a server: the own FieldAddUser's user block, equips and gems, a Stat packet with all 35 stats
    /// (three values each, Ms2.Net PlayerStat) and the AttributePoint loads (modes 0 and 1), in the parsed packet shapes, with
    /// real-shaped values for the HUD fixture's level 21 Knight, not a recorded character.
    /// </summary>
    public static Ms2.Net.Packets.MyCharacter Character()
    {
        var me = new Ms2.Net.Packets.MyCharacter();
        me.Apply(new Ms2.Net.Packets.FieldAddUser
        {
            ObjectId = 1,
            User = new Ms2.Net.Packets.UserBlock { AccountId = 1, CharacterId = 2, Name = "Fixture", Level = 21, JobCode = 10, Job = 100, Gender = 0, Motto = "", Picture = "", GuildName = "" },
            // the equip block: a two-handed sword in RH and an outfit top; the gem block: a chat balloon (gem type 3)
            Equips =
            {
                new Ms2.Net.Packets.EquipItem { Id = 15000001, Uid = 2001, Slot = Ms2.Net.Packets.EquipSlot.RH, Rarity = 2, Amount = 1 },
                new Ms2.Net.Packets.EquipItem { Id = 11400021, Uid = 2002, Slot = Ms2.Net.Packets.EquipSlot.CL, Rarity = 1, Amount = 1 },
            },
            Badges = { new Ms2.Net.Packets.EquipItem { Id = 70200001, Uid = 2003, IsBadge = true, BadgeSlot = 3, GemType = 3, Rarity = 1, Amount = 1 } },
        });
        var stats = new Dictionary<byte, long[]>();
        for (byte i = 0; i < Ms2.Net.Packets.PlayerStat.Total; i++) stats[i] = new long[] { 0, 0, 0 };
        void Set(byte stat, long total, long base_, long current) => stats[stat] = new[] { total, base_, current };
        Set(0, 48, 43, 48); Set(1, 12, 12, 12); Set(2, 4, 4, 4); Set(3, 6, 6, 6);  // str, dex, int, luk
        Set(4, 2100, 1900, 1630);                                                // health
        Set(7, 100, 100, 82); Set(10, 120, 120, 100);                            // spirit, stamina
        Set(0xe, 120, 120, 120); Set(0x11, 62, 55, 62);                           // attack speed, critical rate
        Set(0x14, 41, 30, 41);                                                   // defense
        me.Apply(new Ms2.Net.Packets.PlayerStat(1, stats));
        me.StatPoints.Apply(new Ms2.Net.Packets.AttributePointMessage.Extra(15, Array.Empty<(int, uint)>()));
        me.StatPoints.Apply(new Ms2.Net.Packets.AttributePointMessage.Stats(15, new (byte, int)[] { (0, 5), (0x11, 7) }));
        return me;
    }

    /// <summary>A KeyTable packet in the server's layout (Ms2.Net ServerKeyTable): mode 0 with the use-default flag, which
    /// makes the client load DefaultKey.xml and read no pages (OnKeyTable, keytable.cpp:285), so every quick slot is empty.</summary>
    public static Ms2.Net.Packets.ServerKeyTable NewCharacterKeyTable()
    {
        var t = new Ms2.Net.Packets.ServerKeyTable();
        t.Apply(new Maple2.PacketLib.Tools.ByteReader(Ms2.Net.Packets.ServerKeyTable.Write(0, true, null, 0, null)));
        return t;
    }
    /// <summary>A rank 1 skill book for a job group as a new character has it: every skill of the job's table at level 1, learnt
    /// when the table learns it with the rank (isLearn), and no tabs saved (the skill window's 0x10C mode 0 with none, which
    /// makes it add the free tab "Build 1").</summary>
    public static Ms2.Net.Packets.SkillBook NewCharacterSkillBook(Skills.JobTable jobs, int jobGroup) => new()
    {
        Rank = 1,
        JobGroup = jobGroup,
        Skills = (jobs.GetJobData(jobGroup)?.Skills ?? Enumerable.Empty<Skills.JobSkillData>())
            .Select(r => new Ms2.Net.Packets.BookSkill((int)r.SkillId, 1, false, r.IsLearn, false)).ToList(),
    };

    /// <summary>The SkillBookTree answer for a character with no saved tabs: mode 0, one tab allowed, none sent.</summary>
    public static Ms2.Net.Packets.SkillBookTreePacket NoSkillTabs() => Ms2.Net.Packets.SkillBookTreePacket.Read(
        new Maple2.PacketLib.Tools.ByteReader(Ms2.Net.Packets.SkillBookTreePacket.WriteTabs(1, 0, Array.Empty<Ms2.Net.Packets.SkillTreeTab>())));
}
