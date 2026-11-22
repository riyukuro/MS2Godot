using Ms2.Avm2;

namespace Ms2.Ui.Hud;

/// <summary>What the home menu reaches outside itself: the local player, the cube map service's mode and rights, and the sends.</summary>
public interface IHomeMenuHost
{
    /// <summary>WorldService::GetLocalPlayer() != 0.</summary>
    bool HasPlayer { get; }
    /// <summary>MyPC::IsInCubeMode (UIHomeMenuDialog::IsCraftMode @0x140c2af70).</summary>
    bool InCubeMode { get; }
    /// <summary>CubeMapInformationService::FUN_1410c3650: the plot mode (0 default, 1 design, 2 other, 3 blueprint).</summary>
    int PlotMode { get; }
    /// <summary>FUN_1410d5640: the player may change this home (owner).</summary>
    bool CanChange { get; }
    /// <summary>FUN_1410d56a0: the player may edit this home (the f330 == 1 test inverted).</summary>
    bool CanEditHome { get; }
    /// <summary>The CubeMapInformationService exists (the client tests the smart pointer before FUN_1410d5640 / FUN_1410d56a0).</summary>
    bool HasService { get; }
    /// <summary>FUN_1418fd280 (uicommon.cpp:353): the own account owns the home.</summary>
    bool OwnsHome { get; }
    /// <summary>FUN_140930c50: in a house with a known owner.</summary>
    bool InOwnersHouse { get; }
    /// <summary>FUN_141902580 (uicommon.cpp:1955): blueprints are available; with <paramref name="message"/> a shutdown shows notice 0x2f7.</summary>
    bool BlueprintAvailable(bool message);
    /// <summary>IsBlueprintShopAvailable (uicommon.cpp:1969).</summary>
    bool BlueprintShopAvailable(bool message);
    /// <summary>CUIService::ToggleDialog(id).</summary>
    void ToggleDialog(int uiId);
    /// <summary>MyPC::SendCraftModeOff; true when it sent.</summary>
    bool SendCraftModeOff();
    /// <summary>MyPC::FUN_14152feb0(1, 0): the craft mode on.</summary>
    void EnterCraftMode();
    /// <summary>MyPC::SendRequestMoveFieldMode3 / Mode4 / Mode5.</summary>
    void SendRequestMoveFieldMode(int mode);
    /// <summary>CubeMapInformationService::FUN_1410d5530: the design has changes.</summary>
    bool DesignHasChanges { get; }
    /// <summary>CubeMapInformationService::CountCubes.</summary>
    int CubeCount { get; }
    /// <summary>The feature check (CFeatureManager::IsFeatureEnabled).</summary>
    bool FeatureEnabled(int id);
    /// <summary>MyPC::FUN_14152f3c0 (the feature 0xd4 step before a demolish).</summary>
    void BeforeDemolish();
    /// <summary>UIHomeMenuDialog::SendRequestCube's packet: RequestCube byte 0x1f.</summary>
    void SendDemolish();
    /// <summary>GetHomeSaveSlotDialog(0x4f27)->SetLoadMode() (SetSaveMode with <paramref name="save"/>).</summary>
    void SetSaveSlotMode(bool save);
    /// <summary>UIHomeMenuDialog::SetFreeCamera's camera and dialog work (CameraMgr mode 6 and 0x4f3e, or the saved camera back).</summary>
    void SetFreeCamera(bool on);
    /// <summary>CameraMgr::GetModeType() == 1.</summary>
    bool FreeCameraActive { get; }
}

