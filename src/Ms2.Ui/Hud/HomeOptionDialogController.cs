using Ms2.Avm2;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIHomeOptionDialog's movie (uihomeoptiondialog.cpp; dialog 0x4f26, movie uihomeoptiondialog, GetTypeName "HomeOptionDialog"): the setData
/// call after the load, the movie's five calls (vfunc_28 @ :112-190) and the answers to the boxes they open (OnMessage). The values and the
/// sends are <see cref="HomeOptionController"/>.
/// STILL-OPEN: the opener, UIHomeMenuDialog::OnHomePriority (uihomemenudialog.cpp:526-535), is not ported; nothing opens this dialog from the game yet.
/// </summary>
public class HomeOptionDialogController : UiController, IMessageBoxListener
{
    public const int UiId = 0x4f26;

    public override string Movie => "uihomeoptiondialog";
    protected override string EntityMethod => "setData";
    public override string TypeName => "HomeOptionDialog";

    /// <summary>FUN_140223190 (row): the name the movie's setData object keys a row by.</summary>
    public static readonly string[] RowNames =
        { "jump", "climb", "skill", "playInstrument", "potion", "ride_ground", "ride_air", "pet_summon", "revival_here" };

    readonly HomeOptionController logic;

    public HomeOptionDialogController(HomeOptionController logic) { this.logic = logic; }

    /// <summary>GetStringByKey.</summary>
    public Func<string, string> Str = key => MessageBoxData.CommonByKey(key) ?? "";
    public Action<MessageBoxData> ShowBox = MessageBoxQueue.Show;
    /// <summary>FUN_14151f200: the queue shows its next box.</summary>
    public Action NextBox = MessageBoxQueue.Next;
    /// <summary>ShowSystemNotice(text, 1, 1).</summary>
    public Action<string> SystemNotice = _ => { };
    /// <summary>m_30->Invoke(name, args): a call into the movie's entity.</summary>
    public Action<string, object[]> CallMovie;

    void Call(string name, params object[] args)
    {
        if (CallMovie != null) CallMovie(name, args);
        else Host.Invoke(Host.Entity, name, args);
    }

    /// <summary>OnLoadComplete (@:96): UIDialog's, then SetData.</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        SetData();
    }

    /// <summary>FUN_140fa97a0 (:201-229): m_30->Invoke("setData", {opentype (when known), the nine rows}); nothing without the service.</summary>
    public void SetData()
    {
        if (Members() is not { } members) return;
        Host.Invoke(Host.Entity, "setData", Host.Obj(members));
    }

    /// <summary>The members of the setData object, in the client's order; null without the service.</summary>
    public (string Key, object Value)[] Members()
    {
        if (logic.SetData() is not { } d) return null;
        var members = new List<(string, object)>();
        if (d.OpenType is int open) members.Add(("opentype", open));
        for (int i = 0; i < d.Rows.Length; i++) members.Add((RowNames[i], d.Rows[i]));
        return members.ToArray();
    }

    /// <summary>vfunc_28 (:112-190): the movie's calls, compared without case (_stricmp).</summary>
    public override bool OnCallback(string name, object[] args)
    {
        args ??= Array.Empty<object>();
        if (Is(name, "onApply")) Confirm("onApply");
        else if (Is(name, "onConfirm"))
        {
            // SetArgsIfAny == 0 (:122): no argument means the client does nothing; only an explicit false closes
            if (args.Length == 0) return true;
            if (Rt.ToBoolean(args[0])) Confirm("OnConfirm");
            else Close();
        }
        else if (Is(name, "apply"))
        {
            // SetArgsIfAny == 0 (:134): no argument means the client does nothing; with some, missing trailing pops read as 0
            // and the 0x6e1 / 0xb0a boxes still show
            if (args.Length == 0) return true;
            var rows = new int[HomeOptionController.Rows];
            for (int i = 0; i < rows.Length; i++) rows[i] = 2 + i < args.Length ? Rt.ToInt32(args[2 + i]) : 0;
            logic.Apply(Rt.ToInt32(args[0]), args.Length > 1 ? Rt.ToString(args[1]) : "", rows);
        }
        else if (Is(name, "onKickUser")) ShowBox(MessageBoxQueue.Create(1, Str("s_home_option_msg_etc_kickuser"), this, "OnKickUser"));
        else if (Is(name, "OnReset")) ShowBox(MessageBoxQueue.Create(1, Str("s_home_option_init"), this, "OnReset"));
        else return false;
        return true;
    }

    static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    void Confirm(string tag) => ShowBox(MessageBoxQueue.Create(1, Str("s_home_option_msg_confirm"), this, tag));

    /// <summary>OnMessage (:233-275): the queue goes on, then yes (0) acts by the tag.</summary>
    public bool OnMessageBox(MessageBoxController sender, int button, string tag)
    {
        NextBox();
        if (button != 0) return true;
        switch (tag)
        {
            case "OnReset": Call("reset"); break;
            case "OnKickUser": logic.KickUsers(() => SystemNotice(Str("s_home_option_msg_kick_user"))); break;
            case "onApply": Call("requestData", false); break;
            case "OnConfirm": Call("requestData", true); break;
        }
        return true;
    }
}
