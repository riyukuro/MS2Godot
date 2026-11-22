using System.Linq;
using Ms2.Avm2.Flash;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

[NonParallelizable]
public class TooltipGaugeTests
{
    [Test]
    public void TheGaugeBarIsUnderItsHeading()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var packs = new Ms2.Formats.Pack.PackSet(d);
        var m = new UiModel(new GameTables(packs, "en")) { FeatureEnabledByName = f => LobbyFeatures.IsEnabled(packs, f) };
        m.HasLocalPlayer = true;
        var gauge = new EquipItem { Id = 15000307, Uid = 12, Rarity = 4, Amount = 1 };
        gauge.Enchant.Level = 3; gauge.Enchant.Charge = 5000; gauge.Enchant.Version = 1;
        var tip = new Ms2.Ui.Items.ItemTooltip(m, 0x200);
        tip.Fill(Ms2.Ui.Hud.InventoryController.ToItem(gauge, m.Tables.Items));
        var p = TooltipTests.DrawStage(tip.Data);
        var fields = TooltipTests.TextFields(p.Stage).Where(t => t.Text.Length > 0).ToList();
        var status = fields.Single(t => t.name == "m_itemStatus");
        var bar = fields.Where(t => t.Text == "1 / 2").Select(t => t.parent.parent).Distinct().ToList();
        Assert.That(bar, Has.Count.EqualTo(1), "one gauge bar, in the tooltip");
        Assert.That(bar[0].parent, Is.SameAs(status.parent), "its parent is the status field's");
        // CommonTooltip.setItemStatus: the bar goes on the line the "gauge:" marker was on, just under the heading
        double dy = bar[0].y - status.y;
        Assert.That(dy, Is.InRange(18, 40), "bar y below the heading line");
        Assert.That(bar[0].x - status.x, Is.InRange(0, 10));
        Assert.That(status.htmlText.ToLowerInvariant(), Does.Contain("ffd200"), "the heading stays yellow");
    }
}
