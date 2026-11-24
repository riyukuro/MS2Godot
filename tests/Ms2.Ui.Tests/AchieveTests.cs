using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>CAchieveManager's trophy tally (AchieveBook) over the Achieve packet, and CAchieveData's load (needs MS2_DATA).</summary>
public class AchieveTests
{
    static AchieveProgress P(byte state, int cur, long count, params (int Grade, long Value)[] values) =>
        new(state, 0, cur, 1, false, count, values.ToDictionary(v => v.Grade, v => v.Value));

    static Dictionary<int, AchieveData> Table() => new()
    {
        // type 2 category 1 / 2 / 3; type 1; a trophy whose grade 1 has no condition
        [21100001] = new(21100001, 2, 1, 1, new Dictionary<int, int> { [1] = 41, [2] = 41, [3] = 41 }),
        [22200002] = new(22200002, 2, 2, 1, new Dictionary<int, int> { [1] = 95, [2] = 95 }),
        [23300003] = new(23300003, 2, 3, 1, new Dictionary<int, int> { [1] = 70 }),
        [11100004] = new(11100004, 1, 1, 1, new Dictionary<int, int> { [1] = 41 }),
        [21100005] = new(21100005, 2, 1, 1, new Dictionary<int, int> { [1] = 0 }),
    };

    static AchieveMessage Rows(params (int, int, AchieveProgress)[] rows) => new AchieveMessage.Rows(rows);

    [Test]
    public void TallyCountsTheReachedGradesOfType2TrophiesByCategory()
    {
        var book = new AchieveBook(Table());
        Assert.That(book.Apply(new AchieveMessage.Reload()), Is.False);
        Assert.That(book.Has(21100001) && book.Has(11100004), Is.True);
        Assert.That(book.Has(21100005), Is.False, "a grade without a condition type: CAchieve::Init fails");
        Assert.That(book.Sum, Is.EqualTo(0u));

        book.Apply(Rows(
            (21100001, 1, P(2, 3, 2, (1, 100), (2, 200))),             // grades 1, 2 reached (3 has no value)
            (22200002, 9, P(3, 2, 2, (1, 1), (2, 1))),                 // grade 9 missing: Init takes the lowest
            (23300003, 1, P(2, 1, 0, (1, 1))),                         // not started: mode 1 tallies it anyway
            (11100004, 1, P(3, 1, 1, (1, 1))),                         // type 1: no tally
            (21100005, 1, P(3, 1, 1, (1, 1))),                         // dropped
            (99999999, 1, P(3, 1, 1, (1, 1)))));                       // no data: dropped
        Assert.That(book.Tally, Is.EqualTo(new[] { 2, 2, 1 }));
        Assert.That(book.Sum, Is.EqualTo(5u));
        Assert.That(book.Has(99999999), Is.False);

        // mode 2: the same grade and state: no rebuild; a new grade: RebuildTally over the started trophies only
        Assert.That(book.Apply(new AchieveMessage.Update(21100001, P(2, 3, 2, (1, 100), (2, 200), (3, 300)))), Is.True);
        Assert.That(book.Sum, Is.EqualTo(5u), "same grade and state: the tally is not rebuilt");
        Assert.That(book.Apply(new AchieveMessage.Update(22200002, P(3, 3, 3, (1, 1), (2, 1), (3, 1)))), Is.True);
        Assert.That(book.Tally, Is.EqualTo(new[] { 3, 3, 0 }), "rebuilt: the unstarted category-3 trophy no longer counts");
        Assert.That(book.Apply(new AchieveMessage.Update(11100004, P(3, 1, 1))), Is.True);
        Assert.That(book.Apply(new AchieveMessage.Update(12345678, P(3, 1, 1))), Is.False, "a trophy the player lacks");
    }

    [Test]
    public void HasCompletedOfTypeSkipsAccountTrophiesAndFiltersByType()
    {
        var t = Table();
        t[21100001] = t[21100001] with { Account = true, MaxGrade = 3 };
        t[22200002] = t[22200002] with { MaxGrade = 2 };
        t[11100004] = t[11100004] with { MaxGrade = 1 };
        var book = new AchieveBook(t);
        book.Apply(Rows(
            (21100001, 1, P(2, 3, 2, (1, 100), (2, 200))),             // completed, but an account trophy
            (22200002, 1, P(2, 1, 1, (1, 1))),                         // type 2, not completed
            (11100004, 1, P(3, 1, 1, (1, 1)))));                       // type 1, one grade: completed at state 3
        Assert.That(book.Find(21100001)!.IsCompleted && book.Find(11100004)!.IsCompleted, Is.True);
        Assert.That(book.HasCompletedOfType(2), Is.False, "the account trophy is skipped");
        Assert.That(book.HasCompletedOfType(1), Is.True);
        Assert.That(book.HasCompletedOfType(null), Is.True, "no type: any");
        book.Apply(new AchieveMessage.Update(22200002, P(3, 2, 2, (1, 1), (2, 1))));
        Assert.That(book.HasCompletedOfType(2), Is.True);
    }

    [Test]
    public void DataLoadsTheAchieveFiles()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var table = AchieveData.LoadAll(new PackSet(d));
        TestContext.Progress.WriteLine($"achieve data: {table.Count} trophies in {sw.ElapsedMilliseconds} ms");
        Assert.That(table, Has.Count.GreaterThan(1000));
        var a = table[21100001];                                       // <achieves id="21100001"> grade 1: job_change
        Assert.That((a.Type, a.Category, a.FirstGrade, a.GradeConditions[1]), Is.EqualTo((2, 1, 1, 71)));
        Assert.That(table.Values.Count(t => t.Type == 2), Is.GreaterThan(500));
    }
}