/// <summary>
/// UIHomeMenuDialog (uihomemenudialog.cpp; dialog 0x4f7c, movie uihomemenudialog, GetTypeName "HomeMenuDialog"): the state frame the movie
/// shows after the load (Refresh @0x140c29600), the button states (FUN_140c2a780), and the movie's calls (vfunc_28 @0x140c29340, matched
/// without case).
/// STILL-OPEN: OnScreenshot (@0x140c29c70, the upload box and the render pass of FUN_140c2b180), the design and blueprint save and load
/// (OnDesignSave and OnBlueprintSave: they choose by the cube count of CubeMapInformationService::FUN_1410ce310, which is not ported; OnDesignLoad
/// and OnBlueprintImport reach <see cref="HomeSaveSlotDialogController"/>), OnBlueprintShop
/// (UIMeratMarketDialog::SearchByCategory(3, 0xcc)), the 0x4f7a (home attribute) and 0x4f7b (blueprint export) dialogs behind their
/// ToggleDialog calls, and the camera work of <see cref="IHomeMenuHost.SetFreeCamera"/>. Those calls do nothing here.
/// </summary>
public class HomeMenuDialogController : UiController, IMessageBoxListener
{
    public const int UiId = 0x4f7c;
    /// <summary>The dialogs ToggleDialog reaches (OnHomePriority, OnHomeAttribute, OnBlueprintExport).</summary>
    public const int HomeOptionId = 0x4f26, HomeAttributeId = 0x4f7a, BlueprintExportId = 0x4f7b;

    public override string Movie => "uihomemenudialog";
    protected override string EntityMethod => "setState";
    public override string TypeName => "HomeMenuDialog";

    readonly IHomeMenuHost host;
    /// <summary>m_bFreeCamera (the constructor sets 0, vfunc_67 resets it).</summary>
    public bool FreeCamera { get; private set; }

    public HomeMenuDialogController(IHomeMenuHost host) { this.host = host; }

    /// <summary>GetStringByKey.</summary>
    public Func<string, string> Str = key => MessageBoxData.CommonByKey(key) ?? "";
    /// <summary>GetStringCommon(id).</summary>
    public Func<int, string> CommonText = MessageBoxData.Common;
    public Action<MessageBoxData> ShowBox = MessageBoxQueue.Show;
    /// <summary>FUN_141275260(id, 1, 1): the system notice of common string <c>id</c>.</summary>
    public Action<string> SystemNotice = _ => { };
    /// <summary>m_30->Invoke(name, args).</summary>
    public Action<string, object[]> CallMovie;
    /// <summary>GetChild&lt;ASButton&gt;(name): a button of the movie, null when it has none.</summary>
    public Func<string, object> ChildButton;
    public Action<object, string, object> SetMember;

