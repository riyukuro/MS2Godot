using Ms2.Avm2;
using Ms2.Net.Packets;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// The C++ side of the party search listing's register window, UIPartySearchRegisterDialog
/// (client/ms2/uipartysearchregisterdialog.cpp; UI id 0x4edb, a UIModalDialog), for its movie uipartysearchregisterdialog
/// (PartySearchRegisterDialog). The movie calls nothing: the register and remove buttons get click handlers
/// (ASButton::AddClickHandler). Without an own listing it registers the typed title, the auto join check and the member
/// count combo's value (Constants PartySearchRegisterComboValues); with one it shows the listing, disabled, and offers its
/// removal. m_190, the last title and check used, outlives the window, so the HUD keeps one controller.
/// STILL-OPEN: the random title list (BuildRandomTitleList, common 0xc48-0xc4c) is built by the client but never used.
/// </summary>
public sealed class PartySearchRegisterDialogController : UiController, IMessageBoxListener
{
    public const int UiId = 0x4edb;
    /// <summary>GetSelectedMaxUserCount's value for no valid row.</summary>
    public const int DefaultMaxMembers = 10;

    public override string Movie => "uipartysearchregisterdialog";
    /// <summary>Unused: the movie class adds no public method (only the compiler's component setup), see FindEntity.</summary>
    protected override string EntityMethod => "";
    /// <summary>The movie class (dialog.PartySearchRegisterDialog) adds no public method, so the entity is found by its class.</summary>
    const string EntityClass = "scaleform.ms2.controls.dialog.PartySearchRegisterDialog";

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
    /// <summary>GetTypeName (@0x1413208b0).</summary>
    public override string TypeName => "PartySearchRegisterDialog";

    readonly PartyBook book;

    public PartySearchRegisterDialogController(PartyBook book)
    {
        this.book = book;
        book.RegisterTitleCleared += ClearRegisteredTitle;
    }

    /// <summary>Drops the book subscription (the HUD's teardown).</summary>
    public void Detach() => book.RegisterTitleCleared -= ClearRegisteredTitle;

    /// <summary>m_190: the last registered title and auto join flag, shown on the next open without a listing.</summary>
    public string SavedTitle { get; private set; } = "";
    public bool SavedAutoJoin { get; private set; } = true;
    /// <summary>m_180: the title and flag read off the window at the register click (Reset by the close).</summary>
    string pendingTitle = "";
    bool pendingAutoJoin = true;

    /// <summary>Constants PartySearchRegisterComboValues (m_e770).</summary>
    public IReadOnlyList<int> ComboValues = Array.Empty<int>();
    /// <summary>GetStringCommon.</summary>
    public Func<int, string> Common = id => id.ToString("x");
    /// <summary>CBanTextChecker::FUN_1402262e0: null when the title passes, else the message.</summary>
    public Func<string, string> CheckTitle = _ => null;
    /// <summary>The party search window is open (GetPartySearchDialog(0x4eda)).</summary>
    public Func<bool> SearchDialogOpen = () => true;
    public Action<MessageBoxData> ShowBox = MessageBoxQueue.Show;

