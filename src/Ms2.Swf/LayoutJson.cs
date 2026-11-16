using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ms2.Swf;

/// <summary>Turns a <see cref="SwfMovie"/> into the layout JSON a UI can be built from. All lengths are pixels.</summary>
public static class LayoutJson
{
    public static JsonObject Build(SwfMovie m, string movieName)
    {
        var o = new JsonObject
        {
            ["movie"] = movieName,
            ["format"] = m.Signature,
            ["version"] = m.Version,
            ["stage"] = new JsonObject { ["width"] = R(m.Stage.W), ["height"] = R(m.Stage.H), ["x"] = R(m.Stage.X), ["y"] = R(m.Stage.Y) },
            ["fps"] = R(m.FrameRate),
            ["frames"] = m.FrameCount,
            ["actionScript3"] = m.ActionScript3,
            ["background"] = m.Background?.Hex,
            ["documentClass"] = m.RootClass,
            ["exporter"] = m.ExporterName == null ? null : new JsonObject { ["name"] = m.ExporterName, ["version"] = m.ExporterVersion, ["bitmapFormat"] = m.ExporterBitmapFormat },
        };
        o["imports"] = new JsonArray(m.Imports.Select(g => (JsonNode)new JsonObject
        {
            ["url"] = g.Url,
            ["assets"] = new JsonArray(g.Assets.Select(a => (JsonNode)new JsonObject { ["id"] = a.Id, ["name"] = a.Name }).ToArray()),
        }).ToArray());

        // external images (DDS atlases) and the sub-rectangles that shapes use
        o["images"] = new JsonArray(m.Characters.Values.Where(c => c.Kind == CharKind.Image).OrderBy(c => c.Id).Select(c => (JsonNode)new JsonObject
        {
            ["id"] = c.Id, ["file"] = c.ImageFile, ["embedded"] = c.ImageFile == null ? "tag " + c.TagCode : null,
            ["width"] = c.ImageWidth == 0 ? null : c.ImageWidth, ["height"] = c.ImageHeight == 0 ? null : c.ImageHeight,
        }).ToArray());
        o["subImages"] = new JsonArray(m.Characters.Values.Where(c => c.Kind == CharKind.SubImage).OrderBy(c => c.Id).Select(c => (JsonNode)new JsonObject
        {
            ["id"] = c.Id, ["image"] = c.AtlasImageId, ["x"] = c.AtlasRect.X, ["y"] = c.AtlasRect.Y, ["w"] = c.AtlasRect.W, ["h"] = c.AtlasRect.H,
        }).ToArray());

        var syms = new JsonObject();
        foreach (var c in m.Characters.Values.Where(c => c.Kind is CharKind.Shape or CharKind.Sprite or CharKind.EditText or CharKind.Button
                     or CharKind.StaticText or CharKind.MorphShape).OrderBy(c => c.Id))
            syms[c.Id.ToString()] = Symbol(m, c);
        o["symbols"] = syms;
        o["fonts"] = new JsonObject(m.FontNames.OrderBy(k => k.Key).Select(k => KeyValuePair.Create(k.Key.ToString(), (JsonNode)k.Value)));

        o["root"] = TimelineJson(m, m.Root, m.FrameCount);
        var named = new JsonArray();
        CollectNamed(m, m.Root, Matrix.Identity, "", 0, named, new HashSet<int>());
        o["namedInstances"] = named;
        o["coverage"] = Coverage(m);
        return o;
    }

    static JsonNode Coverage(SwfMovie m)
    {
        var tags = new JsonObject();
        foreach (var (code, s) in m.Stats)
            tags[code.ToString()] = new JsonObject { ["parsed"] = s.Parsed, ["skipped"] = s.Skipped, ["failed"] = s.Failed };
        return new JsonObject
        {
            ["parsed"] = m.Stats.Values.Sum(s => s.Parsed), ["skipped"] = m.Stats.Values.Sum(s => s.Skipped), ["failed"] = m.Stats.Values.Sum(s => s.Failed),
            ["abcBytes"] = m.AbcBytes, ["tags"] = tags, ["problems"] = new JsonArray(m.Problems.Select(p => (JsonNode)p).ToArray()),
        };
    }

    static double R(double v) => Math.Round(v, 3);
    static JsonNode Rc(Rect r) => r == null ? null : new JsonObject { ["x"] = R(r.X), ["y"] = R(r.Y), ["w"] = R(r.W), ["h"] = R(r.H) };

