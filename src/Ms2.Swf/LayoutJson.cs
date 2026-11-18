using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ms2.Swf;

/// <summary>Turns a <see cref="SwfMovie"/> into the layout JSON a UI can be built from. All lengths are pixels.</summary>
public static class LayoutJson
{
    /// <param name="loader">resolves an imported movie by name without extension (for example "component_v1"); null skips the library</param>
    public static JsonObject Build(SwfMovie m, string movieName, Func<string, SwfMovie> loader = null)
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
        o["images"] = new JsonArray(m.Characters.Values.Where(c => c.ImageFile != null || c.Kind == CharKind.Image).OrderBy(c => c.Id).Select(c => (JsonNode)new JsonObject
        {
            ["id"] = c.Id, ["file"] = c.ImageFile, ["embedded"] = c.ImageFile == null ? "tag " + c.TagCode : null,
            ["width"] = c.ImageWidth == 0 ? null : c.ImageWidth, ["height"] = c.ImageHeight == 0 ? null : c.ImageHeight,
        }).ToArray());
        o["subImages"] = new JsonArray(m.Characters.Values.Where(c => c.Kind == CharKind.SubImage).OrderBy(c => c.Id).Select(c => (JsonNode)new JsonObject
        {
            ["id"] = c.Id, ["image"] = c.AtlasImageId, ["x"] = c.AtlasRect.X, ["y"] = c.AtlasRect.Y, ["w"] = c.AtlasRect.W, ["h"] = c.AtlasRect.H,
        }).ToArray());

        var bases = ClassBases(m); var scripted = FrameScriptClasses(m);
        var syms = new JsonObject();
        foreach (var c in m.Characters.Values.Where(c => c.Kind is CharKind.Shape or CharKind.Sprite or CharKind.EditText or CharKind.Button
                     or CharKind.StaticText or CharKind.MorphShape).OrderBy(c => c.Id))
            syms[c.Id.ToString()] = Symbol(m, c, bases, scripted);
        o["symbols"] = syms;
        o["fonts"] = new JsonObject(m.FontNames.OrderBy(k => k.Key).Select(k => KeyValuePair.Create(k.Key.ToString(), (JsonNode)k.Value)));

