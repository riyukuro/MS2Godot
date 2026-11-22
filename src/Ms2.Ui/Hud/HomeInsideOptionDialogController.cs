using Ms2.Avm2;
using Ms2.Formats.World;
using Ms2.Net.Packets;

namespace Ms2.Ui.Hud;

/// <summary>What the home inside option dialog reaches outside itself (UIHomeInsideOptionDialog, uihomeinsideoptiondialog.cpp).</summary>
public interface IHomeInsideOptionHost
{
    /// <summary>UgcHomeOptionData's three lists (the singleton).</summary>
    UgcHomeOptionData Data { get; }
    /// <summary>CubeMapInformationService::FUN_1410c34f0 (f418): the home setting's background, light and camera bytes.</summary>
    (byte Background, byte Light, byte Camera) Setting { get; }
    /// <summary>RequestCube u8 mode (0x33 / 0x34 / 0x36), u8 index (FUN_140c280d0).</summary>
    void Send(byte mode, byte index);
}

/// <summary>
/// UIHomeInsideOptionDialog (dialog 0x4f7a, movie uihomeinsideoptiondialog, uihomeinsideoptiondialog.cpp; ctor @0x140c27340): the home's
/// background, light and camera combos (m_background, m_light, m_camera) and the m_apply button. The opener is
/// <see cref="HomeMenuDialogController.OnHomeAttribute"/>.
/// <para>STILL-OPEN: the three answers' effects (UgcHomeOptionData::ApplyBg / ApplyLight / ApplyCamera) are not ported.</para>
/// </summary>
public class HomeInsideOptionDialogController : UiController, IMessageBoxListener
{
    public const int UiId = 0x4f7a;

    public override string Movie => "uihomeinsideoptiondialog";
    /// <summary>Unused: the movie class adds no public method (only the component setup), see FindEntity.</summary>
    protected override string EntityMethod => "";
    const string EntityClass = "scaleform.ms2.controls.dialog.HomeInsideOptionDialog";

    /// <summary>The movie's entity is found by its class (HomeInsideOptionDialog).</summary>
    public override AsObject FindEntity()
    {
        static bool Is(AsClass k) { for (; k != null; k = k.Super) if (k.Name == EntityClass) return true; return false; }
        static Ms2.Avm2.Flash.AsDisplayObject Walk(Ms2.Avm2.Flash.AsDisplayObject o)
        {
            if (Is(o.Class)) return o;
            if (o is Ms2.Avm2.Flash.AsContainer c) foreach (var k in c.Children) if (Walk(k) is { } r) return r;
            return null;
        }
        return Host.Player.Root == null ? null : Walk(Host.Player.Root);
    }
    public override string TypeName => "HomeInsideOptionDialog";

    readonly IHomeInsideOptionHost host;
    public HomeInsideOptionDialogController(IHomeInsideOptionHost host) { this.host = host; }

    /// <summary>GetStringByKey.</summary>
    public Func<string, string> Str = key => MessageBoxData.CommonByKey(key) ?? "";
    public Action<MessageBoxData> ShowBox = MessageBoxQueue.Show;
    /// <summary>FUN_14151f200: the queue shows its next box.</summary>
    public Action NextBox = MessageBoxQueue.Next;

    /// <summary>OnLoadComplete (@0x140c27440): UIDialog's, then each combo that exists gets its list (ClearItems, a {label} row per
    /// entry, SetSelectedIndex of the setting's byte, vfunc_24(true)); a list with no rows leaves its combo alone.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        var (bg, light, camera) = host.Setting;
        var d = host.Data;
        Fill("m_background", d.Backgrounds.Select(r => r.Name), bg);
        Fill("m_light", d.Lights.Select(r => r.Name), light);
        Fill("m_camera", d.Cameras.Select(r => r.Name), camera);
    }

    void Fill(string name, IEnumerable<string> labels, byte selected)
    {
        var rows = labels.ToList();
        if (rows.Count == 0) return;
        if (Host.Child(name) is not { } combo) return;
        var dp = Host.Get(combo, "dataProvider");
        if (!Rt.IsNullish(dp)) Host.Invoke(dp, "cleanUp");                                    // ASDropdownMenu::ClearItems
        foreach (var label in rows)
            if (!Rt.IsNullish(dp)) Host.Invoke(dp, "push", Host.Obj(("label", label)));     // AddItemRow
        Host.Set(combo, "selectedIndex", (uint) selected);                                  // SetSelectedIndex
        Host.Invoke(combo, "invalidate");                                                    // vfunc_24(true)
        Host.Invoke(combo, "validateNow");
    }

    /// <summary>vfunc_28 (@0x140c27af0): the movie's two calls, compared without case (_stricmp).</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (Is(name, "OnApply")) ShowBox(MessageBoxQueue.Create(1, Str("s_home_option_msg_confirm"), this, "OnApply"));
        else if (Is(name, "OnConfirm"))
        {
            // no m_apply: the client does nothing
            if (Host.Child("m_apply") is not { } apply) return true;
            if (Rt.ToBoolean(Host.Get(apply, "enabled"))) ShowBox(MessageBoxQueue.Create(1, Str("s_home_option_msg_confirm"), this, "OnConfirm"));
            else Close();
        }
        else return false;
        return true;
    }

    static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>FUN_140c27fd0 (OnApply's box) and FUN_140c28000 (OnConfirm's box): yes (0) sends; the confirm box then closes the dialog
    /// (with m_apply present, sending only while it is enabled).</summary>
    public bool OnMessageBox(MessageBoxController sender, int button, string tag)
    {
        NextBox();
        if (button != 0) return true;
        if (tag == "OnApply") SendSelections();
        else if (tag == "OnConfirm" && Host.Child("m_apply") is { } apply)
        {
            if (Rt.ToBoolean(Host.Get(apply, "enabled"))) SendSelections();
            Close();
        }
        return true;
    }

    /// <summary>FUN_140c280d0 (@0x140c280d0): each combo that exists sends its selected index (0x33, 0x34, 0x36); then m_apply is disabled.</summary>
    void SendSelections()
    {
        SendCombo("m_background", HomeInsideOptionRequest.Background);
        SendCombo("m_light", HomeInsideOptionRequest.Light);
        SendCombo("m_camera", HomeInsideOptionRequest.Camera);
        if (Host.Child("m_apply") is { } apply) Host.Set(apply, "enabled", false);
    }

    void SendCombo(string name, byte mode)
    {
        if (Host.Child(name) is { } combo) host.Send(mode, (byte) Rt.ToInt32(Host.Get(combo, "selectedIndex")));
    }
}
