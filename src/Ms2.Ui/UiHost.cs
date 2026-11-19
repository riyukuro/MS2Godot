using System.Collections;
using Ms2.Avm2;
using Ms2.Avm2.Flash;

namespace Ms2.Ui;

/// <summary>
/// The game side of one UI movie: what GFxMovieMain and UISwfEntity do for a dialog in the client.
/// <list type="bullet">
/// <item>The movie's ExternalInterface.call(id, name, args...) goes to the controller (GFxMovieMain::vfunc_4,
/// gfxmoviemain.cpp:96): loadInit / loadComplete / playSound handled here, every other name to
/// <see cref="UiController.OnCallback"/> (UISwfEntity::vfunc_28).</item>
/// <item>The shared static functions the shell movie installs on its sharedFunctionInit / sharedDataInit
/// (NotifySwfEntity, NotifyDragSlot, NotifyBinding): MS2DataModel.getUserData → <see cref="UiController.GetUserData"/>
/// (notifyswfentity.cpp:30), DataBinding.createBindingImpl / deleteBindingImpl → <see cref="Bind"/>.</item>
/// <item>The load sequence: construct, run the first frames, the controller's sizing and stage alignment
/// (UIObject vfunc_43), then <see cref="UiController.OnLoadComplete"/>.</item>
/// </list>
/// The controller pushes data with <see cref="Invoke(string, object[])"/> (m_30->Invoke), child lookups and the
/// SetChildText / SetChildVisible helpers, with C# values converted the way the ASObj / GFx::Value setters do.
/// </summary>
public sealed class UiHost
{
    public readonly Player Player;
    public readonly UiController Controller;
    public readonly UiModel Model;
    /// <summary>The dialog id: the name of the Loader the movie sits in, which CommonDef.getID returns in every call.</summary>
    public readonly string Id;
    /// <summary>The object the controller drives (UISwfEntity m_30, CreateGameEntity of the root).</summary>
    public AsObject Entity;

    /// <summary>The character's UI settings folder (GetCharacterFolder); null before a character is chosen (nothing is kept).</summary>
    public ICharacterFolder Folder;

    /// <summary>
    /// The client size times the UI scale (DAT_143af62d4 / DAT_143af62d8 times FUN_141276490), which a dialog's saved rates
    /// are relative to (UIDialog vfunc_53 / vfunc_54). The window host sets it; until then it is the movie's stage.
    /// </summary>
    public double ClientStageW, ClientStageH;
    /// <summary>Every callback name the movie sent, in order (for tests and logs).</summary>
    public readonly List<string> Calls = new();
    /// <summary>Callback names no controller handled (the client returns 0 for these too).</summary>
    public readonly HashSet<string> Unhandled = new();

    readonly Dictionary<string, List<(object Fn, object Scope)>> bindings = new();

    public Runtime RT => Player.RT;

    /// <summary>The drag slots this movie's drag callbacks look up: its own controller's, or a shared stage's
    /// (<see cref="UiStage"/>), as the client's one binding map.</summary>
    public BindingRegistry DragSlots { get; set; }

    public UiHost(Player player, UiController controller, UiModel model, string id = null)
    {
        Player = player; Controller = controller; Model = model;
        ClientStageW = player.StageW; ClientStageH = player.StageH;
        Id = id ?? (string)player.Layout["movie"];
        Player.HostId = Id;
        Player.External = External;
        controller.Host = this;
        DragSlots = new BindingRegistry();
        DragSlots.Add(controller as IDragSlotOwner);
        RT.Guard("sharedFunctionInit", InstallShared);
    }

    // ---- load -------------------------------------------------------------------------------------------------

    /// <summary>Timing marks of <see cref="Load"/>'s steps (null: none).</summary>
    public Action<string> Mark;

