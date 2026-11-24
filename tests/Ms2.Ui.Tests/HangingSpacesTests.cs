using Ms2.Ui;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

public class HangingSpacesTests
{
    // every char 10 wide; a rect of 40 holds four chars
    static double W(int a, int b) => (b - a) * 10;

    [Test]
    public void AWordThatFitsWithoutItsTrailingSpaceMovesBackToTheLineBeforeIt()
    {
        string t = "abc de f";           // lines the engine returns: "abc " (4 = 40) | "de f"
        int[] engine = { 0, 4, 4, 8 };
        // "abc de" would be 6 chars = 60 > 40: nothing moves
        Assert.That(HangingSpaces.Fit(engine, i => t[i] == ' ', W, 40), Is.EqualTo(engine));
        // limit 60: "abc de" fits without the space after "de", the engine broke before it (the space made 70)
        Assert.That(HangingSpaces.Fit(engine, i => t[i] == ' ', W, 60), Is.EqualTo(new[] { 0, 7, 7, 8 }));
    }

    [Test]
    public void ALineEmptiedByTheMoveIsDropped()
    {
        string t = "abc de";
        int[] engine = { 0, 4, 4, 6 };
        Assert.That(HangingSpaces.Fit(engine, i => t[i] == ' ', W, 60), Is.EqualTo(new[] { 0, 6 }));
    }

    [Test]
    public void AWordThatDoesNotFitStaysOnItsLine()
    {
        string t = "abc defgh";
        int[] engine = { 0, 4, 4, 9 };
        Assert.That(HangingSpaces.Fit(engine, i => t[i] == ' ', W, 60), Is.EqualTo(engine));
    }

    [Test]
    public void ALineWithoutATrailingSpaceIsLeftAlone()
    {
        string t = "abcdefgh";            // broken inside a word
        int[] engine = { 0, 4, 4, 8 };
        Assert.That(HangingSpaces.Fit(engine, i => t[i] == ' ', W, 40), Is.EqualTo(engine));
    }

    [Test]
    public void AnEngineThatTrimsTheEdgeSpacesIsHandledTheSame()
    {
        string t = "abc de f";           // the lines "abc" [0,3) and "de f" [4,8): the space between is trimmed
        int[] engine = { 0, 3, 4, 8 };
        Assert.That(HangingSpaces.Fit(engine, i => t[i] == ' ', W, 60), Is.EqualTo(new[] { 0, 6, 7, 8 }));
    }
}