    static JsonNode Mx(Matrix m) => m == null || m.IsIdentity ? null : new JsonObject
    {
        ["x"] = R(m.Tx), ["y"] = R(m.Ty),
        ["a"] = Math.Abs(m.A - 1) < 1e-4 ? null : R(m.A), ["d"] = Math.Abs(m.D - 1) < 1e-4 ? null : R(m.D),
        ["b"] = Math.Abs(m.B) < 1e-4 ? null : R(m.B), ["c"] = Math.Abs(m.C) < 1e-4 ? null : R(m.C),
    };

    static JsonNode Cx(ColorXform c) => c == null ? null : new JsonObject
    {
        ["mul"] = new JsonArray(R(c.RMul), R(c.GMul), R(c.BMul), R(c.AMul)),
        ["add"] = new JsonArray(c.RAdd, c.GAdd, c.BAdd, c.AAdd),
    };

    static string KindName(CharKind k) => k switch
    {
        CharKind.Shape => "shape", CharKind.Sprite => "sprite", CharKind.EditText => "text", CharKind.Button => "button",
        CharKind.StaticText => "staticText", CharKind.MorphShape => "morph", _ => k.ToString().ToLowerInvariant(),
    };

    static JsonObject Symbol(SwfMovie m, Character c)
    {
        var o = new JsonObject { ["kind"] = KindName(c.Kind), ["class"] = c.ClassName, ["export"] = c.ExportName, ["bounds"] = Rc(c.Bounds), ["scale9"] = Rc(c.Scale9) };
        switch (c.Kind)
        {
            case CharKind.Shape:
                o["fill"] = c.SolidFill?.Hex;
                if (c.BitmapFills != null)
                    o["bitmaps"] = new JsonArray(c.BitmapFills.Select(b =>
                    {
                        var sub = m.Characters.TryGetValue(b.BitmapId, out var s) ? s : null;
                        return (JsonNode)new JsonObject
                        {
                            ["id"] = b.BitmapId, ["smooth"] = b.Smooth, ["repeat"] = b.Repeat, ["matrix"] = Mx(b.Matrix),
                            ["image"] = sub?.AtlasImageId, ["atlasRect"] = sub?.AtlasRect == null ? null : Rc(sub.AtlasRect),
                            ["file"] = sub?.AtlasImageId is int ai && m.Characters.TryGetValue(ai, out var im) ? im.ImageFile : sub?.ImageFile,
                        };
                    }).ToArray());
                break;
            case CharKind.EditText:
                var t = c.Text;
                o["text"] = new JsonObject
                {
                    ["fontId"] = t.FontId, ["fontClass"] = t.FontClass, ["font"] = t.FontName, ["size"] = t.Size, ["color"] = t.Color?.Hex,
                    ["align"] = t.Align, ["leftMargin"] = t.LeftMargin == 0 ? null : t.LeftMargin, ["rightMargin"] = t.RightMargin == 0 ? null : t.RightMargin,
                    ["indent"] = t.Indent == 0 ? null : t.Indent, ["leading"] = t.Leading == 0 ? null : t.Leading,
                    ["variable"] = t.Variable, ["initial"] = t.InitialText, ["maxLength"] = t.MaxLength,
                    ["wordWrap"] = t.WordWrap, ["multiline"] = t.Multiline, ["password"] = t.Password, ["readOnly"] = t.ReadOnly,
                    ["autoSize"] = t.AutoSize, ["selectable"] = !t.NoSelect, ["border"] = t.Border, ["html"] = t.Html,
                };
                break;
            case CharKind.Button:
                o["trackAsMenu"] = c.ButtonTrackAsMenu;
                o["states"] = new JsonArray(c.Button.Select(b => (JsonNode)new JsonObject
                {
                    ["up"] = b.Up, ["over"] = b.Over, ["down"] = b.Down, ["hit"] = b.Hit, ["id"] = b.CharacterId, ["depth"] = b.Depth,
                    ["matrix"] = Mx(b.Matrix), ["color"] = Cx(b.Color),
                }).ToArray());
                break;
            case CharKind.Sprite:
                o["timeline"] = TimelineJson(m, c.Timeline, c.Timeline.FrameCount);
                break;
        }
        return o;
    }