    /// <summary>Constructs the movie and runs the client's load sequence. Returns false when the document class failed.</summary>
    public bool Load(int frames = 1)
    {
        if (Controller.IsDialogEntity)
        {
            Model.TopLayer = Id;                                    // UIDialog's open: FUN_1422d4dd0(m_20)
            Guide.GuideManager.Current?.QueueOccurrence(Guide.GuideOccurrence.Dialog, 0);   // vfunc_65 (uidialog.cpp:585)
        }
        if (!Player.Start()) return false;
        Mark?.Invoke("as-construct");
        // The client's Loader path: MovieRoot::ProcessLoadQueueEntry (sfr_as3_18.cpp:8483-8508) queues the INIT / COMPLETE events
        // on AL_Manual; FrameCounter::AdvanceFrame (sfr_as3_16z.cpp:240) requeues them behind the loaded movie's frame-1 script, and
        // the new sprite is skipped for frame advance by Flag_NeedExecuteFrame (:633). So the timelines stay on frame 1 until
        // msLoader.contentloadComplete calls "loadComplete" -> UISwfEntityManager::vfunc_9 -> OnLoadComplete (a lobby dialog's
        // popup() returns false from its "popup" label on frame 2), hence one tick that does not advance them; more frames only
        // where a caller asks.
        // Known order difference: the client sends configUI (the render event, stage.invalidate -> validateNow) one frame before
        // loadComplete, not in the same tick, and runs it before enterFrame/timers; our Player.Tick runs enterFrame and timers
        // first and the render event last.
        for (int i = 0; i < frames; i++) Player.Tick(advanceTimelines: i > 0);
        Mark?.Invoke("as-frames");
        Entity = Controller.FindEntity();
        if (Entity == null) { RT.NoteMissing("ui entity of " + Id); return false; }
        // the part of a subclass's OnLoadComplete that runs before it calls UIDialog's (UIShopDialog: initShop), then
        // UISwfEntity::OnLoadComplete (uiswfentity.cpp:443): the settings file (vfunc_5), the layout (FUN_1422dc450), then
        // the rest of the subclass's OnLoadComplete
        RT.Guard("OnLoadComplete start " + Id, Controller.OnLoadCompleteBeforeBase);
        RT.Guard("load transform " + Id, Controller.LoadTransform);
        Model.AlignWidgets?.Add(Controller);                        // UISwfEntityManager's loaded entities
        Controller.LayoutAndClear(Player.StageW, Player.StageH);
        RT.Guard("OnLoadComplete " + Id, Controller.OnLoadComplete);
        Model.Changed += OnStatus;
        Mark?.Invoke("controller-init");
        return true;
    }

    void OnStatus(StatusEvent e) => RT.Guard("status " + e, () => Controller.OnStatus(e));

    /// <summary>
    /// The dialog's movie is taken down, in UISwfEntity::Unload's order (uiswfentity.cpp:241): first the entity's listeners go
    /// (vfunc_26, before the swf is dropped: the status dispatcher unregister and the widget alignment list here), then the
    /// loader manager unloads the swf (UnloadSwf, uiswfentitymanager.cpp:215 → ASMovieClipLoader's "unloadSwf",
    /// asmoviecliploader.cpp:47, whose AS side, scaleform.ms2.loader.msLoader.unloadSwf, removes the dialog's clip from its
    /// layer and unloads its Loader). The removedFromStage that runs down its display tree makes every element registered
    /// with the movie's TooltipManager destroy its tooltip (TooltipManager.appendMouseEvent's REMOVED_FROM_STAGE listener →
    /// handleRemovedFromStage → destroyTooltipByOwner → "Tooltip" "destroyTooltip", and UIComponent.handleRemoveFromStage →
    /// unregisterMouseEvent likewise), so a dialog closed while one of its tooltips shows leaves none behind in the run's
    /// tooltip movie; the unload also stops the content (its listeners, and the timers of the movie's own code: <see cref="AsLoader.unloadAndStop"/>). vfunc_27
    /// follows.
    /// </summary>
    public void Unload()
    {
        Model.Changed -= OnStatus;
        Model.AlignWidgets?.Remove(Controller);
        if (Player?.HostLoader is { } loader) RT.Guard("unload " + Id, () => loader.unloadAndStop());
    }

    // ---- AS -> C++ --------------------------------------------------------------------------------------------

