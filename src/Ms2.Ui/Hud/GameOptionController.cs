using Ms2.Avm2;
using Ms2.Formats.Options;

namespace Ms2.Ui.Hud;

/// <summary>
/// The movie side of the options dialog, UIGameOptionDialog (client/ms2/uigameoptiondialog.cpp; UI id 0x4ef3), for its movie
/// uigameoptiondialog. The dialog's C++ side is <see cref="GameOptionDialog"/>, kept between opens (the client keeps the
/// dialog object; each open loads the movie again). This controller is that dialog's movie: the root's Invoke calls, the
/// tab's controls (<see cref="IOptionTab"/>, "getTabChild" on the root as Unk_GpDialog::GetTab* do) and the movie's calls.
/// </summary>
public sealed class GameOptionController : UiController, IOptionTab
{
    public override string Movie => "uigameoptiondialog";
    protected override string EntityMethod => "setTabComponent";
    public override string TypeName => "GameOptionDialog";      // GetTypeName @0x141942880

    /// <summary>The dialog's C++ side; its host routes the movie calls (<see cref="IGameOptionDialogHost.Tab"/>,
    /// Invoke, CloseDialog, SetApplyEnabled) to this controller while the movie is loaded.</summary>
    public GameOptionDialog Dialog;

    /// <summary>OnLoadComplete @0x1419428d0: UIDialog's, then the dialog's (initData, the tab components, OpenTab).</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        if (Host.Entity == null) return;
        Dialog.OnLoadComplete();
    }

    /// <summary>vfunc_67 @0x141942a10: every page deactivates, UIDialog's close (through the host's CloseDialog, which
    /// calls <see cref="CloseMovie"/>), simple mode and the tab reset.</summary>
    public override void Close() => Dialog.Close();

    /// <summary>UIDialog::vfunc_67, the movie's close tween.</summary>
    public void CloseMovie() => base.Close();

    /// <summary>vfunc_28 @0x141942cc0 takes OnCloseDialog itself (the modified / quick-slot checks) before UIDialog's.</summary>
    public override bool OnCallbackBeforeDialog(string name, object[] args) =>
        string.Equals(name, "OnCloseDialog", StringComparison.OrdinalIgnoreCase) && Dialog.Call(name, args);

    /// <summary>vfunc_28 @0x141942cc0: the dialog's calls, then the open page's (vfunc_5).</summary>
    public override bool OnCallback(string name, object[] args) => Dialog.Call(name, args);

    // ---- the root's calls ------------------------------------------------------------------------------------------

    public void Invoke(string method, object[] args)
    {
        if (Host?.Entity != null) Host.Invoke(method, args);
    }

    public string InvokeString(string method) =>
        Host?.Entity == null ? "" : Rt.ToString(Host.Invoke(method));

    /// <summary>SetApplyEnabled @0x141943820: GetChild&lt;ASButton&gt;("m_apply")->SetEnabled.</summary>
    public void SetApplyEnabled(bool enabled)
    {
        if (Host?.Child("m_apply") is { } b) Host.Set(b, "enabled", enabled);
    }

    // ---- IOptionTab: the current page's controls ---------------------------------------------------------------------

    /// <summary>"getTabChild"(name, -1): the control on the current page (CommonFinder.traverseByName on m_container). A
    /// child of a clip is "clip.child" (ASDisplayObject's child getter on the clip found).</summary>
    object Child(string name)
    {
        if (Host?.Entity == null) return null;
        var parts = name.Split('.');
        var o = Host.Invoke(Host.Entity, "getTabChild", parts[0], -1);
        for (int i = 1; i < parts.Length && !Rt.IsNullish(o); i++) o = Host.Invoke(o, "getChildByName", parts[i]);
        return Rt.IsNullish(o) ? null : o;
    }

    /// <summary>ASButton::IsSelected @0x1422ce0e0 ("selected").</summary>
    public bool? IsSelected(string name) => Child(name) is { } c ? Rt.ToBoolean(Host.Get(c, "selected")) : null;

    /// <summary>ASButton::SetSelected @0x1422ce090.</summary>
    public void SetSelected(string name, bool selected)
    {
        if (Child(name) is { } c) Host.Set(c, "selected", selected);
    }

    /// <summary>ASSlider::GetValue ("value").</summary>
    public float? GetValue(string name) => Child(name) is { } c ? (float)Rt.ToNumber(Host.Get(c, "value")) : null;

    /// <summary>ASSlider::SetValue.</summary>
    public void SetValue(string name, float value)
    {
        if (Child(name) is { } c) Host.Set(c, "value", (double)value);
    }

    /// <summary>ASDropdownMenu::GetSelectedIndex.</summary>
    public int? GetSelectedIndex(string name) => Child(name) is { } c ? Rt.ToInt32(Host.Get(c, "selectedIndex")) : null;

    /// <summary>ASDropdownMenu::SetSelectedIndex.</summary>
    public void SetSelectedIndex(string name, int index)
    {
        if (Child(name) is { } c) Host.Set(c, "selectedIndex", index);
    }

    /// <summary>ASTextField / ASTextInput GetText ("text").</summary>
    public string GetText(string name) => Child(name) is { } c ? Rt.ToString(Host.Get(c, "text")) : null;

    /// <summary>SetTextW ("text").</summary>
    public void SetText(string name, string text)
    {
        if (Child(name) is { } c) Host.Set(c, "text", text ?? "");
    }

    /// <summary>UIComponent::SetEnabled ("enabled").</summary>
    public void SetEnabled(string name, bool enabled)
    {
        if (Child(name) is { } c) Host.Set(c, "enabled", enabled);
    }

    /// <summary>ASDisplayObject::vfunc_3 @0x1422bfc60: the display info's visible flag.</summary>
    public bool SetClipShown(string name, bool shown)
    {
        if (Child(name) is not { } c) return false;
        Host.SetVisible(c, shown);
        return true;
    }

    /// <summary>FillResolutionCombo's dropdown calls: ClearItems (the provider's cleanUp), SetScrollBar("export_scroll") or
    /// SetMenuRowCount, AddItemRow per row (push), SetSelectedIndex, vfunc_24(true) (invalidate, validateNow).</summary>
    public bool SetResolutionRows(string name, IReadOnlyList<ResolutionRow> rows, int selected)
    {
        if (Child(name) is not { } c) return false;
        var dp = Host.Get(c, "dataProvider");
        if (!Rt.IsNullish(dp)) Host.Invoke(dp, "cleanUp");
        if (rows.Count > 6) Host.Set(c, "scrollBar", "export_scroll");
        else Host.Set(c, "menuRowCount", (uint)rows.Count);
        foreach (var r in rows)
            if (!Rt.IsNullish(dp)) Host.Invoke(dp, "push", new Dictionary<string, object> { ["modeid"] = r.ModeId, ["label"] = r.Label });
        Host.Set(c, "selectedIndex", selected);
        Host.Invoke(c, "invalidate");
        Host.Invoke(c, "validateNow");
        return true;
    }

    /// <summary>GetTabComponent(type)->Invoke("getResolution") (@0x14124bf20).</summary>
    public int GetResolution(int tabType)
    {
        if (Host?.Entity == null) return -1;
        var tab = Host.Invoke(Host.Entity, "getTabComponent", tabType);
        return Rt.IsNullish(tab) ? -1 : Rt.ToInt32(Host.Invoke(tab, "getResolution"));
    }
}
