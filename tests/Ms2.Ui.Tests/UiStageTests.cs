using System.Text.Json.Nodes;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Ui;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

[TestFixture]
public class UiStageTests
{
    /// <summary>A movie with the dialogs' IDragSlot interface and one slot class implementing it (binding + handleDropEvent).</summary>
    static (Player P, AsClass Slot, List<object> Drops) Movie()
    {
        var rt = new Runtime();
        var layout = new JsonObject { ["movie"] = "t", ["stage"] = new JsonObject { ["width"] = 800, ["height"] = 600 }, ["symbols"] = new JsonObject(), ["root"] = new JsonObject() };
        var p = new Player(rt, layout);
        var iface = rt.NewNativeClass(UiStage.DragSlotInterface, null);
        iface.IsInterface = true;
        var slot = rt.NewNativeClass("t.Slot", rt.GetClass("flash.display.Sprite"), dynamic: true);
        slot.Interfaces.Add(iface);
        var drops = new List<object>();
        Runtime.Method(slot.InstanceTraits, "handleDropEvent", (self, a) => { drops.Add(self); return true; });
        return (p, slot, drops);
    }

    static AsSprite Box(Player p, AsClass cls, double x, string binding = null)
    {
        var b = (AsSprite)cls.Construct();
        b.graphics.beginFill(0xffffff); b.graphics.drawRect(0, 0, 40, 40);
        b.x = x;
        if (binding != null) b.SetDynamic("binding", binding);
        p.Stage.addChild(b);
        return b;
    }

    [Test]
    public void ADropOnAnotherMoviesSlotReachesItThroughAProxy()
    {
        var (a, aSlot, aDrops) = Movie();
        var (b, bSlot, bDrops) = Movie();
        var sprite = a.RT.GetClass("flash.display.Sprite");
        var own = Box(a, aSlot, 100, "InventoryDialog.0");
        var dragged = Box(a, sprite, 100);
        var icon = b.RT.GetClass("flash.display.Sprite");
        var other = Box(b, bSlot, 100, "EquipSlot.3");
        var child = Box(b, icon, 0);                      // an icon inside the other movie's slot is what the mouse hits
        b.Stage.removeChild(child); other.addChild(child);

        int aOrder = 0, bOrder = 1;
        var stage = new UiStage();
        stage.Add(a, () => (110, 10), () => aOrder);
        stage.Add(b, () => (110, 10), () => bOrder);

        // the other movie is in front: its slot, as a proxy that is an IDragSlot of the dragging movie
        var t = dragged.dropTarget;
        Assert.That(t, Is.Not.Null.And.Not.SameAs(own));
        Assert.That(a.RT.IsType(t, a.RT.GetClass(UiStage.DragSlotInterface)), Is.True, "passes DragManager's is IDragSlot");
        Assert.That(a.RT.GetProperty(t, Name.Public("binding")), Is.EqualTo("EquipSlot.3"));
        Assert.That(t.parent, Is.Null);
        Assert.That(UiStage.OtherMovieSlotOf(t)?.Slot, Is.SameAs(other));
        Assert.That(Rt.ToBoolean(a.Call(t, "handleDropEvent", new object[] { null })), Is.True);
        Assert.That(bDrops, Is.EqualTo(new object[] { other }), "the other movie's own handleDropEvent ran");
        Assert.That(aDrops, Is.Empty);

        // the dragging movie in front: its own slot, as before
        aOrder = 2;
        Assert.That(dragged.dropTarget, Is.SameAs(own));

        // nothing of the front movie under the mouse: the movie behind decides; a hit with no slot above it is null
        own.x = 300;
        Assert.That(UiStage.OtherMovieSlotOf(dragged.dropTarget)?.Slot, Is.SameAs(other));
        other.x = 300; child.x = 0;
        var plain = Box(b, icon, 100);
        Assert.That(dragged.dropTarget, Is.Null);

        stage.Remove(b);
        Assert.That(b.DropStage, Is.Null);
    }

