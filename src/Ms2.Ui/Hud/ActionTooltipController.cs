using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Model;

namespace Ms2.Ui.Hud;

/// <summary>
/// UIActionTooltip (UI 0x4e5c, uiactiontooltip.cpp, "ActionTooltip"), a UIHeadUpDisplay run by the uiactiontooltip movie: the
/// action key's caption beside the current action's target. Its entity (scaleform.ms2.entity.ActionTooltip) has
/// show(keyText, caption, kind) and hide; the kind is "key" (the key button with its name), "watch" or "lock".
/// The action key manager (Unk_ActionKeyManager::Update, actionkeyobject.cpp:556-602) calls <see cref="Refresh"/> when the
/// current action changes, when its vfunc_10 asks for it, and when the shown action, title or caption no longer match
/// (<see cref="NeedsRefresh"/>); the frame (<see cref="Process"/>) hides it while the action is busy and keeps it placed.
/// </summary>
public sealed class ActionTooltipController : UiController
{
    public override string Movie => "uiactiontooltip";
    protected override string EntityMethod => "show";
    protected override bool IsDialog => false;          // a UIHeadUpDisplay
    /// <summary>The constructor clears m_58 (uiactiontooltip.cpp:85), so the entity is hidden at load until show().</summary>
    public override bool VisibleOnLoad => false;

    /// <summary>Unk_ActionKeyManager m_nCur with no action (and FUN_1418b23a0 without a local player).</summary>
    public const int NoAction = 0x1e;
    /// <summary>The key hint's action (FUN_1418b1950): 0x493e3 for action 0x1d, else the action key 0x493e2.</summary>
    public const int ActionKeyCode = 0x493e2, ActionKey1dCode = 0x493e3;
    /// <summary>ShowAction (FUN_1418b1ab0) lifts a target 75 before projecting it.</summary>
    public const float TargetLift = 75f;
    /// <summary>The Client::Constants names of the place without a target (+0xc768 / +0xc7c0, 0x58 apart like every
    /// constant entry, unit_1402ab440_constantsblocks.cpp:1468-1469) and their defaults.</summary>
    public const string OffsetXKey = "OffsetActionTooltipX", OffsetYKey = "OffsetActionTooltipY";
    public const int DefaultOffsetX = 50, DefaultOffsetY = -30;

    /// <summary>The local player is in the field (GetLocalPlayer).</summary>
    public Func<bool> HasPlayer = () => false;
    /// <summary>Unk_ActionKeyManager::IsEnabled.</summary>
    public Func<bool> Enabled = () => true;
    /// <summary>Unk_ActionKeyManager::GetCur (m_nCur): the current action's key, <see cref="NoAction"/> for none.</summary>
    public Func<int> Current = () => NoAction;
    /// <summary>The current action's vfunc_6 (the caption), "" for an action whose caption is not ported.</summary>
    public Func<string> Caption = () => "";
    /// <summary>The current action's vfunc_7 (the title; "" by default).</summary>
    public Func<string> Title = () => "";
    /// <summary>The current action's vfunc_8 (the kind; "key" by default).</summary>
    public Func<string> Kind = () => "key";
    /// <summary>The current action's vfunc_12 (the target point; none by default).</summary>
    public Func<Vec3?> Target = () => null;
    /// <summary>UITalkManager::GetTalking.</summary>
    public Func<bool> Talking = () => false;
    /// <summary>CUIService IsDialogClosed(id) == 0.</summary>
    public Func<int, bool> DialogOpen = _ => false;
    /// <summary>The own pc's GetCurStateId.</summary>
    public Func<int> OwnState = () => 0;
    /// <summary>CGameOption's gamepad state (GetGamepadState): the key name's priority is 2 with it, else 0.</summary>
    public Func<bool> Gamepad = () => false;
    /// <summary>The key table the key hint reads (the HUD's; the movie's own model when not set).</summary>
    public Func<KeyTable> Keys;
    /// <summary>WorldToScreen of a world point in stage units; null when it is not on the screen side.</summary>
    public Func<Vec3, (float X, float Y)?> Project = _ => null;
    /// <summary>The two Client::Constants offsets (<see cref="OffsetXKey"/>, <see cref="OffsetYKey"/>).</summary>
    public int OffsetX = DefaultOffsetX, OffsetY = DefaultOffsetY;

    // m_pAction (the shown action's key, -1 for none), m_strTitle, m_strText, m_kTarget
    int shownAction = -1;
    string shownTitle = "", shownText = "";
    Vec3? target;
    bool placed;

    /// <summary>The shown action's key (GetAction), -1 when hidden.</summary>
    public int ShownAction => shownAction;
    /// <summary>GetText: the shown caption.</summary>
    public string ShownText => shownText;
    /// <summary>The last show call's arguments, for the checks: the key text and the kind.</summary>
    public string ShownKeyText { get; private set; } = "";
    public string ShownKind { get; private set; } = "";
    /// <summary>The shown action's target point (lifted <see cref="TargetLift"/>), null for none.</summary>
    public Vec3? ShownTarget => shownAction >= 0 ? target : null;
    /// <summary>The entity's last place (SetPositionInt / SetPositionFloat).</summary>
    public (float X, float Y) Place { get; private set; }

    /// <summary>
    /// Unk_ActionKeyManager::Update's tooltip test (actionkeyobject.cpp:586-596), with the current action unchanged: a
    /// refresh when the tooltip shows another action, or its title or caption differ from the current action's.
    /// </summary>
    public bool NeedsRefresh()
    {
        int cur = HasPlayer() ? Current() : NoAction;
        if (cur == NoAction) return false;                 // GetCurrentAction: no action under m_nCur
        return shownAction != cur || shownTitle != Title() || shownText != Caption();
    }

