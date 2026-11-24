using Godot;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The couple action dialog: UICoupleActionDialog (0x4f4e, movie uicoupleaction) over the live session's emotions, opened
/// by the popup menu's couple action item (OpenCoupleActionDialog, uipartydialog.cpp:3435) for the menu's character.
/// Live, the favorite toggle sends Emote mode 4; offline the list is empty and the send is logged.
/// </summary>
public partial class HudScene
{
    CoupleActionDialogController coupleAction;
    MovieDialog coupleActionMovie;             // from open until the close

    bool CoupleActionOpen => coupleActionMovie != null && coupleAction?.IsClosed == false;

    void CoupleAction()
    {
        if (coupleAction != null) return;
        var live = Ms2Godot.Net.LiveSession.Instance;
        var t = hudModel.Tables;
        var c = coupleAction = new CoupleActionDialogController(live?.Emotions ?? offlineEmotions)
        {
            Skill = id => t.Skills.GetSkillData(id),
            ActionName = t.SkillName,
            Reserved = t.DynamicActionReserved,
            Now = () => Ms2Godot.Net.LiveSession.Instance?.GameSession?.ServerTime ?? 0,
            Send = p =>
            {
                if (Ms2Godot.Net.LiveSession.Instance is { } l) l.SendTalk(p);
                else GD.Print($"couple action: {p.GetType().Name} not sent (offline)");
            },
            HasPlayer = () => GetTree().GetFirstNodeInGroup(Player.Group) is Player,
            // WorldService::FUN_141bd1530: the character among the field's users
            HasCharacter = id => Ms2Godot.Net.LiveSession.Instance?.UserInField(id) ?? FixtureUserInField(id),
            UseSkill = UseOwnSkill,
        };
        c.Unloaded += () =>
        {
            coupleActionMovie?.Host.Unload();
            coupleActionMovie?.QueueFree();
            coupleActionMovie = null;
        };
    }

    /// <summary>OpenCoupleActionDialog (uipartydialog.cpp:3435): OpenDialog(0x4f4e) when it is closed (vfunc_65 clears the
    /// selection and filters), then SetSelectedCharacter(m_a8).</summary>
    void OpenCoupleActionDialog(long characterId)
    {
        CoupleAction();
        if (!CoupleActionOpen)
        {
            if (coupleActionMovie != null) { coupleActionMovie.Host.Unload(); coupleActionMovie.QueueFree(); coupleActionMovie = null; }
            coupleAction.Opened();              // vfunc_65
            coupleActionMovie = new MovieDialog(assets, coupleAction, hudModel, ScreenMode.PixelSize(GetWindow()));
            uiRoot.AddChild(coupleActionMovie);
        }
        coupleAction.SetSelectedCharacter(characterId);
    }
}
