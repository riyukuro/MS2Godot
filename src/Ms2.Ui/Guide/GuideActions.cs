using System.Numerics;
using System.Xml.Linq;
using Ms2.Net.Packets;

namespace Ms2.Ui.Guide;

/// <summary>
/// A guide action (CGuideAction, client/ms2/guideaction.cpp): what an event does while it runs. vfunc_2
/// <see cref="Load"/>, vfunc_3 <see cref="Start"/> (the event opens), vfunc_4 <see cref="Stop"/> (it closes or is
/// cleared), vfunc_5 <see cref="IsDone"/> (the event waits in state 1 until every action is done).
/// </summary>
public abstract class GuideAction
{
    public virtual void Load(XElement e) { }
    public virtual void Start(GuideManager g) { }
    public virtual void Stop(GuideManager g) { }
    public virtual bool IsDone(GuideManager g) => true;

    static readonly Dictionary<string, Func<GuideAction>> Classes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ClearTutorial"] = () => new ClearTutorialAction(),
        ["ConditionAction"] = () => new ConditionActionAction(),
        ["Effect"] = () => new EffectAction(),
        ["EquipWeaponTutorial"] = () => new EquipWeaponTutorialAction(),
        ["ForceClear"] = () => new ForceClearAction(),
        ["GameMenuLimit"] = () => new GameMenuLimitAction(),
        ["Limit"] = () => new LimitAction(),
        ["LimitDialog"] = () => new LimitDialogAction(),
        ["MinimapMaximize"] = () => new MinimapMaximizeAction(),
        ["OpenDialog"] = () => new OpenDialogAction(),
        ["RequestTutorialItem"] = () => new RequestTutorialItemAction(),
        ["SequenceCamera"] = () => new SequenceCameraAction(),
        ["ServerTriggerEvent"] = () => new ServerTriggerEventAction(),
        ["SideNpcTalk"] = () => new SideNpcTalkAction(),
        ["SmartRecommend"] = () => new SmartRecommendAction(),
        ["UIArrow"] = () => new UiArrowAction(),
        ["UIArrowClose"] = () => new UiArrowCloseAction(),
        ["UIArrowDialog"] = () => new UiArrowDialogAction(),
        ["UISummary"] = () => new UiSummaryAction(),
        ["UITextBase"] = () => new UiTextAction(),
        ["UITextTip"] = () => new UiTextTipAction(),
        ["UITutorial"] = () => new UiTutorialAction(),
    };

    /// <summary>The registered class names ("CGuideAction{name}").</summary>
    public static IEnumerable<string> ClassNames => Classes.Keys;

    /// <summary>CreateGuideAction (@0x141382b90): "CGuideAction" + the type attribute through the class registry, loaded;
    /// an unknown type throws.</summary>
    public static GuideAction Create(XElement e)
    {
        string type = (string)e.Attribute("type") ?? "";
        if (!Classes.TryGetValue(type, out var make))
            throw new GuideLoadException($"Unknown Guide action Tag({type})");
        var a = make();
        a.Load(e);
        return a;
    }
}

public sealed class MinimapMaximizeAction : GuideAction
{
    public int IsOn;
    public override void Load(XElement e) => GuideXml.Int(e, "isOn", ref IsOn);
    public override void Start(GuideManager g) => g.World.SetMinimapMaximized(IsOn != 0);
}

/// <summary>OpenDialog: the dialog of an id, else the hidden dialog of a name, opens.</summary>
public sealed class OpenDialogAction : GuideAction
{
    public int DialogId;
    public string DialogName = "";
    public override void Load(XElement e)
    {
        DialogId = 0;
        GuideXml.Int(e, "dialogID", ref DialogId);
        GuideXml.String(e, "dialogName", ref DialogName);
    }
    public override void Start(GuideManager g)
    {
        if (DialogId != 0) g.World.OpenDialog(DialogId);
        else if (DialogName.Length != 0) g.World.OpenDialog(DialogName);
    }
}

/// <summary>Effect: an effect at each &lt;cube&gt; (pos, rotate in degrees, scale) while the event runs.</summary>
public sealed class EffectAction : GuideAction
{
    public sealed class Cube
    {
        public Vector3 Pos, Rotate;
        public float Scale = 1.0f;
        public int Handle;
    }
    public string Path = "";
    public List<Cube> Cubes = new();

