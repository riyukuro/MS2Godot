using Ms2.Avm2;

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
    /// <summary>vfunc_4: the page is left.</summary>
    public virtual void Leave() { }
    /// <summary>vfunc_2: NotifyPage(event).</summary>
    public virtual void Notify(int ev) { }
    /// <summary>vfunc_6: a callback of the page's movie (true: handled).</summary>
    public virtual bool OnCallback(string name, object[] args) => false;
    /// <summary>vfunc_1: the dialog closes.</summary>
    public virtual void Destroy() { }
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
public sealed class LapenshardSystemController : UiController
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

    public void AddTab(LapenshardTabPage p) => tabs.Add(p);

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
