using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The attendance window (uiattendgiftdialog, UIAttendGiftDialog @0x14102BAB0, uiattendgiftdialog.cpp) run by its own ActionScript, on the
/// restored data (MS2_DATA) and the GMS2 data (MS2_GMS2_DATA): the event buttons, the 28 gift slots, the pay and use-point buttons, the
/// requests it sends and the cases it refuses. The window is new (it had no controller), so these do not compile on the old code.
/// </summary>
public class AttendGiftDialogTests
{
    const long Now = 1_700_000_000;
    // Items every data set has: a consumable, an equipment piece and a pet item (itemdata/002, /110, /600).
    const int Potion = 20000004, Pet = 60000001;

    static PackSet packs;
    static MovieSource src;
    static PackSet gms2Packs;
    static MovieSource gms2Src;

    static (PackSet, MovieSource) Restored()
    {
        if (packs == null)
        {
            var d = RcGap.Data("MS2_DATA");
            packs = TestData.Packs(d);
            src = TestData.Movies(packs);
        }
        return (packs, src);
    }

    static (PackSet, MovieSource) Gms2()
    {
        if (gms2Packs == null)
        {
            var p = TestData.Packs(RcGap.Data("MS2_GMS2_DATA"));
            gms2Src = TestData.Movies(p);
            gms2Packs = p;
        }
        return (gms2Packs, gms2Src);
    }

    static AttendGiftEvent Event(int id, byte payType = 0, int maxPay = 0, bool usePoints = true, byte kind = 0, long begin = 1_699_000_000,
        long end = 1_701_000_000, string desc = "", int coupon = 0, long cost = 0, params GuildStorageItem[] gifts) =>
        new(id, begin, end, "n" + id, desc, false, usePoints, 0, kind, 0, payType, maxPay, cost, coupon, gifts);

    static GuildStorageItem Gift(int id, int rank = 1, int count = 1) => new(id, rank, count, false, false, false, false);

    static GameEventMessage Msg(byte kind, params AttendGiftEvent[] e) =>
        new(kind, Array.Empty<ShutdownInteractObjectEvent>(), Array.Empty<int>(), false) { AttendGiftEvents = e };

    sealed class Rig
    {
        public UiHost Host;
        public AttendGiftDialogController C;
        public AttendGiftState State;
        public AttendEvents Attend;
        public GameEventUserValues Values;
        public UiModel Model;
        public readonly List<IOutPacket> Sent = new();
        public readonly List<MessageBoxData> Boxes = new();
        public readonly List<string> Plain = new();
        public long Coupon;
        public bool KeyDown, HudOn;
        public readonly List<string> Details = new(), Previews = new();

        public object Child(string n) => Host.Child(n);
        public object Get(object o, string m) => Host.Get(o, m);
        public bool Visible(string n) => Rt.ToBoolean(Get(Child(n), "visible"));
        public string Label(string n) => Rt.ToString(Get(Child(n), "label"));
        public object Slot(int i) => Host.Invoke(Get(Child("m_giftList"), "dataProvider"), "requestItemAt", i);
        public int Length => (int)Rt.ToNumber(Get(Get(Child("m_giftList"), "dataProvider"), "length"));
        public bool Flag(int i, string m) => Rt.ToBoolean(Get(Slot(i), m));
        public void Click(string n) => Host.Invoke(Child(n), "dispatchEvent", Host.RT.Construct(Host.RT.GetClass("scaleform.clik.events.ButtonEvent"), new object[] { "buttonClick" }));
        public byte[] Bytes(IOutPacket p) { var w = new Maple2.PacketLib.Tools.ByteWriter(); p.Write(w); return w.ToArray(); }
    }