    public override void Load(XElement e)
    {
        GuideXml.String(e, "path", ref Path);
        foreach (var c in e.Elements("cube"))
        {
            var cube = new Cube();
            GuideXml.Point3(c, "pos", ref cube.Pos);
            GuideXml.Point3(c, "rotate", ref cube.Rotate);
            GuideXml.Float(c, "scale", ref cube.Scale);
            Cubes.Add(cube);
        }
    }
    public override void Start(GuideManager g)
    {
        const float rad = 3.1415927f / 180.0f;
        foreach (var c in Cubes)
            c.Handle = g.World.PlayEffect(Path, c.Pos, new Vector3(c.Rotate.X * rad, c.Rotate.Y * rad, c.Rotate.Z * rad), c.Scale);
    }
    public override void Stop(GuideManager g)
    {
        foreach (var c in Cubes)
        {
            if (c.Handle == 0) continue;
            g.World.StopEffect(c.Handle);
            c.Handle = 0;
        }
    }
}

/// <summary>Limit: holds a <see cref="GuideLimit"/> while the event runs.</summary>
public sealed class LimitAction : GuideAction
{
    public GuideLimit Limit;
    public override void Load(XElement e)
    {
        Limit = GuideLimit.None;
        var s = (string)e.Attribute("limit");
        if (s != null) Limit = GuideNames.ParseLimit(s);
        if (Limit == GuideLimit.None) throw new GuideLoadException("limit Guide Unknown Type");
    }
    public override void Start(GuideManager g) => g.AddLimit(Limit);
    public override void Stop(GuideManager g) => g.RemoveLimit(Limit);
}

/// <summary>LimitDialog: the dialog type of a name cannot open while the event runs.</summary>
public sealed class LimitDialogAction : GuideAction
{
    public string DialogName = "";
    public override void Load(XElement e)
    {
        GuideXml.String(e, "dialogName", ref DialogName);
        if (DialogName.Length == 0) throw new GuideLoadException("limitDialog Name is Empty");
    }
    public override void Start(GuideManager g) => g.AddDialogLimit(DialogName);
    public override void Stop(GuideManager g) => g.RemoveDialogLimit(DialogName);
}

/// <summary>UITutorial: the guide pane's tutorial box steps through its texts; each step shows at least waitTick ms or
/// until the action key (totalAction). Holds PlayerControl and CubeKeyAll while it runs.</summary>
public sealed class UiTutorialAction : GuideAction
{
    public readonly record struct Step(int TextId, uint WaitTick);
    public int EntityId = 1;
    public List<Step> Steps = new();
    int index;
    uint tick;

    public override void Load(XElement e)
    {
        index = 0;
        EntityId = 1;
        GuideXml.Int(e, "entityId", ref EntityId);
        foreach (var t in e.Elements("text"))
        {
            int text = 0, wait = 0;
            GuideXml.UInt(t, "textId", ref text);
            GuideXml.UInt(t, "waitTick", ref wait);
            Steps.Add(new Step(text, (uint)wait));
        }
    }
    public override void Start(GuideManager g)
    {
        tick = 0;
        index = 0;
        if (Steps.Count == 0) return;
        g.World.Pane?.ShowTutorial(EntityId, Steps[index].TextId, true);
        g.AddLimit(GuideLimit.PlayerControl);
        g.AddLimit(GuideLimit.CubeKeyAll);
    }
    public override void Stop(GuideManager g)
    {
        g.RemoveLimit(GuideLimit.PlayerControl);
        g.RemoveLimit(GuideLimit.CubeKeyAll);
        g.World.Pane?.HideTutorial(EntityId);
    }
    public override bool IsDone(GuideManager g)
    {
        var pane = g.World.Pane;
        if (pane != null && pane.TutorialShowing) return false;
        if (index < Steps.Count) Advance(g);
        return index >= Steps.Count;
    }

    /// <summary>FUN_141383f50.</summary>
    void Advance(GuideManager g)
    {
        var w = g.World;
        if (tick == 0) tick = w.Now;
        if (w.Now - tick >= Steps[index].WaitTick || (!w.TextFocused && w.IsActionKeyDown(GuideNames.ActionTotal)))
        {
            index++;
            tick = 0;
            if (index < Steps.Count) w.Pane?.ShowTutorial(EntityId, Steps[index].TextId, false);
        }
    }
}

