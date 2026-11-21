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
/// STILL-OPEN: the caster's gem font (bGem, GFxMovieDamage::LoadDamageFont), a summon's numbers as the own (bMySummon),
/// observer mode, PC::vfunc_122's pvp and pk rules and the caster's forceApplyTarget bit 2 (another PC is always User
/// here), m_nRepeat (a dev repeat, 1).
/// </summary>
public sealed partial class DamageText : CanvasLayer
{
    sealed class Clip
    {
        public Node2D Wrap;
        public MovieClipNode Ani;
        public int[] Remove;
        public double Frame;          // the ani's playhead, at the movie's frame rate
        public double Age;            // frames since creation: the looping clips added to its slots
        public MovieClipNode Loop1, Loop2, Loop3;
        public bool Gone;
    }

    sealed class Entry : DamageInfo
    {
        public Clip Clip;
        public Vector3 Pos;            // the stored world point (normals)
        public DamageTarget Target => (DamageTarget)Owner;
    }

    readonly UiMovie movie;
    float u = 1;
    readonly DamageTextLayout layout;
    readonly DamageList<Entry> normals, followers;
    readonly List<Clip> clips = new();
    readonly Dictionary<Node3D, DamageTarget> targets = new();
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
    }

    public DamageText() { }

    /// <summary>
    /// The window was resized: the movie's new viewport scale (GFxManager::ResizeMovies, GFxMovie::vfunc_7,
    /// gfxmovie.cpp:146), kept alive with its state. STILL-OPEN: the clips already built keep the glyphs they were built
    /// with at the old scale and are only scaled; the client's Scaleform draws them again at the new viewport scale.
    /// </summary>
    public void Resize(Vector2 window) => u = UiScale.For(window);

    /// <summary>
    /// UIDamageText::ClearAll (@0x1419135c0, uidamagetext.cpp:523), which a field move runs from GFxMovieDamage::
    /// UnloadExpiredFonts (@0x140b61ce0, gfxmoviedamage.cpp:249, called by CUIService FUN_141ad9dc0 in FUN_141adb900,
    /// uiservice.cpp:1316): the normal and follower lists and their counters are emptied (m_normals, m_ownerFollowers,
    /// m_counter, m_ownerFollowerCounter, their positions and m_queue) and ClearContainer removes every shown number. The
    /// old field's camera and player are dropped with them and found again in the new field. There is no spawn queue
    /// is emptied too; there is no m_bClearing here. UnloadExpiredFonts' other part, the
    /// movie font library's expiry (fonts unused for 60 s), has no counterpart: this movie has no loaded font library.
    /// </summary>
    public void ClearAll()
    {
        normals.Clear();
        followers.Clear();
        foreach (var c in clips) Kill(c);       // ClearContainer
        clips.Clear();
        targets.Clear();
        queue.Clear();
        camera = null; field = null; player = null; self = null;
    }

    /// <summary>The numbers in the lists and the clips shown, for checks.</summary>
    public int Count => normals.Count + followers.Count + clips.Count;

    /// <summary>A number on an npc; <paramref name="casterMe"/> when the local player dealt it.</summary>
    public void OnNpc(FieldNpcs.Placed n, long value, int grade, bool casterMe = true)
    {
        if (!Ready() || n?.Root == null) return;
        if (!targets.TryGetValue(n.Root, out var t) || t.Gone)
            targets[n.Root] = t = new DamageTarget(n.Root, () => NpcPresence.Dummy(n, "Eff_Damage") ?? n.Root.GlobalPosition, false);
        Spawn(t, value, grade, casterMe);
    }

    /// <summary>A number on another player (RemoteLayer's target for it); <paramref name="casterMe"/> when the local player dealt it.</summary>
    public void OnPc(DamageTarget pc, long value, int grade, bool casterMe)
    {
        if (!Ready() || pc == null || pc.Gone) return;
        if (!targets.TryGetValue(pc.Root, out var t) || t.Gone) targets[pc.Root] = t = pc;
        Spawn(t, value, grade, casterMe);
    }

    /// <summary>A number on the own PC.</summary>
    public void OnPlayer(long value, int grade)
    {
        if (!Ready() || self == null) return;
        Spawn(self, value, grade, false);
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
    void Spawn(DamageTarget t, long value, int grade, bool casterMe)
    {
        if (!WithinShowDistance(t)) return;
        var pos = t.Point();
        if (camera.IsPositionBehind(pos)) return;       // world -> screen failed: nothing is created
        bool targetMe = t == self;
        bool mine = targetMe || casterMe;
        string target = DamageTextNames.TargetKindName(DamageTextNames.TargetKind(t.Pc), true);
        string text = DamageTextNames.ValueText(value, grade);
        var look = DamageLook.For(text, target, DamageTextNames.GradeName(grade), "", "", mine);
        var o = GameOptions.Current;
        float scale = (o?.InterfaceOption?.DamageTextScaleStep ?? 100) / 100f, alpha = -1;
        int form = (o?.BattleOption ?? defaultBattle).OtherDamageForm;
        if (!mine && (form & 1) != 0) scale *= layout.OtherScale;    // diffScale
        if (!mine && (form & 2) != 0) alpha = layout.OtherAlpha;     // diffAlpha
        var clip = Build(look, text, "", null, scale, alpha);
        if (clip == null) return;
        var e = new Entry { Type = 0, Grade = grade, Owner = t, Mine = mine, Pos = pos, Clip = clip };
        var list = DamageNumbers.IsRecover(grade) ? followers : normals;
        if (list.Insert(mine, e, layout.MaxList) is { } old) Drop(old);
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
        string text = DamageTextNames.GainValueText(value, type);
        string target = DamageTextNames.TargetKindName(DamageTextNames.TargetKind(t.Pc), true);
        string grade = DamageTextNames.GainGradeName(type);
        var look = type == 2 ? DamageLook.ForItem(text, target, grade) : DamageLook.For(text, target, grade);
        var clip = Build(look, text, grade, iconPath, 1, -1);
        if (clip == null) return;
        var e = new Entry { Type = 1, Grade = 1, Owner = t, Mine = t == self, Pos = pos, Clip = clip };
        if (normals.Insert(true, e, layout.MaxList) is { } old) Drop(old);
    }

    /// <summary>IsWithinShowDistance: the target's distance to the local player (GameObject m_unk_bc) is at most DamageDistance.</summary>
    bool WithinShowDistance(DamageTarget t) =>
        player != null && !t.Gone && (t == self || t.Root.GlobalPosition.DistanceTo(player.GlobalPosition) <= layout.ShowDistance);

    /// <summary>DamageTextFactory.damageText: the number (DamageMovie) and the ani with its slots filled.</summary>
    Clip Build(DamageLook look, string text, string grade, string iconPath, float scale, float alpha)
    {
        if (look.Glyphs == null) return null;
        var number = Number(look, text, grade, iconPath, out float h);     // the DamageMovie, made before the ani
        if (number == null) return null;
        var a = movie.BuildClass(look.Ani.Class, u);
        if (a == null) { number.Free(); return null; }
        var clip = new Clip { Ani = a, Remove = look.Ani.Remove };
        if (a.Direct("dummy1")?.Node is MovieClipNode copySlot)
        {
            // DamageMovie.copyFrom: the same glyphs at the same places
            if (Number(look, text, grade, null, out _) is { } copy)
            {
                if (alpha >= 0) copy.Modulate = new Color(1, 1, 1, alpha);
                Fill(copySlot, copy);
            }
        }
        if (a.Direct("cridmy")?.Node is MovieClipNode critSlot)
        {
            if (look.CritText == null || movie.BuildClass(look.CritText, u) is not { } crit) { number.Free(); a.Free(); return null; }
            critSlot.AddChild(crit);
            clip.Loop1 = crit;
            if (a.Direct("cridmy1")?.Node is MovieClipNode critSlot2 && movie.BuildClass(look.CritText, u) is { } crit2)
            {
                critSlot2.AddChild(crit2);
                clip.Loop2 = crit2;
            }
        }
        if (a.Direct("exeff") is { } exSlot)
        {
            if (look.ExEffect != null && exSlot.Node is MovieClipNode exNode && movie.BuildClass(look.ExEffect, u) is { } ex)
            {
                exNode.AddChild(ex);
                clip.Loop3 = ex;
            }
            else exSlot.Hidden = true;
        }
        if (a.Direct("mesoeff")?.Node is Node2D mesoeff && number.GetChildCount() > 0 && number.GetChild(0) is Node2D first)
        {
            // damageText: mesoeff.x = (m_dmg.x + child 0's x) x 0.8, y = m_dmg.y + child 0's y
            a.Direct("mesoeff").Override = new Transform2D(mesoeff.Rotation, mesoeff.Scale, mesoeff.Skew,
                new Vector2((number.Position.X + first.Position.X) * 0.8f, number.Position.Y + first.Position.Y));
        }
        if (a.Direct("dummy")?.Node is MovieClipNode dummy) Fill(dummy, number);
        else number.Free();
        a.Position = new Vector2(0, -h / 2);
        a.Scale = new Vector2(scale, scale);
        if (alpha >= 0) a.Modulate = new Color(1, 1, 1, alpha);
        var wrap = new Node2D { Name = "wrapper", Visible = false };   // created hidden, shown on its first placement
        wrap.AddChild(a);
        containers[look.Container].AddChild(wrap);
        a.GotoFrame(0);
        clip.Wrap = wrap;
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

    /// <summary>
    /// DamageMovie.init: numberText for a number, else the one image glyph (Miss, Block); centred on its bounds. numberText
    /// starts at getStartPosX / getStartPosY (the Item icon's width - 10 and (height - 24) x 0.5), puts preGradeText's Exp
    /// glyph first (the digits from its right edge - 2), each glyph advancing by its right edge plus the movie's spacing,
    /// then gradeText (Meso: its glyph at x - numberLetterSpacing - 2, moved to child 0; Item: the icon added); a leading
    /// "+" (child 0) is moved to the text's length - 1. The pcBang glyphs are never made (FUN_141914490 returns 0).
    /// Null when an exp number's Exp glyph is not in the movie (the init throws).
    /// </summary>
    Node2D Number(DamageLook look, string text, string grade, string iconPath, out float h)
    {
        var number = new Node2D { Name = "m_dmg" };
        float x = 0, y = 0;
        h = 0;
        MovieClipNode icon = grade == DamageTextNames.Item ? ItemIcon(iconPath) : null;
        if (icon != null)
        {
            float iw = icon.Bounds.Size.X * icon.Scale.X, ih = icon.Bounds.Size.Y * icon.Scale.Y;
            x = iw - 10;
            y = (ih - 24) * 0.5f;
        }
        if (!DamageLook.IsNumber(text))
        {
            if (movie.BuildClass(look.Glyphs + text, u) is { } g) { number.AddChild(g); h = g.Bounds.End.Y; }
        }
        else
        {
            if (grade == DamageTextNames.Exp)
            {
                // preGradeText reads the Exp glyph's x and width without a test: initImageText's ExpExp class is in
                // neither the KR Live nor the restore movie, so the DamageMovie's init throws and damageText adds nothing
                if (movie.BuildClass(look.Glyphs + DamageTextNames.Exp, u) is not { } exp) { number.Free(); return null; }
                exp.Position = new Vector2(x, 0);
                number.AddChild(exp);
                x = exp.Position.X + exp.Bounds.Size.X - 2;
                h = Math.Max(h, exp.Bounds.End.Y);
            }
            foreach (char ch in text)
            {
                string key = DamageLook.GlyphKey(ch);
                if (key == null || movie.BuildClass(look.Glyphs + key, u) is not { } g) continue;
                g.Position = new Vector2(x, y);
                number.AddChild(g);
                x += g.Bounds.End.X + (char.IsDigit(ch) ? look.NumberSpacing : look.SignSpacing);
                h = Math.Max(h, y + g.Bounds.End.Y);
            }
            if (grade == DamageTextNames.Meso && movie.BuildClass(look.Glyphs + DamageTextNames.Meso, u) is { } meso)
            {
                meso.Position = new Vector2(x - look.NumberSpacing - 2, 0);
                number.AddChild(meso);
                number.MoveChild(meso, 0);
                h = Math.Max(h, meso.Bounds.End.Y);
            }
            if (icon != null) { number.AddChild(icon); h = Math.Max(h, icon.Position.Y + icon.Bounds.End.Y * icon.Scale.Y); }
            if (text.Length > 0 && text[0] == '+' && number.GetChildCount() > 0)
                number.MoveChild(number.GetChild(0), Math.Min(text.Length - 1, number.GetChildCount() - 1));
        }
        float w = 0;
        for (int i = 0; i < number.GetChildCount(); i++)      // by index: GetChildren() is a tracked array per call
            if (number.GetChild(i) is MovieClipNode g) w = Math.Max(w, g.Position.X + g.Bounds.End.X * g.Scale.X);
        number.Position = new Vector2(-w / 2, -h / 2);
        return number;
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
        if (e.RemovesClipOnDrop) Kill(e.Clip);
    }

    void Kill(Clip c)
    {
        if (c.Gone) return;
        c.Gone = true;
        c.Wrap.QueueFree();
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
        if (normals.Count == 0 && followers.Count == 0 && targets.Count > 0) targets.Clear();
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
                        Show(e.Clip, stage, DistanceScale(e.Pos));
                    }
                }
                else if (e.State == 0)
                {
                    e.State = 3;
                    if (!e.Clip.Gone) e.Clip.Wrap.Visible = false;
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
                        if (!e.Clip.Gone) e.Clip.Wrap.Visible = false;
                        remove = true;
                    }
                    else
                    {
                        var p = t.Point();
                        if (camera.IsPositionBehind(p)) { if (!e.Clip.Gone) e.Clip.Wrap.Visible = false; }
                        else
                        {
                            var stage = camera.UnprojectPosition(p);
                            if (followers.TryCounter(e.Owner, out var c) && c.Index - e.Index > 0 && e.MayJump) Jump(e);
                            stage.Y += PitchShift(p) * u;            // GetHeightOffset; + PCBase::m_f178 = OffsetPcDamagenumber, 0
                            Show(e.Clip, stage, t == self ? 1 : DistanceScale(t.Root.GlobalPosition));
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
        if (!e.Clip.Gone && e.Clip.Ani.Labels.TryGetValue("_next", out int f)) e.Clip.Frame = f;
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
