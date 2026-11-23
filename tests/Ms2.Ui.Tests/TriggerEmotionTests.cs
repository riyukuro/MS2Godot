using Ms2.Ui.Guide;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>StateTriggerEmotion (stateemotionidle.cpp:384-460): the list mode plays each name once, the timed mode replays
/// the first until the duration has passed, and the flag lasts until the state is left.</summary>
public class TriggerEmotionTests
{
    [Test]
    public void ListModePlaysEachNameOnceThenIdle()
    {
        var s = new TriggerEmotion();
        Assert.That(s.Enter(new TriggerEmotionData(new[] { "A", "B", "C" }, 0, 0, 0, false), 1000), Is.EqualTo("A"));
        Assert.That(s.EndOfSequence(1500), Is.EqualTo("B"));
        Assert.That(s.EndOfSequence(99999), Is.EqualTo("C"));
        Assert.That(s.EndOfSequence(99999), Is.Null);
    }

    [Test]
    public void TimedModeReplaysTheFirstUntilTheDuration()
    {
        var s = new TriggerEmotion();
        Assert.That(s.Enter(new TriggerEmotionData(new[] { "A" }, 1, 1, 3000, true), 1000), Is.EqualTo("A"));
        Assert.That(s.Flag, Is.EqualTo(1));
        Assert.That(s.EndOfSequence(2000), Is.EqualTo("A"));
        Assert.That(s.EndOfSequence(3999), Is.EqualTo("A"));
        Assert.That(s.EndOfSequence(4000), Is.Null);
        s.Leave();
        Assert.That(s.Flag, Is.EqualTo(0));
    }

    [Test]
    public void EnteringAgainStartsTheNewListFromItsFirst()
    {
        var s = new TriggerEmotion();
        s.Enter(new TriggerEmotionData(new[] { "A", "B" }, 0, 0, 0, false), 0);
        s.EndOfSequence(10);
        Assert.That(s.Enter(new TriggerEmotionData(new[] { "C", "D" }, 0, 0, 0, false), 20), Is.EqualTo("C"));
        Assert.That(s.EndOfSequence(30), Is.EqualTo("D"));
        Assert.That(s.Enter(new TriggerEmotionData(System.Array.Empty<string>(), 0, 0, 0, false), 40), Is.Null);
    }
}
