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

    /// <summary>A KeyTable packet in the server's layout (Ms2.Net ServerKeyTable): mode 0 with the use-default flag, which
    /// makes the client load DefaultKey.xml and read no pages (OnKeyTable, keytable.cpp:285), so every quick slot is empty.</summary>
    public static Ms2.Net.Packets.ServerKeyTable NewCharacterKeyTable()
    {
        var t = new Ms2.Net.Packets.ServerKeyTable();
        t.Apply(new Maple2.PacketLib.Tools.ByteReader(Ms2.Net.Packets.ServerKeyTable.Write(0, true, null, 0, null)));
        return t;
    }
}
