using System.Text.RegularExpressions;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The cut scene movie pane's swf movies (uicutscenemovie, needs MS2_DATA): UICutSceneMovie::PlaySwf calls the movie's
/// playSwfMovie(path), the movie's Loader loads the second movie, and at its last frame the movie calls onSwfStop (Exit).</summary>
public class CutSceneMovieSwfTests
{
    static readonly Regex SceneMovie = new(@"<action\s+name=""PlaySceneMovie""[^>]*?fileName=[""'](?<f>[^""']*\.swf)[""']", RegexOptions.IgnoreCase);
    internal static readonly string[] NotShipped = { "Royal_IshuraRemember.swf", "Starlight_expedition.swf", "Kritias_EpicCutScene_second_01.swf" };

    internal static (PackSet packs, MovieSource src) Open()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var packs = new PackSet(d);
        return (packs, TestData.Movies(packs));
    }

    internal static SortedSet<string> SwfPaths(PackSet packs)
    {
        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in packs.List("Xml").Where(n => n.StartsWith("trigger/", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".xml")))
        {
            var text = System.Text.Encoding.UTF8.GetString(packs.Read("Xml", n));
            if (!text.Contains(".swf", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (Match m in SceneMovie.Matches(text)) set.Add(m.Groups["f"].Value);
        }
        return set;
    }

    /// <summary>Opens the pane (the movie plus its controller) as the HUD does.</summary>
    internal static (CutSceneMovieController c, CutSceneMovie state, Runtime rt, Player p, List<IOutPacket> sent) Pane(MovieSource src, PackSet packs)
    {
        var sent = new List<IOutPacket>();
        var state = new CutSceneMovie(sent.Add);
        var c = new CutSceneMovieController(state);
        var model = new UiModel(new GameTables(packs, "en")) { HasLocalPlayer = true };
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        p.Translate = k => k.TrimStart('$');
        var host = new UiHost(p, c, model);
        Assert.That(host.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 5; i++) p.Tick();
        return (c, state, rt, p, sent);
    }

    [Test]
    public void ABlackstarMovieLoadsAndReachesItsEnd()
    {
        var (packs, src) = Open();
        var (c, state, rt, p, sent) = Pane(src, packs);
        Assert.That(c.Loaded, Is.True, "the movie's CutSceneMovie entity is there");
        state.Start("Cut_Blackstar_Crash.swf", 7);
        c.Play(state.Path);
        int ticks = 0;
        for (; ticks < 6000 && c.SwfStops == 0; ticks++) p.Tick();
        Assert.That(rt.Errors, Is.Empty);
        Assert.That(c.SwfStops, Is.EqualTo(1), $"the movie stopped itself after {ticks} frames");
        Assert.That(sent, Has.Count.EqualTo(1), "Exit with a cut scene id sends the trigger request");
        TestContext.Out.WriteLine($"Cut_Blackstar_Crash stopped after {ticks} frames");
    }

    /// <summary>Whole list: every swf movie the trigger scripts play loads into the pane's Loader, draws (the Loader holds a
    /// display list) and reaches its last frame, where the movie calls onSwfStop; the pane's Exit sends the request once. The
    /// ones the data set does not ship (NotShipped) load nothing: the client's loader gets an error and the pane waits.</summary>
    [Test]
    public void EveryTriggerSwfMovieLoadsAndReachesItsEnd()
    {
        var (packs, src) = Open();
        var paths = SwfPaths(packs);
        Assert.That(paths, Is.Not.Empty);
        var bad = new List<string>();
        foreach (var path in paths)
        {
            var gfx = Path.ChangeExtension(Path.GetFileName(path), ".gfx").ToLowerInvariant();
            var shipped = packs.FindByFileName("Resource/Gfx", gfx) != null;
            if (!shipped && !NotShipped.Contains(path, StringComparer.OrdinalIgnoreCase)) bad.Add($"{path}: not shipped and not in the known list");
            var (c, state, rt, p, sent) = Pane(src, packs);
            state.Start(path, 9);
            c.Play(path);
            int ticks = 0;
            for (; ticks < 20000 && c.SwfStops == 0; ticks++) p.Tick();
            var content = Rt.IsNullish(c.Host.Get(c.Host.Get(c.Host.Entity, "m_swfLoader"), "content")) ? "no content" : "content";
            TestContext.Out.WriteLine($"{path}: {(shipped ? "shipped" : "not shipped")}, stops {c.SwfStops} after {ticks} frames, {content}, errors {rt.Errors.Count}{(shipped ? $", movie {Ms2.Swf.SwfMovie.Parse(packs.Read("Resource/Gfx", packs.FindByFileName("Resource/Gfx", gfx))).FrameRate} fps, pane {p.FrameRate} fps" : "")}");
            if (!shipped) { if (c.SwfStops != 0) bad.Add($"{path}: not shipped but stopped"); continue; }
            if (c.SwfStops != 1) bad.Add($"{path}: no onSwfStop in {ticks} frames");
            else if (sent.Count != 1) bad.Add($"{path}: {sent.Count} sends");
            if (rt.Errors.Count > 0) bad.Add($"{path}: {rt.Errors.Count} errors, e.g. {rt.Errors[0]}");
        }
        Assert.That(bad, Is.Empty, string.Join("\n", bad));
    }

    /// <summary>Whole list (order D): every shipped swf cut scene has the stream sound the client's sound renderer plays: a
    /// SoundStreamHead (ADPCM or MP3) with one SoundStreamBlock per frame, and the decode is PCM as long as the blocks say, not silent.</summary>
    [Test]
    public void EveryTriggerSwfMovieHasAStreamSoundThatDecodes()
    {
        var (packs, _) = Open();
        var bad = new List<string>();
        int n = 0;
        foreach (var path in SwfPaths(packs))
        {
            var gfx = Path.ChangeExtension(Path.GetFileName(path), ".gfx").ToLowerInvariant();
            if (packs.FindByFileName("Resource/Gfx", gfx) is not { } e) continue;
            var movie = Ms2.Swf.SwfMovie.Parse(packs.Read("Resource/Gfx", e));
            var s = movie.SoundStream;
            if (s == null) { bad.Add($"{path}: no stream sound"); continue; }
            if (s.Codec != 1 && s.Codec != 2) bad.Add($"{path}: codec {s.Codec}");
            // one block per frame from the first block's frame (0 or 5 here) on; the last frames (at most 18 here, 0.3 s) carry none
            int first = s.Blocks[0].Frame, last = s.Blocks[^1].Frame;
            if (first is < 0 or > 5 || last - first + 1 != s.Blocks.Count || movie.FrameCount - (last + 1) is < 0 or > 60)
                bad.Add($"{path}: {s.Blocks.Count} blocks (frames {first}..{last}) for {movie.FrameCount} frames");
            var pcm = Ms2.Audio.SwfStreamDecoder.Decode(movie);
            double want = (double)(s.Blocks[0].Frame + s.Blocks.Count) * s.SamplesPerFrame / s.Rate;     // laid out from frame 0
            if (pcm == null) { bad.Add($"{path}: decodes to nothing"); continue; }
            int peak = pcm.Samples.Length == 0 ? 0 : pcm.Samples.Max(v => Math.Abs((int)v));
            if (Math.Abs(pcm.Seconds - want) > 0.25)     // an MP3 decode adds its frame padding
                bad.Add($"{path}: {pcm.Seconds:F2} s, the blocks say {want:F2} s");
            if (peak < 100) bad.Add($"{path}: silent (peak {peak})");
            n++;
            TestContext.Out.WriteLine($"{path}: {(s.Codec == 1 ? "ADPCM" : "MP3")} {s.Rate} Hz x{(s.Stereo ? 2 : 1)}, {s.Blocks.Count} blocks from frame {s.Blocks[0].Frame} of {movie.FrameCount} frames, {pcm.Seconds:F2} s, peak {peak}");
        }
        Assert.That(n, Is.GreaterThan(0));
        Assert.That(bad, Is.Empty, string.Join("\n", bad));
    }

    /// <summary>Whole list (order E): the client samples every DefineShape bitmap fill linearly, whatever the SWF's smooth flag says
    /// (FillStyleSwfReader::Read @0x14265adf0, sfr_gfx_08s2.cpp:457-459 maps 0x40..0x43 to Sample_Linear); only a morph shape's
    /// 0x42/0x43 stay Sample_Point (ReadMorphFillStyle @0x14265fbb0, sfr_gfx_08s3.cpp:385-386). The layout of every shipped swf cut
    /// scene therefore carries no point sampled fill. The point sampled fills made the movies blocky at a window below the
    /// 1920x1080 stage (the 1280x720 images are drawn at 0.698 at 1280x720).</summary>
    [Test]
    public void TheMovieVolumeIsZeroWhenMutedAndTheMovieStepTimesTheMainStepOtherwise()
    {
        var o = new Ms2.Formats.Options.GameOption.Sound { MainSoundStep = 8, MovieSoundStep = 5 };
        Assert.That(o.EffectiveMovieVolume, Is.EqualTo(0.4f).Within(1e-6));
        o.MuteAll = true;
        Assert.That(o.EffectiveMovieVolume, Is.EqualTo(0f));
        o.MuteAll = false; o.MovieSoundStep = 0;
        Assert.That(o.EffectiveMovieVolume, Is.EqualTo(0f));
    }
}
