using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2Godot.World;

namespace Ms2Godot.Ui;

/// <summary>
/// The minimap's places: the field's portals and spawned npcs drawn as UIMinimapDialog does (see <see cref="MinimapIcons"/>).
/// Each one is an icon from uiminimapdialog (<see cref="MinimapEntities"/>). A portal also gets its label, the target map's
/// name, as a minimap_entity_text_* clip. The entities are grouped in the client's layer order (portals under npcs, labels
/// above both). They keep their size whatever the map's zoom (getEntityScale).
/// It sits on the map image (so it moves with it and is clipped by the map mask) and fills itself in once the field's
/// contents exist; the portals come with the sandbox's quest masks applied.
/// A portal is sent with showguide (UIMinimapEntity::SetFromPortal and UIMinimapDialog::UpdatePortal set m_showGuide), so it
/// also gets a guide arrow (createGuideArrow) that onUpdateGuide puts on the mask's edge while the portal is outside it.
/// </summary>
public sealed partial class MinimapPlaces : Node2D
{
    static readonly string[] LayerOrder =
    {
        "watchtower", "portal", "apartment", "home", "myhome", "enemy", "friendly", "taming_pet", "telescope", "pc", "buddy",
        "guild", "party", "interact", "npc", "shop", "taxi", "triggerobject", "text",
    };

    /// <summary>
    /// UIMinimapDialog::BuildShowAttributes: the layers the minimap options toggle and their default state (the friendly npc
    /// layer starts off). The dialog sends them as showentity and MinimapDialogMax.showEntities sets each layer's visible to
    /// state != 0. The player's saved iconCheck options would override them; there are none here.
    /// </summary>
    public static readonly Dictionary<string, bool> ShowDefaults = new()
    {
        ["shop"] = true, ["friendly"] = false, ["taming_pet"] = true, ["portal"] = true, ["buddy"] = true, ["pc"] = true, ["text"] = true,
    };

    /// <summary>MinimapDialogMax.m_guidearrow: the guide arrow frame for an entity's layer; other layers get no arrow.</summary>
    static readonly Dictionary<string, string> GuideFrames = new()
    {
        ["player"] = "player", ["portal"] = "_portal", ["party"] = "_party", ["quest_epic"] = "_quest", ["quest_world"] = "_quest",
        ["quest_repeat"] = "_quest", ["quest_event"] = "_quest", ["questportal"] = "_questportal",
    };

    /// <summary>MinimapDialogMax.arrowhalfheight (the slot's default value).</summary>
    const float ArrowHalfHeight = 9;

    sealed class Guide
    {
        public MovieClipNode Clip, Target;
        public MovieClipNode.Child Arrow, Text;
        public Vector2 At;          // the target on the map image, in pixels
        public Rect2 Hit;           // the target's hitArea in its own coordinates
        public float Width;         // the label's autoSize width
        public float Height;
    }

    readonly List<Guide> guides = new();
    readonly Node2D guideRoot;      // m_guide: in the movie root at the mask's top left, above the map
    readonly Rect2 mask;            // m_mask in the movie root

    readonly UiMovie movie;
    readonly MinimapInfo info;
    readonly PackSet packs;
    readonly Vector2 imageSize;     // the map image's drawn size in pixels
    readonly float u;
    readonly Dictionary<string, Node2D> layers = new();
    bool built;
    public int Portals, Npcs, Labels, Objects;

    /// <summary>The player's level (the taxi's best-driver icon) and the server's found-taxi fields (its enabled frame).</summary>
    public int PlayerLevel;
    public IReadOnlyCollection<int> FoundTaxis = Array.Empty<int>();
    /// <summary>The interact objects' state as the server sends it (1 = reactable; 0 before any packet).</summary>
    public int InteractState;

