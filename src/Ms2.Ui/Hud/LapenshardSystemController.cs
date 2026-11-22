using Ms2.Avm2;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Items;

namespace Ms2.Ui.Hud;

/// <summary>One page of the lapenshard window (the client's UILapenshardTab: vfunc_1 destroy, vfunc_2 NotifyPage event,
/// vfunc_3 the page's movie root, vfunc_4 OnLeaveTab, vfunc_6 the page's own callbacks).</summary>
public abstract class LapenshardTabPage
{
    /// <summary>TabInfo::m_strName: the body frame the tab strip row selects ("equip", "upgrade").</summary>
    public abstract string Name { get; }
    /// <summary>The common-string key of TabInfo::m_strLabel.</summary>
    public abstract string LabelKey { get; }
    /// <summary>vfunc_3: the page was created over the body frame's first child.</summary>
    public virtual void Show(UiHost host, object root) { }
    /// <summary>The window the page belongs to.</summary>
    protected LapenshardSystemController Owner { get; private set; }
    internal void Attach(LapenshardSystemController owner) => Owner = owner;
    /// <summary>vfunc_4: the page is left.</summary>
    public virtual void Leave() { }
    /// <summary>vfunc_2: NotifyPage(event).</summary>
    public virtual void Notify(int ev) { }
    /// <summary>vfunc_6: a callback of the page's movie (true: handled).</summary>
    public virtual bool OnCallback(string name, object[] args) => false;
    /// <summary>vfunc_1: the dialog closes.</summary>
    public virtual void Destroy() { }
    /// <summary>The binding of a slot of the page's bags (null: not the page's).</summary>
    public virtual object GetBinding(string name) => null;
    /// <summary>The slot names of a bag the page owns.</summary>
    public virtual IEnumerable<string> GetBagSlotBindings(string bag) => Array.Empty<string>();
    /// <summary>The tooltip of a slot of the page's bags (null: not the page's).</summary>
    public virtual IDictionary<string, object> GetSlotTooltip(string name) => null;
    /// <summary>vfunc_9: an inbound lapenshard packet reaches the current page.</summary>
    public virtual void OnPacket(LapenshardMessage m) { }

    // ---- the page's drag slots (the controller answers IDragSlotOwner for them) ----
    public virtual bool IsDragSlot(string name) => false;
    public virtual void RefreshSlot(string name) { }
    public virtual void SlotDragStarted(string name) { }
    public virtual void SlotDragDone(string name) { }
    public virtual void SlotDroppedOn(string name, string source, IDragSlotOwner sourceOwner) { }
}

/// <summary>
/// The lapenshard window, UILapenshardSystemDialog (client/ms2/uilapenshardsystemdialog.cpp, UI id 0x4f6d, type name
/// "LapenshardSystemDialog", movie uilapenshardsystemdialog). It is a tab shell: OnLoadComplete (@0x140c61870) fills
/// m_buttonBar with a row {tabIndex, label, frame} per page, selects tab 0 and hides m_webGuideButton; the movie's
/// onChangeTab then calls OnLeaveTab and OnChangeTab(tabIndex, body root), which switch the current page
/// (ShowTabPage @0x140c627a0). vfunc_28 (@0x140c61be0) tries the current page's callbacks before its own.
/// <para>STILL-OPEN: the pages (equip, upgrade) are added by later slices; the item events listener
/// (FUN_1419cae20) and vfunc_3's inventory notification; the tab menu (OnTabMenu) has no client handler.</para>
/// </summary>
public sealed class LapenshardSystemController : UiController, IDragSlotOwner
{
    public override string Movie => "uilapenshardsystemdialog";
    protected override string EntityMethod => "alignment";
    public override string TypeName => "LapenshardSystemDialog";       // GetTypeName (@0x140c612a0)

    public const int UiId = 0x4f6d;
    /// <summary>vfunc_75 (@0x140c62390): the feature that gates the window.</summary>
    public const int FeatureId = 0xd3;

    readonly List<LapenshardTabPage> tabs = new();
    LapenshardTabPage page;

    /// <summary>The common string of a key (GetStringCommonByKey).</summary>
    public Func<string, string> Text;

    /// <summary>m_tabs, in tab order.</summary>
    public IReadOnlyList<LapenshardTabPage> Tabs => tabs;
    /// <summary>m_spPage: the page shown now.</summary>
    public LapenshardTabPage Page => page;

    public void AddTab(LapenshardTabPage p) { tabs.Add(p); p?.Attach(this); }

