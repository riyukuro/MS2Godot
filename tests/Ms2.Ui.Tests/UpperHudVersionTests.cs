using Ms2.Avm2.Flash;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

public partial class HudTests
{
    /// <summary>
    /// A release build shows what the retail client shows in the upper HUD's version field: nothing while the watermark feature
    /// (0xc9) is off, the movie's own hidden text stays hidden. Before, the development client's unconditional block
    /// (uiupperhuddialog.cpp:316-322) showed it on every build.
    /// </summary>
    [Test]
    public void TheReleaseUpperHudKeepsTheVersionFieldHiddenWithoutTheWatermark()
    {
        var m = Model();
        m.Features.Remove(Feature.VersionText);
        var h = Load(new UpperHudController(), m);
        var v = h.FindNamed("m_version") as AsTextField;
        Assert.That(v, Is.Not.Null);
        Assert.That(v.visible, Is.False);
    }

    /// <summary>The watermark feature shows the field with s_upper_hud_version (uiupperhuddialog.cpp:306-315).</summary>
    [Test]
    public void TheWatermarkFeatureShowsTheVersionText()
    {
        var m = Model();
        m.Features.Add(Feature.VersionText);
        var h = Load(new UpperHudController(), m);
        var v = h.FindNamed("m_version") as AsTextField;
        Assert.That(v.visible, Is.True);
        string common = m.Tables.Common("s_upper_hud_version") ?? "";
        if (common.Length > 0) Assert.That(v.Text, Is.Not.EqualTo(""));
    }
}
