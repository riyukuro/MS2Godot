using Ms2.Formats.Pack;
using Ms2.Ui;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The game pad kind (CMS2InputService::DetectGamePad @0x140d8be40) and its key table users: the pad default files
/// (FUN_141429e60, keytable.cpp:500-565) and the HUD key text (FUN_141431820, keytable.cpp:2478). Ids and structure only.</summary>
public class PadKindTests
{
    static KeyTable Keys()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        return new KeyTable(new PackSet(d), "en");
    }

    [TestCase(true, true, true, true, PadKinds.XInput)]
    [TestCase(true, true, false, true, PadKinds.Dual)]
    [TestCase(true, true, false, false, PadKinds.None)]
    [TestCase(false, true, true, true, PadKinds.None)]
    [TestCase(true, false, true, true, PadKinds.None)]
    public void DetectFollowsTheClientsOrder(bool enabled, bool feature, bool xinput, bool di, int expect) =>
        Assert.That(PadKinds.Detect(enabled, feature, xinput, di), Is.EqualTo(expect));

    [Test]
    public void KeyTextIsEmptyWhileThePadIsNotInUse()
    {
        var k = Keys();
        Assert.That(k.GamepadKeyText(0x96, false, 2), Is.EqualTo(""));
        Assert.That(k.GamepadKeyText(0x96, false, 1), Is.EqualTo(""));
    }

    [Test]
    public void DualPadReadsTheShoulderKeysAsL1L2R1R2AndNothingElse()
    {
        var k = Keys();
        Assert.That(new[] { 0x96, 0x97, 0x98, 0x99 }.Select(x => k.GamepadKeyText(x, true, 2)), Is.EqualTo(new[] { "L1", "L2", "R1", "R2" }));
        Assert.That(k.GamepadKeyText(0x9a, true, 2), Is.EqualTo(""));
    }

    [Test]
    public void OtherPadsReadTheKeysOwnName()
    {
        var k = Keys();
        Assert.That(k.GamepadKeyText(0x96, true, 1), Is.EqualTo(k.GetKeyName(0x96)));
        Assert.That(k.GamepadKeyText(0x96, true, 0), Is.EqualTo(k.GetKeyName(0x96)));
    }

    [TestCase(1)]
    [TestCase(2)]
    public void PadDefaultsTouchOnlyPadKeysAndAreSentToTheServer(int kind)
    {
        var k = Keys();
        var before = k.SnapshotBindings().ToDictionary(b => b.Key);
        int sent = 0;
        k.KeysSentToServer += _ => sent++;
        if (!k.LoadDefaultPadKeys(kind)) Assert.Fail("the pad default file is not in the pack");
        Assert.That(sent, Is.EqualTo(1));
        foreach (var b in k.SnapshotBindings())
            if (!KeyTable.IsGamepadKey(b.Key))
                Assert.That((b.Type, b.Code, b.Priority), Is.EqualTo((before[b.Key].Type, before[b.Key].Code, before[b.Key].Priority)), $"key {b.Key:x}");
        Assert.That(k.SnapshotBindings().Any(b => KeyTable.IsGamepadKey(b.Key) && b.Type != 0), "the file binds some pad key");
    }
}
