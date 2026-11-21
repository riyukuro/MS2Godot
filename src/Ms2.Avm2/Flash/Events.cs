namespace Ms2.Avm2.Flash;

[AsClass("flash.events.EventDispatcher")]
public class AsEventDispatcher : AsObject
{
    sealed class Listener { public AsFunction F; public int Priority; public long Order; }
    Dictionary<string, List<Listener>> bubble, capture;
    static long order;
    AsObject target;

    [AsCtor] void Ctor(object[] a) { target = a.Length > 0 ? a[0] as AsObject : null; }

    public Runtime RT => Class.Runtime;

    /// <summary>Events Flash sends to every listener whether or not the object is on the stage.</summary>
    public static readonly HashSet<string> BroadcastTypes = new() { "enterFrame", "exitFrame", "frameConstructed", "render", "activate", "deactivate" };

    [As] public void addEventListener(string type, AsFunction listener, bool useCapture = false, int priority = 0, bool useWeakReference = false)
    {
        if (type == null || listener == null) return;
        var map = useCapture ? (capture ??= new()) : (bubble ??= new());
        if (!map.TryGetValue(type, out var l)) map[type] = l = new();
        if (l.Any(x => Rt.StrictEquals(x.F, listener))) return;
        l.Add(new Listener { F = listener, Priority = priority, Order = order++ });
        l.Sort((x, y) => x.Priority != y.Priority ? y.Priority.CompareTo(x.Priority) : x.Order.CompareTo(y.Order));
        if (BroadcastTypes.Contains(type)) RT.Player?.Broadcast(type, this, true);
    }

    [As] public void removeEventListener(string type, AsFunction listener, bool useCapture = false)
    {
        var map = useCapture ? capture : bubble;
        if (type == null || map == null || !map.TryGetValue(type, out var l)) return;
        l.RemoveAll(x => Rt.StrictEquals(x.F, listener));
        if (l.Count == 0) map.Remove(type);
        if (BroadcastTypes.Contains(type) && !hasEventListener(type)) RT.Player?.Broadcast(type, this, false);
    }

    [As] public bool hasEventListener(string type) => (bubble?.ContainsKey(type ?? "") ?? false) || (capture?.ContainsKey(type ?? "") ?? false);

    /// <summary>
    /// Every listener of this object goes (Loader::unloadAndStop's ForceShutdown drops the loaded content's listeners,
    /// sfr_as3_19.cpp:8453): the broadcast kinds stop hearing it too, so a detached object gets no more enterFrame.
    /// </summary>
    internal void RemoveAllListeners()
    {
        if (bubble != null)
            foreach (var k in bubble.Keys)
                if (BroadcastTypes.Contains(k)) RT.Player?.Broadcast(k, this, false);
        bubble?.Clear();
        capture?.Clear();
    }

    [As] public bool willTrigger(string type)
    {
        for (AsObject o = this; o != null; o = (o as AsDisplayObject)?.Parent)
            if (o is AsEventDispatcher d && d.hasEventListener(type)) return true;
        return false;
    }

    [As] public bool dispatchEvent(AsEvent e)
    {
        if (e == null) throw RT.TypeError("Error #2007: Parameter event must be non-null.");
        if (e.Dispatched)
        {
            var c = RT.CallProperty(e, Name.Public("clone"), Array.Empty<object>()) as AsEvent;
            if (c != null) e = c;
        }
        return Dispatch(e);
    }

