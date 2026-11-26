#if MS2_DEVTOOLS
using Ms2.Devtools;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Quest;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2Godot.Tests;

/// <summary>
/// The item browser's enhancements: the spawn line, the client tooltip model built from the settings (the bag's own item
/// tooltip), the limits read from the client's tables, and the server help reply that says which options the live server
/// takes. Needs MS2_DATA; the GMS2 layout's option data is covered by the catalog test.
/// </summary>
[NonParallelizable]
public class ItemBrowserTests
{
    static PackSet packs;
    static GameTables tables;
    static UiModel model;

    static UiModel Model()
    {
        if (model != null) return model;
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        packs = new PackSet(d);
        tables = new GameTables(packs, "en");
        model = new UiModel(tables) { FeatureEnabledByName = f => LobbyFeatures.IsEnabled(packs, f) };
        return model;
    }

    [Test]
    public void TheSpawnLineCarriesEverySetting()
    {
        Assert.That(ItemSpawnCommand.Line(11400016, 1, 0, 1, new ItemBrowserSettings()), Is.EqualTo("/item 11400016 --amount 1"),
            "a plain spawn keeps the server's random options");
        var s = new ItemBrowserSettings { Enchant = 3 };
        s.BonusLines.Add(new ItemBrowserSettings.Bonus(false, 31, 50));
        Assert.That(ItemSpawnCommand.Line(11400016, 1, 0, 1, s),
            Is.EqualTo("/item 11400016 --amount 1 --enchant 3 --bonus-clear --stat 31=50"));

        var all = new ItemBrowserSettings { LimitBreak = 2, Sockets = 3, Gems = { [0] = 40200001, [2] = 40200003 } };
        all.BonusLines.Add(new ItemBrowserSettings.Bonus(true, 7, 3));
        all.BonusLines.Add(new ItemBrowserSettings.Bonus(false, 31, 50));
        Assert.That(ItemSpawnCommand.Line(11200006, 1, 4, 1, all),
            Is.EqualTo("/item 11200006 --amount 1 --rarity 4 --limit-break 2 --socket 3 --gem 40200001 --gem 0 --gem 40200003 --bonus-clear --special 7=3 --stat 31=50"));
    }

    [Test]
    public void TheTooltipIsTheClientsFromTheSettings()
    {
        Model();
        Assert.That(ItemSpawnCommand.Line(11400016, 1, 0, 1, new ItemBrowserSettings()), Is.EqualTo("/item 11400016 --amount 1"),
            "a plain spawn keeps the server's random options");
        var s = new ItemBrowserSettings { Enchant = 3 };
        s.BonusLines.Add(new ItemBrowserSettings.Bonus(false, 31, 50));
        Assert.That(ItemBrowserModel.Build(model, 11400016, s), Is.Not.Null);
        var (item, data) = ItemBrowserModel.Build(model, 11400016, s)!.Value;
        string name = tables.ItemName(11400016);
        Assert.That(data["itemName"], Is.EqualTo($"+3 {name}"), "the enchant level is in the name");
        string status = (string)data["itemStatus"];
        Assert.That(status, Does.Contain(tables.StatName(31)), "the Piercing line is in the status");
        Assert.That(status, Does.Contain(ClientPercent(31, 50)), "in the client's own unit");
        Assert.That(data["enablePuton"], Does.Contain(Common("s_msg_rclick_puton_equip")), "the bag's use hint");
        Assert.That(item.EnchantLevel, Is.EqualTo(3));
    }

    [Test]
    public void TheTooltipShowsTheGemsAndTheServerBlocks()
    {
        Model();
        var sock = new ItemBrowserSettings { Sockets = 3, Gems = { [0] = 40200001, [1] = 40200002, [2] = 40200003 } };
        var (_, data) = ItemBrowserModel.Build(model, 11200006, sock)!.Value;
        string socket = (string)data["itemSocket"];
        Assert.That(socket, Does.Contain(Common("s_tooltip_itemoption_socket").Split("{0}")[0]));
        var gem = tables.Items.Get(40200001);
        var constant = tables.ItemOptions.ConstantOptions(gem, gem.Rank)!.Value;
        if (constant.Stat.Count > 0) Assert.That(socket, Does.Contain(tables.StatName(constant.Stat.Keys.Min())), "the gem's own option");

        // The enchant and limit break options are the server's; a spawned item's record supplies them.
        var withEnchant = new ItemBrowserSettings { Enchant = 3, LimitBreak = 0 };
        var server = new EquipItem { Id = 11400016, Rarity = tables.Items.Get(11400016).Rank };
        server.Enchant.Level = 3;
        server.Enchant.Options[31] = 16;
        var (_, enchanted) = ItemBrowserModel.Build(model, 11400016, withEnchant, server)!.Value;
        Assert.That((string)enchanted["itemStatus"], Does.Contain(tables.StatName(31)), "the server's enchant option is shown");

        var limit = new ItemBrowserSettings { LimitBreak = 2 };
        var (_, plain) = ItemBrowserModel.Build(model, 11400016, limit)!.Value;
        var limitServer = new EquipItem { Id = 11400016, Rarity = tables.Items.Get(11400016).Rank };
        limitServer.LimitBreak.Level = 2;
        limitServer.LimitBreak.Stat[20] = 10;
        var (_, limited) = ItemBrowserModel.Build(model, 11400016, limit, limitServer)!.Value;
        string text = (string)limited["itemStatus"];
        Assert.That(text, Is.Not.EqualTo((string)plain["itemStatus"]), "the server's limit break block is used");
        Assert.That(text, Does.Contain(Common("s_tooltip_itemoption_unlimited").Split("{0}")[0]), "the unlimited enchant block");
    }

