using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>UIItemMultiOpenBoxDialog's send timing and OnOpenResult (uiitemmultiopenboxdialog.cpp:137,160).</summary>
public class ItemMultiOpenBoxTests
{
    [Test] public void TheFirstSendIsDue500msAfterTheLoadAndCapsAtTheMaximum()
    {
        var b = new ItemMultiOpenBox(10);
        b.Open(77, 3, "k", 25); b.Loaded(1000, true);
        Assert.That(b.Tick(1500), Is.Null, "not before the delay has passed");
        var r = b.Tick(1501)!;
        Assert.That((r.ItemId, r.Slot, r.Count, r.Key), Is.EqualTo((77, (short) 3, 10, "k")));
        Assert.That(b.Tick(9999), Is.Null, "sent once");
    }

    [Test] public void NothingIsSentWhenTheBoxIsNotInTheBag()
    {
        var b = new ItemMultiOpenBox(10); b.Open(77, 3, "", 5); b.Loaded(0, false);
        Assert.That(b.Tick(100000), Is.Null);
    }

    [Test] public void AnOpenedAnswerCountsAndSchedulesTheNextBatch100msLater()
    {
        var b = new ItemMultiOpenBox(10); b.Open(77, 3, "", 25); b.Loaded(0, true); b.Tick(600);
        Assert.That(b.OnOpenResult(10, 2, 1000), Is.EqualTo(ItemMultiOpenBox.Outcome.Continue));
        Assert.That((b.Left, b.CountText), Is.EqualTo((15, (10, 25))));
        Assert.That(b.Tick(1100), Is.Null);
        Assert.That(b.Tick(1101)!.Count, Is.EqualTo(10));
    }

    [Test] public void TheLastOpenedAnswerClosesTheDialog()
    {
        var b = new ItemMultiOpenBox(10); b.Open(77, 3, "", 4); b.Loaded(0, true); b.Tick(600);
        Assert.That(b.OnOpenResult(4, 2, 1000), Is.EqualTo(ItemMultiOpenBox.Outcome.Close));
        Assert.That(b.Active, Is.False);
    }

    [Test] public void AFailureShowsItsBoxAndClosesAndAClosedDialogIgnoresAnAnswer()
    {
        var b = new ItemMultiOpenBox(10); b.Open(77, 3, "", 4); b.Loaded(0, true);
        Assert.That(b.OnOpenResult(0, 3, 10), Is.EqualTo(ItemMultiOpenBox.Outcome.ShowInventoryFailBox));
        Assert.That(b.Active, Is.False);
        Assert.That(b.OnOpenResult(1, 2, 20), Is.EqualTo(ItemMultiOpenBox.Outcome.Ignored));
        b.Open(77, 3, "", 4);
        Assert.That(b.OnOpenResult(0, 4, 10), Is.EqualTo(ItemMultiOpenBox.Outcome.ShowInventoryBox));
    }
}
