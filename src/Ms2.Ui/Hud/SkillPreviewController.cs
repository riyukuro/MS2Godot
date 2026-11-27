using Ms2.Ui.Skills;

namespace Ms2.Ui.Hud;

/// <summary>
/// The skill preview window, UISkillPreviewDialog (client/ms2/uiskillpreviewdialog.cpp, UI id 0x4ec3, type name
/// "SkillPreviewDialog", movie uiskillpreviewdialog): a copy of the local player (UIAvatarPC(local PC, true)) in the movie's
/// render target, playing the action asked for. Its one opener is the dynamic action dialog's shift + right click
/// (UIDynamicActionDialog::PreviewAction @0x14192ca10, <see cref="DynamicActionDialogController"/>), with level 0.
/// The avatar itself is the host's: it says when the avatar's view is up (<see cref="AvatarReady"/>), plays an emotion on it
/// (<see cref="PlayEmotion"/>) and takes the mouse calls through <see cref="Avatar"/>.
/// <para>STILL-OPEN: a non-emotion skill (UIAvatarPC::FUN_1418bcf60, AvatarPC::PlaySkill avatarpc.cpp:527) plays nothing
/// here (<see cref="PlaySkill"/> is the host's, which does not port it); the one opener sends only emotions. The skin colour
/// list (UIAvatarBase's OnAvatarSkinColor), the top-most layer (SetTopMostLayer) and the close on a ride with hidePCInfo
/// above 1 (mypc.cpp:3314) are not ported here.</para>
/// </summary>
public sealed class SkillPreviewController : UiController
{
    public override string Movie => "uiskillpreviewdialog";
    protected override string EntityMethod => "initData";
    public override string TypeName => "SkillPreviewDialog";

    public const int UiId = 0x4ec3;
    /// <summary>vfunc_25's camera: ApplyCharCamera, then m_fMinDistance 1000, m_nStepsDone 2, m_nSteps 5.</summary>
    public const float CameraMinDistance = 1000f;
    public const int CameraStepsDone = 2, CameraSteps = 5;

    /// <summary>The ctor (uiskillpreviewdialog.cpp:17) has no SetTweenEnabled: m_c4 stays 0, so the dialog opens and closes
    /// without the movie's tween.</summary>
    protected override bool TweenEnabled => false;

    /// <summary>SkillDataTableCli::GetSkillData; null when unknown.</summary>
    public Func<uint, SkillData> Skill = _ => null;
    /// <summary>WorldService::GetLocalPlayer exists (CreateAvatar asserts and makes nothing without one).</summary>
    public Func<bool> HasLocalPlayer = () => true;
    /// <summary>UIAvatarPC::FUN_1418bce80 (uiavatarbase.cpp:1509): the avatar's PCBase::FUN_14166ed70 with the emotion's row.</summary>
    public Action<SkillData> PlayEmotion = _ => { };
    /// <summary>UIAvatarPC::FUN_1418bcf60 (uiavatarbase.cpp:1531): AvatarPC::PlaySkill(skill, level).</summary>
    public Action<SkillData, int> PlaySkill = (_, _) => { };
    /// <summary>The avatar's turn and zoom (UIAvatarBase::vfunc_27); null while no avatar is made.</summary>
    public AvatarControl Avatar { get; private set; }
    /// <summary>UIAvatarPC::vfunc_1's CLimitCharRotate; null: no limit.</summary>
    public AvatarRotateLimit RotateLimit;

    /// <summary>m_skill / m_level: the action kept until the avatar is loaded, 0 when none.</summary>
    public uint PendingSkill { get; private set; }
    public int PendingLevel { get; private set; }
    /// <summary>UIAvatarPC::IsLoaded: the host's view of the avatar is up.</summary>
    public bool AvatarLoaded { get; private set; }

    /// <summary>vfunc_65 (@0x140e93870, :41): m_skill = m_level = 0 and CreateAvatar (@0x140e93ff0, :180): nothing when an
    /// avatar is held or there is no local player; else the avatar, not loaded yet. STILL-OPEN: SetTopMostLayer.</summary>
    public void Open()
    {
        PendingSkill = 0;
        PendingLevel = 0;
        if (Avatar != null || !HasLocalPlayer()) return;
        Avatar = new AvatarControl(RotateLimit ?? new AvatarRotateLimit(0, 360), 0, 0, 90);
        AvatarLoaded = false;
    }

    /// <summary>
    /// FUN_140e93e70 (@0x140e93e70, :150): skill 0 or no avatar does nothing; the action is kept (m_skill, m_level); an avatar not
    /// loaded yet keeps it for vfunc_25; a skill with no row asserts and stays kept; else an emotion (SkillData::IsEmotion) plays
    /// through <see cref="PlayEmotion"/>, anything else through <see cref="PlaySkill"/>, and the kept action is cleared.
    /// </summary>
    public void Preview(uint skill, int level)
    {
        if (skill == 0 || Avatar == null) return;
        PendingSkill = skill;
        PendingLevel = level;
        if (!AvatarLoaded) return;
        var row = Skill(skill);
        if (row == null) return;
        if (row.IsEmotion) PlayEmotion(row);
        else PlaySkill(row, level);
        PendingSkill = 0;
        PendingLevel = 0;
    }

    /// <summary>
    /// The host's avatar view is up: vfunc_o178_1 (@0x140e93d40, :127, the avatar's loaded event: loadRenderTarget again and
    /// initData of an AScript(2), an empty object) and vfunc_25 (@0x140e939f0, :72), the first draw into the render target,
    /// which plays the kept action (FUN_140e93e70(m_skill, m_level)). Later draws find nothing kept.
    /// </summary>
    public void AvatarReady()
    {
        if (Avatar == null || AvatarLoaded) return;
        AvatarLoaded = true;
        Preview(PendingSkill, PendingLevel);
    }

    public override void OnLoadComplete()
    {
        base.OnLoadComplete();
        if (Avatar == null) return;
        LoadRenderTarget();
        if (Host?.Entity != null) Host.Invoke(Host.Entity, "initData", Host.Obj());
    }

    /// <summary>vfunc_28 (@0x140e93930, :62): the dialog's own calls, then the avatar's (the mouse turn and zoom).</summary>
    public override bool OnCallback(string name, object[] args) =>
        Avatar != null && name.StartsWith("OnAvatar", StringComparison.OrdinalIgnoreCase) && Avatar.OnCallback(name, args ?? Array.Empty<object>());

    /// <summary>LoadRenderTarget / UnloadRenderTarget (@0x140e94150, @0x140e941b0): the movie's call while it is up.</summary>
    void LoadRenderTarget() { if (Host?.Entity != null) Host.Invoke(Host.Entity, "loadRenderTarget"); }
    void UnloadRenderTarget() { if (Host?.Entity != null) Host.Invoke(Host.Entity, "unloadRenderTarget"); }

    /// <summary>vfunc_26 (@0x140e93800): UnloadRenderTarget; vfunc_67 (@0x140e93830): the avatar dropped.</summary>
    public override void Close()
    {
        if (IsClosed) return;
        UnloadRenderTarget();
        Avatar = null;
        AvatarLoaded = false;
        base.Close();
    }
}