    /// <summary>Capture, target and bubble phases along the display list.</summary>
    public bool Dispatch(AsEvent e)
    {
        var tgt = target ?? this;
        e.Dispatched = true;
        e.Target = tgt;
        // the ancestors as they are at the start (pooled; none for an object off the display list)
        int n = 0;
        if (this is AsDisplayObject d) for (var p = d.Parent; p != null; p = p.Parent) n++;
        var path = n > 0 ? System.Buffers.ArrayPool<AsEventDispatcher>.Shared.Rent(n) : Array.Empty<AsEventDispatcher>();
        if (n > 0) { int k = 0; for (var p = ((AsDisplayObject)this).Parent; p != null; p = p.Parent) path[k++] = p; }
        try
        {
            // capture: from the top down; a listener that threw ends the dispatch (EventDispatcher::Dispatch,
            // sfr_as3_09.cpp: each phase stops when DispatchSingleEvent returns false)
            bool ok = true;
            e.Phase = 1;
            for (int i = n - 1; i >= 0 && ok && !e.Stopped; i--) ok = path[i].Fire(e, true);
            if (ok && !e.Stopped) { e.Phase = 2; ok = Fire(e, false); }
            if (ok && e.bubbles)
            {
                e.Phase = 3;
                for (int i = 0; i < n && ok; i++) { if (e.Stopped) break; ok = path[i].Fire(e, false); }
            }
        }
        finally { if (n > 0) { Array.Clear(path, 0, n); System.Buffers.ArrayPool<AsEventDispatcher>.Shared.Return(path); } }
        e.CurrentTarget = null;
        e.Phase = 0;
        return !e.Prevented;
    }

    /// <summary>Calls this object's listeners of <paramref name="e"/>'s type. An AS3 exception from a listener is output and
    /// ignored and the rest of the listeners are skipped (EventDispatcher::DispatchSingleEvent, sfr_as3_09.cpp:
    /// OutputAndIgnoreException, rv = false, break); false then, and the dispatch stops. dispatchEvent itself does not throw it.</summary>
    internal bool Fire(AsEvent e, bool useCapture)
    {
        var map = useCapture ? capture : bubble;
        if (map == null || !map.TryGetValue(e.type, out var l) || l.Count == 0) return true;
        e.CurrentTarget = target ?? this;
        // the listeners as they are now, in a pooled copy (one added or removed by a listener does not change this dispatch)
        int n = l.Count;
        var ls = System.Buffers.ArrayPool<Listener>.Shared.Rent(n);
        for (int i = 0; i < n; i++) ls[i] = l[i];   // per element: a bulk copy card-marks the whole range of the pooled (old) array
        try
        {
            for (int i = 0; i < n; i++)
            {
                if (e.StoppedNow) break;
                try { ls[i].F.Call(null, new object[] { e }); }
                catch (AsThrow t) { RT.OutputError("listener", e.type, t); return false; }
            }
        }
        finally { Array.Clear(ls, 0, n); System.Buffers.ArrayPool<Listener>.Shared.Return(ls); }
        return true;
    }

    /// <summary>Makes and dispatches a plain event of a native class (Event, MouseEvent ...).</summary>
    public bool Send(string cls, string type, bool bubbles = false, Action<AsEvent> init = null)
    {
        if (!willTrigger(type) && !hasEventListener(type)) return true;
        var e = (AsEvent)RT.GetClass(cls).Construct(type, Rt.Box(bubbles));
        init?.Invoke(e);
        return Dispatch(e);
    }
}

