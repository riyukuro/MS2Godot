using System.Reflection;
using System.Text.Json.Nodes;
using Ms2.Swf.Abc;

namespace Ms2.Avm2
{
    public sealed partial class Runtime
    {
        /// <summary>The player running this runtime's movie (display list, frames, input); null for a bare runtime.</summary>
        public Flash.Player Player;
        /// <summary>Counts the marks made on this runtime's display objects (<see cref="Flash.AsDisplayObject.MarkChanged"/>):
        /// a movie whose count did not move has the same display list.</summary>
        public long DisplayMarks;
    }
}

namespace Ms2.Avm2.Flash
{
    /// <summary>Symbols of one movie (the main one or an imported library), with the classes linked to them.</summary>
    public sealed class SymbolScope
    {
        public string Key;
        public Ms2.Swf.LayoutSymbols Symbols;
        public JsonObject Layout;          // the movie layout (main) or library entry
        public readonly Dictionary<string, int> Classes = new();

        public JsonObject Get(int id) => Symbols?.Get(id);
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
        /// <summary>
        /// The stage this movie shares with other movies, or null. In the client every dialog is loaded into main.gfx's one
        /// stage, so Sprite.dropTarget sees all of them; here each movie has its own Player, and the shared stage stands in.
        /// </summary>
        public IDropStage DropStage;
        public bool MouseDown;
        public object Focus;
        /// <summary>
        /// The clipboard every movie's text fields share (the one TextClipboard state the client's loader installs,
        /// gfxmanager.cpp:1063). The game puts the system clipboard here.
        /// </summary>
        public static ITextClipboard SharedClipboard { get; set; } = new TextClipboard();
        ITextClipboard clipboard;
        /// <summary>This movie's clipboard: <see cref="SharedClipboard"/> unless one is set.</summary>
        public ITextClipboard Clipboard { get => clipboard ?? SharedClipboard; set => clipboard = value; }
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
        readonly List<AsTextField> scrollWatch = new();
        internal void WatchScroll(AsTextField f) => scrollWatch.Add(f);
        readonly List<Action> later = new();
        readonly List<AsMovieClip> scriptQueue = new();     // clips whose frame needs its script (Flag_NeedExecuteFrame)
        readonly List<FrameAction> frameActions = new();    // the action queue's AL_Frame level, run from frameHead on
        // MovieImpl::pPlayListOptHead (sfr_gfx_01i.cpp:3255 @0x14256d390): playing multi-frame clips, so Advance skips
        // quiet movies and static subtrees instead of walking the whole display list every tick.
        readonly List<AsMovieClip> playingClips = new();
        readonly List<AsDisplayObject> advanceBuf = new();
        readonly List<KeyValuePair<JsonObject, AsDisplayObject>> wrapBuf = new();   // Advance's copy of Live at a loop to frame 0
        uint advanceEpoch;
        bool advancing;
        int frameHead;
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
            Main = new SymbolScope { Key = (string)layout["movie"], Symbols = Ms2.Swf.LayoutSymbols.Of(layout["symbols"]), Layout = layout };
            if (layout["library"] is JsonObject lib)
                foreach (var (key, node) in lib)
                {
                    var sc = new SymbolScope { Key = key, Symbols = Ms2.Swf.LayoutSymbols.Of(node["symbols"]), Layout = (JsonObject)node };
                    if (node["classes"] is JsonObject cl) foreach (var (cn, id) in cl) { sc.Classes[cn] = J.Iv(id); classSymbols.TryAdd(cn, (sc, J.Iv(id))); }
                }
            if (Main.Symbols != null)
                foreach (var n in Main.Symbols.Named)
                    if (n.Class is string cn) { Main.Classes[cn] = n.Id; classSymbols[cn] = (Main, n.Id); }
            // the imported movies' own symbols (shared libraries: every class they link, not only those this movie names)
            if (imported != null)
                foreach (var (key, il) in imported)
                {
                    if (Ms2.Swf.LayoutSymbols.Of(il?["symbols"]) is not { } isyms) continue;
                    var sc = new SymbolScope { Key = key, Symbols = isyms, Layout = il };
                    foreach (var n in isyms.Named)
                        foreach (var cn in new[] { n.Class, n.Export })
                            if (cn != null) { sc.Classes[cn] = n.Id; classSymbols.TryAdd(cn, (sc, n.Id)); }
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
                // and the restrict sets the name boxes take (Main.addedToStage: CommonPath.flash = false, then CommonInit.init,
                // which runs CommonTextField.init after CommonDepth.init). Common.init, first in CommonInit.init, has cleared
                // Common.m_getFormatFunction, and main registers it only when its classMemberSendToClient call arrives later
                // in addedToStage (GFxMovieMain::vfunc_4 @0x1422ef3c0, gfxmoviemain.cpp:106-114): initRestrictString reads ""
                // for "$s_common_text_restrict" and leaves both sets null, so a name field takes any character. The host here
                // has set the function already, so it is held back for the call. Not run, getCommonRestrictString hands out
                // its initial "" and a name field given it takes none (an empty restrict lets no char in).
                // STILL-OPEN: of CommonInit.init, Start runs only CommonDepth.init and CommonTextField.init; it still skips
                // Common.init, CommonFinder, CommonAlign, CommonClass, CommonColor, CommonDef, CommonMath, CommonPath.init,
                // CommonSlot, CommonShop, Align, EventString and IncludeFiles
                if (InGame && RT.GetClass("scaleform.ms2.common.CommonTextField") is { } ctf)
                {
                    var common = RT.GetClass("scaleform.ms2.common.Common");
                    var format = common == null ? null : RT.GetProperty(common, Name.Public("m_getFormatFunction"));
                    if (common != null) RT.SetProperty(common, Name.Public("m_getFormatFunction"), null);
                    try { RT.CallProperty(ctf, Name.Public("init"), Array.Empty<object>()); }
                    finally { if (common != null) RT.SetProperty(common, Name.Public("m_getFormatFunction"), format); }
                }
                // and starts the tooltip manager on the stage (Main.addedToStage: TooltipManager.init(stage)), so the dialogs'
                // items register their tooltips with it; started with CommonPath.flash already off it keeps the game's
                // MS2DataModel.getUserData instead of installing its authoring stand-in
                if (InGame && RT.GetClass("scaleform.ms2.manager.TooltipManager") is { } tm)
                    RT.CallProperty(tm, Name.Public("init"), new object[] { Stage });
                // and the drag manager (Main.addedToStage: DragManager.init(stage), next): its drag canvas goes into the
                // layer under the mouse cursor and it hears DRAG_START on the stage. A dialog movie loaded into main.gfx's
                // VM resolves scaleform.clik.managers.DragManager to main's class, already started, so a slot built off the
                // display list (whose own DragSlot ctor sees no stage) still drags its icon
                if (InGame && RT.GetClass("scaleform.clik.managers.DragManager") is { } dm)
                    RT.CallProperty(dm, Name.Public("init"), new object[] { Stage });
                // and listens for the right button on the stage (Main.addedToStage, after TooltipManager.init)
                if (InGame) ListenForRightButton();
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
                AddMovieRoot(Root, Main.Key);
                EnterStage(HostLoader);
                RunFrameScripts();
            });
        }

        /// <summary>Allocates a display object, puts it into the parent (timeline order), builds its frame 1, then runs its constructor.</summary>
        AsObject ConstructInto(AsClass cls, AsContainer parent, SymbolScope scope, JsonObject sym, JsonObject pl, int depth, bool setFov = false)
        {
            var o = (cls.Allocator ?? (_ => new AsObject()))(cls);
            o.Init(cls);
            if (o is AsDisplayObject d)
            {
                d.Placement = pl; d.Depth = depth;
                if (pl != null) ApplyPlacement(d, pl, initial: true);
                if (parent != null) { if (depth == 0 && pl == null) parent.Children.Add(d); else PlaceChild(parent, d); d.Parent = parent; }
            }
            pending = (scope, sym, pl);
            RT.BeforeConstruct(o);
            pending = null;
            // like the Flash/Scaleform players: an uncaught error in a display object's constructor is reported and the
            // instance stays where it was placed (the rest of its constructor does not run)
            // a movie a Loader loads gets SetFOV(55) before its constructor runs (sfr_as3_18.cpp:8135, 8494)
            if (setFov) AsTransform.InitFov(o as AsDisplayObject);
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
                    MarkScript(mc);
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
                PlaceChild(parent, bm);
                if ((string)pl["name"] is string bn && bn.Length > 0) { bm.InstanceName = bn; BindName(parent, bn, bm); }
                return bm;
            }
            var cls = ClassForPlacement(pl, scope, out var sym, out var symScope);
            if (sym == null && pl["id"] is JsonNode iid && (string)pl["kind"] is "subimage" or "image" && PlacedImage(scope, J.Iv(iid)) is { } pi)
            {
                // a character id that is no character definition but an image resource (a DefineSubImage or DefineBits tag
                // placed on a timeline, as CommonJobIcon's job icon frames are): the client makes a Bitmap character of it
                // (AvmSprite::AddDisplayObject, sfr_as3_16z.cpp:1588-1593, type 8 = CharacterDef::Bitmap, created by
                // AS3Support::CreateCharacterInstance as an AvmBitmap, sfr_as3_01.cpp:51) drawing that image at its own size
                var ib = NewBitmap(null, pi.W, pi.H);
                ib.bitmapData.ImageScope = scope; ib.bitmapData.ImageId = J.Iv(iid);
                ib.Placement = pl; ib.Depth = J.Iv(pl["depth"]); ApplyPlacement(ib, pl, initial: true);
                PlaceChild(parent, ib);
                if ((string)pl["name"] is string ibn && ibn.Length > 0) { ib.InstanceName = ibn; BindName(parent, ibn, ib); }
                return ib;
            }
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
        // where PlaceChild puts the next timeline object: before this child of replaceParent (null: at the end)
        AsContainer replaceParent;
        AsDisplayObject replaceBefore;

        /// <summary>Puts a timeline object into its parent: where the object it replaces was, else in depth order.</summary>
        void PlaceChild(AsContainer parent, AsDisplayObject d)
        {
            if (replaceParent == parent)
            {
                replaceParent = null;
                int idx = replaceBefore == null ? -1 : parent.Children.IndexOf(replaceBefore);
                replaceBefore = null;
                parent.Children.Insert(idx < 0 ? parent.Children.Count : idx, d);
                d.Parent = parent;
                return;
            }
            parent.InsertByDepth(d);
        }

        /// <summary>
        /// A placement whose tag replaces the character at its depth (PlaceObject2/3 with the move flag and a character,
        /// "replace" in the layout) takes the replaced object's place in the parent's list: the client's Place_Replace runs
        /// DisplayList::ReplaceDisplayObject (sfr_gfx_05.cpp:575, sfr_gfx_06c.cpp:331), which swaps the entry at that index.
        /// So a script-added child (Depth -1 in the client, int.MinValue here) that went above the old object stays above
        /// the new one: RankIcon's grade frames swap the depth-1 background below the m_icon its constructor added.
        /// Without the replaced object (script removed it) the new one goes in by depth, as AddDisplayObject does.
        /// </summary>
        void BuildFrame(AsMovieClip mc, int f)
        {
            mc.Frame = f;
            if (mc.TL == null) return;
            var pls = mc.TL.Placements; var tracks = mc.TL.Tracks;
            // the objects removed this frame by depth, and the child that followed each removed object (a replacing
            // placement comes after the one it replaces, which was placed earlier); built only when a frame removes
            // something. When that child was itself removed, the anchor walks on to the child that followed it.
            Dictionary<int, AsDisplayObject> removedAt = null;
            Dictionary<AsDisplayObject, AsDisplayObject> removedNext = null;
            for (int i = 0; i < pls.Count; i++)
            {
                var pl = pls[i]; var tr = tracks[i];
                bool alive = tr.AliveAt(f);
                mc.Live.TryGetValue(pl, out var cur);
                if (alive && cur == null)
                {
                    if (removedAt != null && pl["replace"] != null && removedAt.TryGetValue(J.Iv(pl["depth"]), out var gone))
                    {
                        replaceBefore = removedNext[gone];
                        while (replaceBefore != null && removedNext.TryGetValue(replaceBefore, out var next)) replaceBefore = next;
                        replaceParent = mc;
                    }
                    var o = Instantiate(mc, pl, mc.TL.Scope);
                    replaceParent = null; replaceBefore = null;
                    if (o != null) { mc.Live[pl] = o; ApplyMoves(o, pl, tr, f); if (mc.OnStage) EnterStage(o); }
                }
                else if (alive) ApplyMoves(cur, pl, tr, f);
                else if (cur != null)
                {
                    mc.Live.Remove(pl);
                    if (cur.Parent == mc)
                    {
                        int idx = mc.Children.IndexOf(cur);
                        removedAt ??= new Dictionary<int, AsDisplayObject>();
                        removedNext ??= new Dictionary<AsDisplayObject, AsDisplayObject>();
                        removedAt[cur.Depth] = cur;
                        removedNext[cur] = idx + 1 < mc.Children.Count ? mc.Children[idx + 1] : null;
                        mc.RemoveChildInternal(cur, true);
                    }
                }
            }
        }

        static void ApplyMoves(AsDisplayObject o, JsonObject pl, Timeline.Track tr, int f)
        {
            if (tr.Moves == null) return;
            int k = tr.MoveAt(f);
            if (k < 0) { ApplyPlacement(o, pl, initial: false); return; }
            tr.Get(k, out var m, out var c);
            if (!o.MatrixLocked && m is Mat mm) o.Matrix = mm;
            if (!o.ColorLocked && c is Cx cc) o.Color = cc;
        }

        /// <summary>
        /// Sprite::GotoFrame: changes the frame now; its script is only marked (the frame tags set Flag_NeedExecuteFrame,
        /// AvmSprite::ExecuteFrameTags @0x1424feab0) and runs when the frame actions are next queued and run.
        /// </summary>
        public void GotoFrame(AsMovieClip mc, int f)
        {
            if (f == mc.Frame) return;
            if (f < mc.Frame)
            {
                // going back: placements first shown after the target frame go; ones shown before it are kept or rebuilt
                foreach (var (pl, o) in mc.Live.ToList())
                    if (!AliveAt(pl, f) || J.Iv(pl["first"]) > f) { mc.Live.Remove(pl); if (o.Parent == mc) mc.RemoveChildInternal(o, true); }
            }
            BuildFrame(mc, f);
            MarkScript(mc);
        }

        bool queueUnordered;    // a mark that did not come from Advance's pre-order walk is in the queue

        /// <summary>Marks a clip's frame script. <paramref name="inOrder"/>: the caller marks in play-list order (Advance).</summary>
        internal void MarkScript(AsMovieClip mc, bool inOrder = false)
        {
            if (mc.ScriptQueued) return;
            mc.ScriptQueued = true; scriptQueue.Add(mc);
            if (!inOrder) queueUnordered = true;
        }

        /// <summary>
        /// One entry of the action queue's AL_Frame level (MovieRoot::ActionEntry): a frameConstructed/exitFrame event for
        /// one listener (AvmDisplayObj::OnEvent queues both at AL_Frame, sfr_as3_18.cpp:2438) or, with Type null, the
        /// frame script of Target's frame Frame (AvmSprite::QueueFrameScript @0x1425200b0 also inserts at AL_Frame).
        /// A null Target is an entry RemoveActionQueueEntriesFor took the character from: it is skipped.
        /// </summary>
        sealed class FrameAction { public AsEventDispatcher Target; public string Type; public int Frame; }

        /// <summary>
        /// FrameCounter::QueueFrameActions (@0x14251ff60): frameConstructed for every listener, then the marked clips'
        /// frame scripts (the mark is cleared as each is queued), then exitFrame for every listener, all appended to AL_Frame.
        /// </summary>
        void QueueFrameActions(bool events)
        {
            if (events) QueueBroadcast("frameConstructed");
            if (queueUnordered && scriptQueue.Count > 1) SortByPlayList();
            queueUnordered = false;
            foreach (var mc in scriptQueue)
            {
                mc.ScriptQueued = false;
                if (mc.LastScriptFrame == mc.Frame) continue;
                mc.LastScriptFrame = mc.Frame;
                if (mc.FrameScripts.ContainsKey(mc.Frame)) frameActions.Add(NewAction(mc, null, mc.Frame));
            }
            scriptQueue.Clear();
            if (events) QueueBroadcast("exitFrame");
        }

        /// <summary>
        /// The marked clips in the client's play-list order (QueueFrameActions walks pPlayPrev from the frame counter). A
        /// child joins the list next to its place in the parent (AvmDisplayObjContainer::FindInsertToPlayList @0x142501920,
        /// sfr_as3_17d.cpp:177: after the previous in-list sibling's branch, before the next one, right after the parent
        /// when it has none; AvmInteractiveObj::FindInsertToPlayList @0x142501a60 puts it right after its parent), so the
        /// walk is the display tree in pre-order: a parent's script before its children's, siblings in display order.
        /// Advance marks in that order already; only construction and goto can leave the queue out of it, so the sort runs
        /// only when queueUnordered is set. It reuses its buffers and compares two clips by climbing to their common
        /// ancestor, so a tick without it allocates nothing.
        /// Off the stage: the client keeps an AS3-built clip with no parent on the play list too (AddScriptableMovieClip
        /// FUN_1424e9380, sfr_as3_16.cpp:2347 calls AddToPlayList; removeChild's MoveBranchInPlayList with a null parent puts
        /// the branch at the head, sfr_as3_18.cpp:3375, 2976-2984), so those clips run after the stage tree, oldest first,
        /// each branch in pre-order. Here they keep their mark order after the stage's clips. STILL-OPEN: the exact
        /// off-stage order (the test pins this guess).
        /// STILL-OPEN: setChildIndex (@0x142526610, sfr_as3_19.cpp:3017) does not call MoveBranchInPlayList, so the client
        /// keeps the old play-list place of a re-indexed child; this sort follows the current child index.
        /// </summary>
        void SortByPlayList()
        {
            int n = scriptQueue.Count;
            if (sortBuf.Length < n) sortBuf = new PlayKey[Math.Max(n, sortBuf.Length * 2)];
            for (int i = 0; i < n; i++)
            {
                var mc = scriptQueue[i];
                int depth = 0; AsDisplayObject o = mc;
                while (o.Parent != null) { depth++; o = o.Parent; }
                sortBuf[i] = new PlayKey { Mc = mc, Depth = ReferenceEquals(o, Stage) ? depth : -1, Seq = i };
            }
            Array.Sort(sortBuf, 0, n, playOrder ??= new PlayOrder());
            for (int i = 0; i < n; i++) { scriptQueue[i] = sortBuf[i].Mc; sortBuf[i] = default; }
        }

        struct PlayKey { public AsMovieClip Mc; public int Depth, Seq; }
        PlayKey[] sortBuf = new PlayKey[16];
        PlayOrder playOrder;

        sealed class PlayOrder : IComparer<PlayKey>
        {
            public int Compare(PlayKey a, PlayKey b)
            {
                if (a.Depth < 0 || b.Depth < 0) return (a.Depth < 0) == (b.Depth < 0) ? a.Seq.CompareTo(b.Seq) : a.Depth < 0 ? 1 : -1;
                AsDisplayObject x = a.Mc, y = b.Mc; int dx = a.Depth, dy = b.Depth;
                while (dx > dy) { x = x.Parent; dx--; }
                while (dy > dx) { y = y.Parent; dy--; }
                if (ReferenceEquals(x, y)) { int c = a.Depth.CompareTo(b.Depth); return c != 0 ? c : a.Seq.CompareTo(b.Seq); }   // an ancestor comes first
                while (!ReferenceEquals(x.Parent, y.Parent)) { x = x.Parent; y = y.Parent; }
                var kids = x.Parent.Children;
                return kids.IndexOf(x).CompareTo(kids.IndexOf(y));
            }
        }

        void QueueBroadcast(string type)
        {
            // EventChains::QueueEvents (@0x14251fd40) walks the chain as it is now: a listener added later is not queued
            if (broadcast.TryGetValue(type, out var l))
                foreach (var d in l) frameActions.Add(NewAction(d, type, 0));
        }

        /// <summary>
        /// MovieRoot::ExecuteActionQueue (@0x1424fa320) on AL_Frame: takes the entries off the front until none is left. The
        /// queue is shared, so a goto run by one of them drains the rest (and what it queued) inside the call, and the
        /// outer loop then finds nothing (ActionQueueIterator::getNext restarts from the front when ModId moved).
        /// </summary>
        void RunFrameQueue()
        {
            while (frameHead < frameActions.Count)
            {
                var a = frameActions[frameHead++];
                if (a.Target == null) continue;
                if (a.Type != null) { RT.Guard(a.Type, a.Target.Class?.Name ?? "", (p: this, t: a.Type, d: a.Target), static x => x.p.Dispatch(x.t, x.d)); continue; }
                var mc = (AsMovieClip)a.Target;
                if (mc.FrameScripts.TryGetValue(a.Frame, out var fn))
                    RT.Guard("frame script", mc.ScriptLabel(a.Frame), (fn, mc), static x => x.fn.Call(x.mc, Array.Empty<object>()));
            }
            foreach (var x in frameActions) { x.Target = null; x.Type = null; spareActions.Push(x); }
            frameActions.Clear(); frameHead = 0;
        }

        // the queue's entries are reused frame to frame (an entry is read before the call it makes, so a drain inside
        // that call may take it back)
        readonly Stack<FrameAction> spareActions = new();
        FrameAction NewAction(AsEventDispatcher target, string type, int frame)
        {
            var a = spareActions.Count > 0 ? spareActions.Pop() : new FrameAction();
            a.Target = target; a.Type = type; a.Frame = frame;
            return a;
        }

        /// <summary>
        /// MovieRoot::RemoveActionQueueEntriesFor(root, AL_Frame, clip) (FUN_142522490, sfr_as3_18.cpp:4037): the clip's
        /// pending AL_Frame entries (its frame script, its frameConstructed/exitFrame) lose their character.
        /// </summary>
        internal void DropFrameActions(AsMovieClip mc)
        {
            for (int i = frameHead; i < frameActions.Count; i++)
                if (ReferenceEquals(frameActions[i].Target, mc)) frameActions[i].Target = null;
        }

        /// <summary>The frame scripts of the marked clips, with no frame events (the end of a movie's load).</summary>
        void RunFrameScripts() { QueueFrameActions(false); RunFrameQueue(); }

        // ---- stage membership --------------------------------------------------------------------------------

        void EnterStage(AsDisplayObject o)
        {
            if (o is AsMovieClip mc && mc.CanAdvance) RegisterPlaying(mc);
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
            if (!broadcast.TryGetValue(type, out var l) || l.Count == 0) return;
            int n = l.Count;
            var ds = System.Buffers.ArrayPool<AsEventDispatcher>.Shared.Rent(n);
            for (int i = 0; i < n; i++) ds[i] = l[i];   // per element, as in AsEventDispatcher.Fire
            try { for (int i = 0; i < n; i++) RT.Guard(type, ds[i].Class?.Name ?? "", (p: this, t: type, d: ds[i]), static x => x.p.Dispatch(x.t, x.d)); }
            finally { Array.Clear(ds, 0, n); System.Buffers.ArrayPool<AsEventDispatcher>.Shared.Return(ds); }
        }

        void Dispatch(string type, AsEventDispatcher d)
        {
            var e = (AsEvent)RT.GetClass("flash.events.Event").Construct(type, Rt.False);
            e.Dispatched = true; e.Target = d; e.Phase = 2;
            d.Fire(e, false);
        }

        /// <summary>
        /// FrameCounter::QueueFrameActions (@0x14251ff60) and the action levels run right after (ExecuteActionQueue
        /// @0x1424fa320), as GotoFrameAndRun does and as each frame does: frameConstructed to every listener, the marked
        /// clips' frame scripts, exitFrame, behind whatever AL_Frame entries were still pending.
        /// </summary>
        internal void RunFrameActions() { QueueFrameActions(true); RunFrameQueue(); }

        internal void AddTimer(AsTimer t) { if (!timers.Contains(t)) timers.Add(t); }
        internal void RemoveTimer(AsTimer t) => timers.Remove(t);
        public void Later(Action a) => later.Add(a);

        /// <summary>
        /// MovieRoot::UnloadMovie (@0x1425305f0, sfr_as3_19.cpp:8453), for Loader::unload (dropAll false) and unloadAndStop
        /// (dropAll true, sfr_as3_07.cpp:5160): the content's loaderInfo hears "unload" first (8457-8460), the content leaves
        /// the loader, and its movie definition loses a user. With dropAll the content is also stopped (its listeners and
        /// queued frame actions go: ForceShutdown) and, when that was the definition's last user, the definition's timers
        /// shut down (ShutdownTimersForMovieDef, 8544). Content that is not a movie (an icon's Bitmap, Icon.loadImage and
        /// the other image loaders) has no definition here, so its unload stops no timer.
        /// STILL-OPEN: the client runs ForceShutdown for dropAll false too (outside the dropAll test); plain unload keeps the
        /// content's listeners here as before. The per-definition drops of listeners (RemoveListenersForMovieDef on the
        /// stage), fonts and registered display objects are not ported.
        /// </summary>
        internal void UnloadMovie(AsLoader loader, bool dropAll)
        {
            var li = loader == HostLoader ? rootInfo : loader.InfoIfAny;
            var content = loader.Children.FirstOrDefault();
            if (li != null && content != null) RT.Guard("unload event", () => li.Send("flash.events.Event", "unload"));
            foreach (var c in loader.Children.ToList())
            {
                loader.RemoveChildInternal(c, true);
                if (dropAll) StopContent(c);
                if (movieRoots.Remove(c, out var key) && --movieUsers[key] == 0)
                {
                    movieUsers.Remove(key);
                    if (dropAll) ShutdownTimersForMovieDef(key);
                }
            }
        }

        // the root display object of each movie this player holds → its movie definition (SymbolScope key), and the
        // number of roots of each definition (MovieRoot +0x6e8, the LoadedMovieDefs count UnloadMovie counts down)
        readonly Dictionary<AsDisplayObject, string> movieRoots = new();
        readonly Dictionary<string, int> movieUsers = new(StringComparer.OrdinalIgnoreCase);

        void AddMovieRoot(AsDisplayObject root, string key)
        {
            if (root == null || key == null || !movieRoots.TryAdd(root, key)) return;
            movieUsers[key] = movieUsers.TryGetValue(key, out var n) ? n + 1 : 1;
        }

        void StopContent(AsDisplayObject o)
        {
            if (o is AsEventDispatcher d) d.RemoveAllListeners();
            if (o is AsMovieClip mc) DropFrameActions(mc);
            if (o is AsContainer cont) foreach (var k in cont.Children) StopContent(k);
        }

        /// <summary>
        /// MovieImpl::ShutdownTimersForMovieDef (@0x142576cc0, sfr_gfx_01i.cpp:2403): each interval timer whose ClearFor
        /// (@0x1424efc30, sfr_as3_17.cpp:6320) matches the definition goes. ClearFor compares the definition's ABC file with
        /// GetFilePtr of the traits of the timer's function value, or of its Timer object (+0x30). Only user-defined traits
        /// have a file, so a Timer of a class the movie defines (a subclass of flash.utils.Timer) stops, and a plain
        /// flash.utils.Timer, whose builtin traits have none (base Traits::GetFilePtr, 0x142509ca0), keeps running.
        /// setTimeout / setInterval never match: their TimerObj is null, and the traits of a function, closure or method
        /// value are InstanceTraits::Function (vftable 0x14313fe18, slot 10 = the base GetFilePtr, NULL), so the movie's
        /// intervals keep running after the unload, as in the client.
        /// </summary>
        void ShutdownTimersForMovieDef(string key)
        {
            for (int i = timers.Count - 1; i >= 0; i--)
                if (string.Equals(timers[i].Class?.MovieKey, key, StringComparison.OrdinalIgnoreCase)) { timers[i].Halt(); timers.RemoveAt(i); }
        }

        /// <summary>One frame: deferred loads, enterFrame, timeline advance, frame scripts, exitFrame, timers and timeouts, render.</summary>
        public void Tick(bool advanceTimelines = true)
        {
            FrameCount++;
            if (later.Count > 0)
            {
                var todo = later.ToArray(); later.Clear();
                foreach (var a in todo) RT.Guard("deferred", a);
            }
            // STILL-OPEN: the client queues enterFrame at AL_EnterFrame while the frame advances and runs it after the
            // advance; here it is sent before. Marked clips off the stage run after the stage's (see
            // SortByPlayList for the STILL-OPEN off-stage order).
            SendBroadcast("enterFrame");
            UpdateDrag();               // AvmSprite::AdvanceFrame: DoMouseDrag before the frame moves on
            if (advanceTimelines && playingClips.Count > 0) RT.Guard("advance", null, this, static p => p.AdvanceTimelines());
            RunFrameActions();
            double dt = 1000.0 / FrameRate;
            RT.Guard("clock", null, (rt: RT, dt), static x => x.rt.AdvanceClock(x.dt));
            if (timers.Count is int nt and > 0)
            {
                var ts = System.Buffers.ArrayPool<AsTimer>.Shared.Rent(nt);
                for (int i = 0; i < nt; i++) ts[i] = timers[i];
                try { for (int i = 0; i < nt; i++) RT.Guard("timer", null, ts[i], static t => t.Pump()); }
                finally { Array.Clear(ts, 0, nt); System.Buffers.ArrayPool<AsTimer>.Shared.Return(ts); }
            }
            if (scrollWatch.Count > 0)
            {
                // the reformat of the fields changed this frame (DocView::Format): each sends its scroll event if its range moved
                var fs = scrollWatch.ToArray(); scrollWatch.Clear();
                foreach (var f in fs) RT.Guard("scroll", null, f, static t => t.CheckScroll());
            }
            // MovieRoot::AdvanceFrame (@0x1424eb2f0, sfr_as3_16z.cpp:607-611), at the end of every MovieImpl::Advance
            // (sfr_gfx_01i.cpp:3869): with StageInvalidated (+0x6e0, set only by Stage::invalidate) the render event chain
            // is queued and run, and the flag is cleared after it, so a stage.invalidate() inside a render handler is
            // dropped. Nothing set: one bool test, no dispatch.
            if (RenderInvalidated) { SendBroadcast("render"); RenderInvalidated = false; }
        }

        internal void RegisterPlaying(AsMovieClip mc)
        {
            if (!mc.TrackedPlaying)
            {
                mc.TrackedPlaying = true;
                playingClips.Add(mc);
            }
            if (advancing) MarkAdvancePath(mc, toRoot: true);
        }

        /// <summary>
        /// Marks <paramref name="o"/> and its ancestors. Before the walk (<paramref name="toRoot"/> false) the marking stops at
        /// the first ancestor already marked this tick, whose own path is marked then. During the walk a constructor or an
        /// event handler can move a marked subtree under an unmarked parent (the old path stays marked above the old parent),
        /// so a mark made then goes up to the root: a subtree that enters the stage re-registers every playing clip in it
        /// (EnterStage), and so marks its new place.
        /// </summary>
        void MarkAdvancePath(AsDisplayObject o, bool toRoot = false)
        {
            o.AdvanceEpoch = advanceEpoch;
            for (var p = o.Parent; p != null && (toRoot || p.AdvanceEpoch != advanceEpoch); p = p.Parent)
                p.AdvanceEpoch = advanceEpoch;
        }

        static void ResetAdvanceEpoch(AsDisplayObject o)
        {
            o.AdvanceEpoch = 0;
            if (o is AsContainer c)
                foreach (var k in c.Children) ResetAdvanceEpoch(k);
        }

        /// <summary>
        /// Advances every playing multi-frame clip on the stage in pre-order (matching MovieImpl::AdvanceFrame's
        /// optimized play list pPlayListOptHead, sfr_gfx_01i.cpp:3255 @0x14256d390): only paths leading to a playing clip
        /// are descended, and a reusable buffer replaces per-container ArrayPool.Shared Rent/Return calls.
        /// </summary>
        void AdvanceTimelines()
        {
            if (++advanceEpoch == 0) { ResetAdvanceEpoch(Stage); advanceEpoch = 1; }
            int any = 0;
            for (int i = playingClips.Count - 1; i >= 0; i--)
            {
                var mc = playingClips[i];
                if (!mc.CanAdvance || !mc.OnStage)
                {
                    mc.TrackedPlaying = false;
                    playingClips[i] = playingClips[^1];
                    playingClips.RemoveAt(playingClips.Count - 1);
                    continue;
                }
                MarkAdvancePath(mc);
                any++;
            }
            if (any == 0) return;
            advancing = true;
            try { Advance(Stage); }
            finally { advancing = false; advanceBuf.Clear(); }
        }

        void Advance(AsDisplayObject o)
        {
            if (o is AsMovieClip mc && mc.CanAdvance)
            {
                int next = (mc.Frame + 1) % mc.totalFrames;
                if (next == 0)
                {
                    bool any = false;
                    foreach (var (pl, _) in mc.Live)
                        if (J.Iv(pl["first"]) > 0) { any = true; break; }
                    if (any)
                    {
                        // from a copy, as a removedFromStage handler can change Live (Advance itself is not re-entered)
                        foreach (var kv in mc.Live) wrapBuf.Add(kv);
                        try
                        {
                            foreach (var (pl, ch) in wrapBuf)
                                if (J.Iv(pl["first"]) > 0) { mc.Live.Remove(pl); if (ch.Parent == mc) mc.RemoveChildInternal(ch, true); }
                        }
                        finally { wrapBuf.Clear(); }
                    }
                }
                BuildFrame(mc, next);
                MarkScript(mc, inOrder: true);
            }
            // the children as they are now (a child added later in this tick is advanced next tick), in a reused buffer; a
            // child is walked when its subtree holds a playing clip by the time the walk reaches it (a path a constructor
            // marks during the walk included), which is when the whole-list walk did anything in it
            if (o is AsContainer c && c.Children.Count is int n and > 0)
            {
                int start = advanceBuf.Count;
                for (int i = 0; i < n; i++) advanceBuf.Add(c.Children[i]);
                try
                {
                    for (int i = start; i < start + n; i++)
                        if (advanceBuf[i].AdvanceEpoch == advanceEpoch) Advance(advanceBuf[i]);
                }
                finally { advanceBuf.RemoveRange(start, n); }
            }
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

        /// <summary>The size of image resource <paramref name="id"/> of a scope (its atlas rectangle, or the whole image), or null.
        /// STILL-OPEN: an embedded DefineBits/JPEG image (tags 6, 21, 35, 90) carries no size in its tag, so the layout has
        /// none and this gives 0x0 where the client's bitmap has the decoded size.</summary>
        static (int W, int H)? PlacedImage(SymbolScope scope, int id)
        {
            foreach (var n in scope.Layout?["subImages"] as JsonArray ?? new JsonArray())
                if (J.Iv(n["id"]) == id) return ((int)Math.Round(J.D(n["w"]) ?? 0), (int)Math.Round(J.D(n["h"]) ?? 0));
            foreach (var n in scope.Layout?["images"] as JsonArray ?? new JsonArray())
                if (J.Iv(n["id"]) == id) return (J.I(n["width"]) ?? 0, J.I(n["height"]) ?? 0);
            return null;
        }

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

        /// <summary>
        /// Puts the root of another movie into a Loader, as Loader.load of a .swf / .gfx url does (the content is the loaded
        /// movie's root sprite: its document class, or a MovieClip, over its root timeline, its symbols in a scope of their own).
        /// The movie's code must be loaded into this runtime first; classes it links that this player already knows keep theirs.
        /// </summary>
        public AsDisplayObject LoadMovie(AsLoader l, string key, JsonObject layout)
        {
            var sc = new SymbolScope { Key = key, Symbols = Ms2.Swf.LayoutSymbols.Of(layout["symbols"]), Layout = layout };
            if (sc.Symbols != null)
                foreach (var (id, s) in sc.Symbols)
                    if ((string)s["class"] is string cn) { sc.Classes[cn] = int.Parse(id); classSymbols.TryAdd(cn, (sc, int.Parse(id))); }
            var rootSym = new JsonObject { ["kind"] = "sprite", ["timeline"] = layout["root"]?.DeepClone() };
            var name = (string)layout["documentClass"];
            var cls = name == null ? null : RT.GetClass(name);
            if (name != null && cls == null) RT.NoteMissing("document class " + name);
            cls ??= RT.GetClass("flash.display.MovieClip");
            AsDisplayObject root = null;
            RT.Guard("construct loaded movie " + key, () => root = (AsDisplayObject)ConstructInto(cls, l, sc, rootSym, null, 0, setFov: true));
            AddMovieRoot(root, key);
            if (root != null && OnStage(l)) EnterStage(root);
            RunFrameScripts();
            return root;
        }

        bool OnStage(AsDisplayObject o)
        {
            for (; o != null; o = o.Parent) if (o == Stage) return true;
            return false;
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
                    // as in ContainerTopMost: a clip-depth mask decides for the children up to its clip depth, so a scroll
                    // pane's rows outside its mask take no hit
                    var hit = MaskArray(kids, x, y);
                    try
                    {
                        for (int i = kids.Count - 1; i >= 0; i--)
                        {
                            var k = kids[i];
                            if (k.ClipDepth != null || hit != null && !hit[i]) continue;
                            var h = Hit(k);
                            if (h != null) return h;
                        }
                    }
                    finally { ReturnMaskArray(hit); }
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
        /// <summary>
        /// CalcDisplayListHitTestMaskArray (0x1425fbff0): for each child, whether the point is inside the clip-depth mask that
        /// covers it (a mask covers the children after it up to its clip depth; a mask's own entry is its own test; children no
        /// mask covers are true). Null when no child is a mask. The array is pooled and may be longer than the child list: give
        /// it back with <see cref="ReturnMaskArray"/> once the walk is done (the walk recurses, so each level has its own).
        /// </summary>
        static bool[] MaskArray(ChildList kids, double gx, double gy)
        {
            int n = kids.Count;
            bool[] hit = null;
            for (int i = 0; i < n; i++)
            {
                if (kids[i].ClipDepth is not { } clip || clip <= 0) continue;
                if (hit == null) { hit = System.Buffers.ArrayPool<bool>.Shared.Rent(n); Array.Fill(hit, true, 0, n); }
                hit[i] = PointTest(kids[i], gx, gy, true, false);
                int k = i + 1;
                for (; k < n && kids[k].Depth <= clip; k++) hit[k] = hit[i];
                i = k - 1;
            }
            return hit;
        }

        static void ReturnMaskArray(bool[] hit)
        {
            if (hit != null) System.Buffers.ArrayPool<bool>.Shared.Return(hit);
        }

        TopMostResult ContainerTopMost(AsContainer c, double gx, double gy, TopMostDescr d)
        {
            var holder = (c as AsSprite)?.IsHitAreaOf;
            // hidden containers take no mouse events unless they are a hit area; masks never do
            if (c.HitTestDisabled || (!c.IsVisible && holder == null) || c.MaskOwner != null) return Nothing(d);
            if (d.IgnoreMC == c) return Nothing(d);
            if (!LocalAndCheckBounds(c, gx, gy, out _, out _)) return TopMostResult.FoundNothing;
            if (c.MaskObject is { } mask && mask.MaskOwner == c && !PointTest(mask, gx, gy, true, false)) return Nothing(d);

            var hit = MaskArray(c.Children, gx, gy);
            try { return ContainerTopMost(c, gx, gy, d, holder, hit); }
            finally { ReturnMaskArray(hit); }
        }

        /// <summary>ContainerTopMost's children walk, with the clip-depth mask array (<see cref="MaskArray"/>).</summary>
        TopMostResult ContainerTopMost(AsContainer c, double gx, double gy, TopMostDescr d, AsSprite holder, bool[] hit)
        {
            var kids = c.Children;
            int n = kids.Count;
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
            using var memo = BoundsMemo.Scope.Open();
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
                if (o.Symbol?["scale9"] == null && !o.MemoBounds().Contains(lx, ly)) return false;
                if (ignoreInvisible && !o.IsVisible) return false;
                if (o.MaskObject is { } m && !PointTest(m, gx, gy, shapes, ignoreInvisible)) return false;
                var kids = c.Children;
                var hit = MaskArray(kids, gx, gy);
                try
                {
                    for (int i = kids.Count - 1; i >= 0; i--)
                    {
                        var k = kids[i];
                        if (ignoreInvisible && !k.IsVisible) continue;
                        if (hit != null && (!hit[i] || k.ClipDepth is > 0)) continue;
                        if (PointTest(k, gx, gy, shapes, ignoreInvisible)) return true;
                    }
                }
                finally { ReturnMaskArray(hit); }
                return o is AsSprite sp && sp.GraphicsOrNull is { } g && DrawingHit(g, lx, ly, shapes);
            }
            switch (o)
            {
                case AsTextField:
                    return (!ignoreInvisible || o.IsVisible) && o.HitsContent(gx, gy);
                case AsBitmap:
                    return o.MemoBounds().Contains(lx, ly);
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
        /// <summary>MouseState::IsMouseInsideEntityLast (sfr_as3_17.cpp:7038-7069, :7246, :7271): whether a release notes the
        /// click time (and may click or double click).</summary>
        bool mouseInsideEntityLast;

        AsClass mouseEventClass, mouseEventExClass;
        bool mouseEventExLooked;

        /// <summary>
        /// EventDispatcher::CreateMouseEventObject (sfr_as3_09.cpp:2303): with the extensions on (they are in the game) the
        /// event is a scaleform.gfx.MouseEventEx, the class the movie's ABC declares, constructed with the type alone; then
        /// bubbles (all but roll over/out), not cancelable, and the Ex fields mouseIdx (one mouse: 0) and buttonIdx (the
        /// EventId's ButtonId: the button pressed or released, 0 for the others). STILL-OPEN: nestingIdx is the EventId's
        /// RollOverCnt, which this player does not count; it stays 0.
        /// </summary>
        void MouseEv(AsInteractiveObject t, string type, AsObject related = null, int delta = 0, int button = 0)
        {
            // no listener on the target or its ancestors (the capture and bubble path): nothing would see it (as Send)
            if (t == null || !t.willTrigger(type)) return;
            var (lx, ly) = t.GlobalToLocal(MouseX, MouseY);
            bool bubbles = type is not ("rollOver" or "rollOut");
            RT.Guard(type, () =>
            {
                mouseEventClass ??= RT.GetClass("flash.events.MouseEvent");
                if (!mouseEventExLooked) { mouseEventExLooked = true; mouseEventExClass = RT.GetClass("scaleform.gfx.MouseEventEx"); }
                var e = (AsMouseEvent)(mouseEventExClass ?? mouseEventClass).Construct(type);
                e.bubbles = bubbles; e.cancelable = false;
                e.localX = lx; e.localY = ly; e.StageX = MouseX; e.StageY = MouseY; e.buttonDown = MouseDown; e.relatedObject = related; e.delta = delta; e.ButtonIdx = button;
                if (mouseEventExClass != null)
                {
                    RT.SetProperty(e, Name.Public("mouseIdx"), Rt.Box(0));
                    RT.SetProperty(e, Name.Public("buttonIdx"), Rt.Box(button));
                }
                t.Dispatch(e);
            });
        }

        /// <summary>
        /// A mouse move. covered: another movie's object is over the point. The client's dialogs share main.gfx's one stage,
        /// so its top-most entity there is that movie's object (GetTopMostEntity) and none of this movie's: what the mouse was
        /// over rolls out, and the move goes to the stage as it bubbles there. Here each movie has its own stage, and the
        /// host tells it.
        /// </summary>
        public void MouseMove(double x, double y, bool covered = false)
        {
            MouseX = x; MouseY = y; MouseActivated = true;
            UpdateDrag();               // Stage::PropagateMouseEvent: a mouse move drags what is being dragged
            // TextField's mouse handler (sfr_gfx_03e.cpp:1583) passes the move to the editor that has the mouse capture,
            // wherever the mouse is, before the AS3 events
            if (TextCapture is { MouseCaptured: true, visible: true, OnStage: true } c) { var (lx, ly) = c.GlobalToLocal(x, y); c.MouseMove(lx, ly); }
            var t = covered ? null : MouseTarget(x, y);
            Hover(t);
            MouseEv(t ?? (AsInteractiveObject)Stage, "mouseMove");
        }

        /// <summary>Whether a mouse event came (MouseState::IsActivated): before one, the mouse has no position.</summary>
        public bool MouseActivated { get; private set; }

        /// <summary>
        /// MovieImpl::ProcessInput's mice without an event (sfr_gfx_01i.cpp:3126-3141): after a display list change
        /// (Flag_NeedMouseUpdate) the top-most entity under the mouse's last position is found again and
        /// GenerateMouseEvents runs the change's mouseOut / rollOut and mouseOver / rollOver (no mouseMove: the mouse did not
        /// move). covered: as for <see cref="MouseMove"/>.
        /// </summary>
        public void MouseRetest(bool covered)
        {
            if (!MouseActivated) return;
            Hover(covered ? null : MouseTarget(MouseX, MouseY));
        }

        /// <summary>The mouse's top-most entity becomes t: mouseOut and rollOut up from the old one, then mouseOver and
        /// rollOver down to the new one.</summary>
        void Hover(AsInteractiveObject t)
        {
            // a change of the top-most entity sets IsMouseInsideEntityLast (sfr_as3_17.cpp:7246), after the drag block cleared
            // it for the change (:7060-7068): only a release in the same call sees it cleared (MouseButton)
            if (t != over)
            {
                mouseInsideEntityLast = true;
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
        }

        static bool Contains(AsDisplayObject a, AsDisplayObject b) { for (var o = b; o != null; o = o.Parent) if (o == a) return true; return false; }

        /// <summary>The mouse button index of <see cref="MouseButton(bool, int)"/>: MouseEventEx.LEFT_BUTTON, RIGHT_BUTTON,
        /// MIDDLE_BUTTON.</summary>
        public const int LeftButton = 0, RightButton = 1, MiddleButton = 2;

        readonly AsInteractiveObject[] pressedBy = new AsInteractiveObject[3];     // GetMouseButtonDownEntity(buttonIdx)
        // MouseState::DblClick[buttonIdx] (GetDoubleClickInfo): the last click's time (ms, 0 = none) and stage pixel
        readonly (uint Time, int X, int Y)[] lastClick = new (uint, int, int)[3];

        static readonly System.Diagnostics.Stopwatch wallClock = System.Diagnostics.Stopwatch.StartNew();

        /// <summary>The double click's clock in ms: GenerateMouseEvents reads Timer::GetTicks()/1000 as a UInt32
        /// (sfr_as3_17.cpp:7134), the wall clock, not the movie's frame timer, which falls behind when frames are dropped.
        /// Tests set their own.</summary>
        public Func<double> Clock = () => wallClock.Elapsed.TotalMilliseconds;

        /// <summary>GenerateMouseEvents' double-click window: a second release within this many ms of the last click, at the
        /// same stage pixel (sfr_as3_17.cpp:7140, GFX_DOUBLE_CLICK_TIME_MS).</summary>
        public const double DoubleClickMs = 300;
        /// <summary>
        /// A mouse button (<see cref="LeftButton"/>, <see cref="RightButton"/>, <see cref="MiddleButton"/>). MovieRoot::
        /// GenerateMouseEvents (sfr_as3_17.cpp:7007) runs per button index: the mouse up, the wheel and the mouse move go to
        /// the top-most entity, or to the stage when there is none (topmostEntitySafe), so a release over nothing still
        /// reaches the stage's listeners (DragManager's MOUSE_UP); the mouse down likewise (activeEntitySafe, :7265), and
        /// the click goes where both went, each with that button's index as buttonIdx. Only a left press moves the focus
        /// (MovieImpl's mouse handling, sfr_gfx_01i.cpp:3673), and MouseEvent.buttonDown is the left button (ButtonsMask &amp; 1,
        /// sfr_as3_09.cpp:1708). A second release soon after a click at the same pixel is a doubleClick instead of a click
        /// when the target has doubleClickEnabled (<see cref="DoubleClickMs"/>): the client hands a WM_LBUTTONDBLCLK to the
        /// movie as a plain mouse down (GFxMovieEventHandler @0x1422da5a0), so the press after a double click is no different
        /// here. The double click runs for every button: the game's movies turn the extensions on (CLIK.initialize sets
        /// Extensions.enabled), so GenerateMouseEvents checks 16 buttons, not 1 (checkCount, :7032). main.gfx's stage listeners turn the right button's events into the right* events
        /// (<see cref="ListenForRightButton"/>).
        /// </summary>
        public void MouseButton(bool down, int button = LeftButton)
        {
            if ((uint)button >= (uint)pressedBy.Length) return;
            if (button == LeftButton) MouseDown = down;
            var t = MouseTarget(MouseX, MouseY);
            if (button == LeftButton) EditorMouse(t, down);
            if (down)
            {
                pressedBy[button] = t ?? Stage;     // SetMouseButtonDownEntity(activeEntitySafe)
                mouseInsideEntityLast = true;       // :7271
                // a left press moves the focus to the top-most entity (MovieImpl's mouse handling, sfr_gfx_01i.cpp:3673; no
                // ancestor of it is asked), and QueueSetFocusTo (:2672) takes NULL for one that is not focus enabled
                // (IsFocusEnabled by mouse), so a press on nothing focusable, or on nothing, clears the focus; the change
                // event names the entity pressed. QueueSetFocusTo sends the mouseFocusChange (NotifyOnFocusChange, :2688),
                // then tells the AS3 root (NotifyQueueSetFocus, :2698), whose slot moves the focus at once
                // (MovieImpl::TransferFocus, sfr_as3_18.cpp:9444) with its focusOut/focusIn; GenerateMouseEvents sends the
                // mouseDown after that (:3681), so its listeners see the focus already moved, and one that sets the focus wins.
                if (button == LeftButton)
                {
                    var focusTo = t != null && MouseFocusEnabled(t) ? t : null;
                    SetFocus(focusTo, "mouse", focusTo == null ? t : null);
                }
                MouseEv(t ?? (AsInteractiveObject)Stage, "mouseDown", button: button);     // activeEntitySafe, sfr_as3_17.cpp:7265
            }
            else
            {
                var pressed = pressedBy[button];
                var top = t ?? (AsInteractiveObject)Stage;
                MouseEv(top, "mouseUp", button: button);
                // the drag block (sfr_as3_17.cpp:7038-7069): with the left button held, a release whose top-most entity is
                // not the active one (the mouse's last entity) clears IsMouseInsideEntityLast for this release
                bool inside = mouseInsideEntityLast;
                if (pressedBy[LeftButton] != null && t != over) inside = false;
                if (pressed != null && inside)      // the button was down (GetPrevButtonsState() & buttonMask)
                {
                    // sfr_as3_17.cpp:7131-7165: a release within 300 ms of the last click at the same pixel is a double click.
                    // The top-most entity gets doubleClick when its doubleClickEnabled is set; a click needs the release over
                    // the entity the press went to. The last click is noted on every such release, also one away from the press.
                    uint tm = (uint)(ulong)Clock();
                    int ax = (int)MouseX, ay = (int)MouseY;
                    ref var dci = ref lastClick[button];
                    bool dblClick = dci.Time != 0 && tm <= unchecked(dci.Time + (uint)DoubleClickMs) && dci.X == ax && dci.Y == ay;
                    if (dblClick && top.doubleClickEnabled) MouseEv(top, "doubleClick", button: button);
                    else if (top == pressed) MouseEv(top, "click", button: button);
                    dci = (dblClick ? 0 : tm, ax, ay);
                }
                if (t != over) mouseInsideEntityLast = true;    // :7246, the top-most entity changed in this call
                pressedBy[button] = null;
            }
        }

        /// <summary>The text field whose editor took the mouse capture with the last left press (null for none).</summary>
        public AsTextField TextCapture { get; private set; }

        /// <summary>Whether a text field's editor has the mouse capture: its moves and its release go to it wherever the
        /// mouse is, as a drag's do.</summary>
        public bool TextMouseCaptured => TextCapture is { MouseCaptured: true };

        /// <summary>
        /// The left button for a text field's editor (TextField's mouse handler, sfr_gfx_03e.cpp:1583): MovieImpl's mouse
        /// handling propagates the event to the display list (Stage::PropagateMouseEvent) before it moves the focus and
        /// runs the AS3 events (sfr_gfx_01i.cpp:3612). The top-most entity, when it is a visible field with an editor, gets
        /// the press in its own units (the world matrix's inverse); an AS3 movie's top-most test takes every field
        /// (GetTopMostEntity with testAll = avm2, sfr_gfx_01i.cpp:3621), selectable or not. The release goes to the field
        /// with the capture, or to the top-most field.
        /// </summary>
        void EditorMouse(AsInteractiveObject t, bool down)
        {
            if (down)
            {
                if (t is AsTextField { HasEditor: true, visible: true } tf)
                {
                    var (lx, ly) = tf.GlobalToLocal(MouseX, MouseY);
                    tf.MouseDown(lx, ly, (uint)(ulong)Clock());
                    if (tf.MouseCaptured) TextCapture = tf;
                }
                return;
            }
            var to = TextCapture is { MouseCaptured: true } c ? c : t as AsTextField;
            if (to is { HasEditor: true, visible: true }) to.MouseUp();
            if (TextCapture is { MouseCaptured: false }) TextCapture = null;
        }

        /// <summary>The classes main.gfx's Main.useRClickEvent leaves alone: their right button events are not redispatched.</summary>
        static readonly string[] RightClickOwnClasses =
        {
            "scaleform.ms2.controls.RClickDropdown", "scaleform.clik.controls.ButtonBar", "scaleform.ms2.renderer.SkillRenderer",
            "scaleform.ms2.renderer.ComponentRenderer", "scaleform.ms2.renderer.GroupMessengerUserRenderer",
            "scaleform.ms2.unit.construct.CubeRenderer",
        };

        /// <summary>
        /// The right button's events as main.gfx's scaleform.ms2.Main makes them. Main.addedToStage puts onHandleMouseClick,
        /// onHandleMouseDown, onHandleMouseUp and onHandleMouseDoubleClick on the stage for click, mouseDown, mouseUp and
        /// doubleClick, capture and bubble, priority 10000; each returns at once when useRClickEvent(target) (the target is
        /// one of <see cref="RightClickOwnClasses"/>), and for buttonIdx == MouseEventEx.RIGHT_BUTTON stops the event
        /// (stopImmediatePropagation, stopPropagation) and dispatchMouseEventToTarget: a new MouseEvent of EventString's
        /// rightClick, rightMouseDown, rightMouseUp or rightDoubleClick with the event's bubbles, cancelable, localX, localY,
        /// relatedObject, ctrlKey, altKey, shiftKey, buttonDown and delta, dispatched on the event's target. In the client
        /// every dialog is in main.gfx's one stage; here each movie has its own, so each gets these listeners.
        /// </summary>
        void ListenForRightButton()
        {
            var rt = RT;
            AsClass[] own = null;
            void Listen(string type, string rightType)
            {
                var fn = new AsPlainFunction(rt, new NativeMethod("Main.onHandleMouse " + type, (_, a) =>
                {
                    if (a.Length == 0 || a[0] is not AsMouseEvent e) return Rt.Undef;
                    own ??= RightClickOwnClasses.Select(rt.GetClass).Where(c => c != null).ToArray();
                    if (own.Any(c => rt.IsType(e.target, c))) return Rt.Undef;          // useRClickEvent
                    if (e.ButtonIdx != RightButton) return Rt.Undef;
                    e.stopImmediatePropagation();
                    e.stopPropagation();
                    if (e.target is not { } target) return Rt.Undef;                     // dispatchMouseEventToTarget
                    var r = (mouseEventClass ??= rt.GetClass("flash.events.MouseEvent")).Construct(rightType, Rt.Box(e.bubbles), Rt.Box(e.cancelable), e.localX, e.localY, e.relatedObject,
                        Rt.Box(e.ctrlKey), Rt.Box(e.altKey), Rt.Box(e.shiftKey), Rt.Box(e.buttonDown), Rt.Box(e.delta));
                    rt.CallProperty(target, Name.Public("dispatchEvent"), new object[] { r });
                    return Rt.Undef;
                }));
                Stage.addEventListener(type, fn, false, 10000, true);
                Stage.addEventListener(type, fn, true, 10000, true);
            }
            Listen("click", "rightClick");
            Listen("mouseDown", "rightMouseDown");
            Listen("mouseUp", "rightMouseUp");
            Listen("doubleClick", "rightDoubleClick");
        }

        /// <summary>
        /// A wheel step over the mouse position (MovieImpl::ProcessMouse, sfr_gfx_01i.cpp:3667): the top-most entity's
        /// OnMouseWheelEvent runs first (a text field scrolls its text, TextField::OnMouseWheelEvent, sfr_gfx_03e.cpp:915; anything
        /// else does not, InteractiveObject, sfr_as3_18.cpp:8911), then the AS3 mouseWheel event is generated
        /// (GenerateMouseEvents), so a handler sees the field already scrolled.
        /// </summary>
        public void Wheel(int delta)
        {
            var t = MouseTarget(MouseX, MouseY) ?? (AsInteractiveObject)Stage;
            if (t is AsTextField tf) tf.OnMouseWheel(delta);
            MouseEv(t, "mouseWheel", null, delta);
        }

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
            // EditorKit's shift state (sfr_gfx_07.cpp): Shift down on the focused selectable field enters the selection mode
            // (KeyAct_EnterSelectionMode), any Shift up leaves it (OnKeyUp)
            if (keyCode == 16 && Focus is AsTextField sf && sf.HasEditor)
            {
                if (!down) sf.ShiftPressed = false;
                else if (proceed && sf.selectable) sf.ShiftPressed = true;
            }
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
            if (Focus is AsTextField tf && (tf.type == "input" || tf.selectable))
                RT.Guard("text input", () =>
                {
                    switch (EditKey(keyCode, shift, ctrl))
                    {
                        case EditAction.Copy: tf.Copy(Clipboard); return;
                        case EditAction.Cut: tf.Cut(Clipboard); return;
                        case EditAction.Paste: tf.Paste(Clipboard); return;
                        case EditAction.SelectAll: tf.SelectAll(); return;
                    }
                    if (CaretKeyOf(keyCode, ctrl) is { } caret) { tf.MoveCaret(caret, shift); return; }
                    if (tf.type != "input") return;
                    if (keyCode == 8) { tf.Backspace(); return; }
                    if (keyCode == 46 && !shift) { tf.Delete(); return; }
                    if (charCode >= 32 && !ctrl) TypeInto(tf, ((char)charCode).ToString());
                });
        }

        public enum EditAction { None, Copy, Cut, Paste, SelectAll }

        /// <summary>
        /// The clipboard keys of the client's text key map (TextKeyMap::InitWindowsKeyMap @0x14258e0c0,
        /// sfr_gfx_01c.cpp:191): Ctrl+C and Ctrl+Insert copy, Ctrl+X and Shift+Delete cut, Ctrl+V and Shift+Insert paste,
        /// Ctrl+A selects all. An entry matches when the held modifiers include its own (TextKeyMap::Find @0x14258dae0),
        /// and for one key the later entry wins, so Ctrl+Shift+Insert pastes.
        /// </summary>
        public static EditAction EditKey(uint keyCode, bool shift, bool ctrl) => keyCode switch
        {
            67 when ctrl => EditAction.Copy,                                   // C
            88 when ctrl => EditAction.Cut,                                    // X
            86 when ctrl => EditAction.Paste,                                  // V
            65 when ctrl => EditAction.SelectAll,                              // A
            45 when shift => EditAction.Paste,                                 // Insert
            45 when ctrl => EditAction.Copy,
            46 when shift => EditAction.Cut,                                   // Delete
            _ => EditAction.None,
        };

        /// <summary>
        /// The caret keys of the client's text key map (TextKeyMap::InitWindowsKeyMap @0x14258e0c0): the arrows, Home / End
        /// for the line, Ctrl+Home / Ctrl+End for the text, PageUp / PageDown, Ctrl+PageUp / Ctrl+PageDown for the visible
        /// lines. Extra modifiers do not matter (TextKeyMap::Find takes the first entry whose modifiers are held, the Ctrl
        /// entries first), so Ctrl+Left is Left and Ctrl+Shift+Home selects to the start of the text.
        /// </summary>
        public static CaretKey? CaretKeyOf(uint keyCode, bool ctrl) => keyCode switch
        {
            0x25 => CaretKey.Left,
            0x27 => CaretKey.Right,
            0x26 => CaretKey.Up,
            0x28 => CaretKey.Down,
            0x24 => ctrl ? CaretKey.DocHome : CaretKey.LineHome,
            0x23 => ctrl ? CaretKey.DocEnd : CaretKey.LineEnd,
            0x21 => ctrl ? CaretKey.PageHome : CaretKey.PageUp,
            0x22 => ctrl ? CaretKey.PageEnd : CaretKey.PageDown,
            _ => null,
        };

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

        /// <summary>IsFocusEnabled(GFx_FocusMovedByMouse): any text field (AvmTextField, as3_avm.h:651: only the keyboard
        /// tests read-only) and any SimpleButton (AvmButton, as3_avm.h:595) always; a sprite in button mode or tabable
        /// (AvmSprite::IsFocusEnabled @0x1425142a0, sfr_as3_18.cpp:1132, Flag_ButtonMode before IsTabable); the stage and
        /// another container never (Stage @0x142514310, AvmDisplayObjContainer @0x142514290).</summary>
        static bool MouseFocusEnabled(AsInteractiveObject io) => io switch
        {
            AsTextField or AsSimpleButton => true,
            AsSprite sp => sp.buttonMode || SpriteTabable(sp),
            _ => false,
        };

        /// <summary>AvmSprite::IsTabable (@0x142514fc0, sfr_as3_18.cpp:1147): visible, and tabEnabled when set (true only
        /// if no ancestor has tabChildren off), else a positive tabIndex, else buttonMode.</summary>
        static bool SpriteTabable(AsSprite sp)
        {
            if (!sp.visible) return false;
            if (sp.TabEnabledSet is bool te)
            {
                if (!te) return false;
                for (var p = sp.Parent; p != null; p = p.Parent) if (p is AsContainer { tabChildren: false }) return false;
                return true;
            }
            return sp.tabIndex > 0 || sp.buttonMode;
        }

        /// <summary>The focused object, or one under it, left the display list: the focus goes with no events. A text field
        /// still loses its editor's focus (OnEventUnload, ResetFocusForChar, SetFocusTo(NULL), TextField::OnFocus), which
        /// ends its mouse capture and its Shift state (EditorKit::OnKillFocus).</summary>
        public void ClearFocusSilently()
        {
            if (Focus is AsTextField { HasEditor: true } tf) tf.KillEditorFocus();
            TextCapture = null;
            Focus = null;
        }

        /// <summary>Moves the focus to <paramref name="o"/> (null for none). With <paramref name="how"/> the focused object (or
        /// the stage) first gets the cancelable mouseFocusChange / keyFocusChange, its relatedObject <paramref name="related"/>
        /// when given (the entity a press went to, QueueSetFocusTo's ch before IsFocusEnabled), else <paramref name="o"/>.</summary>
        public void SetFocus(AsInteractiveObject o, string how, AsInteractiveObject related = null)
        {
            // QueueSetFocusTo (sfr_gfx_01i.cpp:2688) compares the entity it was given, before IsFocusEnabled, with the
            // focus: a press on something not focusable while nothing has the focus still sends the change event
            if (ReferenceEquals(related ?? o, Focus)) return;
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
                    e.relatedObject = related ?? o;
                    ok = from.Dispatch(e);
                });
                if (!ok) return;
            }
            if (ReferenceEquals(o, Focus)) return;
            Focus = o;
            // TextField::OnFocus @0x1425ba020 (sfr_gfx_03e.cpp:1057), selectable fields only: the field losing the focus drops
            // its selection unless alwaysShowSelection (EditorKit::ClearSelection), and the field getting it selects all of
            // its text only when the keyboard moved the focus (GFx_FocusMovedByKeyboard; a press and stage.focus, which is
            // GFx_FocusMovedByAS, sfr_as3_07.cpp:4007, keep its selection). Flags_NoAutoSelection, the one other condition,
            // has only an AS2 setter.
            if (old is AsTextField was && was.selectable && !was.alwaysShowSelection) was.ClearSelection();
            // EditorKit::OnKillFocus @0x142618bb0: the shift state and the mouse capture end
            if (old is AsTextField lost && lost.HasEditor) lost.KillEditorFocus();
            if (o is AsTextField tf && tf.selectable && how == "key") tf.SelectAll();
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
        /// <summary>A sprite is being dragged (startDrag until stopDrag): it follows the mouse wherever the mouse is.</summary>
        public bool Dragging => dragging != null;
        /// <summary>The sprite being dragged, or null.</summary>
        public AsSprite DraggedSprite => dragging;
        /// <summary>Ends a drag without any event (the movie is going away mid-drag).</summary>
        public void CancelDrag() => dragging = null;
        /// <summary>A sprite the host draws over every other movie of the screen (DragManager's drag sprite, set by the
        /// screen's stage), or null.</summary>
        public AsDisplayObject DrawnOnTop
        {
            get => drawnOnTop;
            set { if (drawnOnTop == value) return; drawnOnTop?.MarkChanged(); drawnOnTop = value; value?.MarkChanged(); }
        }
        AsDisplayObject drawnOnTop;
        /// <summary>
        /// InteractiveObject::DoMouseDrag (sfr_gfx_06b.cpp:167): the dragged sprite at the mouse in its parent's space plus the
        /// centre delta, clamped to the bounds. Called on every mouse move (Stage::PropagateMouseEvent, sfr_as3_18.cpp:4486)
        /// and every frame (AvmSprite::AdvanceFrame, sfr_as3_16z.cpp:632).
        /// </summary>
        public void UpdateDrag()
        {
            if (dragging == null) return;
            var (px, py) = dragging.Parent?.GlobalToLocal(MouseX, MouseY) ?? (MouseX, MouseY);
            double nx = px + dragDx, ny = py + dragDy;
            if (dragBounds is { } b && !b.IsEmpty) { nx = Math.Clamp(nx, b.X, b.X + b.W); ny = Math.Clamp(ny, b.Y, b.Y + b.H); }
            var dm = dragging.Matrix; dm.Tx = nx; dm.Ty = ny; dragging.Matrix = dm; dragging.ScriptMoved();
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

    /// <summary>Several movies drawn as one stage: Sprite.dropTarget's search over all of them (see <see cref="Player.DropStage"/>).</summary>
    public interface IDropStage
    {
        /// <summary>The drop target under the mouse for <paramref name="dragged"/>, a sprite of <paramref name="source"/>.</summary>
        AsDisplayObject DropTarget(Player source, AsDisplayObject dragged);
    }
}
