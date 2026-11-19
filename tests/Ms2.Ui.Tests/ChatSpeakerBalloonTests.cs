using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Options;
using Ms2.Formats.Pack;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// A chat line over its speaker (UIMessengerBrowser::ShowChatBalloon, PCBase::ShowChatBalloon, PC::vfunc_85) on the
/// uipopupimageballoon movie (needs MS2_DATA).
/// </summary>
public class ChatSpeakerBalloonTests
{
    static PackSet packs;
    static MovieSource src;
    static GameTables tables;

    static UiHost Load(UiController c)
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = new PackSet(d);
            src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
            tables = new GameTables(packs, "en");
        }
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000, c.Libraries);
        p.StageW = 1920; p.StageH = 1080;
        var h = new UiHost(p, c, new UiModel(tables));
        Assert.That(h.Load(), Is.True, c.Movie + " load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 5; i++) p.Tick();
        return h;
    }

    [Test]
    public void TheChatTypesMapToTheirStatusOptions()
    {
        var s = new GameOption.Status(true);
        Assert.That(new[] { 0, 7, 8, 0x17 }.All(t => ChatSpeakerBalloon.TypeShows(s, t)), Is.True);
        Assert.That(new[] { 1, 2, 3, 4, 5, 0x12, 0x14 }.Any(t => ChatSpeakerBalloon.TypeShows(s, t)), Is.False);
        s.DisplayNormalChatBalloon = false;
        Assert.That(ChatSpeakerBalloon.TypeShows(s, 0), Is.False);
        s.DisplayGuildChatBalloon = false;
        Assert.That(ChatSpeakerBalloon.TypeShows(s, 8) || ChatSpeakerBalloon.TypeShows(s, 0x17), Is.False);
        Assert.That(ChatSpeakerBalloon.TypeShows(s, 7), Is.True);
    }

    [Test]
    public void ALineShowsOverItsSpeakerWithinTheDistance()
    {
        long now = 1000;
        var c = new ChatBalloonController { TickMs = () => now, ProgressiveLoading = true, Duration = 5000 };
        var h = Load(c);
        var me = new ChatBalloonController.Actor { ObjectId = 1, IsPc = true, IsMyPc = true, ProfileImage = "me.png" };
        var other = new ChatBalloonController.Actor { ObjectId = 2, IsPc = true, ProfileImage = "other.png", Distance = 100 };
        var status = new GameOption.Status(true);
        bool talking = false, haveMe = false;
        var turned = new List<int>();
        var s = new ChatSpeakerBalloon(c)
        {
            Distance = 2000,
            LocalPlayer = () => haveMe ? me : null,
            FindPc = n => n == "Me" ? me : n == "Other" ? other : null,
            Talking = () => talking,
            Status = () => status,
            Common = tables.Common,
            ReplaceValueWords = t => t.Replace("$MyPCName", "Me"),
            SpeakerTurn = a => turned.Add(a.ObjectId),
        };
        // no local player, or no PC of the name: not taken
        Assert.That(s.Show("Other", "hi", 0), Is.EqualTo(0));
        haveMe = true;
        Assert.That(s.Show("Nobody", "hi", 0), Is.EqualTo(0));
        Assert.That(c.Entities.Count, Is.EqualTo(0));
        // a normal line: the PC's balloon (no ProgressiveLoading test) with the value words replaced, started
        Assert.That(s.Show("Other", "hello $MyPCName", 0), Is.EqualTo(1));
        var e = c.PcBalloon(other);
        Assert.That(e, Is.Not.Null);
        Assert.That(c.Entities[2], Is.SameAs(e));
        Assert.That(e.Text, Is.EqualTo("hello Me"));
        Assert.That(e.Active && e.TimerRunning(now), Is.True);
        Assert.That(turned, Is.EqualTo(new[] { 2 }));
        c.Process(_ => (500f, 400f));
        Assert.That(e.Opens, Is.EqualTo(1));
        Assert.That(e.Opened?.ImgPath, Is.EqualTo("other.png"));
        Assert.That(e.Opened?.Npc, Is.False);
        // a party line: the 0xaa7 prefix; a guild line 0xaa8
        Assert.That(s.Show("Other", "p", 7), Is.EqualTo(1));
        Assert.That(e.Text, Is.EqualTo(tables.Common(ChatSpeakerBalloon.PartyPrefix) + "p"));
        Assert.That(s.Show("Other", "g", 8), Is.EqualTo(1));
        Assert.That(e.Text, Is.EqualTo(tables.Common(ChatSpeakerBalloon.GuildPrefix) + "g"));
        Assert.That(tables.Common(ChatSpeakerBalloon.PartyPrefix), Is.Not.Empty);
        // a guild 0x17 line has the option but no prefix
        Assert.That(s.Show("Other", "x", 0x17), Is.EqualTo(1));
        Assert.That(e.Text, Is.EqualTo("x"));
        // a type with no option, or its option off: not taken, the balloon untouched
        Assert.That(s.Show("Other", "w", 3), Is.EqualTo(0));
        status.DisplayNormalChatBalloon = false;
        Assert.That(s.Show("Other", "n", 0), Is.EqualTo(0));
        Assert.That(e.Text, Is.EqualTo("x"));
        status.DisplayNormalChatBalloon = true;
        // another PC at the distance or beyond: taken, not shown; the local player always
        other.Distance = 2000;
        Assert.That(s.Show("Other", "far", 0), Is.EqualTo(1));
        Assert.That(e.Text, Is.EqualTo("x"));
        me.Distance = 5000;
        Assert.That(s.Show("Me", "mine", 0), Is.EqualTo(1));
        var mine = c.PcBalloon(me);
        Assert.That(mine.Text, Is.EqualTo("mine"));
        Assert.That(mine, Is.Not.SameAs(e));
        // the own line in a talk: taken, not shown
        talking = true;
        Assert.That(s.Show("Me", "talk", 0), Is.EqualTo(1));
        Assert.That(mine.Text, Is.EqualTo("mine"));
        Assert.That(s.Show("Other", "near", 0), Is.EqualTo(1));       // another's line still checks its distance
        Assert.That(e.Text, Is.EqualTo("x"));
        // the PC's teardown releases its balloon to the pool; the next PC reuses the clip
        c.RemovePc(other);
        Assert.That(c.Entities.ContainsKey(2), Is.False);
        var third = new ChatBalloonController.Actor { ObjectId = 3, IsPc = true };
        Assert.That(c.PcBalloon(third), Is.SameAs(e));
        Assert.That(c.ClipsMade, Is.EqualTo(2));
        _ = h;
    }
}
