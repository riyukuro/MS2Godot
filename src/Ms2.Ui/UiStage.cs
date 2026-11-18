using Ms2.Avm2;
using Ms2.Avm2.Flash;

namespace Ms2.Ui;

/// <summary>
/// The movies of one screen as the client's single stage. In the client every dialog is loaded into main.gfx's stage and
/// VM, so a drag from one dialog can drop on another: DragManager.handleEndDragEvent takes the dragged sprite's dropTarget
/// (Sprite::dropTargetGet, MovieImpl::GetTopMostEntity over the whole stage), walks up to the first IDragSlot
/// (findSpriteAncestorOf), and calls its handleDropEvent. Here each movie has its own Player and Runtime, so this stage
/// does the search over all of them, front to back, and hands a target from another movie to the dragging movie as a
/// <see cref="SlotProxyClass"/> object, which passes that movie's own <c>is IDragSlot</c>.
/// </summary>
public sealed class UiStage : IDropStage
{
    /// <summary>The IDragSlot interface of every dialog movie (scaleform.clik.interfaces).</summary>
    public const string DragSlotInterface = "scaleform.clik.interfaces.IDragSlot";
    /// <summary>The class the drop event is made of (scaleform.clik.events.DragEvent; type "drop" is DragEvent.DROP).</summary>
    public const string DragEventClass = "scaleform.clik.events.DragEvent";
    /// <summary>The proxy class registered in a dragging movie's runtime.</summary>
    public const string SlotProxyClass = "ms2.stage.OtherMovieDragSlot";

    sealed record Movie(Player Player, Func<(double X, double Y)> Mouse, Func<int> DrawOrder, Func<bool> Shown);

    readonly List<Movie> movies = new();

    /// <summary>
    /// Adds a movie. <paramref name="mouse"/> gives the pointer in that movie's own stage coordinates; a larger
    /// <paramref name="drawOrder"/> is drawn in front (the client sorts its dialogs by CommonDepth layer, then by add order;
    /// the host draws its movies in that order). A movie <paramref name="shown"/> says is not drawn is not hit.
    /// </summary>
    public void Add(Player player, Func<(double X, double Y)> mouse, Func<int> drawOrder, Func<bool> shown = null)
    {
        Remove(player);
        movies.Add(new Movie(player, mouse, drawOrder, shown));
        player.DropStage = this;
        ListenForDrags(player);
    }

    public void Remove(Player player)
    {
        if (movies.RemoveAll(m => m.Player == player) > 0 && player.DropStage == this) player.DropStage = null;
    }

    // ---- main.gfx's Main: the drag callbacks of every movie ---------------------------------------------------------

    /// <summary>The client's one binding map (NotifyContext ms_mapBindings) for the drag slots of every movie here.</summary>
    public readonly BindingRegistry DragSlots = new();

    /// <summary>Main.m_dragBindId: the binding of the slot being dragged, "" when none.</summary>
    string dragBindId = "";

    /// <summary>
    /// Adds a dialog's movie (see <see cref="Add(Player, Func{ValueTuple{double, double}}, Func{int}, Func{bool})"/>): its
    /// drag slots join <see cref="DragSlots"/> and its drag callbacks look them up there. The dialog leaves when its
    /// controller unloads.
    /// </summary>
    public void Add(UiHost host, Func<(double X, double Y)> mouse, Func<int> drawOrder, Func<bool> shown = null)
    {
        Add(host.Player, mouse, drawOrder, shown);
        host.DragSlots = DragSlots;
        DragSlots.Add(host.Controller as IDragSlotOwner);
        host.Controller.Unloaded += () => Remove(host);
    }

    readonly HashSet<Player> listening = new();

    /// <summary>The listeners main.gfx's Main puts on the client's stage (<see cref="OnDragStart"/>, <see cref="OnDragEnd"/>),
    /// on this movie's stage; they do nothing once the movie has left.</summary>
    void ListenForDrags(Player player)
    {
        var rt = player.RT;
        if (!listening.Add(player) || rt.GetClass(DragEventClass) is not { } ev) return;
        void Listen(string type, Action<Player, object> f)
        {
            if (rt.GetProperty(ev, Name.Public(type)) is not string name) return;
            var fn = new AsPlainFunction(rt, new NativeMethod("Main." + type, (_, a) =>
            {
                if (movies.Any(m => m.Player == player)) rt.Guard("Main " + type, () => f(player, a.ElementAtOrDefault(0)));
                return Rt.Undef;
            }));
            player.Call(player.Stage, "addEventListener", new object[] { name, fn });
        }
        Listen("DRAG_START", OnDragStart);
        Listen("DRAG_END", OnDragEnd);
    }

    public void Remove(UiHost host)
    {
        Remove(host.Player);
        DragSlots.Remove(host.Controller as IDragSlotOwner);
    }

    /// <summary>An AS String field set from a value (coerce_s): null and undefined stay null.</summary>
    static string AsString(object v) => Rt.IsNullish(v) ? null : Rt.ToString(v);

    static object Get(Runtime rt, object o, string name) => o is AsObject ? rt.GetProperty(o, Name.Public(name)) : null;

