using Godot;
using Ms2.Formats.World;
using Ms2.Ui.Hud;
using Ms2Godot.World;

namespace Ms2Godot.Ui;

/// <summary>
/// A damage number's target as UIDamageText finds it by its key each frame (WorldService::FindObjectByKey): its root
/// (the key; a freed root is a target gone), its Eff_Damage point now (GetAnchorPosition with m_strDamageNode, its
/// position without the dummy) and whether it is a PC (GetTargetKind 0). <paramref name="body"/> is its Eff_Body point
/// (m_strBodyNode, where an exp gain shows); the Eff_Damage point without one.
/// </summary>
public sealed class DamageTarget(Node3D root, Func<Vector3> point, bool pc, Func<Vector3> body = null)
{
    public readonly Node3D Root = root;
    public readonly Func<Vector3> Point = point;
    public readonly Func<Vector3> Body = body ?? point;
    public readonly bool Pc = pc;
    public bool Gone => !GodotObject.IsInstanceValid(Root) || !Root.IsInsideTree();
}

/// <summary>
/// Floating combat numbers (UIDamageText, the uidamagetext movie), default mode only (UpdateNormals + UpdateOwnerFollowers;
/// the old "Clearing" mode is a dev toggle). Hits and heals on npcs, the own PC and the other players
/// (FUN_141911e90): grades 0 Normal, 1 Critical, 2 Miss, 3 Block and the recover grades 5, 6 and 7, picked by the
/// movie's factory as <see cref="DamageLook"/> says; and the gains (FUN_141912a80, FUN_141913060): exp, meso and items.
/// <list type="bullet">
/// <item>The call's object: target User for the own PC and another PC, Monster for an npc; myDamage when the target or
/// the caster is the local player; scale the interface DamageTextScaleStep / 100; for a number not mine, the battle
/// option OtherDamageForm bit 1 sets diffScale (DamageOtherScale, times the scale) and bit 2 diffAlpha (DamageOtherAlpha,
/// on the ani and on its copy).</item>
/// <item>The ani (damageText): the number in "dummy" (its placeholder hidden), a copy in "dummy1" (placeholder hidden),
/// the critical text in "cridmy" (and a duplicate in "cridmy1"), the extra effect in "exeff" (hidden without one); the
/// ani is raised by half the number's height. The wrapper goes to its container: the other numbers' normal and critical
/// lists, then the own normal and critical, in that draw order.</item>
/// <item>Gem fonts (bGem, uidamagetext.cpp:354, :399-407): a PC caster's usable type 2 gem names its font for a damage
/// grade on another target; once GFxMovieDamage::LoadDamageFont (gfxmoviedamage.cpp:210, <see cref="DamageFonts"/>) has
/// loaded uidamagefont_&lt;gem id&gt;, the monster numbers take its Nor / Cri / Miss / Block glyphs and dmg_mc_monstercri
/// text (loadDamageFont); the data's font movies hold only the digits, so a gem Miss or Block is empty and a gem critical
/// takes the plain critical text (getCriticalTxt's fallback, <see cref="DamageLook.FontCritText"/>). The loads come from <see cref="NpcEmoticons.DamageFontAsked"/>; a field move (ClearAll) unloads
/// the fonts not asked for in the last 60 s (UnloadExpiredFonts, :249).</item>
/// <item>Pooling: the ani sets, glyph nodes, numbers, clips and entries are reused (no per-hit allocation); a clip's
/// generation keeps a stale entry from touching its next use. The client has no pool (Scaleform makes the AS objects per
/// hit), so the pools are the port's own: they are filled at load with one clip of each common look (<see cref="Warm"/>),
/// grow to the most clips shown at once and never shrink, a field move (ClearAll) keeps them, and the HUD's end frees
/// them (Predelete), the pooled nodes being outside the tree.</item>
/// <item>Text: glyphs left to right, each advancing by its right edge plus the grade's movie spacing, a leading "+" on
/// top; Miss and Block are one image glyph. The number is centred on its own bounds.</item>
/// <item>Spawn: the target's Eff_Damage point, stored once (a hit point is not sent here). A number is not created when
/// the point is behind the camera, nor for a target out of show distance. Recover grades follow the owner's Eff_Damage
/// every frame (the followers list); the others stay put (the normals list).</item>
/// <item>Stack, scale and place: <see cref="DamageNormalsPass"/> and <see cref="DamageList{T}"/>; the distance scale
/// FUN_141914a90 (a follower's from its owner's root, 1 for the own PC), the camera-pitch shift FUN_1412762d0.</item>
/// <item>Gains (FUN_141912b20, FUN_141913060): exp at the target's Eff_Body point, meso and items at Eff_Damage; exp and
/// meso only with the battle option's DisplayGetExp / DisplayGetMeso; the call's object has no scale, caster or
/// myDamage (scale 1, the other numbers' containers), an item always goes to the own containers (itemIcon). The entry
/// is type 1 grade 1 (never hidden, never lifted, never jumps), always appended, mine when the target is the own PC.
/// Quest exp (OnExpUp's types 0xc1f-0xc22) waits in the queue (FUN_1419129d0), one shown each 200 ms (FUN_141916340).
/// The DamageMovie: the Exp glyph before the digits (no such class in the KR Live or the restore movie: preGradeText
/// reads its x of null, the init throws and an exp gain shows nothing), the Meso glyph after them (moved to child 0), the item's icon
/// (the ItemMovie's "icon" with the item image fitted to its width) before them; the ani's mesoeff at child 0.</item>
/// </list>
/// STILL-OPEN: a summon's numbers as the own (bMySummon),
/// observer mode, PC::vfunc_122's pvp and pk rules and the caster's forceApplyTarget bit 2 (another PC is always User
/// here), m_nRepeat (a dev repeat, 1).
/// </summary>
public sealed partial class DamageText : CanvasLayer
{
    /// <summary>An ani of one class with its slots, built once and reused.</summary>
    sealed class AniSet
    {
        public MovieClipNode Ani;
        public string Class;
        public MovieClipNode Dummy, Dummy1, Cridmy, Cridmy1, ExNode;
        public Node2D Mesoeff;
        public MovieClipNode.Child ExSlot, MesoSlot;
        public int Epoch;
    }