    object External(string ev, object[] args)
    {
        // one-argument shell calls from the shared library classes' initialize()
        if (ev == "dataBindingInit" || ev == "gameDataModelInit") { RT.Guard(ev, InstallShared); return null; }
        bool own = false;
        RT.Guard("movie event " + ev, () => own = Controller.OnMovieEvent(ev, args));
        if (own) return null;
        if (ev == "Tooltip")
        {
            // GFxMovieMain::vfunc_4 (gfxmoviemain.cpp:130): FindMovie(5)->vfunc_3(args[0], the rest), the tooltip movie
            if (args.Length > 0 && Tooltips is { } t)
                RT.Guard("Tooltip", () => t.Call(Rt.ToString(args[0]), args.Skip(1).Select(FromAs).ToArray()));
            return null;
        }
        if (ev == "MouseCursor") return null;                           // forwarded to the cursor movie
        if (args.Length == 0) return null;
        var name = Rt.ToString(args[0]);
        var rest = args.Skip(1).ToArray();
        if (ev != Id) return null;      // another loader inside the movie (msLoader of an image or sub movie)
        Calls.Add(name);
        // GFxMovieHeadUpDisplay's callback: "OnTopMostLayer" makes this movie the top one, then goes to the entity too
        if (name.Equals("OnTopMostLayer", StringComparison.OrdinalIgnoreCase)) Model.TopLayer = Id;
        switch (name)
        {
            case "loadInit": return null;       // the client's CreateGameEntity: Load() finds the entity
            case "loadComplete": return null;   // Load() runs OnLoadComplete
            case "playSound": return null;      // no audio from the UI movies here
        }
        bool handled = false;
        ReturnValue = null;
        RT.Guard("callback " + name, () => handled = Controller.OnCallbackBeforeDialog(name, rest));
        if (handled) return null;
        if (Controller.IsDialogEntity)
        {
            RT.Guard("dialog callback " + name, () => handled = Controller.DialogCallback(name));
            if (handled) return null;
        }
        RT.Guard("callback " + name, () => handled = Controller.OnCallback(name, rest));
        if (!handled) Unhandled.Add(name);
        var ret = ReturnValue;
        ReturnValue = null;
        return ret == null ? null : ToAs(ret);
    }

    /// <summary>
    /// GFx::Movie::SetExternalInterfaceRetVal: what the ExternalInterface.call being handled returns to the movie (set by
    /// <see cref="UiController.OnCallback"/>; undefined when not set).
    /// </summary>
    public object ReturnValue;

    void InstallShared()
    {
        CommonCallback();
        if (RT.GetClass("scaleform.ms2.Data.MS2DataModel") is { } dm)
        {
            Static(dm, "getUserData", a =>
            {
                if (a.Length < 2) return null;
                var name = Rt.ToString(a[1]);
                Calls.Add("getUserData:" + name);
                var args = a.Skip(2).ToArray();
                var r = Controller.GetUserData(name, args);
                return r != null ? ToAs(r) : ToAs(Controller.GetUserValue(name, args));
            });
            Static(dm, "playSound", a => null);
        }
        if (RT.GetClass("scaleform.clik.data.DataBinding") is { } db)
        {
            Static(db, "createBindingImpl", a => { Bind(Rt.ToString(a.ElementAtOrDefault(0)), a.ElementAtOrDefault(1), a.ElementAtOrDefault(2)); return null; });
            Static(db, "deleteBindingImpl", a => { Unbind(Rt.ToString(a.ElementAtOrDefault(0)), a.ElementAtOrDefault(1)); return null; });
        }
        if (RT.GetClass("com.scaleform.mmo.GameDataModel") is { } gm)
        {
            foreach (var n in new[] { "notifySlotUse", "notifySlotShiftClick" })
                Static(gm, n, a => { RT.Guard(n, () => Controller.OnCallback(n, a)); return null; });
            // NotifyDragSlot::SlotDragStart / SlotDragEnd (notifydragslot.cpp:77, :93) over the screen's drag slots
            Static(gm, "notifySlotDragStart", a => { RT.Guard("notifySlotDragStart", () => NotifyDragSlot.SlotDragStart(DragSlots, a)); return null; });
            Static(gm, "notifySlotDragEnd", a => { RT.Guard("notifySlotDragEnd", () => NotifyDragSlot.SlotDragEnd(DragSlots, a)); return null; });
            // NotifyDragSlot::GetSlotTooltip (notifydragslot.cpp:127): for a bound slot a new object, filled by its vfunc_0
            Static(gm, "getSlotTooltip", a =>
            {
                IDictionary<string, object> t = null;
                RT.Guard("getSlotTooltip", () => t = Controller.GetSlotTooltip(Rt.ToString(a.ElementAtOrDefault(0))));
                return t == null ? null : ToAs(t);
            });
            Static(gm, "getBagSlotBindings", a => ToAs(Controller.GetBagSlotBindings(Rt.ToString(a.ElementAtOrDefault(0)))));
        }
    }

