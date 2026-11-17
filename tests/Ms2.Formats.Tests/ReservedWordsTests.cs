using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>CReservedWordTable's scan and formatters, on made-up words and syllables.</summary>
public class ReservedWordsTests
{
    // syllables built from their code points: one open (no final consonant), one with final 1, one with final 8 (rieul)
    static readonly string Open = ((char)(0xAC00 + 28 * 3)).ToString();
    static readonly string Closed = ((char)(0xAC00 + 28 * 3 + 1)).ToString();
    static readonly string Rieul = ((char)(0xAC00 + 28 * 3 + 8)).ToString();

    static readonly Dictionary<string, ReservedWords.Word> Words = new()
    {
        ["$name"] = (_, a, _) => "<" + ReservedWords.Atoi(a) + ">",
        ["$nest"] = (_, _, _) => "[$name:7$]",
        ["$pp"] = ReservedWords.Ppr,
        ["$pp2"] = ReservedWords.Pp2,
    };

    [Test]
    public void ArgumentIsReadAsLeadingDigits()
    {
        Assert.That(ReservedWords.Replace("go to $name:11000001[gender:1]$ now", Words), Is.EqualTo("go to <11000001> now"));
        Assert.That(ReservedWords.TokenName("$name:12$"), Is.EqualTo("$name"));
        Assert.That(ReservedWords.TokenName("$MyPCName$"), Is.EqualTo("$MyPCName"));
        Assert.That(ReservedWords.TokenArgument("$name:12$"), Is.EqualTo("12"));
        Assert.That(ReservedWords.TokenArgument("$MyPCName$"), Is.EqualTo(""));
    }

    [Test]
    public void InsertedTextIsScannedAgain()
    {
        Assert.That(ReservedWords.Replace("a $nest$ b", Words), Is.EqualTo("a [<7>] b"));
    }

    [Test]
    public void UnknownTokenIsSkippedPastItsClosingDollar()
    {
        // "$x$" is unknown, so its closing "$" does not open the next token: "$ $name:3$" still resolves after it
        Assert.That(ReservedWords.Replace("$x$name:3$", Words), Is.EqualTo("$x$name:3$"));
        Assert.That(ReservedWords.Replace("$x$ $name:3$", Words), Is.EqualTo("$x$ <3>"));
        Assert.That(ReservedWords.Replace("cost 5$", Words), Is.EqualTo("cost 5$"));
    }

    [Test]
    public void JosaFollowsTheWordBefore()
    {
        Assert.That(ReservedWords.Replace("x" + Open + "$pp:A,B$ y", Words), Is.EqualTo("x" + Open + "A y"));
        Assert.That(ReservedWords.Replace("x" + Closed + "$pp:A,B$ y", Words), Is.EqualTo("x" + Closed + "B y"));
        // tags and closing brackets are not part of the word; a latin word reads as open
        Assert.That(ReservedWords.Replace("<b>x" + Closed + "</b>)$pp:A,B$", Words), Is.EqualTo("<b>x" + Closed + "</b>)B"));
        Assert.That(ReservedWords.Replace("abc$pp:A,B$", Words), Is.EqualTo("abcA"));
        // digits 2, 4, 5 and 9 read as open
        Assert.That(ReservedWords.Replace("5$pp:A,B$ 3$pp:A,B$", Words), Is.EqualTo("5A 3B"));
        // $pp2 counts a final rieul as open, $pp does not
        Assert.That(ReservedWords.Replace(Rieul + "$pp:A,B$", Words), Is.EqualTo(Rieul + "B"));
        Assert.That(ReservedWords.Replace(Rieul + "$pp2:A,B$", Words), Is.EqualTo(Rieul + "A"));
    }

    [Test]
    public void JosaWithoutAWordKeepsTheTokenWithoutItsDollars()
    {
        Assert.That(ReservedWords.Replace("$pp:A,B$ x", Words), Is.EqualTo("pp:A,B x"));
        Assert.That(ReservedWords.Replace("x" + Open + "$pp:A$", Words), Is.EqualTo("x" + Open + "pp:A"));
        // the text is split at spaces too, so a token with a space in it never matches a word
        Assert.That(ReservedWords.Replace("x" + Open + "$pp:A, B$", Words), Is.EqualTo("x" + Open + "pp:A, B"));
    }

    [Test]
    public void NumbersParseLikeTheCrt()
    {
        Assert.That(ReservedWords.Atoi(" -12ab"), Is.EqualTo(-12));
        Assert.That(ReservedWords.Atoi("x1"), Is.EqualTo(0));
        Assert.That(ReservedWords.ParseUInt64("0831180407000001"), Is.EqualTo(831180407000001UL));
        Assert.That(ReservedWords.StripTags("<a>b<c"), Is.EqualTo("b<c"));
    }
}