    [Test]
    public void TheStatusKeepsTheOptionsAndLimitBreakOfAnAdditionalEffectItem()
    {
        Model();
        // The Fire Prism Knife has additional effects (Power of Phantom Spacetime); the block is still built, with
        // the data's options, the browser's bonus line and the server's limit break rows.
        var s = new ItemBrowserSettings { LimitBreak = 2 };
        s.BonusLines.Add(new ItemBrowserSettings.Bonus(false, 31, 50));
        var server = new EquipItem { Id = 13100313, Rarity = 6 };
        server.LimitBreak.Level = 2;
        server.LimitBreak.Stat[4] = 195;
        server.LimitBreak.Stat[31] = 30;
        Assert.That(ItemBrowserModel.Build(model, 13100313, s, server), Is.Not.Null);
        var (_, data) = ItemBrowserModel.Build(model, 13100313, s, server)!.Value;
        string status = (string)data["itemStatus"];
        string unlimited = Common("s_tooltip_itemoption_unlimited").Split("{0}")[0];
        string random = Common("s_tooltip_itemoption_kinds_random").Split("{0}")[0];
        Assert.Multiple(() => {
            Assert.That(status, Does.Contain(unlimited), "the unlimited enchant block");
            Assert.That(status, Does.Contain("Limit Break Lv. 2"), "the label from the fallback language, not its marker");
            Assert.That(status, Does.Not.Contain("STRINGCOMMON_"), "the localisation marker is not shown");
            Assert.That(status, Does.Contain(tables.StatName(31)), "the limit break's Piercing row");
            Assert.That(status, Does.Contain(ClientPercent(31, 30)), "the limit break's rate in tenths");
            Assert.That(status, Does.Contain("195"), "the limit break's Health row");
            Assert.That(status, Does.Contain(random), "the browser's bonus line is shown as the random kind");
        });
    }

    [Test]
    public void TheLimitsAreTheClientsTablesOverEveryGearItem()
    {
        Model();
        var (gear, enchanted, limited, socketed, luaSockets) = SweepGear(packs, tables, model, "KMS");
        TestContext.Out.WriteLine($"KMS gear {gear}: enchantable {enchanted}, limit break {limited}, sockets {socketed} (Lua rule {luaSockets})");
        Assert.That(gear, Is.GreaterThan(0));
        Assert.That(enchanted, Is.GreaterThan(0));
        Assert.That(limited, Is.GreaterThan(0));
        Assert.That(socketed, Is.GreaterThan(0));
    }

    [Test]
    public void TheLimitsAreTheClientsTablesOverEveryGms2GearItem()
    {
        var d = Environment.GetEnvironmentVariable("MS2_GMS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_GMS2_DATA is not set");
        var gpack = new PackSet(d);
        var gtables = new GameTables(gpack, "en");
        var gmodel = new UiModel(gtables) { FeatureEnabledByName = f => LobbyFeatures.IsEnabled(gpack, f) };
        var (gear, enchanted, limited, socketed, luaSockets) = SweepGear(gpack, gtables, gmodel, "GMS2");
        TestContext.Out.WriteLine($"GMS2 gear {gear}: enchantable {enchanted}, limit break {limited}, sockets {socketed} (Lua rule {luaSockets})");
        Assert.That(gear, Is.GreaterThan(0));
        Assert.That(enchanted, Is.GreaterThan(0), "the GMS2 data has enchantable gear");
        Assert.That(limited, Is.GreaterThan(0), "the GMS2 data has limit-break gear");
    }

