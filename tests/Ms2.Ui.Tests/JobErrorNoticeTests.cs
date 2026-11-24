using System.Globalization;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The Job packet's mode 3 errors (CSceneStateGame::OnJob, scenestategame.cpp:7537-7565): every error byte against the
/// client's switch, and every named error's text in the data.</summary>
public class JobErrorNoticeTests
{
    /// <summary>The client's switch, line by line (scenestategame.cpp:7544-7562).</summary>
    static readonly Dictionary<int, int> ClientIds = new()
    {
        [9] = 0x76, [10] = 0x77, [2] = 0x78, [12] = 0x79, [11] = 0x7a, [3] = 0x7b, [4] = 0x7b, [5] = 0x7c, [15] = 0x7d, [8] = 0x7e,
        [14] = 0x7f,
    };

    static string Fake(int id) => id switch
    {
        0x80 => "full {0}",
        0x81 => "unknown {0}",
        _ => "c" + id.ToString("x", CultureInfo.InvariantCulture),
    };

    [Test]
    public void EveryErrorByteShowsTheClientsText()
    {
        int named = 0, formatted = 0;
        for (int e = 0; e < 256; e++)
        {
            var (notice, box) = JobErrorNotice.Text(e, 7, Fake);
            if (e == 0) { Assert.That((notice, box), Is.EqualTo(("", (string)null)), "0 shows nothing"); continue; }
            if (e == 18) { Assert.That((notice, box), Is.EqualTo(("full 7", "full 7")), "18: the box and the notice, with the value"); continue; }
            if (ClientIds.TryGetValue(e, out int id))
            {
                Assert.That((notice, box), Is.EqualTo((Fake(id), (string)null)), $"error {e}");
                named++;
            }
            else
            {
                Assert.That((notice, box), Is.EqualTo(("unknown " + e.ToString(CultureInfo.InvariantCulture), (string)null)), $"error {e}");
                formatted++;
            }
        }
        Assert.That((named, formatted), Is.EqualTo((11, 243)));
    }

    [Test]
    public void AnEmptyStringShowsNoNotice()
    {
        Assert.That(JobErrorNotice.Text(5, 0, _ => "").Notice, Is.Empty);
        Assert.That(JobErrorNotice.Text(5, 0, _ => null).Notice, Is.Empty);
    }

    /// <summary>Every error's text in each data set: the named ones but 5 and 11 (s_err_job_privilege, s_err_job_no_home: in no
    /// data set, so the client shows nothing), 18 with its value, the rest the unknown text.</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryErrorHasItsTextInTheData(string variable)
    {
        using var packs = new PackSet(RcGap.Data(variable));
        var t = new GameTables(packs, "en");
        var empty = new List<int>();
        for (int e = 1; e < 256; e++)
            if (JobErrorNotice.Text(e, 3, t.Common).Notice.Length == 0) empty.Add(e);
        Assert.That(empty, Is.EqualTo(new[] { 5, 11 }), "errors with no text");
        Assert.That(JobErrorNotice.Text(18, 3, t.Common).Notice, Does.Contain("3"), "s_err_job_inventory_full takes the value");
        Assert.That(JobErrorNotice.Text(9, 0, t.Common).Notice, Is.EqualTo(t.Common("s_err_job_not_enough_meso")));
        Assert.That(JobErrorNotice.Text(8, 0, t.Common).Notice, Is.EqualTo(t.Common("s_err_job_debuff")));
    }
}