    /// <summary>
    /// Main.onStartDragAlertModel: with a dragged slot, the drag sprite is left out of hit tests
    /// (InteractiveObjectEx.setHitTestDisable), GameDataModel.notifySlotDragStart(slot.binding) and the binding is kept;
    /// without one, the kept binding is cleared.
    /// </summary>
    void OnDragStart(Player player, object e)
    {
        var slot = Get(player.RT, e, "dragTarget");
        if (Rt.ToBoolean(slot))
        {
            if (Get(player.RT, e, "dragSprite") is AsDisplayObject sprite) sprite.HitTestDisabled = true;
            var binding = AsString(Get(player.RT, slot, "binding"));
            NotifyDragSlot.SlotDragStart(DragSlots, new object[] { binding });
            dragBindId = binding;
        }
        else dragBindId = "";
    }

    /// <summary>
    /// Main.onEndDragAlertModel: with a kept binding, the drop slot's binding, or its parent's when that is null or empty
    /// (null without a drop slot), goes to GameDataModel.notifySlotDragEnd(kept, drop); the kept binding is cleared. A drop
    /// slot of another movie (a proxy) is read in that movie.
    /// </summary>
    void OnDragEnd(Player player, object e)
    {
        if (!string.IsNullOrEmpty(dragBindId))
        {
            var drop = Get(player.RT, e, "dropTarget");
            var (rt, obj) = OtherMovieSlotOf(drop) is { } t ? (t.Owner.RT, (object)t.Slot) : (player.RT, drop);
            string binding = null;
            if (Rt.ToBoolean(obj))
            {
                binding = AsString(Get(rt, obj, "binding"));
                if (string.IsNullOrEmpty(binding) && obj is AsDisplayObject d && d.Parent != null) binding = AsString(Get(rt, d.Parent, "binding"));
            }
            NotifyDragSlot.SlotDragEnd(DragSlots, new object[] { dragBindId, binding });
        }
        dragBindId = "";
    }

    /// <summary>
    /// MovieImpl::GetTopMostEntity over the whole stage: the movies from front to back, each tested at the pointer in its
    /// own coordinates with every object counted; only the dragging movie leaves out the dragged sprite. The first movie
    /// with a hit decides. A hit in the dragging movie is returned as it is. A hit in another movie is walked up to its
    /// first IDragSlot there (findSpriteAncestorOf, with that movie's own interface) and returned as a proxy of it; a hit
    /// with no IDragSlot above it is returned as null, which is what findSpriteAncestorOf would make of it.
    /// </summary>
    public AsDisplayObject DropTarget(Player source, AsDisplayObject dragged)
    {
        foreach (var m in movies.OrderByDescending(m => m.DrawOrder()))
        {
            if (m.Shown?.Invoke() == false) continue;
            var (x, y) = m.Mouse();
            var hit = m.Player.GetTopMostEntity(x, y, true, m.Player == source ? dragged : null);
            if (hit == null) continue;
            if (m.Player == source) return hit;
            var slotType = m.Player.RT.GetClass(DragSlotInterface);
            AsDisplayObject slot = hit;
            while (slot != null && (slotType == null || !m.Player.RT.IsType(slot, slotType))) slot = slot.Parent;
            return slot == null ? null : Proxy(source, m.Player, slot);
        }
        return null;
    }

    /// <summary>
    /// A stand-in, in the dragging movie's runtime, for <paramref name="slot"/> of <paramref name="owner"/>. It implements
    /// that runtime's IDragSlot, so DragManager stops at it and sends it the drop. Its <c>binding</c> is the slot's (what
    /// Main.onEndDragAlertModel reads); <c>parent</c> is null; <c>handleDropEvent</c> runs the slot's own handleDropEvent
    /// with a drop event made in the slot's movie and returns its answer.
    /// </summary>
    static AsSprite Proxy(Player source, Player owner, AsDisplayObject slot)
    {
        var rt = source.RT;
        var cls = rt.GetClass(SlotProxyClass) ?? MakeProxyClass(rt);
        if (cls == null) return null;
        var p = (AsSprite)cls.Construct();
        p.SetDynamic("binding", owner.RT.GetProperty(slot, Name.Public("binding")));
        p.SetDynamic(TargetKey, new OtherMovieSlot(owner, slot));
        return p;
    }

    const string TargetKey = "__otherMovieSlot";

    sealed class OtherMovieSlot(Player owner, AsDisplayObject slot)
    {
        public readonly Player Owner = owner;
        public readonly AsDisplayObject Slot = slot;
    }

    static AsClass MakeProxyClass(Runtime rt)
    {
        var iface = rt.GetClass(DragSlotInterface);
        var sprite = rt.GetClass("flash.display.Sprite");
        if (iface == null || sprite == null) return null;
        var cls = rt.NewNativeClass(SlotProxyClass, sprite, dynamic: true);
        cls.Interfaces.Add(iface);
        Runtime.Method(cls.InstanceTraits, "handleDropEvent", (self, a) =>
        {
            if (self is not AsObject o || o.GetDynamic(TargetKey) is not OtherMovieSlot t) return false;
            var ev = t.Owner.RT.GetClass(DragEventClass)?.Construct("drop", null, null, t.Slot, null);
            return Rt.ToBoolean(t.Owner.Call(t.Slot, "handleDropEvent", new object[] { ev }));
        });
        return cls;
    }

    /// <summary>The slot of another movie a proxy stands for (its movie and display object), or null for any other object.</summary>
    public static (Player Owner, AsDisplayObject Slot)? OtherMovieSlotOf(object proxy) =>
        proxy is AsObject o && o.GetDynamic(TargetKey) is OtherMovieSlot t ? (t.Owner, t.Slot) : null;
}
