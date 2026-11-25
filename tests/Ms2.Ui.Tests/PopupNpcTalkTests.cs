using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Quest;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The npc talk popup of a field quest (uipopupnpctalkdialog) run by its own ActionScript with UIPopupNpcTalkDialog's
/// C++ side (needs MS2_DATA).</summary>
public class PopupNpcTalkTests
{
    static PackSet packs;
    static MovieSource src;
    static QuestTable quests;
    static Ms2.Formats.World.QuestScriptTable scripts;

    static PackSet Packs()
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = TestData.Packs(d);
            src = TestData.Movies(packs);
            quests = QuestTable.Load(packs, new[] { "en" }, "KR", _ => true);
            scripts = new Ms2.Formats.World.QuestScriptTable(packs, (e, c, k) => FeatureElements.Collect(packs, e, c, k));
        }
        return packs;
    }

    // two field quests (questType 8) of field 2000147 whose talks are script 100 (6 lines, 3 lines) and 300 (1 line each)
    const int Wildflower = 92000010, Second = 92000020, Field = 2000147;

    sealed class Book
    {
        public readonly Dictionary<int, QuestRecord> Records = new();
        public void Start(int q, bool complete) => Records[q] = new QuestRecord(q, 1, 0, 0, 0, complete, false);
    }

    static PopupNpcTalkController New(Book book, int field = Field)
    {
        Packs();
        return new PopupNpcTalkController
        {
            FindQuestTalk = (q, step) => scripts.Find(q, step) is { Contents.Count: > 0 } s
                ? s.Contents.Select(x => new PopupNpcTalkController.Line(x.Text, x.SpeakerIllust)).ToList() : null,
            QuestOf = quests.Quest,
            RecordOf = q => book.Records.TryGetValue(q, out var r) ? r : null,
            FieldId = () => field,
            FieldQuests = f => quests.Quests.Where(d => d.Type == 8 && d.Fields.Length > 0 && d.Fields[0] == f).Select(d => d.Id).OrderBy(x => x).ToList(),
        };
    }

    static (UiHost, Player, Runtime) Load(PopupNpcTalkController c)
    {
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1837; p.StageH = 1034;
        var model = new UiModel(new GameTables(Packs(), "en")) { Keys = new KeyTable(Packs(), "en") };
        model.Keys.Apply(UiFixture.NewCharacterKeyTable());
        var h = new UiHost(p, c, model);
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 60 && !c.Loaded; i++) p.Tick();
        Assert.That(c.Loaded, Is.True, "OnLoadComplete");
        foreach (var e in rt.Errors) TestContext.WriteLine("  error: " + e);
        return (h, p, rt);
    }

    static string Text(UiHost h, string child, string member) => Rt.ToString(h.Get(h.Child(child), member));
    static bool Visible(UiHost h, string child) => h.Get(h.Child(child), "visible") is true;

    static string Label(UiHost h, string key, int keyId) =>
        ClientFormat.Format(h.Model.Tables.Common(key), h.Model.Keys.GetKeyName(keyId));

    [Test]
    public void TheTalkOpensTheMovieAndShowsItsFirstLine()
    {
        var c = New(new Book());
        int opened = 0;
        c.OpenRequested += () => opened++;
        c.StartQuestTalk(Wildflower);
        c.StartQuestTalk(Wildflower);
        Assert.That(opened, Is.EqualTo(1), "FUN_141924680 opens a closed dialog once");
        Assert.That(c.Quest, Is.EqualTo(Wildflower));
        var (h, _, _) = Load(c);
        Assert.That(Text(h, "m_script", "htmlText"), Is.Not.Empty, "the first line");
        Assert.That(Visible(h, "m_end"), Is.True, "not the last line: Close is shown");
        Assert.That(Text(h, "m_end", "label"), Is.EqualTo(Label(h, "s_field_quest_talk_end", 1)));
        int talkKey = h.Model.Keys.FindKeyByActionAndPriority(PopupNpcTalkController.TalkAction, 0);
        Assert.That(Text(h, "m_next", "label"), Is.EqualTo(Label(h, "s_field_quest_talk_next", talkKey)));
        TestContext.WriteLine($"end '{Text(h, "m_end", "label")}', next '{Text(h, "m_next", "label")}', line '{Text(h, "m_script", "htmlText")}'");
    }

    [Test]
    public void TheLastLineOfATalkInProgressSaysOkAndQueuesTheBanner()
    {
        var c = New(new Book());
        c.StartQuestTalk(Second);
        var (h, _, _) = Load(c);
        int count = scripts.Find(Second, 100).Contents.Count;
        int banner = 0, sent = 0;
        c.OpeningBanner += q => banner = q;
        c.SendComplete += _ => sent++;
        for (int i = 1; i < count; i++) Assert.That(c.OnCallback("onClickNext", Array.Empty<object>()), Is.True);
        Assert.That(c.LineIndex, Is.EqualTo(count - 1));
        Assert.That(Visible(h, "m_end"), Is.False, "the last line hides Close");
        int talkKey = h.Model.Keys.FindKeyByActionAndPriority(PopupNpcTalkController.TalkAction, 0);
        Assert.That(Text(h, "m_next", "label"), Is.EqualTo(Label(h, "s_field_quest_talk_ok", talkKey)));
        c.OnCallback("onClickNext", Array.Empty<object>());
        Assert.That(banner, Is.EqualTo(Second), "past the last line, not complete: QueueOpeningBanner");
        Assert.That(sent, Is.Zero);
        Assert.That(c.IsClosed || c.Quest == 0, Is.True, "closed");
        Assert.That(c.Quest, Is.Zero, "vfunc_67 clears the quest");
    }

    [Test]
    public void ACompleteQuestTalksItsCompleteScriptAndSendsQuestMode4()
    {
        var book = new Book();
        book.Start(Wildflower, complete: true);
        var c = New(book);
        c.StartQuestTalk(Wildflower);
        var (h, _, _) = Load(c);
        Assert.That(scripts.Find(Wildflower, 300).Contents, Has.Count.EqualTo(1), "script 300");
        Assert.That(Visible(h, "m_end"), Is.False, "one line: the last");
        int talkKey = h.Model.Keys.FindKeyByActionAndPriority(PopupNpcTalkController.TalkAction, 0);
        Assert.That(Text(h, "m_next", "label"), Is.EqualTo(Label(h, "s_field_quest_talk_complete", talkKey)));
        int sent = 0, banner = 0;
        c.SendComplete += q => sent = q;
        c.OpeningBanner += q => banner = q;
        c.Key(PopupNpcTalkController.PopupKey.Talk);
        Assert.That(sent, Is.EqualTo(Wildflower), "the talk key past the last line: Quest mode 4");
        Assert.That(banner, Is.Zero);
    }

    [Test]
    public void EscQueuesTheBannerOfAQuestInProgressAndCloses()
    {
        var c = New(new Book());
        c.StartQuestTalk(Wildflower);
        Load(c);
        int banner = 0;
        c.OpeningBanner += q => banner = q;
        c.Key(PopupNpcTalkController.PopupKey.Escape);
        Assert.That(banner, Is.EqualTo(Wildflower));
        Assert.That(c.Quest, Is.Zero, "closed");
        banner = 0;
        c.Key(PopupNpcTalkController.PopupKey.Escape);
        Assert.That(banner, Is.Zero, "a closed dialog takes no key (vfunc_55)");
    }

    [Test]
    public void TheFadeInOpensTheFirstActiveFieldQuestOnce()
    {
        var book = new Book();
        book.Start(Second, complete: false);
        var c = New(book);
        int opened = 0;
        c.OpenRequested += () => opened++;
        c.OnFadeInComplete();
        Assert.That(c.Quest, Is.EqualTo(Second), "Wildflower has no record: the first active one is the second");
        Assert.That(opened, Is.EqualTo(1));
        book.Start(Wildflower, complete: false);
        c.Close();
        c.OnFadeInComplete();
        Assert.That(c.Quest, Is.EqualTo(Wildflower), "Second was opened before; Wildflower is next");
        c.Close();
        c.OnFadeInComplete();
        Assert.That(c.Quest, Is.Zero, "each quest opens once");
        c.ClearOpenedQuests();
        c.OnFadeInComplete();
        Assert.That(c.Quest, Is.EqualTo(Wildflower), "NotifyChangeMyPC clears the opened quests");
    }

    [Test]
    public void TheListenersStartAFieldQuestOfThisFieldAndAReadyOne()
    {
        var book = new Book();
        var c = New(book);
        book.Start(Wildflower, complete: false);
        c.OnQuestStarted(Wildflower);
        Assert.That(c.Quest, Is.EqualTo(Wildflower), "slot 1: a field quest whose progressMap has the field");
        c.Close();
        var other = New(book, field: 2000148);
        other.OnQuestStarted(Wildflower);
        Assert.That(other.Quest, Is.Zero, "another field");
        c.OnQuestProgress(Wildflower);
        Assert.That(c.Quest, Is.Zero, "slot 3: not complete yet");
        book.Start(Wildflower, complete: true);
        c.OnQuestProgress(Wildflower);
        Assert.That(c.Quest, Is.EqualTo(Wildflower), "slot 3: FUN_140d738d0, active and complete");
    }

    [Test]
    public void WithoutFameContentsNothingStarts()
    {
        var c = New(new Book());
        c.FeatureEnabled = id => id != PopupNpcTalkController.FameContentsFeature;
        c.StartQuestTalk(Wildflower);
        Assert.That(c.Quest, Is.Zero);
    }
}