    /// <summary>A DamageMovie (m_dmg): the holder and the glyphs in it, each with the movie and class it came from.</summary>
    sealed class Num
    {
        public readonly Node2D Node = new() { Name = "DamageMovie" };
        public readonly Parts Parts = new();
        public MovieClipNode Icon;     // the item icon: from pickups, not pooled
    }

    readonly struct Part(UiMovie m, string cls, MovieClipNode n)
    {
        public readonly UiMovie Movie = m;
        public readonly string Class = cls;
        public readonly MovieClipNode Node = n;
    }

    /// <summary>
    /// The parts of a number or clip: an array that only grows, not a List of the struct (the game assembly runs under the
    /// JIT, and a List over a struct of its own is a new instantiation of every List method it calls at the first hit).
    /// </summary>
    sealed class Parts
    {
        Part[] items = new Part[4];
        public int Count { get; private set; }
        public ref readonly Part this[int i] => ref items[i];
        public void Add(in Part p)
        {
            if (Count == items.Length) System.Array.Resize(ref items, Count * 2);
            items[Count++] = p;
        }
        public void Clear() { System.Array.Clear(items, 0, Count); Count = 0; }
    }

    sealed class Clip
    {
        public readonly Node2D Wrap = new() { Name = "wrapper", Visible = false };
        public AniSet Set;
        public MovieClipNode Ani;
        public int[] Remove;
        public double Frame;          // the ani's playhead, at the movie's frame rate
        public double Age;            // frames since creation: the looping clips added to its slots
        public MovieClipNode Loop1, Loop2, Loop3;
        public Num Number, Copy;
        public readonly Parts Parts = new();    // the critical texts and the extra effect
        public int Container = -1;
        public int Epoch;
        public int Gen;               // bumped on release: an entry holding an older generation sees its clip gone
        public bool Gone;
    }

    sealed class Entry : DamageInfo
    {
        public Clip Clip;
        public int Gen;
        public Vector3 Pos;            // the stored world point (normals)
        public DamageTarget Target => (DamageTarget)Owner;
        /// <summary>The clip is still this entry's and not removed.</summary>
        public bool Live => Clip.Gen == Gen && !Clip.Gone;
    }

    readonly UiMovie movie;
    float u = 1;
    int epoch;                         // the scale the pooled nodes were built at: bumped on a resize
    readonly DamageTextLayout layout;
    readonly DamageList<Entry> normals, followers;
    readonly List<Clip> clips = new();
    readonly Dictionary<Node3D, DamageTarget> targets = new();
    readonly List<Node3D> goneTargets = new();
    readonly Node2D[] containers = new Node2D[4];
    readonly Ms2.Formats.Pack.PackSet packs;
    readonly UiAssets assets;
    static readonly NodePath ModelPath = "Model";
    readonly Queue<(int Value, int Type)> queue = new();     // m_queue: quest exp for the own PC (DamageQueueParam)
    double lastQueueTime;              // m_nLastQueueTime
    double clock;                      // ms
    Camera3D camera;
    Field field;
    Player player;
    DamageTarget self;
    static readonly Ms2.Formats.Options.GameOption.Battle defaultBattle = new();

    // per-hit cost: the looks, the nodes, the clips and the entries are reused, so a hit allocates nothing once warm
    readonly DamageLookCache looks = new();
    readonly char[] text = new char[24];
    /// <summary>
    /// A movie's pooled glyph nodes by class, and the classes it does not have. Keyed by reference types only, so the
    /// lookups run the shared code the ReadyToRun image holds (a (movie, class) tuple key is compiled at the first hit).
    /// </summary>
    sealed class MoviePool
    {
        public readonly Dictionary<string, Stack<MovieClipNode>> Nodes = new();
        public readonly HashSet<string> Missing = new();
        public void Free() { foreach (var st in Nodes.Values) while (st.TryPop(out var n)) n.Free(); }
    }
    readonly Dictionary<UiMovie, MoviePool> nodePool = new();
    readonly Dictionary<string, Stack<AniSet>> aniPool = new();
    readonly Stack<Num> numPool = new();
    readonly Stack<Clip> clipPool = new();
    readonly Stack<Entry> entryPool = new();

    // GFxMovieDamage's gem fonts (m_90): the font movies by id text
    readonly DamageFonts fonts = new();
    readonly Dictionary<string, UiMovie> fontMovies = new();
    readonly List<int> expired = new();

    public DamageText(UiAssets ui, ClientConstants constants, Vector2 window)
    {
        layout = DamageTextLayout.From(constants);
        normals = new(layout.MaxIndex);
        followers = new(layout.MaxIndex);
        Name = "DamageText";
        Layer = 5;                     // with the name tags and HP bars, added after them; the client's order of the movies is not known
        MovieLayerHost.Bind(this, Ms2.Ui.CommonDepth.Damage);   // UIDamageText's layer is 8 (m_2c, uidamagetext.cpp:207)
        Offset = UiScale.MovieOrigin;
        packs = ui.Packs;
        assets = ui;
        u = UiScale.For(window);
        // DamageTextFactory's containers in their draw order (DamageLook.ListNormal .. MyCritical)
        string[] names = ["m_listNormal", "m_listCritical", "m_myDamageNormal", "m_myDamageCritical"];
        for (int i = 0; i < containers.Length; i++) AddChild(containers[i] = new Node2D { Name = names[i] });
        try { movie = new UiMovie(ui, "uidamagetext"); } catch (Exception e) { GD.PushWarning("damage text movie: " + e.Message); }
        Warm();
    }

    /// <summary>
    /// One clip of each common look built and released at load: a monster's normal and critical number dealt by the own PC
    /// and a normal number on the own PC. The port's pools and the code they run are then ready before the first hit,
    /// which the client does not need (it has no pool). Public for the checks.
    /// </summary>
    public void Warm()
    {
        if (movie == null) return;
        WarmLook(false, 0);
        WarmLook(false, 1);
        WarmLook(true, 0);
    }