    /// <summary>Send an outbound packet (true: sent).</summary>
    public Func<IOutPacket, bool> Send;
    /// <summary>The local player is in battle (MyPC vfunc_65).</summary>
    public Func<bool> InBattle = () => false;
    /// <summary>CFieldDataCli::HasLimitContent(0x200). STILL-OPEN: the field's limit contents are not read, so false.</summary>
    public Func<bool> FieldLimitsLapenshard = () => false;
    /// <summary>ShowMessageBox: show a box.</summary>
    public Action<MessageBoxData> ShowBox = MessageBoxQueue.Show;

    /// <summary>MyPC::GetLapenshardItemId: the item worn in a slot, 0 for none.</summary>
    public int WornItemId(int slot) => Model?.Player.Lapenshards?.GetItemId(slot, HasTableRow) ?? 0;

    /// <summary>Whether the lapenshard table has a row for the id (EquipLapenshard ignores an id without one).</summary>
    bool HasTableRow(int itemId) => Model.Tables.Lapenshards.Mask(itemId) != null;

    /// <summary>The kind (red 1, blue 2, green 4) the player selected: m_nSlot (SelectSlot).</summary>
    public int SelectedKind { get; private set; }

    /// <summary>FUN_140c62520 (@0x140c62520): a local player, not in battle (s_lapenshard_disable_battle), a field whose
    /// limits allow it (s_lapenshard_disable_field); the box is shown for the two refusals.</summary>
    public bool CanUse()
    {
        if (Model == null || !Model.HasLocalPlayer) return false;
        if (InBattle())
        {
            ShowBox(MessageBoxQueue.CreateWithCallback(T("s_lapenshard_disable_battle"), 0, (_, _) => true));
            return false;
        }
        if (FieldLimitsLapenshard())
        {
            ShowBox(MessageBoxQueue.CreateWithCallback(T("s_lapenshard_disable_field"), 0, (_, _) => true));
            return false;
        }
        return true;
    }

    /// <summary>FUN_140c60920's source: the local inventory's items of a category (0xd holds the lapenshards), the bag's own
    /// copies. Unset: none.</summary>
    public Func<int, IEnumerable<Item>> InventoryItems = _ => Array.Empty<Item>();
    /// <summary>PC::CanUseItem of a bag item (FUN_140c60920 with bCheckUse). Unset: usable.</summary>
    public Func<Item, bool> CanUseItem = _ => true;
    /// <summary>The host's <see cref="CanUseItem"/>: PC::CanUseItem (<see cref="PcItemUse"/>) on the item's packet record, the owner's state
    /// from <paramref name="pc"/>. An item without a record (never from the inventory) is not usable.</summary>
    public static Func<Item, bool> CanUseFrom(ItemDataTable items, Func<PcItemUse.Pc> pc) =>
        i => i?.Record is { } rec && PcItemUse.CanUseItem(rec, items.Get(rec.Id), pc());

    /// <summary>ShowSystemNotice: a common-string key's text.</summary>
    public event Action<string> NoticeText;
    /// <summary>FUN_140d73bc0(id, 0, 0, ""): a common-string message box by id (0xd3, the kind mismatch of a drop); the host shows it.</summary>
    public event Action<int> NoticeCode;
    public void ShowSystemNotice(string key) => NoticeText?.Invoke(T(key));
    public void ShowNoticeCode(int id) => NoticeCode?.Invoke(id);

    /// <summary>MyPC::HasEquippedLapenshardOfSameGroup (@0x14152f8b0, mypc.cpp:4261): an id of the count table (m_map16f8) has the
    /// group of the item. The table counts what EquipLapenshard saw, so a replaced id stays in it (see <see cref="LapenshardData"/>).</summary>
    public bool HasEquippedLapenshardOfSameGroup(int itemId) =>
        Model.Player.Lapenshards?.HasSameGroup(itemId, HasTableRow, Model.Tables.Lapenshards.Group) == true;

    /// <summary>MyPC::FindLapenSlotForItem(kind): the lowest empty slot of the kind, else the lowest slot of the kind; 0 for none.</summary>
    public int FindLapenSlotForItem(int kind)
    {
        int first = 0;
        for (int slot = 1; slot <= LapenshardEquipTabPage.Slots; slot++)
        {
            if (LapenshardEquipTabPage.KindOfSlot(slot) != kind) continue;
            if (first == 0) first = slot;
            if (WornItemId(slot) == 0) return slot;
        }
        return first;
    }

    /// <summary>SendLapenshard (LapenshardSlot, @FUN_140c62520 then mode 1): the use check, then the equip request.</summary>
    public bool SendLapenshard(int slot, long uid)
    {
        if (!CanUse()) return false;
        return Send?.Invoke(new LapenshardEquip(slot, uid)) == true;
    }