    /// <summary>vfunc_15 (@0x1418b1630): the frame. Hidden while the action is busy; placed while shown; else shown when
    /// there is an action.</summary>
    public void Process()
    {
        if (Busy()) HideAction();
        else if (shownAction >= 0) PlaceEntity();
        else if (CurrentOrNone() != NoAction) ShowAction();
    }

    /// <summary>FUN_1418b16f0 (@0x1418b16f0): the manager's refresh. Hidden with no action or an empty caption, or while the
    /// action is busy; else shown anew.</summary>
    public void Refresh()
    {
        int cur = CurrentOrNone();
        if (cur == NoAction || Caption() == "") HideAction();     // FUN_1418b1a20: "" without a current action
        else if (Busy()) HideAction();
        else ShowAction();
    }

    /// <summary>vfunc_3 (@0x1418b16c0): the display is taken down hidden.</summary>
    public override void Close()
    {
        HideAction();
        base.Close();
    }

    /// <summary>FUN_1418b23a0: m_nCur, <see cref="NoAction"/> without a local player.</summary>
    int CurrentOrNone() => HasPlayer() ? Current() : NoAction;

    /// <summary>
    /// FUN_1418b2140 (@0x1418b2140): the tooltip waits while the action runs: always without a player or with the manager
    /// disabled; talk (0x14) and buy cube (0x16) while talking; 0x10 while dialog 0x4e56 is open; gathering (4), interact
    /// npc (0x11) and interact (0x12) in StateInteraction (0x19); fitting doll (0xe) while 0x4e77 is open; portal (0x13)
    /// while 0x4ebd is open; reactor (0x1a) in state 0x3d; 0x1c in state 0x3e.
    /// </summary>
    public bool Busy()
    {
        if (!HasPlayer()) return true;
        if (!Enabled()) return true;
        switch (CurrentOrNone())
        {
            case 0x14:
            case 0x16: return Talking();
            case 0x10: return DialogOpen(0x4e56);
            case 4:
            case 0x11:
            case 0x12: return OwnState() == 0x19;
            case 0xe: return DialogOpen(0x4e77);
            case 0x13: return DialogOpen(0x4ebd);
            case 0x1a: return OwnState() == 0x3d;
            case 0x1c: return OwnState() == 0x3e;
        }
        return false;
    }

    /// <summary>GetKeyHintText (@0x1418b1990): the key name of the hint's action at priority 2 with the gamepad, else 0.</summary>
    string KeyHintText()
    {
        int action = CurrentOrNone() == 0x1d ? ActionKey1dCode : ActionKeyCode;
        var keys = Keys?.Invoke() ?? Host?.Model?.Keys;
        return keys == null ? "" : keys.GetKeyName(keys.FindKeyByActionAndPriority(action, Gamepad() ? 2 : 0));
    }

    /// <summary>FUN_1418b1ab0 (@0x1418b1ab0): with the entity and a current action whose caption is not empty, keep the
    /// action, its title, caption and target (lifted 75), place the entity and show it.</summary>
    void ShowAction()
    {
        if (Host?.Entity == null) return;
        int cur = CurrentOrNone();
        if (cur == NoAction) return;
        string text = Caption();
        if (text == "") return;
        shownAction = cur;
        shownTitle = Title();
        shownText = text;
        target = Target();
        if (target is { } t) target = t with { Z = t.Z + TargetLift };   // the z of m_kTarget
        PlaceEntity();
        ShownKeyText = KeyHintText();
        ShownKind = Kind();
        Host.Invoke(Host.Entity, "show", ShownKeyText, text, ShownKind);
        // ChromaSDK::OnKeySkill: no keyboard lighting here
    }

    /// <summary>HideAction (@0x1418b1e60): forget the action and play hide.</summary>
    void HideAction()
    {
        if (shownAction < 0) return;
        shownAction = -1;
        if (Host?.Entity != null) Host.Invoke(Host.Entity, "hide");
        // ChromaSDK::ResetKeys: no keyboard lighting here
    }

    /// <summary>
    /// FUN_1418b1ef0 (@0x1418b1ef0): without a target at the stage's centre plus the two offsets (SetPositionInt);
    /// with one at its screen point, kept inside the stage less the entity's size (SetPositionFloat).
    /// </summary>
    void PlaceEntity()
    {
        if (Host?.Entity == null) return;
        double stageW = Host.Player.StageW, stageH = Host.Player.StageH;
        float x, y;
        if (target is not { } t)
        {
            x = (int)((float)stageW * 0.5f + OffsetX);
            y = (int)((float)stageH * 0.5f + OffsetY);
        }
        else
        {
            if (Project(t) is not { } p) return;
            float w = (float)Math.Ceiling(Rt.ToNumber(Host.Get(Host.Entity, "width")));
            float h = (float)Math.Ceiling(Rt.ToNumber(Host.Get(Host.Entity, "height")));
            x = ClampProxyValue(p.X, 0f, (float)((int)stageW - w));
            y = ClampProxyValue(p.Y, 0f, (float)((int)stageH - h));
        }
        if (Place == (x, y) && placed) return;
        placed = true;
        Place = (x, y);
        Host.Set(Host.Entity, "x", (double)x);
        Host.Set(Host.Entity, "y", (double)y);
    }

    /// <summary>ClampProxyValue (userproxy.cpp:45): below lo gives lo, above hi gives hi (lo wins when hi &lt; lo).</summary>
    static float ClampProxyValue(float x, float lo, float hi) => x < lo ? lo : hi < x ? hi : x;
}
