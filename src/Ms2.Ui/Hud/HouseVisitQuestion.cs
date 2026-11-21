namespace Ms2.Ui.Hud;

/// <summary>What the house visit question reaches outside itself (CPersonalInfoAgreement, gameoptionstruct.cpp:3524-3690).</summary>
public interface IHouseVisitHost
{
    /// <summary>WorldService::HasTutorial.</summary>
    bool HasTutorial { get; }
    /// <summary>CFeatureManager::IsFeatureEnabled(0x117).</summary>
    bool PersonalInfoFeature { get; }
    /// <summary>WorldService::GetLocalPlayer() != 0.</summary>
    bool HasLocalPlayer { get; }
    /// <summary>MyPC::HasNoHome() != 0.</summary>
    bool HasNoHome { get; }
    /// <summary>CubeMapInformationService is up, the map is a home and its owner is this account (FUN_1410d2f10, FUN_1410c3180 == DAT_143ab2820).</summary>
    bool InMyHouse { get; }
    /// <summary>HasGroupAgreement(1): the house visit group is agreed.</summary>
    bool HouseGroupAgreed { get; }
    /// <summary>FindGameMenuDialog(0x4e28)->FUN_141939320(show): the game menu's red circle (notify flag 0x16).</summary>
    void SetHouseMenuRedCircle(bool show);
    /// <summary>MyPC::SendHouseSetting.</summary>
    void SendHouseSetting(int kind, string value, bool flag);
    /// <summary>GetStringByKey / GetStringCommonByKey.</summary>
    string Text(string key);
    string CommonText(string key);
    /// <summary>The input text of the box the callback got (GetInputText).</summary>
    string InputText(MessageBoxData box);
}

/// <summary>
/// The question the client asks in the player's own house until the house visit group is agreed: who may visit (public, password,
/// private). FUN_141262180 asks, FUN_141262830 takes the answer, FUN_141262b70 the six digit password, FUN_141262f10 asks again after the error box.
/// The live session calls <see cref="Ask"/> after ResponseCube 0x14 stored the home info and the player has a home (scenestategame.cpp:10231-10234, HudScene.OnHomeInfoApplied).
/// STILL-OPEN: the other client callers (field entry, FUN_141262650's close of the 0x4f7c notice after an answer) are not ported.
/// </summary>
public sealed class HouseVisitQuestion
{
    readonly IHouseVisitHost host;
    public HouseVisitQuestion(IHouseVisitHost host) { this.host = host; }

    /// <summary>FUN_140d73c60 (show a created box), FUN_140d73bc0 (a box of common string id) and FUN_14151f200 (the next queued box); the message box queue by default.</summary>
    public Action<MessageBoxData> Show = MessageBoxQueue.Show;
    public Action<int> ShowCommon = id => MessageBoxQueue.ShowCommon(id, 0, null, "");
    public Action Next = MessageBoxQueue.Next;

    /// <summary>m_bAnswered (+0x08): the question was answered this stay.</summary>
    public bool Answered { get; private set; }

    /// <summary>FUN_141262180 (@0x141262180, gameoptionstruct.cpp:3524): the red circle follows the missing agreement, and in my own house the box opens.</summary>
    public void Ask()
    {
        if (host.HasTutorial) return;
        if (!host.PersonalInfoFeature) return;
        if (!host.HasLocalPlayer) return;
        bool show = !host.HouseGroupAgreed;
        if (host.HasNoHome) show = false;
        host.SetHouseMenuRedCircle(show);
        if (!show) return;
        if (!host.InMyHouse) return;
        var box = MessageBoxQueue.Create(4, host.Text("s_personal_infomaion_agreement_visit_myhome_content"), null, "");
        box.Title = host.CommonText("s_personal_infomaion_agreement_title");
        box.EnterAllowed = false;     // FUN_141a024a0(0)
        box.Align = 0;
        box.SetButtonLabels(host.CommonText("s_personal_infomaion_agreement_visit_myhome_btn_public"),
                            host.CommonText("s_personal_infomaion_agreement_visit_myhome_btn_password"),
                            host.CommonText("s_personal_infomaion_agreement_visit_myhome_btn_private"));
        box.Callback = OnAnswer;
        Show(box);
    }

    /// <summary>FUN_141262830 (@0x141262830, :3608): 0 public, 1 asks for the password, else private.</summary>
    bool OnAnswer(int result, MessageBoxData data)
    {
        if (!host.HasLocalPlayer) return false;
        if (result == 1)
        {
            var box = MessageBoxQueue.Create(10, host.CommonText("s_personal_infomaion_agreement_visit_myhome_input_password"), null, "");
            box.SetInput(2, "", 6, 0, 0);
            box.Callback = OnPassword;
            Show(box);
            return true;
        }
        Answered = true;
        host.SendHouseSetting(result == 0 ? 0 : 2, "", true);
        host.SetHouseMenuRedCircle(false);
        return true;
    }

    /// <summary>FUN_141262b70 (@0x141262b70, :3647): exactly six digits, else the error and the question again.</summary>
    bool OnPassword(int result, MessageBoxData data)
    {
        if (result != 0) { Ask(); return true; }
        string password = (host.InputText(data) ?? "").Trim(',');
        if (password.Length != 6)
        {
            var box = MessageBoxQueue.Create(0, host.CommonText("s_home_password_string_error"), null, "");
            box.Callback = (_, _) => { Ask(); return true; };      // FUN_141262f10
            Show(box);
            return false;
        }
        foreach (char c in password)
        {
            if (c is < '0' or > '9')
            {
                ShowCommon(0xb0a);
                Ask();
                return false;
            }
        }
        if (host.HasLocalPlayer)
        {
            Answered = true;
            host.SendHouseSetting(1, password, false);
        }
        Next();     // FUN_14151f200
        return true;
    }
}