    /// <summary>OnLoadComplete @0x140c29300: UIDialog's, then Refresh(IsCraftMode()).</summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        Refresh(IsCraftMode());
    }

    /// <summary>vfunc_67 @0x140c292d0: m_bFreeCamera = 0.</summary>
    public void ResetFreeCamera() => FreeCamera = false;

    /// <summary>IsCraftMode @0x140c2af70: no player, no craft.</summary>
    public bool IsCraftMode() => host.HasPlayer && host.InCubeMode;

    /// <summary>GetStateFrameName @0x140c2ad90.</summary>
    public string GetStateFrameName()
    {
        bool craft = IsCraftMode();
        if (!host.OwnsHome) return craft ? "other_craft" : "other";
        switch (host.PlotMode)
        {
            case 0: return craft ? "default_craft" : "default";
            case 1: return craft ? "design_craft" : "design";
            case 3: return craft ? "blueprint_craft" : "blueprint";
            case 2: return "other";
        }
        return "default";
    }

    /// <summary>Refresh @0x140c29600: setState with the frame name (only while m_30 is an ASDialog: the movie hook is set then), the free camera off in craft mode,
    /// the button states. STILL-OPEN: the closing vfunc_43 (UIDialog @0x1419257b0, :822), the check that moves a dialog sitting outside the screen back (vfunc_25).</summary>
    public void Refresh(bool craft)
    {
        Call("setState", GetStateFrameName());
        if (craft) SetFreeCamera(false);
        UpdateButtons();
    }

    /// <summary>SetFreeCamera @0x140c2afd0.</summary>
    void SetFreeCamera(bool on)
    {
        FreeCamera = on;
        host.SetFreeCamera(on);
    }

    /// <summary>FUN_140c2a780 (:1-1536): the selected and enabled state of the buttons, in the client's order.</summary>
    public void UpdateButtons()
    {
        bool craft = IsCraftMode();
        bool design = host.PlotMode == 1, blueprint = host.PlotMode == 3;
        Button("m_craftMode", selected: craft);
        Button("m_designMode", selected: design, enabled: blueprint ? false : null);
        Button("m_blueprintMode", selected: blueprint, enabled: design ? false : host.BlueprintAvailable(false));
        Button("m_freeCamera", selected: host.FreeCameraActive);
        Button("m_blueprintSave", enabled: host.BlueprintAvailable(false));
        Button("m_blueprintImport", enabled: host.BlueprintAvailable(false));
        Button("m_blueprintExport", enabled: host.BlueprintAvailable(false));
        Button("m_blueprintShop", enabled: host.BlueprintShopAvailable(false));
    }

    void Button(string name, bool? selected = null, bool? enabled = null)
    {
        if ((ChildButton != null ? ChildButton(name) : Host.Child(name)) is not { } b) return;
        if (selected is bool s) Set(b, "selected", s);
        if (enabled is bool e) Set(b, "enabled", e);
    }

    void Set(object b, string member, object value)
    {
        if (SetMember != null) SetMember(b, member, value);
        else Host.Set(b, member, value);
    }

    void Call(string name, params object[] args)
    {
        if (CallMovie != null) CallMovie(name, args);
        else Host.Invoke(Host.Entity, name, args);
    }

    /// <summary>vfunc_28 @0x140c29340: the movie's calls, compared without case (_stricmp); every other name is the base's.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (Is(name, "ToggleCraftMode")) ToggleCraftMode();
        else if (Is(name, "ToggleDesignMode")) ToggleDesignMode();
        else if (Is(name, "ToggleBlueprintMode")) ToggleBlueprintMode();
        else if (Is(name, "OnFreeCamera")) SetFreeCamera(!FreeCamera);
        else if (Is(name, "OnHomePriority")) OnHomePriority();
        else if (Is(name, "OnHomeAttribute")) OnHomeAttribute();
        else if (Is(name, "OnHomeDemolish")) OnHomeDemolish();
        else if (Is(name, "OnBlueprintExport")) OnBlueprintExport();
        else if (Is(name, "OnDesignLoad")) host.SetSaveSlotMode(false);
        else if (Is(name, "OnBlueprintImport")) OnBlueprintImport();
        else if (Is(name, "OnScreenshot") || Is(name, "OnDesignSave") || Is(name, "OnBlueprintSave")
            || Is(name, "OnBlueprintShop")) { }   // STILL-OPEN, see the class doc
        else return false;
        return true;
    }

    /// <summary>OnBlueprintImport @0x140c2a320: FUN_141902580(1) (blueprints available, with the shutdown notice), then SetLoadMode of the slot dialog.</summary>
    void OnBlueprintImport()
    {
        if (!host.BlueprintAvailable(true)) return;
        host.SetSaveSlotMode(false);
    }

    static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>The shared gate of OnHomePriority and OnHomeAttribute: the owner check or either mode, else the notice 0x6e1.</summary>
    bool Allowed()
    {
        if (host.CanChange || host.PlotMode == 1 || host.PlotMode == 3) return true;
        SystemNotice(CommonText(0x6e1));
        return false;
    }

    /// <summary>OnHomePriority @0x140c2a370 (uihomemenudialog.cpp:526): the home option dialog (0x4f26).</summary>
    public void OnHomePriority()
    {
        if (Allowed()) host.ToggleDialog(HomeOptionId);
    }

    /// <summary>OnHomeAttribute @0x140c2a410: the same gate, dialog 0x4f7a.</summary>
    public void OnHomeAttribute()
    {
        if (Allowed()) host.ToggleDialog(HomeAttributeId);
    }

    /// <summary>OnHomeDemolish @0x140c2a4b0: type 0x1f box of common 0x6e2, preset 2, check box 0x9d1, no agreement, 0x1c0 x 400; yes sends the demolish.</summary>
    public void OnHomeDemolish()
    {
        var box = new MessageBoxData(0x1f) { Msg = CommonText(0x6e2), Extra = "" };
        box.SetButtonPreset(2);
        box.SetCheckbox0(CommonText(0x9d1), false);
        box.SetAgreement(false);
        box.SetSize(0x1c0, 400);
        box.Callback = (n, _) => SendRequestCube(n);
        ShowBox(box);
    }

    /// <summary>SendRequestCube @0x140c2b3e0: on yes, feature 0xd4 runs MyPC::FUN_14152f3c0, then RequestCube 0x1f.</summary>
    public bool SendRequestCube(int result)
    {
        if (result == 0)
        {
            if (host.FeatureEnabled(0xd4) && host.HasPlayer) host.BeforeDemolish();
            host.SendDemolish();
        }
        return true;
    }

    /// <summary>OnBlueprintExport @0x140c2a6a0: with blueprints (and cubes) dialog 0x4f7b, else the box of common 0xbba.</summary>
    public void OnBlueprintExport()
    {
        if (!host.BlueprintAvailable(true)) return;
        if (host.CubeCount > 0) host.ToggleDialog(BlueprintExportId);
        else ShowBox(MessageBoxQueue.Create(0, CommonText(0xbba), null, ""));
    }

    /// <summary>ToggleCraftMode @0x140c29b90: allowed for the owner, either mode, or FUN_140930c50; in craft the mode goes off, else on; otherwise notice 0x6e1.</summary>
    public void ToggleCraftMode()
    {
        if (!host.HasPlayer) return;
        if (host.CanChange || host.PlotMode == 1 || host.PlotMode == 3 || host.InOwnersHouse)
        {
            if (IsCraftMode()) host.SendCraftModeOff();
            else host.EnterCraftMode();
        }
        else SystemNotice(CommonText(0x6e1));
    }

    /// <summary>ToggleDesignMode @0x140c29710.</summary>
    public void ToggleDesignMode()
    {
        if (!host.HasPlayer) return;
        if (host.HasService && !host.CanChange && !host.CanEditHome) { SystemNotice(CommonText(0x6e1)); return; }
        if (host.PlotMode == 1)
        {
            if (host.DesignHasChanges) LeaveBox("s_home_info_exit_messagebox_save");
            else host.SendRequestMoveFieldMode(3);
        }
        else host.SendRequestMoveFieldMode(4);
    }

    /// <summary>ToggleBlueprintMode @0x140c29980.</summary>
    public void ToggleBlueprintMode()
    {
        if (!host.BlueprintAvailable(true)) return;
        if (!host.HasPlayer) return;
        if (host.PlotMode == 3)
        {
            if (host.DesignHasChanges) LeaveBox("s_home_info_exit_blueprint_messagebox_save");
            else host.SendRequestMoveFieldMode(3);
        }
        else host.SendRequestMoveFieldMode(5);
    }

    /// <summary>The save question: type 1, Enter does not answer it, preset 1, OnLeaveDesignResult as the callback.</summary>
    void LeaveBox(string key)
    {
        var box = MessageBoxQueue.Create(1, Str(key), null, "");
        box.EnterAllowed = false;   // FUN_141a024a0(0)
        box.SetButtonPreset(1);
        box.Callback = (n, _) => OnLeaveDesignResult(n);
        ShowBox(box);
    }

    /// <summary>OnLeaveDesignResult @0x140c2b390: no player, false; yes sends MoveFieldMode3.</summary>
    public bool OnLeaveDesignResult(int result)
    {
        if (!host.HasPlayer) return false;
        if (result == 0) host.SendRequestMoveFieldMode(3);
        return true;
    }

    public bool OnMessageBox(MessageBoxController sender, int button, string tag) => true;
}
