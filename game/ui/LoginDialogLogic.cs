using Ms2Godot.Ui.Contracts;
using Godot;

namespace Ms2Godot.Ui;

/// <summary>
/// The login dialog's behaviour, ported by hand from the movie's AS3 (scaleform.ms2.controls.dialog.LoginDialog) and
/// wired through the generated contract: instance names come from <see cref="UILoginDialogContract.Instances"/>, what it
/// tells the game from <see cref="UILoginDialogContract.ToHost"/>, what the game calls on it from <see cref="UILoginDialogContract.FromHost"/>.
/// </summary>
public sealed class LoginDialogLogic
{
    readonly MovieClipNode root;
    readonly TextInputWidget inputId, inputPw;
    readonly ButtonWidget lockBtn, helpBtn, loginBtn, chkLocaleName;
    bool mouseState = true;
    string localeName;

    public ButtonWidget LoginButton => loginBtn;
    public TextInputWidget IdInput => inputId;
    public TextInputWidget PasswordInput => inputPw;
    public ButtonWidget SaveIdButton => lockBtn;
    public ButtonWidget LocaleNameBox => chkLocaleName;
    public string Id => inputId.Text;
    public string Password => inputPw.Text;

    public LoginDialogLogic(MovieClipNode root)
    {
        this.root = root;
        inputId = W<TextInputWidget>(UILoginDialogContract.Instances.M_dialog_m_inputID);
        inputPw = W<TextInputWidget>(UILoginDialogContract.Instances.M_dialog_m_inputPW);
        lockBtn = W<ButtonWidget>(UILoginDialogContract.Instances.M_dialog_m_lock);
        helpBtn = W<ButtonWidget>(UILoginDialogContract.Instances.M_dialog_m_help);
        loginBtn = W<ButtonWidget>(UILoginDialogContract.Instances.M_dialog_m_login);
        chkLocaleName = W<ButtonWidget>(UILoginDialogContract.Instances.M_dialog_m_chkLocaledName);

        // configUI: listeners
        lockBtn.Select += (_, sel) => UiBridge.Raise(UILoginDialogContract.Movie, UILoginDialogContract.ToHost.OnSaveID, sel);
        chkLocaleName.Select += (_, sel) => UiBridge.Raise(UILoginDialogContract.Movie, UILoginDialogContract.ToHost.OnChkLocaleName, sel);
        loginBtn.Click += _ => OnConnect();
        helpBtn.Click += _ => { };      // onHelp: empty in the movie
        inputPw.Edit.TextSubmitted += _ => OnEnterKey();
        inputId.Edit.TextSubmitted += _ => OnEnterKey();
    }

    T W<T>(string path) where T : Widget
    {
        var clip = root.FindClip(path) ?? throw new InvalidOperationException("login dialog: missing instance " + path);
        return (T)clip.Widget;
    }

    /// <summary>initData(id, saveId, localeName, region): called by the host once the dialog is loaded.</summary>
    public void InitData(string id, bool saveId, bool useLocaleName, string region)
    {
        if (id.Length > 0) inputId.Text = id;
        inputId.Focus();
        inputId.Edit.SelectAll();
        lockBtn.SetSelected(saveId, false);
        chkLocaleName.SetSelected(useLocaleName, false);
        localeName = region;
    }

    public void OnConnect()
    {
        if (!mouseState) return;
        mouseState = false;
        UiBridge.Raise(UILoginDialogContract.Movie, UILoginDialogContract.ToHost.OnConnect);
    }

    /// <summary>The host answers a failed login: input is allowed again.</summary>
    public void FailConnect() => mouseState = true;

    public void OnEnterKey()
    {
        if (!mouseState) return;
        if (inputId.Length > 0) OnConnect(); else inputId.Focus();
    }

    public void OnTabKey()
    {
        if (!mouseState) return;
        if (inputId.Focused) inputPw.Focus(); else inputId.Focus();
    }
}