[AsClass("flash.events.Event", "Object")]
public class AsEvent : AsObject
{
    [As] public const string ACTIVATE = "activate", ADDED = "added", ADDED_TO_STAGE = "addedToStage", CANCEL = "cancel", CHANGE = "change",
        CLEAR = "clear", CLOSE = "close", COMPLETE = "complete", CONNECT = "connect", COPY = "copy", CUT = "cut", DEACTIVATE = "deactivate",
        ENTER_FRAME = "enterFrame", EXIT_FRAME = "exitFrame", FRAME_CONSTRUCTED = "frameConstructed", FULLSCREEN = "fullScreen", ID3 = "id3",
        INIT = "init", MOUSE_LEAVE = "mouseLeave", OPEN = "open", PASTE = "paste", REMOVED = "removed", REMOVED_FROM_STAGE = "removedFromStage",
        RENDER = "render", RESIZE = "resize", SCROLL = "scroll", SELECT = "select", SELECT_ALL = "selectAll", SOUND_COMPLETE = "soundComplete",
        TAB_CHILDREN_CHANGE = "tabChildrenChange", TAB_ENABLED_CHANGE = "tabEnabledChange", TAB_INDEX_CHANGE = "tabIndexChange", UNLOAD = "unload";
    [As] public string type { get; set; }
    [As] public bool bubbles { get; set; }
    [As] public bool cancelable { get; set; }
    internal bool Dispatched, Stopped, StoppedNow, Prevented;
    internal int Phase;
    internal AsObject Target, CurrentTarget;
    [As] public AsObject target => Target;
    [As] public AsObject currentTarget => CurrentTarget;
    [As] public int eventPhase => Phase;
    [AsCtor] protected virtual void Ctor(object[] a)
    {
        type = a.Length > 0 ? Rt.ToString(a[0]) : null;
        bubbles = a.Length > 1 && Rt.ToBoolean(a[1]);
        cancelable = a.Length > 2 && Rt.ToBoolean(a[2]);
    }
    [As] public AsEvent clone()
    {
        var e = (AsEvent)Class.Construct(type, Rt.Box(bubbles), Rt.Box(cancelable));
        CopyTo(e);
        return e;
    }
    protected virtual void CopyTo(AsEvent e) { }
    [As] public void stopPropagation() => Stopped = true;
    [As] public void stopImmediatePropagation() { Stopped = true; StoppedNow = true; }
    [As] public void preventDefault() { if (cancelable) Prevented = true; }
    [As] public bool isDefaultPrevented() => Prevented;
    [As("toString")] public string ToAsString() => $"[{Class?.LocalName} type=\"{type}\" bubbles={bubbles} cancelable={cancelable} eventPhase={Phase}]";
    [As] public string formatToString(string className, params object[] args) => "[" + className + " " + string.Join(" ", args.Select(Rt.ToString)) + "]";
}

[AsClass("flash.events.MouseEvent", "flash.events.Event")]
public class AsMouseEvent : AsEvent
{
    [As] public new const string CLICK = "click", DOUBLE_CLICK = "doubleClick", MOUSE_DOWN = "mouseDown", MOUSE_MOVE = "mouseMove", MOUSE_OUT = "mouseOut",
        MOUSE_OVER = "mouseOver", MOUSE_UP = "mouseUp", MOUSE_WHEEL = "mouseWheel", ROLL_OUT = "rollOut", ROLL_OVER = "rollOver",
        MIDDLE_CLICK = "middleClick", MIDDLE_MOUSE_DOWN = "middleMouseDown", MIDDLE_MOUSE_UP = "middleMouseUp",
        RIGHT_CLICK = "rightClick", RIGHT_MOUSE_DOWN = "rightMouseDown", RIGHT_MOUSE_UP = "rightMouseUp", CONTEXT_MENU = "contextMenu";
    [As] public double localX { get; set; }
    [As] public double localY { get; set; }
    [As] public AsObject relatedObject { get; set; }
    [As] public bool ctrlKey { get; set; }
    [As] public bool altKey { get; set; }
    [As] public bool shiftKey { get; set; }
    [As] public bool buttonDown { get; set; }
    [As] public int delta { get; set; }
    public double StageX, StageY;
    /// <summary>The button of a player-made event (EventDispatcher::CreateMouseEventObject, sfr_as3_09.cpp:2325-2328, writes
    /// MouseEventEx.buttonIdx = evtId.ButtonId). The client's VM has MouseEventEx built in; a movie whose ABC does not declare
    /// it still gets the index here, which Main's right-button listeners read.</summary>
    public int ButtonIdx;
    [As] public double stageX => Target is AsDisplayObject d ? d.LocalToGlobal(localX, localY).X : StageX;
    [As] public double stageY => Target is AsDisplayObject d ? d.LocalToGlobal(localX, localY).Y : StageY;
    [As] public void updateAfterEvent() { }
    [AsCtor] protected override void Ctor(object[] a)
    {
        base.Ctor(a.Length > 1 ? a : new object[] { a.Length > 0 ? a[0] : null, Rt.True });
        double N(int i) => a.Length > i ? Rt.ToNumber(a[i]) : 0;
        localX = N(3); localY = N(4); relatedObject = a.Length > 5 ? a[5] as AsObject : null;
        ctrlKey = a.Length > 6 && Rt.ToBoolean(a[6]); altKey = a.Length > 7 && Rt.ToBoolean(a[7]); shiftKey = a.Length > 8 && Rt.ToBoolean(a[8]);
        buttonDown = a.Length > 9 && Rt.ToBoolean(a[9]); delta = a.Length > 10 ? Rt.ToInt32(a[10]) : 0;
    }
    protected override void CopyTo(AsEvent e)
    {
        if (e is not AsMouseEvent m) return;
        m.localX = localX; m.localY = localY; m.relatedObject = relatedObject; m.ctrlKey = ctrlKey; m.altKey = altKey; m.shiftKey = shiftKey;
        m.buttonDown = buttonDown; m.delta = delta; m.StageX = StageX; m.StageY = StageY;
    }
}

