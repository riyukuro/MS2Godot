using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Net.Packets;
using Ms2.Quest;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The recall notices (UIRecallScrollNotifyDialog, unit_14129ba60_recallscrollnotifydialog.cpp) run by their movie: the
/// RecallScroll packet's open and close, the countdown, the click's confirm box and its yes (needs MS2_DATA).</summary>
public class RecallScrollNotifyTests
{
    static PackSet packs;
    static MovieSource src;
    static GameTables tables;

    sealed class Rig
    {
        public RecallScrollNotifyController C;
        public UiHost H;
        public Runtime RT;
        public Player P;
        public long Now = 1_700_000_000;
        public ulong Ms = 10_000;
        public bool InField = true;
        public readonly HashSet<int> Fields = new() { 2000001 };
        public readonly List<(string Text, int Type, bool Log)> Notices = new();
        public readonly List<string> Sounds = new();
        public readonly List<MessageBoxData> Boxes = new();
    }

    static Rig New()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        if (packs == null)
        {
            packs = TestData.Packs(d);
            src = TestData.Movies(packs);
            tables = new GameTables(packs, "en");
        }
        var g = new Rig();
        g.C = new RecallScrollNotifyController
        {
            ServerNow = () => g.Now,
            NowMs = () => g.Ms,
            InField = () => g.InField,
            FieldExists = g.Fields.Contains,
            MapName = id => tables.MapName(id) ?? "",
            CommonById = id => MessageBoxData.Common(id),
            CommonByKey = key => tables.Common(key) ?? "",
            Notice = (t, ty, l) => g.Notices.Add((t, ty, l)),
            PlaySound = g.Sounds.Add,
            ShowBox = g.Boxes.Add,
        };
        return g;
    }

    static void Load(Rig g)
    {
        var (rt, p) = src.Open(g.C.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1920; p.StageH = 1080;
        g.RT = rt; g.P = p;
        g.H = new UiHost(p, g.C, new UiModel(tables));
        Assert.That(g.H.Load(), Is.True, g.C.Movie + " load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 5; i++) p.Tick();
    }

    static RecallScrollPacket.Open Open(byte type, long expiry, string name = "Caller", int field = 2000001) =>
        new(1, type, name, 0, field, 0, expiry);

    bool Shown(Rig g, string clip) => Equals(g.H.Get(g.H.Child(clip), "visible"), true);

    [Test]
    public void AnOpenKeepsTheSlotThenShowsTheTypesNoticeAndPlaysTheSound()
    {
        var g = New();
        g.C.OnPacket(Open(1, g.Now + 125));
        g.C.OnPacket(Open(2, g.Now + 60, "Partner", 2000002));
        g.C.OnPacket(Open(0, g.Now + 30, "Master"));
        g.C.OnPacket(Open(7, g.Now + 30, "Nobody"));
        Assert.That((g.C.Time(1), g.C.Name(1), g.C.Field(1)), Is.EqualTo((g.Now + 125, "Caller", 2000001)));
        Assert.That((g.C.Time(2), g.C.Name(2), g.C.Field(2)), Is.EqualTo((g.Now + 60, "Partner", 2000002)));
        Assert.That(g.C.Name(0), Is.EqualTo("Master"));
        Assert.That(g.Notices, Is.EqualTo(new[]
        {
            (MessageBoxData.Common(0xb63), 3, true),
            (tables.Common("s_cash_recall_wedding_notice") ?? "", 3, true),
            (MessageBoxData.Common(0xb64), 3, true),
            ("", 3, true),
        }), "type 1 party, 2 wedding, 0 guild; another type an empty text");
        Assert.That(g.Sounds, Is.EqualTo(Enumerable.Repeat(RecallScrollNotifyController.Sound, 4)));
        Assert.That(g.C.IsActive, Is.False, "not loaded: kept only");
        g.C.OnPacket(new RecallScrollPacket.Close(1));
        Assert.That(g.C.Time(1), Is.Zero);
        Assert.That(g.C.Name(1), Is.EqualTo("Caller"), "a close clears the time only");
        Assert.That(g.Notices, Has.Count.EqualTo(4), "a close shows nothing");
    }

    [Test]
    public void TheLoadShowsTheSlotsStillAheadAndEachSecondCountsDownAndClosesThePastOnes()
    {
        var g = New();
        g.C.OnPacket(Open(1, g.Now + 3));
        g.C.OnPacket(Open(0, g.Now - 1));
        Load(g);
        Assert.That(Shown(g, "m_partyNotify"), Is.True);
        Assert.That(Shown(g, "m_guildNotify"), Is.False, "an expiry already past is not opened");
        Assert.That(Shown(g, "m_weddingNotify"), Is.False);
        g.C.Update();                                  // the first call arms the second's timer
        g.Ms += 999; g.C.Update();
        Assert.That(g.C.IsActive, Is.False, "before a second");
        g.Ms += 1; g.Now += 1; g.C.Update();
        Assert.That(g.C.IsActive, Is.True, "one shown notice");
        g.C.OnPacket(Open(2, g.Now + 600, "Partner"));
        g.P.Tick();
        Assert.That(Shown(g, "m_weddingNotify"), Is.True, "an open on the loaded movie shows at once");
        g.Ms += 1000; g.Now += 3; g.C.Update();        // the party notice's time is past
        g.P.Tick();
        Assert.That(Shown(g, "m_partyNotify"), Is.False);
        Assert.That(g.C.Time(1), Is.Not.Zero, "the tick's close keeps the expiry");
        g.C.OnPacket(new RecallScrollPacket.Close(2));
        g.P.Tick();
        Assert.That(Shown(g, "m_weddingNotify"), Is.False);
        g.Ms += 1000; g.C.Update();
        Assert.That(g.C.IsActive, Is.False, "none shown");
        for (int i = 0; i < 10; i++) g.P.Tick();
        Assert.That(g.RT.Errors, Is.Empty);
    }

    [Test]
    public void TheTimeLeftIsMinutesAndSecondsRedAtTenSecondsOrLess()
    {
        Assert.That(RecallScrollNotifyController.Remaining(125), Is.EqualTo("<FONT color='#ffffff'>02:05</FONT>"));
        Assert.That(RecallScrollNotifyController.Remaining(11), Is.EqualTo("<FONT color='#ffffff'>00:11</FONT>"));
        Assert.That(RecallScrollNotifyController.Remaining(10), Is.EqualTo("<FONT color='#ff0000'>00:10</FONT>"));
        Assert.That(RecallScrollNotifyController.Remaining(3725), Is.EqualTo("<FONT color='#ffffff'>02:05</FONT>"), "the hour is dropped");
        Assert.That(RecallScrollNotifyController.Remaining(-5), Is.EqualTo("<FONT color='#ff0000'>00:-5</FONT>"));
    }

    [Test]
    public void ANoticeClickAsksWithTheCallerAndMapAndTheYesSendsTheSlotNameAndField()
    {
        var g = New();
        g.C.OnPacket(Open(1, g.Now + 100, "Caller", 2000001));
        g.C.OnPacket(Open(0, g.Now + 100, "Master", 99999999));
        Assert.That(g.C.ClickNotice(1), Is.True);
        Assert.That(g.Boxes, Has.Count.EqualTo(1));
        var box = g.Boxes[0];
        Assert.That(box.Type, Is.EqualTo(1));
        Assert.That(box.Extra, Is.EqualTo(RecallScrollNotifyController.ConfirmTag));
        Assert.That(box.Msg, Is.EqualTo(ClientFormat.Format(MessageBoxData.Common(0xb62), "Caller", tables.MapName(2000001) ?? "")));
        Assert.That((box.Modaless, box.NoInputBlock, box.EnterAllowed), Is.EqualTo((true, true, false)));
        Assert.That((box.Btn0, box.Btn1), Is.EqualTo((MessageBoxData.Common(0x477), MessageBoxData.Common(0x478))));
        Assert.That(RecallScrollConfirm.Accept(box), Is.EqualTo(new RecallScrollAccept(1, "Caller", 2000001)));

        Assert.That(g.C.ClickNotice(0), Is.False, "a field the data lacks is not taken");
        Assert.That(g.C.ClickNotice(3), Is.True, "a slot out of range is taken and does nothing");
        g.InField = false;
        Assert.That(g.C.ClickNotice(1), Is.False, "outside a field nothing is taken");
        Assert.That(g.Boxes, Has.Count.EqualTo(1));
    }

    [Test]
    public void TheConfirmBoxsYesSendsAndItsNoSendsNothing()
    {
        var c = new MessageBoxController();
        var sent = new List<RecallScrollAccept>();
        c.SendRecallScroll = sent.Add;
        var box = MessageBoxQueue.Create(1, "", null, RecallScrollNotifyController.ConfirmTag);
        box.Append(2);
        box.Args.Add("Partner");
        box.Append(2000002u);
        c.data = box;
        Assert.That(c.HandlePlainTag(1, RecallScrollNotifyController.ConfirmTag), Is.True);
        Assert.That(sent, Is.Empty, "no: nothing sent");
        Assert.That(c.HandlePlainTag(0, RecallScrollNotifyController.ConfirmTag), Is.True);
        Assert.That(sent, Is.EqualTo(new[] { new RecallScrollAccept(2, "Partner", 2000002) }));
        c.data = null;
        Assert.That(c.HandlePlainTag(0, RecallScrollNotifyController.ConfirmTag), Is.True, "no box: taken, nothing sent");
        Assert.That(sent, Has.Count.EqualTo(1));
    }
}
