using System.Numerics;
using System.Xml.Linq;

namespace Ms2.Ui.Guide;

/// <summary>
/// A guide condition (CGuideCondition, client/ms2/guideentity_9.cpp @0x1413a54e0): one test of a guide event's open, close
/// or cancel list. The virtuals are the client's: vfunc_2 <see cref="Load"/>, vfunc_3 <see cref="Check"/>, vfunc_4
/// <see cref="Reset"/>, vfunc_6 <see cref="Prune"/>, vfunc_8 <see cref="SubKey"/> (vfunc_5 and vfunc_7 are the debug
/// test case and text, not ported). <see cref="Kind"/> (m_n18) is the occurrence the condition waits on
/// (<see cref="GuideOccurrence"/>); 0 is none.
/// </summary>
public abstract class GuideCondition
{
    /// <summary>m_n18: the occurrence the condition is indexed under (0 none).</summary>
    public int Kind { get; protected set; }
    /// <summary>m_b1c: the "not" attribute.</summary>
    public bool Not;
    /// <summary>m_n20: the "value" attribute.</summary>
    public int Value;

    /// <summary>vfunc_2 (@0x1413a5670): "value" and "not".</summary>
    public virtual void Load(XElement e)
    {
        GuideXml.Int(e, "value", ref Value);
        GuideXml.Bool(e, "not", ref Not);
    }

    /// <summary>vfunc_3: nonzero when the condition holds (@0x1413a56d0: 0).</summary>
    public virtual int Check(GuideManager g) => 0;

    /// <summary>vfunc_4 (@0x1413a56e0): clears what a test kept (a deadline, a start time); the base keeps nothing.</summary>
    public virtual void Reset() { }

    /// <summary>vfunc_6 (@0x1413a5740): false keeps the condition; AllOf moves its children into <paramref name="keep"/> and
    /// returns true (it drops itself).</summary>
    public virtual bool Prune(List<GuideCondition> keep) => false;

    /// <summary>vfunc_8 (@0x1413a5810): the value the occurrence index files the condition under (0 the whole kind).</summary>
    public virtual int SubKey(GuideManager g) => 0;

    /// <summary>ApplyNot (@0x1413a55a0).</summary>
    protected int ApplyNot(int r) => Not ? (r == 0 ? 1 : 0) : r;
    /// <summary>ApplyNotBool (@0x1413a5600).</summary>
    protected int ApplyNotBool(bool b) => Not ? (b ? 0 : 1) : (b ? 1 : 0);
    protected int ApplyNot(bool b) => ApplyNot(b ? 1 : 0);

    /// <summary>The days of a time span (ATL::CTimeSpan::GetDays: seconds / 86400, toward zero).</summary>
    protected static long Days(long seconds) => seconds / 86400;

    // ---- the factory ---------------------------------------------------------------------------------------------