    static Rig Open((PackSet packs, MovieSource src) data, AttendGiftEvent[] events, Action<Rig> before = null, Action<GameEventUserValues> values = null,
        long now = Now)
    {
        var (packs, src) = data;
        var r = new Rig();
        r.Model = new UiModel(new GameTables(packs, "en")) { FeatureEnabledByName = f => LobbyFeatures.IsEnabled(packs, f), HasLocalPlayer = true };
        r.Model.Player.Level = 50;
        r.Attend = new AttendEvents();
        r.Values = new GameEventUserValues(() => now);
        values?.Invoke(r.Values);
        r.State = new AttendGiftState(r.Attend, r.Values, () => now, k => r.Model.Tables.GameEventStrings.GetString(k));
        r.Attend.Apply(Msg(0, events));
        var c = new AttendGiftDialogController(r.State)
        {
            Send = r.Sent.Add,
            ShowBox = r.Boxes.Add,
            PlainBox = r.Plain.Add,
            CheckboxChecked = (b, n) => n == 0 ? b.Check0Selected : b.Check1Selected,
            FindItemUid = _ => r.Coupon,
            ActionKeyDown = _ => r.KeyDown,
            IsPreviewHudOn = () => r.HudOn,
            PreviewItem = it => r.Previews.Add(it.Data.Id.ToString()),
            ShowDetail = r.Details.Add,
        };
        r.C = c;
        before?.Invoke(r);
        var (rt, p) = src.Open(c.Movie, x => x.Budget = 40_000_000);
        p.StageW = 1280; p.StageH = 720;
        r.Host = new UiHost(p, c, r.Model);
        Assert.That(r.Host.Load(), Is.True, "load: " + string.Join("; ", rt.Errors));
        for (int i = 0; i < 30; i++) p.Tick();
        return r;
    }

    static IEnumerable<TestCaseData> Sets()
    {
        yield return new TestCaseData("restore").SetName("Restore");
        yield return new TestCaseData("gms2").SetName("Gms2");
    }

    static (PackSet, MovieSource) Data(string set) => set == "restore" ? Restored() : Gms2();

    static AttendGiftEvent Standard(int id = 1, byte payType = 1, int maxPay = 3, int gifts = 5, bool usePoints = true, string desc = "d", int coupon = 0) =>
        Event(id, payType, maxPay, usePoints, 0, desc: desc, coupon: coupon, cost: 1000,
            gifts: Enumerable.Range(0, gifts).Select(i => Gift(i % 2 == 0 ? Potion : Pet, 1 + i % 3, 1 + i)).ToArray());

    static GameEventVariable V(int type, int id, int value) => new(type, id, value.ToString(), Now + 1000);

    [TestCaseSource(nameof(Sets))]
    public void TheWindowLoadsAndShowsTheEventsGiftsInTheirSlots(string set)
    {
        var data = Data(set);
        var r = Open(data, new[] { Standard(1, gifts: 3) }, values: v => v.Apply(new GameEventUserValueMessage(1, 0, new[] { V(0x66, 1, 1) })));
        Assert.That(r.C.Movie, Is.EqualTo("uiattendgiftdialog"));
        Assert.That(r.Length, Is.EqualTo(AttendGiftDialogController.SlotCount), "28 slots, the gifts first and empty objects after");
        for (int i = 0; i < 3; i++)
        {
            var slot = r.Slot(i);
            Assert.That((uint)Rt.ToNumber(r.Get(slot, "itemID")), Is.EqualTo(i % 2 == 0 ? (uint)Potion : (uint)Pet), $"slot {i}");
            Assert.That((int)Rt.ToNumber(r.Get(slot, "index")), Is.EqualTo(i + 1), "the day, from 1");
            Assert.That(r.Get(slot, "disableDrag"), Is.Not.Null);
        }
        for (int i = 3; i < 28; i++) Assert.That(Rt.IsNullish(r.Get(r.Slot(i), "itemID")), Is.True, $"slot {i} past the gifts is empty");
        Assert.That(r.Sent, Is.Empty, "nothing is sent without the window-opened flag (bool 100)");
        Assert.That(r.Visible("m_eventButton0"), Is.True);
        Assert.That(r.Visible("m_eventButton1"), Is.False, "one event: the other buttons are hidden");
        Assert.That(r.Visible("m_eventButton2"), Is.False);
    }