    void WarmLook(bool pc, int grade)
    {
        string target = DamageTextNames.TargetKindName(DamageTextNames.TargetKind(pc), true);
        int len = DamageTextNames.ValueChars(12345, grade, text);
        string kind = DamageTextNames.ValueKind(grade);
        var look = looks.Get(kind, target, DamageTextNames.GradeName(grade), null, true);
        var clip = Build(look, text.AsSpan(0, len), DamageLook.IsNumber(kind), "", null, 1, -1);
        if (clip == null) return;
        clips.Remove(clip);
        Kill(clip);
        Release(clip);
    }

    /// <summary>The nodes the pools hold, for the checks: they are outside the tree.</summary>
    public int PooledNodes
    {
        get
        {
            int n = numPool.Count + clipPool.Count;
            foreach (var mp in nodePool.Values) foreach (var st in mp.Nodes.Values) n += st.Count;
            foreach (var st in aniPool.Values) n += st.Count;
            return n;
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete) FreePools();
    }

    /// <summary>The HUD goes: the pooled nodes are outside the tree, so the tree's free does not reach them.</summary>
    void FreePools()
    {
        foreach (var mp in nodePool.Values) mp.Free();
        nodePool.Clear();
        foreach (var st in aniPool.Values) while (st.TryPop(out var set)) set.Ani.Free();
        aniPool.Clear();
        while (numPool.TryPop(out var n)) n.Node.Free();
        while (clipPool.TryPop(out var c)) c.Wrap.Free();
    }

    public DamageText() { }

    /// <summary>
    /// The window was resized: the movie's new viewport scale (GFxManager::ResizeMovies, GFxMovie::vfunc_7,
    /// gfxmovie.cpp:146), kept alive with its state. STILL-OPEN: the clips already built keep the glyphs they were built
    /// with at the old scale and are only scaled; the client's Scaleform draws them again at the new viewport scale.
    /// </summary>
    public void Resize(Vector2 window)
    {
        float nu = UiScale.For(window);
        if (nu == u) return;
        u = nu;
        // the pooled nodes were built at the old scale: dropped, and the shown ones are freed when they are released
        epoch++;
        foreach (var mp in nodePool.Values) mp.Free();
        foreach (var st in aniPool.Values) while (st.TryPop(out var set)) set.Ani.Free();
    }

    /// <summary>
    /// UIDamageText::ClearAll (@0x1419135c0, uidamagetext.cpp:523), which a field move runs from GFxMovieDamage::
    /// UnloadExpiredFonts (@0x140b61ce0, gfxmoviedamage.cpp:249, called by CUIService FUN_141ad9dc0 in FUN_141adb900,
    /// uiservice.cpp:1316): the normal and follower lists and their counters are emptied (m_normals, m_ownerFollowers,
    /// m_counter, m_ownerFollowerCounter, their positions and m_queue) and ClearContainer removes every shown number. The
    /// old field's camera and player are dropped with them and found again in the new field. There is no spawn queue
    /// is emptied too; there is no m_bClearing here. Then UnloadExpiredFonts' expiry: every gem font not asked for in the
    /// last 60 s of server time is unloaded (<see cref="DamageFonts.Expire"/>), its movie and its pooled glyphs dropped.
    /// </summary>
    public void ClearAll()
    {
        ReturnEntries(normals);
        ReturnEntries(followers);
        normals.Clear();
        followers.Clear();
        foreach (var c in clips) { Kill(c); Release(c); }       // ClearContainer
        clips.Clear();
        targets.Clear();
        queue.Clear();
        camera = null; field = null; player = null; self = null;
        expired.Clear();
        fonts.Expire(ServerTime(), expired);
        foreach (int id in expired) UnloadFont(fonts.Name(id));
    }

    void ReturnEntries(DamageList<Entry> list)
    {
        for (var n = list.First; n != null; n = n.Next) Free(n.Value);
    }

    static long ServerTime() => Ms2Godot.Net.LiveSession.Instance?.ServerTime ?? 0;

    /// <summary>
    /// GFxMovieDamage::LoadDamageFont (@0x140b61900, gfxmoviedamage.cpp:210) for a gem's font id (CUIService
    /// FUN_141ad9c40, uiservice.cpp:608): nothing without the damage text movie or for id 0; a font already loaded is
    /// restamped; a new one loads its uidamagefont_&lt;id&gt; movie (UIDamageText::LoadFont, DamageTextCommon.loadDamageFont).
    /// The client loads the swf and preloads queued ids one a frame (vfunc_8); here the movie is read at once.
    /// </summary>
    public void LoadDamageFont(int id)
    {
        if (movie == null) return;
        var r = fonts.Load(id, ServerTime());
        if (r != DamageFonts.LoadResult.Loaded) return;
        string name = fonts.Name(id);
        try { fontMovies[name] = new UiMovie(assets, "uidamagefont_" + name); }
        catch (Exception e) { GD.PushWarning("damage font " + name + ": " + e.Message); }
    }

    /// <summary>The gem font is loaded and its movie read: a number may use it.</summary>
    public bool FontLoaded(int id) => fonts.Loaded(id) && fontMovies.ContainsKey(fonts.Name(id));

    /// <summary>UIDamageText::UnloadFont / unloadDamageFont: the font's movie and the glyphs pooled from it go.</summary>
    void UnloadFont(string name)
    {
        if (!fontMovies.Remove(name, out var m)) return;
        if (nodePool.Remove(m, out var mp)) mp.Free();
    }

    /// <summary>The numbers in the lists and the clips shown, for checks.</summary>
    public int Count => normals.Count + followers.Count + clips.Count;

    /// <summary>A number on an npc; <paramref name="casterMe"/> when the local player dealt it.</summary>
    /// <para><paramref name="gem"/> the caster's gem font id when it shows (<see cref="DamageTextNames.GemShows"/>), 0 for none.</para>
    public void OnNpc(FieldNpcs.Placed n, long value, int grade, bool casterMe = true, int gem = 0)
    {
        if (!Ready() || n?.Root == null) return;
        Spawn(NpcTarget(n), value, grade, casterMe, gem);
    }

