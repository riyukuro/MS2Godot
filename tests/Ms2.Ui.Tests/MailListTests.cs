using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>CUserMail's packet handling (MailBook) and UIMailListDialog's rules (MailListController); the last tests run the
/// movie uimaillistdialog with its ActionScript (needs MS2_DATA).</summary>
[NonParallelizable]
public class MailListTests
{
    static MailInfo M(byte kind, long id, long sent, long expire = 100_000, long read = 0, long meso = 0) => new()
    {
        Kind = kind, Id = id, Sender = $"s{id}", Title = $"t{id}", SentTime = sent, ExpireTime = expire, ReadTime = read,
        Meso = new MailRange { Begin = meso },
    };

    static MailBook Book(params MailInfo[] mails)
    {
        var b = new MailBook(MailTexts.Empty());
        b.Apply(new MailMessage.Listed(mails), false, 0);
        return b;
    }

    [Test]
    public void TheBookKeepsNewIdsAndSetsTheListFlagsWhileOpen()
    {
        var b = Book(M(1, 10, 5), M(1, 11, 6));
        int counts = 0;
        b.MenuCount += () => counts++;
        b.Apply(new MailMessage.Listed(new[] { M(1, 10, 99), M(2, 12, 7) }), true, 0);
        Assert.That(b.Mails.Select(m => (m.Id, m.SentTime)), Is.EqualTo(new[] { (10L, 5L), (11L, 6L), (12L, 7L) }), "a known id is left");
        b.Apply(new MailMessage.EndList(), true, 0);
        Assert.That(b.ListRefresh, Is.True);
        b.Apply(new MailMessage.Deleted(11), true, 0);
        Assert.That((b.Find(11), b.ListRemoved), Is.EqualTo(((MailInfo)null, true)));
        long readId = 0;
        b.ReadMail += id => readId = id;
        b.Apply(new MailMessage.MarkedRead(10, 42), true, 0);
        Assert.That((b.Find(10).ReadTime, readId), Is.EqualTo((42L, 10L)));
        b.Apply(new MailMessage.New(3, false, 0), false, 0);
        Assert.That(b.Unread, Is.EqualTo(3));
        Assert.That(counts, Is.EqualTo(5), "the game menu's count after each mode");
        b.Apply(new MailMessage.Collected(999, false, 0, 1), true, 0);
        Assert.That(counts, Is.EqualTo(5), "an unknown mail returns before the count");
    }

    [Test]
    public void RemoveKeepsAMailHoldingSomethingUnlessForced()
    {
        var b = Book(M(1, 10, 5, meso: 100));
        Assert.That(b.Remove(10, false), Is.False);
        Assert.That(b.Remove(10, true), Is.True);
    }

    [Test]
    public void TabMailsAreAdsFirstThenNewestSent()
    {
        var b = Book(M(101, 1, 50), M(200, 2, 10), M(102, 3, 90), M(1, 4, 70), M(103, 5, 90));
        var c = new MailListController(b);
        // no movie: GetSelectedTab is -1, the system tab
        Assert.That(c.GetTabMails().Select(m => m.Id), Is.EqualTo(new long[] { 2, 5, 3, 1 }));
    }

    [Test]
    public void DaysLeftByTheExpiry()
    {
        var texts = MailTexts.Empty();
        texts.CommonByKey = k => k + "|{0}|{1}";
        var c = new MailListController(new MailBook(texts)) { ServerTime = () => 1000 };
        long hours2 = 1000 + 3600 * 2 + 60 * 5, days3 = 1000 + 86400 * 3 + 5;
        Assert.That(texts.Common(0x80b), Is.Not.Empty);
        Assert.That(c.DaysLeft(M(1, 1, 0, expire: 999), false), Is.EqualTo(texts.Common(0x80b)));
        Assert.That(c.DaysLeft(M(1, 1, 0, expire: hours2), false), Is.EqualTo(texts.Common(0x809)));
        Assert.That(c.DaysLeft(M(1, 1, 0, expire: hours2), true), Is.EqualTo(texts.CommonF(0x80a, 2L, 5L)));
        Assert.That(c.DaysLeft(M(1, 1, 0, expire: days3), false), Is.EqualTo(texts.CommonF(0x807, 3)));
        Assert.That(c.DaysLeft(M(1, 1, 0, expire: days3), true), Is.EqualTo(texts.CommonF(0x808, 3)));
        Assert.That(texts.CommonF(0x808, 3), Does.Contain("3"));
    }

    [Test]
    public void TheAmountsAddTheUnclaimedRangeAndTheAvailableBenefits()
    {
        var m = M(1, 1, 0, meso: 100);
        m.Benefits.Add(new MailBenefit { Type = 0, Sub = 0, Amount = 5 });
        m.Benefits.Add(new MailBenefit { Type = 2, Sub = 3, Amount = 7 });
        m.Benefits.Add(new MailBenefit { Type = 2, Sub = 1, Amount = 9, UsedTime = 1 });
        m.RedMeret = new MailRange { Begin = 4, Claim = 1 };
        Assert.That((MailListController.MesoAmount(m), MailListController.MeratAmount(m), MailListController.MeratRedAmount(m)),
            Is.EqualTo((105L, 7L, 0L)));
    }

