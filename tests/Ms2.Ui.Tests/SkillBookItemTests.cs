using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The GMS2 client's skill book item check (the job system's @0x14057d8b0) and its job rule (@0x140350640).</summary>
public class SkillBookItemTests
{
    /// <summary>A GMS2 file item of type 2, subtype 8 (a skill book), job 10, level 20, skill 10500001 level 1.</summary>
    static ItemData Book(Action<ItemData> set = null)
    {
        var d = new ItemData { Type = 2, Subtype = ItemData.Gms2SkillBookSubtype, SkillId = 10500001, SkillLevel = 1, LevelLimit = 20 };
        d.JobLimit.Add(10);
        set?.Invoke(d);
        d.FillGms2File();
        return d;
    }

    static readonly SkillBookItem.Owner Pc = new(20, 10, false, false, false);

    /// <summary>The check at server time 1000 of an item of <paramref name="d"/> expiring at <paramref name="expiry"/>.</summary>
    static int Check(ItemData d, SkillBookItem.Owner? owner = null, long expiry = 0, Func<uint, bool> learned = null)
    {
        var item = Item.Create(d, 1);
        item.ExpiryTime = expiry;
        return SkillBookItem.Check(item, owner ?? Pc, 1000, learned);
    }

    [Test]
    public void OnlyAGms2FileItemOfSubtype8IsASkillBook()
    {
        Assert.That(Book().IsGms2SkillBook, Is.True);
        // the KMS data: no GMS2 file type, so no skill book whatever the subtype
        Assert.That(new ItemData { Type = 2, Subtype = 8 }.IsGms2SkillBook, Is.False);
        var emotion = new ItemData { Type = 2, Subtype = ItemData.Gms2EmotionSubtype };
        emotion.FillGms2File();
        Assert.That((emotion.IsGms2SkillBook, emotion.IsEmotionItem), Is.EqualTo((false, true)));
        // the file's subtype counts, not the one later fills rewrite
        var d = Book();
        d.Subtype = 0;
        Assert.That(d.IsGms2SkillBook, Is.True);
    }

    [Test]
    public void TheCheckAnswersInTheClientsOrder()
    {
        Assert.That(Check(Book()), Is.EqualTo(SkillBookItem.Ok));
        // no owner PC, no item, not a skill book: 10
        Assert.That(SkillBookItem.Check(Item.Create(Book(), 1), null, 1000, null), Is.EqualTo(SkillBookItem.NoItem));
        Assert.That(SkillBookItem.Check(null, Pc, 1000, null), Is.EqualTo(SkillBookItem.NoItem));
        Assert.That(Check(new ItemData { Type = 2, Subtype = 8, LevelLimit = 1 }), Is.EqualTo(SkillBookItem.NoItem));
        // job before level: a wrong job at a low level is 3
        Assert.That(Check(Book(), Pc with { Job = 20, Level = 1 }), Is.EqualTo(SkillBookItem.WrongJob));
        Assert.That(Check(Book(), Pc with { Level = 19 }), Is.EqualTo(SkillBookItem.LowLevel));
        // the level is compared as a u16
        Assert.That(Check(Book(), Pc with { Level = 0x10014 }), Is.EqualTo(SkillBookItem.Ok));
        // level before the expiry; expired: 5; a pet or a maid id item that expired passes the use test
        Assert.That(Check(Book(), Pc with { Level = 19 }, expiry: 500), Is.EqualTo(SkillBookItem.LowLevel));
        Assert.That(Check(Book(), expiry: 500), Is.EqualTo(SkillBookItem.Expired));
        Assert.That(Check(Book(), expiry: 1000), Is.EqualTo(SkillBookItem.Expired));
        Assert.That(Check(Book(), expiry: 1001), Is.EqualTo(SkillBookItem.Ok));
        Assert.That(Check(Book(d => d.MaidElementId = 3), expiry: 500), Is.EqualTo(SkillBookItem.Ok));
        // expiry before the PC bang, PC bang before VIP, VIP before the wedding, all before the learned test
        var all = Book(d => { d.PCBang = true; d.Vip = true; d.Wedding = true; });
        Assert.That(Check(all, expiry: 500, learned: _ => true), Is.EqualTo(SkillBookItem.Expired));
        Assert.That(Check(all, learned: _ => true), Is.EqualTo(SkillBookItem.PcBang));
        Assert.That(Check(all, Pc with { InPcBang = true }, learned: _ => true), Is.EqualTo(SkillBookItem.Vip));
        Assert.That(Check(all, Pc with { InPcBang = true, IsVip = true }, learned: _ => true), Is.EqualTo(SkillBookItem.Wedding));
        Assert.That(Check(all, Pc with { InPcBang = true, IsVip = true, WeddingInProgress = true }, learned: _ => true),
            Is.EqualTo(SkillBookItem.Learned));
        // the learned test reads the item's skill id
        var asked = new List<uint>();
        Assert.That(Check(Book(), learned: id => { asked.Add(id); return false; }), Is.EqualTo(SkillBookItem.Ok));
        Assert.That(asked, Is.EqualTo(new[] { 10500001u }));
        Assert.That(Check(Book(), learned: id => id == 10500001), Is.EqualTo(SkillBookItem.Learned));
    }

