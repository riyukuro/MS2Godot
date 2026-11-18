using System.Reflection;
using System.Text.Json.Nodes;
using Ms2.Swf.Abc;

namespace Ms2.Avm2
{
    public sealed partial class Runtime
    {
        /// <summary>The player running this runtime's movie (display list, frames, input); null for a bare runtime.</summary>
        public Flash.Player Player;
    }
}

namespace Ms2.Avm2.Flash
{
    /// <summary>Symbols of one movie (the main one or an imported library), with the classes linked to them.</summary>
    public sealed class SymbolScope
    {
        public string Key;
        public JsonObject Symbols;
        public JsonObject Layout;          // the movie layout (main) or library entry
        public readonly Dictionary<string, int> Classes = new();

        public JsonObject Get(int id) => Symbols?[id.ToString()] as JsonObject;
        public static RectD Rect(JsonObject b) => b == null ? RectD.None : new RectD(J.D(b["x"]) ?? 0, J.D(b["y"]) ?? 0, J.D(b["w"]) ?? 0, J.D(b["h"]) ?? 0);
        public static Mat Matrix(JsonNode m) => m is not JsonObject o ? Mat.Identity
            : new Mat(J.D(o["a"]) ?? 1, J.D(o["b"]) ?? 0, J.D(o["c"]) ?? 0, J.D(o["d"]) ?? 1, J.D(o["x"]) ?? 0, J.D(o["y"]) ?? 0);
        public static Cx Color(JsonNode c)
        {
            if (c is not JsonObject o || o["mul"] is not JsonArray mul || o["add"] is not JsonArray add) return Cx.Identity;
            return new(J.Dv(mul[0]), J.Dv(mul[1]), J.Dv(mul[2]), J.Dv(mul[3]), J.Dv(add[0]), J.Dv(add[1]), J.Dv(add[2]), J.Dv(add[3]));
        }
    }

    /// <summary>
    /// A headless Flash player over one movie layout: builds timeline display objects for the AS3 classes, runs frames
    /// (enterFrame, timeline advance, frame scripts, exitFrame), timers, mouse/keyboard input with focus, and the host bridge
    /// (ExternalInterface calls out, Invoke calls in). A renderer reads the display list from <see cref="Stage"/>.
    /// </summary>
    public sealed class Player
    {
        public readonly Runtime RT;
        public readonly JsonObject Layout;
        public AsStage Stage;
        public AsDisplayObject Root;
        /// <summary>The Loader the movie sits in (as the game hosts each dialog); its name is <see cref="HostId"/>.</summary>
        public AsLoader HostLoader;
        /// <summary>The dialog id the game gives the movie's loader (default: the movie name).</summary>
        public string HostId;
        /// <summary>Run as the game hosts the movie (authoring-only demo code off). False = as the Flash IDE would.</summary>
        public bool InGame = true;
        public double StageW, StageH, FrameRate = 30;
        public double MouseX, MouseY;
        public bool MouseDown;
        public object Focus;
        public AsDisplayObject ModalClip;
        public bool RenderInvalidated;
        public int FrameCount;

        /// <summary>ExternalInterface.call from the movie: (name, args) → result.</summary>
        public Func<string, object[], object> External;
        /// <summary>Functions the movie registered with ExternalInterface.addCallback.</summary>
        public readonly Dictionary<string, AsFunction> Callbacks = new();
        /// <summary>String-table lookup for texts that are keys ("$s_login_msg_id"); null = keep the key.</summary>
        public Func<string, string> Translate;
        /// <summary>Reads a URL the movie loads as text (XML tables); null = not found.</summary>
        public Func<string, string> ReadText;
        /// <summary>Loader.load: the host may put display objects into the loader (an image) or leave it empty.</summary>
        public Action<AsLoader, string> LoadUrl;

        public readonly SymbolScope Main;
        readonly Dictionary<string, (SymbolScope Scope, int Id)> classSymbols = new();
        readonly Dictionary<string, List<AsEventDispatcher>> broadcast = new();
        readonly List<AsTimer> timers = new();
        readonly List<Action> later = new();
        readonly List<AsMovieClip> scriptQueue = new();
        (SymbolScope Scope, JsonObject Sym, JsonObject Placement)? pending;

        static readonly Type[] FlashTypes = typeof(Player).Assembly.GetTypes().Where(t => t.GetCustomAttribute<AsClassAttribute>() != null).ToArray();

        /// <summary>Defines every [AsClass] type of this assembly (bases first) and the Scaleform natives.</summary>
        public static void InstallFlash(Runtime rt)
        {
            var byName = FlashTypes.ToDictionary(t => t.GetCustomAttribute<AsClassAttribute>().Name);
            var done = new HashSet<string>();
            void Def(string n)
            {
                if (done.Contains(n) || !byName.TryGetValue(n, out var t)) return;
                done.Add(n);
                var sup = t.GetCustomAttribute<AsClassAttribute>().Super;
                if (sup != null) Def(sup);
                rt.Bind(t);
            }
            foreach (var n in byName.Keys.OrderBy(k => k)) Def(n);
            ScaleformNatives.Install(rt);
        }

        /// <summary>Things Scaleform tolerates that Flash would throw on (missing frame labels ...), by count.</summary>
        public readonly Dictionary<string, int> Warnings = new();
        public void Warn(string w) { Warnings.TryGetValue(w, out var n); Warnings[w] = n + 1; }

        public Player(Runtime rt, JsonObject layout, IEnumerable<(string Key, JsonObject Layout)> imported = null)
        {
            RT = rt; Layout = layout;
            rt.Player = this;
            if (rt.GetClass("flash.display.Stage") == null) InstallFlash(rt);
            var st = layout["stage"] as JsonObject;
            StageW = J.D(st?["width"]) ?? 1024; StageH = J.D(st?["height"]) ?? 768;
            FrameRate = J.D(layout["fps"]) ?? 30;
            Main = new SymbolScope { Key = (string)layout["movie"], Symbols = layout["symbols"] as JsonObject, Layout = layout };
            if (layout["library"] is JsonObject lib)
                foreach (var (key, node) in lib)
                {
                    var sc = new SymbolScope { Key = key, Symbols = node["symbols"] as JsonObject, Layout = (JsonObject)node };
                    if (node["classes"] is JsonObject cl) foreach (var (cn, id) in cl) { sc.Classes[cn] = J.Iv(id); classSymbols.TryAdd(cn, (sc, J.Iv(id))); }
                }
            if (Main.Symbols != null)
                foreach (var (id, s) in Main.Symbols)
                    if ((string)s["class"] is string cn) { Main.Classes[cn] = int.Parse(id); classSymbols[cn] = (Main, int.Parse(id)); }
            // the imported movies' own symbols (shared libraries: every class they link, not only those this movie names)
            if (imported != null)
                foreach (var (key, il) in imported)
                {
                    if (il?["symbols"] is not JsonObject isyms) continue;
                    var sc = new SymbolScope { Key = key, Symbols = isyms, Layout = il };
                    foreach (var (id, s) in isyms)
                        foreach (var cn in new[] { (string)s["class"], (string)s["export"] })
                            if (cn != null) { sc.Classes[cn] = int.Parse(id); classSymbols.TryAdd(cn, (sc, int.Parse(id))); }
                }
            rt.AutoClass = AutoClass;
            rt.OnBeforeConstruct += BeforeConstruct;
            Stage = (AsStage)rt.GetClass("flash.display.Stage").Construct();
        }