        o["root"] = TimelineJson(m, m.Root, m.FrameCount);
        var named = new JsonArray();
        CollectNamed(m, m.Root, Matrix.Identity, "", 0, named, new HashSet<int>());
        o["namedInstances"] = named;
        var lib = loader == null ? null : Library(m, loader);
        if (lib != null && lib.Count > 0) o["library"] = lib;
        o["coverage"] = Coverage(m);
        return o;
    }

    static readonly HashSet<int> MnOps = new() { 0x4A, 0x5D, 0x5E, 0x5F, 0x60, 0x80, 0x86, 0xB2 };

    /// <summary>Names the movie's code refers to as classes (getlex, new, is, coerce ...) or as string constants, plus the classes of its own symbols.</summary>
    static HashSet<string> ReferencedNames(SwfMovie m)
    {
        var set = new HashSet<string>();
        foreach (var (_, raw) in m.AbcBlocks)
        {
            var abc = Abc.AbcFile.Parse(raw);
            foreach (var str in abc.Strings) set.Add(str);   // classes are often looked up by name (getDefinitionByName, attachMovie style)
            foreach (var mi in abc.Methods)
            {
                if (mi.Body == null) continue;
                foreach (var i in Abc.Disasm.Decode(mi.Body.Code))
                    if (MnOps.Contains(i.Op) && i.A > 0) { set.Add(abc.Name(i.A)); set.Add(abc.Local(i.A)); }
            }
            foreach (var inst in abc.Instances) { if (inst.Super != 0) { set.Add(abc.Name(inst.Super)); set.Add(abc.Local(inst.Super)); } }
        }
        foreach (var c in m.Characters.Values) if (c.ClassName != null && c.Kind == CharKind.Sprite && c.Timeline.Frames.Count == 0) set.Add(c.ClassName);
        // instances placed by class name (PlaceObject3 with a class and no character: imported library symbols)
        foreach (var tl in m.Characters.Values.Where(c => c.Kind == CharKind.Sprite).Select(c => c.Timeline).Append(m.Root))
            foreach (var f in tl.Frames)
                foreach (var op in f)
                    if (op.Place is { CharacterId: null, ClassName: not null } pl) set.Add(pl.ClassName);
        return set;
    }

    /// <summary>Symbols of imported movies that this movie uses by class name (it imports them with no asset list), with what they need.</summary>
    static JsonObject Library(SwfMovie m, Func<string, SwfMovie> loader)
    {
        var refs = ReferencedNames(m);
        var local = new HashSet<string>(m.SymbolClasses.Values.Concat(m.Exports.Values));
        var lib = new JsonObject();
        foreach (var g in m.Imports)
        {
            var key = Path.GetFileNameWithoutExtension(g.Url);
            SwfMovie im;
            try { im = loader(key); } catch { im = null; }
            if (im == null) continue;
            var wanted = new Dictionary<string, int>();
            foreach (var (id, name) in im.SymbolClasses.Concat(im.Exports))
                if (id != 0 && !local.Contains(name) && (refs.Contains(name) || refs.Contains(name.Substring(name.LastIndexOf('.') + 1)))) wanted[name] = id;
            if (wanted.Count == 0) continue;
            var closure = new SortedSet<int>(); var imgIds = new SortedSet<int>();
            foreach (var id in wanted.Values) Close(im, id, closure, imgIds);
            var syms = new JsonObject(); var ibases = ClassBases(im); var iscripted = FrameScriptClasses(im);
            foreach (var id in closure) if (im.Characters.TryGetValue(id, out var c) && c.Kind is CharKind.Shape or CharKind.Sprite or CharKind.EditText or CharKind.Button or CharKind.StaticText or CharKind.MorphShape) syms[id.ToString()] = Symbol(im, c, ibases, iscripted);
            lib[key] = new JsonObject
            {
                ["classes"] = new JsonObject(wanted.OrderBy(k => k.Key).Select(k => KeyValuePair.Create(k.Key, (JsonNode)k.Value))),
                ["images"] = new JsonArray(imgIds.Select(i => im.Characters[i]).Where(c => c.ImageFile != null || c.Kind == CharKind.Image).Select(c => (JsonNode)new JsonObject { ["id"] = c.Id, ["file"] = c.ImageFile, ["width"] = c.ImageWidth == 0 ? null : c.ImageWidth, ["height"] = c.ImageHeight == 0 ? null : c.ImageHeight }).ToArray()),
                ["subImages"] = new JsonArray(imgIds.Select(i => im.Characters[i]).Where(c => c.Kind == CharKind.SubImage).Select(c => (JsonNode)new JsonObject { ["id"] = c.Id, ["image"] = c.AtlasImageId, ["x"] = c.AtlasRect.X, ["y"] = c.AtlasRect.Y, ["w"] = c.AtlasRect.W, ["h"] = c.AtlasRect.H }).ToArray()),
                ["symbols"] = syms,
            };
        }
        return lib;
    }

    static void Close(SwfMovie m, int id, SortedSet<int> seen, SortedSet<int> images)
    {
        if (!m.Characters.TryGetValue(id, out var c) || !seen.Add(id)) return;
        switch (c.Kind)
        {
            case CharKind.Sprite:
                foreach (var f in c.Timeline.Frames) foreach (var op in f) if (op.Place?.CharacterId is int cid) Close(m, cid, seen, images);
                break;
            case CharKind.Button: foreach (var b in c.Button) Close(m, b.CharacterId, seen, images); break;
            case CharKind.Shape:
                if (c.BitmapFills != null) foreach (var b in c.BitmapFills)
                {
                    images.Add(b.BitmapId);
                    if (m.Characters.TryGetValue(b.BitmapId, out var sub) && sub.AtlasImageId is int ai) images.Add(ai);
                }
                break;
        }
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

    /// <summary>For each class bound to a symbol: its super class chain from the movie's own ABC (nearest first).</summary>
    static Dictionary<string, List<string>> ClassBases(SwfMovie m)
    {
        var super = new Dictionary<string, string>();
        foreach (var (_, raw) in m.AbcBlocks)
        {
            var abc = Abc.AbcFile.Parse(raw);
            foreach (var i in abc.Instances) super[abc.Name(i.Name)] = i.Super == 0 ? null : abc.Name(i.Super);
        }
        var r = new Dictionary<string, List<string>>();
        foreach (var c in m.Characters.Values)
        {
            if (c.ClassName == null) continue;
            var l = new List<string>(); var cur = c.ClassName; int guard = 0;
            while (cur != null && super.TryGetValue(cur, out var s) && s != null && s != "Object" && guard++ < 32) { l.Add(s); cur = s; }
            r[c.ClassName] = l;
        }
        return r;
    }

    /// <summary>
    /// The classes whose constructor calls addFrameScript (the frameN methods Flash compiles a timeline's frame code into).
    /// A sprite with none of them has no frame code: it never stops, so once placed it plays its frames over and over.
    /// </summary>
    static HashSet<string> FrameScriptClasses(SwfMovie m)
    {
        var set = new HashSet<string>();
        foreach (var (_, raw) in m.AbcBlocks)
        {
            var abc = Abc.AbcFile.Parse(raw);
            foreach (var inst in abc.Instances)
            {
                var body = inst.Init >= 0 && inst.Init < abc.Methods.Length ? abc.Methods[inst.Init].Body : null;
                if (body == null) continue;
                foreach (var i in Abc.Disasm.Decode(body.Code))
                    if ((i.Op == 0x46 || i.Op == 0x4F) && abc.Local(i.A) == "addFrameScript") { set.Add(abc.Name(inst.Name)); break; }
            }
        }
        return set;
    }

    static JsonObject Symbol(SwfMovie m, Character c, Dictionary<string, List<string>> bases = null, HashSet<string> scripted = null)
    {
        var o = new JsonObject { ["kind"] = KindName(c.Kind), ["class"] = c.ClassName,
            ["bases"] = c.ClassName != null && bases != null && bases.TryGetValue(c.ClassName, out var bl) && bl.Count > 0 ? new JsonArray(bl.Select(x => (JsonNode)x).ToArray()) : null, ["export"] = c.ExportName, ["bounds"] = Rc(c.Bounds), ["scale9"] = Rc(c.Scale9) };
        if (c.Kind == CharKind.Sprite && c.ClassName != null && scripted != null && scripted.Contains(c.ClassName)) o["frameScripts"] = true;
        switch (c.Kind)
        {
            case CharKind.Shape:
                o["fill"] = c.SolidFill?.Hex;
                if (c.Paths != null) o["paths"] = new JsonArray(c.Paths.Select(pp => (JsonNode)new JsonObject { ["fill"] = pp.Color.Hex, ["pts"] = new JsonArray(pp.Points.Select(v => (JsonNode)Math.Round(v, 2)).ToArray()) }).ToArray());
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
                    // readability text: a CSMTextSettings tag, or a device font (TextField::SetTextFieldDef, sfr_gfx_03e.cpp:191)
                    ["readable"] = c.AaReadable || !t.UseOutlines ? true : null,
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

    /// <summary>A placement's matrix or colour as its last move left it (a copy), else as placed.</summary>
    static JsonNode Current(JsonObject placed, string key)
    {
        if (placed["moves"] is JsonArray mv)
            for (int i = mv.Count - 1; i >= 0; i--)
            {
                if (key == "matrix" && !((bool?)mv[i]["hasMatrix"] ?? false)) continue;
                if (mv[i][key] is { } v) return v.DeepClone();
            }
        return placed[key]?.DeepClone();
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
                if (p.CharacterId != null || (p.ClassName != null && !p.Move))
                {
                    if (live.Remove(p.Depth, out var old)) old["last"] = f - 1;
                    // PlaceObject2/3 with the move flag and a character replaces the character at that depth and keeps what the
                    // tag leaves out (SWF spec, PlaceObject2: "the character at the depth is replaced ... its other properties
                    // are retained"): the matrix and colour transform as they last were
                    bool replace = p.Move && old != null;
                    var o = new JsonObject
                    {
                        ["depth"] = p.Depth, ["id"] = p.CharacterId, ["name"] = p.Name, ["class"] = p.ClassName, ["first"] = f,
                        ["kind"] = p.CharacterId == null ? "class" : m.Characters.TryGetValue(p.CharacterId.Value, out var ch) ? KindName(ch.Kind) : "missing",
                        ["matrix"] = p.Matrix == null && replace ? Current(old, "matrix") : Mx(p.Matrix),
                        ["color"] = p.Color == null && replace ? Current(old, "color") : Cx(p.Color),
                        ["clipDepth"] = p.ClipDepth, ["ratio"] = p.Ratio,
                        ["blend"] = p.BlendMode, ["visible"] = p.Visible, ["cacheAsBitmap"] = p.CacheAsBitmap,
                        ["filters"] = Filters(p.Filters),
                    };
                    live[p.Depth] = o; all.Add(o);
                }
                else if (p.ClassName != null && !p.Move)
                {
                    // PlaceObject3 by class name only: an instance of a class the movie gets at run time (often from a shared
                    // library such as commonresource), e.g. the game menu's icon layer "m_layerBtn" of class export_common_menu
                    if (live.Remove(p.Depth, out var old)) old["last"] = f - 1;
                    var o = new JsonObject
                    {
                        ["depth"] = p.Depth, ["id"] = null, ["name"] = p.Name, ["class"] = p.ClassName, ["first"] = f, ["kind"] = "class",
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
        new JsonArray(f.Select(x => (JsonNode)new JsonObject { ["type"] = x.Type, ["color"] = x.Color, ["blurX"] = x.BlurX == 0 ? null : R(x.BlurX), ["blurY"] = x.BlurY == 0 ? null : R(x.BlurY),
            ["angle"] = x.Angle == 0 ? null : R(x.Angle), ["distance"] = x.Distance == 0 ? null : R(x.Distance), ["strength"] = x.Strength == 1 ? null : R(x.Strength),
            ["passes"] = x.Passes == 1 ? null : x.Passes, ["inner"] = x.Inner ? true : null, ["knockout"] = x.Knockout ? true : null }).ToArray());

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