    public MinimapPlaces(UiMovie movie, MinimapInfo info, PackSet packs, Vector2 imageSize, float u, Node2D guideRoot, Rect2 mask)
    {
        this.guideRoot = guideRoot; this.mask = mask;
        this.movie = movie; this.info = info; this.packs = packs; this.imageSize = imageSize; this.u = u;
        Name = "MinimapPlaces";
        foreach (var l in LayerOrder) { var n = new Node2D { Name = l, Visible = ShowDefaults.GetValueOrDefault(l, true) }; layers[l] = n; AddChild(n); }
    }

    public MinimapPlaces() { }

    public override void _Process(double delta)
    {
        if (built) { UpdateGuides(); return; }
        var fc = GetTree().GetFirstNodeInGroup(FieldContents.Group) as FieldContents;   // the first in tree order, as a walk finds
        if (fc?.Portals == null) return;
        built = true;
        try { Build(fc); }
        catch (Exception e) { GD.PushWarning("minimap places: " + e.Message); }
    }

    void Build(FieldContents fc)
    {
        var icons = new MinimapIcons(packs, FieldContents.Npcs(packs), new GameStrings(packs, "en"));
        // BuildEntities (uiminimapentity.cpp:2900-2938): every proxy that is not a portal first (AddProxy), then the portals,
        // then the taxi, telescope, return point, named objects, triggers and map objects. Entities stack in a layer by when
        // they were added, and an indoor portal draws its npc's icon in the npc layer (FUN_141a44c00), so the npcs go under
        // the portals. The order inside each group is the server's object ids through a hash map, not reproduced here.
        foreach (var n in fc.SpawnedNpcs)
        {
            var e = MinimapIcons.Npc(n.Row);
            if (!e.Visible || !n.Root.Visible) continue;
            if (Add(e, FieldPortals.ToMs2(n.Position))) Npcs++;
        }
        foreach (var p in fc.Portals.All)
        {
            var e = icons.Portal(fc.FieldId, p.TargetField, (int)p.Entity.Number("PortalType"), p.Visible, p.Entity.Bool("MinimapIconVisible", true));
            if (e == null || !e.Visible) continue;
            if (Add(e, p.Position, showGuide: true)) Portals++;
        }
        // BuildTaxi, when the field has a taxi station: the first world object of the MS2TaxiStation model, at its Position
        if (icons.HasTaxi(fc.FieldId) && fc.Map?.Entities.FirstOrDefault(e => IsModel(e, "MS2TaxiStation")) is { } taxi)
        {
            int bestLevel = (int)new ClientConstants(packs).Float("TaxiCallBestDriverLevel", 25);
            if (Add(MinimapIcons.Taxi(FoundTaxis.Contains(fc.FieldId), PlayerLevel >= bestLevel), taxi.Position)) Objects++;
        }
        // BuildNamed: the interact objects (CInteractObject, a world object with an interactID), with the server's state
        foreach (var e in fc.Map?.Entities.Where(e => IsModel(e, "MS2InteractObject")) ?? Enumerable.Empty<FieldEntity>())
        {
            int id = (int)e.Number("interactID");
            var (icon, type) = id > 0 ? icons.InteractRow(packs, id) : ("", 0);
            if (MinimapIcons.Named(icon, type, InteractState) is not { } named || !named.Visible) continue;
            if (Add(named, e.Position)) Objects++;
        }
        // BuildMapObjects: every world object of the MS2Minimap model
        foreach (var e in fc.Map?.Entities.Where(e => IsModel(e, "MS2Minimap")) ?? Enumerable.Empty<FieldEntity>())
            if (Add(MinimapIcons.MapObject(e.Get("MinimapIconFrame")), e.Position)) Objects++;
        GD.Print($"minimap places: {Portals} portals, {Npcs} npcs, {Objects} objects, {Labels} labels");
    }

    /// <summary>WorldService's model lists match an entity whose model is the list's model or has it as a mixin.</summary>
    static bool IsModel(FieldEntity e, string model) =>
        string.Equals(e.ModelName, model, StringComparison.OrdinalIgnoreCase) || e.HasMixin(model);

