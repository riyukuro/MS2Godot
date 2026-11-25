using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>UICutSceneMovie::SetSoundFade (@0x14190f270, uicutscenemovie.cpp:197-213) runs from vfunc_52 (start, true) and vfunc_53
/// (stop, false) and acts only when m_bSoundFaded changes.</summary>
public class CutSceneMovieSoundFadeTests
{
    static (CutSceneMovie movie, List<bool> fades) Pane()
    {
        var fades = new List<bool>();
        return (new CutSceneMovie(_ => { }) { SoundFade = fades.Add }, fades);
    }

    [Test]
    public void StartFadesTheSoundOutAndStopFadesItBackIn()
    {
        var (movie, fades) = Pane();
        movie.Start("Common/a.usm", 4);
        Assert.That(fades, Is.EqualTo(new[] { true }));
        Assert.That(movie.SoundFaded, Is.True);
        movie.Stop();
        Assert.That(fades, Is.EqualTo(new[] { true, false }));
        Assert.That(movie.SoundFaded, Is.False);
    }

    [Test]
    public void AStopWithoutAFadeDoesNothing()
    {
        var (movie, fades) = Pane();
        movie.Stop();
        movie.Start("a.usm", 1);
        movie.Stop();
        movie.Stop();
        Assert.That(fades, Is.EqualTo(new[] { true, false }), "m_bSoundFaded guards both ways");
    }

    [Test]
    public void ASecondStartStopsTheFirstSoTheSoundFadesBackInAndOutAgain()
    {
        var (movie, fades) = Pane();
        movie.Start("a.usm", 1);
        movie.Start("b.usm", 2);
        Assert.That(fades, Is.EqualTo(new[] { true, false, true }));
    }

    [Test]
    public void AnEmptyPathFadesNothing()
    {
        var (movie, fades) = Pane();
        movie.Start("", 1);
        Assert.That(fades, Is.Empty);
    }

    [Test]
    public void TheStopOfTheCutSceneIdAndTheZeroIdSkipEndTheFadeToo()
    {
        var (movie, fades) = Pane();
        movie.Start("a.usm", 7);
        movie.StopIfCutScene(9);
        Assert.That(fades, Is.EqualTo(new[] { true }), "another cut scene's stop leaves it");
        movie.StopIfCutScene(7);
        Assert.That(fades, Is.EqualTo(new[] { true, false }));
        movie.Start("b.usm", 0);
        movie.Exit();
        Assert.That(fades, Is.EqualTo(new[] { true, false, true, false }));
    }
}
