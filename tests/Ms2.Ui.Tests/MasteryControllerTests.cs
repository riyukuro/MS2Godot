using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>UIMasteryDialog's slot and type mapping (GetTypeForSlot) and the craft box's required items.</summary>
public class MasteryControllerTests
{
    [TestCase(0, 3), TestCase(1, 5), TestCase(2, 4), TestCase(3, 6), TestCase(4, 8), TestCase(5, 7), TestCase(6, 9),
     TestCase(7, 10), TestCase(8, 0), TestCase(9, 0xb), TestCase(0xe, 0)]
    public void TypeForSlotFollowsTheClient(int slot, int type) => Assert.That(MasteryController.TypeForSlot(slot), Is.EqualTo(type));

    [Test]
    public void CraftBoxKeepsItsRequiredItems()
    {
        var box = new MessageBoxData(0x23);
        box.SetRequiredItems(new[] { (1, 0, 2), (3, 1, 4) });
        Assert.That(box.RequiredItems, Is.EqualTo(new[] { (1, 0, 2), (3, 1, 4) }));
        box.SetRequiredItems(new[] { (5, 0, 1) });
        Assert.That(box.RequiredItems, Has.Count.EqualTo(1));
    }
}
