using Godot;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// UIRewardNotify (0x4e5d, movie uirewardnotify) with its C++ side as <see cref="RewardNotifyController"/>: the merat toasts
/// the Meret packets post. The movie loads on the first toast and stays; the controller holds toasts back until it is
/// loaded (UIRewardNotify::AppendReward) and hands them over from the frame step (vfunc_15).
/// </summary>
public partial class HudScene
{
    MovieDialog rewardNotifyMovie;
    RewardNotifyController rewardNotify;

    RewardNotifyController EnsureRewardNotify()
    {
        if (rewardNotify != null) return rewardNotify;
        var c = rewardNotify = new RewardNotifyController { Sound = key => FieldAudio.Instance?.PlayUi(key), SubSceneId = () => Ms2Godot.Net.LiveSession.Instance?.Guide?.World.SubSceneId ?? 0 };
        c.OpenDialog += id => GD.Print($"reward notify: dialog 0x{id:x} is not in this HUD");
        rewardNotifyMovie = new MovieDialog(assets, c, hudModel, ScreenMode.PixelSize(GetWindow()));
        uiRoot.AddChild(rewardNotifyMovie);
        return c;
    }
}