        AsClass AutoClass(string pkg, string local)
        {
            var qn = pkg.Length == 0 ? local : pkg + "." + local;
            if (!classSymbols.TryGetValue(qn, out var cs) || cs.Scope.Get(cs.Id) is not JsonObject sym) return null;
            var baseName = (string)sym["kind"] switch
            {
                "button" => "flash.display.SimpleButton", "shape" => "flash.display.Shape", "text" => "flash.text.TextField",
                _ => "flash.display.MovieClip",
            };
            if (sym["bases"] is JsonArray ba && ba.Count > 0 && (string)ba[0] is string b0 && RT.GetClass(b0) is { } bc) return RT.NewNativeClass(qn, bc);
            return RT.NewNativeClass(qn, RT.GetClass(baseName));
        }

        // ---- construction ----------------------------------------------------------------------------------

        /// <summary>Constructs the document class (or a plain MovieClip) with the root timeline, on the stage. Returns false on an AS error.</summary>
        public bool Start()
        {
            return RT.Guard("construct root", () =>
            {
                // the movies carry an authoring switch (CommonPath.flash, true by default) that runs demo data when played
                // in the Flash IDE; running under the game it is off
                if (InGame && RT.GetClass("scaleform.ms2.common.CommonPath") is { } cp) RT.SetProperty(cp, Name.Public("flash"), Rt.False);
                // the game's shell movie builds the shared depth layers on the stage before any dialog loads (popups,
                // tooltips and modal canvases go into them)
                if (InGame && RT.GetClass("scaleform.ms2.common.CommonDepth") is { } cd)
                    RT.CallProperty(cd, Name.Public("init"), new object[] { Stage });
                // and starts the tooltip manager on the stage (Main.addedToStage: TooltipManager.init(stage)), so the dialogs'
                // items register their tooltips with it; started with CommonPath.flash already off it keeps the game's
                // MS2DataModel.getUserData instead of installing its authoring stand-in
                if (InGame && RT.GetClass("scaleform.ms2.manager.TooltipManager") is { } tm)
                    RT.CallProperty(tm, Name.Public("init"), new object[] { Stage });
                var rootSym = new JsonObject { ["kind"] = "sprite", ["timeline"] = Layout["root"]?.DeepClone() };
                var name = (string)Layout["documentClass"];
                var cls = name == null ? null : RT.GetClass(name);
                if (name != null && cls == null) RT.NoteMissing("document class " + name);
                cls ??= RT.GetClass("flash.display.MovieClip");
                // the game loads each dialog movie into a Loader named after the dialog (the id its ExternalInterface calls carry)
                HostLoader = (AsLoader)RT.GetClass("flash.display.Loader").Construct();
                HostLoader.InstanceName = HostId ?? (string)Layout["movie"];
                Stage.Children.Add(HostLoader); HostLoader.Parent = Stage;
                Root = (AsDisplayObject)ConstructInto(cls, HostLoader, Main, rootSym, null, 0);
                EnterStage(HostLoader);
                RunFrameScripts();
            });
        }

        /// <summary>Allocates a display object, puts it into the parent (timeline order), builds its frame 1, then runs its constructor.</summary>
        AsObject ConstructInto(AsClass cls, AsContainer parent, SymbolScope scope, JsonObject sym, JsonObject pl, int depth)
        {
            var o = (cls.Allocator ?? (_ => new AsObject()))(cls);
            o.Init(cls);
            if (o is AsDisplayObject d)
            {
                d.Placement = pl; d.Depth = depth;
                if (pl != null) ApplyPlacement(d, pl, initial: true);
                if (parent != null) { if (depth == 0 && pl == null) parent.Children.Add(d); else parent.InsertByDepth(d); d.Parent = parent; }
            }
            pending = (scope, sym, pl);
            RT.BeforeConstruct(o);
            pending = null;
            // like the Flash/Scaleform players: an uncaught error in a display object's constructor is reported and the
            // instance stays where it was placed (the rest of its constructor does not run)
            if (cls.Ctor != null) RT.Guard("construct " + cls.Name, () => cls.Ctor.Invoke(o, Array.Empty<object>()));
            RT.AfterConstruct(o);
            return o;
        }

        void BeforeConstruct(AsObject o)
        {
            if (o is not AsDisplayObject d || d.Symbol != null) return;
            SymbolScope scope; JsonObject sym;
            if (pending is { } p && p.Sym != null) { scope = p.Scope; sym = p.Sym; }
            else if (classSymbols.TryGetValue(o.Class.Name, out var cs)) { scope = cs.Scope; sym = scope.Get(cs.Id); }
            else return;
            pending = null;
            d.Symbol = sym; d.Scope = scope;
            switch (d)
            {
                case AsMovieClip mc when sym["timeline"] is JsonObject tl:
                    mc.TL = Timeline.From(tl, scope);
                    BuildFrame(mc, 0);
                    scriptQueue.Add(mc);
                    break;
                case AsTextField tf: tf.FromSymbol(sym); break;
                case AsSimpleButton sb: BuildButton(sb, sym, scope); break;
            }
        }

        AsClass ClassForPlacement(JsonObject pl, SymbolScope scope, out JsonObject sym, out SymbolScope symScope)
        {
            sym = null; symScope = scope;
            string cname = (string)pl["class"];
            if (pl["id"] is JsonNode idn) sym = scope.Get(J.Iv(idn));
            else if (cname != null && classSymbols.TryGetValue(cname, out var cs)) { symScope = cs.Scope; sym = cs.Scope.Get(cs.Id); }
            var kind = (string)sym?["kind"] ?? (string)pl["kind"];
            cname ??= (string)sym?["class"];
            if (cname != null)
            {
                var c = RT.GetClass(cname);
                if (c != null) return c;
                RT.NoteMissing("class " + cname);
            }
            return RT.GetClass(kind switch
            {
                "shape" => "flash.display.Shape", "text" => "flash.text.TextField", "staticText" => "flash.text.StaticText",
                "button" => "flash.display.SimpleButton", "morph" => "flash.display.MorphShape", _ => "flash.display.MovieClip",
            });
        }

