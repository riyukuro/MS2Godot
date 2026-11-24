using Ms2.Formats.Text;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>JamoSearchText, the bag search's text form (FUN_1413fa9b0) and its C locale lower case.</summary>
public class JamoSearchTextTests
{
    // U+3131.. compatibility jamo, in the order of the syllable's initial (19), medial (21) and final (27) indices
    const string Initials = "\u3131\u3132\u3134\u3137\u3138\u3139\u3141\u3142\u3143\u3145\u3146\u3147\u3148\u3149\u314a\u314b\u314c\u314d\u314e";
    const string Medials = "\u314f\u3150\u3151\u3152\u3153\u3154\u3155\u3156\u3157\u3158\u3159\u315a\u315b\u315c\u315d\u315e\u315f\u3160\u3161\u3162\u3163";
    const string Finals = "\u3131\u3132\u3133\u3134\u3135\u3136\u3137\u3139\u313a\u313b\u313c\u313d\u313e\u313f\u3140\u3141\u3142\u3144\u3145\u3146\u3147\u3148\u314a\u314b\u314c\u314d\u314e";

    [Test]
    public void EverySyllableIsItsInitialMedialAndFinalJamo()
    {
        int wrong = 0; string first = null;
        for (int c = 0xac00; c <= 0xd7a3; c++)
        {
            int k = c - 0xac00;
            string want = $"{Initials[k / 588]}{Medials[k % 588 / 28]}" + (k % 28 > 0 ? Finals[k % 28 - 1].ToString() : "");
            string got = JamoSearchText.Of(((char)c).ToString());
            if (got != want) { wrong++; first ??= $"U+{c:X4}: {string.Join(" ", got.Select(x => ((int)x).ToString("X4")))}"; }
        }
        Assert.That(wrong, Is.Zero, first);
    }

    [Test]
    public void ALoneJamoAndAsciiStayThemselves()
    {
        foreach (char j in Initials + Medials + Finals) Assert.That(JamoSearchText.Of(j.ToString()), Is.EqualTo(j.ToString()), $"U+{(int)j:X4}");
        Assert.That(JamoSearchText.Of("Pebble 10"), Is.EqualTo("Pebble 10"));
    }

    [Test]
    public void ACharacterJohabLacksIsAQuestionMarkAndASymbolIsSplitByTheBits()
    {
        Assert.That(JamoSearchText.Of("\u00e9"), Is.EqualTo("?"));
        // the star (Johab D9 6A) and the multiplication sign (D9 4F) read as initial / medial / final fields
        Assert.That(JamoSearchText.Of("\u2605"), Is.EqualTo("\u3155\u313a"));
        Assert.That(JamoSearchText.Of("\u00d7"), Is.EqualTo("\u3154\u313f"));
    }

    [Test]
    public void TheLowerCaseIsAsciiOnly()
    {
        Assert.That(JamoSearchText.Lower("Big Pebble"), Is.EqualTo("big pebble"));
        Assert.That(JamoSearchText.Lower("\u00c9A"), Is.EqualTo("\u00c9a"));
    }
}