    static readonly Dictionary<string, Func<GuideCondition>> Classes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AchieveExistTrophy"] = () => new AchieveExistTrophyCondition(),
        ["AchieveTab"] = () => new AchieveTabCondition(),
        ["ActionInput"] = () => new ActionInputCondition(),
        ["AllOf"] = () => new AllOfCondition(),
        ["AnyOne"] = () => new AnyOneCondition(),
        ["BattleState"] = () => new BattleStateCondition(),
        ["ButtonSelected"] = () => new ButtonSelectedCondition(),
        ["CanObserveMode"] = () => new CanObserveModeCondition(),
        ["ChatEmoticon"] = () => new ChatEmoticonCondition(),
        ["Constructable"] = () => new ConstructableCondition(),
        ["ConstructMode"] = () => new ConstructModeCondition(),
        ["ConstructSlot"] = () => new ConstructSlotCondition(),
        ["ContinentCode"] = () => new ContinentCodeCondition(),
        ["Contract"] = () => new ContractCondition(),
        ["CountInDoorInstallItem"] = () => new CountInDoorInstallItemCondition(),
        ["CountItem"] = () => new CountItemCondition(),
        ["CubeHolderSelectItem"] = () => new CubeHolderSelectItemCondition(),
        ["Dead"] = () => new DeadCondition(),
        ["DesignItem"] = () => new DesignItemCondition(),
        ["Dialog"] = () => new DialogCondition(),
        ["EnterDungeon"] = () => new EnterDungeonCondition(),
        ["EquipItem"] = () => new EquipItemCondition(),
        ["EquipSkill"] = () => new EquipSkillCondition(),
        ["ExistItem"] = () => new ExistItemCondition(),
        ["ExistShortcut"] = () => new ExistShortcutCondition(),
        ["False"] = () => new FalseCondition(),
        ["FieldId"] = () => new FieldIdCondition(),
        ["GameScene"] = () => new GameSceneCondition(),
        ["GuideClear"] = () => new GuideClearCondition(),
        ["GuideOpen"] = () => new GuideOpenCondition(),
        ["GuildPvpPeriod"] = () => new GuildPvpPeriodCondition(),
        ["HasManufacture"] = () => new HasManufactureCondition(),
        ["HasMoney"] = () => new HasMoneyCondition(),
        ["HasStatPoint"] = () => new HasStatPointCondition(),
        ["HomeMode"] = () => new HomeModeCondition(),
        ["Input"] = () => new InputCondition(),
        ["Interactable"] = () => new InteractableCondition(),
        ["InteractObject"] = () => new InteractObjectCondition(),
        ["InventoryTab"] = () => new InventoryTabCondition(),
        ["IsMyHome"] = () => new IsMyHomeCondition(),
        ["JobCode"] = () => new JobCodeCondition(),
        ["JoinGuild"] = () => new JoinGuildCondition(),
        ["LastClosedTextTip"] = () => new LastClosedTextTipCondition(),
        ["MasteryMainTabIndex"] = () => new MasteryMainTabIndexCondition(),
        ["MasterySubTabIndex"] = () => new MasterySubTabIndexCondition(),
        ["MentoringPage"] = () => new MentoringPageCondition(),
        ["MenuExpand"] = () => new MenuExpandCondition(),
        ["Money"] = () => new MoneyCondition(),
        ["Movement"] = () => new MovementCondition(),
        ["MyPlayerLevel"] = () => new MyPlayerLevelCondition(),
        ["Next"] = () => new NextCondition(),
        ["PlaceUgcCube"] = () => new PlaceUgcCubeCondition(),
        ["PlaceUgcCubeByIconType"] = () => new PlaceUgcCubeByIconTypeCondition(),
        ["PlaceUgcCubeItem"] = () => new PlaceUgcCubeItemCondition(),
        ["PlayingInstrument"] = () => new PlayingInstrumentCondition(),
        ["PlayInstrumentHasSelectItem"] = () => new PlayInstrumentHasSelectItemCondition(),
        ["PlayInstrumentTabIndex"] = () => new PlayInstrumentTabIndexCondition(),
        ["PortalEnable"] = () => new PortalEnableCondition(),
        ["Position"] = () => new PositionCondition(),
        ["QuestComplete"] = () => new QuestCompleteCondition(),
        ["QuestProgress"] = () => new QuestProgressCondition(),
        ["QuestSatisfied"] = () => new QuestSatisfiedCondition(),
        ["RealEstateExpireDays"] = () => new RealEstateExpireDaysCondition(),
        ["ReturnPortal"] = () => new ReturnPortalCondition(),
        ["SelectItemEnchant"] = () => new SelectItemEnchantCondition(),
        ["ServerTime"] = () => new ServerTimeCondition(),
        ["State"] = () => new StateCondition(),
        ["SubJobCode"] = () => new SubJobCodeCondition(),
        ["SummonPet"] = () => new SummonPetCondition(),
        ["Timeout"] = () => new TimeoutCondition(),
        ["Title"] = () => new TitleCondition(),
        ["Trigger"] = () => new TriggerCondition(),
        ["UgcCollectMoney"] = () => new UgcCollectMoneyCondition(),
        ["UgcManagementTab"] = () => new UgcManagementTabCondition(),
        ["UgcProductExpireDays"] = () => new UgcProductExpireDaysCondition(),
    };

    /// <summary>The registered class names (the ClassObjectRegistrar entries "CGuideCondition{name}").</summary>
    public static IEnumerable<string> ClassNames => Classes.Keys;

    /// <summary>FUN_1413a52c0: the condition of a &lt;condition&gt; element (FUN_1413a5420 creates "CGuideCondition" + its
    /// type through the class registry, which matches names case-insensitively), loaded. An unknown type throws.</summary>
    public static GuideCondition Create(XElement e)
    {
        string type = (string)e.Attribute("type") ?? "";
        if (!Classes.TryGetValue(type, out var make))
            throw new GuideLoadException($"Unknown Guide Condition Tag({type})");
        var c = make();
        c.Load(e);
        return c;
    }

    /// <summary>The &lt;condition&gt; children of an element, as the list loaders read them.</summary>
    protected static List<GuideCondition> LoadChildren(XElement e)
    {
        var list = new List<GuideCondition>();
        foreach (var c in e.Elements("condition"))
        {
            var cond = Create(c);
            if (cond != null) list.Add(cond);
        }
        return list;
    }
}

/// <summary>A guide load error (the std::exception the loaders throw; LoadFile logs it per event).</summary>
public sealed class GuideLoadException(string message) : Exception(message);

// ---- the composites (unnamed/unit_1413a9df0.cpp) -------------------------------------------------------------------------

/// <summary>AllOf: every child holds. Prune lifts its children into the owning list (@0x1413aa440).</summary>
public sealed class AllOfCondition : GuideCondition
{
    public List<GuideCondition> Children = new();
    public override void Load(XElement e) { base.Load(e); Children = LoadChildren(e); }
    public override int Check(GuideManager g)
    {
        foreach (var c in Children)
            if (c.Check(g) == 0) return 0;
        return 1;
    }
    public override bool Prune(List<GuideCondition> keep)
    {
        foreach (var c in Children)
            if (!c.Prune(keep)) keep.Add(c);
        return true;
    }
}

/// <summary>AnyOne: some child holds; Reset resets the children (@0x1413aa250).</summary>
public sealed class AnyOneCondition : GuideCondition
{
    public List<GuideCondition> Children = new();
    public override void Load(XElement e) { base.Load(e); Children = LoadChildren(e); }
    public override int Check(GuideManager g)
    {
        foreach (var c in Children)
            if (c.Check(g) != 0) return 1;
        return 0;
    }
    public override void Reset()
    {
        base.Reset();
        foreach (var c in Children) c.Reset();
    }
}

public sealed class FalseCondition : GuideCondition { }

// ---- the guide itself ------------------------------------------------------------------------------------------------------

/// <summary>Trigger (guideentity_9.cpp @0x1413b2…): a trigger of this update, by value / sub value or a value range.</summary>
public sealed class TriggerCondition : GuideCondition
{
    public GuideTrigger Trigger;
    public int? TriggerValue, SubValue;
    public GuideRange Range;

