using Ms2.Avm2;

namespace Ms2.Ui.Hud;

/// <summary>What UISmartPush::AddPush (uismartpush.cpp:122) hands the strip movie's addPush for one push.</summary>
public sealed class SmartPushView
{
    public int Id;
    public string Text = "";
    public string Icon = "";
    public bool IsToggle;
    public bool Active;
    public bool IsSmallActiveIcon;
    public string ActiveMovie = "";
    public string EventPopup = "";
}

/// <summary>The strip as the manager sees it (CSmartPushManager calls UISmartPush::AddPush / RemovePush).</summary>
public interface ISmartPushStrip
{
    bool Ready { get; }
    bool AddPush(SmartPushView v);
    bool RemovePush(int id);
}

/// <summary>
/// UISmartPush (uismartpush.cpp; UI id 0x4ef7), the strip of smart push buttons at the bottom left of the HUD, run from the
/// uismartpush movie. The manager adds and removes pushes (<see cref="AddPush"/>, <see cref="RemovePush"/>); a click on a
/// push's button calls back "onSmartPush" with its id (vfunc_28, :136), which becomes <see cref="PushClicked"/>.
/// </summary>
public sealed class SmartPushStripController : UiController, ISmartPushStrip
{
    public override string Movie => "uismartpush";
    protected override string EntityMethod => "alignment";
    public override string TypeName => "SmartPush";                  // GetTypeName (:89)

    /// <summary>A UIHeadUpDisplay, not a dialog: no drag bar, no settings file.</summary>
    protected override bool IsDialog => false;
    protected override bool TweenEnabled => false;

    /// <summary>The bar heights and margin FUN_1418dd0d0 sums (uibuiltindialog.cpp:53): DAT_1439f8ccc, DAT_1439f8cd0 (with feature
    /// 0xbd) and DAT_1439f8cc8 (193 in the data; STILL-OPEN whether the client changes it at run time), as PopupNpcTalk keeps them.</summary>
    public int BottomBarHeight = 21, AdventureBarHeight = 21, ScreenMargin = 193;

    /// <summary>FUN_1418dd0d0 = (DAT_1439f8ccc - 2) + FUN_1418dd110 + DAT_1439f8cc8, added to 0x5a in vfunc_43 (:106); FUN_1418dd110
    /// is the adventure bar's height - 2 with feature 0xbd, else 0.</summary>
    public int BottomInset
        => BottomBarHeight - 2 + (Model.IsFeatureEnabled(MainPlayerInfoController.AdventureLevelFeature) ? AdventureBarHeight - 2 : 0) + ScreenMargin;

    /// <summary>vfunc_28 "onSmartPush" (:136): CSmartPushManager::OnPushClicked(id).</summary>
    public event Action<int> PushClicked;

    /// <summary>UISmartPush::vfunc_3 (:157): the strip was hidden, the manager forgets which pushes are shown
    /// (CSmartPushManager::ResetPushStates).</summary>
    public event Action Hidden;

    /// <summary>True once the movie's entity exists (m_30 non-null): AddPush and RemovePush need it.</summary>
    public bool Ready => Host?.Entity != null;

    /// <summary>vfunc_43 (:100): ASGameEntity::vfunc_2(7, 0x1b4, -(FUN_1418dd0d0() + 0x5a)): bottom left, 0x1b4 across.</summary>
    public override void Layout(double stageW, double stageH)
    {
        if (Host?.Entity != null) Host.Align(Host.Entity, 7, 0x1b4, -(BottomInset + 0x5a));
    }

    /// <summary>AddPush (:122): the push as an object on the movie's addPush; 0 without the entity.</summary>
    public bool AddPush(SmartPushView v)
    {
        if (!Ready) return false;
        Host.Invoke(Host.Entity, "addPush", Host.Obj(
            ("id", v.Id), ("text", v.Text), ("icon", v.Icon), ("isToggle", v.IsToggle), ("active", v.Active),
            ("isSmallActiveIcon", v.IsSmallActiveIcon), ("activeMovie", v.ActiveMovie), ("eventPopup", v.EventPopup)));
        return true;
    }

    /// <summary>RemovePush (:166): removePush(id) on the entity; 0 without it.</summary>
    public bool RemovePush(int id)
    {
        if (!Ready) return false;
        Host.Invoke(Host.Entity, "removePush", id);
        return true;
    }

    public override bool OnCallback(string name, object[] args)
    {
        if (!string.Equals(name, "onSmartPush", StringComparison.OrdinalIgnoreCase)) return false;
        if (args != null && args.Length > 0) PushClicked?.Invoke((int)Rt.ToNumber(args[0]));
        return true;
    }

    /// <summary>The host calls this when the strip hides (vfunc_3).</summary>
    public void OnHidden() => Hidden?.Invoke();
}
