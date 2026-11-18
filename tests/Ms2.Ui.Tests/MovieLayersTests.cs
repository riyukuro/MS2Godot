using Ms2.Ui;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

public class MovieLayersTests
{
    [Test]
    public void TheTalkHidesTheFieldLayersAndKeepsItsOwn()
    {
        var layers = new MovieLayers();
        layers.EnterTalk();
        foreach (var hidden in new[] { CommonDepth.NameTag, CommonDepth.HpBar, CommonDepth.Damage, CommonDepth.Dialog,
                     CommonDepth.ExpBar, CommonDepth.Default, CommonDepth.ChatBalloon, CommonDepth.UpperHud })
            Assert.That(layers.IsVisible(hidden), Is.False, $"layer {hidden}");
        foreach (var shown in new[] { CommonDepth.SystemText, CommonDepth.Tooltip, CommonDepth.Cutscene, CommonDepth.Cinematic,
                     CommonDepth.ScreenMask, CommonDepth.DamageEffect, CommonDepth.CinematicDialog, CommonDepth.Emoticon,
                     CommonDepth.MouseCursor, CommonDepth.AdventureLevelExpBar })
            Assert.That(layers.IsVisible(shown), Is.True, $"layer {shown}");
    }

    [Test]
    public void LeavingTheTalkShowsTheFieldLayersAgain()
    {
        var layers = new MovieLayers();
        var changes = 0;
        layers.Changed += (_, _) => changes++;
        layers.EnterTalk();
        layers.LeaveTalk();
        Assert.That(layers.IsVisible(CommonDepth.NameTag), Is.True);
        Assert.That(layers.IsVisible(CommonDepth.HpBar), Is.True);
        // The talk scene hides the emoticon layer as it closes, and the field shows it again as one of its own.
        Assert.That(layers.IsVisible(CommonDepth.Emoticon), Is.True);
        Assert.That(layers.IsVisible(CommonDepth.CinematicDialog), Is.True, "the field list leaves the talk's layer alone");
        Assert.That(changes, Is.GreaterThan(0));
    }

    [Test]
    public void TheAdventureLevelBarFollowsTheFieldOnlyWithItsFeature()
    {
        var layers = new MovieLayers { FeatureEnabled = id => id == Feature.AdventureLevel };
        layers.SetFieldLayersVisible(false);
        Assert.That(layers.IsVisible(CommonDepth.AdventureLevelExpBar), Is.False);
        var off = new MovieLayers();
        off.SetFieldLayersVisible(false);
        Assert.That(off.IsVisible(CommonDepth.AdventureLevelExpBar), Is.True);
    }
}