    public override void Load(XElement e)
    {
        base.Load(e);
        var s = (string)e.Attribute("trigger");
        if (s != null) Trigger = GuideNames.ParseTrigger(s);       // FUN_1413b1fc0
        TriggerValue = GuideXml.OptInt(e, "value");
        SubValue = GuideXml.OptInt(e, "subValue");
        if (e.Attribute("range") != null) GuideXml.Range(e, "range", ref Range);
        if (Trigger == GuideTrigger.None) throw new GuideLoadException("Unknown Guide Trigger Tag");
    }

    public override int Check(GuideManager g) => Range.IsSet
        ? ApplyNot(g.CheckTriggerRange(Trigger, Range))
        : ApplyNot(g.CheckTrigger(Trigger, TriggerValue, SubValue));
}

/// <summary>Timeout: <see cref="GuideCondition.Value"/> ms after the first test.</summary>
public sealed class TimeoutCondition : GuideCondition
{
    uint deadline;
    public override int Check(GuideManager g)
    {
        if (deadline == 0) deadline = g.World.Now + (uint)Value;
        return ApplyNotBool(g.World.Now >= deadline);
    }
    public override void Reset() => deadline = 0;
}

/// <summary>Next (unit_1413a7bc0.cpp): <see cref="GuideCondition.Value"/> ms after the first test, or the action key
/// (totalAction) while no text field has focus.</summary>
public sealed class NextCondition : GuideCondition
{
    uint start;
    public override int Check(GuideManager g)
    {
        var w = g.World;
        if (start == 0) start = w.Now;
        if (w.Now - start >= (uint)Value) return 1;
        if (w.TextFocused) return 0;
        return w.IsActionKeyDown(GuideNames.ActionTotal) ? 1 : 0;
    }
    public override void Reset() => start = 0;
}

public sealed class GuideClearCondition : GuideCondition
{
    public int GuideId;
    public override void Load(XElement e) { base.Load(e); Kind = (int)GuideOccurrence.GuideClear; GuideId = 0; GuideXml.UInt(e, "id", ref GuideId); }
    public override int Check(GuideManager g) => ApplyNotBool(g.IsCleared(GuideId));
    public override int SubKey(GuideManager g) => Not ? 0 : GuideId;
}

/// <summary>GuideOpen: the event of an id runs (its state is not 0).</summary>
public sealed class GuideOpenCondition : GuideCondition
{
    public int GuideId;
    public override void Load(XElement e) { base.Load(e); Kind = (int)GuideOccurrence.GuideOpen; GuideId = 0; GuideXml.UInt(e, "id", ref GuideId); }
    public override int Check(GuideManager g)
    {
        var entity = g.FindEntity(GuideId);
        return entity != null ? ApplyNotBool(entity.State != 0) : 0;
    }
}

/// <summary>LastClosedTextTip: the last text tip the guide event pane closed was this event's.</summary>
public sealed class LastClosedTextTipCondition : GuideCondition
{
    public int EntityId;
    public override void Load(XElement e)
    {
        base.Load(e);
        EntityId = 0;
        GuideXml.Int(e, "entityId", ref EntityId);
        if (EntityId == 0) throw new GuideLoadException("lastClosedToolTip entityId == 0");
    }
    public override int Check(GuideManager g)
    {
        var pane = g.World.Pane;
        return pane != null ? ApplyNotBool(EntityId == pane.LastClosedTip) : 0;
    }
}

// ---- the field and the scene -----------------------------------------------------------------------------------------------

/// <summary>FieldId (unit_1413a9a50.cpp): the field is <see cref="GuideCondition.Value"/>, or in [rangeBeg, rangeEnd].
/// During a field move the field reads as 0.</summary>
public sealed class FieldIdCondition : GuideCondition
{
    public int RangeBeg, RangeEnd;
    public override void Load(XElement e)
    {
        base.Load(e);
        Kind = (int)GuideOccurrence.FieldId;
        RangeBeg = 0; RangeEnd = 0;
        GuideXml.Int(e, "rangeBeg", ref RangeBeg);
        GuideXml.Int(e, "rangeEnd", ref RangeEnd);
        if (Value == 0 && RangeBeg == 0 && RangeEnd == 0) throw new GuideLoadException("Invalid FieldId Data");
    }
    public override int Check(GuideManager g)
    {
        uint field = (uint)g.World.FieldId;
        if (g.World.Migrating) field = 0;
        if (RangeBeg != 0 || RangeEnd != 0)
            return ApplyNotBool(field >= (uint)RangeBeg && field <= (uint)RangeEnd);
        return ApplyNotBool((uint)Value == field);
    }
    public override int SubKey(GuideManager g) => !Not && Value != 0 && RangeBeg == 0 && RangeEnd == 0 ? Value : 0;
}

public sealed class ContinentCodeCondition : GuideCondition
{
    public override int Check(GuideManager g)
    {
        var code = g.World.ContinentCode;
        return code == null ? 0 : ApplyNotBool(Value == code.Value);
    }
}

/// <summary>GameScene: the sub scene is 0x620001d (no "not").</summary>
public sealed class GameSceneCondition : GuideCondition
{
    public override int Check(GuideManager g) => g.World.SubSceneId == 0x620001d ? 1 : 0;
}

public sealed class EnterDungeonCondition : GuideCondition
{
    public override void Load(XElement e) { base.Load(e); Kind = (int)GuideOccurrence.EnterDungeon; }
    public override int Check(GuideManager g)
    {
        int state = g.World.DungeonEnterState(Value);
        return ApplyNotBool(state == 0 || state == 1);
    }
}

