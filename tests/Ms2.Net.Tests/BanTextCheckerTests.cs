using Ms2.Formats.Text;
using NUnit.Framework;

namespace Ms2.Net.Tests;

/// <summary>The client's name check over made-up word tables.</summary>
public class BanTextCheckerTests
{
    static string Table(params string[] names) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?><ms2>" + string.Concat(names.Select((n, i) => $"<key id=\"{i + 1}\" name=\"{n}\" />")) + "</ms2>";

    static BanTextChecker Checker() => new(file => file switch
    {
        "reserveIDAll.xml" => Table("Admin"),
        "reserveIDAny.xml" => Table("gm"),
        "banWord.xml" => Table("bad", "badword"),
        "startWithWord.xml" => Table("sys"),
        "banWordAll.xml" => Table("whole"),
        "ugcBanWord.xml" => Table("ugc"),
        _ => null,
    });

    [TestCase("Ab", 0)]
    [TestCase("A", 1)]
    [TestCase("", 1)]
    [TestCase("Abcdefghijkl", 0)]
    [TestCase("Abcdefghijklm", 2)]
    [TestCase("Ab cd", 3)]
    [TestCase(" Abcd", 3)]
    [TestCase("Ab_cd", 3)]
    [TestCase("\uac00\ub098\ub2e4\ub77c\ub9c8\ubc14", 0)]     // six Hangul syllables count as 12
    [TestCase("\uac00\ub098\ub2e4\ub77c\ub9c8\ubc14a", 2)]
    [TestCase("\u00c4bc", 3)]                                 // umlauts only for the German country
    public void Limit(string name, int expected) => Assert.That(Checker().CheckCharacterName(name, out _), Is.EqualTo(expected));

    [TestCase("admin", 5)]
    [TestCase("ADMIN", 5)]
    [TestCase("admins", 0)]
    [TestCase("TheGMx", 6)]
    [TestCase("Verybadword", 8)]
    [TestCase("Sysop", 8)]
    [TestCase("Opsys", 0)]
    [TestCase("Whole", 9)]
    [TestCase("Wholesome", 0)]
    [TestCase("ugcname", 0)]                                  // the UGC table is not in the name checks
    public void Tables(string name, int expected) => Assert.That(Checker().CheckCharacterName(name, out _), Is.EqualTo(expected));

    [Test]
    public void Messages()
    {
        var c = Checker();
        c.CheckCharacterName("A", out var m);
        Assert.That(m, Is.EqualTo("Enter at least 2 letters."));
        c.CheckCharacterName("Verybadword", out m);
        Assert.That(m, Is.EqualTo("Contains a forbidden word (badword)."));
        c.CheckCharacterName("Ab cd", out m);
        Assert.That(m, Is.EqualTo("Can't use spaces or special characters."));
        c.CheckCharacterName("Whole", out m);
        Assert.That(m, Is.EqualTo("This name cannot be used."));         // 0xdf has the limit bit: all_name
        Assert.That(c.CheckClubName("Whole", out m), Is.EqualTo(9));
        Assert.That(m, Is.EqualTo("This word cannot be used."));         // 0xd8 has not: all_word
    }

    [Test]
    public void MailTitleAndContentUseTheirOwnWordHitStrings()
    {
        var c = new BanTextChecker(file => file == "banWord.xml" ? Table("bad") : null, key => "<" + key + ">{0}");
        Assert.That(c.CheckMailTitleText("verybad", out var m), Is.EqualTo(8));
        Assert.That(m, Does.StartWith("<s_ban_check_title_err_any_word>"));
        Assert.That(c.CheckContentText("verybad", out m), Is.EqualTo(8));
        Assert.That(m, Does.StartWith("<s_ban_check_comment_err_any_word>"));
    }

    [Test]
    public void GermanUmlauts()
    {
        var c = Checker();
        c.ClientCountry = BanTextChecker.Country.Region2;
        Assert.That(c.CheckCharacterName("\u00c4bc", out _), Is.EqualTo(0));
    }
}