    /// <summary>Children as display-list lifespans: one entry per (depth, placement), with its matrix changes.</summary>
    static JsonNode TimelineJson(SwfMovie m, Timeline tl, int frameCount)
    {
        var live = new Dictionary<int, JsonObject>();
        var all = new List<JsonObject>();
        for (int f = 0; f < tl.Frames.Count; f++)
            foreach (var op in tl.Frames[f])
            {
                if (op.Place == null)
                {
                    if (live.Remove(op.RemoveDepth, out var gone)) gone["last"] = f - 1;
                    continue;
                }
                var p = op.Place;
                if (p.CharacterId != null)
                {
                    if (live.Remove(p.Depth, out var old)) old["last"] = f - 1;
                    var o = new JsonObject
                    {
                        ["depth"] = p.Depth, ["id"] = p.CharacterId, ["name"] = p.Name, ["class"] = p.ClassName, ["first"] = f,
                        ["kind"] = m.Characters.TryGetValue(p.CharacterId.Value, out var ch) ? KindName(ch.Kind) : "missing",
                        ["matrix"] = Mx(p.Matrix), ["color"] = Cx(p.Color), ["clipDepth"] = p.ClipDepth, ["ratio"] = p.Ratio,
                        ["blend"] = p.BlendMode, ["visible"] = p.Visible, ["cacheAsBitmap"] = p.CacheAsBitmap,
                        ["filters"] = Filters(p.Filters),
                    };
                    live[p.Depth] = o; all.Add(o);
                }
                else if (live.TryGetValue(p.Depth, out var cur))
                {
                    // property change of an existing child
                    if (p.Name != null) cur["name"] ??= p.Name;
                    if (p.Matrix != null || p.Color != null || p.Visible != null)
                    {
                        var mv = cur["moves"] as JsonArray ?? (JsonArray)(cur["moves"] = new JsonArray());
                        mv.Add(new JsonObject { ["frame"] = f, ["matrix"] = Mx(p.Matrix ?? Matrix.Identity), ["color"] = Cx(p.Color), ["visible"] = p.Visible, ["hasMatrix"] = p.Matrix != null });
                    }
                }
            }
        return new JsonObject
        {
            ["frames"] = frameCount,
            ["labels"] = new JsonArray(tl.Labels.Select(l => (JsonNode)new JsonObject { ["frame"] = l.Frame, ["name"] = l.Name }).ToArray()),
            ["children"] = new JsonArray(all.Select(x => (JsonNode)x).ToArray()),
        };
    }

    static JsonNode Filters(List<FilterInfo> f) => f == null ? null :
        new JsonArray(f.Select(x => (JsonNode)new JsonObject { ["type"] = x.Type, ["color"] = x.Color, ["blurX"] = x.BlurX == 0 ? null : R(x.BlurX), ["blurY"] = x.BlurY == 0 ? null : R(x.BlurY) }).ToArray());

    /// <summary>Named instances reachable from the root through named children, in stage coordinates (first frame they appear).</summary>
    static void CollectNamed(SwfMovie m, Timeline tl, Matrix parent, string path, int depth, JsonArray into, HashSet<int> stack)
    {
        if (depth > 12 || into.Count > 4000) return;
        var seen = new List<(int Id, Matrix M, string Name)>();
        var uniq = new HashSet<(int, Matrix, string)>();
        foreach (var f in tl.Frames)
            foreach (var op in f)
            {
                var p = op.Place;
                if (p?.CharacterId is not int cid) continue;
                var key = (cid, p.Matrix ?? Matrix.Identity, p.Name);
                bool descend = p.Name == null && p.ClipDepth == null && m.Characters.TryGetValue(cid, out var cc) && cc.Kind == CharKind.Sprite;
                if ((p.Name != null || descend) && uniq.Add(key)) seen.Add(key);
            }
        foreach (var (id, mat, name) in seen)
        {
            var abs = parent.Compose(mat);
            if (!m.Characters.TryGetValue(id, out var c)) continue;
            string p2 = name == null ? path : path.Length == 0 ? name : path + "." + name;
            if (name != null)
                into.Add(new JsonObject { ["path"] = p2, ["id"] = id, ["kind"] = KindName(c.Kind), ["class"] = c.ClassName, ["bounds"] = Rc(abs.Transform(c.Bounds)) });
            if (c.Kind == CharKind.Sprite && stack.Add(id))
            {
                CollectNamed(m, c.Timeline, abs, p2, depth + 1, into, stack);
                stack.Remove(id);
            }
        }
    }

    public static string ToJson(JsonObject o) => o.ToJsonString(new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
}