    // ---- the movie ----

    static PackSet packs;
    static MovieSource src;
    static GameTables tables;

    static (UiHost, MailListController, List<IOutPacket>) Load(MailBook book)
    {
        if (packs == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            packs = new PackSet(d);
            src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
            tables = new GameTables(packs, "en");
        }
        book.Texts.CommonByKey = tables.Common;
        var sent = new List<IOutPacket>();
        var c = new MailListController(book) { ServerTime = () => 1000, Send = sent.Add, ShowBox = _ => { } };
        var (rt, p) = src.Open(c.Movie, r => r.Budget = 20_000_000);
        p.StageW = 1280; p.StageH = 720;
        var h = new UiHost(p, c, new UiModel(tables));
        Assert.That(h.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 10; i++) p.Tick();
        foreach (var e in rt.Errors) TestContext.WriteLine("  error: " + e);
        return (h, c, sent);
    }

    static bool Arrow(UiHost h, string name) => Rt.ToBoolean(h.Get(h.Child(name), "enabled"));

    static int Rows(UiHost h) => Rt.ToInt32(h.Get(h.Get(h.Child("m_list"), "dataProvider"), "length"));

    [Test]
    public void TheMovieListsTheTabPageAndAsksForTheMailsAtLoad()
    {
        var mails = Enumerable.Range(1, 16).Select(i => M(101, i, i)).Append(M(1, 100, 5)).Append(M(101, 200, 9, expire: 10)).ToArray();
        var book = Book(mails);
        var (h, c, sent) = Load(book);
        Assert.That(sent.OfType<MailListRequest>().Count(), Is.EqualTo(1), "SendMailMode0 at load");
        foreach (var fn in new[] { "setMailInfo", "getMailID", "readMail", "receiveMail", "onMailCount" })
            Assert.That(h.Get(h.Entity, fn), Is.Not.Null, $"the movie's {fn}");
        foreach (var child in new[] { "m_list", "m_tab", "m_page", "m_prev", "m_next", "m_checkAll" })
            Assert.That(h.Child(child), Is.Not.Null, child);
        book.Apply(new MailMessage.EndList(), true, 0);
        c.Process();
        Assert.That(book.ListRefresh, Is.False, "the frame clears the flag");
        int tab = Rt.ToInt32(h.Get(h.Child("m_tab"), "selectedIndex"));
        TestContext.WriteLine($"tab {tab}, rows {Rows(h)}");
        if (tab != 1)
        {
            // 17 system mails (one expired); page 0 holds the first 14 by sent time, the expired one among them not shown
            Assert.That(Rows(h), Is.EqualTo(13));
            Assert.That((Arrow(h, "m_prev"), Arrow(h, "m_next")), Is.EqualTo((false, true)), "page 0 of 2");
            Assert.That(c.OnCallback("onNextPage", Array.Empty<object>()), Is.False);
            Assert.That(c.Page, Is.EqualTo(1));
            Assert.That(Rows(h), Is.EqualTo(3));
            Assert.That((Arrow(h, "m_prev"), Arrow(h, "m_next")), Is.EqualTo((true, false)), "page 1 of 2");
        }
        c.OnCallback("onSelectMail", new object[] { "100" });
        Assert.That(sent.OfType<MailReadRequest>().Single().Id, Is.EqualTo(100));
        c.OnCallback("onSelectMail", new object[] { "3" });
        Assert.That(sent.OfType<MailReadRequest>().Select(r => r.Id), Is.EqualTo(new long[] { 100, 3 }));
    }

    [Test]
    public void RemoveAsksFirstAndDeletesOnOk()
    {
        var book = Book(M(1, 7, 5, meso: 10), M(200, 8, 5));
        book.Find(8).AdEnd = 500;                                   // an expired ad goes at once
        var boxes = new List<MessageBoxData>();
        var (_, c, sent) = Load(book);
        c.ShowBox = boxes.Add;
        c.OnCallback("onRemove", new object[] { "8" });
        Assert.That(sent.OfType<MailDeleteRequest>().Single().Ids, Is.EqualTo(new long[] { 8 }));
        c.OnCallback("onRemove", new object[] { "7" });
        var box = boxes.Single();
        Assert.That(box.Args, Is.EqualTo(new[] { "7" }));
        Assert.That(box.Callback(0, box), Is.True);
        Assert.That(sent.OfType<MailDeleteRequest>().Last().Ids, Is.EqualTo(new long[] { 7 }));
        c.OnCallback("onRemove", new object[] { "0" });
        Assert.That(boxes, Has.Count.EqualTo(2), "no id: box 0x23d");
    }
}