/// <summary>Position: the player (or the active cube craft's cell under a construction guide) is within a cube's range
/// (default 75).</summary>
public sealed class PositionCondition : GuideCondition
{
    public readonly record struct Cube(Vector3 Pos, float Range);
    public List<Cube> Cubes = new();
    public override void Load(XElement e)
    {
        base.Load(e);
        foreach (var c in e.Elements("cube"))
        {
            float range = 75.0f;
            GuideXml.Float(c, "range", ref range);
            var pos = GuideXml.Floats((string)c.Attribute("pos") ?? "", ",");
            if (pos.Count == 3) Cubes.Add(new Cube(new Vector3(pos[0], pos[1], pos[2]), range));
        }
    }
    public override int Check(GuideManager g)
    {
        var w = g.World;
        if (!w.HasPlayer) return 0;
        var pos = w.ConstructionCell ?? w.PlayerPosition;
        foreach (var c in Cubes)
            if ((pos - c.Pos).Length() < c.Range) return ApplyNot(1);
        return ApplyNot(0);
    }
}

/// <summary>Movement (unit_1413a76f0.cpp): the player moved <see cref="GuideCondition.Value"/> units since the reset.</summary>
public sealed class MovementCondition : GuideCondition
{
    Vector3 lastPos;
    float moved;
    public override int Check(GuideManager g)
    {
        var w = g.World;
        if (!w.HasPlayer) return 0;
        var pos = w.PlayerPosition;
        if (lastPos == Vector3.Zero) lastPos = pos;
        moved += (lastPos - pos).Length();
        lastPos = pos;
        return ApplyNotBool(Value <= moved);
    }
    public override void Reset() { lastPos = Vector3.Zero; moved = 0; }
}

public sealed class InteractObjectCondition : GuideCondition
{
    public int InteractId, State;
    public float Distance;
    public override void Load(XElement e)
    {
        base.Load(e);
        GuideXml.Int(e, "interactId", ref InteractId);
        GuideXml.Float(e, "distance", ref Distance);
        GuideXml.Int(e, "state", ref State);
    }
    public override int Check(GuideManager g)
    {
        if (!g.World.HasPlayer) return 0;
        var o = g.World.InteractObject(InteractId);
        if (o == null) return 0;
        if (o.Value.Distance > Distance) return ApplyNot(0);
        if (o.Value.State != State) return ApplyNot(0);
        return ApplyNot(1);
    }
}

/// <summary>Interactable (unit_1413a6e30.cpp): the nearest interact object can be used.</summary>
public sealed class InteractableCondition : GuideCondition
{
    public override int Check(GuideManager g)
    {
        if (!g.World.HasPlayer) return 0;
        var usable = g.World.NearestInteractableUsable;
        return usable == null ? 0 : ApplyNot(usable.Value);
    }
}

public sealed class PortalEnableCondition : GuideCondition
{
    public override int Check(GuideManager g) => g.World.HasPlayer ? ApplyNotBool(g.World.PortalNearby) : 0;
}

/// <summary>ReturnPortal: FUN_141bc2f40 as is (no "not").</summary>
public sealed class ReturnPortalCondition : GuideCondition
{
    public override int Check(GuideManager g) => g.World.ReturnPortal ? 1 : 0;
}

/// <summary>ServerTime: the server time is at or past dateTime. STILL-OPEN: FUN_140718950, the text-to-CMsTime parse
/// XmlGetAttr(CMsTime) uses, is not ported (no table event uses the condition), so the time stays 0 and the condition
/// holds unless "not".</summary>
public sealed class ServerTimeCondition : GuideCondition
{
    public string DateTime = "";
    public long Time;
    public override void Load(XElement e) { base.Load(e); GuideXml.String(e, "dateTime", ref DateTime); }
    public override int Check(GuideManager g) => ApplyNotBool(Time <= g.World.ServerTime);
}

// ---- input -------------------------------------------------------------------------------------------------------------------

/// <summary>Input (unit_1413a6580.cpp): the key <see cref="GuideCondition.Value"/> is held (for tick ms when tick is set)
/// while no text field has focus; never in sub scene 0x6200031.</summary>
public sealed class InputCondition : GuideCondition
{
    public int Tick;
    int deadline;
    public override void Load(XElement e) { base.Load(e); GuideXml.Int(e, "tick", ref Tick); }
    public override int Check(GuideManager g)
    {
        var w = g.World;
        if (w.SubSceneId == 0x6200031) return 0;
        bool held = w.IsKeyDown(Value) && !w.TextFocused;
        if (Tick == 0) return ApplyNot(held);
        if (!held) { deadline = 0; return ApplyNot(0); }
        if (deadline == 0) deadline = (int)(w.Now + (uint)Tick);
        return ApplyNotBool(w.Now >= (uint)deadline);
    }
    public override void Reset() => deadline = 0;
}

/// <summary>ActionInput: a key bound to the action <see cref="GuideCondition.Value"/> (priorities 0-2, or its pair key) is
/// down.</summary>
public sealed class ActionInputCondition : GuideCondition
{
    public override int Check(GuideManager g)
    {
        var w = g.World;
        for (int p = 0; p < 3; p++)
        {
            int key = w.FindKeyByActionAndPriority(Value, p);
            if (key == 0) continue;
            if (w.IsKeyDown(key)) return ApplyNot(1);
            if (w.GetPairKey(key, out int pair) && w.IsKeyDown(pair)) return ApplyNot(1);
        }
        return ApplyNot(0);
    }
}

// ---- dialogs -----------------------------------------------------------------------------------------------------------------