    [TestCaseSource(nameof(Sets))]
    public void TheDaysBeforeTheNextAttendanceAreDarkenedAndCheckedAndTheNextOneIsBordered(string set)
    {
        var data = Data(set);
        // 3 days attended and not yet today: days 1..3 done, day 4 is the one to attend
        var r = Open(data, new[] { Standard(1, gifts: 6) }, values: v => v.Apply(new GameEventUserValueMessage(1, 0, new[] { V(0x66, 1, 3) })));
        var state = Enumerable.Range(0, 6).Select(i => (r.Flag(i, "isDarken"), r.Flag(i, "isCheck"), r.Flag(i, "isBorder"))).ToArray();
        Assert.That(state, Is.EqualTo(new[]
        {
            (true, true, false), (true, true, false), (true, true, false), (false, false, true), (false, false, false), (false, false, false),
        }));
        // checked in today: the 3rd is the current one, bordered and checked
        var r2 = Open(data, new[] { Standard(1, gifts: 6) }, values: v => v.Apply(new GameEventUserValueMessage(1, 0,
            new[] { V(0x66, 1, 3), new GameEventVariable(0x65, 1, Now.ToString(), Now + 1000) })));
        var state2 = Enumerable.Range(0, 6).Select(i => (r2.Flag(i, "isDarken"), r2.Flag(i, "isCheck"), r2.Flag(i, "isBorder"))).ToArray();
        Assert.That(state2, Is.EqualTo(new[]
        {
            (true, true, false), (true, true, false), (false, true, true), (false, false, false), (false, false, false), (false, false, false),
        }));
    }

    [TestCaseSource(nameof(Sets))]
    public void MoreGiftsThanSlotsFillOnlyTheTwentyEightAndTheListReadsTheCountAsText(string set)
    {
        var r = Open(Data(set), new[] { Standard(1, gifts: 30) });
        Assert.That(r.Length, Is.EqualTo(28));
        Assert.That(Rt.ToString(r.Get(r.Slot(2), "itemCount")), Is.EqualTo("3"), "SetItemCountValue: the count as text");
        Assert.That((int)Rt.ToNumber(r.Get(r.Slot(27), "index")), Is.EqualTo(28));
    }

    [TestCaseSource(nameof(Sets))]
    public void TheEventButtonsShowTheActiveEventsAndAClickSelectsOne(string set)
    {
        var r = Open(Data(set), new[] { Standard(1), Standard(2, payType: 2), Standard(3, usePoints: false), Standard(4) });
        Assert.That(new[] { r.Visible("m_eventButton0"), r.Visible("m_eventButton1"), r.Visible("m_eventButton2") }, Is.EqualTo(new[] { true, true, true }),
            "four events: three buttons");
        Assert.That(Rt.ToBoolean(r.Get(r.Child("m_eventButton0"), "selected")), Is.True);
        Assert.That(Rt.ToBoolean(r.Get(r.Child("m_eventButton1"), "selected")), Is.False);
        Assert.That(r.State.Selected.Id, Is.EqualTo(1));
        r.Click("m_eventButton1");
        Assert.That(r.State.Selected.Id, Is.EqualTo(2));
        Assert.That(Rt.ToBoolean(r.Get(r.Child("m_eventButton1"), "selected")), Is.True);
        Assert.That(Rt.ToBoolean(r.Get(r.Child("m_eventButton0"), "selected")), Is.False);
        Assert.That(r.Label("m_payAttendButton"), Is.EqualTo(r.Model.Tables.Common("s_attendGift_payAttend_merat_button")), "the second event pays with merat");
        Assert.That(r.Sent, Is.Empty, "choosing an event sends nothing");
    }

