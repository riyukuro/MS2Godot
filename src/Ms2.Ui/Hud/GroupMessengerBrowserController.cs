namespace Ms2.Ui.Hud;

/// <summary>
/// UIGroupMessengerBrowser (ms2/client/ms2/uigroupmessenger.cpp), the C++ side of uigroupmessengerbrowser: the bar of group
/// chat windows above the chat. The movie's GroupMessengerBrowser draws the window buttons and the insert button.
/// OnLoadInit (:1504) opens the joined group chats' windows and sends the list (RefreshWindowList :1839); group chats come
/// from CGroupChatManager, which is not ported, so the list is empty. Its settings file (vfunc_53 / vfunc_54: offsetX,
/// offsetY, alignType) is not read or written yet; the place is the constructor's.
/// </summary>
public sealed class GroupMessengerBrowserController : UiController
{
    public override string Movie => "uigroupmessengerbrowser";
    protected override string EntityMethod => "setData";
    public override string TypeName => "GroupMessengerBrowser";      // GetTypeName (:1449)

    // the constructor (:1431): offset (0, -300), align type 7 (bottom left)
    float offsetX, offsetY = -300f;
    int alignType = 7;

    /// <summary>
    /// OnLoadInit (:1504), before the load's layout (and here in place of the settings file, which is this class's own
    /// vfunc_54 :1490 and not read yet): RefreshWindowList's setData with one {number, name, minimize} per window, none
    /// here; the movie's setData leaves only the insert button.
    /// </summary>
    public override void LoadTransform() => Host.Invoke("setData", (object)Array.Empty<object>());

    /// <summary>
    /// vfunc_43 (:1513): placed by its align type and offsets (ASDisplayObject::vfunc_2), which are then taken back from
    /// the place (UIDialog::FUN_141926620). m_60 (a saved rate above 1) does not occur without the file.
    /// </summary>
    public override void Layout(double stageW, double stageH)
    {
        Host.Align(Host.Entity, alignType, (int)offsetX, (int)offsetY);
        (alignType, offsetX, offsetY) = AlignFromPlace(alignType, offsetX, offsetY);
    }

    /// <summary>UIDialog::FUN_141926620 (uidialog.cpp:1056).</summary>
    (int, float, float) AlignFromPlace(int align, float ox, float oy)
    {
        float x = (float)Ms2.Avm2.Rt.ToNumber(Host.Get(Host.Entity, "x")), y = (float)Ms2.Avm2.Rt.ToNumber(Host.Get(Host.Entity, "y"));
        float w = (float)Math.Ceiling(Ms2.Avm2.Rt.ToNumber(Host.Get(Host.Entity, "width")));
        float h = (float)Math.Ceiling(Ms2.Avm2.Rt.ToNumber(Host.Get(Host.Entity, "height")));
        float hw = (int)Host.Player.StageW * 0.5f, hh = (int)Host.Player.StageH * 0.5f;
        if (x <= hw && y <= hh) return (5, x, y);
        if (x >= hw && y <= hh) return (6, x + w - hw * 2f, y);
        if (x <= hw && y >= hh) return (7, x, y + h - hh * 2f);
        if (x >= hw && y >= hh) return (8, x + w - hw * 2f, y + h - hh * 2f);
        return (align, ox, oy);
    }

}