/// <summary>Dialog (unit_1413a6010.cpp): a dialog (by id, else by name) is open or closed as isOpen says, the listed
/// controls are visible and, when subIndex ≥ 0, its sub index matches. Indexed by the interned dialog name while isOpen.
/// The constructor (CGuideConditionDialog::CGuideConditionDialog @0x1413a60c0) sets isOpen 1 and subIndex -1: a condition
/// without those attributes asks for an open dialog of any sub index.</summary>
public sealed class DialogCondition : GuideCondition
{
    public string DialogName = "";
    public int DialogId, IsOpen = 1, SubIndex = -1;
    public List<string> Controls = new();
    public override void Load(XElement e)
    {
        base.Load(e);
        Kind = (int)GuideOccurrence.Dialog;
        if (Not) throw new GuideLoadException("Dialog is Not Allow Keword 'not'");
        GuideXml.String(e, "dialogName", ref DialogName);
        GuideXml.Int(e, "dialogID", ref DialogId);
        GuideXml.Int(e, "isOpen", ref IsOpen);
        GuideXml.Int(e, "subIndex", ref SubIndex);
        Controls = GuideXml.StringList(e, "control");
    }
    public override int Check(GuideManager g)
    {
        var d = DialogId != 0 ? g.World.FindDialog(DialogId) : null;
        d ??= g.World.FindDialog(DialogName);
        if (d == null) return 0;
        if ((d.Hidden ? 0 : 1) != IsOpen) return 0;
        foreach (var name in Controls)
        {
            var child = d.Child(name);
            if (child == null || !child.Visible) return 0;
        }
        if (SubIndex >= 0 && d.SubIndex != SubIndex) return 0;
        return 1;
    }
    public override int SubKey(GuideManager g) => IsOpen == 0 ? 0 : g.InternOccurrence(DialogName);
}

/// <summary>ButtonSelected: the button at childName's dotted path in a dialog's movie is visible and selected (each node
/// on the way visible).</summary>
public sealed class ButtonSelectedCondition : GuideCondition
{
    public string DialogName = "";
    public List<string> Children = new();
    public override void Load(XElement e)
    {
        base.Load(e);
        if (Not) throw new GuideLoadException("ButtonSelected is Not Allow Keword 'not'");
        GuideXml.String(e, "dialogName", ref DialogName);
        string child = "";
        GuideXml.String(e, "childName", ref child);
        Children = GuideXml.Tokens(child, ".");
    }
    public override int Check(GuideManager g)
    {
        var d = g.World.FindDialog(DialogName);
        if (d == null || d.Hidden) return 0;
        if (Children.Count == 0) return 0;
        var node = d.Root;
        if (node == null) return 0;
        for (int i = 0; i < Children.Count; i++)
        {
            node = node.Child(Children[i]);
            if (node == null || !node.Visible) return 0;
            if (i == Children.Count - 1) return node.Selected ? 1 : 0;
        }
        return 0;
    }
}

/// <summary>Contract: the house contract ends within 7 days while a dialog's control shows (no "not").</summary>
public sealed class ContractCondition : GuideCondition
{
    public int DialogId;
    public string ChildName = "", DialogName = "";
    public override void Load(XElement e)
    {
        base.Load(e);
        GuideXml.Int(e, "dialogID", ref DialogId);
        GuideXml.String(e, "childName", ref ChildName);
        GuideXml.String(e, "dialogName", ref DialogName);
    }
    public override int Check(GuideManager g)
    {
        var w = g.World;
        if (!w.HasPlayer) return 0;
        var expire = w.UgcMapExpire;
        if (expire == null) return 0;
        var d = DialogId != 0 ? w.FindDialog(DialogId) : null;
        d ??= w.FindDialog(DialogName);
        if (d == null) return 0;
        var child = d.Child(ChildName);
        if (child == null || !child.Visible) return 0;
        return Days(expire.Value - w.ServerTime) <= 7 ? 1 : 0;
    }
}

public sealed class AchieveTabCondition : GuideCondition
{
    public override int Check(GuideManager g) => g.World.AchieveTab is int tab ? ApplyNotBool(Value == tab) : 0;
}

public sealed class InventoryTabCondition : GuideCondition
{
    public override int Check(GuideManager g) => g.World.InventoryTab is int tab ? ApplyNotBool(Value == tab) : 0;
}

public sealed class CanObserveModeCondition : GuideCondition
{
    public override int Check(GuideManager g) => g.World.CanObserveMode is bool b ? ApplyNot(b) : 0;
}

public sealed class ChatEmoticonCondition : GuideCondition
{
    public override void Load(XElement e) { base.Load(e); Kind = (int)GuideOccurrence.ChatEmoticon; }
    public override int Check(GuideManager g) => g.World.ChatEmoticonFlag is bool b ? ApplyNotBool(b) : 0;
}

public sealed class MasteryMainTabIndexCondition : GuideCondition
{
    public override int Check(GuideManager g) => g.World.MasteryMainTab is int tab ? ApplyNotBool(tab == Value) : 0;
}

public sealed class MasterySubTabIndexCondition : GuideCondition
{
    public override int Check(GuideManager g) => g.World.MasterySubTab is int tab ? ApplyNotBool(tab == Value) : 0;
}

/// <summary>MentoringPage: the mentoring dialog is open on a page of this name. Its Load does not call the base, so
/// "value" and "not" are not read (no "not").</summary>
public sealed class MentoringPageCondition : GuideCondition
{
    public string PageName = "";
    public override void Load(XElement e) => GuideXml.String(e, "pageName", ref PageName);
    public override int Check(GuideManager g) => g.World.MentoringPageShown(PageName) ? 1 : 0;
}

public sealed class MenuExpandCondition : GuideCondition
{
    public string MenuName = "";
    public override void Load(XElement e) { base.Load(e); GuideXml.String(e, "menuName", ref MenuName); }
    public override int Check(GuideManager g) => g.World.MenuExpanded(MenuName) is bool b ? ApplyNot(b) : 0;
}

