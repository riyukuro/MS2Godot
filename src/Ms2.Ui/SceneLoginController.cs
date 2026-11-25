namespace Ms2.Ui;

/// <summary>
/// UISceneLogin (ms2/client/ms2/uiscenelogin.cpp, scene 0x6200009): the login scene's own movie (uiscenelogin, AS class
/// scaleform.ms2.scene.SceneLogin) over the background movie. It draws the logo (the movie's logo_ani symbol), the age
/// rating marks (m_grade), the loading bar and the bottom left buttons (Quit, Options, Prev). It is a UISwfEntity, not a
/// UIDialog.
/// The load progress is a 16-bit mask (m_nProgress): bit 0 is the scene shown (vfunc_55), 0x3ffe the file preload done,
/// 0x4000 the background's title shown (preset off the Nexon platform), 0x8000 the background loaded. All 16 open the
/// login dialogs. IsNexonPlatform is 0 for the id and password start, so the title shows as soon as the background has
/// loaded; the launcher ticket's start is the Nexon platform's (<see cref="Passport"/>): the title plays to its end and the
/// scene connects with no login dialog.
/// The region list dialog (0x4eef) goes with <see cref="RegionListUsed"/>, which is false on every start this client
/// makes. STILL-OPEN: the preload (CFilePreload) is taken as done.
/// </summary>
public sealed class SceneLoginController : UiController
{
    public override string Movie => "uiscenelogin";
    protected override string EntityMethod => "showButtonsAndAlign";
    protected override bool IsDialog => false;

    /// <summary>CFeatureManager::IsFeatureEnabled by id; feature 0xd8 (MushKingRoyaleModeSelect) shows the Prev button.</summary>
    public Func<int, bool> IsFeatureEnabled = _ => false;

    /// <summary>GetRegion() == 1 shows the copyright line (ShowTitle).</summary>
    public int Region;

    /// <summary>
    /// GFxMovieBackground::GotoAndPlay("endTitleAni") on the background (FUN_141acf7c0): true when the background took the
    /// label. Null while there is no background movie.
    /// </summary>
    public Func<string, bool> BackgroundGotoAndPlay;

    /// <summary>OpenLoginDialogs (:0x141acfa00): the login dialog 0x4e22 (after the region list when that is used).</summary>
    public event Action OpenLoginDialogs;
    /// <summary>OnGameExit (FUN_1418fc760, uichatballoon.cpp:463): the quit message box 0x288.</summary>
    public event Action GameExit;
    /// <summary>OnGameOption (FUN_1418fc760): the options dialog 0x4ef3.</summary>
    public event Action GameOption;
    /// <summary>OnPrev (vfunc_28): the state service goes to the mode select scene 0x6200085.</summary>
    public event Action Prev;
    /// <summary>
    /// OnEndPopdown (the movie's hide tween ended): UISceneLogin::OnEndPopdown (uiscenelogin.cpp:482-487), the connect to the
    /// configured server, when the region list is not used (its own popdown connects then).
    /// </summary>
    public event Action EndPopdown;

    /// <summary>
    /// IsNexonPlatform for this start: the launcher ticket's passport start (Launch.LoginStart.Passport). vfunc_49 starts the
    /// progress at 0, so the background's endShowTitle sets 0x4000, and at 0xffff the scene opens no login dialog: when not
    /// connected it hides its movie (FUN_141acf420), whose OnEndPopdown connects (uiscenelogin.cpp:481-486).
    /// </summary>
    public bool Passport;
    /// <summary>
    /// IsRegionListUsed (:0x141acfb40, uiscenelogin.cpp:443-456): on the Nexon platform its vfunc_0 is 1, else the client
    /// country (DAT_143ab0248, set from -locale by FUN_1406509e0, localepath.cpp:597-608: 0 KR, 1 JP, 2 NA, 3 CN) is 2.
    /// </summary>
    public static bool IsRegionListUsed(bool nexonPlatform, int platformKind, int country) =>
        nexonPlatform ? platformKind == 1 : country == 2;

