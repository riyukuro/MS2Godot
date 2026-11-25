using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>UICutSceneMovie::vfunc_52 (@0x14190ec00, uicutscenemovie.cpp:78-81) keeps its second argument as m_68, the time PlayUsm
/// seeks to after Play (:399). Its three callers pass 0.0f.</summary>
public class CutSceneMovieStartSecondsTests
{
    [Test]
    public void TheStartTimeIsKeptAndTheClientsCallersSendZero()
    {
        var movie = new CutSceneMovie(_ => { });
        movie.Start("Common/a.usm", 4);
        Assert.That(movie.StartSeconds, Is.EqualTo(0f));
        movie.Start("Common/a.usm", 4, 12.5f);
        Assert.That(movie.StartSeconds, Is.EqualTo(12.5f));
    }

    [Test]
    public void AnEarlierStartTimeDoesNotCarryOverToTheNextMovie()
    {
        var movie = new CutSceneMovie(_ => { });
        movie.Start("Common/a.usm", 4, 12.5f);
        movie.Start("Common/b.usm", 5);
        Assert.That(movie.StartSeconds, Is.EqualTo(0f));
    }
}