    /// <summary>CNpc::FlushDamageText (@0x141093910): an npc's exp at the end of its death sequence, FUN_141912a80 type 0 on
    /// the npc (its Eff_Body). With no Exp glyph in the movie it shows nothing, as the own PC's exp.</summary>
    public void OnNpcExp(FieldNpcs.Placed n, long exp)
    {
        if (!Ready() || n?.Root == null || exp <= 0) return;
        Gain(NpcTarget(n), (int)exp, 0, null);
    }

    DamageTarget NpcTarget(FieldNpcs.Placed n)
    {
        if (!targets.TryGetValue(n.Root, out var t) || t.Gone)
            targets[n.Root] = t = new DamageTarget(n.Root, () => NpcPresence.Dummy(n, "Eff_Damage") ?? n.Root.GlobalPosition, false,
                () => NpcPresence.Dummy(n, "Eff_Body") ?? n.Root.GlobalPosition);
        return t;
    }

    /// <summary>A number on another player (RemoteLayer's target for it); <paramref name="casterMe"/> when the local player dealt it.</summary>
    public void OnPc(DamageTarget pc, long value, int grade, bool casterMe, int gem = 0)
    {
        if (!Ready() || pc == null || pc.Gone) return;
        if (!targets.TryGetValue(pc.Root, out var t) || t.Gone) targets[pc.Root] = t = pc;
        Spawn(t, value, grade, casterMe, gem);
    }

    /// <summary>A number on the own PC.</summary>
    public void OnPlayer(long value, int grade)
    {
        if (!Ready() || self == null) return;
        Spawn(self, value, grade, false, 0);
    }

    /// <summary>
    /// OnExpUp's number for the own PC (scenestategame.cpp:4152-4170): quest exp (<see cref="DamageTextNames.QuestExp"/>)
    /// waits in the queue (FUN_1419129d0), any other shows now (FUN_141912a80 type 0).
    /// </summary>
    public void OnExp(long exp, int type)
    {
        if (!Ready() || self == null) return;
        if (DamageTextNames.QuestExp(type)) queue.Enqueue(((int)exp, 0));
        else Gain(self, (int)exp, 0, null);
    }

    /// <summary>A meso drop reached its picker (CDropItem::SetState(3), dropitem.cpp:675-684): FUN_141912a80 type 1 on the
    /// picker, another player's target or null for the own PC.</summary>
    public void OnMeso(DamageTarget pc, long meso)
    {
        if (!Ready() || (pc ?? self) is not { } t) return;
        Gain(t, (int)meso, 1, null);
    }

    /// <summary>An item drop of type 3 reached its picker (dropitem.cpp:686-694): FUN_141913060 with the count and the item's
    /// icon path; <paramref name="pc"/> null for the own PC.</summary>
    public void OnItem(DamageTarget pc, int count, string iconPath)
    {
        if (!Ready() || (pc ?? self) is not { } t) return;
        Gain(t, count, 2, iconPath);
    }

    bool Ready()
    {
        if (movie == null) return false;
        camera ??= GetViewport().GetCamera3D();
        field ??= GetTree().GetFirstNodeInGroup(Field.Group) as Field;
        player ??= GetTree().GetFirstNodeInGroup(Player.Group) as Player;
        if (player != null && (self == null || self.Gone))
        {
            var dummies = EffectDummyTable.Load(packs).Pc(player.Gender);
            var p = player;
            Node3D model = null;
            // the model is looked up while it is not there yet (a player made before its visual)
            Vector3 At(string dummy)
            {
                if (model == null || !IsInstanceValid(model)) model = p.Visual?.GetNodeOrNull<Node3D>(ModelPath);
                return DummyAnchor.Point(dummies, dummy, model, p.RootName) ?? p.GlobalPosition;
            }
            self = new DamageTarget(p, () => At("Eff_Damage"), true, () => At("Eff_Body"));
        }
        return camera != null && player != null;
    }

    /// <summary>
    /// FUN_141911e90 (@0x141911e90) after the show test (DamageNumbers.Shows, RemoteLayer): nothing for a target out of
    /// show distance (FUN_141914d40) or a point behind the camera; then the factory's damageText and FUN_1419175c0
    /// (@0x1419175c0): recover grades go to the followers, the others to the normals, the own numbers appended
    /// (bAppend = myDamage), the clip hidden until its first placement.
    /// </summary>
    void Spawn(DamageTarget t, long value, int grade, bool casterMe, int gem)
    {
        if (!WithinShowDistance(t)) return;
        var pos = t.Point();
        if (camera.IsPositionBehind(pos)) return;       // world -> screen failed: nothing is created
        bool targetMe = t == self;
        bool mine = targetMe || casterMe;
        string target = DamageTextNames.TargetKindName(DamageTextNames.TargetKind(t.Pc), true);
        int len = DamageTextNames.ValueChars(value, grade, text);
        string kind = DamageTextNames.ValueKind(grade);
        // obj.gem: the id's text, which the factory finds only once the font is loaded (DamageTextCommon[key + gem])
        string gemName = gem != 0 && FontLoaded(gem) ? fonts.Name(gem) : null;
        var look = looks.Get(kind, target, DamageTextNames.GradeName(grade), gemName, mine);
        var o = GameOptions.Current;
        float scale = (o?.InterfaceOption?.DamageTextScaleStep ?? 100) / 100f, alpha = -1;
        int form = (o?.BattleOption ?? defaultBattle).OtherDamageForm;
        if (!mine && (form & 1) != 0) scale *= layout.OtherScale;    // diffScale
        if (!mine && (form & 2) != 0) alpha = layout.OtherAlpha;     // diffAlpha
        var clip = Build(look, text.AsSpan(0, len), DamageLook.IsNumber(kind), "", null, scale, alpha);
        if (clip == null) return;
        var e = NewEntry(clip, t, pos);
        e.Type = 0; e.Grade = grade; e.Mine = mine;
        var list = DamageNumbers.IsRecover(grade) ? followers : normals;
        if (list.Insert(mine, e, layout.MaxList) is { } old) Drop(old);
    }