    [TestCaseSource(nameof(Sets))]
    public void ThePayButtonFollowsThePayTypeAndTheUsePointButtonThePoints(string set)
    {
        var data = Data(set);
        foreach (var (pay, key) in new[] { ((byte)0, (string)null), ((byte)1, "s_attendGift_payAttend_meso_button"), ((byte)2, "s_attendGift_payAttend_merat_button"), ((byte)3, (string)null) })
        {
            var r = Open(data, new[] { Standard(1, payType: pay) });
            Assert.That(r.Visible("m_payAttendButton"), Is.EqualTo(key != null), $"pay type {pay}");
            if (key != null) Assert.That(r.Label("m_payAttendButton"), Is.EqualTo(r.Model.Tables.Common(key)));
        }
        // an event that does not use its points up as a title shows the use-point button with them
        var p = Open(data, new[] { Standard(1, usePoints: false) }, values: v => v.Apply(new GameEventUserValueMessage(1, 0, new[] { V(0x69, 1, 7) })));
        Assert.That(p.Visible("m_usePointButton"), Is.True);
        Assert.That(Rt.ToBoolean(p.Get(p.Child("m_usePointButton"), "enabled")), Is.True);
        Assert.That(p.Visible("m_footerText"), Is.False);
        var none = Open(data, new[] { Standard(1, usePoints: false) });
        Assert.That(Rt.ToBoolean(none.Get(none.Child("m_usePointButton"), "enabled")), Is.False, "no points: disabled");
        var uses = Open(data, new[] { Standard(1, usePoints: true) });
        Assert.That((uses.Visible("m_usePointButton"), uses.Visible("m_footerText")), Is.EqualTo((false, true)));
        // a points value that arrives later updates the button of the loaded window
        none.Values.Apply(new GameEventUserValueMessage(1, 0, new[] { V(0x69, 1, 2) }));
        Assert.That(Rt.ToBoolean(none.Get(none.Child("m_usePointButton"), "enabled")), Is.True);
    }

    [TestCaseSource(nameof(Sets))]
    public void TheUsePointButtonAsksForTheShownEventAndTheOpenFlagAsksForTheWindow(string set)
    {
        var data = Data(set);
        var r = Open(data, new[] { Standard(170042, usePoints: false) }, values: v => v.Apply(new GameEventUserValueMessage(1, 0, new[] { V(0x69, 170042, 4) })));
        r.Click("m_usePointButton");
        Assert.That(r.Sent.Select(r.Bytes).Select(Convert.ToHexString), Is.EqualTo(new[] { "083A980200" }), "mode 8 and the event id");
        var opened = Open(data, new[] { Standard(1) }, values: v => v.Apply(new GameEventUserValueMessage(1, 0, new[] { new GameEventVariable(100, 0, "1", Now + 1000) })));
        Assert.That(opened.Sent.Select(opened.Bytes).Select(Convert.ToHexString), Is.EqualTo(new[] { "02" }), "mode 2 on load when bool 100 is set");
    }

    [TestCaseSource(nameof(Sets))]
    public void APayClickWithEveryDayAttendedOrEveryEarlyParticipationUsedGivesAPlainBoxAndSendsNothing(string set)
    {
        var data = Data(set);
        var all = Open(data, new[] { Standard(1, gifts: 4, maxPay: 3) }, values: v => v.Apply(new GameEventUserValueMessage(1, 0, new[] { V(0x66, 1, 4) })));
        all.Click("m_payAttendButton");
        Assert.That(all.Plain, Is.EqualTo(new[] { "s_attendGift_payAttend_attend_all" }));
        Assert.That((all.Sent, all.Boxes), Is.EqualTo((new List<IOutPacket>(), new List<MessageBoxData>())), "no packet, no confirm box");

        var used = Open(data, new[] { Standard(1, gifts: 6, maxPay: 3, payType: 2) }, values: v => v.Apply(new GameEventUserValueMessage(1, 0, new[] { V(0x66, 1, 1), V(0x67, 1, 3) })));
        used.Click("m_payAttendButton");
        Assert.That(used.Plain, Is.EqualTo(new[] { "s_attendGift_payAttend_uesd_all" }));
        Assert.That(used.Sent, Is.Empty, "the merat pay does not send its mode 3 once the limit is reached");
        Assert.That(used.Boxes, Is.Empty);
    }

    [TestCaseSource(nameof(Sets))]
    public void APayClickWithNoPayTypeOrNoEventDoesNothing(string set)
    {
        var data = Data(set);
        var none = Open(data, new[] { Standard(1, payType: 0) });
        none.C.OnPay();
        Assert.That((none.Sent.Count, none.Boxes.Count, none.Plain.Count), Is.EqualTo((0, 0, 0)), "pay type 0");
        var empty = Open(data, Array.Empty<AttendGiftEvent>());
        empty.C.OnPay();
        empty.C.SendSelectedEventRequest();
        Assert.That((empty.Sent.Count, empty.Boxes.Count, empty.Plain.Count), Is.EqualTo((0, 0, 0)), "no event shown");
        Assert.That(empty.Visible("m_eventButton0"), Is.False);
        Assert.That(empty.Visible("m_payAttendButton"), Is.False);
    }

