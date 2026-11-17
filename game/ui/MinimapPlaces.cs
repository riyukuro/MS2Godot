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

    readonly UiMovie movie;
    readonly MinimapInfo info;
    readonly PackSet packs;
    readonly Vector2 imageSize;     // the map image's drawn size in pixels
    readonly float u;
    readonly Dictionary<string, Node2D> layers = new();
    bool built;
    public int Portals, Npcs, Labels;

    public MinimapPlaces(UiMovie movie, MinimapInfo info, PackSet packs, Vector2 imageSize, float u)
    {
        this.movie = movie; this.info = info; this.packs = packs; this.imageSize = imageSize; this.u = u;
        Name = "MinimapPlaces";
        foreach (var l in LayerOrder) { var n = new Node2D { Name = l, Visible = ShowDefaults.GetValueOrDefault(l, true) }; layers[l] = n; AddChild(n); }
    }

    public MinimapPlaces() { }

    public override void _Process(double delta)
    {
        if (built) return;
        var fc = Find(GetTree().Root);
        if (fc?.Portals == null) return;
        built = true;
        try { Build(fc); }
        catch (Exception e) { GD.PushWarning("minimap places: " + e.Message); }
    }

    static FieldContents Find(Node n)
    {
        if (n is FieldContents f) return f;
        foreach (var c in n.GetChildren()) if (Find(c) is { } r) return r;
        return null;
    }

    void Build(FieldContents fc)
    {
        var icons = new MinimapIcons(packs, FieldContents.Npcs(packs), new GameStrings(packs, "en"));
        foreach (var p in fc.Portals.All)
        {
            var e = icons.Portal(fc.FieldId, p.TargetField, (int)p.Entity.Number("PortalType"), p.Visible, p.Entity.Bool("MinimapIconVisible", true));
            if (e == null || !e.Visible) continue;
            if (Add(e, p.Position)) Portals++;
        }
        foreach (var n in fc.SpawnedNpcs)
        {
            var e = MinimapIcons.Npc(n.Row);
            if (!e.Visible || !n.Root.Visible) continue;
            if (Add(e, FieldPortals.ToMs2(n.Position))) Npcs++;
        }
        GD.Print($"minimap places: {Portals} portals, {Npcs} npcs, {Labels} labels");
    }

    /// <summary>createEntity (and createEntityText when it has a label) at the entity's map position.</summary>
    bool Add(MinimapIcons.Entity e, Vec3 world)
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
        return true;
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