/// <summary>The controller a UI text action is for (Parse_14021ade0).</summary>
public enum GuideController { Any = 0, Keyboard = 1, GamePad = 2 }

/// <summary>UITextBase: entityId, textId, isNotClose and controllerType; the UI actions derive from it. Its own Start and
/// Stop are the base's (nothing).</summary>
public class UiTextAction : GuideAction
{
    public int EntityId, TextId, IsNotClose;
    public GuideController ControllerType;

    public override void Load(XElement e)
    {
        EntityId = 0; TextId = 0; IsNotClose = 0; ControllerType = GuideController.Any;
        GuideXml.Int(e, "entityId", ref EntityId);
        GuideXml.UInt(e, "textId", ref TextId);
        GuideXml.Int(e, "isNotClose", ref IsNotClose);
        var s = (string)e.Attribute("controllerType");
        if (s != null)
            ControllerType = s.Equals("Keyboard", StringComparison.OrdinalIgnoreCase) ? GuideController.Keyboard
                : s.Equals("GamePad", StringComparison.OrdinalIgnoreCase) ? GuideController.GamePad : GuideController.Any;
    }

    /// <summary>FUN_141384200: the action is for the controller in use (the gamepad state's first byte, 0 keyboard).</summary>
    protected bool ForController(GuideManager g) => ControllerType switch
    {
        GuideController.Keyboard => g.World.GamepadState == 0,
        GuideController.GamePad => g.World.GamepadState == 1,
        _ => true,
    };
}

/// <summary>UISummary: the guide pane's summary panel; done once the panel is gone.</summary>
public sealed class UiSummaryAction : UiTextAction
{
    public override void Load(XElement e)
    {
        base.Load(e);
        if (EntityId == 0) EntityId = 1;
    }
    public override void Start(GuideManager g)
    {
        if (ForController(g)) g.World.Pane?.ShowSummary(EntityId, TextId);
    }
    public override void Stop(GuideManager g)
    {
        if (!ForController(g) || IsNotClose != 0) return;
        g.World.Pane?.HideSummary(EntityId);
    }
    public override bool IsDone(GuideManager g)
    {
        var pane = g.World.Pane;
        return pane == null || !pane.SummaryShowing;
    }
}

/// <summary>UITextTip: queues a text tip on the guide pane; Stop closes it (unless isNotClose).</summary>
public sealed class UiTextTipAction : UiTextAction
{
    public override void Start(GuideManager g)
    {
        if (ForController(g)) g.World.Pane?.QueueOrShowTextTip(EntityId, TextId);
    }
    public override void Stop(GuideManager g)
    {
        if (!ForController(g) || IsNotClose != 0) return;
        g.World.Pane?.AdvanceTextTip(EntityId);
    }
}

/// <summary>UIArrow: an arrow at a world offset with a text.</summary>
public class UiArrowAction : UiTextAction
{
    public Vector3 Offset;
    public int Direction, Align = 1;

    public override void Load(XElement e)
    {
        base.Load(e);
        Offset = Vector3.Zero;
        Direction = 0;
        Align = 1;
        GuideXml.Point3(e, "offset", ref Offset);
        GuideXml.UInt(e, "direction", ref Direction);
        GuideXml.UInt(e, "align", ref Align);
        if (EntityId == 0) throw new GuideLoadException("CGuideActionGuideTextTip Id is Zero");
    }
    public override void Start(GuideManager g)
    {
        if (ForController(g)) g.World.Pane?.AddArrow(new GuideArrow(EntityId, Offset, Direction, Align, TextId));
    }
    public override void Stop(GuideManager g)
    {
        if (!ForController(g) || IsNotClose != 0) return;
        g.World.Pane?.ReleaseArrow(EntityId);
    }
}

/// <summary>UIArrowDialog: an arrow at a dialog's control (a dotted child path, a child index, or a custom position kind).</summary>
public sealed class UiArrowDialogAction : UiArrowAction
{
    public int DialogId, CustomPosValue, ChildIndex = -1;
    public string DialogName = "", CustomPosType = "";
    public List<string> FocusDialogNames = new(), ChildName = new();