    /// <summary>The drag event class a movie's DragManager and Main use, with its two type names.</summary>
    static AsClass DragEvents(Runtime rt)
    {
        var cls = rt.NewNativeClass(UiStage.DragEventClass, rt.GetClass("flash.events.Event"), dynamic: true);
        rt.SetProperty(cls, Name.Public("DRAG_START"), "dragStart");
        rt.SetProperty(cls, Name.Public("DRAG_END"), "dragEnd");
        return cls;
    }

    static void Dispatch(Player p, AsClass cls, string type, AsDisplayObject from, object dropTarget, AsDisplayObject sprite)
    {
        var e = (AsObject)cls.Construct(type, true, true);
        e.SetDynamic("dragTarget", from);
        e.SetDynamic("dropTarget", dropTarget);
        e.SetDynamic("dragSprite", sprite);
        p.Call(from, "dispatchEvent", new object[] { e });
    }

    sealed class Slots(string prefix, List<string> log) : IDragSlotOwner
    {
        public bool IsDragSlot(string name) => name.StartsWith(prefix, StringComparison.Ordinal);
        public void RefreshSlot(string name) => log.Add("refresh " + name);
        public void SlotDroppedOn(string name, string source, IDragSlotOwner sourceOwner) => log.Add($"on {name} from {source}");
        public void SlotDropUnresolved(string name) => log.Add("unresolved " + name);
        public void SlotDragStarted(string name) => log.Add("start " + name);
        public void SlotDragDone(string name) => log.Add("done " + name);
    }

    [Test]
    public void TheStagesDragEventsGoToTheSlotOwnersAsMainSendsThem()
    {
        var (a, aSlot, _) = Movie();
        var (b, bSlot, _) = Movie();
        var aEvents = DragEvents(a.RT);
        DragEvents(b.RT);
        var bag = Box(a, aSlot, 100, "Bag.1");
        var dragged = Box(a, a.RT.GetClass("flash.display.Sprite"), 100);
        var panel = Box(b, b.RT.GetClass("flash.display.Sprite"), 100, "Equip.panel");
        var equip = Box(b, bSlot, 0, "");
        b.Stage.removeChild(equip); panel.addChild(equip);
        var stage = new UiStage();
        stage.Add(a, () => (110, 10), () => 0);
        stage.Add(b, () => (110, 10), () => 1);
        var log = new List<string>();
        stage.DragSlots.Add(new Slots("Bag.", log));
        stage.DragSlots.Add(new Slots("Equip.", log));

        Dispatch(a, aEvents, "dragStart", bag, null, dragged);
        Assert.That(log, Is.EqualTo(new[] { "start Bag.1" }));
        Assert.That(dragged.HitTestDisabled, Is.True, "setHitTestDisable(dragSprite, true)");

        // the drop slot is in the other movie and has an empty binding: its parent's binding is the destination
        Dispatch(a, aEvents, "dragEnd", bag, dragged.dropTarget, dragged);
        Assert.That(log.Skip(1), Is.EqualTo(new[] { "on Equip.panel from Bag.1", "done Bag.1" }));

        // no drop slot: the source's open branch, then done; a drag end with no drag started sends nothing
        log.Clear();
        Dispatch(a, aEvents, "dragStart", bag, null, dragged);
        Dispatch(a, aEvents, "dragEnd", bag, null, dragged);
        Dispatch(a, aEvents, "dragEnd", bag, null, dragged);
        Assert.That(log, Is.EqualTo(new[] { "start Bag.1", "unresolved Bag.1", "done Bag.1" }));

        // a drop back on the source slot itself: vfunc_12, a refresh
        log.Clear();
        Dispatch(a, aEvents, "dragStart", bag, null, dragged);
        Dispatch(a, aEvents, "dragEnd", bag, bag, dragged);
        Assert.That(log, Is.EqualTo(new[] { "start Bag.1", "refresh Bag.1", "done Bag.1" }));
    }
}
