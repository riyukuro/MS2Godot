using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ms2.Swf;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The login background's title events (<see cref="LoginTitleEvents"/>) over every login background movie in the data: the
/// title sprite (the movie's first placed sprite, Background.getContent) dispatches showTitle and then endShowTitle, the
/// latter on the title's last frame, where its frame script also stops it.
/// </summary>
public class LoginTitleEventsTests
{
    static Ms2.Formats.Pack.PackSet Data()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        return TestData.Packs(d);
    }

    /// <summary>The title of a background movie: the class and frame count of the root's first placed sprite.</summary>
    static (string Class, int Frames) TitleOf(SwfMovie m)
    {
        var place = m.Root.Frames[0].Select(o => o.Place).Where(p => p?.CharacterId != null).OrderBy(p => p.Depth).First();
        int id = place.CharacterId.Value;
        return (m.SymbolClasses.GetValueOrDefault(id), m.Characters[id].Timeline?.FrameCount ?? 1);
    }

    [Test]
    public void EveryLoginBackgroundsTitleShowsThenEndsOnItsLastFrame()
    {
        var packs = Data();
        var movies = TestData.Movies(packs);
        var keys = packs.List("Resource/Gfx").Where(n => n.EndsWith(".gfx", StringComparison.OrdinalIgnoreCase))
            .Select(n => Path.GetFileNameWithoutExtension(n).ToLowerInvariant())
            .Where(k => k.StartsWith("bg_", StringComparison.Ordinal) && k.Contains("login", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.That(keys, Is.Not.Empty);
        var wrong = new List<string>();
        foreach (var key in keys)
        {
            var (cls, frames) = TitleOf(movies.Movie(key));
            var events = LoginTitleEvents.Find(movies.Abc(key).Select(a => a.Abc), cls);
            TestContext.WriteLine($"{key} {cls} frames {frames}: {string.Join(", ", events)}");
            if (events.Count != 2 || events[0].Event != LoginTitleEvents.ShowTitle || events[1].Event != LoginTitleEvents.EndShowTitle
                || events[0].Frame >= events[1].Frame || events[1].Frame != frames - 1)
                wrong.Add($"{key}: {string.Join(", ", events)} of {frames} frames");
        }
        Assert.That(wrong, Is.Empty);
    }

    /// <summary>The Henesys title as the decompiled class has it: addFrameScript(0, frame1, 170, frame171, 319, frame320).</summary>
    [Test]
    public void TheHenesysTitleShowsOnFrame171AndEndsOn320()
    {
        var movies = TestData.Movies(Data());
        var (cls, _) = TitleOf(movies.Movie("bg_henesys_login"));
        Assert.That(LoginTitleEvents.Find(movies.Abc("bg_henesys_login").Select(a => a.Abc), cls),
            Is.EqualTo(new[] { (170, "showTitle"), (319, "endShowTitle") }));
        Assert.That(LoginTitleEvents.Find(movies.Abc("bg_henesys_login").Select(a => a.Abc), "NoSuchClass"), Is.Empty);
    }

    /// <summary>Each frame script runs once when the playhead reaches it; a jump past both runs both, in order.</summary>
    [Test]
    public void EachEventGoesOnceWhenItsFrameIsReached()
    {
        var events = new List<(int, string)> { (170, "showTitle"), (319, "endShowTitle") };
        var sent = new List<string>();
        int next = 0;
        foreach (int frame in new[] { 0, 169, 170, 171, 200, 319, 319, 0 })
            LoginTitleEvents.Due(events, ref next, frame, sent.Add);
        Assert.That(sent, Is.EqualTo(new[] { "showTitle", "endShowTitle" }));
        sent.Clear(); next = 0;
        LoginTitleEvents.Due(events, ref next, 319, sent.Add);
        Assert.That(sent, Is.EqualTo(new[] { "showTitle", "endShowTitle" }));
    }
}