    Entry NewEntry(Clip clip, DamageTarget t, Vector3 pos)
    {
        if (entryPool.TryPop(out var e)) e.Reset();
        else e = new Entry();
        e.Clip = clip; e.Gen = clip.Gen; e.Owner = t; e.Pos = pos;
        return e;
    }

    void Free(Entry e)
    {
        e.Clip = null; e.Owner = null;
        entryPool.Push(e);
    }

    /// <summary>
    /// FUN_141912b20 / FUN_141913060: the show test, the anchor (Eff_Body for exp, FUN_141912a80), nothing when the point
    /// is behind the camera, then damageText / itemIcon and FUN_1419175c0 with type 1, grade 1, appended.
    /// </summary>
    void Gain(DamageTarget t, int value, int type, string iconPath)
    {
        var b = GameOptions.Current?.BattleOption ?? defaultBattle;
        if (!DamageTextNames.GainShows(type, b.DisplayGetMeso, b.DisplayGetExp) || !WithinShowDistance(t)) return;
        var pos = type == 0 ? t.Body() : t.Point();
        if (camera.IsPositionBehind(pos)) return;
        int len = DamageTextNames.GainValueChars(value, type, text);
        string target = DamageTextNames.TargetKindName(DamageTextNames.TargetKind(t.Pc), true);
        string grade = DamageTextNames.GainGradeName(type);
        var look = looks.Get("", target, grade, null, false, type == 2);
        var clip = Build(look, text.AsSpan(0, len), true, grade, iconPath, 1, -1);
        // FUN_1419175c0 (@0x1419175c0) inserts only when the Invoke gave an object: a thrown DamageMovie init (no Exp glyph)
        // returns none, so no DamageInfo takes a list slot or lifts the others
        if (clip == null) return;
        var e = NewEntry(clip, t, pos);
        e.Type = 1; e.Grade = 1; e.Mine = t == self;
        if (normals.Insert(true, e, layout.MaxList) is { } old) Drop(old);
    }

    /// <summary>IsWithinShowDistance: the target's distance to the local player (GameObject m_unk_bc) is at most DamageDistance.</summary>
    bool WithinShowDistance(DamageTarget t) =>
        player != null && !t.Gone && (t == self || t.Root.GlobalPosition.DistanceTo(player.GlobalPosition) <= layout.ShowDistance);

    /// <summary>DamageTextFactory.damageText: the number (DamageMovie) and the ani with its slots filled.</summary>
    Clip Build(DamageLook look, ReadOnlySpan<char> value, bool isNumber, string grade, string iconPath, float scale, float alpha)
    {
        if (look.Glyphs == null) return null;
        // a gem font's arrays come from its movie (DamageTextCommon[key + gem]); the ani and the extra effect from this one
        UiMovie glyphs = movie;
        if (look.Font != null && !fontMovies.TryGetValue(look.Font, out glyphs)) return null;
        var number = Number(look, glyphs, value, isNumber, grade, iconPath, out float h);     // the DamageMovie, made before the ani
        if (number == null) return null;
        var set = TakeAni(look.Ani.Class);
        if (set == null) { FreeNum(number, epoch); return null; }
        var clip = clipPool.TryPop(out var pooled) ? pooled : new Clip();
        clip.Gone = false; clip.Frame = 0; clip.Age = 0; clip.Epoch = epoch;
        clip.Set = set; clip.Ani = set.Ani; clip.Remove = look.Ani.Remove;
        clip.Number = number;
        if (set.Dummy1 != null)
        {
            // DamageMovie.copyFrom: the same glyphs at the same places
            if (Number(look, glyphs, value, isNumber, grade, null, out _) is { } copy)
            {
                copy.Node.Modulate = alpha >= 0 ? new Color(1, 1, 1, alpha) : Colors.White;
                Fill(set.Dummy1, copy.Node);
                clip.Copy = copy;
            }
        }
        if (set.Cridmy != null)
        {
            // getCriticalTxt: the font's MonsterCriticalTxt<id> when its movie has the class (none of the data's do), else
            // the damage text movie's MonsterCriticalTxt; duplicateMovieClip makes the second of the same class
            UiMovie cm = glyphs;
            string ct = look.FontCritText;
            MovieClipNode crit = ct != null ? Take(cm, ct) : null;
            if (crit == null) { cm = movie; ct = look.CritText; crit = ct != null ? Take(cm, ct) : null; }
            if (crit == null) { Release(clip); return null; }
            set.Cridmy.AddChild(crit);
            clip.Parts.Add(new Part(cm, ct, crit));
            clip.Loop1 = crit;
            if (set.Cridmy1 != null && Take(cm, ct) is { } crit2)
            {
                set.Cridmy1.AddChild(crit2);
                clip.Parts.Add(new Part(cm, ct, crit2));
                clip.Loop2 = crit2;
            }
        }
        if (set.ExSlot != null)
        {
            if (look.ExEffect != null && set.ExNode != null && Take(movie, look.ExEffect) is { } ex)
            {
                set.ExNode.AddChild(ex);
                clip.Parts.Add(new Part(movie, look.ExEffect, ex));
                clip.Loop3 = ex;
                set.ExSlot.Hidden = false;
            }
            else set.ExSlot.Hidden = true;
        }
        if (set.MesoSlot != null)
        {
            set.MesoSlot.Override = null;
            if (set.Mesoeff != null && number.Parts.Count > 0 && number.Node.GetChildCount() > 0 && number.Node.GetChild(0) is Node2D first)
            {
                // damageText: mesoeff.x = (m_dmg.x + child 0's x) x 0.8, y = m_dmg.y + child 0's y
                var me = set.Mesoeff;
                set.MesoSlot.Override = new Transform2D(me.Rotation, me.Scale, me.Skew,
                    new Vector2((number.Node.Position.X + first.Position.X) * 0.8f, number.Node.Position.Y + first.Position.Y));
            }
        }
        if (set.Dummy != null) Fill(set.Dummy, number.Node);
        var a = set.Ani;
        a.Position = new Vector2(0, -h / 2);
        a.Scale = new Vector2(scale, scale);
        a.Modulate = alpha >= 0 ? new Color(1, 1, 1, alpha) : Colors.White;
        var wrap = clip.Wrap;
        wrap.Visible = false;              // hidden until its first placement
        wrap.AddChild(a);
        containers[look.Container].AddChild(wrap);
        clip.Container = look.Container;
        a.GotoFrame(0);
        clips.Add(clip);
        return clip;
    }

