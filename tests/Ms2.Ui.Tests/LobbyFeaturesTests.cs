using Ms2.Formats.Pack;
using Ms2.Ui;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

public class LobbyFeaturesTests
{
    // a client folder with loose feature tables only (the client reads a loose Xml/table file before the pack entry)
    static string Client(string root, int server, int chat)
    {
        Directory.CreateDirectory(Path.Combine(root, "Resource"));
        var table = Path.Combine(root, "Xml", "table");
        Directory.CreateDirectory(table);
        File.WriteAllText(Path.Combine(table, "feature_setting.xml"), $"<ms2><setting type=\"Live\" KR=\"{server}\"/></ms2>");
        File.WriteAllText(Path.Combine(table, "feature.xml"), $"<ms2><feature name=\"Chat\" KR=\"{chat}\"/></ms2>");
        return root;
    }

    [Test]
    public void EachClientFolderKeepsItsOwnLevels()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ms2-features-" + Guid.NewGuid().ToString("N"));
        try
        {
            var on = new PackSet(Client(Path.Combine(dir, "on"), server: 3, chat: 2));
            var off = new PackSet(Client(Path.Combine(dir, "off"), server: 1, chat: 2));
            Assert.That(LobbyFeatures.IsEnabled(on, "Chat"), Is.True);
            Assert.That(LobbyFeatures.IsEnabled(off, "Chat"), Is.False, "the second client's server level, not the first's");
            Assert.That(LobbyFeatures.LevelOf(off, "Chat"), Is.EqualTo(2));
            Assert.That(LobbyFeatures.Stamp(on), Is.Not.EqualTo(LobbyFeatures.Stamp(off)));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