    // ---- IDragSlotOwner: the page's slots ----
    public bool IsDragSlot(string name) => page?.IsDragSlot(name) == true;
    public void RefreshSlot(string name) => page?.RefreshSlot(name);
    public void SlotDropUnresolved(string name) => page?.RefreshSlot(name);
    public void SlotDragStarted(string name) => page?.SlotDragStarted(name);
    public void SlotDragDone(string name) => page?.SlotDragDone(name);
    public void SlotDroppedOn(string name, string source, IDragSlotOwner sourceOwner) => page?.SlotDroppedOn(name, source, sourceOwner);

    /// <summary>SelectSlot(kind): the kind is kept and the pages light that kind's slots (event 10).</summary>
    public void SelectSlot(int kind)
    {
        SelectedKind = kind;
        NotifyPage(10);
    }

    /// <summary>The slot released (LapenshardSlot::vfunc_21): the highlight goes off (event 11).</summary>
    public void ReleaseSlot()
    {
        SelectedKind = 0;
        NotifyPage(11);
    }

    /// <summary>vfunc_79: an inbound lapenshard packet goes to the current page.</summary>
    public void OnPacket(LapenshardMessage m) { if (!IsClosed) page?.OnPacket(m); }

    public override IEnumerable<string> GetBagSlotBindings(string bag) => page?.GetBagSlotBindings(bag) ?? base.GetBagSlotBindings(bag);
    public override object GetBinding(string name) => page?.GetBinding(name) ?? base.GetBinding(name);
    public override IDictionary<string, object> GetSlotTooltip(string name) => page?.GetSlotTooltip(name) ?? base.GetSlotTooltip(name);

    /// <summary>vfunc_75: IsFeatureEnabled(0xd3).</summary>
    public bool IsAvailable => Model != null && Model.IsFeatureEnabled(FeatureId);

    string T(string key) => Text != null ? Text(key) ?? key : Model?.Tables.Common(key) ?? key;

    /// <summary>vfunc_67 (@0x140c616f0): OnLeaveTab, each page destroyed, the list and the page dropped.</summary>
    public override void Close()
    {
        OnLeaveTab();
        foreach (var t in tabs) t?.Destroy();
        tabs.Clear();
        page = null;
        base.Close();
    }

    /// <summary>OnLoadComplete (@0x140c61870): one row per page with a page, select 0, hide the web guide button.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        if (Host.Child("m_buttonBar") is { } bar)
        {
            for (int i = 0; i < tabs.Count; i++)
            {
                if (tabs[i] == null) continue;
                Host.Invoke(Host.Get(bar, "dataProvider"), "push", Host.Obj(("tabIndex", (uint)i), ("label", T(tabs[i].LabelKey)), ("frame", tabs[i].Name)));
            }
            Host.Invoke(bar, "invalidate");                   // vfunc_24(true)
            Host.Invoke(bar, "validateNow");
            Host.Set(bar, "selectedIndex", 0);
        }
        if (Host.Child("m_webGuideButton") is { } guide) Host.SetVisible(guide, false);       // FUN_1419260d0
    }

    /// <summary>vfunc_28 (@0x140c61be0) after UIDialog's: the page's callbacks, OnLeaveTab, OnChangeTab(tab, root).</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (page != null && page.OnCallback(name, args)) return true;
        if (name.Equals("OnLeaveTab", StringComparison.OrdinalIgnoreCase))
        {
            OnLeaveTab();
            return true;
        }
        if (name.Equals("OnChangeTab", StringComparison.OrdinalIgnoreCase) && args is { Length: > 0 })
        {
            ShowTabPage(Rt.ToUint32(args[0]), args.Length > 1 ? args[1] : null);
            return true;
        }
        return false;
    }

    /// <summary>OnLeaveTab (@0x140c62740): the page is told.</summary>
    public void OnLeaveTab() => page?.Leave();

    /// <summary>ShowTabPage (@0x140c627a0): nothing for a tab past the list; the page is the current one and gets the root.</summary>
    public void ShowTabPage(uint tab, object root)
    {
        if (tab >= tabs.Count) return;
        var t = tabs[(int)tab];
        if (t == null) return;
        page = t;
        page.Show(Host, root);
    }

    /// <summary>NotifyPage (@0x140c623c0): the page gets the event while the window is not closing (vfunc_55 false).</summary>
    public void NotifyPage(int ev)
    {
        if (IsClosed) return;
        page?.Notify(ev);
    }
}