    [TestCaseSource(nameof(Sets))]
    public void TheMesoPayAsksFirstAndOnlyYesSendsTheEarlyParticipationWithTheCouponWhenChecked(string set)
    {
        var r = Open(Data(set), new[] { Standard(170042, payType: 1, maxPay: 3, gifts: 6, coupon: Potion) });
        r.Coupon = 0x0102030405060708;
        r.Click("m_payAttendButton");
        Assert.That(r.Sent, Is.Empty, "meso: nothing is sent before the answer");
        var box = r.Boxes.Single();
        Assert.That(box.Type, Is.EqualTo(0x19));
        Assert.That(box.Check0Selected, Is.True, "the coupon checkbox is on when the character has the item");
        Assert.That(box.Check0Enabled, Is.True);
        Assert.That(box.Callback(1, box), Is.True, "no: handled");
        Assert.That(r.Sent, Is.Empty, "no sends nothing");
        Assert.That(box.Callback(0, box), Is.True);
        Assert.That(r.Sent.Select(r.Bytes).Select(Convert.ToHexString), Is.EqualTo(new[] { "043A9802000807060504030201" }));
        r.Sent.Clear();
        box.Check0Selected = false;
        box.Callback(0, box);
        Assert.That(r.Sent.Select(r.Bytes).Select(Convert.ToHexString), Is.EqualTo(new[] { "043A9802000000000000000000" }), "the box's checkbox off: coupon 0");
    }

    [TestCaseSource(nameof(Sets))]
    public void TheMeratPaySendsModeThreeBeforeTheBoxAndTheCouponIsOffWithoutTheItem(string set)
    {
        var r = Open(Data(set), new[] { Standard(1, payType: 2, maxPay: 3, gifts: 6, coupon: Potion) });
        r.Click("m_payAttendButton");
        Assert.That(r.Sent.Select(r.Bytes).Select(Convert.ToHexString), Is.EqualTo(new[] { "03" }));
        var box = r.Boxes.Single();
        Assert.That((box.Check0Selected, box.Check0Enabled), Is.EqualTo((false, false)), "no coupon item in the bag: the checkbox is off and disabled");
        box.Callback(0, box);
        Assert.That(r.Sent.Select(r.Bytes).Select(Convert.ToHexString), Is.EqualTo(new[] { "03", "04010000000000000000000000" }));
    }

    [TestCaseSource(nameof(Sets))]
    public void TheDetailButtonOpensTheDescriptionOnlyForAnEventThatHasOne(string set)
    {
        var data = Data(set);
        var with = Open(data, new[] { Standard(1, desc: "about") });
        Assert.That(Rt.ToBoolean(with.Get(with.Child("m_detailButton"), "enabled")), Is.True);
        with.Click("m_detailButton");
        Assert.That(with.Details, Is.EqualTo(new[] { "about" }));
        var without = Open(data, new[] { Standard(1, desc: "") });
        Assert.That(Rt.ToBoolean(without.Get(without.Child("m_detailButton"), "enabled")), Is.False);
    }

    [TestCaseSource(nameof(Sets))]
    public void TheRightClickPreviewNeedsTheActionKeyAHudAndAnItem(string set)
    {
        var r = Open(Data(set), new[] { Standard(1, gifts: 2) });
        var slot = r.Slot(0);
        r.C.OnCallback("OnRightClickRenderer", new object[] { slot });
        Assert.That(r.Previews, Is.Empty, "the preview key is not held");
        r.KeyDown = true;
        r.C.OnCallback("OnRightClickRenderer", new object[] { slot });
        Assert.That(r.Previews, Is.Empty, "the preview HUD is off");
        r.HudOn = true;
        r.C.OnCallback("OnRightClickRenderer", new object[] { slot });
        Assert.That(r.Previews, Is.EqualTo(new[] { Potion.ToString() }));
        r.C.OnCallback("OnRightClickRenderer", new object[] { r.Slot(5) });
        r.C.OnCallback("OnRightClickRenderer", new object[] { null });
        Assert.That(r.Previews.Count, Is.EqualTo(1), "an empty slot and a null slot give no preview");
        Assert.That(r.C.OnCallback("Other", new object[] { slot }), Is.False);
    }