public sealed class PlayInstrumentHasSelectItemCondition : GuideCondition
{
    public override int Check(GuideManager g) => g.World.InstrumentHasScore is bool b ? ApplyNotBool(b) : 0;
}

public sealed class PlayInstrumentTabIndexCondition : GuideCondition
{
    public override int Check(GuideManager g) => g.World.InstrumentTab is int tab ? ApplyNotBool(tab == Value) : 0;
}

public sealed class SelectItemEnchantCondition : GuideCondition
{
    public override int Check(GuideManager g) => g.World.EnchantHasItem is bool b ? ApplyNotBool(b) : 0;
}

/// <summary>UgcManagementTab: the UGC management dialog (0x4ec5) shows.</summary>
public sealed class UgcManagementTabCondition : GuideCondition
{
    public override int Check(GuideManager g) => g.World.UgcManagementShown ? ApplyNot(1) : 0;
}

public sealed class CubeHolderSelectItemCondition : GuideCondition
{
    public override int Check(GuideManager g) => g.World.CubePaletteSelection is int id ? ApplyNotBool(id == Value) : 0;
}

// ---- the player --------------------------------------------------------------------------------------------------------------

public sealed class BattleStateCondition : GuideCondition
{
    public override void Load(XElement e) { base.Load(e); Kind = (int)GuideOccurrence.BattleState; }
    public override int Check(GuideManager g) => g.World.HasPlayer ? ApplyNot(g.World.InBattle) : 0;
}

public sealed class DeadCondition : GuideCondition
{
    public override void Load(XElement e) { base.Load(e); Kind = (int)GuideOccurrence.Dead; }
    public override int Check(GuideManager g) => g.World.HasPlayer ? ApplyNot(g.World.Dead) : 0;
}

public sealed class ConstructModeCondition : GuideCondition
{
    public override int Check(GuideManager g) => g.World.HasPlayer ? ApplyNot(g.World.CubeMode) : 0;
}

public sealed class ConstructableCondition : GuideCondition
{
    public override int Check(GuideManager g) => g.World.HasPlayer ? ApplyNot(g.World.Constructable) : 0;
}

/// <summary>ConstructSlot: always holds (its fields are only read by the debug text).</summary>
public sealed class ConstructSlotCondition : GuideCondition
{
    public int SlotIdx;
    public bool Indoor, Outdoor;
    public override void Load(XElement e)
    {
        base.Load(e);
        SlotIdx = 0; Indoor = false; Outdoor = false;
        GuideXml.Int(e, "slotIdx", ref SlotIdx);
        GuideXml.Bool(e, "isIndoor", ref Indoor);
        GuideXml.Bool(e, "isOutdoor", ref Outdoor);
    }
    public override int Check(GuideManager g) => 1;
}

public sealed class JobCodeCondition : GuideCondition
{
    public override void Load(XElement e) { base.Load(e); Kind = (int)GuideOccurrence.JobCode; }
    public override int Check(GuideManager g) => g.World.HasPlayer ? ApplyNotBool(g.World.JobGroup == Value) : 0;
}

public sealed class SubJobCodeCondition : GuideCondition
{
    public List<int> SubJobCodes = new();
    public override void Load(XElement e)
    {
        base.Load(e);
        var s = (string)e.Attribute("subJobCodes");
        if (s != null) SubJobCodes = GuideXml.Ints(s);
    }
    public override int Check(GuideManager g)
    {
        if (!g.World.HasPlayer) return 0;
        foreach (int code in SubJobCodes)
            if (g.World.SubJob == code) return ApplyNot(1);
        return ApplyNot(0);
    }
}

public sealed class MyPlayerLevelCondition : GuideCondition
{
    public override void Load(XElement e) { base.Load(e); Kind = (int)GuideOccurrence.MyPlayerLevel; }
    public override int Check(GuideManager g) => g.World.HasPlayer ? ApplyNotBool(g.World.Level >= Value) : 0;
}

/// <summary>State: the player's state id is <see cref="GuideCondition.Value"/>.</summary>
public sealed class StateCondition : GuideCondition
{
    public override int Check(GuideManager g) => g.World.HasPlayer ? ApplyNot(g.World.PlayerState == Value) : 0;
}

public sealed class PlayingInstrumentCondition : GuideCondition
{
    public override int Check(GuideManager g)
    {
        if (!g.World.HasPlayer) return 0;
        if (g.World.PlayerState != 0x2f) return ApplyNot(0);
        return ApplyNotBool(g.World.InstrumentMotion == 0x7f);
    }
}

public sealed class SummonPetCondition : GuideCondition
{
    public override int Check(GuideManager g) => g.World.HasPlayer ? ApplyNotBool(g.World.PetSummoned) : 0;
}

public sealed class TitleCondition : GuideCondition
{
    public override int Check(GuideManager g) => g.World.HasPlayer ? ApplyNotBool(g.World.TitleCount > 0) : 0;
}

public sealed class HasStatPointCondition : GuideCondition
{
    public override int Check(GuideManager g) => g.World.StatPointsLeft is int n ? ApplyNotBool((uint)n > 0) : 0;
}

/// <summary>EquipSkill: whether a quick slot holds the skill <see cref="GuideCondition.Value"/> is isEquip.</summary>
public sealed class EquipSkillCondition : GuideCondition
{
    public int IsEquip;
    public override void Load(XElement e) { base.Load(e); GuideXml.Int(e, "isEquip", ref IsEquip); }
    public override int Check(GuideManager g) =>
        ApplyNotBool((g.World.FindQuickSlotBySkill(Value) != -1 ? 1u : 0u) == (uint)IsEquip);
}