    /// <summary>
    /// OnLoadComplete (@0x141320a10): UIModalDialog's, BindCloseButton("m_closeButton"); with no search window it closes.
    /// The combo gets ClearItems, menuRowCount = the value count and a label per value; the buttons get their click handlers.
    /// No own listing: the saved title and check, the last combo row, register shown and remove hidden (vfunc_4, SetVisible).
    /// An own listing: the three inputs disabled showing its title, auto join and member count row (FindMaxUserCountIndex),
    /// register hidden and remove shown.
    /// </summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        BindCloseButton("m_closeButton");
        if (!SearchDialogOpen()) { Close(); return; }
        var combo = Host.Child("m_maxUserCountCombo");
        var title = Host.Child("m_inputTitle");
        var check = Host.Child("m_checkAllowAutoJoin");
        var register = Host.Child("m_btnRegister");
        var remove = Host.Child("m_btnRemove");
        if (combo == null || title == null || check == null || register == null || remove == null)
        {
            Host.RT.NoteMissing("party search register dialog: a control");
            return;
        }
        if (Host.Get(combo, "dataProvider") is AsObject dp && !Rt.IsNullish(dp))
        {
            Host.Invoke(dp, "cleanUp");
            foreach (var v in ComboValues) Host.Invoke(dp, "push", Host.Obj(("label", v.ToString())));
        }
        Host.Set(combo, "menuRowCount", ComboValues.Count);
        Listen(register, OnRegisterClicked);
        Listen(remove, OnRemoveClicked);
        var own = book.Recruitment;
        if (own.IsEmpty)
        {
            Host.Set(title, "text", SavedTitle);
            Host.Set(check, "selected", SavedAutoJoin);
            Host.Set(combo, "selectedIndex", ComboValues.Count - 1);
            Host.SetVisible(register, true);
            Host.SetVisible(remove, false);
        }
        else
        {
            Host.Set(title, "enabled", false);
            Host.Set(check, "enabled", false);
            Host.Set(combo, "enabled", false);
            Host.Set(title, "text", own.Title ?? "");
            Host.Set(check, "selected", own.AutoJoin);
            Host.Set(combo, "selectedIndex", FindMaxUserCountIndex(own.MaxMembers));
            Host.SetVisible(register, false);
            Host.SetVisible(remove, true);
        }
    }

    /// <summary>vfunc_67 (@0x141320900): UIModalDialog's close, then m_180.Reset (no text, flag 1).</summary>
    public override void Close()
    {
        base.Close();
        pendingTitle = "";
        pendingAutoJoin = true;
    }

    /// <summary>vfunc_13 (@0x141321960): Enter or Tab go to the movie's onProcessKey.</summary>
    public void Key(bool enter)
    {
        if (Host?.Entity != null) Host.Invoke("onProcessKey", (object)(enter ? "onEnterKey" : "onTabKey"));
    }

    /// <summary>ClearRegisteredTitle (@0x141321d30): the saved title emptied (a register refused for its title).</summary>
    public void ClearRegisteredTitle() => SavedTitle = "";

    /// <summary>GetSelectedMaxUserCount (@0x141322020): the combo row's value, 10 for no valid row.</summary>
    int GetSelectedMaxUserCount()
    {
        if (Host?.Entity == null || Host.Child("m_maxUserCountCombo") is not { } combo) return DefaultMaxMembers;
        int row = Rt.ToInt32(Host.Get(combo, "selectedIndex"));
        return row < 0 || row >= ComboValues.Count ? DefaultMaxMembers : ComboValues[row];
    }

    /// <summary>FindMaxUserCountIndex (@0x141322380): the row of the value, or -1.</summary>
    int FindMaxUserCountIndex(int value)
    {
        for (int i = 0; i < ComboValues.Count; i++) if (ComboValues[i] == value) return i;
        return -1;
    }

    /// <summary>
    /// ValidateAndSendRegistration (@0x1413220d0): under 2 letters, the box of common 0xb53 of 2; a refused title, a box of the
    /// check's message; else, unless silent, SendMatchPartyMode0(title, flag, the combo's value), and the title (when not
    /// empty) and flag saved (ApplyTitleAndAutoJoin @0x141321cc0).
    /// </summary>
    bool ValidateAndSend(string text, bool autoJoin, bool silent)
    {
        text ??= "";
        if (text.Length < 2)
        {
            ShowBox(MessageBoxQueue.Create(0, ClientFormat.Format(Common(0xb53) ?? "", 2), null, ""));
            return false;
        }
        if (CheckTitle(text) is { } bad)
        {
            ShowBox(MessageBoxQueue.Create(0, bad, null, ""));
            return false;
        }
        if (!silent) book.RegisterRecruit(text, autoJoin, GetSelectedMaxUserCount());
        if (text != "") SavedTitle = text;
        SavedAutoJoin = autoJoin;
        return true;
    }

    /// <summary>OnRegisterClicked (@0x141322400): m_180 takes the trimmed title and the check; a sent register closes.</summary>
    public void OnRegisterClicked()
    {
        if (Host?.Entity == null || Host.Child("m_inputTitle") is not { } title || Host.Child("m_checkAllowAutoJoin") is not { } check) return;
        pendingTitle = (Rt.ToString(Host.Get(title, "text")) ?? "").Trim();
        pendingAutoJoin = Rt.ToBoolean(Host.Get(check, "selected"));
        if (ValidateAndSend(pendingTitle, pendingAutoJoin, false)) Close();
    }

    /// <summary>OnRemoveClicked (@0x141322520): the yes / no box common 0xb56 tagged "remove".</summary>
    public void OnRemoveClicked() => ShowBox(MessageBoxQueue.Create(1, Common(0xb56) ?? "", this, "remove"));

    /// <summary>OnMessage (@0x141321b30): yes on "remove" sends SendMatchPartyMode1 and closes; yes on "modify" saves m_180
    /// silently and closes when it passes.</summary>
    public bool OnMessageBox(MessageBoxController sender, int button, string tag)
    {
        if (button != 0) return true;
        if (tag == "remove") { book.RemoveRecruit(); Close(); }
        else if (tag == "modify" && ValidateAndSend(pendingTitle, pendingAutoJoin, true)) Close();
        return true;
    }

    void Listen(object target, Action a)
    {
        Func<object[], object> f = _ => { a(); return null; };
        Host.Invoke(target, "addEventListener", "buttonClick", f, false, 0, false);
    }

    /// <summary>BindCloseButton (uidialog.cpp:977): the child's click runs the close.</summary>
    void BindCloseButton(string name)
    {
        if (Host.Child(name) is not { } b) { Host.RT.NoteMissing("party search register dialog: " + name); return; }
        Listen(b, Close);
    }
}
