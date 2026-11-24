using Ms2.Formats.World;
using NUnit.Framework;

namespace Ms2.Audio.Tests;

/// <summary>CSoundEventEffectParam::vfunc_7 (soundeventmgr.cpp:360-363) over CFieldData::FUN_1403756f0 (fielddata.cpp:849-852).</summary>
public class MusicConcertSkipTests
{
    [Test]
    public void TheConcertIsMap2000064WithTheFeatureOn()
    {
        Assert.That(EffectPart.IsMusicConcert(2000064, true), Is.True);
        Assert.That(EffectPart.IsMusicConcert(2000064, false), Is.False, "feature 0x60 off");
        Assert.That(EffectPart.IsMusicConcert(2000062, true), Is.False, "another map");
    }
}