        AsDisplayObject Instantiate(AsContainer parent, JsonObject pl, SymbolScope scope)
        {
            if (pl["id"] == null && (string)pl["class"] is string img && System.Text.RegularExpressions.Regex.IsMatch(img, @"\.(png|dds|jpe?g|bmp|tga)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                // an image placed by its file name (a bitmap the movie imports from a shared library)
                var size = ImageSize?.Invoke(img) ?? (0, 0);
                var bm = NewBitmap(img, size.W, size.H);
                bm.Placement = pl; bm.Depth = J.Iv(pl["depth"]); ApplyPlacement(bm, pl, initial: true);
                parent.InsertByDepth(bm);
                if ((string)pl["name"] is string bn && bn.Length > 0) { bm.InstanceName = bn; BindName(parent, bn, bm); }
                return bm;
            }
            var cls = ClassForPlacement(pl, scope, out var sym, out var symScope);
            var o = ConstructInto(cls, parent, symScope, sym ?? new JsonObject { ["kind"] = "missing" }, pl, J.I(pl["depth"]) ?? 0) as AsDisplayObject;
            if (o == null) return null;
            if ((string)pl["name"] is string nm && nm.Length > 0)
            {
                o.InstanceName = nm;
                BindName(parent, nm, o);
            }
            return o;
        }

        void BindName(AsContainer parent, string nm, object v)
        {
            if (parent.Traits?.FindPublic(nm) is { } b && (b.Kind == BindingKind.Slot || b.Kind == BindingKind.Accessor && b.Setter != null))
                RT.SetProperty(parent, Name.Public(nm), v);
            else if (parent.IsDynamic && parent is not AsStage) parent.SetDynamic(nm, v);
        }

        static readonly string[] BlendNames = { "normal", "normal", "layer", "multiply", "screen", "lighten", "darken", "difference", "add", "subtract", "invert", "alpha", "erase", "overlay", "hardlight" };

        static void ApplyPlacement(AsDisplayObject d, JsonObject pl, bool initial)
        {
            if (initial || !d.MatrixLocked)
            {
                if (pl["matrix"] is JsonObject m) d.Matrix = SymbolScope.Matrix(m);
                // placed again (a rewind to this placement): Scaleform moves the object it already has there to the tag's
                // position (AvmSprite::AddDisplayObject -> DisplayList::MoveDisplayObject, sfr_as3_16z.cpp,
                // sfr_gfx_06c.cpp:291), which sets the matrix whenever the tag has one, the identity included
                else if (!initial && (bool?)pl["hasMatrix"] == true) d.Matrix = Mat.Identity;
            }
            if (initial || !d.ColorLocked)
            {
                if (pl["color"] is JsonObject c) d.Color = SymbolScope.Color(c);
                // the same move sets a tag without a colour transform to the identity one (AvmSprite::AddDisplayObject)
                else if (!initial) d.Color = Cx.Identity;
            }
            // only the placement sets the visible flag; a timeline move does not (DisplayList::MoveDisplayObject,
            // sfr_gfx_06c.cpp:291), so a script's visible stays
            if (initial && pl["visible"] is JsonNode v) d.IsVisible = (bool)v;
            if (initial)
            {
                if (J.I(pl["clipDepth"]) is int cd) d.ClipDepth = cd;
                if (pl["filters"] is JsonArray fl) d.PlaceFilters = fl;
                if (pl["blend"] is JsonValue bv) d.Blend = bv.TryGetValue<string>(out var bs) ? bs : BlendNames[Math.Clamp(J.Iv(bv), 0, BlendNames.Length - 1)];
            }
        }

        void BuildButton(AsSimpleButton sb, JsonObject sym, SymbolScope scope)
        {
            if (sym["states"] is not JsonArray states) return;
            AsDisplayObject StateClip(Func<JsonObject, bool> want)
            {
                var holder = (AsSprite)RT.GetClass("flash.display.Sprite").Construct();
                foreach (JsonObject s in states)
                    if (want(s)) Instantiate(holder, new JsonObject { ["id"] = J.Iv(s["id"]), ["depth"] = J.Iv(s["depth"]), ["matrix"] = s["matrix"]?.DeepClone(), ["color"] = s["color"]?.DeepClone() }, scope);
                return holder;
            }
            sb.upState = StateClip(s => (bool?)s["up"] == true);
            sb.overState = StateClip(s => (bool?)s["over"] == true);
            sb.downState = StateClip(s => (bool?)s["down"] == true);
            sb.hitTestState = StateClip(s => (bool?)s["hit"] == true);
        }

        // ---- timelines ---------------------------------------------------------------------------------------

        static bool AliveAt(JsonObject pl, int f) => J.Iv(pl["first"]) <= f && (pl["last"] is not JsonNode l || f <= J.Iv(l));

        /// <summary>Makes the timeline children match frame <paramref name="f"/> (creates, moves, removes).</summary>
        void BuildFrame(AsMovieClip mc, int f)
        {
            mc.Frame = f;
            if (mc.TL == null) return;
            foreach (var pl in mc.TL.Placements)
            {
                bool alive = AliveAt(pl, f);
                mc.Live.TryGetValue(pl, out var cur);
                if (alive && cur == null)
                {
                    var o = Instantiate(mc, pl, mc.TL.Scope);
                    if (o != null) { mc.Live[pl] = o; ApplyMoves(o, pl, f); if (mc.OnStage) EnterStage(o); }
                }
                else if (alive) ApplyMoves(cur, pl, f);
                else if (cur != null)
                {
                    mc.Live.Remove(pl);
                    if (cur.Parent == mc) mc.RemoveChildInternal(cur, true);
                }
            }
        }

        static void ApplyMoves(AsDisplayObject o, JsonObject pl, int f)
        {
            if (pl["moves"] is not JsonArray mv) return;
            JsonObject last = null;
            foreach (JsonObject m in mv) if (J.Iv(m["frame"]) <= f) last = m;
            if (last == null) { ApplyPlacement(o, pl, initial: false); return; }
            // PlaceObject2 with PlaceFlagHasMatrix: the layout writes an identity matrix as null (LayoutJson Mx), so null = identity here
            if (!o.MatrixLocked && (bool?)last["hasMatrix"] != false) o.Matrix = SymbolScope.Matrix(last["matrix"]);
            if (!o.ColorLocked && last["color"] is JsonObject c) o.Color = SymbolScope.Color(c);
        }

        /// <summary>gotoAndStop/Play, nextFrame: changes the frame now and runs its frame script.</summary>
        public void GotoFrame(AsMovieClip mc, int f, bool fromScript)
        {
            if (f == mc.Frame) return;
            if (f < mc.Frame)
            {
                // going back: placements first shown after the target frame go; ones shown before it are kept or rebuilt
                foreach (var (pl, o) in mc.Live.ToList())
                    if (!AliveAt(pl, f) || J.Iv(pl["first"]) > f) { mc.Live.Remove(pl); if (o.Parent == mc) mc.RemoveChildInternal(o, true); }
            }
            BuildFrame(mc, f);
            if (fromScript) RunFrameScript(mc);
        }

        void RunFrameScript(AsMovieClip mc)
        {
            if (mc.LastScriptFrame == mc.Frame) return;
            mc.LastScriptFrame = mc.Frame;
            if (mc.FrameScripts.TryGetValue(mc.Frame, out var fn)) fn.Call(mc, Array.Empty<object>());
        }

        void RunFrameScripts()
        {
            for (int i = 0; i < scriptQueue.Count; i++)
            {
                var mc = scriptQueue[i];
                RT.Guard($"frame script {mc.Class.Name}@{mc.Frame + 1}", () => RunFrameScript(mc));
            }
            scriptQueue.Clear();
        }

        // ---- stage membership --------------------------------------------------------------------------------

        void EnterStage(AsDisplayObject o)
        {
            o.Send("flash.events.Event", "addedToStage");
            if (o is AsContainer c) foreach (var ch in c.Children.ToList()) EnterStage(ch);
        }

        internal void AddedToStage(AsDisplayObject o) => RT.Guard("addedToStage", () => EnterStage(o));

        internal void RemovedFromStage(AsDisplayObject o)
        {
            void Leave(AsDisplayObject x)
            {
                x.Send("flash.events.Event", "removedFromStage");
                if (x is AsContainer c) foreach (var ch in c.Children.ToList()) Leave(ch);
            }
            RT.Guard("removedFromStage", () => Leave(o));
        }

        // ---- frame loop --------------------------------------------------------------------------------------

        internal void Broadcast(string type, AsEventDispatcher d, bool add)
        {
            if (!broadcast.TryGetValue(type, out var l)) broadcast[type] = l = new();
            if (add) { if (!l.Contains(d)) l.Add(d); } else l.Remove(d);
        }

        void SendBroadcast(string type)
        {
            if (!broadcast.TryGetValue(type, out var l)) return;
            foreach (var d in l.ToArray())
                RT.Guard(type + " " + d.Class?.Name, () =>
                {
                    var e = (AsEvent)RT.GetClass("flash.events.Event").Construct(type, Rt.False);
                    e.Dispatched = true; e.Target = d; e.Phase = 2;
                    d.Fire(e, false);
                });
        }

        internal void AddTimer(AsTimer t) { if (!timers.Contains(t)) timers.Add(t); }
        internal void RemoveTimer(AsTimer t) => timers.Remove(t);
        public void Later(Action a) => later.Add(a);

        /// <summary>One frame: deferred loads, enterFrame, timeline advance, frame scripts, exitFrame, timers and timeouts, render.</summary>
        public void Tick()
        {
            FrameCount++;
            var todo = later.ToArray(); later.Clear();
            foreach (var a in todo) RT.Guard("deferred", a);
            SendBroadcast("enterFrame");
            RT.Guard("advance", () => Advance(Stage));
            SendBroadcast("frameConstructed");
            RunFrameScripts();
            SendBroadcast("exitFrame");
            double dt = 1000.0 / FrameRate;
            RT.Guard("clock", () => RT.AdvanceClock(dt));
            foreach (var t in timers.ToArray()) RT.Guard("timer", t.Pump);
            if (RenderInvalidated) { RenderInvalidated = false; SendBroadcast("render"); }
        }

        void Advance(AsDisplayObject o)
        {
            if (o is AsMovieClip mc && mc.Playing && mc.TL != null && mc.totalFrames > 1)
            {
                int next = (mc.Frame + 1) % mc.totalFrames;
                if (next == 0)
                {
                    foreach (var (pl, ch) in mc.Live.ToList())
                        if (J.Iv(pl["first"]) > 0) { mc.Live.Remove(pl); if (ch.Parent == mc) mc.RemoveChildInternal(ch, true); }
                }
                BuildFrame(mc, next);
                scriptQueue.Add(mc);
            }
            if (o is AsContainer c) foreach (var ch in c.Children.ToArray()) Advance(ch);
        }

        // ---- host bridge -------------------------------------------------------------------------------------

        /// <summary>The C++ side's Invoke("path.method", args): calls a method on the root (or a dotted path below it), or a registered callback.</summary>
        public object Invoke(string path, params object[] args)
        {
            object result = Rt.Undef;
            RT.Guard("invoke " + path, () =>
            {
                if (Callbacks.TryGetValue(path, out var cb)) { result = cb.Call(null, args); return; }
                object target = Root;
                var parts = path.Split('.');
                for (int i = 0; i < parts.Length - 1 && target != null; i++) target = RT.GetProperty(target, Name.Public(parts[i]));
                if (Rt.IsNullish(target)) { RT.NoteMissing("invoke target " + path); return; }
                var f = RT.GetProperty(target, Name.Public(parts[^1]));
                if (f is not AsFunction fn) { RT.NoteMissing("invoke " + path); return; }
                result = fn.Call(target, args);
            });
            return result;
        }

        /// <summary>The first display object (root first, depth first) whose class has the method: the object the game's
        /// dialog class drives (its m_30), e.g. the one with "onEnterKey" for the login dialog.</summary>
        public AsDisplayObject FindOwner(string method)
        {
            var n = Name.Public(method);
            AsDisplayObject Walk(AsDisplayObject o)
            {
                if (o.Traits?.Find(n) is { Kind: BindingKind.Method }) return o;
                if (o is AsContainer c) foreach (var k in c.Children) if (Walk(k) is { } r) return r;
                return null;
            }
            return Root == null ? null : Walk(Root);
        }

        /// <summary>Calls a method on an AS object the way GFx::Value::Invoke does (errors recorded, undefined on failure).</summary>
        public object Call(object target, string method, params object[] args)
        {
            object result = Rt.Undef;
            RT.Guard("invoke " + method, () => result = RT.CallProperty(target, Name.Public(method), args));
            return result;
        }

        /// <summary>A plain AS object with the given members (the ASObj the C++ side builds for Invoke arguments).</summary>
        public AsObject NewObject(params (string Key, object Value)[] members)
        {
            var o = new AsObject(RT.ObjectClass);
            foreach (var (k, v) in members) o.SetDynamic(k, Runtime.ToAs(v));
            return o;
        }

        /// <summary>Clicks at a stage point: move, press, release, as the mouse would.</summary>
        public void Click(double x, double y) { MouseMove(x, y); MouseButton(true); MouseButton(false); }

        public object Get(string path)
        {
            object target = Root;
            foreach (var p in path.Split('.')) { if (Rt.IsNullish(target)) return Rt.Undef; target = RT.GetProperty(target, Name.Public(p)); }
            return target;
        }

        AsLoaderInfo rootInfo;
        internal AsObject LoaderInfoFor(AsDisplayObject d)
        {
            if (d is AsStage) return null;
            if (rootInfo == null)
            {
                rootInfo = (AsLoaderInfo)RT.GetClass("flash.display.LoaderInfo").Construct();
                rootInfo.url = (string)Layout["movie"] + ".swf";
                rootInfo.parameters = new AsObject(RT.ObjectClass);
                rootInfo.bytesLoaded = rootInfo.bytesTotal = 1;
            }
            rootInfo.content = Root; rootInfo.loader = HostLoader;
            return rootInfo;
        }

        /// <summary>Size of an image the movie loads by URL (icons, portraits); null = not found (the load fails with ioError).
        /// Without a resolver every load succeeds with a 0x0 image.</summary>
        public Func<string, (int W, int H)?> ImageSize;

        /// <summary>(movie key, export name) → the size of the image a movie's IMG tag url names (MovieImpl::FindExportedResource
        /// from that movie); null = no image by that name.</summary>
        public Func<string, string, (double W, double H)?> ExportedImageSize;

        /// <summary>A Bitmap standing for an image file; the renderer draws it from <see cref="AsBitmapData.Source"/>.</summary>
        public AsBitmap NewBitmap(string url, int w, int h)
        {
            var bd = (AsBitmapData)RT.GetClass("flash.display.BitmapData").Construct(Rt.Box(w), Rt.Box(h));
            bd.Source = url;
            return (AsBitmap)RT.GetClass("flash.display.Bitmap").Construct(bd);
        }

        internal void LoadInto(AsLoader l, string url)
        {
            // Only the "simg://" and "simgps://" protocols load inside load() (LoaderImpl::IsProtocolImage sets sync for
            // the 's' prefix only, sfr_gfx_02f.cpp:1046-1074; MovieRoot::AddNewLoadQueueEntry processes those at once,
            // sfr_as3_16.cpp:2162-2183). Everything else, "img://" included, goes on the load queue and fires its
            // init / complete events when MovieRoot::ProcessLoadQueueEntry runs it (sfr_as3_18.cpp:8314), so a listener
            // added after load() still hears them.
            bool sync = url != null && (url.StartsWith("simg://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("simgps://", StringComparison.OrdinalIgnoreCase));
            if (sync) Finish(); else Later(Finish);
            void Finish()
            {
                LoadUrl?.Invoke(l, url);
                if (l.Children.Count == 0 && url != null && !url.EndsWith(".swf", StringComparison.OrdinalIgnoreCase) && !url.EndsWith(".gfx", StringComparison.OrdinalIgnoreCase))
                {
                    var size = ImageSize == null ? (0, 0) : ImageSize(url);
                    if (size is { } sz) { var bm = NewBitmap(url, sz.W, sz.H); l.Children.Add(bm); bm.Parent = l; }
                }
                if (l.Children.Count == 0) { l.contentLoaderInfo.Send("flash.events.IOErrorEvent", "ioError"); return; }
                l.contentLoaderInfo.content = l.Children.FirstOrDefault();
                l.contentLoaderInfo.Send("flash.events.Event", "init");
                l.contentLoaderInfo.Send("flash.events.Event", "complete");
            }
        }

        // ---- input -------------------------------------------------------------------------------------------

        /// <summary>The topmost display object under a stage point (hit areas, mouseEnabled and hit-test-disabled honoured).</summary>
        public AsDisplayObject TopMost(double x, double y, bool testAll)
        {
            AsDisplayObject Hit(AsDisplayObject o)
            {
                if (!o.IsVisible || o.HitTestDisabled || (o is AsSprite s0 && s0.IsHitAreaOf != null)) return null;
                if (o.MaskObject != null && !o.MaskObject.GetBounds(null).Contains(x, y)) return null;
                if (o is AsSprite sp && sp.HitAreaObject != null)
                {
                    var ha = sp.HitAreaObject;
                    return ha.GetBounds(null).Contains(x, y) ? o : null;
                }
                if (o is AsContainer c)
                {
                    var kids = c.Children;
                    for (int i = kids.Count - 1; i >= 0; i--)
                    {
                        var k = kids[i];
                        if (k.ClipDepth != null) continue;
                        var h = Hit(k);
                        if (h != null) return h;
                    }
                }
                return o.HitsContent(x, y) ? o : null;
            }
            return Hit(Stage);
        }

        // ---- GetTopMostEntity (the mouse entity under a point) ----------------------------------------------------------

        enum TopMostResult { FoundNothing, Found, Continue }

        /// <summary>DisplayObjectBase::TopMostDescr: the result, the object to skip and whether every object counts (not
        /// only those that take mouse events).</summary>
        sealed class TopMostDescr
        {
            public AsInteractiveObject Result;
            public AsDisplayObject IgnoreMC;
            public bool TestAll;
            public TopMostDescr Copy() => (TopMostDescr)MemberwiseClone();
        }

        /// <summary>
        /// MovieImpl::GetTopMostEntity (sfr_gfx_01i.cpp:3317): the topmost-level objects first, the last one on top, then
        /// the level (the Stage). Points stay in stage coordinates here (the client hands each object a point in its
        /// parent's space). The topmost-level objects are taken in display-list order; the client keeps them in the order
        /// they were made topmost. IsFocusAllowed (focus groups per controller) is not modelled: one controller, all allowed.
        /// </summary>
        public AsInteractiveObject GetTopMostEntity(double x, double y, bool testAll, AsDisplayObject ignoreMC)
        {
            var topmost = new List<AsDisplayObject>();
            void Collect(AsDisplayObject o)
            {
                if (o.TopmostLevel > 0 && o != Stage) topmost.Add(o);
                if (o is AsContainer c) foreach (var k in c.Children) Collect(k);
            }
            Collect(Stage);
            for (int i = topmost.Count - 1; i >= 0; i--)
            {
                var d = new TopMostDescr { IgnoreMC = ignoreMC, TestAll = testAll };
                if (TopMostMouseEntity(topmost[i], x, y, d) == TopMostResult.Found) return d.Result;
            }
            var ld = new TopMostDescr { IgnoreMC = ignoreMC, TestAll = testAll };
            return TopMostMouseEntity(Stage, x, y, ld) == TopMostResult.Found ? ld.Result : null;
        }

        /// <summary>The GetTopMostMouseEntity override of the object's kind.</summary>
        TopMostResult TopMostMouseEntity(AsDisplayObject o, double gx, double gy, TopMostDescr d) => o switch
        {
            AsSprite sp => SpriteTopMost(sp, gx, gy, d),
            AsContainer c => ContainerTopMost(c, gx, gy, d),
            AsTextField tf => TextFieldTopMost(tf, gx, gy, d),
            AsSimpleButton b => ButtonTopMost(b, gx, gy, d),
            AsBitmap bm => BitmapTopMost(bm, gx, gy, d),
            AsShape sh => ShapeTopMost(sh, gx, gy, d),
            AsStaticText or AsMorphShape => DefTopMost(o, gx, gy, d),
            _ => Nothing(d),                                    // DisplayObjectBase::GetTopMostMouseEntity (sfr_as3_18.cpp:8791)
        };

        static TopMostResult Nothing(TopMostDescr d) { d.Result = null; return TopMostResult.FoundNothing; }

        /// <summary>AvmInteractiveObj / AvmSprite / AvmDisplayObjContainer::ActsAsButton (sfr_as3_16.cpp:2925-2946): a
        /// sprite in button mode or with a button listener (EventDispatcher::HasButtonHandlers, sfr_as3_09.cpp:880); any
        /// other container never; another interactive object when it has a button listener.</summary>
        static bool ActsAsButton(AsDisplayObject o) => o switch
        {
            AsSprite sp => sp.buttonMode || HasButtonHandlers(sp),
            AsContainer => false,
            AsInteractiveObject io => HasButtonHandlers(io),
            _ => false,
        };

        /// <summary>IsButtonEventInl (sfr_as3_09.cpp:990): the mouse events counted as button handlers.</summary>
        static readonly string[] ButtonEvents = { "mouseDown", "mouseUp", "click", "doubleClick", "mouseOver", "mouseOut", "rollOver", "rollOut" };

        static bool HasButtonHandlers(AsEventDispatcher e)
        {
            foreach (var t in ButtonEvents) if (e.hasEventListener(t)) return true;
            return false;
        }

        /// <summary>DisplayObject::TransformPointToLocalAndCheckBounds (sfr_gfx_06.cpp:1831): with a scroll rect, the point
        /// must fall inside it.</summary>
        static bool LocalAndCheckBounds(AsDisplayObject o, double gx, double gy, out double lx, out double ly)
        {
            (lx, ly) = o.GlobalToLocal(gx, gy);
            if (o.ScrollRect is { } r && (lx < 0 || ly < 0 || lx > r.W || ly > r.H)) return false;
            return true;
        }

        /// <summary>DisplayObjContainer::GetTopMostMouseEntity (sfr_gfx_06d.cpp:298), AVM2.</summary>
        TopMostResult ContainerTopMost(AsContainer c, double gx, double gy, TopMostDescr d)
        {
            var holder = (c as AsSprite)?.IsHitAreaOf;
            // hidden containers take no mouse events unless they are a hit area; masks never do
            if (c.HitTestDisabled || (!c.IsVisible && holder == null) || c.MaskOwner != null) return Nothing(d);
            if (d.IgnoreMC == c) return Nothing(d);
            if (!LocalAndCheckBounds(c, gx, gy, out _, out _)) return TopMostResult.FoundNothing;
            if (c.MaskObject is { } mask && mask.MaskOwner == c && !PointTest(mask, gx, gy, true, false)) return Nothing(d);

            // CalcDisplayListHitTestMaskArray: a clip-depth mask decides for the children up to its clip depth
            var kids = c.Children;
            int n = kids.Count;
            bool[] hit = null;
            for (int i = 0; i < n; i++)
            {
                if (kids[i].ClipDepth is not { } clip || clip <= 0) continue;
                if (hit == null) { hit = new bool[n]; Array.Fill(hit, true); }
                hit[i] = PointTest(kids[i], gx, gy, true, false);
                int k = i + 1;
                for (; k < n && kids[k].Depth <= clip; k++) hit[k] = hit[i];
                i = k - 1;
            }

            TopMostDescr saved = null;
            bool foundButMouseDisabled = false;
            for (int i = n - 1; i >= 0; i--)
            {
                var ch = kids[i];
                if (hit != null && (!hit[i] || ch.ClipDepth is > 0)) continue;
                if (ch.TopmostLevel > 0) continue;
                var te = TopMostMouseEntity(ch, gx, gy, d);
                if (te == TopMostResult.Found)
                {
                    if (!c.mouseChildren) d.Result = c;
                    if (d.Result != null && !d.Result.mouseEnabled)
                    {
                        d.Result = c;
                        foundButMouseDisabled = true;
                        continue;
                    }
                }
                else if (te == TopMostResult.Continue && d.Result != null)
                    saved = d.Copy();

                // a hit in button mode is ours (or the hit area holder's)
                if (ActsAsButton(c) || (holder != null && ActsAsButton(holder)))
                {
                    if (te == TopMostResult.Found || saved != null)
                    {
                        if (holder != null && ActsAsButton(holder)) { d.Result = holder; return TopMostResult.Found; }
                        // AS3: the hit area does not count when a child interactive object was hit
                        if (c is AsSprite { HitAreaObject: { } ha } && (d.Result == null || d.Result == c))
                            return TopMostMouseEntity(ha, gx, gy, d);
                    }
                    if (te == TopMostResult.Found && d.TestAll) return te;
                }
                else
                {
                    if (te == TopMostResult.Found && d.TestAll) return te;
                    if (te == TopMostResult.Found && d.Result != c)
                    {
                        if (d.Result.Parent != null && d.Result.Parent.IsVisible) return te;
                        return Nothing(d);
                    }
                }
            }
            if (saved != null)
            {
                d.Result = saved.Result;
                return TopMostResult.Found;
            }
            if (foundButMouseDisabled) { d.Result = c; return TopMostResult.Found; }
            d.Result = null;
            return TopMostResult.Continue;
        }

        /// <summary>Sprite::GetTopMostMouseEntity (sfr_gfx_05b.cpp:1350): the container's, then the sprite's own drawing
        /// for a button-mode sprite (or its hit area holder's) or when every object counts.</summary>
        TopMostResult SpriteTopMost(AsSprite sp, double gx, double gy, TopMostDescr d)
        {
            var res = ContainerTopMost(sp, gx, gy, d);
            if (res != TopMostResult.Continue || sp.GraphicsOrNull is not { } g) return res;
            var holder = sp.IsHitAreaOf;
            if (!(ActsAsButton(sp) || d.TestAll || (holder != null && ActsAsButton(holder)))) return res;
            var (lx, ly) = sp.GlobalToLocal(gx, gy);
            if (!DrawingHit(g, lx, ly, true)) return res;
            if (holder != null && ActsAsButton(holder)) { d.Result = holder; return TopMostResult.Found; }
            if (sp.HitAreaObject is { } ha)
            {
                if (d.Result == null || d.Result == sp || d.Result is not AsInteractiveObject)
                    return TopMostMouseEntity(ha, gx, gy, d);
                return res;
            }
            d.Result = sp;
            return TopMostResult.Found;
        }

        /// <summary>
        /// TextField::GetTopMostMouseEntity (sfr_gfx_03e.cpp:1312): a visible field under the point is the result when
        /// every object counts or it is selectable; else the nearest parent sprite that acts as a button (or any, when every
        /// object counts). The url test of a non-selectable html field is not modelled.
        /// </summary>
        static TopMostResult TextFieldTopMost(AsTextField tf, double gx, double gy, TopMostDescr d)
        {
            d.Result = null;
            if (tf.HitTestDisabled || !tf.IsVisible || d.IgnoreMC == tf) return TopMostResult.FoundNothing;
            if (tf.ClipDepth is null or 0 && tf.HitsContent(gx, gy))
            {
                if (d.TestAll || tf.selectable) { d.Result = tf; return TopMostResult.Found; }
                for (var p = tf.Parent; p is AsSprite; p = p.Parent)
                    if ((d.TestAll || ActsAsButton(p)) && (d.IgnoreMC == null || p != d.IgnoreMC))
                    {
                        d.Result = (AsInteractiveObject)p;
                        return TopMostResult.Found;
                    }
            }
            d.Result = null;
            return TopMostResult.Continue;
        }

        /// <summary>Button::GetTopMostMouseEntity (sfr_gfx_05c.cpp:241): the button when its hit state is under the point.</summary>
        static TopMostResult ButtonTopMost(AsSimpleButton b, double gx, double gy, TopMostDescr d)
        {
            d.Result = null;
            if (!b.IsVisible || d.IgnoreMC == b) return TopMostResult.FoundNothing;
            if (!LocalAndCheckBounds(b, gx, gy, out _, out _)) return TopMostResult.FoundNothing;
            if (b.hitTestState != null && b.HitsContent(gx, gy)) { d.Result = b; return TopMostResult.Found; }
            return TopMostResult.Continue;
        }

        /// <summary>AvmBitmap::GetTopMostMouseEntity (sfr_as3_18.cpp:5881): a visible bitmap is hit inside its image's
        /// rectangle; the result is its parent.</summary>
        static TopMostResult BitmapTopMost(AsBitmap bm, double gx, double gy, TopMostDescr d)
        {
            if (!bm.IsVisible || bm.bitmapData == null) return TopMostResult.FoundNothing;
            var (lx, ly) = bm.GlobalToLocal(gx, gy);
            if (!new RectD(0, 0, bm.bitmapData.width, bm.bitmapData.height).Contains(lx, ly)) return TopMostResult.FoundNothing;
            d.Result = bm.Parent as AsInteractiveObject;
            return TopMostResult.Found;
        }

        /// <summary>ShapeObject::GetTopMostMouseEntity (sfr_as3_18.cpp:3452): a script drawing hit gives the parent; a
        /// shape from the movie data goes through GetTopMostMouseEntityDef and continues.</summary>
        static TopMostResult ShapeTopMost(AsShape sh, double gx, double gy, TopMostDescr d)
        {
            if (sh.GraphicsOrNull is { Ops.Count: > 0 } g)
            {
                if (LocalAndCheckBounds(sh, gx, gy, out var lx, out var ly) && DrawingHit(g, lx, ly, true))
                {
                    d.Result = sh.Parent as AsInteractiveObject;
                    return TopMostResult.Found;
                }
                return TopMostResult.FoundNothing;
            }
            d.Result = TopMostMouseEntityDef(sh, gx, gy, d.TestAll, d.IgnoreMC);
            return d.Result != null ? TopMostResult.Continue : TopMostResult.FoundNothing;
        }

        /// <summary>StaticTextCharacter::GetTopMostMouseEntity (sfr_gfx_05d.cpp:544), also used for a morph shape: the
        /// definition's test, found when it gives a sprite.</summary>
        static TopMostResult DefTopMost(AsDisplayObject o, double gx, double gy, TopMostDescr d)
        {
            d.Result = TopMostMouseEntityDef(o, gx, gy, d.TestAll, d.IgnoreMC);
            return d.Result != null ? TopMostResult.Found : TopMostResult.FoundNothing;
        }

        /// <summary>
        /// DisplayObjectBase::GetTopMostMouseEntityDef (sfr_gfx_06.cpp:599): when the point is inside the visible,
        /// non-mask character's shape, the nearest parent sprite in button mode (or whose hit area holder is), or any parent
        /// sprite when every object counts, other than the ignored one.
        /// </summary>
        static AsInteractiveObject TopMostMouseEntityDef(AsDisplayObject o, double gx, double gy, bool testAll, AsDisplayObject ignoreMC)
        {
            if (!o.IsVisible) return null;
            var (lx, ly) = o.GlobalToLocal(gx, gy);
            if (o.ClipDepth is not (null or 0) || !ShapeDefHit(o.Symbol, lx, ly, true)) return null;
            for (var p = o.Parent; p is AsSprite spr; p = p.Parent)
                if (testAll || ActsAsButton(spr) || (spr.IsHitAreaOf is { } h && ActsAsButton(h)))
                    if (ignoreMC == null || ignoreMC != spr) return spr;
            return null;
        }

        /// <summary>
        /// MovieImpl::HitTest (0x142570a00) with HitTest_ShapesNoInvisible, the test CUIService::FUN_141aec760 (uiservice.cpp:2905)
        /// makes at the cursor: the level-0 clip (the Stage in AS3, sfr_as3_18.cpp:4006) is point-tested by its shapes, invisible
        /// clips skipped. True when the point is on something the movie draws, which keeps the mouse from the game.
        /// </summary>
        public bool HitTestShapes(double x, double y)
        {
            if (!Stage.GetBounds(null).Contains(x, y)) return false;
            return PointTest(Stage, x, y, true, true);
        }

        /// <summary>
        /// PointTestLocal of the display object at a stage point. A container (DisplayObjContainer::PointTestLocal 0x1425fcfa0,
        /// Sprite 0x1425dc2e0): not when hit-test disabled, outside its bounds (unless it has a scale-9 grid) or invisible (when
        /// asked); its script mask must hit; the children are tested topmost first, a clip-depth mask deciding for the children up to
        /// its clip depth (CalcDisplayListHitTestMaskArray 0x1425fbff0, the mask itself tested by shape) and never hit itself; then a
        /// sprite's drawing. A shape (ShapeObject 0x14251cac0) hits on its drawing or its definition's fills; a text field
        /// (TextField 0x1425ba750) on its view rectangle; a bitmap (AvmBitmap 0x14251c9f0) on its image rectangle.
        /// </summary>
        static bool PointTest(AsDisplayObject o, double gx, double gy, bool shapes, bool ignoreInvisible)
        {
            if (o.HitTestDisabled) return false;
            var (lx, ly) = o.GlobalToLocal(gx, gy);
            if (o is AsContainer c)
            {
                if (o.Symbol?["scale9"] == null && !o.LocalBounds().Contains(lx, ly)) return false;
                if (ignoreInvisible && !o.IsVisible) return false;
                if (o.MaskObject is { } m && !PointTest(m, gx, gy, shapes, ignoreInvisible)) return false;
                var kids = c.Children;
                int n = kids.Count;
                bool[] hit = null;
                for (int i = 0; i < n; i++)
                {
                    if (kids[i].ClipDepth is not { } clip || clip <= 0) continue;
                    if (hit == null) { hit = new bool[n]; Array.Fill(hit, true); }
                    hit[i] = PointTest(kids[i], gx, gy, true, false);
                    int k = i + 1;
                    for (; k < n && kids[k].Depth <= clip; k++) hit[k] = hit[i];
                    i = k - 1;
                }
                for (int i = n - 1; i >= 0; i--)
                {
                    var k = kids[i];
                    if (ignoreInvisible && !k.IsVisible) continue;
                    if (hit != null && (!hit[i] || k.ClipDepth is > 0)) continue;
                    if (PointTest(k, gx, gy, shapes, ignoreInvisible)) return true;
                }
                return o is AsSprite sp && sp.GraphicsOrNull is { } g && DrawingHit(g, lx, ly, shapes);
            }
            switch (o)
            {
                case AsTextField:
                    return (!ignoreInvisible || o.IsVisible) && o.HitsContent(gx, gy);
                case AsBitmap:
                    return o.LocalBounds().Contains(lx, ly);
                case AsShape sh:
                    if (sh.GraphicsOrNull is { } g && DrawingHit(g, lx, ly, shapes)) return true;
                    return ShapeDefHit(o.Symbol, lx, ly, shapes);
                default:
                    return o.Symbol?["kind"]?.GetValue<string>() == "shape" ? ShapeDefHit(o.Symbol, lx, ly, shapes) : o.HitsContent(gx, gy);
            }
        }

        /// <summary>The script drawing (Graphics): its rectangles and ellipses, or only its bounds when not testing shapes.</summary>
        static bool DrawingHit(AsGraphics g, double x, double y, bool shapes)
        {
            if (!g.Bounds.Contains(x, y)) return false;
            if (!shapes) return true;
            foreach (var (op, v, _, _) in g.Ops)
            {
                if (op == "rect" && x >= v[0] && x < v[0] + v[2] && y >= v[1] && y < v[1] + v[3]) return true;
                if (op == "ellipse" && v[2] > 0 && v[3] > 0)
                {
                    double dx = (x - (v[0] + v[2] / 2)) / (v[2] / 2), dy = (y - (v[1] + v[3] / 2)) / (v[3] / 2);
                    if (dx * dx + dy * dy <= 1) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// A shape definition's fills: inside one of its solid fill outlines (even-odd); a bitmap-filled shape by its bounds,
        /// which is where the movie data stores its bitmap fill area.
        /// </summary>
        static bool ShapeDefHit(System.Text.Json.Nodes.JsonObject sym, double x, double y, bool shapes)
        {
            if (sym == null) return false;
            var b = sym["bounds"] is System.Text.Json.Nodes.JsonObject bo ? SymbolScope.Rect(bo) : RectD.None;
            if (!b.Contains(x, y)) return false;
            if (!shapes) return true;
            if (sym["bitmaps"] is System.Text.Json.Nodes.JsonArray bms && bms.Count > 0) return true;
            if (sym["paths"] is not System.Text.Json.Nodes.JsonArray ps) return false;
            foreach (var p in ps)
            {
                if (p?["pts"] is not System.Text.Json.Nodes.JsonArray a) continue;
                int n = a.Count / 2;
                bool inside = false;
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    double xi = (double)a[2 * i], yi = (double)a[2 * i + 1], xj = (double)a[2 * j], yj = (double)a[2 * j + 1];
                    if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
                }
                if (inside) return true;
            }
            return false;
        }

        /// <summary>The interactive object that receives a mouse event for a hit (mouseEnabled / mouseChildren applied).</summary>
        public AsInteractiveObject MouseTarget(double x, double y)
        {
            var hit = TopMost(x, y, true);
            AsInteractiveObject t = null;
            for (var o = hit; o != null; o = o.Parent)
                if (o is AsInteractiveObject io && io is not AsStage) { if (t == null && io.mouseEnabled) t = io; }
            // an ancestor with mouseChildren=false takes the event
            for (var o = hit; o != null; o = o.Parent)
                if (o is AsContainer c && !c.mouseChildren && c is not AsStage) t = c.mouseEnabled ? c : t;
            return t ?? (hit != null ? Stage : null);
        }

        AsInteractiveObject over;

        void MouseEv(AsInteractiveObject t, string type, AsObject related = null, int delta = 0)
        {
            if (t == null) return;
            var (lx, ly) = t.GlobalToLocal(MouseX, MouseY);
            bool bubbles = type is not ("rollOver" or "rollOut");
            RT.Guard(type, () =>
            {
                var e = (AsMouseEvent)RT.GetClass("flash.events.MouseEvent").Construct(type, Rt.Box(bubbles));
                e.localX = lx; e.localY = ly; e.StageX = MouseX; e.StageY = MouseY; e.buttonDown = MouseDown; e.relatedObject = related; e.delta = delta;
                t.Dispatch(e);
            });
        }

        public void MouseMove(double x, double y)
        {
            MouseX = x; MouseY = y;
            var t = MouseTarget(x, y);
            if (t != over)
            {
                var old = over; over = t;
                if (old != null) { MouseEv(old, "mouseOut", t); for (var o = (AsDisplayObject)old; o != null && !Contains(o, t); o = o.Parent) if (o is AsInteractiveObject io) MouseEv(io, "rollOut", t); }
                if (t != null)
                {
                    MouseEv(t, "mouseOver", old);
                    var chain = new List<AsInteractiveObject>();
                    for (var o = (AsDisplayObject)t; o != null && !Contains(o, old); o = o.Parent) if (o is AsInteractiveObject io) chain.Add(io);
                    for (int i = chain.Count - 1; i >= 0; i--) MouseEv(chain[i], "rollOver", old);
                }
            }
            MouseEv(t, "mouseMove");
        }

        static bool Contains(AsDisplayObject a, AsDisplayObject b) { for (var o = b; o != null; o = o.Parent) if (o == a) return true; return false; }

        AsInteractiveObject pressed;
        public void MouseButton(bool down)
        {
            MouseDown = down;
            var t = MouseTarget(MouseX, MouseY);
            if (down)
            {
                pressed = t;
                var f = t;
                for (var o = (AsDisplayObject)t; o != null; o = o.Parent) if (o is AsInteractiveObject io && (io is AsTextField { type: "input" } || io.tabEnabled)) { f = io; break; }
                MouseEv(t, "mouseDown");
                if (f is AsTextField { type: "input" } || f?.tabEnabled == true) SetFocus(f, "mouse");
            }
            else
            {
                MouseEv(t, "mouseUp");
                if (t != null && t == pressed) MouseEv(t, "click");
                pressed = null;
            }
        }

        public void Wheel(int delta) => MouseEv(MouseTarget(MouseX, MouseY), "mouseWheel", null, delta);

        /// <summary>A key from the host: keyDown/keyUp to the focus (or the stage); Tab moves focus; typed characters go to an input field.</summary>
        public void Key(uint keyCode, uint charCode, bool down, bool shift = false, bool ctrl = false)
        {
            AsInteractiveObject target = Focus as AsInteractiveObject ?? Stage;
            if (Focus is AsDisplayObject fd && !fd.OnStage) target = Stage;
            bool proceed = true;
            RT.Guard(down ? "keyDown" : "keyUp", () =>
            {
                var e = (AsKeyboardEvent)RT.GetClass("flash.events.KeyboardEvent").Construct(down ? "keyDown" : "keyUp", Rt.True, Rt.True);
                e.keyCode = keyCode; e.charCode = charCode; e.shiftKey = shift; e.ctrlKey = ctrl;
                proceed = target.Dispatch(e);
            });
            if (!down || !proceed) return;
            if (keyCode == 9)
            {
                bool moved = true;
                RT.Guard("keyFocusChange", () =>
                {
                    var e = (AsFocusEvent)RT.GetClass("flash.events.FocusEvent").Construct("keyFocusChange", Rt.True, Rt.True);
                    e.keyCode = 9; e.shiftKey = shift;
                    moved = target.Dispatch(e);
                });
                if (moved) MoveFocus(shift ? "up" : "down");
                return;
            }
            if (Focus is AsTextField tf && tf.type == "input")
                RT.Guard("text input", () =>
                {
                    if (keyCode == 8) { tf.Backspace(); return; }
                    if (charCode >= 32 && !ctrl) TypeInto(tf, ((char)charCode).ToString());
                });
        }

        /// <summary>Text typed into the focused field (IME output arrives here too): textInput first, then the edit.</summary>
        public void TypeInto(AsTextField tf, string s)
        {
            bool ok = true;
            RT.Guard("textInput", () =>
            {
                var e = (AsTextEvent)RT.GetClass("flash.events.TextEvent").Construct("textInput", Rt.True, Rt.True, s);
                ok = tf.Dispatch(e);
            });
            if (ok) tf.TypeText(s);
        }

        public void TypeText(string s) { if (Focus is AsTextField tf) TypeInto(tf, s); }

        public void ClearFocusSilently() => Focus = null;

        public void SetFocus(AsInteractiveObject o, string how)
        {
            if (ReferenceEquals(o, Focus)) return;
            var old = Focus as AsInteractiveObject;
            if (how != null)
            {
                // the change event goes to the focused object, or to the stage when nothing has focus (CLIK's
                // FocusHandler listens on the stage and sets UIComponent.focused from it)
                AsDisplayObject from = (AsDisplayObject)old ?? Stage;
                bool ok = true;
                RT.Guard(how == "mouse" ? "mouseFocusChange" : "keyFocusChange", () =>
                {
                    var e = (AsFocusEvent)RT.GetClass("flash.events.FocusEvent").Construct(how == "mouse" ? "mouseFocusChange" : "keyFocusChange", Rt.True, Rt.True);
                    e.relatedObject = o;
                    ok = from.Dispatch(e);
                });
                if (!ok) return;
            }
            Focus = o;
            if (o is AsTextField tf) { tf.SelBegin = 0; tf.SelEnd = tf.Text.Length; }
            if (old != null) RT.Guard("focusOut", () => { var e = (AsFocusEvent)RT.GetClass("flash.events.FocusEvent").Construct("focusOut", Rt.True); e.relatedObject = o; old.Dispatch(e); });
            if (o != null) RT.Guard("focusIn", () => { var e = (AsFocusEvent)RT.GetClass("flash.events.FocusEvent").Construct("focusIn", Rt.True); e.relatedObject = old; o.Dispatch(e); });
        }

        /// <summary>Tab order: tabIndex when any is set, else reading order (top to bottom, left to right) of tab-enabled objects.</summary>
        public void MoveFocus(string dir)
        {
            var list = new List<AsInteractiveObject>();
            void Walk(AsDisplayObject o)
            {
                if (!o.IsVisible) return;
                if (o is AsInteractiveObject io && io is not AsStage && io.tabEnabled) list.Add(io);
                if (o is AsContainer c && c.tabChildren) foreach (var k in c.Children) Walk(k);
            }
            Walk(ModalClip ?? Stage);
            if (list.Count == 0) return;
            List<AsInteractiveObject> order = list.Any(x => x.tabIndex >= 0)
                ? list.Where(x => x.tabIndex >= 0).OrderBy(x => x.tabIndex).ToList()
                : list.OrderBy(x => Math.Round(x.GetBounds(null).Y / 8)).ThenBy(x => x.GetBounds(null).X).ToList();
            int i = order.IndexOf(Focus as AsInteractiveObject);
            int n = dir == "up" ? (i <= 0 ? order.Count - 1 : i - 1) : (i + 1) % order.Count;
            SetFocus(order[n], "key");
        }

        // ---- misc used by natives ----------------------------------------------------------------------------

        internal List<object> ObjectsUnder(AsContainer c, double x, double y)
        {
            var r = new List<object>();
            void Walk(AsDisplayObject o)
            {
                if (o is AsContainer cc) foreach (var k in cc.Children) Walk(k);
                else if (o.IsVisible && o.HitsContent(x, y)) r.Add(o);
            }
            Walk(c);
            return r;
        }

        AsSprite dragging; double dragDx, dragDy; RectD? dragBounds;
        internal void StartDrag(AsSprite s, bool lockCenter, RectD? bounds)
        {
            dragging = s; dragBounds = bounds;
            var (px, py) = s.Parent?.GlobalToLocal(MouseX, MouseY) ?? (MouseX, MouseY);
            dragDx = lockCenter ? 0 : s.Matrix.Tx - px; dragDy = lockCenter ? 0 : s.Matrix.Ty - py;
        }
        internal void StopDrag(AsSprite s) { if (dragging == s) dragging = null; }
        public void UpdateDrag()
        {
            if (dragging == null) return;
            var (px, py) = dragging.Parent?.GlobalToLocal(MouseX, MouseY) ?? (MouseX, MouseY);
            double nx = px + dragDx, ny = py + dragDy;
            if (dragBounds is { } b && !b.IsEmpty) { nx = Math.Clamp(nx, b.X, b.X + b.W); ny = Math.Clamp(ny, b.Y, b.Y + b.H); }
            dragging.Matrix.Tx = nx; dragging.Matrix.Ty = ny; dragging.ScriptMoved();
        }
    }
}

namespace Ms2.Avm2.Flash
{
    /// <summary>Numbers from the layout JSON, whichever of int or double they were written as.</summary>
    public static class J
    {
        public static double? D(JsonNode n) => n is JsonValue v ? (v.TryGetValue<double>(out var d) ? d : v.TryGetValue<int>(out var i) ? i : v.TryGetValue<long>(out var l) ? l : v.TryGetValue<float>(out var f) ? f : v.TryGetValue<System.Text.Json.JsonElement>(out var e) && e.ValueKind == System.Text.Json.JsonValueKind.Number ? e.GetDouble() : null) : null;
        public static double Dv(JsonNode n) => D(n) ?? 0;
        public static int? I(JsonNode n) => D(n) is double d ? (int)d : null;
        public static int Iv(JsonNode n) => I(n) ?? 0;
    }
}
