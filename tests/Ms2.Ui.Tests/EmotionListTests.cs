using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

public class EmotionListTests
{
    static EmoteEntry E(int id, ushort lv = 1, bool perm = false, long expire = 0, long sort = 0, bool fav = false)
        => new(id, lv, perm, expire, sort, fav);

    [Test]
    public void ListModeReplacesTheWholeList()
    {
        // OnEmote mode 0: ClearEmotions, then AddEmotion per entry (scenestategame.cpp:2541-2552)
        var list = new EmotionList();
        list.Apply(new EmoteMessage.List(new[] { E(1), E(2) }), 100);
        list.Apply(new EmoteMessage.List(new[] { E(3, fav: true) }), 100);
        Assert.That(list.All, Has.Count.EqualTo(1));
        Assert.That(list.All[0].Id, Is.EqualTo(3));
        Assert.That(list.All[0].Entry.Favorite, Is.True);
        Assert.That(list.All[0].IsNew, Is.False);
    }

    [Test]
    public void LearnModeAddsNewAndRaisesLearned()
    {
        // mode 1: AddEmotion(.., bNew 1), then NotifyLearnEmotion(id) (:2559-2560)
        var list = new EmotionList();
        int learned = 0;
        list.Learned += id => learned = id;
        list.Apply(new EmoteMessage.Learn(E(90200011, 2)), 100);
        Assert.That(list.All, Has.Count.EqualTo(1));
        Assert.That(list.All[0].IsNew, Is.True);
        Assert.That(learned, Is.EqualTo(90200011));
        list.ClearNew();
        Assert.That(list.All[0].IsNew, Is.False);
    }

    [Test]
    public void DuplicateIdAndLevelIsSkippedUnlessExpired()
    {
        // AddEmotion adds only when IsEmotionLearned is 0 (skillslot.cpp:2792); an expired one is not learned (:2824)
        var list = new EmotionList();
        list.Apply(new EmoteMessage.List(new[] { E(5, 1, expire: 50), E(5, 1, expire: 500), E(5, 2) }), 40);
        Assert.That(list.All, Has.Count.EqualTo(2), "same id and level while the first is valid");
        list.Apply(new EmoteMessage.List(new[] { E(6, 1, expire: 50), E(6, 1, expire: 500) }), 60);
        Assert.That(list.All, Has.Count.EqualTo(2), "the first expired, so the second is added");
        Assert.That(list.IsLearned(6, 1, 60), Is.True);
        Assert.That(list.IsLearned(6, 1, 600), Is.False);
        Assert.That(list.IsLearned(6, 2, 60), Is.False, "the level must match too");
    }

    [Test]
    public void ExpiryZeroNeverEndsAndExpiryEqualToNowIsValid()
    {
        // stEmotion::FUN_1417643d0 (skillslot.cpp:1778-1784): 0 -> valid; expire >= now -> valid
        var list = new EmotionList();
        list.Apply(new EmoteMessage.List(new[] { E(1, expire: 0), E(2, expire: 100), E(3, expire: 99) }), 0);
        var valid = new List<Emotion>();
        list.GetValid(100, valid);
        Assert.That(valid.Select(e => e.Id), Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public void ResultModeShowsTheCommonStringOfTheCode()
    {
        // mode 3 (:2563-2569): 1 -> 0x858, 0 -> 0x857, others nothing
        var list = new EmotionList();
        var shown = new List<int>();
        list.Notice += shown.Add;
        list.Apply(new EmoteMessage.Result(1), 0);
        list.Apply(new EmoteMessage.Result(0), 0);
        list.Apply(new EmoteMessage.Result(2), 0);
        Assert.That(shown, Is.EqualTo(new[] { 0x858, 0x857 }));
        Assert.That(list.Apply(null, 0), Is.False);
    }

    [Test]
    public void SortPutsPermanentFirstThenSortTimeThenId()
    {
        // Unk_SsEmotionLess (skillslot.cpp:1787-1801)
        var list = new EmotionList();
        list.Apply(new EmoteMessage.List(new[]
        {
            E(30, sort: 5), E(20, sort: 5), E(10, sort: 9), E(40, perm: true, sort: 100), E(unchecked((int)0x80000000), perm: true),
        }), 0);
        var sorted = list.All.ToList();
        sorted.Sort(EmotionList.CompareAscending);
        Assert.That(sorted.Select(e => e.Id), Is.EqualTo(new[] { 40, unchecked((int)0x80000000), 20, 30, 10 }));
    }

    [Test]
    public void LearnFromItemChecksTypeAndLearned()
    {
        // Unk_Inventory::SendEmotion (inventory.cpp:1615-1626): an emotion item, not learned -> mode 1 with the uid
        var list = new EmotionList();
        list.Apply(new EmoteMessage.List(new[] { E(7, 1) }), 0);
        Assert.That(list.LearnFromItem(11, false, 8, 1, 0, out int n1), Is.Null);
        Assert.That(n1, Is.EqualTo(2135));
        Assert.That(list.LearnFromItem(11, true, 7, 1, 0, out int n2), Is.Null);
        Assert.That(n2, Is.EqualTo(2136));
        var req = list.LearnFromItem(11, true, 8, 1, 0, out int n3);
        Assert.That(n3, Is.EqualTo(0));
        Assert.That(req, Is.EqualTo(new EmoteLearnRequest(11)));
    }

    static Ms2.Ui.Items.ItemData Item(int id, string property, bool gms2File)
    {
        var d = Ms2.Ui.Items.ItemData.Load(id, System.Xml.Linq.XElement.Parse($"<environment><property {property}/></environment>"));
        if (gms2File) { d.FillGms2File(); d.FillEquipSubtype(); d.FillBoxType(); }   // ItemDataTable.PerItem's order
        return d;
    }

    [Test]
    public void AnEmotionItemIsType24OnKmsDataAndSubtype14OnGms2Data()
    {
        // KMS FUN_14053ce40 (itemdata.cpp:725): type 0x18. GMS2 client: subtype == 14 (@0x140343af0, @0x140346cd0, @0x1403333d0)
        Assert.That(Item(20200001, "type=\"24\" subtype=\"0\"", false).IsEmotionItem, Is.True, "KMS row");
        Assert.That(Item(20200001, "type=\"2\" subtype=\"14\"", false).IsEmotionItem, Is.False, "a KMS row reads the type only");
        Assert.That(Item(20200001, "type=\"2\" subtype=\"14\"", true).IsEmotionItem, Is.True, "GMS2 file");
        Assert.That(Item(20200001, "type=\"24\" subtype=\"0\"", true).IsEmotionItem, Is.False, "the GMS2 client never reads type 0x18");
        Assert.That(Item(20000001, "type=\"2\" subtype=\"2\"", true).IsEmotionItem, Is.False);
        // the file's subtype, before FillEquipSubtype turns a group 114 equipment's kind into 14
        var equip = Item(11400001, "type=\"1\" subtype=\"0\"", true);
        Assert.That((equip.Subtype, equip.IsEmotionItem), Is.EqualTo((14, false)));
    }
}