    /// <summary>
    /// GFxMovie::InvokeCommonCallback (gfxmovie.cpp:334) with the callback CUIService sets, FUN_141ade430
    /// (uiservice.cpp:3185): members of scaleform.ms2.common.Common: the feature list, and the meso and numeric text
    /// functions (GetMesoText / GetNumericText, uiservice.cpp:3147 / 3166: (obj, text) sets obj.text). The stage margins
    /// (FUN_1418dd0c0 / dd110 / dd0d0, from DAT_1439f8ccc, DAT_1439f8cd0, DAT_1439f8cc8) and the tooltip label width are
    /// not set yet.
    /// </summary>
    void CommonCallback()
    {
        if (RT.GetClass("scaleform.ms2.common.Common") is not { } common) return;
        if (Model.FeatureEnabledByName != null)
            RT.SetProperty(common, Name.Public("m_currentFeatureByString"), FeatureTable.EnabledString(Model.FeatureEnabledByName));
        // GFxMovie::RegisterFormatFunction (gfxmovie.cpp:319) with CUIService's format callback (FUN_141addb30)
        Static(common, "m_getFormatFunction", FormatCallback);
        Static(common, "m_getMesoTextFunction", a => TextCallback(a, NumberText.MesoText));
        Static(common, "m_getNumericFunction", a => TextCallback(a, NumberText.NumericText));
    }

    /// <summary>
    /// FUN_141addb30 (uiservice.cpp:3252), Common.getFormatText's function: (obj, key, args...) sets obj.text. A key that
    /// starts with "$" is the movie translator's text (GFxManager::Localize), "s_" a common string (GetStringCommonByKey),
    /// anything else the text itself; an empty key or text sets nothing. Each further argument becomes a string by its
    /// GFx type (int "%d", uint "%ld", Number "%f" of the float, strings as they are, any other type empty) and the text
    /// is formatted with them (StringFormat); without them it is the text.
    /// </summary>
    object FormatCallback(object[] a)
    {
        if (a.Length < 1) return null;
        string key = a.Length > 1 && a[1] is not null && a[1] is not Undefined ? Rt.ToString(a[1]) : "";
        if (key.Length == 0) return null;
        string format = key.StartsWith('$') ? Player.Translate?.Invoke(key) ?? ""
            : key.StartsWith("s_", StringComparison.Ordinal) ? Model.Tables.Common(key) ?? ""
            : key;
        if (format.Length == 0) return null;
        string result = format;
        if (a.Length > 2)
        {
            var args = a.Skip(2).Select(v => (object)(v switch
            {
                int i => i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                uint u => u.ToString(System.Globalization.CultureInfo.InvariantCulture),
                double d => ((double)(float)d).ToString("F6", System.Globalization.CultureInfo.InvariantCulture),
                string str => str,
                _ => "",
            })).ToArray();
            result = Ms2.Quest.ClientFormat.Format(format, args);
        }
        if (a[0] is AsObject o) RT.SetProperty(o, Name.Public("text"), result);
        return null;
    }

    // GetMesoText / GetNumericText: no work without an argument; the first is the object, the second the text (ASData Pop order)
    object TextCallback(object[] a, Func<string, int, string> f)
    {
        if (a.Length < 1 || a[0] is not AsObject o) return null;
        string text = a.Length > 1 ? Rt.ToString(a[1]) : "";
        RT.SetProperty(o, Name.Public("text"), f(text, NumberText.DefaultLanguage));
        return null;
    }

    void Static(AsClass cls, string name, Func<object[], object> f) =>
        RT.SetProperty(cls, Name.Public(name), new AsPlainFunction(RT, new NativeMethod(cls.Name + "." + name, (s, a) => f(a))));

