using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Formats.Pack;
using NUnit.Framework;

namespace Ms2.Avm2.Tests;

/// <summary>UI movies' own ActionScript driven the way the client's C++ drives them (needs MS2_DATA).</summary>
public class MovieTests
{
    static MovieSource src;

    static (Runtime Rt, Player P) Open(string key)
    {
        if (src == null)
        {
            var d = Environment.GetEnvironmentVariable("MS2_DATA");
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
            var packs = new PackSet(d);
            src = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
        }
        var (rt, p) = src.Open(key, r => r.Budget = 20_000_000);
        p.External = (n, a) => null;
        Assert.That(p.Start(), Is.True, key + " document class");
        for (int i = 0; i < 5; i++) p.Tick();
        return (rt, p);
    }

    /// <summary>A namespace constant (<c>namespace motion_internal = "..."</c>, a script trait with a namespace value) is the
    /// namespace itself: fl.motion's MotionBase keeps spanStart in it, and the timeline code reaches it through an RTQName whose
    /// namespace comes from that constant. It used to read back as null (public), so the write found no property.</summary>
    [Test]
    public void NamespaceConstantQualifiesRuntimeNames()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var packs = new PackSet(d);
        var source = new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
        var (rt, p) = source.Open("bg_login_2020_spring", r => r.Budget = 20_000_000);
        p.External = (n, a) => null;
        p.Start();
        Assert.That(rt.Errors.Where(e => e.Contains("spanStart")), Is.Empty);
        Assert.That(rt.Missing.Keys.Where(k => k.Contains("spanStart")), Is.Empty);
    }

    static object Get(Player p, object o, string n) => p.RT.GetProperty(o, Name.Public(n));

    static MovieSource Fresh()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var packs = new PackSet(d);
        return new MovieSource(k => packs.FindByFileName("Resource/Gfx", k + ".gfx") is { } e ? packs.Read("Resource/Gfx", e) : null);
    }

    /// <summary>The display list after the document class and 5 frames: type, name, depth, matrix, colour, visibility,
    /// frame and text of every object, in child order.</summary>
    static string DisplayList(MovieSource source, string key, out Player player)
    {
        var (_, p) = source.Open(key, r => r.Budget = 20_000_000);
        p.External = (n, a) => null;
        Assert.That(p.Start(), Is.True, key + " document class");
        for (int i = 0; i < 5; i++) p.Tick();
        var sb = new System.Text.StringBuilder();
        void Walk(AsDisplayObject d, int depth)
        {
            var m = d.Matrix;
            sb.Append(' ', depth).Append(d.GetType().Name).Append(' ').Append(d.InstanceName).Append(' ').Append(d.Depth)
              .Append($" [{m.A:R} {m.B:R} {m.C:R} {m.D:R} {m.Tx:R} {m.Ty:R}] {d.Color} {d.IsVisible}");
            if (d is AsMovieClip mc) sb.Append(" f").Append(mc.currentFrame);
            if (d is AsTextField t) sb.Append(" \"").Append(t.Text).Append('"');
            sb.Append('\n');
            if (d is AsContainer c) foreach (var ch in c.Children) Walk(ch, depth + 1);
        }
        Walk(p.Stage, 0);
        player = p;
        return sb.ToString();
    }

    /// <summary>
    /// A movie source shared by every open (MovieDialog's, one per pack set) gives each open the same display list as a
    /// source of its own, the second open too: the cached layouts and code are only read.
    /// </summary>
    [TestCase("uiinventorydialog")]
    [TestCase("uiworldmapdialog")]
    [TestCase("uishopdialog")]
    [TestCase("uimyinfodialog")]
    public void SharedSourceOpensMatchAFreshSource(string key)
    {
        var shared = Fresh();
        var first = DisplayList(shared, key, out var p1);
        var second = DisplayList(shared, key, out var p2);
        var fresh = DisplayList(Fresh(), key, out var pf);
        Assert.That(fresh.Split('\n').Length, Is.GreaterThan(40), "a whole window's display list");
        Assert.That(p2.Layout, Is.SameAs(p1.Layout), "the layout is built once");
        Assert.That(first, Is.EqualTo(fresh));
        Assert.That(second, Is.EqualTo(fresh));
        Assert.That(p1.Layout.ToJsonString(), Is.EqualTo(pf.Layout.ToJsonString()), "the opens leave the shared layout as built");
    }

    [Test]
    public void LoginTypingTabAndConnect()
    {
        var (rt, p) = Open("uilogindialog");
        var dlg = p.FindOwner("onEnterKey");
        string connected = null;
        p.External = (n, a) =>
        {
            if (a.Length > 0 && Rt.ToString(a[0]) == "OnConnect")
                connected = Rt.ToString(Get(p, Get(p, dlg, "m_inputID"), "text")) + "/" + Rt.ToString(Get(p, Get(p, dlg, "m_inputPW"), "text"));
            return null;
        };
        var id = (AsDisplayObject)Get(p, Get(p, dlg, "m_inputID"), "textField");
        var b = id.GetBounds(null);
        p.Click(b.X + b.W / 2, b.Y + b.H / 2); p.Tick();
        Assert.That(p.Focus, Is.SameAs(id));
        ((AsTextField)p.Focus).setSelection(0, ((AsTextField)p.Focus).Text.Length);
        foreach (var ch in "tester") p.Key(char.ToUpperInvariant(ch), ch, true);
        p.Tick();
        p.Call(dlg, "onTabKey"); p.Tick();
        Assert.That(p.Focus, Is.SameAs(Get(p, Get(p, dlg, "m_inputPW"), "textField")));
        foreach (var ch in "secret") p.Key(char.ToUpperInvariant(ch), ch, true);
        p.Tick();
        p.Call(dlg, "onEnterKey"); p.Tick();
        Assert.That(connected, Is.EqualTo("tester/secret"));
        Assert.That(rt.Errors, Is.Empty);
    }

    /// <summary>
    /// Ctrl+V and Shift+Insert paste the clipboard into the login fields (TextKeyMap::InitWindowsKeyMap, KeyAct_Paste,
    /// sfr_gfx_07.cpp:847); the id field's maxChars 32 cuts a longer paste (DocView::EditCommand Cmd_ReplaceTextByPlainText).
    /// </summary>
    [Test]
    public void LoginFieldsTakeAPaste()
    {
        var (rt, p) = Open("uilogindialog");
        var dlg = p.FindOwner("onEnterKey");
        string connected = null;
        p.External = (n, a) =>
        {
            if (a.Length > 0 && Rt.ToString(a[0]) == "OnConnect")
                connected = Rt.ToString(Get(p, Get(p, dlg, "m_inputID"), "text")) + "/" + Rt.ToString(Get(p, Get(p, dlg, "m_inputPW"), "text"));
            return null;
        };
        var clip = new TextClipboard();
        p.Clipboard = clip;
        var id = (AsTextField)Get(p, Get(p, dlg, "m_inputID"), "textField");
        var b = id.GetBounds(null);
        p.Click(b.X + b.W / 2, b.Y + b.H / 2); p.Tick();
        Assert.That(p.Focus, Is.SameAs(id));
        id.setSelection(0, id.Text.Length);
        clip.SetText(new string('a', 30) + "bcdef");
        p.Key(86, 0, true, false, true); p.Key(86, 0, false, false, true);
        p.Tick();
        Assert.That(id.Text, Is.EqualTo(new string('a', 30) + "bc"), "maxChars 32");
        id.setSelection(0, id.Text.Length);
        clip.SetText("tester");
        p.Key(86, 0, true, false, true); p.Key(86, 0, false, false, true);
        p.Tick();
        p.Call(dlg, "onTabKey"); p.Tick();
        Assert.That(p.Focus, Is.SameAs(Get(p, Get(p, dlg, "m_inputPW"), "textField")));
        clip.SetText("secret");
        p.Key(45, 0, true, true, false); p.Key(45, 0, false, true, false);
        p.Tick();
        p.Call(dlg, "onEnterKey"); p.Tick();
        Assert.That(connected, Is.EqualTo("tester/secret"));
        Assert.That(rt.Errors, Is.Empty);
    }

    /// <summary>
    /// Delete in the login id field (KeyAct_Delete, sfr_gfx_07.cpp:798): the selection goes, then the char after the caret,
    /// and at the end of the text nothing; the field's change listener sees only the deletions, and onEnterKey sends what
    /// is left.
    /// </summary>
    [Test]
    public void LoginIdFieldTakesDelete()
    {
        var (rt, p) = Open("uilogindialog");
        var dlg = p.FindOwner("onEnterKey");
        string connected = null;
        p.External = (n, a) =>
        {
            if (a.Length > 0 && Rt.ToString(a[0]) == "OnConnect")
                connected = Rt.ToString(Get(p, Get(p, dlg, "m_inputID"), "text"));
            return null;
        };
        var id = (AsTextField)Get(p, Get(p, dlg, "m_inputID"), "textField");
        var b = id.GetBounds(null);
        p.Click(b.X + b.W / 2, b.Y + b.H / 2); p.Tick();
        Assert.That(p.Focus, Is.SameAs(id));
        int changes = 0;
        id.addEventListener("change", new AsPlainFunction(p.RT, new NativeMethod("f", (t, a) => { changes++; return Rt.Undef; })));
        foreach (var ch in "testerXY") p.Key(char.ToUpperInvariant(ch), ch, true);
        p.Tick();
        int typed = changes;
        void Delete() { p.Key(46, 0, true); p.Key(46, 0, false); p.Tick(); }
        id.setSelection(6, 7);
        Delete();
        Assert.That((id.Text, id.caretIndex, changes - typed), Is.EqualTo(("testerY", 6, 1)), "the selection");
        Delete();
        Assert.That((id.Text, id.caretIndex, changes - typed), Is.EqualTo(("tester", 6, 2)), "the char after the caret");
        Delete();
        Assert.That((id.Text, changes - typed), Is.EqualTo(("tester", 2)), "at the end: nothing, no change event");
        p.Call(dlg, "onEnterKey"); p.Tick();
        Assert.That(connected, Is.EqualTo("tester"));
        Assert.That(rt.Errors, Is.Empty);
    }

    [Test]
    public void LoginIdFieldTakesCaretKeys()
    {
        var (rt, p) = Open("uilogindialog");
        var dlg = p.FindOwner("onEnterKey");
        string connected = null;
        p.External = (n, a) =>
        {
            if (a.Length > 0 && Rt.ToString(a[0]) == "OnConnect")
                connected = Rt.ToString(Get(p, Get(p, dlg, "m_inputID"), "text"));
            return null;
        };
        var id = (AsTextField)Get(p, Get(p, dlg, "m_inputID"), "textField");
        var b = id.GetBounds(null);
        p.Click(b.X + b.W / 2, b.Y + b.H / 2); p.Tick();
        Assert.That(p.Focus, Is.SameAs(id));
        foreach (var ch in "testerXY") p.Key(char.ToUpperInvariant(ch), ch, true);
        p.Tick();
        void Press(uint key, bool shift = false) { p.Key(key, 0, true, shift); p.Key(key, 0, false, shift); p.Tick(); }
        (string, int, int, int) State() => (id.Text, id.caretIndex, id.selectionBeginIndex, id.selectionEndIndex);
        Press(0x25); Press(0x25);
        Assert.That(State(), Is.EqualTo(("testerXY", 6, 6, 6)), "Left twice");
        Press(0x27, shift: true);
        Assert.That(State(), Is.EqualTo(("testerXY", 7, 6, 7)), "Shift+Right selects the X");
        Press(46);
        Assert.That(State(), Is.EqualTo(("testerY", 6, 6, 6)), "Delete removes the selection");
        Press(0x24); Press(46);
        Assert.That(State(), Is.EqualTo(("esterY", 0, 0, 0)), "Home, then Delete");
        Press(0x23); Press(0x25, shift: true); Press(0x25, shift: true);
        Assert.That(State(), Is.EqualTo(("esterY", 4, 4, 6)), "End, then Shift+Left twice");
        Press(8);
        Assert.That(State(), Is.EqualTo(("este", 4, 4, 4)), "Backspace removes the selection");
        p.Call(dlg, "onEnterKey"); p.Tick();
        Assert.That(connected, Is.EqualTo("este"));
        Assert.That(rt.Errors, Is.Empty);
    }

    [Test]
    public void MinimapEntityHidesItsHitArea()
    {
        var (rt, p) = Open("uiminimapdialog");
        var dlg = p.FindOwner("createEntity");
        var e = p.Call(dlg, "createEntity", p.NewObject(("mcname", "portal"), ("frame", "portal"), ("layer", "portal"), ("x", 120), ("y", 80),
            ("visible", true), ("showtext", false), ("showguide", false), ("name", "portal"))) as AsSprite;
        p.Tick();
        Assert.That(e, Is.Not.Null);
        var hit = Get(p, e, "m_hitarea") as AsDisplayObject;
        Assert.That(hit, Is.Not.Null);
        Assert.That(e.hitArea, Is.SameAs(hit));
        Assert.That(hit.IsVisible, Is.False);
        Assert.That(rt.Errors, Is.Empty);
    }

    /// <summary>The minimap's map-alpha slider box (SliderEx.updateTooltip, a vertical slider): it puts the autoSize CENTER
    /// percent field at x = the gap it measured from the box's left, so the field's x must be its text rectangle's left
    /// side, as TextField::GetX / SetX give (sfr_gfx_03e.cpp:760, sfr_gfx_03f.cpp:861); with x as the matrix origin the
    /// text sat off that gap. The minimap's art puts the field nearer the box's left than its right, so a box at its
    /// minimum width does not centre the text; only the gap is checked.</summary>
    [Test]
    public void MinimapSliderTextKeepsItsGap()
    {
        var (rt, p) = Open("uiminimapdialog");
        var dlg = p.FindOwner("setAlpha");
        var slider = Get(p, Get(p, dlg, "m_controller"), "m_mapAlpha") as AsObject;
        Assert.That(slider, Is.Not.Null);
        foreach (var v in new[] { 0.0, 80.0, 100.0 })
        {
            var c = SliderTooltipCentres(p, slider, v);
            Assert.That(c.Gap, Is.EqualTo(c.Left).Within(0.1), $"value {v}: the text keeps the gap measured from the box's left");
        }
        Assert.That(rt.Errors, Is.Empty);
    }
    /// <summary>SliderEx's value box after its private showTooltip / updateTooltip run at the value (scaleform.ms2.controls.SliderEx):
    /// in the tooltip's space the centres of the box art (m_bg) and of the percent text field (m_percent), the text's gap
    /// from the box's left and the gap the first call measured (m_tooltipAddSize.left); in the slider's space the centres
    /// of the box art and of the thumb.</summary>
    static (double Box, double Text, double Gap, double Left, double BoxOnSlider, double Thumb) SliderTooltipCentres(Player p, AsObject slider, double value)
    {
        object Get(object o, string n) => p.RT.GetProperty(o, Name.Public(n));
        void Private(string name, params object[] args)
        {
            foreach (var b in slider.Traits.All) if (b.Local == name) { b.Method.Invoke(slider, args); return; }
            Assert.Fail("no " + name);
        }
        object Find(object o, string name)
        {
            if (Get(o, "name") as string == name) return o;
            if (o is not AsContainer) return null;
            for (int i = 0, n = Rt.ToInt32(Get(o, "numChildren")); i < n; i++)
                if (Find(p.Call(o, "getChildAt", (double)i), name) is { } f) return f;
            return null;
        }
        p.RT.SetProperty(slider, Name.Public("value"), value);
        Private("showTooltip", true);
        Private("updateTooltip");
        var tip = Get(slider, "m_tooltip");
        double Left(object o, object space) => Rt.ToNumber(Get(p.Call(o, "getBounds", space), "x"));
        double Centre(object o, object space) { var r = p.Call(o, "getBounds", space); return Rt.ToNumber(Get(r, "x")) + Rt.ToNumber(Get(r, "width")) / 2; }
        var box = Find(tip, "m_bg"); var text = Find(tip, "m_percent");
        Assert.That(box, Is.Not.Null); Assert.That(text, Is.InstanceOf<AsTextField>());
        return (Centre(box, tip), Centre(text, tip), Left(text, tip) - Left(box, tip), Rt.ToNumber(Get(Get(slider, "m_tooltipAddSize"), "left")),
            Centre(box, slider), Centre(Get(slider, "thumb"), slider));
    }

    /// <summary>PopupImageBalloonAni declares m_popup private, so a public-namespace read from outside (Value::GetMember, the host's
    /// Get) finds no member and answers undefined, as Scaleform does; the popup that open() makes is reachable as the last child
    /// of m_dummy. Not a runtime gap.</summary>
    [Test]
    public void PrivateSlotIsNotReadableFromOutside()
    {
        var (rt, p) = Open("uipopupimageballoon");
        var dlg = p.FindOwner("createEntity");
        var e = p.Call(dlg, "createEntity", 7.0, "imageballoon_Ani", false) as AsObject;
        p.Tick();
        Assert.That(e, Is.Not.Null);
        p.Call(e, "open", p.NewObject(("text", "0123"), ("imgpath", ""), ("skin", "")));
        Assert.That(e.Traits.FindAny("m_popup").Ns.Kind, Is.EqualTo(NsKind.Private));
        Assert.That(Get(p, e, "m_popup"), Is.SameAs(Undefined.Value));
        var dummy = Get(p, e, "m_dummy");
        var last = p.Call(dummy, "getChildAt", Rt.ToNumber(Get(p, dummy, "numChildren")) - 1);
        Assert.That(Rt.ToString(p.Call(last, "toString")), Is.EqualTo("PopupImageBalloon"));
        Assert.That(rt.Errors, Is.Empty);
    }

    /// <summary>Loader.load of a movie that itself imports others: the imports' code loads before the loaded movie's own
    /// (MovieRun's LoadUrl path), so a class that only an import defines resolves afterwards.</summary>
    [Test]
    public void LoadedMovieImportsLoadWithIt()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        var packs = new PackSet(d);
        var keys = packs.List("Resource/Gfx").Where(e => e.EndsWith(".gfx", StringComparison.OrdinalIgnoreCase)).ToDictionary(e => Path.GetFileNameWithoutExtension(e).ToLowerInvariant(), e => e);
        var source = new MovieSource(k => keys.TryGetValue(k.ToLowerInvariant(), out var e) ? packs.Read("Resource/Gfx", e) : null);
        string target = null, imported = null, cls = null;
        foreach (var k in keys.Keys.OrderBy(k => k))
        {
            var m = source.Movie(k);
            if (m == null || k == "uiexpbar") continue;
            foreach (var g in m.Imports)
            {
                var ik = Path.GetFileNameWithoutExtension(g.Url);
                if (ik.Equals(k, StringComparison.OrdinalIgnoreCase)) continue;
                var im = source.Movie(ik);
                var c = im?.SymbolClasses.Values.FirstOrDefault(n => n.Contains('.'));
                if (c != null) { target = k; imported = ik; cls = c; break; }
            }
            if (target != null) break;
        }
        if (target == null) Assert.Ignore("no movie imports a movie with a packaged class");
        TestContext.Out.WriteLine("target=" + target + " imported=" + imported + " cls=" + cls);
        var (rt, p) = source.Open("uiexpbar", r => r.Budget = 20_000_000);
        p.External = (n, a) => null;
        Assert.That(p.Start(), Is.True);
        Assert.That(rt.GetClass(cls), Is.Null, "the class is not loaded yet");
        var loader = rt.GetClass("flash.display.Loader").Construct();
        p.LoadUrl((AsLoader)loader, target + ".swf");
        Assert.That(rt.GetClass(cls), Is.Not.Null, cls + " (from " + imported + ", imported by " + target + ")");
    }

    [Test]
    public void ExpBarSetExp()
    {
        var (rt, p) = Open("uiexpbar");
        if (p.FindOwner("alignment") is { } al) p.Call(al, "alignment");
        var dlg = p.FindOwner("setExp");
        Assert.That(dlg, Is.Not.Null);
        p.Call(dlg, "setExp", 42.5, 10.0, "42.50%", 3);
        for (int i = 0; i < 30; i++) p.Tick();
        Assert.That(rt.Errors, Is.Empty);
    }
}