[AsClass("flash.events.KeyboardEvent", "flash.events.Event")]
public class AsKeyboardEvent : AsEvent
{
    [As] public const string KEY_DOWN = "keyDown", KEY_UP = "keyUp";
    [As] public uint charCode { get; set; }
    [As] public uint keyCode { get; set; }
    [As] public uint keyLocation { get; set; }
    [As] public bool ctrlKey { get; set; }
    [As] public bool altKey { get; set; }
    [As] public bool shiftKey { get; set; }
    [As] public void updateAfterEvent() { }
    [AsCtor] protected override void Ctor(object[] a)
    {
        base.Ctor(a.Length > 1 ? a : new object[] { a.Length > 0 ? a[0] : null, Rt.True });
        charCode = a.Length > 3 ? Rt.ToUint32(a[3]) : 0; keyCode = a.Length > 4 ? Rt.ToUint32(a[4]) : 0; keyLocation = a.Length > 5 ? Rt.ToUint32(a[5]) : 0;
        ctrlKey = a.Length > 6 && Rt.ToBoolean(a[6]); altKey = a.Length > 7 && Rt.ToBoolean(a[7]); shiftKey = a.Length > 8 && Rt.ToBoolean(a[8]);
    }
    protected override void CopyTo(AsEvent e)
    {
        if (e is not AsKeyboardEvent k) return;
        k.charCode = charCode; k.keyCode = keyCode; k.keyLocation = keyLocation; k.ctrlKey = ctrlKey; k.altKey = altKey; k.shiftKey = shiftKey;
    }
}

[AsClass("flash.events.FocusEvent", "flash.events.Event")]
public class AsFocusEvent : AsEvent
{
    [As] public const string FOCUS_IN = "focusIn", FOCUS_OUT = "focusOut", KEY_FOCUS_CHANGE = "keyFocusChange", MOUSE_FOCUS_CHANGE = "mouseFocusChange";
    [As] public AsObject relatedObject { get; set; }
    [As] public bool shiftKey { get; set; }
    [As] public uint keyCode { get; set; }
    [AsCtor] protected override void Ctor(object[] a)
    {
        base.Ctor(a.Length > 1 ? a : new object[] { a.Length > 0 ? a[0] : null, Rt.True });
        relatedObject = a.Length > 3 ? a[3] as AsObject : null; shiftKey = a.Length > 4 && Rt.ToBoolean(a[4]); keyCode = a.Length > 5 ? Rt.ToUint32(a[5]) : 0;
    }
    protected override void CopyTo(AsEvent e) { if (e is AsFocusEvent f) { f.relatedObject = relatedObject; f.shiftKey = shiftKey; f.keyCode = keyCode; } }
}

[AsClass("flash.events.TextEvent", "flash.events.Event")]
public class AsTextEvent : AsEvent
{
    [As] public const string LINK = "link", TEXT_INPUT = "textInput";
    [As] public string text { get; set; }
    [AsCtor] protected override void Ctor(object[] a) { base.Ctor(a); text = a.Length > 3 ? Rt.ToString(a[3]) : ""; }
    protected override void CopyTo(AsEvent e) { if (e is AsTextEvent t) t.text = text; }
}

