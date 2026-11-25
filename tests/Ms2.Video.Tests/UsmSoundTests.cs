using System.Security.Cryptography;
using Ms2.Audio;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Video.Tests;

/// <summary>
/// The sound of the shipped usm movies (the cut scene movie plays it at the movie volume, UICutSceneMovie::PlayUsm @0x14190ffc0,
/// uicutscenemovie.cpp:392-404): the movie's @SFA stream decodes with <see cref="AdxDecoder"/> to the same samples as ffmpeg's ADX
/// decoder (the expected digests are of ffmpeg's 16-bit little endian output, cut to the header's sample count).
/// These use the new decoder API, so they cannot compile on the code before it.
/// </summary>
public class UsmSoundTests
{
    static readonly string[] Archives = { "Resource/Movie/Common", "Resource/Movie/Item", "Resource/Movie/Emotion" };

    /// <summary>(file, samples per channel, SHA-256 of the interleaved little endian samples): the restore data's movies
    /// and the GMS2 data's are different recordings of the same movies, so each file has one digest per data set; a data set under
    /// test has to match one of them (the gate runs the restore data as MS2_DATA, then the GMS2 data as MS2_DATA).</summary>
    static readonly (string file, int frames, string[] sha)[] Movies =
    {
        ("Common_Opening.usm", 8406048, new[] { "4E2B112D2189EE4FADC46CBF08C907352E82C54DCD692974DCC9BEE4189C0844", "A5059760ADA42A632331CEA8E72031A18AD184BD4778550D0B585837DFFB3D3F" }),
        ("JobIntro_Assassin.usm", 2338829, new[] { "512CEBB56B9637BBC096F1C7BD21787869B0CF34F06828CC91A366D769249774", "24BAF212501FBFF5AEA0FE06FB1BA1130714E03DF753EA1DF5C35F5B8E7B9E66" }),
    };

    static IEnumerable<(string env, string data)> DataSets()
    {
        foreach (var env in new[] { "MS2_DATA", "MS2_GMS2_DATA" })
        {
            var data = Environment.GetEnvironmentVariable(env);
            if (!string.IsNullOrEmpty(data) && File.Exists(Path.Combine(data, "Resource/Movie/Common.m2d"))) yield return (env, data);
        }
    }

    [Test]
    public void TheShippedMoviesDecodeLikeFfmpeg()
    {
        var any = false;
        foreach (var (env, data) in DataSets())
        {
            using var packs = new PackSet(data);
            foreach (var (file, frames, sha) in Movies)
            {
                UsmVideo video = null;
                foreach (var archive in Archives)
                {
                    var entry = packs.FindByFileName(archive, file);
                    if (entry != null) { video = UsmVideo.Open(packs.Read(archive, entry)); break; }
                }
                if (video == null) continue;
                any = true;
                Assert.That(video.HasAudio, Is.True, $"{env} {file}");
                var pcm = AdxDecoder.Decode(video.File.Audio[0].Data);
                Assert.That(pcm, Is.Not.Null, $"{env} {file}");
                Assert.That((pcm.Rate, pcm.Channels, pcm.Frames), Is.EqualTo((44100, 2, frames)), $"{env} {file}");
                Assert.That(sha, Does.Contain(Convert.ToHexString(SHA256.HashData(pcm.ToBytes()))), $"{env} {file}");
            }
        }
        if (!any) Assert.Ignore("no data set with the movie archives");
    }
}