    /// <summary>
    /// Every gear item of a data set: the browser's enchant, limit break and socket maximums are in their tables' ranges,
    /// an enchantable item's grade is its slot's maxGrade and its level is inside the slot's band, a socket count from the
    /// Lua rule is an accessory at level 50 and up, and every stat range rises. Returns the counts for the test's log.
    /// </summary>
    static (int Gear, int Enchanted, int Limited, int Socketed, int LuaSockets) SweepGear(PackSet packs, GameTables tables, UiModel model, string label)
    {
        var catalog = ItemCatalog.Build(packs, tables);
        int gear = 0, enchanted = 0, limited = 0, socketed = 0, luaSockets = 0;
        foreach (var e in catalog.Entries)
        {
            if (tables.Items.Get(e.Id) is not { Type: 1 } d) continue;
            gear++;
            int enchant = ItemBrowserLimits.EnchantMax(tables, d);
            Assert.That(enchant, Is.InRange(0, ItemBrowserLimits.MaxEnchant), $"{label} {e.Id} enchant");
            if (enchant > 0)
            {
                enchanted++;
                var limit = tables.ItemOptions.EnchantLimitOf(d);
                Assert.That(limit, Is.Not.Null, $"{label} {e.Id} limit row");
                Assert.That((ushort)d.LevelLimit, Is.InRange(limit!.Value.MinLevel, limit.Value.MaxLevel), $"{label} {e.Id} level band");
                Assert.That(enchant, Is.EqualTo(Math.Min(limit.Value.MaxGrade, ItemBrowserLimits.MaxEnchant)), $"{label} {e.Id} max grade");
            }
            int lb = ItemBrowserLimits.LimitBreakMax(d);
            Assert.That(lb, Is.GreaterThanOrEqualTo(0), $"{label} {e.Id} limit break");
            if (lb > 0) limited++;
            int sockets = ItemBrowserLimits.SocketMax(tables, model.IsFeatureEnabled, d, e.Rank);
            Assert.That(sockets, Is.InRange(0, ItemBrowserLimits.MaxSockets), $"{label} {e.Id} sockets");
            if (sockets > 0) socketed++;
            var table = tables.ItemSockets.GetMaxSocketCount(d, e.Rank, model.IsFeatureEnabled);
            if (table == null && sockets > 0)
            {
                luaSockets++;
                Assert.That(d.SkinType, Is.Zero, $"{label} {e.Id}: the Lua rule gives a non-skin no sockets");
                Assert.That(d.Subtype, Is.AnyOf(12, 19, 20), $"{label} {e.Id}: the Lua rule gives only accessories sockets");
                Assert.That((ushort)d.LevelLimit, Is.GreaterThanOrEqualTo(50), $"{label} {e.Id}: the Lua rule needs level 50");
            }
            foreach (int stat in Enumerable.Range(0, ItemBrowserLimits.ClientStatCount))
                if (ItemBrowserLimits.StatRange(tables, d, e.Rank, stat) is var (first, last) && first != 0)
                    Assert.That(first, Is.LessThanOrEqualTo(last), $"{label} {e.Id} stat {stat} range");
        }
        return (gear, enchanted, limited, socketed, luaSockets);
    }

    [Test]
    public void TheServerHelpNamesTheOptionsItTakes()
    {
        string fork = "Usage:\n  item <id> [options]\n\nOptions:\n  -a, --amount <amount>\n  -r, --rarity <rarity>\n  --drop\n  --roll-max\n  --enchant <enchant>\n  --limit-break <limit-break>\n  --socket <socket>\n  --gem <gem>\n  --stat <stat>\n  --special <special>\n";
        var all = ItemCommandSupport.Parse(fork);
        Assert.Multiple(() => {
            Assert.That(all.Enchant && all.LimitBreak && all.Socket && all.Gem && all.Stat && all.Special, Is.True);
            Assert.That(all.Any, Is.True);
        });
        string upstream = "Usage:\n  item <id> [options]\n\nOptions:\n  -a, --amount <amount>\n  -r, --rarity <rarity>\n  --drop\n  --roll-max\n";
        var none = ItemCommandSupport.Parse(upstream);
        Assert.That(none.Any, Is.False);
        Assert.That(ItemCommandSupport.Parse("").Any, Is.False);
    }

    static string Common(string key) => tables.Common(key) ?? "";

    /// <summary>A stat's value as the client's tooltip prints it (FUN_140631600: a rate stat in tenths).</summary>
    static string ClientPercent(int stat, int value) =>
        ClientFormat.Format(tables.ItemOptions.StatValueText(stat), tables.ItemOptions.IsStatRate(stat) ? value / 10f : value);

    static string ClientFormatInt(int stat, int value) =>
        ClientFormat.Format(tables.ItemOptions.StatValueText(stat), value);
}
#endif
