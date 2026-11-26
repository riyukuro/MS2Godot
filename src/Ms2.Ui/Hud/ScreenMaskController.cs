namespace Ms2.Ui.Hud;

/// <summary>
/// UIScreenMask (ms2/client/ms2/uiscreenmask.cpp): the field's screen mask, a movie on CommonDepth 10 (ScreenMask, the
/// constructor's m_2c) that loads the field's bgMask movie (fielddata.xml &lt;ui bgMask&gt;, "BG_mask_shadow.swf") and
/// stretches it over the stage. A UIHeadUpDisplay, not a dialog. UISceneGame shows it on WorldLoadComplete (vfunc_55
/// @0x141ac78a0: FindObject(0x4e55)->vfunc_2 = LoadSwf) and hides it on WorldUnLoadComplete (vfunc_56 @0x141ac78f0) and on
/// leaving the scene (vfunc_49); a field without a bgMask still loads the movie, which then shows nothing.
/// </summary>
public sealed class ScreenMaskController : UiController
{
    /// <summary>GetBgMask @0x141ad09e0: the field data's m_strBgMask, "" without field data.</summary>
    readonly string bgMask;

    public ScreenMaskController(string bgMask) { this.bgMask = bgMask ?? ""; }

    public override string Movie => "uiscreenmask";
    protected override string EntityMethod => "load";      // the entity's own public method (loadComplete is private)
    protected override bool IsDialog => false;
    /// <summary>The constructor clears m_58 (uiscreenmask.cpp:31); vfunc_43 (<see cref="Layout"/>) shows it again.</summary>
    public override bool VisibleOnLoad => false;
    public override string TypeName => "ScreenMask";       // GetTypeName @0x141ad06f0

    /// <summary>
    /// OnLoadInit @0x141ad0790, before the load's layout: UIObject::OnLoadInit, then load(bgMask) on the entity when the
    /// field has one. The movie's load puts the bgMask movie into a new Loader and adds it on complete, sized to the stage.
    /// </summary>
    public override void LoadTransform()
    {
        if (bgMask.Length != 0) Host.Invoke(Host.Entity, "load", bgMask);
    }

    /// <summary>
    /// vfunc_43 @0x141ad08c0: ASUIComponent::SetSizeInt = setSize(stage width, stage height) (asuicomponent.cpp:188;
    /// ASDisplayObject::FUN_1422c1500 / 1530), ASDisplayObject::vfunc_2(5, 0, 0) (absolute: at 0, 0), vfunc_3(1) (visible).
    /// </summary>
    public override void Layout(double stageW, double stageH)
    {
        Host.Invoke("setSize", (int)stageW, (int)stageH);
        Host.Align(Host.Entity, 5, 0, 0);
        Host.SetVisible(Host.Entity, true);
    }
}