    /// <summary>A slot's placeholder (child 0) hidden and the number added.</summary>
    static void Fill(MovieClipNode slot, Node2D number)
    {
        if (slot.Children.Count > 0) slot.Children[0].Hidden = true;
        slot.GotoFrame(0);
        slot.AddChild(number);
    }

    /// <summary>A class of a movie at the current scale: one released before, else built. Null for a class not in it.</summary>
    MovieClipNode Take(UiMovie m, string cls)
    {
        if (!nodePool.TryGetValue(m, out var mp)) nodePool[m] = mp = new MoviePool();
        if (mp.Nodes.TryGetValue(cls, out var st) && st.TryPop(out var n))
        {
            n.GotoFrame(0);
            return n;
        }
        if (mp.Missing.Contains(cls)) return null;
        n = m.BuildClass(cls, u);
        if (n == null) mp.Missing.Add(cls);
        return n;
    }

    void Give(in Part p, int builtAt)
    {
        var n = p.Node;
        n.GetParent()?.RemoveChild(n);
        if (builtAt != epoch || (p.Movie != movie && !fontMovies.ContainsValue(p.Movie))) { n.Free(); return; }
        n.Position = Vector2.Zero;
        if (!nodePool.TryGetValue(p.Movie, out var mp)) nodePool[p.Movie] = mp = new MoviePool();
        if (!mp.Nodes.TryGetValue(p.Class, out var st)) mp.Nodes[p.Class] = st = new Stack<MovieClipNode>();
        st.Push(n);
    }

    AniSet TakeAni(string cls)
    {
        if (aniPool.TryGetValue(cls, out var st) && st.TryPop(out var set)) return set;
        if (movie.BuildClass(cls, u) is not { } a) return null;
        set = new AniSet { Ani = a, Class = cls, Epoch = epoch };
        set.Dummy = a.Direct("dummy")?.Node as MovieClipNode;
        set.Dummy1 = a.Direct("dummy1")?.Node as MovieClipNode;
        set.Cridmy = a.Direct("cridmy")?.Node as MovieClipNode;
        set.Cridmy1 = a.Direct("cridmy1")?.Node as MovieClipNode;
        set.ExSlot = a.Direct("exeff");
        set.ExNode = set.ExSlot?.Node as MovieClipNode;
        set.MesoSlot = a.Direct("mesoeff");
        set.Mesoeff = set.MesoSlot?.Node;
        return set;
    }

    /// <summary>
    /// The clip's nodes back to their pools (or freed when built at an old scale): its numbers, critical texts and extra
    /// effect out of their slots, the ani out of its wrapper and the wrapper out of its container. Run once a killed clip
    /// leaves the clip list, so a clip is never released twice.
    /// </summary>
    void Release(Clip c)
    {
        for (int i = 0; i < c.Parts.Count; i++) Give(c.Parts[i], c.Epoch);
        c.Parts.Clear();
        if (c.Number != null) { FreeNum(c.Number, c.Epoch); c.Number = null; }
        if (c.Copy != null) { FreeNum(c.Copy, c.Epoch); c.Copy = null; }
        c.Loop1 = c.Loop2 = c.Loop3 = null;
        if (c.Set != null)
        {
            var set = c.Set;
            if (set.Ani.GetParent() == c.Wrap) c.Wrap.RemoveChild(set.Ani);
            if (set.Epoch != epoch) set.Ani.Free();
            else
            {
                if (!aniPool.TryGetValue(set.Class, out var st)) aniPool[set.Class] = st = new Stack<AniSet>();
                st.Push(set);
            }
            c.Set = null; c.Ani = null;
        }
        if (c.Container >= 0) { containers[c.Container].RemoveChild(c.Wrap); c.Container = -1; }
        c.Wrap.Visible = false;
        c.Gen++;
        clipPool.Push(c);
    }

    void FreeNum(Num n, int builtAt)
    {
        for (int i = 0; i < n.Parts.Count; i++) Give(n.Parts[i], builtAt);
        n.Parts.Clear();
        if (n.Icon != null) { n.Icon.Free(); n.Icon = null; }
        n.Node.GetParent()?.RemoveChild(n.Node);
        n.Node.Modulate = Colors.White;
        numPool.Push(n);
    }