    // ---- data bindings (DataBinding.createBinding(name, setter, scope)) ---------------------------------------

    /// <summary>NotifyBinding: keeps the setter and pushes the current value (PushBinding, the BindData's vfunc_8).</summary>
    void Bind(string name, object fn, object scope)
    {
        if (name == null || fn is not AsFunction) return;
        if (!bindings.TryGetValue(name, out var l)) bindings[name] = l = new();
        l.Add((fn, scope));
        // CreateBindingImpl: SetData(the scope) runs the BindData's vfunc_9 before PushBinding
        if (!Rt.IsNullish(scope)) RT.Guard("binding data " + name, () => Controller.OnBindData(name, scope));
        // PushBinding pushes to the binding's target, the setter just bound; the ones bound before already hold the value
        // (pushing them again made binding n slots under one name n*(n+1)/2 setter calls)
        Push(name, fn, scope);
    }

    void Unbind(string name, object fn)
    {
        if (name != null && bindings.TryGetValue(name, out var l)) l.RemoveAll(b => ReferenceEquals(b.Fn, fn));
    }

    public IEnumerable<string> BindingNames => bindings.Where(b => b.Value.Count > 0).Select(b => b.Key);

    /// <summary>Calls every setter bound to <paramref name="name"/> with the controller's value (null clears the slot).</summary>
    public void Push(string name)
    {
        if (!bindings.TryGetValue(name, out var l)) return;
        var v = ToAs(Controller.GetBinding(name));
        foreach (var (fn, scope) in l.ToArray()) Call(name, fn, scope, v);
    }

    void Push(string name, object fn, object scope) => Call(name, fn, scope, ToAs(Controller.GetBinding(name)));

    void Call(string name, object fn, object scope, object v) =>
        RT.Guard("binding " + name, () => ((AsFunction)fn).Call(Rt.IsNullish(scope) ? null : scope, new[] { v }));

    // ---- C++ -> AS --------------------------------------------------------------------------------------------

    /// <summary>m_30->Invoke(method, args...).</summary>
    public object Invoke(string method, params object[] args) => Invoke(Entity, method, args);

    /// <summary>Value::Invoke on any AS object (a child component, a value object).</summary>
    public object Invoke(object target, string method, params object[] args)
    {
        if (Rt.IsNullish(target)) return null;
        return Player.Call(target, method, args.Select(ToAs).ToArray());
    }

    /// <summary>GetChild&lt;T&gt;("m_x"): a member of the entity (the instance names the movie declares).</summary>
    public object Child(string name, object parent = null)
    {
        var p = parent ?? Entity;
        if (Rt.IsNullish(p)) return null;
        object v = null;
        RT.Guard("child " + name, () => v = RT.GetProperty(p, Name.Public(name)));
        return Rt.IsNullish(v) ? null : v;
    }

    public object Get(object target, string member)
    {
        if (Rt.IsNullish(target)) return null;
        object v = null;
        RT.Guard("get " + member, () => v = RT.GetProperty(target, Name.Public(member)));
        return v;
    }

    /// <summary>Value::SetMember.</summary>
    public void Set(object target, string member, object value)
    {
        if (Rt.IsNullish(target)) return;
        RT.Guard("set " + member, () => RT.SetProperty(target, Name.Public(member), ToAs(value)));
    }

    /// <summary>ASDisplayObject::vfunc_3 / vfunc_4 (SetDisplayInfo visible).</summary>
    public void SetVisible(object target, bool visible) => Set(target, "visible", visible);

    /// <summary>ASDisplayObject::vfunc_2 / vfunc_1 (asdisplayobject.cpp:44): places the object on the stage by its width and
    /// height. Mode 0 centre, 1 top centre, 2 bottom centre, 3 left middle, 4 right middle, 5 absolute, 6 top right,
    /// 7 bottom left, 8 bottom right; (dx, dy) is added.</summary>
    public void Align(object target, int mode, int dx, int dy)
    {
        if (Rt.IsNullish(target)) return;
        int sw = (int)Player.StageW, sh = (int)Player.StageH;
        int w = (int)Rt.ToNumber(Get(target, "width")), h = (int)Rt.ToNumber(Get(target, "height"));
        int cx = (int)((sw - w) * 0.5f), cy = (int)((sh - h) * 0.5f);
        (int x, int y) = mode switch
        {
            0 => (cx, cy), 1 => (cx, 0), 2 => (cx, sh - h), 3 => (0, cy), 4 => (sw - w, cy),
            6 => (sw - w, 0), 7 => (0, sh - h), 8 => (sw - w, sh - h), _ => (0, 0),
        };
        Set(target, "x", x + dx);
        Set(target, "y", y + dy);
    }