    public override void Load(XElement e)
    {
        base.Load(e);
        DialogId = 0; CustomPosValue = 0; ChildIndex = -1;
        GuideXml.UInt(e, "dialogID", ref DialogId);
        GuideXml.String(e, "dialogName", ref DialogName);
        FocusDialogNames = GuideXml.StringList(e, "focusDialogName");
        GuideXml.String(e, "customPosType", ref CustomPosType);
        GuideXml.Int(e, "customPosValue", ref CustomPosValue);
        string child = "";
        GuideXml.String(e, "childName", ref child);
        ChildName = GuideXml.Tokens(child, ".");
        GuideXml.Int(e, "childIndex", ref ChildIndex);
    }
    public override void Start(GuideManager g)
    {
        if (!ForController(g)) return;
        g.World.Pane?.AddArrow(new GuideDialogArrow(EntityId, Offset, Direction, Align, TextId, DialogId, DialogName,
            ChildIndex, ChildName, FocusDialogNames, CustomPosType, CustomPosValue));
    }
}

/// <summary>UIArrowClose: releases an arrow of another event when the event opens (isAtOpen) or when it closes.</summary>
public sealed class UiArrowCloseAction : GuideAction
{
    public int EntityId, IsAtOpen;
    public override void Load(XElement e)
    {
        EntityId = 0; IsAtOpen = 0;
        GuideXml.UInt(e, "entityId", ref EntityId);
        GuideXml.Int(e, "isAtOpen", ref IsAtOpen);
    }
    public override void Start(GuideManager g)
    {
        if (IsAtOpen != 0) g.World.Pane?.ReleaseArrow(EntityId);
    }
    public override void Stop(GuideManager g)
    {
        if (IsAtOpen == 0) g.World.Pane?.ReleaseArrow(EntityId);
    }
}

/// <summary>ConditionAction: starts its actions when its conditions hold at the start. The client builds each nested
/// condition and action from the &lt;conditions&gt; / &lt;actions&gt; list element instead of the child it iterates (so the
/// list element's own "type" is read, once per child); that is kept. No table uses the action. Its Stop is the base's
/// (the nested actions are not stopped).</summary>
public sealed class ConditionActionAction : GuideAction
{
    public List<GuideCondition> Conditions = new();
    public List<GuideAction> Actions = new();
    bool started;

    public override void Load(XElement e)
    {
        if (e.Element("conditions") is { } conditions)
            foreach (var _ in conditions.Elements("condition"))
                Conditions.Add(GuideCondition.Create(conditions));
        if (e.Element("actions") is { } actions)
            foreach (var _ in actions.Elements("action"))
                Actions.Add(Create(actions));
    }
    public override void Start(GuideManager g)
    {
        started = false;
        foreach (var c in Conditions)
            if (c.Check(g) == 0) return;
        foreach (var a in Actions) a.Start(g);
        started = true;
    }
    public override bool IsDone(GuideManager g)
    {
        if (!started) return true;
        foreach (var a in Actions)
            if (!a.IsDone(g)) return false;
        return true;
    }
}

/// <summary>EquipWeaponTutorial: the inventory's align mode and the quick slots' open animation while the event runs.</summary>
public sealed class EquipWeaponTutorialAction : GuideAction
{
    public int IsUse;
    public override void Load(XElement e) { IsUse = 0; GuideXml.Int(e, "isUse", ref IsUse); }
    public override void Start(GuideManager g) => g.World.SetInventoryTutorial(IsUse != 0);
    public override void Stop(GuideManager g) => g.World.SetInventoryTutorial(false);
}

/// <summary>ForceClear: finishes another event (CGuideManager::FinishById).</summary>
public sealed class ForceClearAction : GuideAction
{
    public int Id;
    public override void Load(XElement e)
    {
        Id = 0;
        GuideXml.Int(e, "id", ref Id);
        if (Id == 0) throw new GuideLoadException("forceClear id is Zero");
    }
    public override void Start(GuideManager g) => g.FinishById(Id);
}

/// <summary>SequenceCamera: runs trigger camera steps, optionally as a skippable cinematic (sub scene 0x6200041); holds
/// PlayerControl and CubeKeyAll. Done when skipped or when the camera is back in mode 2.</summary>
public sealed class SequenceCameraAction : GuideAction
{
    public int IsUseCinematicCutScene = 1;
    public float EndInterpolationTime;
    public List<int> TriggerCameraId = new();
    bool skipped;