    /// <summary>
    /// <see cref="IsRegionListUsed"/> for this start; false on every start that reaches the scene. The Nexon Korea platform
    /// (country 0) keeps the base vfunc_0, 0 (ms2clientplatformbase.cpp:187; ms2clientplatformnexonkorea.h overrides
    /// vfunc_1 only), and off it the country is 0 or 3: CreateClientPlatform makes a platform for those two only
    /// (ms2clientplatform.cpp:26-44) and InitInstance quits without one (ms2.cpp:1610-1617), so NA (2) never gets here.
    /// The NA client's region source (its platform's vfunc_57, which UIRegionListDialog reads) is not in this client.
    /// </summary>
    public bool RegionListUsed;
    /// <summary>OpenDialog(0x4eef), the region list, from OpenLoginDialogs and FUN_141acf420 when <see cref="RegionListUsed"/>.</summary>
    public event Action OpenRegionList;
    /// <summary>FUN_141acf470's UIRegionListDialog::FUN_140e80910 (popup) when <see cref="RegionListUsed"/>.</summary>
    public event Action PopupRegionList;

    /// <summary>CNetworkService::IsConnected for the passport start's check (uiscenelogin.cpp:205-208); null is not connected.</summary>
    public Func<bool> Connected;

    int progress;          // m_nProgress
    bool progressDirty;    // m_nProgressDirty
    bool shown;            // m_nShown: the login dialogs were opened
    int exitFlag;          // m_nExitFlag: 1 once the login dialogs opened (the Quit button shows from then on)
    bool titleDone;        // m_nTitleDone
    bool backgroundLoaded;

    public int Progress => progress;
    public bool Shown => shown;

    /// <summary>vfunc_49 (:0x141acee00, uiscenelogin.cpp:169-182), entering the scene: off the Nexon platform the progress
    /// starts at 0x4000, on it (<see cref="Passport"/>) at 0.</summary>
    public void Enter()
    {
        titleDone = false;
        shown = false;
        progress = Passport ? 0 : 0x4000;
    }

