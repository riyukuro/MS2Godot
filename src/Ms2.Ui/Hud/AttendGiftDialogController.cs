using Ms2.Avm2;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Quest;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>
/// The window of the attendance gifts, UIAttendGiftDialog (client/ms2/uiattendgiftdialog.cpp, 0x14102bab0-0x14102f180; UI id 0x4eb8,
/// movie uiattendgiftdialog) over <see cref="AttendGiftState"/> (the part of the client's one dialog object that outlives the window:
/// the events, the request and the selection). Up to three event buttons (m_eventButton0..2) pick an event, m_giftList shows its 28
/// days of gifts, m_payAttendButton asks to attend a missed day early (for meso or merat), m_usePointButton spends the event's points
/// and m_detailButton shows its description.
/// STILL-OPEN: GetClientPlatform()->vfunc_43(2, 0, 1000) at the end of OnLoadComplete (the platform's call, not recovered);
/// FUN_1418fe220, the detail button's description window (<see cref="ShowDetail"/> is the owner's); the item preview window of the
/// right click (<see cref="PreviewItem"/> is the owner's, as the shop's).
/// </summary>
public sealed class AttendGiftDialogController : UiController
{
    public const int UiId = 0x4eb8;
    /// <summary>The gift slots of the list (UpdateGiftList's 0x1c).</summary>
    public const int SlotCount = 28;
    /// <summary>The event buttons the movie has (m_eventButton0..2).</summary>
    public const int ButtonCount = 3;
    /// <summary>The action key of the item preview (the right click needs it held).</summary>
    public const long PreviewAction = 0x49406;

    public override string Movie => "uiattendgiftdialog";
    protected override string EntityMethod => "alignment";
    /// <summary>GetTypeName (@0x14102bab0).</summary>
    public override string TypeName => "AttendGiftDialog";

    readonly AttendGiftState state;
    readonly List<object> buttons = new();                 // m_buttons
    object pay, detail, usePoint, giftList;                // m_payAttendButton, m_detailButton, m_usePointButton, m_giftList

    public AttendGiftDialogController(AttendGiftState state)
    {
        this.state = state;
        state.IsOpen = () => Loaded && !IsClosed;
        state.IsLoaded = () => Loaded;
        state.RefreshAll = RefreshAll;
        state.UpdateEventButtons = UpdateEventButtons;
        state.UpdateUsePointButton = UpdateUsePointButton;
        state.Close = Close;
        Unloaded += state.Unload;
    }

    public AttendGiftState State => state;

    /// <summary>FUN_1422dc2d0: the movie is loaded.</summary>
    bool Loaded => Host?.Entity != null;

    // The packets and boxes the window makes, and what it reads from the game.
    public Action<IOutPacket> Send = _ => { };
    public Action<MessageBoxData> ShowBox = MessageBoxQueue.Show;
    public Func<MessageBoxData, int, bool> CheckboxChecked = (b, n) =>
        MessageBoxQueue.Layer != null ? MessageBoxQueue.Layer.CheckboxChecked(b, n) : n == 0 ? b.Check0Selected : b.Check1Selected;
    /// <summary>FUN_140d73ff0: a plain message box of a common string key.</summary>
    public Action<string> PlainBox;
    /// <summary>Unk_Inventory::FUN_140a5e2e0 (<see cref="InventoryCount.FirstUidById"/>): the uid of the first usable bag item of the id, 0 for none.</summary>
    public Func<int, long> FindItemUid = _ => 0;
    /// <summary>GetLanguage: the date text's field order.</summary>
    public Func<int> Language = () => NumberText.DefaultLanguage;
    /// <summary>FUN_1418fe220: the description window of an event.</summary>
    public Action<string> ShowDetail = _ => { };
    /// <summary>The key action held (CInputKeyHandler::IsActionKeyDown).</summary>
    public Func<long, bool> ActionKeyDown = _ => false;
    /// <summary>ItemPreviewMgr::IsHudOn and OpenPreview(item, 0).</summary>
    public Func<bool> IsPreviewHudOn = () => false;
    public Action<Item> PreviewItem = _ => { };

    string Common(string key, params object[] args) => ClientFormat.Format(Model.Tables.Common(key) ?? "", args);

    /// <summary>FUN_140d73ff0 (:~315 callers): the plain box of a common string.</summary>
    void Plain(string key)
    {
        if (PlainBox != null) { PlainBox(key); return; }
        ShowBox(MessageBoxQueue.Create(0, Common(key), null, ""));
    }

