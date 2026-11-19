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

    /// <summary>
    /// table/feature.xml's names that are off: a level above the Live server type's, or one left at zero (which
    /// CFeatureManager::LoadFeature skips, featuremanager.cpp:804). The HUD's feature filter uses this set (StubHudData), so
    /// a feature the data leaves on (LapentaFrontierQuest, level 39 under Live) is not dropped by a hand-written list.
    /// </summary>
    [Test]
    public void DisabledIsTheTablesOffFeatures()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ms2-features-off-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "Resource"));
            var table = Path.Combine(dir, "Xml", "table");
            Directory.CreateDirectory(table);
            File.WriteAllText(Path.Combine(table, "feature_setting.xml"), "<ms2><setting type=\"Live\" KR=\"30\"/></ms2>");
            File.WriteAllText(Path.Combine(table, "feature.xml"),
                "<ms2><feature name=\"On\" KR=\"9\"/><feature name=\"Zero\" KR=\"0\"/><feature name=\"None\"/>" +
                "<feature name=\"Above\" KR=\"31\"/><feature name=\"Edge\" KR=\"30\"/></ms2>");
            var packs = new PackSet(dir);
            Assert.That(LobbyFeatures.Disabled(packs), Is.EquivalentTo(new[] { "Zero", "None", "Above" }));
            Assert.That(LobbyFeatures.IsEnabled(packs, "On"), Is.True);
            Assert.That(LobbyFeatures.IsEnabled(packs, "Edge"), Is.True, "a level equal to the server type's is on");
            Assert.That(LobbyFeatures.IsEnabled(packs, "Above"), Is.False);
            Assert.That(LobbyFeatures.IsEnabled(packs, "Zero"), Is.False);
            Assert.That(LobbyFeatures.IsEnabled(packs, "Missing"), Is.False, "an unknown name is off, as the client answers");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
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
