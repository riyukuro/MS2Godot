namespace Ms2.Ui.Hud;

/// <summary>
/// The movie side of one <see cref="BootyPopup"/>: UIBootyPopup's uibootypopup (GetSwfFileName "UI" + GetTypeName
/// "BootyPopup", @0x141031550), loaded on layer 7 (the constructor's LoadSwf(7), uibootypopup.cpp:58), its root the entity
/// (vfunc_41 @0x141031ec0: CreateGameEntity of the root). The popup's text goes to the root's setText(name, description) and
/// setShow(bool) (FUN_141031a60); its place to the root's x / y (SetPositionFloat, UpdateScreenPosition).
/// </summary>
public sealed class BootyPopupController : UiController
{
    public override string Movie => "uibootypopup";
    protected override string EntityMethod => "setText";
    protected override bool IsDialog => false;
    /// <summary>m_58 = 0 in the UIBootyPopup ctor (uibootypopup.cpp:57): hidden at load (vfunc_8(m_58)).</summary>
    public override bool VisibleOnLoad => false;
    public override string TypeName => "BootyPopup";

    /// <summary>The popup this movie draws.</summary>
    public BootyPopup Popup { get; }

    public BootyPopupController(BootyPopup popup)
    {
        Popup = popup;
        popup.SetText = (name, desc) => { if (Host?.Entity != null) Host.Invoke(Host.Entity, "setText", name, desc); };
        popup.SetShow = on => { if (Host?.Entity != null) Host.Invoke(Host.Entity, "setShow", on); };
    }

    /// <summary>The root's place (SetPositionFloat).</summary>
    public void Place(float x, float y)
    {
        if (Host?.Entity == null) return;
        Host.Set(Host.Entity, "x", (double)x);
        Host.Set(Host.Entity, "y", (double)y);
    }

    /// <summary>GetWidthCeil / GetHeightCeil of the root.</summary>
    public (int Width, int Height) Size()
    {
        if (Host?.Entity == null) return (0, 0);
        return ((int)Math.Ceiling(Ms2.Avm2.Rt.ToNumber(Host.Get(Host.Entity, "width"))),
                (int)Math.Ceiling(Ms2.Avm2.Rt.ToNumber(Host.Get(Host.Entity, "height"))));
    }
}
