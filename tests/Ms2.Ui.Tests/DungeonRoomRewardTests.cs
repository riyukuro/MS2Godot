using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Ui;
using Ms2.Ui.Hud;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The dungeon room panel's reward preview (UIDungeonRoomInfo::RefreshRewardPreview @0x1412b2250): the preview table, the unique box
/// items and the sort, over every room of the data set (needs MS2_DATA). Compared by item ids and counts, never by text.
/// </summary>
public class DungeonRoomRewardTests
{
    static PackSet packs;
    static GameTables tables;

    static GameTables Tables()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        packs ??= new PackSet(d);
        return tables ??= new GameTables(packs, "en");
    }

    // ---- the table and its items ----

    [Test]
    public void ARewardPreviewItemReadsItsTagJobsAndLevelRange()
    {
        var e = XElement.Parse("<item itemID=\"13100007\" grade=\"3\" itemTag=\"EVENT\" reqJobs=\"70,10\" reqLevel=\"50-60\"/>");
        var i = RewardPreviewItem.Load(e);
        Assert.That((i.ItemId, i.Rank, i.Tag), Is.EqualTo((13100007, 3, RewardPreviewItemTag.Event)));
        Assert.That(i.ReqJobs, Is.EqualTo(new[] { 70, 10 }));
        Assert.That((i.ReqLevelLow, i.ReqLevelHigh), Is.EqualTo((50, 60)));
        Assert.That(RewardPreviewItem.ParseTag("New"), Is.EqualTo(RewardPreviewItemTag.New));
        Assert.That(RewardPreviewItem.ParseTag("other"), Is.EqualTo(RewardPreviewItemTag.None));
        // a range of another shape stays unread (GetLevelRangeAttr)
        Assert.That(RewardPreviewItem.Load(XElement.Parse("<item itemID=\"1\" reqLevel=\"50\"/>")).ReqLevelHigh, Is.EqualTo(0));
    }

    [Test]
    public void ThePreviewItemsAreFilteredByJobGroupAndLevel()
    {
        var any = RewardPreviewItem.Load(XElement.Parse("<item itemID=\"1\"/>"));
        Assert.That(any.IsValid(10, 1), Is.True, "no job list and no level range: shown to everyone");
        var knight = RewardPreviewItem.Load(XElement.Parse("<item itemID=\"1\" reqJobs=\"10\" reqLevel=\"20-30\"/>"));
        Assert.That(knight.IsValid(10, 25), Is.True);
        Assert.That(knight.IsValid(1, 25), Is.True, "job group 1 counts as 10 (IsJobValid)");
        Assert.That(knight.IsValid(20, 25), Is.False);
        Assert.That(knight.IsValid(10, 31), Is.False);
        Assert.That(knight.IsValid(10, 19), Is.False);
    }

    [Test]
    public void TheCompareListsAHigherTagThenRankThenTypeThenId()
    {
        var t = Tables();
        var items = t.Items;
        var known = new List<ItemData>();
        foreach (var box in t.IndividualDrops.BoxIds.Take(300))
        {
            var l = new List<ItemDropEntry>();
            t.IndividualDrops.GetBoxItems(items, box, 0, 2, false, l);
            known.AddRange(l.Select(x => items.Get(x.ItemId)).Where(d => d != null && d.Rank > 0));
        }
        if (known.Count == 0 || !known.Any(d => d.Rank > known[0].Rank)) Assert.Ignore("this data has no two ranked items in its boxes");
        var a = known[0];
        var higher = known.First(d => d.Rank > a.Rank);
        RewardPreviewItem P(ItemData d, RewardPreviewItemTag t = RewardPreviewItemTag.None) => new() { ItemId = d.Id, Rank = d.Rank, Tag = t };
        Assert.That(RewardPreviewItem.Less(items, P(a, RewardPreviewItemTag.Event), P(higher)), Is.True, "a higher tag first, whatever the rank");
        Assert.That(RewardPreviewItem.Less(items, P(higher), P(a)), Is.True, "a higher rank first");
        Assert.That(RewardPreviewItem.Less(items, P(a), P(higher)), Is.False);
        Assert.That(RewardPreviewItem.Less(items, P(a), P(a)), Is.False, "an item is not before itself");
        Assert.That(RewardPreviewItem.Less(items, new RewardPreviewItem { ItemId = -5 }, P(a)), Is.False, "unknown data: false");
    }

    [Test]
    public void AGlobalOrUnknownBoxListsNothing()
    {
        var t = Tables();
        var list = new List<ItemDropEntry>();
        Assert.That(t.IndividualDrops.GetUniqueBoxItems(t.Items, 5, 0, 2, list), Is.False, "ids below 10000000 are global boxes");
        Assert.That(t.IndividualDrops.GetUniqueBoxItems(t.Items, 99999999, 0, 2, list), Is.False);
        Assert.That(list, Is.Empty);
        Assert.That(IndividualDropTable.IsGlobalBox(9999999), Is.True);
        Assert.That(IndividualDropTable.IsGlobalBox(10000000), Is.False);
    }

    [Test]
    public void TheUniqueItemsOfABoxHaveNoRepeatedIdAndNoHiddenOne()
    {
        var t = Tables();
        int checkedBoxes = 0;
        foreach (var id in t.IndividualDrops.BoxIds.Where(b => !IndividualDropTable.IsGlobalBox(b)).Take(400))
        {
            var list = new List<ItemDropEntry>();
            Assert.That(t.IndividualDrops.GetUniqueBoxItems(t.Items, id, 0, 2, list), Is.True);
            Assert.That(list.Select(x => x.ItemId).Distinct().Count(), Is.EqualTo(list.Count), $"box {id} lists an id twice");
            Assert.That(list.All(x => x.Tooltip != 2 && t.Items.Get(x.ItemId) != null), Is.True, $"box {id} lists a hidden or unknown item");
            checkedBoxes++;
        }
        if (checkedBoxes == 0) Assert.Ignore("this data has no individual drop box");
    }

    // ---- every room ----

    /// <summary>The preview row of the room by an independent read of the file: the last row of the id, its available items.</summary>
    static List<XElement> RawPreviewItems(PackSet p, int roomId)
    {
        string entry = TablePath.Resolve(p, "RewardPreview_dungeon.xml");
        if (entry == null) return null;
        var root = Ms2.Formats.Text.PackXml.Parse(p.Read("Xml", entry))?.Root;
        XElement row = null;
        foreach (var e in root?.Elements("preview") ?? Enumerable.Empty<XElement>())
            if (int.TryParse((string)e.Attribute("id"), out var id) && id == roomId) row = e;
        return row?.Elements("item").Where(i => FeatureElements.IsAvailable(p, i)).ToList();
    }

    /// <summary>
    /// Every room of the data: the reward grid lists its preview row (the items valid for the job and level), or, with no row, the
    /// unique items of its reward boxes, sorted by the client's compare. No room with a preview row or a displayable box item lists none.
    /// </summary>
    [TestCase(0, 60)]
    [TestCase(10, 30)]
    public void EveryRoomListsItsRewardItems(int job, int level)
    {
        var t = Tables();
        int withItems = 0, rooms = 0;
        foreach (var room in t.DungeonRooms.RoomList)
        {
            rooms++;
            var got = DungeonRoomInfoPanel.PreviewItems(t, room, job, level);
            var raw = RawPreviewItems(packs, (int) room.Id);
            List<int> expected;
            if (raw != null)
            {
                expected = new();
                foreach (var e in raw)
                {
                    var i = RewardPreviewItem.Load(e);
                    if (i.IsValid(job, level)) expected.Add(i.ItemId);
                }
            }
            else
            {
                expected = new();
                foreach (var box in room.RewardDropBoxIds)
                {
                    var l = new List<ItemDropEntry>();
                    if (!t.IndividualDrops.GetBoxItems(t.Items, (int) box, job, 2, true, l)) continue;
                    var seen = new HashSet<int>();
                    foreach (var d in l)           // GetBoxItems without the dedupe: the first entry of an id per box
                        if (seen.Add(d.ItemId)) expected.Add(d.ItemId);
                }
            }
            Assert.That(got.Select(x => x.ItemId).OrderBy(x => x), Is.EqualTo(expected.OrderBy(x => x)), $"room {room.Id}");
            for (int i = 1; i < got.Count; i++)
                Assert.That(RewardPreviewItem.Less(t.Items, got[i], got[i - 1]), Is.False, $"room {room.Id}: {got[i].ItemId} sorts before {got[i - 1].ItemId}");
            if (got.Count > 0) withItems++;
            if (raw is { Count: > 0 } && job == 0)
                Assert.That(got.Count + raw.Count(e => !RewardPreviewItem.Load(e).IsValid(job, level)), Is.EqualTo(raw.Count));
            if (raw == null && room.RewardDropBoxIds.Count > 0 && expected.Count == 0)
                TestContext.Out.WriteLine($"room {room.Id}: boxes {string.Join(",", room.RewardDropBoxIds)} list no item");
        }
        TestContext.Out.WriteLine($"rooms={rooms} withItems={withItems} job={job} level={level}");
        Assert.That(withItems, Is.GreaterThan(0));
    }

    /// <summary>
    /// The rooms that name a reward drop box or have a preview row (the round-group rooms aside) never show an empty grid: a box room's
    /// first box item, and a preview row's first item for a player it is meant for (its first job and level), is listed.
    /// </summary>
    [Test]
    public void NoRoomWithABoxOrAPreviewRowHasAnEmptyGrid()
    {
        var t = Tables();
        var empty = new List<uint>();
        foreach (var room in t.DungeonRooms.RoomList)
        {
            var preview = t.RewardPreviews.GetDungeon((int) room.Id);
            if (preview is { Count: > 0 })
            {
                var first = preview[0];
                int job = first.ReqJobs.Count > 0 ? first.ReqJobs[0] : 0, level = first.ReqLevelHigh > 0 ? first.ReqLevelHigh : 60;
                if (!DungeonRoomInfoPanel.PreviewItems(t, room, job, level).Any(x => x.ItemId == first.ItemId)) empty.Add(room.Id);
            }
            else if (room.RewardDropBoxIds.Count > 0 && DungeonRoomInfoPanel.PreviewItems(t, room, 0, 60).Count == 0 &&
                     room.RewardDropBoxIds.Any(b => !IndividualDropTable.IsGlobalBox((int) b) && t.IndividualDrops.Has((int) b)))
                empty.Add(room.Id);
        }
        Assert.That(empty, Is.Empty, "rooms with a box or a preview row and no item: " + string.Join(",", empty));
    }

    /// <summary>The room's description is the string table 0x15 text: a room with a uiDescription has one.</summary>
    [Test]
    public void TheDescriptionIsTheFieldEntranceUiDescription()
    {
        var t = Tables();
        var rows = new GameStrings(packs, new[] { "en" }).Table("stringfieldenterance", "uiDescription");
        int with = 0;
        foreach (var room in t.DungeonRooms.RoomList)
        {
            string text = t.StringTableText(0x15, room.Id);
            bool raw = rows.TryGetValue(room.Id.ToString(), out var r) && !string.IsNullOrEmpty(r);
            Assert.That(text.Length > 0, Is.EqualTo(raw), $"room {room.Id}");
            if (raw) with++;
        }
        TestContext.Out.WriteLine($"{with} of {t.DungeonRooms.RoomList.Count()} rooms have a description");
        if (with == 0) Assert.Ignore("this data has no room description");
    }
}
