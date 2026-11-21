using System;
using Godot;
using Ms2.Ui;
using Ms2.Ui.Hud;

namespace Ms2Godot.Ui;

/// <summary>
/// The dynamic action dialog: UIDynamicActionDialog (0x4e7a, movie uidynamicaction) over the live session's emotions.
/// Live, the favorite toggle sends Emote mode 4; offline the list is empty and the send is logged.
/// </summary>
public partial class HudScene
{
    DynamicActionDialogController dynamicAction;
    MovieDialog dynamicActionMovie;            // from open until the close
    Action toggleDynamicAction;                // the hook the shared messenger log calls
    static readonly EmotionList offlineEmotions = new();

    bool DynamicActionOpen => dynamicActionMovie != null && dynamicAction?.IsClosed == false;

    void DynamicAction()
    {
        if (dynamicAction != null) return;
        var live = Ms2Godot.Net.LiveSession.Instance;
        var t = hudModel.Tables;
        var c = dynamicAction = new DynamicActionDialogController(live?.Emotions ?? offlineEmotions)
        {
            Skill = id => t.Skills.GetSkillData(id),
            ActionName = t.SkillName,
            Reserved = t.DynamicActionReserved,
            Now = () => live?.GameSession?.ServerTime ?? 0,
            Send = p =>
            {
                if (live != null) live.SendTalk(p);
                else GD.Print($"dynamic action: {p.GetType().Name} not sent (offline)");
            },
        };
        c.Unloaded += () =>
        {
            dynamicActionMovie?.Host.Unload();
            dynamicActionMovie?.QueueFree();
            dynamicActionMovie = null;
        };
    }

    /// <summary>CUIService::ToggleDialog(0x4e7a): the messenger's eventEmotion (uimessenger.cpp:4052).</summary>
    void ToggleDynamicAction()
    {
        if (DynamicActionOpen) dynamicAction.Close();
        else OpenDynamicAction();
    }

    void OpenDynamicAction()
    {
        DynamicAction();
        if (dynamicActionMovie != null) { dynamicActionMovie.Host.Unload(); dynamicActionMovie.QueueFree(); dynamicActionMovie = null; }
        dynamicAction.Opened();                 // vfunc_65
        dynamicActionMovie = new MovieDialog(assets, dynamicAction, hudModel, ScreenMode.PixelSize(GetWindow()));
        uiRoot.AddChild(dynamicActionMovie);
    }
}