    /// <summary>
    /// DamageMovie.init: numberText for a number, else the one image glyph (Miss, Block); centred on its bounds. numberText
    /// starts at getStartPosX / getStartPosY (the Item icon's width - 10 and (height - 24) x 0.5), puts preGradeText's Exp
    /// glyph first (the digits from its right edge - 2), each glyph advancing by its right edge plus the movie's spacing,
    /// then gradeText (Meso: its glyph at x - numberLetterSpacing - 2, moved to child 0; Item: the icon added); a leading
    /// "+" (child 0) is moved to the text's length - 1. The pcBang glyphs are never made (FUN_141914490 returns 0).
    /// Null when an exp number's Exp glyph is not in the movie (the init throws).
    /// </summary>
    Num Number(DamageLook look, UiMovie m, ReadOnlySpan<char> value, bool isNumber, string grade, string iconPath, out float h)
    {
        var num = numPool.TryPop(out var pooled) ? pooled : new Num();
        var number = num.Node;
        float x = 0, y = 0;
        h = 0;
        MovieClipNode icon = grade == DamageTextNames.Item ? ItemIcon(iconPath) : null;
        num.Icon = icon;
        if (icon != null)
        {
            float iw = icon.Bounds.Size.X * icon.Scale.X, ih = icon.Bounds.Size.Y * icon.Scale.Y;
            x = iw - 10;
            y = (ih - 24) * 0.5f;
        }
        if (!isNumber)
        {
            int key = value.SequenceEqual(DamageTextNames.Miss.AsSpan()) ? DamageLook.MissKey : DamageLook.BlockKey;
            if (Add(num, m, look.Glyph(key)) is { } g) h = g.Bounds.End.Y;
        }
        else
        {
            if (grade == DamageTextNames.Exp)
            {
                // preGradeText reads the Exp glyph's x and width without a test: initImageText's ExpExp class is in
                // neither the KR Live nor the restore movie, so the DamageMovie's init throws and damageText adds nothing
                if (Add(num, m, look.Glyph(DamageLook.ExpKey)) is not { } exp) { FreeNum(num, epoch); return null; }
                exp.Position = new Vector2(x, 0);
                x = exp.Position.X + exp.Bounds.Size.X - 2;
                h = Math.Max(h, exp.Bounds.End.Y);
            }
            foreach (char ch in value)
            {
                int key = DamageLook.GlyphIndex(ch);
                if (key < 0 || Add(num, m, look.Glyph(key)) is not { } g) continue;
                g.Position = new Vector2(x, y);
                x += g.Bounds.End.X + (key < 10 ? look.NumberSpacing : look.SignSpacing);
                h = Math.Max(h, y + g.Bounds.End.Y);
            }
            if (grade == DamageTextNames.Meso && Add(num, m, look.Glyph(DamageLook.MesoKey)) is { } meso)
            {
                meso.Position = new Vector2(x - look.NumberSpacing - 2, 0);
                number.MoveChild(meso, 0);
                h = Math.Max(h, meso.Bounds.End.Y);
            }
            if (icon != null) { number.AddChild(icon); h = Math.Max(h, icon.Position.Y + icon.Bounds.End.Y * icon.Scale.Y); }
            if (value.Length > 0 && value[0] == '+' && number.GetChildCount() > 0)
                number.MoveChild(number.GetChild(0), Math.Min(value.Length - 1, number.GetChildCount() - 1));
        }
        float w = 0;
        for (int i = 0; i < num.Parts.Count; i++) { var g = num.Parts[i].Node; w = Math.Max(w, g.Position.X + g.Bounds.End.X * g.Scale.X); }
        if (icon != null) w = Math.Max(w, icon.Position.X + icon.Bounds.End.X * icon.Scale.X);
        number.Position = new Vector2(-w / 2, -h / 2);
        return num;
    }

    /// <summary>A glyph of the class added to the number (and tracked), null when the class is not in the movie.</summary>
    MovieClipNode Add(Num num, UiMovie m, string cls)
    {
        if (cls == null || Take(m, cls) is not { } g) return null;
        num.Node.AddChild(g);
        num.Parts.Add(new Part(m, cls, g));
        return g;
    }

    /// <summary>
    /// The ItemMovie's "icon" (dmg_mc_item) taken out of it as gradeText adds it to m_dmg (its place kept), with the item
    /// image (Common.getImgUrl, Resource/Image) under it scaled as onIconLoadComplete does: the icon's scale is its width
    /// before the load over the image's width. Null when the movie has no icon; without the image the icon stays as built.
    /// </summary>
    MovieClipNode ItemIcon(string iconPath)
    {
        if (movie.BuildClass("dmg_mc_item", u) is not { } host) return null;
        host.GotoFrame(0);
        MovieClipNode icon = host.Direct("icon")?.Node as MovieClipNode;
        if (icon != null)
        {
            host.RemoveChild(icon);
            if (IconTexture(iconPath) is { } tex && tex.GetWidth() > 0)
            {
                float k = icon.Bounds.Size.X * icon.Scale.X / tex.GetWidth();
                icon.Scale = new Vector2(k, k);
                icon.AddChild(new Sprite2D { Texture = tex, Centered = false, TextureFilter = CanvasItem.TextureFilterEnum.Linear });   // smoothing
            }
        }
        host.Free();
        return icon;
    }

    Texture2D IconTexture(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        string entry = path.ToLowerInvariant();
        int at = entry.IndexOf("resource/image/", StringComparison.Ordinal);
        if (at >= 0) entry = entry[(at + "resource/image/".Length)..];
        return assets.ImageFile(entry);
    }

    /// <summary>A number leaves its list: the DamageInfo dtor stops a number not jumped and removes its clip.</summary>
    void Drop(Entry e)
    {
        if (e.RemovesClipOnDrop && e.Live) Kill(e.Clip);
        Free(e);
    }

    /// <summary>The clip removed from the stage: hidden now, its nodes released when it leaves the clip list.</summary>
    static void Kill(Clip c)
    {
        if (c.Gone) return;
        c.Gone = true;
        c.Wrap.Visible = false;
    }

    public override void _Process(double delta)
    {
        if (movie == null) return;
        clock += delta * 1000;
        camera ??= GetViewport().GetCamera3D();
        if (camera == null) return;
        // the clips play on their own and dispatch RemoveAni at their script frames; the slot clips loop
        double step = delta * movie.Fps;
        int kept = 0;
        for (int i = 0; i < clips.Count; i++)
        {
            var c = clips[i];
            if (!c.Gone)
            {
                int before = (int)c.Frame;
                c.Frame += step;
                int after = Math.Min((int)c.Frame, c.Ani.FrameCount - 1);
                for (int f = before + 1; f <= after && !c.Gone; f++) if (Array.IndexOf(c.Remove, f) >= 0) Kill(c);
                if (!c.Gone)
                {
                    c.Ani.GotoFrame(after);
                    c.Age += step;
                    Loop(c.Loop1, c.Age); Loop(c.Loop2, c.Age); Loop(c.Loop3, c.Age);
                }
            }
            if (!c.Gone) clips[kept++] = c;
            else Release(c);
        }
        if (kept < clips.Count) clips.RemoveRange(kept, clips.Count - kept);
        // FUN_141916340 before the updates: one queued gain each 200 ms; a target gone drops it without a new wait
        if (queue.Count > 0 && clock >= lastQueueTime + 200)
        {
            var (value, type) = queue.Dequeue();
            if (self != null && !self.Gone) { Gain(self, value, type, null); lastQueueTime = clock; }
        }
        uint now = (uint)clock + 1;     // 0 is DamageInfo's "not started"
        UpdateFollowers(now);
        UpdateNormals(now);
        if (normals.Count == 0 && followers.Count == 0 && targets.Count > 0) PruneTargets();
    }

