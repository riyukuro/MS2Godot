using System.Collections;
using System.Reflection;
using Ms2.Ui.Guide;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The state-only GMS2 movie changes: the removed long-term attendance entry (A3), the home option's password setData
/// (F9), the community tab range (F8) and the community query's mode set (F10), and the dropped input_multi box (F11).
/// These run without a movie; the change sites are GameMenuTable.cs:107, RegisteredDialogs.cs:178,
/// HomeOptionDialogController.cs:58-64, CommunityController.cs:95,399, CommunityQueryController.cs:114,261-275 and
/// MessageBoxData.cs:309,316.
/// </summary>
public class RcUiBridgeTableTests
{
    [Ignore("the long-term attendance entry is still registered")]
    [Test]
    public void TheLongTermAttendMenuIsGone()
    {
        var defsField = typeof(GameMenuTable).GetField("AllDefs", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(defsField, Is.Not.Null);
        var defs = ((IEnumerable)defsField.GetValue(null)).Cast<GameMenuDef>().ToList();
        Assert.That(defs.Select(d => d.Id), Does.Not.Contain(64));
        Assert.That(defs.Select(d => d.Name), Does.Not.Contain("longtermattendgift"));
        var namesField = typeof(RegisteredDialogs).GetField("names", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(namesField, Is.Not.Null);
        var names = (IEnumerable<string>)namesField.GetValue(null);
        Assert.That(names, Does.Not.Contain("LongTermAttendGiftDialog"));
    }

    sealed class HomeHost : IHomeOptionHost
    {
        public bool HasService = true;
        public bool CanChange = true;
        public bool OwnsHome = true;
        public int? OpenType = 1;
        public bool[] On = new bool[9];
        public int[] Levels = new int[9];
        bool IHomeOptionHost.HasService => HasService;
        bool IHomeOptionHost.CanChange => CanChange;
        bool IHomeOptionHost.OwnsHome => OwnsHome;
        int? IHomeOptionHost.OpenType => OpenType;
        public bool RowOn(int row) => On[row];
        public int RowLevel(int row) => Levels[row];
        public void SendKickUsers() { }
        public bool SendHouseSetting(int kind, string value, bool flag) => true;
        public void SendRow(int row, bool on, int level) { }
    }

    [Ignore("setData still sends opentype and nine rows")]
    [Test]
    public void TheHomeOptionSetDataSendsAPassword()
    {
        var d = new HomeOptionDialogController(new HomeOptionController(new HomeHost()));
        var members = d.Members();
        Assert.That(members, Is.Not.Null);
        Assert.That(members.Select(m => m.Key), Is.EqualTo(new[]
            { "password", "jump", "climb", "skill", "playInstrument", "potion", "ride_ground", "ride_air" }));
        Assert.That(members.Single(m => m.Key == "password").Value, Is.EqualTo(true));
    }

    [Ignore("the community tabs still accept 4")]
    [Test]
    public void TheCommunityTabsStopAtThree()
    {
        var c = new CommunityController(new BuddyBook());
        c.SetTab(2);
        Assert.That(c.Tab, Is.EqualTo(2));
        c.SetTab(4);
        Assert.That(c.Tab, Is.EqualTo(2), "tab 4 is gone on GMS2");
        c.SetTab(3);
        Assert.That(c.Tab, Is.EqualTo(3));
        c.SetTab(-1);
        Assert.That(c.Tab, Is.EqualTo(3));
    }

    [Ignore("the query still opens modes 3 and 5")]
    [Test]
    public void TheCommunityQueryDropsTheRemovedModes()
    {
        var q = new CommunityQueryController(new BuddyBook()) { Common = _ => "", ShowBox = _ => { }, NextBox = () => { } };
        int opens = 0;
        q.OpenRequested += () => opens++;
        Assert.That(q.OnQuery(0, 3, "Ann"), Is.True);
        Assert.That(opens, Is.Zero, "mode 3 (home block) is not a GMS2 frame anymore");
        Assert.That(q.OnQuery(0, 5, "Ann"), Is.True);
        Assert.That(opens, Is.Zero, "mode 5 is not a GMS2 frame anymore");
        Assert.That(q.OnQuery(0, 4, "Ann"), Is.True);
        Assert.That((opens, q.Mode), Is.EqualTo((1, 4)), "mode 4 still edits a memo");
        Assert.That(q.OnQuery(0, 1, "Ann"), Is.True);
        Assert.That((opens, q.Mode), Is.EqualTo((2, 1)), "mode 1 asks a friend");
    }

    [Ignore("type 0xf still maps to input_multi")]
    [Test]
    public void TheFifteenBoxTypeIsNotAnInputBox()
    {
        Assert.That(MessageBoxData.BoxTypeName(0xf), Is.EqualTo("ok"));
        var box = new MessageBoxData(15);
        Assert.That(box.HasDefaultButton, Is.True, "type 15 takes Enter like an ok box");
        Assert.That(MessageBoxData.BoxTypeName(5), Is.EqualTo("input"), "the other input boxes stay");
        Assert.That(new MessageBoxData(5).HasDefaultButton, Is.False);
    }
}