public sealed class ExistShortcutCondition : GuideCondition
{
    public int SlotIndex, SkillId;
    public override void Load(XElement e)
    {
        base.Load(e);
        SlotIndex = 0; SkillId = 0;
        GuideXml.UInt(e, "slotIndex", ref SlotIndex);
        GuideXml.UInt(e, "skillID", ref SkillId);
    }
    public override int Check(GuideManager g) => ApplyNotBool(g.World.QuickSlotSkill(SlotIndex) == SkillId);
}

// ---- items -------------------------------------------------------------------------------------------------------------------

public sealed class CountItemCondition : GuideCondition
{
    public int ItemType, Min, Max;
    public override void Load(XElement e)
    {
        base.Load(e);
        Kind = (int)GuideOccurrence.CountItem;
        ItemType = 0; Min = -1; Max = -1;
        GuideXml.Int(e, "itemType", ref ItemType);
        GuideXml.Int(e, "minValue", ref Min);
        GuideXml.Int(e, "maxValue", ref Max);
    }
    public override int Check(GuideManager g)
    {
        if (!g.World.HasPlayer) return 0;
        int n = g.World.CountItemsOfType(ItemType);
        if (Min != -1 && Min > n) return ApplyNot(0);
        if (Max != -1 && Max < n) return ApplyNot(0);
        return ApplyNot(1);
    }
}

/// <summary>CountInDoorInstallItem: no Load of its own, so its min and max are the ctor's (STILL-OPEN: the ctor's values
/// are not read; -1 both, as CountItem's Load sets them, is assumed until then).</summary>
public sealed class CountInDoorInstallItemCondition : GuideCondition
{
    public int Min = -1, Max = -1;
    public override int Check(GuideManager g)
    {
        if (!g.World.HasPlayer) return 0;
        int n = g.World.CountIndoorInstallItems;
        if (Min != -1 && Min > n) return ApplyNot(0);
        if (Max != -1 && Max < n) return ApplyNot(0);
        return ApplyNot(1);
    }
}

public sealed class DesignItemCondition : GuideCondition
{
    public int ItemType;
    public override void Load(XElement e)
    {
        base.Load(e);
        ItemType = 0;
        Kind = (int)GuideOccurrence.DesignItem;
        GuideXml.Int(e, "itemType", ref ItemType);
    }
    public override int Check(GuideManager g) => ApplyNot(g.World.HasDesignItem(ItemType));
}

public sealed class EquipItemCondition : GuideCondition
{
    public int SlotType, ItemId, RangeBegin, RangeLast;
    public string SlotName = "";
    public override void Load(XElement e)
    {
        base.Load(e);
        SlotType = 0; ItemId = 0; RangeBegin = 0; RangeLast = 0;
        GuideXml.UInt(e, "slotType", ref SlotType);
        GuideXml.String(e, "slotName", ref SlotName);
        GuideXml.UInt(e, "itemID", ref ItemId);
        GuideXml.UInt(e, "itemRangeBeginID", ref RangeBegin);
        GuideXml.UInt(e, "itemRangeLastID", ref RangeLast);
    }
    public override int Check(GuideManager g)
    {
        if (!g.World.HasPlayer) return 0;
        var slot = g.World.Equip(SlotName, SlotType);
        if (slot == null) return ApplyNot(0);
        int id = slot.Value.ItemId;
        if (ItemId != 0 && id != ItemId) return ApplyNot(0);
        if (!slot.Value.HasValidItem) return ApplyNot(0);
        if (RangeBegin != 0 && (uint)id < (uint)RangeBegin) return ApplyNot(0);
        if (RangeLast != 0 && (uint)id > (uint)RangeLast) return ApplyNot(0);
        return ApplyNot(1);
    }
}

public sealed class ExistItemCondition : GuideCondition
{
    public int ItemId, ItemType, IconType;
    public override void Load(XElement e)
    {
        base.Load(e);
        ItemId = 0; ItemType = 0; IconType = 0;
        GuideXml.Int(e, "itemId", ref ItemId);
        GuideXml.Int(e, "itemType", ref ItemType);
        GuideXml.Int(e, "iconType", ref IconType);
    }
    public override int Check(GuideManager g)
    {
        var w = g.World;
        if (ItemId != 0 && !w.HasItemCount(ItemId, 1)) return ApplyNot(0);
        if (ItemType != 0 && !w.HasItemOfType(ItemType)) return ApplyNot(0);
        if (IconType != 0 && !w.HasItemWithIconCode(IconType)) return ApplyNot(0);
        return ApplyNot(1);
    }
}

// ---- quests, achievements, guild -------------------------------------------------------------------------------------------

/// <summary>QuestComplete: the quest's reward was taken.</summary>
public sealed class QuestCompleteCondition : GuideCondition
{
    public override void Load(XElement e) { base.Load(e); Kind = (int)GuideOccurrence.QuestComplete; }
    public override int Check(GuideManager g) => ApplyNot(g.World.IsQuestRewarded(Value));
    public override int SubKey(GuideManager g) => Not ? 0 : Value;
}

public sealed class QuestProgressCondition : GuideCondition
{
    public override void Load(XElement e) { base.Load(e); Kind = (int)GuideOccurrence.QuestProgress; }
    public override int Check(GuideManager g) => ApplyNot(g.World.IsQuestActive(Value));
    public override int SubKey(GuideManager g) => Not ? 0 : Value;
}