    /// <summary>createEntity (and createEntityText when it has a label, createGuideArrow for showguide) at the entity's map position.</summary>
    bool Add(MinimapIcons.Entity e, Vec3 world, bool showGuide = false)
    {
        var (iu, iv) = info.ToImage(world);
        var at = new Vector2((float)iu * imageSize.X, (float)iv * imageSize.Y);
        var icon = MinimapEntities.Create(movie, e.Icon, e.Frame, u, e.InFieldNpc);
        if (icon == null) return false;
        icon.Scale = new Vector2(u, u);
        icon.Position = at;
        (layers.GetValueOrDefault(e.Layer) ?? layers["npc"]).AddChild(icon);
        if (!string.IsNullOrEmpty(e.Label))
        {
            var text = movie.BuildClass(MinimapEntities.ClassPrefix + "text_" + e.Icon, u) ?? movie.BuildClass(MinimapEntities.ClassPrefix + "text_normal", u);
            if (text != null)
            {
                text.Scale = new Vector2(u, u);
                text.Position = at;
                text.Modulate = icon.Modulate;
                foreach (var t in text.TextFields) CenterText(t, e.Label);
                layers["text"].AddChild(text);
                Labels++;
            }
        }
        if (showGuide) AddGuide(e, icon, at);
        return true;
    }

    /// <summary>
    /// createGuideArrow(layer, entity, guideName or name): a minimap_guide_arrow whose m_arrow and m_text go to the layer's
    /// m_guidearrow frame; m_text (a CLIK Label) gets autoSize "left" and the name, and the arrow starts hidden. It is added
    /// at the bottom of m_guide (on top only for questportal).
    /// </summary>
    void AddGuide(MinimapIcons.Entity e, MovieClipNode icon, Vector2 at)
    {
        if (guideRoot == null || !GuideFrames.TryGetValue(e.Layer, out var frame)) return;
        var clip = movie.BuildClass("minimap_guide_arrow", u);
        var arrow = clip?.Direct("m_arrow");
        var text = clip?.Direct("m_text");
        if (arrow?.Node is not MovieClipNode am || text?.Node is not MovieClipNode tm) return;
        if (am.Labels.TryGetValue(frame, out var af)) am.GotoFrame(af);
        if (tm.Labels.TryGetValue(frame, out var tf)) tm.GotoFrame(tf);
        // Label.calculateWidth: ceil(textWidth + left + right + 5) with the field's constraint margins; the field is placed
        // at x 2 with a 100 wide rect from x -2 in the 100 wide label, so left 2 and right -2 cancel. Its text is centred,
        // and the field spans the label's width, so the text sits in the middle of it.
        float width = 0, height = 16;
        foreach (var t in tm.TextFields)
        {
            t.Text = e.Label ?? "";
            var lb = t.Label;
            lb.AutowrapMode = TextServer.AutowrapMode.Off;
            lb.ClipText = false;
            lb.HorizontalAlignment = HorizontalAlignment.Center;
            width = MathF.Ceiling(t.TextWidth + 5);
            height = t.Bounds.Size.Y;
            var w = lb.GetMinimumSize().X;
            float cx = width / 2 - t.Position.X;            // the field's centre in the text node's coordinates
            lb.Size = new Vector2(w, lb.Size.Y);
            lb.Position = new Vector2(cx - w * lb.Scale.X / 2, lb.Position.Y);
        }
        var hit = icon.Direct("m_hitarea") is { } h && h.Def?["id"] != null && icon.Scope.Symbol((int)h.Def["id"]) is { } hs
            ? h.Base * UiBuilder.Rc(hs["bounds"]) : icon.ContentRect() ?? new Rect2();
        clip.Visible = false;
        guideRoot.AddChild(clip);
        if (frame != "_questportal") guideRoot.MoveChild(clip, 0);
        guides.Add(new Guide { Clip = clip, Target = icon, Arrow = arrow, Text = text, At = at, Hit = hit, Width = width, Height = height });
    }