    /// <summary>The targets whose root is gone leave the map; the others keep their anchors for the next hit.</summary>
    void PruneTargets()
    {
        foreach (var (root, t) in targets) if (t.Gone) goneTargets.Add(root);
        if (goneTargets.Count == 0) return;
        foreach (var r in goneTargets) targets.Remove(r);
        goneTargets.Clear();
    }

    static void Loop(MovieClipNode m, double age)
    {
        if (m != null && m.FrameCount > 1) m.GotoFrame((int)age % m.FrameCount);
    }

    /// <summary>
    /// UpdateNormals (@0x141915990): each entry in list order; one past its 1000 ms (from its first update) leaves the
    /// list. One that may still jump is hidden or placed as <see cref="DamageNormalsPass"/> says (placed only when its
    /// point projects; hiding a shown one sets state 3 and ends its jumping, so it is never shown again); a placed one is
    /// made visible, lifted, jumps ("_next", state 1, no longer moved), shifted by the camera pitch and scaled by its distance.
    /// </summary>
    void UpdateNormals(uint now)
    {
        if (normals.Count == 0) { normals.ClearCounters(); return; }
        var o = GameOptions.Current;
        var pass = new DamageNormalsPass(layout, o?.InterfaceOption?.DamageTextScaleStep ?? 100, (o?.BattleOption ?? defaultBattle).OtherDamageForm);
        for (var node = normals.First; node != null;)
        {
            var next = node.Next;
            var e = node.Value;
            e.Tick(now);
            if (e.Expired) { normals.Remove(node); normals.Decrement(e.Owner, 1); Drop(e); node = next; continue; }
            if (e.CanJump)
            {
                bool hide = pass.Hidden(e, normals);
                e.Begin(now);
                if (!hide)
                {
                    if (!camera.IsPositionBehind(e.Pos))     // world -> screen
                    {
                        var stage = camera.UnprojectPosition(e.Pos);
                        var (lift, jump) = pass.Place(e);
                        stage.Y -= lift * u;
                        if (jump) Jump(e);
                        stage.Y += PitchShift(e.Pos) * u;
                        if (e.Live) Show(e.Clip, stage, DistanceScale(e.Pos));
                    }
                }
                else if (e.State == 0)
                {
                    e.State = 3;
                    if (e.Live) e.Clip.Wrap.Visible = false;
                    e.CanJump = false;
                }
            }
            node = next;
        }
    }

    /// <summary>
    /// UpdateOwnerFollowers (@0x1419153d0): every frame for as long as the entry lives (it does not stop at the jump), at
    /// the owner's Eff_Damage point now. The entry leaves the list when its owner is gone, or out of show distance (the
    /// clip stopped and hidden first); while the point does not project the clip is hidden. It jumps once, over 100 ms
    /// old in state 0, when the owner has a newer number; scaled by FUN_141914c40 (1 for the own PC).
    /// </summary>
    void UpdateFollowers(uint now)
    {
        if (followers.Count == 0) { followers.ClearCounters(); return; }
        for (var node = followers.First; node != null;)
        {
            var next = node.Next;
            var e = node.Value;
            e.Tick(now);
            bool remove = e.Expired;
            if (!remove)
            {
                var t = e.Target;
                if (t.Gone) remove = true;
                else
                {
                    e.Begin(now);
                    if (!WithinShowDistance(t))
                    {
                        if (e.Live) e.Clip.Wrap.Visible = false;
                        remove = true;
                    }
                    else
                    {
                        var p = t.Point();
                        if (camera.IsPositionBehind(p)) { if (e.Live) e.Clip.Wrap.Visible = false; }
                        else
                        {
                            var stage = camera.UnprojectPosition(p);
                            if (followers.TryCounter(e.Owner, out var c) && c.Index - e.Index > 0 && e.MayJump) Jump(e);
                            stage.Y += PitchShift(p) * u;            // GetHeightOffset; + PCBase::m_f178 = OffsetPcDamagenumber, 0
                            if (e.Live) Show(e.Clip, stage, t == self ? 1 : DistanceScale(t.Root.GlobalPosition));
                        }
                    }
                }
            }
            if (remove) { followers.Remove(node); followers.Decrement(e.Owner, 1); Drop(e); }
            node = next;
        }
    }

    void Show(Clip c, Vector2 stage, float scale)
    {
        if (c.Gone) return;
        var w = c.Wrap;
        w.Visible = true;
        w.Position = UiScale.StagePoint(stage, u);
        w.Scale = new Vector2(scale * u, scale * u);     // x the wrapper's own scale (m_fScale), 1
    }

    /// <summary>gotoAndPlay("_next") on the ani (a no-op on an ani without that label: the clip plays on), state 1, no more jumping.</summary>
    void Jump(Entry e)
    {
        if (e.Live && e.Clip.Ani.Labels.TryGetValue("_next", out int f)) e.Clip.Frame = f;
        e.State = 1;
        e.CanJump = false;
    }

    /// <summary>FUN_141914a90: 1 unless the point is farther from the camera than the player is.</summary>
    float DistanceScale(Vector3 p)
    {
        if (player == null) return 1;
        var at = player.GlobalPosition;
        if (at == p) return 1;
        var eye = camera.GlobalPosition;
        if (eye.DistanceTo(p) <= eye.DistanceTo(at)) return 1;
        return 0.5f + 0.5f * Mathf.Clamp((layout.ShowDistance - p.DistanceTo(at)) / layout.ShowDistance, 0, 1);
    }

    /// <summary>FUN_1412762d0, as for the name tags (<see cref="PlayerNameTag.PitchShift"/>).</summary>
    float PitchShift(Vector3 anchor) => PlayerNameTag.PitchShift(field, anchor);
}