    /// <summary>
    /// OnLoadComplete (:0x141acf1f0): UISwfEntity's, then the preload's bits when it is done, then vfunc_60 when the
    /// background (GFxManager movie 4) is already loaded. FUN_141bcfbd0 (WorldService), which calls vfunc_55 here, is
    /// not ported: <see cref="SceneShown"/> is called by the scene.
    /// </summary>
    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        AddProgressBits(0x3ffe);
        if (backgroundLoaded) BackgroundLoaded();
    }

    /// <summary>vfunc_55 (:0x141aceec0): bit 1.</summary>
    public void SceneShown() => AddProgressBits(1);

    /// <summary>vfunc_60 (:0x141acf190), the background movie loaded: bit 0x8000, then the title when 0x4000 is set.</summary>
    public void BackgroundLoaded()
    {
        backgroundLoaded = true;
        if (Host?.Entity == null) return;      // OnLoadComplete takes it up
        AddProgressBits(0x8000);
        if ((progress & 0x4000) != 0) EndTitle();
    }

    /// <summary>vfunc_61 (:0x141acf0a0): the background's events, "showTitle" and "endShowTitle".</summary>
    public void BackgroundEvent(string ev)
    {
        if (string.Equals(ev, "showTitle", StringComparison.OrdinalIgnoreCase)) ShowTitle();
        else if (string.Equals(ev, "endShowTitle", StringComparison.OrdinalIgnoreCase)) AddProgressBits(0x4000);
    }

    /// <summary>vfunc_13's Escape (:0x141acf000): with the background loaded and the title not done, the title is skipped.</summary>
    public bool EscapePressed()
    {
        if ((progress & 0x8000) == 0 || titleDone) return false;
        EndTitle();
        return true;
    }

    /// <summary>
    /// vfunc_15 (:0x141aceef0, uiscenelogin.cpp:192-220), every frame: the bar, then while the dialogs are not open the
    /// preload's bits, and at 0xffff the login dialogs; on the Nexon platform (<see cref="Passport"/>) none: when the network
    /// service is not connected (and DAT_143ab284b is clear) FUN_141acf420 hides the movie, whose OnEndPopdown connects.
    /// </summary>
    public void Tick()
    {
        if (progressDirty) UpdateProgressBar();
        if (shown) return;
        AddProgressBits(0x3ffe);
        if (progress == 0xffff)
        {
            if (!Passport) { if (RegionListUsed) OpenRegionList?.Invoke(); OpenLoginDialogs?.Invoke(); }
            else if (Connected?.Invoke() != true) RegionListOrHide();
            shown = true;
            exitFlag = 1;
        }
    }

    /// <summary>FUN_141acf7c0: the title shown, the background on to "endTitleAni" (bit 0x4000 when it took the label).</summary>
    void EndTitle()
    {
        ShowTitle();
        if (BackgroundGotoAndPlay?.Invoke("endTitleAni") == true) AddProgressBits(0x4000);
        titleDone = true;
    }

    /// <summary>ShowTitle (:0x141acf5a0): show(obj) with the button flags, logoAniName "logo_ani" and, for region 1, showCopyRight.</summary>
    public void ShowTitle()
    {
        if (Host?.Entity == null) return;
        var o = ButtonFlags();
        Host.Set(o, "logoAniName", "logo_ani");
        if (Region == 1) Host.Set(o, "showCopyRight", true);
        Host.Invoke("show", o);
    }

    /// <summary>FUN_141acf420 (uiscenelogin.cpp:311-317): the region list when used, else the movie hides.</summary>
    void RegionListOrHide()
    {
        if (RegionListUsed) OpenRegionList?.Invoke();
        else HideMovie();
    }

    /// <summary>
    /// FUN_141acf470 (vfunc_64, the connect failed, uiscenelogin.cpp:320-338): the region list pops up again when used, then
    /// failConnect(obj) with the button flags; the movie shows again.
    /// </summary>
    public void FailConnect()
    {
        if (RegionListUsed) PopupRegionList?.Invoke();
        if (Host?.Entity == null) return;
        Host.Invoke("failConnect", ButtonFlags());
    }

    /// <summary>HideMovie (:0x141acfa70): hide; the movie fades out and answers OnEndPopdown.</summary>
    public void HideMovie()
    {
        if (Host?.Entity != null) Host.Invoke("hide");
    }

    /// <summary>SetButtonFlags (:0x141acfba0): showExit once the dialogs opened, showOption always, showPrev with feature 0xd8.</summary>
    Avm2.AsObject ButtonFlags() => Host.Obj(("showExit", exitFlag == 1), ("showOption", true), ("showPrev", IsFeatureEnabled(0xd8)));

    /// <summary>AddProgressBits (:0x141acf700).</summary>
    void AddProgressBits(int bits)
    {
        int old = progress;
        progress |= bits;
        progressDirty = old != progress;
    }

    /// <summary>UpdateProgressBar (:0x141acf890): once 0x4000 is set, showPrograssBar(the bits set of the low 16, 16).</summary>
    void UpdateProgressBar()
    {
        if (!progressDirty || (progress & 0x4000) == 0) return;
        if (Host?.Entity != null)
            Host.Invoke("showPrograssBar", System.Numerics.BitOperations.PopCount((uint)(progress & 0xffff)), 0x10);
        progressDirty = false;
    }

    /// <summary>vfunc_28 (:0x141acf2f0): FUN_1418fc760's OnGameExit / OnGameOption, then OnPrev and OnEndPopdown.</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (Is(name, "OnGameExit")) { GameExit?.Invoke(); return true; }
        if (Is(name, "OnGameOption")) { GameOption?.Invoke(); return true; }
        if (Is(name, "OnPrev")) { Prev?.Invoke(); return true; }
        if (Is(name, "OnEndPopdown")) { if (!RegionListUsed) EndPopdown?.Invoke(); return true; }
        return false;
    }

    static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
