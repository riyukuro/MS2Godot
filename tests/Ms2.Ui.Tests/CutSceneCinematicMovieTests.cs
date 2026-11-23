using Maple2.PacketLib.Tools;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The cinematic pane's movie (uicutscenecinematic, needs MS2_DATA) with the controller's calls: every type 1 to 12 reaches
/// the movie's own ActionScript (setCinematic, setSkip, setScript, hideScript, setCaption, setOpening, setIntro, Close) and the
/// movie raises no runtime error.</summary>
public class CutSceneCinematicMovieTests
{
    static PackSet packs;
    static MovieSource src;

    static void Send(CutSceneCinematic c, Action<ByteWriter> f)
    {
        var w = new ByteWriter();
        f(w);
        c.Apply(CinematicPacket.Read(new ByteReader(w.Buffer.AsSpan(0, w.Length).ToArray())));
    }

    [Test]
    public void EveryTypeRunsOnTheMoviesActionScript()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = new PackSet(d);
            src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
        }
        long now = 1000;
        var state = new CutSceneCinematic { Clock = () => now, PlayerName = () => "me", NpcName = id => "npc" + id };
        var c = new CutSceneCinematicController(state);
        var model = new UiModel(new GameTables(packs, "en")) { HasLocalPlayer = true };
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        p.Translate = k => k.TrimStart('$');
        var host = new UiHost(p, c, model);
        Assert.That(host.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 10; i++) p.Tick();
        Assert.That(c.Loaded, Is.True, "the movie's CutSceneCinematic entity is there");

        Send(state, w => { w.WriteByte(1); w.WriteBool(true); });
        Send(state, w => { w.WriteByte(3); w.WriteInt(4); w.WriteUnicodeString("title"); w.WriteUnicodeString(""); });
        Send(state, w => { w.WriteByte(4); w.WriteByte(2); w.WriteString(""); });
        Send(state, w => { w.WriteByte(6); w.WriteInt(11001217); w.WriteString("11001217"); w.WriteUnicodeString("hello"); w.WriteInt(2000); w.WriteByte(1); });
        state.Update(now);
        for (int i = 0; i < 5; i++) p.Tick();
        now += 2500; state.Update(now);
        Send(state, w => { w.WriteByte(7); });
        Send(state, w =>
        {
            w.WriteByte(10); w.WriteUnicodeString("11"); w.WriteUnicodeString("one"); w.WriteUnicodeString("two");
            w.WriteUnicodeString("6"); w.WriteInt(3500); w.WriteFloat(-0.15f); w.WriteFloat(0.15f); w.WriteFloat(2f);
        });
        Send(state, w => { w.WriteByte(11); w.WriteUnicodeString("open"); w.WriteBool(true); });
        Send(state, w => { w.WriteByte(12); w.WriteUnicodeString("intro"); });
        Send(state, w => { w.WriteByte(5); });
        Send(state, w => { w.WriteByte(2); });
        for (int i = 0; i < 10; i++) p.Tick();
        Assert.That(rt.Errors, Is.Empty, "the movie took the calls");
    }
}