    // ---- load ----

    /// <summary>
    /// OnLoadComplete (@0x14102BB00, uiattendgiftdialog.cpp:402): UIDialog's, RebuildActiveEvents, UpdateSelectedEvent, the close button
    /// and drag bar, m_giftList, the three buttons the load needs (a missing one is a load failure), m_eventButton0..2 each bound to
    /// SelectEventByIndex, the detail, pay (html label) and use-point clicks, the window-opened request when bool 100 of list 0 is set,
    /// RefreshAll.
    /// </summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        state.RebuildActiveEvents();
        state.UpdateSelectedEvent();
        BindCloseButton("m_closeButton");
        giftList = Host.Child("m_giftList");
        pay = Host.Child("m_payAttendButton");
        detail = Host.Child("m_detailButton");
        usePoint = Host.Child("m_usePointButton");
        if (pay == null || detail == null || usePoint == null)
        {
            Host.RT.NoteMissing("attend gift dialog: m_payAttendButton, m_detailButton or m_usePointButton");     // GfxExceptionSwfLoad (:64-66)
            return;
        }
        buttons.Clear();
        for (int i = 0; i < ButtonCount; i++)
        {
            if (Host.Child("m_eventButton" + i) is not { } b) continue;
            int index = i;
            Listen(b, () => SelectEventByIndex(index));
            buttons.Add(b);
        }
        Listen(detail, OnDetail);
        Host.Set(pay, "isHtml", true);                       // SetHtml (asbutton.cpp:54)
        Listen(pay, OnPay);
        Listen(usePoint, SendSelectedEventRequest);
        if (state.Values.GetBool(0, 100, 0, false)) Send(new AttendanceWindowOpened());
        RefreshAll();
    }

    void Listen(object target, Action a)
    {
        Func<object[], object> f = _ => { Host.RT.Guard("attend gift click", a); return null; };
        Host.Invoke(target, "addEventListener", "buttonClick", f, false, 0, false);
    }

    /// <summary>BindCloseButton (uidialog.cpp:977): the child's click runs the close.</summary>
    void BindCloseButton(string name)
    {
        if (Host.Child(name) is not { } b) { Host.RT.NoteMissing("attend gift dialog: " + name); return; }
        Listen(b, Close);
    }

    /// <summary>
    /// vfunc_28 (@0x14102f180 through the movie's "OnRightClickRenderer"): with the preview action key held, a slot of the list that is
    /// defined gives its item and the item preview opens when its HUD is on.
    /// </summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (!name.Equals("OnRightClickRenderer", StringComparison.OrdinalIgnoreCase)) return false;
        if (!Loaded || !ActionKeyDown(PreviewAction)) return false;
        if (args is not { Length: > 0 } || Rt.IsNullish(args[0])) return false;
        var slot = args[0];
        var id = Host.Get(slot, "itemID");
        if (Rt.IsNullish(id)) return false;
        int itemId = (int)Rt.ToUint32(id);
        int grade = Rt.IsNullish(Host.Get(slot, "itemGrade")) ? 0 : Rt.ToInt32(Host.Get(slot, "itemGrade"));
        var item = Item.Create(Model.Tables.Items, itemId, grade);          // CreateItem
        if (item == null) return false;
        if (IsPreviewHudOn()) PreviewItem(item);
        return false;
    }

    // ---- the events ----

    /// <summary>SelectEventByIndex (@0x14102CDC0): the button's event, a refresh when it changed the selection.</summary>
    public void SelectEventByIndex(int index)
    {
        if (state.SelectEventByIndex(index)) RefreshAll();
    }

    /// <summary>FUN_14102cea0 (:296): the description of the shown event in its own window; nothing without one.</summary>
    void OnDetail()
    {
        if (state.Selected == null) return;
        ShowDetail(state.Selected.Description);
    }

    // ---- refresh ----

    /// <summary>RefreshAll (@0x14102D690): only a loaded window; the buttons, gifts, pay and use-point buttons.</summary>
    public void RefreshAll()
    {
        if (!Loaded) return;
        UpdateEventButtons();
        UpdateGiftList();
        UpdatePayButton();
        UpdateUsePointButton();
    }

    /// <summary>"s_common_month_day" of the day of a time in the character's zone (FormatMonthDay @0x140b8e910).</summary>
    string FormatMonthDay(long time)
    {
        var t = MsTime.LocalTm(time);
        return Common("s_common_month_day", t.Month, t.Day);
    }

    /// <summary>
    /// UpdateEventButtons (@0x14102D6E0): the events shown are the requested one alone or the active ones; the first three get a button,
    /// "s_attendGift_title_name" of the start day, the end day and the title with points, selected when it is the shown event; the
    /// other buttons are hidden.
    /// </summary>
    public void UpdateEventButtons()
    {
        if (!Loaded) return;
        IReadOnlyList<AttendGiftEvent> events = state.Requested != null ? new[] { state.Requested } : state.ActiveEvents;
        for (int i = 0; i < ButtonCount && i < buttons.Count; i++)
        {
            if (i >= events.Count) { Host.SetVisible(buttons[i], false); continue; }
            var e = events[i];
            Host.SetVisible(buttons[i], true);
            Host.Set(buttons[i], "label", Common("s_attendGift_title_name", FormatMonthDay(e.Begin), FormatMonthDay(state.EndTime(e)), state.TitleWithPoints(e)));
            Host.Set(buttons[i], "selected", ReferenceEquals(e, state.Selected));
        }
    }

    /// <summary>
    /// UpdateGiftList (@0x14102DA40): for a loaded window and a shown event, the 28 slots: an empty object past the event's gifts, else
    /// the gift of the day (ASSlot::SetItemById, index = day, the days before the next attendance darkened and checked, the next one
    /// bordered, and checked when today's is done) with the item's tooltip; the list's data set and validated; the period text (the
    /// item to hold for kind 2 without a time of the character, else the two dates); the detail button enabled by a description.
    /// </summary>
    public void UpdateGiftList()
    {
        if (!Loaded || giftList == null) return;
        var e = state.Selected;
        if (e == null) return;
        int attended = state.GetAttendedDays(e);
        bool checkedIn = state.IsCheckedInToday(e);
        if (!checkedIn) attended++;
        var slots = new List<object>();
        for (int i = 0; i < SlotCount; i++)
        {
            if (i >= e.Gifts.Count) { slots.Add(Host.Obj()); continue; }
            var gift = e.Gifts[i];
            uint index = (uint)(i + 1);
            var slot = new List<(string, object)>();
            ItemSlot.BuildById(slot, gift.ItemId, gift.Rank, (uint)gift.Count, Model, Model.ItemSlotSources);
            slot.Add(("index", index));
            bool darken = false, check = false, border = false;
            if (index < attended) { darken = true; check = true; }
            else if (index == attended) { border = true; if (checkedIn) check = true; }
            slot.Add(("isDarken", darken));
            slot.Add(("isCheck", check));
            slot.Add(("isBorder", border));
            if (Tooltip(gift.ItemId, gift.Rank) is { } tip) slot.Add(("tooltip", tip));
            slots.Add(Host.Obj(slot.ToArray()));
        }
        var dp = Host.RT.Construct(Host.RT.GetClass("scaleform.clik.data.DataProvider"), Array.Empty<object>());
        foreach (var s in slots) Host.Invoke(dp, "push", s);
        Host.Invoke(giftList, "resetRenderer");              // ASTileCacheList::vfunc_25: ResetRenderer, then the data
        Host.Set(giftList, "dataProvider", dp);
        Host.Invoke(giftList, "invalidate");                 // vfunc_24(true)
        Host.Invoke(giftList, "validateNow");
        string period;
        if (e.Kind == 2 && !state.HasTime(e.Id)) period = Common("s_attend_gift_item_require", Model.Tables.ItemName(e.ValueId));
        else period = Common("s_attendGift_event_period", DateText(e.Begin), DateText(state.EndTime(e)));
        Host.SetChildText("m_eventPeriod", period);
        Host.Set(detail, "enabled", !string.IsNullOrEmpty(e.Description));
    }

    string DateText(long time) => HomeSaveSlotDialogController.DateText(MsTime.LocalTm(time), Language(), "/");

    /// <summary>The item tooltip of a gift: a new item of the id and rank, ASTooltip(local player, 8, 2) filled with it, align 1;
    /// none without an item of that id (the slot is then built without one, as the client skips the member).</summary>
    AsTooltip Tooltip(int itemId, int rank)
    {
        if (!Model.HasLocalPlayer) return null;
        var item = Item.Create(Model.Tables.Items, itemId, rank);
        if (item == null) return null;
        var tip = new ItemTooltip(Model, 8) { Localize = k => Host.Player.Translate?.Invoke(k) ?? k };
        if (!tip.Fill(item)) return null;
        tip.Data.SetTooltipAlign(1);
        return tip.Data;
    }

    /// <summary>UpdatePayButton (@0x14102E490): hidden; for an event with a pay type 1 (meso) or 2 (merat) shown with its label.</summary>
    public void UpdatePayButton()
    {
        if (pay == null) return;
        Host.SetVisible(pay, false);
        var e = state.Selected;
        if (e == null) return;
        string key = e.PayType switch { 1 => "s_attendGift_payAttend_meso_button", 2 => "s_attendGift_payAttend_merat_button", _ => null };
        if (key == null) return;
        Host.SetVisible(pay, true);
        Host.Set(pay, "label", Model.Tables.Common(key) ?? "");
    }

    /// <summary>
    /// UpdateUsePointButton (@0x14102E6A0): the footer shown and the button hidden; for an event that does not use points up as a title,
    /// the footer hidden and the button shown with the event's points (user value 0x69), enabled when there are some.
    /// </summary>
    public void UpdateUsePointButton()
    {
        if (usePoint == null) return;
        Host.SetChildVisible("m_footerText", true);
        Host.SetVisible(usePoint, false);
        var e = state.Selected;
        if (e == null || e.UsePoints) return;
        Host.SetChildVisible("m_footerText", false);
        int points = state.UsablePoints(e);
        Host.SetVisible(usePoint, true);
        Host.Set(usePoint, "label", Common("s_attendGift_usePoint_button", (uint)points));
        Host.Set(usePoint, "enabled", (uint)points > 0);
    }

    // ---- the requests ----

    /// <summary>SendSelectedEventRequest (@0x14102F0E0): Attendance mode 8 with the shown event; nothing without one.</summary>
    public void SendSelectedEventRequest()
    {
        if (state.Selected == null) return;
        Send(new AttendanceSelectEvent(state.Selected.Id));
    }

    /// <summary>
    /// The pay button (FUN_14102cf30 @0x14102cf30): nothing without an event or a pay type; every gift attended and every early
    /// participation used each give a plain box; else the box asking for it (type 0x19, the coupon checkbox on when the character has the
    /// event's coupon item), whose yes is <see cref="OnAttendanceMsgBox"/>. The merat pay sends Attendance mode 3 first, as the client does
    /// before the box.
    /// </summary>
    public void OnPay()
    {
        var e = state.Selected;
        if (e == null || e.PayType == 0) return;
        if (state.GetAttendedDays(e) >= e.Gifts.Count) { Plain("s_attendGift_payAttend_attend_all"); return; }
        if (state.UsedPayAttends(e) >= e.MaxPayAttends) { Plain("s_attendGift_payAttend_uesd_all"); return; }
        string key = null;
        switch (e.PayType)
        {
            case 1: key = "s_attendGift_payAttend_meso"; break;
            case 2: key = "s_attendGift_payAttend_merat"; Send(new AttendanceMeratPay()); break;      // FUN_14102f080
        }
        int remain = e.MaxPayAttends - state.UsedPayAttends(e);
        long couponUid = 0;
        if (e.CouponItemId != 0) couponUid = FindItemUid(e.CouponItemId);
        var box = MessageBoxQueue.CreateWithCallback(Common(key ?? "", (uint)remain, e.PayCost), 0x19, OnAttendanceMsgBox);
        box.SetButtonPreset(1);
        box.SetCheckbox0(Model.Tables.Common("s_attendGift_payAttend_coupon") ?? "", couponUid != 0);
        box.Check0Enabled = couponUid != 0;
        box.Append(couponUid);
        ShowBox(box);
    }

    /// <summary>
    /// OnAttendanceMsgBox (@0x14102D4E0): on yes, Attendance mode 4 with the shown event and the coupon's uid when its checkbox is
    /// checked (else 0); always handled.
    /// </summary>
    public bool OnAttendanceMsgBox(int result, MessageBoxData data)
    {
        if (result == 0)
        {
            long uid = 0;
            if (CheckboxChecked(data, 0)) uid = data.ArgLong(0, 0);
            if (state.Selected != null) Send(new AttendanceEarlyParticipation(state.Selected.Id, uid));
        }
        return true;
    }
}