    [Test]
    public void TheGms2JobRuleDoesNotCountJob1As10()
    {
        // @0x140350640: no "job 1 counts as 10" step (ItemLimit::IsJobAllowed has it)
        var d = Book();
        Assert.That((d.IsJobAllowed(1), d.IsGms2JobAllowed(1)), Is.EqualTo((true, false)));
        Assert.That(Check(d, Pc with { Job = 1 }), Is.EqualTo(SkillBookItem.WrongJob));
        Assert.That(d.IsGms2JobAllowed(10), Is.True);
        Assert.That(d.IsGms2JobAllowed(0), Is.False);
        // a jobLimit entry of 0 or 1 lets every job in; an empty jobLimit too; a disabled job never
        Assert.That(Book(x => x.JobLimit.Add(1)).IsGms2JobAllowed(30), Is.True);
        Assert.That(Book(x => x.JobLimit.Add(0)).IsGms2JobAllowed(30), Is.True);
        Assert.That(Book(x => x.JobLimit.Clear()).IsGms2JobAllowed(30), Is.True);
        Assert.That(Book(x => { x.JobLimit.Clear(); x.DisableJobLimit.Add(30); }).IsGms2JobAllowed(30), Is.False);
        Assert.That(Book(x => x.JobLimit.Add(-1)).IsGms2JobAllowed(30), Is.False, "the entry is compared unsigned");
    }

    [Test]
    public void TheGms2LearnedShortCutIsKindsType3OrANamedEmotion()
    {
        // JobSystemCli::vfunc_2 @0x14057cc50 of the GMS2 client: kinds +8 == 3 (@0x14045d5d0), or emotionType 1-2 with an emotion
        // name (@0x14045d510); KMS rows have no such flag (their client tests the main type)
        static Skills.SkillData Row(string kinds, bool gms2 = true) => Skills.SkillData.Load(
            System.Xml.Linq.XElement.Parse($"<skill id=\"1\"><basic>{kinds}</basic></skill>"), (e, c, k) => e.Elements(c).ToList(), gms2);
        Assert.That(Row("<kinds type=\"3\" subType=\"5\"/>").Gms2NotBookKind, Is.True);
        Assert.That(Row("<kinds type=\"0\" subType=\"6\"/>").Gms2NotBookKind, Is.False, "swim dash: KMS main type 11 short-cuts, GMS2 does not");
        Assert.That(Skills.JobSystem.IsBookSkillKind(Row("<kinds type=\"0\" subType=\"6\"/>")), Is.False);
        Assert.That(Row("<kinds type=\"0\" subType=\"1\"/>").Gms2NotBookKind, Is.False);
        Assert.That(Row("<kinds type=\"0\" subType=\"0\" emotionType=\"1\" emotion=\"hello\"/>").Gms2NotBookKind, Is.True);
        Assert.That(Row("<kinds type=\"0\" subType=\"0\" emotionType=\"2\" emotion=\"100001\"/>").Gms2NotBookKind, Is.True);
        Assert.That(Row("<kinds type=\"0\" subType=\"0\" emotionType=\"1\"/>").Gms2NotBookKind, Is.False);
        Assert.That(Row("<kinds type=\"0\" subType=\"0\" emotionType=\"3\" emotion=\"x\"/>").Gms2NotBookKind, Is.False);
        Assert.That(Row("<kinds type=\"3\"/>", gms2: false).Gms2NotBookKind, Is.Null);
    }

    [Test]
    public void EachResultHasItsBoxKey()
    {
        Assert.That(SkillBookItem.BoxKey(SkillBookItem.Ok), Is.EqualTo("s_msg_skillbook_learn_messagebox"));
        Assert.That(SkillBookItem.BoxKey(SkillBookItem.WrongJob), Is.EqualTo("s_msg_skillbook_error_job"));
        Assert.That(SkillBookItem.BoxKey(SkillBookItem.LowLevel), Is.EqualTo("s_msg_skillbook_error_level"));
        Assert.That(SkillBookItem.BoxKey(SkillBookItem.Learned), Is.EqualTo("s_msg_skillbook_error_master"));
        Assert.That(SkillBookItem.BoxKey(SkillBookItem.Expired), Is.EqualTo("s_msg_skillbook_error_expired"));
        Assert.That(SkillBookItem.BoxKey(SkillBookItem.PcBang), Is.EqualTo("s_item_err_puton_pcbang"));
        Assert.That(SkillBookItem.BoxKey(SkillBookItem.Vip), Is.EqualTo("s_item_err_puton_vip"));
        foreach (int r in new[] { 2, SkillBookItem.Wedding, SkillBookItem.NotUsable, SkillBookItem.NoItem })
            Assert.That(SkillBookItem.BoxKey(r), Is.Null, $"result {r} shows nothing");
    }
}