    /// <summary>The display object named <paramref name="name"/> below <paramref name="from"/> (depth first), as
    /// SetChildText / SetChildVisible find it.</summary>
    public AsDisplayObject FindNamed(string name, AsDisplayObject from = null)
    {
        AsDisplayObject Walk(AsDisplayObject o)
        {
            if (o.InstanceName == name) return o;
            if (o is AsContainer c) foreach (var k in c.Children) if (Walk(k) is { } r) return r;
            return null;
        }
        from ??= Player.Root;
        if (from == null) return null;
        if (from is AsContainer root) foreach (var k in root.Children) if (Walk(k) is { } r) return r;
        return null;
    }

    /// <summary>SetChildText(root, name, text): the text of the named text field below the root.</summary>
    public bool SetChildText(string name, string text, AsDisplayObject from = null)
    {
        var t = FindNamed(name, from);
        if (t == null) return false;
        Set(t, "text", text);
        return true;
    }

    /// <summary>SetChildVisible(root, name, visible).</summary>
    public bool SetChildVisible(string name, bool visible, AsDisplayObject from = null)
    {
        var t = FindNamed(name, from);
        if (t == null) return false;
        SetVisible(t, visible);
        return true;
    }

    /// <summary>The run's tooltip movie (GFxManager::FindMovie(5), GFxMovieTooltip), which every movie's "Tooltip" calls
    /// go to; null = none hosted.</summary>
    public static ITooltipMovie Tooltips;

    /// <summary>
    /// An AS value as plain C# values, the copy GFxMovieTooltip::vfunc_3 makes of a call's objects into its own movie
    /// (GFxObjectAdapter / GFxArrayAdapter @0x1422f13b0 / @0x1422f1760): objects as member maps and arrays as lists,
    /// nested ones copied too, other values as they are.
    /// </summary>
    public static object FromAs(object v) => v switch
    {
        AsArray a => a.Items.Select(FromAs).ToList(),
        AsFunction => v,
        AsObject o => o.DynamicKeys.ToDictionary(k => k, k => FromAs(o.GetDynamic(k))),
        _ => v,
    };

    /// <summary>An ASObj value object with the given members.</summary>
    public AsObject Obj(params (string Key, object Value)[] members)
    {
        var o = new AsObject(RT.ObjectClass);
        foreach (var (k, v) in members) o.SetDynamic(k, ToAs(v));
        return o;
    }

    /// <summary>C# → AS value, as the GFx::Value setters type them: integers stay int (uint above int range as Number),
    /// 64-bit counters as Number, dictionaries as Objects, sequences as Arrays, delegates as functions.</summary>
    public object ToAs(object v) => v switch
    {
        null => null,
        AsObject o => o,
        string s => s,
        bool b => Rt.Box(b),
        int i => Rt.Box(i),
        uint u => Rt.Uint(u),
        long l => l >= int.MinValue && l <= int.MaxValue ? Rt.Box((int)l) : (double)l,
        ulong ul => ul <= int.MaxValue ? Rt.Box((int)ul) : (double)ul,
        short sh => Rt.Box(sh),
        ushort us => Rt.Box(us),
        byte by => Rt.Box(by),
        float f => Rt.Num(f),
        double d => Rt.Num(d),
        IDictionary<string, object> dict => Obj(dict.Select(kv => (kv.Key, kv.Value)).ToArray()),
        Func<object[], object> fn => new AsPlainFunction(RT, new NativeMethod("host function", (s, a) => ToAs(fn(a)))),
        IEnumerable e => new AsArray(RT, e.Cast<object>().Select(ToAs).ToList()),
        _ => v,
    };
}
