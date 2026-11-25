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
    /// The current action's vfunc_6 for the tooltip, by key: gathering 4 (the counted names), hold 6, interact npc 0x11,
    /// interact 0x12, billboard 0x18, reactor 0x1a; each kept while what it reads stays the same. The talk's (0x14) is the
    /// field talk's (NpcTalk.TalkActionCaption). STILL-OPEN: portal, taxi, lift, put down and hold pc give "".
    /// </summary>
    public string ActionKeyCaption(int code)
    {
        switch (code)
        {
            case 4: return InteractObjects?.GatheringCaption() ?? "";
            case 6: return HoldCaption(CaptionTables());
            case 0x11:
                var npc = InteractNpcs?.Current;
                if (npc == null) return "";
                if (!ReferenceEquals(npc, npcCaptionNpc) || npcCaption == null)
                {
                    npcCaptionNpc = npc;
                    npcCaption = ActionCaption.Interact(CaptionTables().Common(0x634) ?? "", npc.Row?.Name ?? "");
                }
                return npcCaption;
            case 0x12: return InteractObjects?.ActionPickCaption(billBoard: false) ?? "";
            case 0x18: return InteractObjects?.ActionPickCaption(billBoard: true) ?? "";
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
    InteractNpcEntry npcCaptionNpc;
    string npcCaption;
    int reactorCaptionId;
    object reactorCaptionState;
    string reactorCaption;

    /// <summary>The current action's vfunc_12: the interact npc's, the interact object's and the billboard's position, the
    /// polled reactor's (ReactorAction::vfunc_12 @0x14095fb80); none for the actions that keep ActionKeyObject's.</summary>
    public Vec3? ActionKeyTargetPoint(int code)
    {
        switch (code)
        {
            case 0x11: return InteractNpcs?.Current?.Position;                                 // InteractNpcAction::vfunc_12 (@0x140959630)
            case 0x12: return InteractObjects?.ActionPick(billBoard: false)?.Position;          // InteractAction::vfunc_12 (@0x140958d50)
            case 0x18: return InteractObjects?.ActionPick(billBoard: true)?.Position;           // the billboard keeps InteractAction's
            case 0x1a:
                if (polledReactor == 0 || reactors?.Get(polledReactor) is not { } r) return null;
                return new Vec3(r.Position.X, r.Position.Y, r.Position.Z);
        }
        return null;
    }

    /// <summary>The current action's vfunc_7 (the title the tooltip compares): InteractAction's object id
    /// (@0x140958c50, the billboard keeps it), ReactorAction's "reactor" (@0x14095faa0); "" for the rest.</summary>
    public string ActionKeyTitle(int code) => code switch
    {
        0x12 => InteractObjects?.ActionPick(billBoard: false)?.Name ?? "",
        0x18 => InteractObjects?.ActionPick(billBoard: true)?.Name ?? "",
        0x1a => "reactor",
        _ => "",
    };

    /// <summary>GetLocalPlayer: the own pc is in the field.</summary>
    public bool HasLocalPlayer => player != null && IsInstanceValid(player);

    static Ms2.Ui.GameTables CaptionTables() =>
        Ms2.Ui.GameTables.Shared(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir), GameOptions.StringLanguage);
}
