using Godot;
using Ms2.Formats.Model;
using Ms2.Ui;

namespace Ms2Godot.Net;

// The action key's current action as the tooltip (UIActionTooltip 0x4e5c) reads it: the key (m_nCur), the caption
// (vfunc_6) and the target point (vfunc_12).
public partial class LiveSession
{
    /// <summary>
    /// The client key (m_nCur) of the poll's current action: the interact part splits into gathering (4), hold (6), taxi (8),
    /// interact npc (0x11) and interact (0x12) by what its poll picked; <see cref="Ms2.Ui.Hud.ActionTooltipController.NoAction"/>
    /// for none.
    /// </summary>
    public int ActionKeyCode(ActionKeyTarget target) => target switch
    {
        ActionKeyTarget.Interact => InteractObjects is { IsGatheringCurrent: true } ? 4
            : holdPolled ? 6 : taxiPolled ? 8 : npcPolled ? 0x11 : 0x12,
        ActionKeyTarget.Hold => 6,
        ActionKeyTarget.PutDownLiftable => 0xc,
        ActionKeyTarget.Portal => 0x13,
        ActionKeyTarget.Talk => 0x14,
        ActionKeyTarget.Lift => 0x15,
        ActionKeyTarget.BillBoard => 0x18,
        ActionKeyTarget.Reactor => 0x1a,
        ActionKeyTarget.HoldPc => 0x1b,
        _ => Ms2.Ui.Hud.ActionTooltipController.NoAction,
    };

    /// <summary>
    /// The current action's vfunc_6 for the tooltip: gathering (GatheringAction::vfunc_6, the counted names), hold
    /// (HoldAction::vfunc_6) and reactor (ReactorAction::vfunc_6, kept while the polled reactor and its state stay).
    /// STILL-OPEN: the other actions' captions are "", so their tooltip stays hidden.
    /// </summary>
    public string ActionKeyCaption(int code)
    {
        switch (code)
        {
            case 4: return InteractObjects?.GatheringCaption() ?? "";
            case 6: return HoldCaption(CaptionTables());
            case 0x1a:
                var r = polledReactor != 0 ? reactors?.Get(polledReactor) : null;
                var state = r?.State;
                if (polledReactor != reactorCaptionId || !ReferenceEquals(state, reactorCaptionState) || reactorCaption == null)
                {
                    reactorCaptionId = polledReactor;
                    reactorCaptionState = state;
                    var t = CaptionTables();
                    reactorCaption = reactors?.Caption(polledReactor, t.Common) ?? "";
                }
                return reactorCaption;
        }
        return "";
    }
    int reactorCaptionId;
    object reactorCaptionState;
    string reactorCaption;

    /// <summary>The current action's vfunc_12: the polled reactor's position (ReactorAction::vfunc_12
    /// @0x14095fb80); none for gathering and hold, which keep ActionKeyObject's. STILL-OPEN: the portal, interact,
    /// interact npc and pull-weapon targets.</summary>
    public Vec3? ActionKeyTargetPoint(int code)
    {
        if (code != 0x1a || polledReactor == 0 || reactors?.Get(polledReactor) is not { } r) return null;
        return new Vec3(r.Position.X, r.Position.Y, r.Position.Z);
    }

    /// <summary>GetLocalPlayer: the own pc is in the field.</summary>
    public bool HasLocalPlayer => player != null && IsInstanceValid(player);

    static Ms2.Ui.GameTables CaptionTables() =>
        Ms2.Ui.GameTables.Shared(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir), GameOptions.StringLanguage);
}
