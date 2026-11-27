using Godot;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The skill preview window: UISkillPreviewDialog (0x4ec3, movie uiskillpreviewdialog) as <see cref="SkillPreviewController"/>,
/// opened by the dynamic action dialog's shift + right click (UIDynamicActionDialog::PreviewAction @0x14192ca10). The movie's
/// loadRenderTarget loads "img://&lt;movie&gt;:avatar/&lt;w&gt;/&lt;h&gt;"; the view is the character window's paper doll
/// (<see cref="AvatarView"/>) with the dialog's zoom (<see cref="SkillPreviewController.CameraMinDistance"/>, two steps out from the eye), and the
/// emotion plays on it (<see cref="AvatarView.PlayEmotion"/>). Escape closes it through the dialog key walk
/// (DialogGates.EscapeKey). STILL-OPEN: the list on the controller.
/// </summary>
public partial class HudScene
{
    SkillPreviewController skillPreview;   // from the first open; the dialog is the HUD's one instance (FindObject(0x4ec3))
    MovieDialog skillPreviewMovie;         // the movie, from open until the unload
    AvatarView skillPreviewView;           // the avatar's view, from the first render target load until the unload

    bool SkillPreviewOpen => skillPreviewMovie != null && skillPreview?.IsClosed == false;

    /// <summary>PreviewAction @0x14192ca10 (uidynamicactiondialog.cpp:473-481): OpenDialog(0x4ec3), then
    /// FUN_140e93e70(action, 0). An open dialog is kept (OpenDialog of an open dialog), so the next action plays on the same
    /// avatar.</summary>
    void PreviewSkillAction(uint action)
    {
        if (!SkillPreviewOpen) OpenSkillPreview();
        skillPreview?.Preview(action, 0);
    }

    void OpenSkillPreview()
    {
        var t = hudModel.Tables;
        var c = skillPreview = new SkillPreviewController
        {
            Skill = id => t.Skills.GetSkillData(id),
            HasLocalPlayer = () => GetTree().GetFirstNodeInGroup(Player.Group) is Player,
            PlayEmotion = row => skillPreviewView?.PlayEmotion(row),
            RotateLimit = avatarLimit ??= AvatarRotateLimit.FromConstants(assets.Packs),
        };
        c.Open();
        c.Unloaded += () =>
        {
            skillPreviewView?.QueueFree();
            skillPreviewView = null;
            skillPreviewMovie?.Host.Unload();
            skillPreviewMovie?.QueueFree();
            skillPreviewMovie = null;
        };
        var m = skillPreviewMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        string avatar = "img://" + m.Host.Id + ":avatar/";
        foreach (var n in m.Kids())
            if (n is As3View v) v.ImageLookup = url =>
            {
                if (!url.StartsWith(avatar, StringComparison.OrdinalIgnoreCase) || c.Avatar == null) return null;
                if (skillPreviewView == null && AvatarView.SizeOf(url) is { } size
                    && (skillPreviewView = AvatarView.Create(assets.Packs, Settings.NifXml, size, c.Avatar,
                        SkillPreviewController.CameraMinDistance, stepsDone: SkillPreviewController.CameraStepsDone)) != null)
                {
                    AddChild(skillPreviewView);
                    c.AvatarReady();      // the first draw replays the kept action
                }
                return skillPreviewView?.Image;
            };
        uiRoot.AddChild(m);
    }
}
