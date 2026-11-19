using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>UIAchieveDialog's list rules (AchieveController) over a small trophy book, with no movie loaded.</summary>
public class AchieveDialogTests
{
    static AchieveProgress P(byte state, int cur, long count, bool bookmark = false, params (int Grade, long Value)[] values) =>
        new(state, 0, cur, 1, bookmark, count, values.ToDictionary(v => v.Grade, v => v.Value));

    static AchieveData D(int id, int type, string categoryTag = "", int maxGrade = 2) =>
        new(id, type, (id / 1000000) % 10, 1, new Dictionary<int, int> { [1] = 41, [2] = 41 }) { MaxGrade = maxGrade, CategoryTag = categoryTag };

    static (AchieveBook Book, AchieveController Dialog) Make(params AchieveData[] data)
    {
        var table = data.ToDictionary(d => d.Id);
        var book = new AchieveBook(table, data);
        book.Apply(new AchieveMessage.Reload());
        var texts = new AchieveTexts(new(), new()) { Data = book.DataOf, Find = book.Find };
        return (book, new AchieveController(book, texts, new AchieveCategoryTable(new List<AchieveCategory>())));
    }

    [Test]
    public void RowFilterKeepsTheTabTagsAndDropdown()
    {
        var (book, c) = Make(D(21100001, 2, "Combat"), D(22200002, 2, "Life"), D(91100003, 9), D(81100004, 8));
        book.Apply(new AchieveMessage.Rows(new[]
        {
            (21100001, 1, P(2, 1, 5)),                       // in progress, started
            (22200002, 1, P(3, 2, 1, true)),                 // done, bookmarked
            (91100003, 1, P(3, 2, 1)),                       // an emblem (type 9), done
            (81100004, 1, P(2, 1, 1)),                       // type 8: only in the debug mode
        }));
        var all = new AchieveController.FilterOptions();
        Assert.That(c.RowFilter(book.Find(21100001), "", all), Is.True);
        Assert.That(c.RowFilter(book.Find(91100003), "", all), Is.False, "type 9 only in the survival (emblem) list");
        Assert.That(c.RowFilter(book.Find(91100003), "", new AchieveController.FilterOptions { Survival = true }), Is.True);
        Assert.That(c.RowFilter(book.Find(21100001), "", new AchieveController.FilterOptions { Survival = true }), Is.False);
        Assert.That(c.RowFilter(book.Find(81100004), "", all), Is.False);
        c.DebugMode = () => true;
        Assert.That(c.RowFilter(book.Find(81100004), "", all), Is.True);
        c.DebugMode = () => false;

        Assert.That(c.RowFilter(book.Find(21100001), "", new AchieveController.FilterOptions { CategoryTag = "Life" }), Is.False);
        Assert.That(c.RowFilter(book.Find(22200002), "", new AchieveController.FilterOptions { CategoryTag = "Life" }), Is.True);
        Assert.That(c.RowFilter(book.Find(21100001), "", new AchieveController.FilterOptions { Filter = 1 }), Is.True, "in progress");
        Assert.That(c.RowFilter(book.Find(22200002), "", new AchieveController.FilterOptions { Filter = 1 }), Is.False);
        Assert.That(c.RowFilter(book.Find(22200002), "", new AchieveController.FilterOptions { Filter = 2 }), Is.True, "done");
        Assert.That(c.RowFilter(book.Find(21100001), "", new AchieveController.FilterOptions { Filter = 3 }), Is.False);
        Assert.That(c.RowFilter(book.Find(22200002), "", new AchieveController.FilterOptions { Filter = 3 }), Is.True, "bookmarked");
    }

    [Test]
    public void ALockedTrophyShowsOnlyWhenDoneOrRewardable()
    {
        var locked = D(21100001, 2) with { Locking = 1 };
        var (book, c) = Make(locked, D(21100002, 2) with { Locking = 1 });
        book.Apply(new AchieveMessage.Rows(new[] { (21100001, 1, P(2, 1, 1)), (21100002, 1, P(3, 2, 1)) }));
        var all = new AchieveController.FilterOptions();
        Assert.That(c.RowFilter(book.Find(21100001), "", all), Is.False, "locked, in progress");
        Assert.That(c.RowFilter(book.Find(21100002), "", all), Is.True, "locked but done: hidden, listed");
    }

    [Test]
    public void HeroRowsGiveAnEmptyRowWhenNothingIsListed()
    {
        var (_, empty) = Make();
        Assert.That(empty.HeroRows("", new AchieveController.FilterOptions()).Single()["state"], Is.EqualTo("empty"));

        var (book, c) = Make(D(91100003, 9));
        book.Apply(new AchieveMessage.Rows(new[] { (91100003, 1, P(3, 2, 1)) }));
        var row = c.HeroRows("", new AchieveController.FilterOptions()).Single();
        Assert.That((row["state"], row["search"]), Is.EqualTo(("empty", 1)), "trophies, but none passes");
    }

    [Test]
    public void ClearRecordsAreNewestFirstThenHigherGrade()
    {
        var (book, _) = Make(D(21100001, 2), D(22200002, 2));
        book.Apply(new AchieveMessage.Rows(new[]
        {
            (21100001, 1, P(3, 2, 1, false, (1, 100), (2, 300))),
            (22200002, 1, P(3, 2, 1, false, (1, 300), (2, 200))),
        }));
        Assert.That(AchieveController.ClearRecords(book), Is.EqualTo(new[]
        {
            (300L, 21100001, 2), (300L, 22200002, 1), (200L, 22200002, 2), (100L, 21100001, 1),
        }));
    }
}
