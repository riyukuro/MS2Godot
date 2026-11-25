using Ms2.Formats.Options;
using NUnit.Framework;

namespace Ms2.Audio.Tests;

/// <summary>
/// CSoundEventMgr's volume fades (StartFadeOut @0x14177dea0, StartFadeIn @0x14177ddf0, UpdateFades @0x14177bbc0,
/// soundeventmgr.cpp:1063-1097, 1744-1764): the cut scene pane fades the bgm (2) and environment (1) categories for 1000 ms
/// (UICutSceneMovie::SetSoundFade @0x14190f270, uicutscenemovie.cpp:197-213).
/// </summary>
public class SoundFadeTests
{
    long now;
    SoundMix mix;
    SoundFades fades;

    [SetUp]
    public void SetUp()
    {
        now = 5000;
        mix = new SoundMix();
        mix.SetCategoryVolume(2, 0.8f);
        mix.SetCategoryVolume(1, 0.4f);
        fades = new SoundFades(mix, () => now);
    }

    [Test]
    public void AFadeOutLowersTheCategoryToZeroOverItsTime()
    {
        fades.StartFadeOut(2, 1000);
        fades.StartFadeOut(1, 1000);
        now += 250;
        Assert.That(fades.Update(), Is.True);
        Assert.That(mix.Volume(2), Is.EqualTo(0.6f).Within(1e-5f));
        Assert.That(mix.Volume(1), Is.EqualTo(0.3f).Within(1e-5f));
        Assert.That(mix.MusicVolume, Is.EqualTo(mix.Volume(2)), "slot 2 is the music volume");
        now += 749;
        fades.Update();
        Assert.That(mix.Volume(2), Is.EqualTo(0.8f * (1f - 0.999f)).Within(1e-5f));
        Assert.That(fades.Running, Is.True);
        now += 1;
        fades.Update();
        Assert.That(mix.Volume(2), Is.Zero);
        Assert.That(mix.Volume(1), Is.Zero);
        Assert.That(fades.Running, Is.False, "the fade is over and dropped");
        Assert.That(fades.Update(), Is.False, "nothing left to update");
    }

    [Test]
    public void AFadeInRaisesItToTheVolumeItHadAtTheStart()
    {
        mix.SetCategoryVolume(2, 0.8f);
        fades.StartFadeIn(2, 1000);
        mix.SetCategoryVolume(2, 0f);
        now += 500;
        fades.Update();
        Assert.That(mix.Volume(2), Is.EqualTo(0.4f).Within(1e-5f));
        now += 600;
        fades.Update();
        Assert.That(mix.Volume(2), Is.EqualTo(0.8f), "the end sets the stored volume");
        Assert.That(fades.Running, Is.False);
    }

    [Test]
    public void ANewFadeOfACategoryReplacesTheRunningOneAndTakesItsNowVolume()
    {
        fades.StartFadeOut(2, 1000);
        now += 500;
        fades.Update();
        Assert.That(mix.Volume(2), Is.EqualTo(0.4f).Within(1e-5f));
        fades.StartFadeIn(2, 1000);
        now += 500;
        fades.Update();
        Assert.That(mix.Volume(2), Is.EqualTo(0.2f).Within(1e-5f), "half of the 0.4 it had when the fade in started");
    }

    [Test]
    public void AFadeLeavesTheOtherCategoriesAndTheMuteAlone()
    {
        mix.SetCategoryVolume(0, 0.7f);
        mix.SetCategoryMute(1, true);
        fades.StartFadeOut(2, 1000);
        now += 1000;
        fades.Update();
        Assert.That(mix.Volume(0), Is.EqualTo(0.7f));
        Assert.That(mix.Muted(1), Is.True);
        Assert.That(mix.MusicMuted, Is.False);
    }

    [Test]
    public void AChangeOfTheMixCountsAsANewVersionSoThePlayersReapply()
    {
        int v = mix.Version;
        fades.StartFadeOut(1, 1000);
        Assert.That(mix.Version, Is.EqualTo(v), "starting changes nothing yet");
        now += 100;
        fades.Update();
        Assert.That(mix.Version, Is.GreaterThan(v));
    }
}
