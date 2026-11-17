using System.Text.RegularExpressions;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Formats.Tests;

/// <summary>CLoadingDescriptionTable::LoadXml resolves the tips' object words as it loads them.</summary>
public class LoadingTipTableTests
{
    static readonly Regex ObjectWord = new(@"\$(item|map|skill|npc|npcTitle|npcName|npcFunction|achieve|script|pp|pp2)(:[^$]*)?\$");

    [Test]
    public void TipsKeepNoObjectWords()
    {
        var data = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(data)) Assert.Ignore("MS2_DATA not set");
        var packs = new PackSet(data);
        foreach (var lang in new[] { "en", "kr" })
        {
            var text = new GameStrings(packs, lang);
            var raw = new LoadingTipTable(packs, text);
            var resolved = new LoadingTipTable(packs, text, new ReservedWords(text, null));
            var before = raw.Candidates(DateTime.Now).Count(t => ObjectWord.IsMatch(t));
            var left = resolved.Candidates(DateTime.Now).Where(t => ObjectWord.IsMatch(t)).ToList();
            TestContext.Out.WriteLine($"{lang}: {raw.Count} tips, {before} with object words before, {left.Count} after");
            Assert.That(resolved.Count, Is.EqualTo(raw.Count));
            Assert.That(left, Is.Empty);
        }
    }
}
