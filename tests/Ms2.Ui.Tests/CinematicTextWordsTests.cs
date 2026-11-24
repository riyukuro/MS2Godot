using System.Text.RegularExpressions;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The words in a cinematic talk's text (trigger string, "$key$" of the Cinematic packet). The tutorial's talk on map 52000113
/// reads "If $male:he,female:she$ is still alive": Rw_Gender (reservedwordtable.cpp:803) takes the local player's gender from the
/// world service, so the pane's translation has to know it (<see cref="ReservedWords.LocalPlayerGender"/>), not only the name.
/// </summary>
public class CinematicTextWordsTests
{
    const string Text = "If $male:he,female:she$ is still alive, $MyPCName$ will return.";

    [Test]
    public void GenderWordsTakeTheLocalPlayersGender()
    {
        var rw = new ReservedWords(null, null);
        // without a provider and no gender passed, the words are empty (no local player): the text the tutorial showed
        Assert.That(rw.ReplaceValueWords(Text, "Kim"), Is.EqualTo("If  is still alive, Kim will return."));
        int? gender = 1;
        rw.LocalPlayerGender = () => gender;
        Assert.That(rw.ReplaceValueWords(Text, "Kim"), Is.EqualTo("If she is still alive, Kim will return."));
        gender = 0;
        Assert.That(rw.ReplaceValueWords(Text, "Kim"), Is.EqualTo("If he is still alive, Kim will return."));
        // a gender passed in wins, as the npc talk's own call passes it
        Assert.That(rw.ReplaceValueWords(Text, "Kim", 1), Is.EqualTo("If she is still alive, Kim will return."));
        gender = null;
        Assert.That(rw.ReplaceValueWords(Text, "Kim"), Is.EqualTo("If  is still alive, Kim will return."));
    }

    /// <summary>Every trigger string of every data set: each value word the client's value table has (name, gender, particles) is
    /// gone after the replacement with a local player; any other "$word" left is listed, so a word that nothing handles shows.</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryTriggerStringsValueWordsAreFilled(string env)
    {
        var dir = System.Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) Assert.Ignore(env + " is not set");
        var packs = new PackSet(dir);
        var tables = new GameTables(packs, env == "MS2_DATA" ? "kr" : "en");
        var rw = tables.ReservedWords;
        string path = new[] { "kr", "en" }.Select(l => $"string/{l}/stringtrigger.xml").FirstOrDefault(p => packs.Exists("Xml", p));
        Assert.That(path, Is.Not.Null, env);
        var doc = GameStrings.Parse(packs.Read("Xml", path));
        int withGender = 0, withName = 0, total = 0;
        var left = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var bad = new List<string>();
        foreach (var key in doc.Root.Elements("key"))
        {
            string text = rw.ReplaceObjectWords(((string)key.Attribute("name") ?? "").Replace("\\n", "\n"));
            total++;
            bool g = text.Contains("$male") || text.Contains("$female"), n = text.Contains("$MyPCName");
            if (g) withGender++;
            if (n) withName++;
            foreach (int gender in new[] { 0, 1 })
            {
                rw.LocalPlayerGender = () => gender;
                string r = rw.ReplaceValueWords(text, "Kim");
                if (g && (r.Contains("$male") || r.Contains("$female"))) bad.Add($"{key.Attribute("id")}: gender word left: {r}");
                if (n && r.Contains("$MyPCName")) bad.Add($"{key.Attribute("id")}: name word left: {r}");
                if (gender == 0) foreach (Match m in Regex.Matches(r, @"\$[A-Za-z0-9]+")) left[m.Value] = left.GetValueOrDefault(m.Value) + 1;
            }
        }
        TestContext.Out.WriteLine($"{env}: {total} strings, {withGender} with $male/$female, {withName} with $MyPCName; other words left: " +
            string.Join(", ", left.Select(kv => $"{kv.Key} x{kv.Value}")));
        Assert.That(bad, Is.Empty);
    }
}
