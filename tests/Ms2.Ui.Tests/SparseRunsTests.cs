using Ms2.Ui;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

public class SparseRunsTests
{
    [Test]
    public void TheSlotsWrittenBeforeTheArrayFilledSurviveItsGrowth()
    {
        // one feed of 40 chars into an empty store, every char with a format: the array is full at 16 and grows, and the
        // chars before 16 (written by this same feed, so no count of earlier feeds covers them) must stay
        int[] runs = null;
        for (int i = 0; i < 40; i++) SparseRuns.Set(ref runs, i, 100 + i, 16);
        for (int i = 0; i < 40; i++) Assert.That(runs[i], Is.EqualTo(100 + i), "slot " + i);
    }

    [Test]
    public void SlotsOfAnEarlierFeedAndPlainSlotsBetweenAreKept()
    {
        int[] runs = null;
        SparseRuns.Set(ref runs, 3, 7, 16);          // the first feed: one styled char
        SparseRuns.Set(ref runs, 20, 8, 16);         // the next feed skips plain chars and grows the array
        Assert.That(runs[3], Is.EqualTo(7));
        Assert.That(runs[20], Is.EqualTo(8));
        Assert.That(runs[10], Is.EqualTo(0));
    }
}