    [TestCaseSource(nameof(Sets))]
    public void AnEventThatEndsWhileOpenClosesTheWindowAndAStartedOneAddsItsButton(string set)
    {
        var r = Open(Data(set), new[] { Standard(1) });
        r.Attend.Apply(Msg(1, Standard(2)));
        Assert.That(r.Visible("m_eventButton1"), Is.True, "a started event refreshes the open window");
        r.Attend.Apply(new GameEventMessage(2, Array.Empty<ShutdownInteractObjectEvent>(), new[] { 1 }, false));
        Assert.That(r.State.Selected.Id, Is.EqualTo(2), "the shown one ended: the first active is shown");
        Assert.That(r.Visible("m_eventButton1"), Is.False);
        r.Attend.Apply(new GameEventMessage(2, Array.Empty<ShutdownInteractObjectEvent>(), new[] { 2 }, false));
        for (int i = 0; i < 120 && !r.C.IsClosed; i++) r.Host.Player.Tick();      // the close tween runs, then OnEndClose unloads
        Assert.That(r.C.IsClosed, Is.True, "no active event left: the window closes");
    }

    /// <summary>The whole list of what the window varies by: the pay type (0..3) x the kind (0..3) x uses points x the gift count (0..30),
    /// each with and without the character's own days: the window loads, shows at most 28 slots, and sends nothing by itself.</summary>
    [TestCaseSource(nameof(Sets))]
    public void EveryPayTypeKindAndGiftCountLoadsWithoutSendingAnything(string set)
    {
        var data = Data(set);
        int cases = 0;
        foreach (byte pay in new byte[] { 0, 1, 2, 3 })
        foreach (byte kind in new byte[] { 0, 1, 2, 3 })
        foreach (bool points in new[] { false, true })
        foreach (int gifts in new[] { 0, 1, 27, 28, 30 })
        {
            var e = Event(7, pay, 2, points, kind, gifts: Enumerable.Range(0, gifts).Select(i => Gift(Potion, 1, i + 1)).ToArray());
            var r = Open(data, new[] { e }, values: v => v.Apply(new GameEventUserValueMessage(1, 0, new[] { V(0x66, 7, gifts / 2), V(0x69, 7, 3), V(0x6a, 7, 1) })));
            // kind 1 and 3 events are active by their own user values: 0x6a-less ValueId 0 reads absent for 1 (inactive) and present for 3
            bool active = kind != 1 && !(kind == 3 && r.Values.Has(0, 0));
            if (active) Assert.That(r.Length, Is.EqualTo(28), $"pay {pay} kind {kind} points {points} gifts {gifts}");
            else Assert.That(r.State.Selected, Is.Null, $"pay {pay} kind {kind}: not active");
            Assert.That(r.Sent, Is.Empty, $"pay {pay} kind {kind} points {points} gifts {gifts}");
            cases++;
        }
        Assert.That(cases, Is.EqualTo(4 * 4 * 2 * 5));
    }

    /// <summary>Every string the window and its error notices read exists in both data sets' common table (each a non-empty text).</summary>
    [TestCaseSource(nameof(Sets))]
    public void EveryStringTheWindowReadsExists(string set)
    {
        var model = new UiModel(new GameTables(Data(set).Item1, "en"));
        var keys = new List<string>
        {
            "s_attendGift_title_name", "s_attendGift_event_period", "s_attend_gift_item_require", "s_common_month_day",
            "s_attendGift_payAttend_meso", "s_attendGift_payAttend_merat", "s_attendGift_payAttend_coupon",
            "s_attendGift_payAttend_meso_button", "s_attendGift_payAttend_merat_button", "s_attendGift_usePoint_button",
            "s_attendGift_payAttend_attend_all", "s_attendGift_payAttend_uesd_all",
        };
        foreach (int i in Enumerable.Range(0, 256)) if (AttendGiftState.ErrorKey((byte)i) is { } k) keys.Add(k);
        var missing = keys.Where(k => string.IsNullOrEmpty(model.Tables.Common(k))).ToList();
        Assert.That(missing, Is.Empty, string.Join(", ", missing));
    }
}