/// <summary>QuestSatisfied: the quest's goals are met (no kind: re-tested on every update it is queued for).</summary>
public sealed class QuestSatisfiedCondition : GuideCondition
{
    public override int Check(GuideManager g) => ApplyNot(g.World.IsQuestComplete(Value));
}

public sealed class AchieveExistTrophyCondition : GuideCondition
{
    public int? AchieveType;
    public override void Load(XElement e) { base.Load(e); AchieveType = GuideXml.OptInt(e, "achieveType"); }
    public override int Check(GuideManager g) => ApplyNot(g.World.HasTrophyOfType(AchieveType));
}

public sealed class JoinGuildCondition : GuideCondition
{
    public override int Check(GuideManager g) => ApplyNot(g.World.InGuild);
}

public sealed class GuildPvpPeriodCondition : GuideCondition
{
    public override int Check(GuideManager g) => ApplyNot(g.World.GuildPvpSeason);
}

/// <summary>HasManufacture: as the manufacture manager answers (no "not").</summary>
public sealed class HasManufactureCondition : GuideCondition
{
    public override int Check(GuideManager g) => g.World.HasManufacture ? 1 : 0;
}

// ---- money, home and UGC -------------------------------------------------------------------------------------------------

/// <summary>Money: the meso is at least <see cref="GuideCondition.Value"/>.</summary>
public sealed class MoneyCondition : GuideCondition
{
    public override int Check(GuideManager g) => ApplyNotBool(g.World.Balance(0) >= Value);
}

/// <summary>HasMoney: a currency's balance is above <see cref="GuideCondition.Value"/>. moneyType names 0-4; the Check also
/// knows 5-8, which no name loads.</summary>
public sealed class HasMoneyCondition : GuideCondition
{
    public int MoneyType;
    public override void Load(XElement e)
    {
        base.Load(e);
        MoneyType = 0;
        string s = "";
        GuideXml.String(e, "moneyType", ref s);
        MoneyType = s switch { "meso" => 0, "merat" => 1, "karma" => 2, "honor" => 3, "lu" => 4, _ => MoneyType };
    }
    public override int Check(GuideManager g) =>
        MoneyType is >= 0 and <= 8 ? ApplyNotBool(g.World.Balance(MoneyType) > Value) : 0;
}

public sealed class HomeModeCondition : GuideCondition
{
    public int Mode;
    public override void Load(XElement e) { base.Load(e); GuideXml.Int(e, "mode", ref Mode); }
    public override int Check(GuideManager g) => ApplyNotBool(g.World.HouseMode == Mode);
}

public sealed class IsMyHomeCondition : GuideCondition
{
    public override int Check(GuideManager g) => ApplyNotBool(g.World.IsMyHome);
}

public sealed class PlaceUgcCubeCondition : GuideCondition
{
    public int ItemId;
    public int? MinCount, MaxCount;
    public override void Load(XElement e)
    {
        base.Load(e);
        ItemId = 0;
        GuideXml.Int(e, "itemId", ref ItemId);
        MinCount = GuideXml.OptInt(e, "minCount");
        MaxCount = GuideXml.OptInt(e, "maxCount");
    }
    public override int Check(GuideManager g)
    {
        var w = g.World;
        if (ItemId != 0 && !w.HasCubeOfItem(ItemId)) return ApplyNot(0);
        if (MinCount != null || MaxCount != null)
        {
            int n = w.CubeCount;
            if (MinCount != null && n < MinCount.Value) return ApplyNot(0);
            if (MaxCount != null && n > MaxCount.Value) return ApplyNot(0);
        }
        return ApplyNot(1);
    }
}

public sealed class PlaceUgcCubeByIconTypeCondition : GuideCondition
{
    public int IconType;
    public override void Load(XElement e) { base.Load(e); IconType = 0; GuideXml.Int(e, "iconType", ref IconType); }
    public override int Check(GuideManager g) => ApplyNot(g.World.HasCubeWithIconCode(IconType));
}

public sealed class PlaceUgcCubeItemCondition : GuideCondition
{
    public Vector3? Pos;
    public bool? Attachable;
    public override void Load(XElement e)
    {
        base.Load(e);
        Pos = GuideXml.OptPoint3(e, "pos");
        Attachable = GuideXml.OptBool(e, "isAttachable");
    }
    public override int Check(GuideManager g)
    {
        if (Pos == null) return 0;
        var attach = g.World.CubeAttachableAt(Pos.Value);
        if (attach == null) return ApplyNot(0);
        if (Attachable != null && attach.Value != Attachable.Value) return ApplyNot(0);
        return ApplyNot(1);
    }
}

public sealed class RealEstateExpireDaysCondition : GuideCondition
{
    public override int Check(GuideManager g)
    {
        var w = g.World;
        if (!w.HasPlayer) return 0;
        var expire = w.UgcMapExpire;
        if (expire == null) return 0;
        long now = w.ServerTime;
        if (now >= expire.Value) return 0;
        return ApplyNotBool(Days(expire.Value - now) < Value);
    }
}

public sealed class UgcProductExpireDaysCondition : GuideCondition
{
    public override int Check(GuideManager g)
    {
        var w = g.World;
        var end = w.EarliestSaleEnd;
        if (end == null || end.Value == 0) return 0;
        long now = w.ServerTime;
        if (now >= end.Value) return 0;
        return ApplyNotBool(Days(end.Value - now) < Value);
    }
}

/// <summary>UgcCollectMoney: there is UGC money to collect (no "not").</summary>
public sealed class UgcCollectMoneyCondition : GuideCondition
{
    public override int Check(GuideManager g) => g.World.UgcMoneyToCollect ? 1 : 0;
}