    /// <summary>
    /// MinimapDialogMax.onUpdateGuide (player-centred mode). A guide whose target's hitArea does not meet the mask rect is
    /// shown: the arrow turns towards the target and sits where the line from the mask's centre to the target leaves the
    /// mask (moved arrowhalfheight inwards), its label beside it on the inner side and kept inside the mask; otherwise hidden.
    /// </summary>
    void UpdateGuides()
    {
        if (guides.Count == 0 || GetParent() is not Control image) return;
        var center = mask.Position + mask.Size / 2;
        var half = mask.Size / 2;
        float maxDeg = Mathf.RadToDeg(MathF.Atan2(mask.Size.Y, mask.Size.X));      // m_degree
        float ahh = ArrowHalfHeight;
        foreach (var g in guides)
        {
            // the target in the movie root: the map image's top left is the mask's, plus the image's scroll
            var t = mask.Position + (image.Position + g.At) / u;
            var hit = new Rect2(t + g.Hit.Position, g.Hit.Size);
            if (mask.Intersects(hit)) { g.Clip.Visible = false; continue; }
            float dx = t.X - center.X, dy = t.Y - center.Y;
            bool lft = dx < 0, up = dy < 0;
            float slope = dy / dx;
            float deg = MathF.Abs(MathF.Atan(slope) * (180 / MathF.PI));
            if (lft) deg = 180 - deg;
            if (up) deg = -deg;
            float ad = MathF.Abs(deg);
            Vector2 p, off, tp;
            if (maxDeg < ad && 180 - maxDeg > ad)
            {
                // top or bottom edge
                tp.X = -g.Width * 0.5f;
                if (up) { p.Y = center.Y - half.Y; off.Y = ahh; tp.Y = ahh; }
                else { p.Y = center.Y + half.Y; off.Y = -ahh; tp.Y = -ahh - g.Height + 2; }
                off.X = lft ? ahh : -ahh;
                p.X = (p.Y - center.Y) / slope + center.X;
            }
            else
            {
                tp.Y = -g.Height - ahh + 2;
                if (lft) { p.X = center.X - half.X; off.X = ahh; tp.X = -ahh; }
                else { p.X = center.X + half.X; off.X = -ahh; tp.X = -g.Width + ahh; }
                off.Y = up ? ahh : -ahh;
                p.Y = slope * (p.X - center.X) + center.Y;
            }
            var pos = p - mask.Position + off;          // m_guide sits at the mask's top left
            // the label's field rect in the root, with the label's width and height, kept inside the mask
            var r = new Rect2(mask.Position + pos + tp, g.Width, g.Height);
            if (r.Position.X < mask.Position.X) tp.X += mask.Position.X - r.Position.X;
            else if (r.End.X > mask.End.X) tp.X -= r.End.X - mask.End.X;
            if (r.Position.Y < mask.Position.Y) tp.Y = ahh;
            else if (r.End.Y > mask.End.Y) tp.Y = -ahh - g.Height + 2;
            g.Clip.Position = pos;
            var rot = Transform2D.Identity.Rotated(Mathf.DegToRad(deg));
            g.Arrow.Override = rot; g.Arrow.Node.Transform = rot;
            var tx = new Transform2D(0, tp);
            g.Text.Override = tx; g.Text.Node.Transform = tx;
            g.Clip.Visible = true;
        }
    }

    /// <summary>textField.autoSize = "center": the field grows to fit its text on a single line around its centre.</summary>
    static void CenterText(TextNode t, string s)
    {
        t.Text = s;
        var l = t.Label;
        float cx = l.Position.X + l.Size.X / 2;
        l.AutowrapMode = TextServer.AutowrapMode.Off;
        l.HorizontalAlignment = HorizontalAlignment.Center;
        l.ClipText = false;
        var w = l.GetMinimumSize().X;
        l.Size = new Vector2(w, l.Size.Y);
        l.Position = new Vector2(cx - w / 2, l.Position.Y);
    }
}
