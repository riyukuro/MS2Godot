using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Ui;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// UIReconnectTopLayerNotify (0x4f78, uireconnecttoplayernotify.cpp) run on its ActionScript movie across game data roots,
/// and DisconnectNotice (windowservice.cpp:482-555, reconnectionmanagercli.cpp:324) swept over every socket code and data root.
/// </summary>
public class ReconnectNotifyTests
{
    static IEnumerable<(string Label, string Dir, string Lang)> DataRoots()
    {
        var d1 = Environment.GetEnvironmentVariable("MS2_DATA");
        if (!string.IsNullOrEmpty(d1) && Directory.Exists(d1))
            yield return ("MS2_DATA", d1, RegionOf(d1));
        var d2 = Environment.GetEnvironmentVariable("MS2_GMS2_DATA");
        if (!string.IsNullOrEmpty(d2) && Directory.Exists(d2) && !string.Equals(d1, d2, StringComparison.Ordinal))
            yield return ("MS2_GMS2_DATA", d2, "en");
    }

    static string RegionOf(string dir)
    {
        using var p = new PackSet(dir);
        return p.Exists("Xml", "string/kr/stringcommon.xml") ? "kr" : "en";
    }

    static readonly int[] AllMappedSocketCodes =
    {
        0x2714, 0x271d, 0x271e, 0x2726, 0x2728, 0x2733, 0x2734, 0x2735, 0x2736, 0x2737, 0x2738,
        0x2739, 0x273a, 0x273b, 0x273c, 0x273d, 0x273e, 0x273f, 0x2740, 0x2741, 0x2742, 0x2743,
        0x2744, 0x2745, 0x2746, 0x2747, 0x2748, 0x2749, 0x274a, 0x274c, 0x274d, 0x2750, 0x2751,
        0x2753, 0x276b, 0x276c, 0x276d, 0x2775, 0x277d, 0x2af9, 0x2afa, 0x2afb, 0x2afc,
    };

    [Test]
    public void ReconnectTopLayerNotifyMovieLoadsUpdatesRetryCountAndCancelsOnEveryDataRoot()
    {
        var roots = DataRoots().ToList();
        if (roots.Count == 0) Assert.Ignore("MS2_DATA is not set");
        foreach (var (label, dir, lang) in roots)
        {
            using var packs = new PackSet(dir);
            var src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
            var model = new UiModel(new GameTables(packs, lang));
            var c = new ReconnectTopLayerNotifyController();
            c.SetRetryCount(2, 5);
            var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
            p.StageW = 1280; p.StageH = 720;
            p.Translate = k => model.Tables.Common(k.TrimStart('$')) ?? k;
            var h = new UiHost(p, c, model);
            Assert.That(h.Load(), Is.True, $"{label} load: " + string.Join("; ", rt.Errors));
            for (int i = 0; i < 4; i++) p.Tick();
            Assert.That(rt.Errors, Is.Empty, label);
            Assert.That((c.Retry, c.MaxRetries), Is.EqualTo((2, 5)), label);

            c.SetRetryCount(4, 5);
            p.Tick();
            Assert.That(rt.Errors, Is.Empty, label);
            Assert.That(c.Retry, Is.EqualTo(4), label);

            int cancelled = 0;
            c.Cancelled += () => cancelled++;
            Assert.That(c.OnCallback("onCancel", Array.Empty<object>()), Is.True, label);
            Assert.That(cancelled, Is.EqualTo(1), label);
        }
    }

    [Test]
    public void DisconnectNoticeMapsAllClientSocketCodesAndSweepsStringsAcrossDataRoots()
    {
        Assert.That(AllMappedSocketCodes.Length, Is.EqualTo(43));
        foreach (int code in AllMappedSocketCodes)
            Assert.That(DisconnectNotice.ReasonStringId(code), Is.InRange(0xd28, 0xd43), $"code 0x{code:x}");
        Assert.That(DisconnectNotice.ReasonStringId(999), Is.EqualTo(0));

        string Fake(int id) => id switch
        {
            0xd37 => "Socket closed",
            DisconnectNotice.DisconnectFormatId => "Disconnected ({0})",
            DisconnectNotice.BypassDisconnectFormatId => "Launcher disconnected ({0})",
            _ => "",
        };
        Assert.That(DisconnectNotice.FormatDisconnect(0x2745, Fake, false), Is.EqualTo("Disconnected (Socket closed : 10053)"));
        Assert.That(DisconnectNotice.FormatDisconnect(0x2745, Fake, true), Is.EqualTo("Launcher disconnected (Socket closed : 10053)"));
        Assert.That(DisconnectNotice.FormatDisconnect(200, Fake, false), Is.EqualTo("Disconnected (200)"));
        Assert.That(DisconnectNotice.FormatSessionEnded(Fake), Is.EqualTo("Disconnected (0)"));

        var roots = DataRoots().ToList();
        if (roots.Count == 0) Assert.Ignore("MS2_DATA is not set");
        foreach (var (label, dir, lang) in roots)
        {
            using var packs = new PackSet(dir);
            var tables = new GameTables(packs, lang);
            string ById(int id) => CommonStringIds.Key(id) is { } k ? tables.Common(k) : null;
            Assert.That(ById(DisconnectNotice.DisconnectFormatId), Is.Not.Empty, $"{label}: 0x20f");
            Assert.That(DisconnectNotice.FormatSessionEnded(ById), Does.Contain("0"), $"{label}: session ended");
            foreach (int code in AllMappedSocketCodes)
            {
                int sid = DisconnectNotice.ReasonStringId(code);
                Assert.That(CommonStringIds.Key(sid), Is.Not.Null.And.Not.Empty, $"{label}: key for 0x{sid:x}");
                string msg = DisconnectNotice.FormatDisconnect(code, ById);
                Assert.That(msg, Does.Contain(code.ToString(System.Globalization.CultureInfo.InvariantCulture)), $"{label}: 0x{code:x}");
            }
        }
    }
}