    public override void Load(XElement e)
    {
        IsUseCinematicCutScene = 1;
        EndInterpolationTime = 0;
        GuideXml.Int(e, "isUseCinematicCutScene", ref IsUseCinematicCutScene);
        GuideXml.Float(e, "endInterpolationTime", ref EndInterpolationTime);
        TriggerCameraId = GuideXml.IntList(e, "triggerCameraId");
    }
    public override void Start(GuideManager g)
    {
        var w = g.World;
        skipped = false;
        if (w.CameraMode != 2) w.CameraInterpolation(0);
        w.QueueCameraSteps(TriggerCameraId);
        if (IsUseCinematicCutScene != 0)
        {
            w.SetSubScene(0x6200041);
            w.StartCinematicSkip(() => skipped = true);
        }
        g.AddLimit(GuideLimit.PlayerControl);
        g.AddLimit(GuideLimit.CubeKeyAll);
    }
    public override void Stop(GuideManager g)
    {
        var w = g.World;
        if (w.CameraMode != 2) w.CameraInterpolation(EndInterpolationTime);
        if (IsUseCinematicCutScene != 0)
        {
            w.SetSubScene(0x620001d);
            w.StopCinematicSkip();
        }
        g.RemoveLimit(GuideLimit.PlayerControl);
        g.RemoveLimit(GuideLimit.CubeKeyAll);
    }
    public override bool IsDone(GuideManager g) => skipped || g.World.CameraMode == 2;
}

public sealed class SmartRecommendAction : GuideAction
{
    public int RecommendCode;
    public override void Load(XElement e) { RecommendCode = 0; GuideXml.UInt(e, "recommendCode", ref RecommendCode); }
    public override void Start(GuideManager g) => g.World.SmartRecommend(RecommendCode);
}

/// <summary>ServerTriggerEvent: tells the server's trigger script the event id.</summary>
public sealed class ServerTriggerEventAction : GuideAction
{
    public int EventId;
    public override void Load(XElement e) { EventId = 0; GuideXml.Int(e, "eventID", ref EventId); }
    public override void Start(GuideManager g) => g.World.Send(new GuideServerTriggerEvent(EventId));
}

public sealed class GameMenuLimitAction : GuideAction
{
    public int MenuId, Enabled;
    public override void Load(XElement e)
    {
        Enabled = 0;
        GuideXml.UInt(e, "menuID", ref MenuId);
        GuideXml.Int(e, "enabled", ref Enabled);
    }
    public override void Start(GuideManager g) => g.World.SetMenuEnable(MenuId, Enabled != 0);
}

/// <summary>RequestTutorialItem: asks the server for the starter items.</summary>
public sealed class RequestTutorialItemAction : GuideAction
{
    public override void Start(GuideManager g) => g.World.Send(new RequestTutorialItem());
}

/// <summary>SideNpcTalk: the side NPC talk popup (uiType talk/movie/cutin/talkbottom, Parse_14021abf0).</summary>
public sealed class SideNpcTalkAction : GuideAction
{
    public int UiType, ScriptId;
    public string Usm = "", Illust = "", Voice = "", Sound = "";
    public int Duration = 3000;

    public override void Load(XElement e)
    {
        UiType = 0;
        Duration = 3000;
        var s = (string)e.Attribute("uiType");
        if (s != null)
            UiType = s.ToLowerInvariant() switch { "movie" => 1, "cutin" => 2, "talkbottom" => 3, _ => 0 };
        GuideXml.String(e, "usm", ref Usm);
        GuideXml.UInt(e, "scriptID", ref ScriptId);
        GuideXml.String(e, "illust", ref Illust);
        GuideXml.UInt(e, "duration", ref Duration);
        GuideXml.String(e, "voice", ref Voice);
        GuideXml.String(e, "sound", ref Sound);
    }
    public override void Start(GuideManager g) =>
        g.World.ShowSideNpcTalk(new GuideSideTalk(UiType, Usm, new[] { ScriptId }, Illust, (uint)Duration, Voice, Sound));
}

/// <summary>ClearTutorial: tells the server the tutorial is done.</summary>
public sealed class ClearTutorialAction : GuideAction
{
    public override void Start(GuideManager g) => g.World.Send(new ClearTutorial());
}