[AsClass("flash.events.ErrorEvent", "flash.events.TextEvent")]
public class AsErrorEvent : AsTextEvent { [As] public new const string ERROR = "error"; [As] public int errorID => 0; }

[AsClass("flash.events.IOErrorEvent", "flash.events.ErrorEvent")]
public class AsIOErrorEvent : AsErrorEvent { [As] public const string IO_ERROR = "ioError"; }

[AsClass("flash.events.SecurityErrorEvent", "flash.events.ErrorEvent")]
public class AsSecurityErrorEvent : AsErrorEvent { [As] public const string SECURITY_ERROR = "securityError"; }

[AsClass("flash.events.TimerEvent", "flash.events.Event")]
public class AsTimerEvent : AsEvent { [As] public const string TIMER = "timer", TIMER_COMPLETE = "timerComplete"; [As] public void updateAfterEvent() { } }

[AsClass("flash.events.ProgressEvent", "flash.events.Event")]
public class AsProgressEvent : AsEvent
{
    [As] public const string PROGRESS = "progress", SOCKET_DATA = "socketData";
    [As] public double bytesLoaded { get; set; }
    [As] public double bytesTotal { get; set; }
}

[AsClass("flash.events.HTTPStatusEvent", "flash.events.Event")]
public class AsHttpStatusEvent : AsEvent { [As] public const string HTTP_STATUS = "httpStatus"; [As] public int status { get; set; } }

[AsClass("flash.events.IMEEvent", "flash.events.TextEvent")]
public class AsImeEvent : AsTextEvent { [As] public const string IME_COMPOSITION = "imeComposition"; }

[AsClass("flash.events.ContextMenuEvent", "flash.events.Event")]
public class AsContextMenuEvent : AsEvent { [As] public const string MENU_ITEM_SELECT = "menuItemSelect", MENU_SELECT = "menuSelect"; }

[AsClass("flash.events.NetStatusEvent", "flash.events.Event")]
public class AsNetStatusEvent : AsEvent { [As] public const string NET_STATUS = "netStatus"; [As] public object info { get; set; } }

[AsClass("flash.events.StatusEvent", "flash.events.Event")]
public class AsStatusEvent : AsEvent { [As] public const string STATUS = "status"; [As] public string code { get; set; } [As] public string level { get; set; } }

[AsClass("flash.events.EventPhase")]
public class AsEventPhase : AsObject { [As] public const int CAPTURING_PHASE = 1, AT_TARGET = 2, BUBBLING_PHASE = 3; }

[AsClass("flash.utils.Timer", "flash.events.EventDispatcher")]
public class AsTimer : AsEventDispatcher
{
    [As] public double delay { get; set; }
    [As] public int repeatCount { get; set; }
    [As] public int currentCount { get; private set; }
    [As] public bool running { get; private set; }
    double next;
    [AsCtor] void Ctor(object[] a) { delay = a.Length > 0 ? Rt.ToNumber(a[0]) : 0; repeatCount = a.Length > 1 ? Rt.ToInt32(a[1]) : 0; }
    [As] public void start() { if (running) return; running = true; next = RT.TimerMs + Math.Max(1, delay); RT.Player?.AddTimer(this); }
    [As] public void stop() { running = false; RT.Player?.RemoveTimer(this); }
    [As] public void reset() { stop(); currentCount = 0; }
    /// <summary>The player takes the timer out of its run without the movie calling stop (Loader::unloadAndStop).</summary>
    internal void Halt() => running = false;

    /// <summary>Fires TIMER events that are due (called by the player each frame).</summary>
    internal void Pump()
    {
        int guard = 0;
        while (running && RT.TimerMs >= next && guard++ < 8)
        {
            currentCount++;
            next += Math.Max(1, delay);
            Send("flash.events.TimerEvent", "timer");
            if (repeatCount > 0 && currentCount >= repeatCount)
            {
                stop();
                Send("flash.events.TimerEvent", "timerComplete");
            }
        }
    }
}
