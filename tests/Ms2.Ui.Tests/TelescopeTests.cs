using System.Xml.Linq;
using Ms2.Formats.Model;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// CInteractTelescopeObject (interactobject.cpp:955-1028): the server's interact message for a telescope finds the nearest
/// MS2Telescope with cameras (MyPC::FindTelescopeEntity, mypc.cpp:3028-3060), queues its cameras, enters the cinematic sub scene
/// with the cut scene cinematic pane's cinematic 9 and skip state 3, and takes the telescope's fog; the pane's skip puts the
/// camera, the sub scene and the field's mapxblock fog back.
/// </summary>
public class TelescopeTests
{
    sealed class TelescopeHost : LiveInteractObjectsTests.Host, IInteractHost
    {
        public Vec3 PlayerPosition { get; set; }
        public float TelescopeFindDistance { get; set; } = 300f;
        public readonly List<string> Calls = new();
        public CutSceneCinematic Cinematic { get; set; }
        public bool GamepadActive { get; set; }
        public FieldFog FieldFog { get; set; }
        public void QueueCameraSteps(IReadOnlyList<int> ids) => Calls.Add("cameras " + string.Join(",", ids));
        public void ResetTriggerCamera() => Calls.Add("camera reset");
        public void PlaySystemSound(string name) => Calls.Add("sound " + name);
        public void SetSubScene(int id)
        {
            Calls.Add($"subscene {id:x}");
            Cinematic?.SetSubScene(id);
        }
        public string ActionKeyName(int action, int kind) => $"key:{action:x}:{kind}";
        public void SetFogRange(bool on, float near, float far, uint color) => Calls.Add($"fog {on} {near} {far} {color:x8}");
        public void SetFogEffect(bool on, uint color, float upper, float lower, float percentage) =>
            Calls.Add($"hfog {on} {color:x8} {upper} {lower} {percentage}");
    }

    sealed class FakePane : ICinematicPane
    {
        public readonly List<string> Calls = new();
        public bool Loaded => true;
        public void SetCinematic(int id, string text) => Calls.Add($"setCinematic {id} [{text}]");
        public void SetOpening(string text, bool isOpening) { }
        public void SetIntro(string text) { }
        public void SetScript(string name, string text, string illust, int align) { }
        public void HideScript() { }
        public void Close() { }
        public void SetSkip(bool on, string key, string label, bool skipVisible) => Calls.Add($"setSkip {on} {key} {label} {skipVisible}");
        public void SetSkipVisible(bool visible) => Calls.Add($"skipVisible {visible}");
        public void SetCaption(CinematicPacket.Caption caption, string text1, string text2) { }
    }

    const int Row = 20;

    static FieldEntity Interact(string id, Vec3 pos)
    {
        var raw = new XEntity { Id = id, ModelName = "MS2InteractObject", Name = "obj_" + id };
        raw.Props["interactID"] = Row.ToString();
        raw.Props["Position"] = $"{pos.X}, {pos.Y}, {pos.Z}";
        var flat = new FlatModel { Name = "co_fi_funct_telescope_a01_" };
        flat.Mixins.Add("MS2Telescope");
        flat.Mixins.Add("MS2InteractObject");
        return new FieldEntity { Raw = raw, Flat = flat };
    }

    static FieldEntity Telescope(string id, Vec3 pos, string cameras, Action<XEntity> props = null)
    {
        var e = Interact(id, pos);
        e.Raw.Props["CameraIDs"] = cameras;
        props?.Invoke(e.Raw);
        return e;
    }

    static (TelescopeHost Host, LiveInteractObjects Live, CutSceneCinematic Cine, FakePane Pane, List<string> Events) Rig(params FieldEntity[] entities)
    {
        var cine = new CutSceneCinematic();
        var pane = new FakePane();
        cine.Pane = pane;
        var events = new List<string>();
        cine.SendTrigger = () => events.Add("trigger");
        cine.KeyModeChanged += on => events.Add("keymode " + on);
        var host = new TelescopeHost { Cinematic = cine };
        var live = new LiveInteractObjects(host, new InteractObjectTable(new Dictionary<int, InteractObjectRow>
        {
            [Row] = new InteractObjectRow { Id = Row, Type = InteractObjectMessage.KindTelescope, InteractionDistance = 200 },
        }));
        live.Attach(entities);
        return (host, live, cine, pane, events);
    }

    static InteractObjectMessage Use(string name) =>
        new() { Mode = InteractObjectMessage.ModeMessage, Name = name, Kind = InteractObjectMessage.KindTelescope };

    static void Fog(XEntity e)
    {
        e.Props["EnableFog"] = "True";
        e.Props["FogNear"] = "1500";
        e.Props["FogFar"] = "9000";
        e.Props["FogColor"] = "1, 0.5, 0.2";
        e.Props["EnableHeightFog"] = "True";
        e.Props["HeightFogColor"] = "0.1, 0.2, 0.3";
        e.Props["HeightFogUpperZ"] = "800";
        e.Props["HeightFogLowerZ"] = "-200";
        e.Props["HeightFogPercentage"] = "70";
    }

    /// <summary>vfunc_2: the nearest telescope's cameras, the use sound, the cinematic sub scene, cinematic 9 with skip state 3 and
    /// the key cap and description texts, then the telescope's fog (each colour packed as (int)(c * 255) per channel).</summary>
    [Test]
    public void AUseEntersTheNearestTelescopesViewWithItsCamerasAndFog()
    {
        var near = Telescope("tel-1", new Vec3(100, 0, 0), "101, 102", Fog);
        var far = Telescope("tel-2", new Vec3(250, 0, 0), "201");
        var r = Rig(near, far);
        r.Host.PlayerPosition = new Vec3(0, 0, 0);
        r.Live.Apply(Use("tel-2"));
        var obj = r.Live.ByName("tel-2");
        Assert.That(obj.TelescopeActive, Is.True);
        Assert.That(obj.TelescopeEntity, Is.SameAs(near), "the nearest telescope, not the object the message names");
        Assert.That(r.Host.Calls, Is.EqualTo(new[]
        {
            "cameras 101,102",
            "sound PC_use_telescope",
            "subscene 6200041",
            "fog True 1500 9000 ffff7f33",
            "hfog True ff19334c 800 -200 70",
        }));
        Assert.That((r.Cine.KeyMode, r.Cine.Mode, r.Cine.Id, r.Cine.Text, r.Cine.SkipState), Is.EqualTo((true, 1, 9, "", 3)));
        Assert.That(r.Pane.Calls, Is.EqualTo(new[] { "setCinematic 9 []", "setSkip True common:666 common:667 True" }));
    }

    /// <summary>The pane's skip (state 3 sends no trigger): the camera reset, the field's sub scene, the field's fog back; the
    /// object no longer hears the skip.</summary>
    [Test]
    public void TheSkipPutsTheCameraSceneAndFieldFogBack()
    {
        var r = Rig(Telescope("tel-1", new Vec3(10, 0, 0), "101", Fog));
        r.Host.FieldFog = FieldFog.Parse(XDocument.Parse(
            "<ms2><fog color=\"10,20,30\" near=\"2000\" far=\"12000\"/><heightfog color=\"40,50,60\" upper=\"300\" lower=\"-100\" percentage=\"50\"/></ms2>"));
        r.Live.Apply(Use("tel-1"));
        r.Host.Calls.Clear();
        Assert.That(r.Cine.KeyEscape(shift: false, develop: false), Is.True, "Esc at the pane asks to skip");
        Assert.That(r.Host.Calls, Is.EqualTo(new[]
        {
            "camera reset",
            "subscene 620001d",
            "fog True 2000 12000 ff0a141e",
            "hfog True ff28323c 300 -100 50",
        }));
        Assert.That(r.Events, Does.Not.Contain("trigger"), "skip state 3 sends no trigger");
        Assert.That(r.Cine.KeyMode, Is.False);
        Assert.That(r.Live.ByName("tel-1").TelescopeActive, Is.False);
        r.Host.Calls.Clear();
        r.Cine.SkipCinematic();
        Assert.That(r.Host.Calls, Is.Empty, "the skip is no longer heard");
    }

    /// <summary>UICutSceneCinematic::vfunc_13 (uicutscenecinematic.cpp:221-237): Shift+Esc ends the cinematic without the skip
    /// notify only with the Develop feature on; otherwise it is Esc, the skip, and the telescope's view ends with it.</summary>
    [Test]
    public void ShiftEscSkipsTheViewUnlessTheDevelopFeatureIsOn()
    {
        var r = Rig(Telescope("tel-1", new Vec3(10, 0, 0), "101", Fog));
        r.Live.Apply(Use("tel-1"));
        Assert.That(r.Cine.KeyEscape(shift: true, develop: false), Is.True);
        Assert.That((r.Live.ByName("tel-1").TelescopeActive, r.Cine.KeyMode), Is.EqualTo((false, false)), "a retail Shift+Esc is the skip");

        var d = Rig(Telescope("tel-1", new Vec3(10, 0, 0), "101", Fog));
        d.Live.Apply(Use("tel-1"));
        Assert.That(d.Cine.KeyEscape(shift: true, develop: true), Is.True);
        Assert.That((d.Live.ByName("tel-1").TelescopeActive, d.Cine.KeyMode), Is.EqualTo((true, false)),
            "the Develop feature's Shift+Esc posts no skip notify: the view stays, as the client's");
    }

    /// <summary>Nothing happens when no telescope is strictly within TelescopeFindDistance, when the nearest has no cameras (a
    /// farther one with cameras is not taken), or without the local player.</summary>
    [Test]
    public void NoTelescopeWithCamerasInReachDoesNothing()
    {
        var r = Rig(Telescope("tel-1", new Vec3(300, 0, 0), "101"));
        r.Live.Apply(Use("tel-1"));
        Assert.That(r.Host.Calls, Is.Empty, "300 away is not within 300");
        r.Host.PlayerPosition = new Vec3(1, 0, 0);
        r.Host.HasPlayer = false;
        r.Live.Apply(Use("tel-1"));
        Assert.That(r.Host.Calls, Is.Empty, "no local player");
        r.Host.HasPlayer = true;
        r.Live.Apply(Use("tel-1"));
        Assert.That(r.Host.Calls[0], Is.EqualTo("cameras 101"));

        var n = Rig(Telescope("tel-1", new Vec3(10, 0, 0), ""), Telescope("tel-2", new Vec3(20, 0, 0), "201"));
        n.Live.Apply(Use("tel-2"));
        Assert.That(n.Host.Calls, Is.Empty, "the nearest has no CameraIDs");
        Assert.That(n.Live.ByName("tel-2").TelescopeActive, Is.False);
        Assert.That(n.Cine.KeyMode, Is.False);
    }

    /// <summary>With a gamepad the key cap is the action key 0x49409's name; a telescope's unset fog turns the depth and height fog
    /// off with its black colour.</summary>
    [Test]
    public void AGamepadShowsTheActionKeyAndUnsetFogIsOff()
    {
        var r = Rig(Telescope("tel-1", new Vec3(10, 0, 0), "101"));
        r.Host.GamepadActive = true;
        r.Live.Apply(Use("tel-1"));
        Assert.That(r.Pane.Calls[^1], Is.EqualTo("setSkip True key:49409:2 common:667 True"));
        Assert.That(r.Host.Calls.TakeLast(2), Is.EqualTo(new[] { "fog False 0 0 ff000000", "hfog False ff000000 0 0 0" }));
    }

    /// <summary>The objects' release (a new field) skips an active telescope's view, as its dtor does; an inactive one is left.</summary>
    [Test]
    public void TheReleaseSkipsAnActiveView()
    {
        var r = Rig(Telescope("tel-1", new Vec3(10, 0, 0), "101"));
        r.Live.Release();
        Assert.That(r.Host.Calls, Is.Empty);
        r.Live.Apply(Use("tel-1"));
        r.Host.Calls.Clear();
        r.Live.Release();
        Assert.That(r.Host.Calls, Is.EqualTo(new[] { "camera reset", "subscene 620001d" }), "no field data: the fog stays");
        Assert.That(r.Live.ByName("tel-1").TelescopeActive, Is.False);
    }

    /// <summary>CFieldDataCli::LoadXBlock: the fog and height fog elements; the constructor's defaults without them.</summary>
    [Test]
    public void TheFieldFogIsTheMapxblocksOrTheDefaults()
    {
        var f = FieldFog.Parse(XDocument.Parse("<ms2><fog color=\"255,128,0\" near=\"500\" far=\"4000\"/></ms2>"));
        Assert.That((f.Fog, f.FogColor, f.FogNear, f.FogFar), Is.EqualTo((true, 0xffff8000u, 500f, 4000f)));
        Assert.That((f.HeightFog, f.HeightFogColor, f.HeightFogUpper, f.HeightFogLower, f.HeightFogPercentage), Is.EqualTo((false, 0u, 0f, -500f, 100f)));
        var none = FieldFog.Parse(XDocument.Parse("<ms2/>"));
        Assert.That((none.Fog, none.FogColor, none.FogNear, none.FogFar), Is.EqualTo((false, 0u, 1000f, 10000f)));
        Assert.That(FieldFog.ParseColor("1,2"), Is.Zero);
    }

    /// <summary>Equal height fog heights (a step, CalculateHeightFog's divide) become a 0.001 range above lower; others stay.</summary>
    [Test]
    public void EqualHeightFogHeightsBlendAsAStep()
    {
        Assert.That(FieldFog.BlendRange(0, 0), Is.EqualTo((0.001f, 0f)));
        Assert.That(FieldFog.BlendRange(800, -200), Is.EqualTo((800f, -200f)));
    }

    /// <summary>A key set before the pane's movie loads is the one its replay shows: the client's pane is loaded before the
    /// telescope sets its key cap (OnLoadComplete then FUN_141907d60).</summary>
    [Test]
    public void TheReplayShowsTheKeySetBeforeTheLoad()
    {
        var cine = new CutSceneCinematic();
        cine.SetSkip(3, "cap", "desc");
        var pane = new FakePane();
        cine.Pane = pane;
        cine.Replay();
        Assert.That(pane.Calls, Is.EqualTo(new[] { "setSkip True cap desc True" }));
    }

    /// <summary>The pane is wanted on entering the cinematic sub scene with no cinematic packet yet (CUIService FUN_141add040(0x4e52),
    /// uiservice.cpp:846): the telescope's view is the first cinematic of a session on a field without a cut scene.</summary>
    [Test]
    public void TheCinematicSubSceneWantsThePaneWithoutAPacket()
    {
        var cine = new CutSceneCinematic();
        Assert.That(cine.Wanted, Is.False);
        cine.SetSubScene(CutSceneCinematic.CinematicSubScene);
        Assert.That((cine.Packets, cine.KeyMode, cine.Wanted), Is.EqualTo((0, true, true)));
        cine.SetSubScene(0x620001d);
        Assert.That((cine.KeyMode, cine.Wanted), Is.EqualTo((false, false)));
    }
}

/// <summary>Every map's MS2Telescope entities on the data: each with cameras registers as a telescope interact object. Its
/// CameraIDs are listed with the ones that are not a trigger camera object of the same map: the data has some (KMS2: camera 601
/// of a 02000115_bf telescope, 601 and 602 of the two on 99999932), and CTriggerService::RunNextCameraStep does nothing for an
/// id the field does not hold (triggerservice.cpp:819-831).</summary>
public class TelescopeDataTests
{
    [Test]
    public void EveryTelescopesCamerasAreItsMapsTriggerCameras()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        using var packs = new Ms2.Formats.Pack.PackSet(d);
        var flats = new FlatLibrary(packs);
        var table = InteractObjectTable.Shared(packs);
        int maps = 0, telescopes = 0, withCameras = 0, cameras = 0;
        var missing = new List<string>();
        var flatHeightFog = new List<string>();
        foreach (var entry in packs.List(XBlock.Archive).Where(n => n.StartsWith("xblock/") && n.EndsWith(".xblock")).OrderBy(n => n, StringComparer.Ordinal))
        {
            string name = entry["xblock/".Length..^".xblock".Length];
            FieldMap map;
            try { map = FieldMap.Load(packs, flats, name); } catch (Exception) { continue; }
            var live = new LiveInteractObjects(new LiveInteractObjectsTests.Host(), table);
            live.Attach(map.Entities);
            if (live.Telescopes.Count == 0) continue;
            maps++;
            var triggerCameras = new HashSet<int>(map.Entities
                .Where(e => TriggerObjects.KindOf(e.HasMixin) == TriggerObjectKind.Camera)
                .Select(e => (int)e.Number("TriggerObjectID")));
            foreach (var t in live.Telescopes)
            {
                telescopes++;
                var ids = LiveInteractObjects.TelescopeCameraIds(t);
                var o = live.ByName(t.Raw.Id.ToLowerInvariant());
                TestContext.Out.WriteLine($"{name} {t.Raw.Id} row {(int)t.Number("interactID")} at {t.Position} cameras [{string.Join(",", ids)}] " +
                                          $"fog {t.Bool("EnableFog")} {t.Number("FogNear")}-{t.Number("FogFar")} hfog {t.Bool("EnableHeightFog")} object {(o == null ? "none" : $"kind {o.Kind}")}");
                if (t.Bool("EnableHeightFog") && t.Number("HeightFogUpperZ") == t.Number("HeightFogLowerZ"))
                    flatHeightFog.Add($"{name} {t.Raw.Id} upper {t.Number("HeightFogUpperZ")} lower {t.Number("HeightFogLowerZ")}");
                if (ids.Count == 0) continue;
                withCameras++;
                Assert.That(o, Is.Not.Null, $"{name} {t.Raw.Id}: a telescope with cameras registers");
                Assert.That(o.Kind, Is.EqualTo(InteractObjectMessage.KindTelescope), $"{name} {t.Raw.Id}");
                foreach (int id in ids)
                {
                    cameras++;
                    if (!triggerCameras.Contains(id)) missing.Add($"{name} {t.Raw.Id} camera {id}");
                }
            }
        }
        TestContext.Out.WriteLine($"{maps} maps with telescopes, {telescopes} telescopes, {withCameras} with cameras, {cameras} camera ids, {missing.Count} not a trigger camera of the map");
        foreach (var m in missing) TestContext.Out.WriteLine("  missing: " + m);
        foreach (var m in flatHeightFog) TestContext.Out.WriteLine("  height fog with upper == lower: " + m);
        Assert.That(withCameras, Is.GreaterThan(0));
        Assert.That(missing.Count, Is.LessThan(cameras / 50), "only a few cameras are not the map's");
    }
}

/// <summary>Every mapxblock's fog and height fog elements on the data, as the field load and CFieldDataCli::LoadXBlock
/// (fielddatacli.cpp:389-430) read them: each fog has a three-part colour, near and far; each height fog a colour, upper,
/// lower and percentage, and its upper differs from its lower (the height blend divides by upper - lower).</summary>
public class FieldFogDataTests
{
    [Test]
    public void EveryMapxblockFogIsComplete()
    {
        var d = Environment.GetEnvironmentVariable("MS2_DATA");
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore("MS2_DATA is not set");
        using var packs = new Ms2.Formats.Pack.PackSet(d);
        int blocks = 0, fogs = 0, hfogs = 0;
        var bad = new List<string>();
        foreach (var entry in packs.List(MapXBlock.Archive).Where(n => n.StartsWith("mapxblock/") && n.EndsWith(".xml")).OrderBy(n => n, StringComparer.Ordinal))
        {
            var x = MapXBlock.Parse(packs.Read(MapXBlock.Archive, entry));
            blocks++;
            if (x.Root?.Element("fog") is { } f)
            {
                fogs++;
                if (((string)f.Attribute("color"))?.Split(',').Length != 3 || f.Attribute("near") == null || f.Attribute("far") == null)
                    bad.Add($"{entry} fog {f}");
            }
            if (x.Root?.Element("heightfog") is { } h)
            {
                hfogs++;
                if (((string)h.Attribute("color"))?.Split(',').Length != 3 || h.Attribute("upper") == null || h.Attribute("lower") == null
                    || h.Attribute("percentage") == null || (string)h.Attribute("upper") == (string)h.Attribute("lower"))
                    bad.Add($"{entry} heightfog {h}");
            }
        }
        TestContext.Out.WriteLine($"{blocks} mapxblocks, {fogs} fog, {hfogs} heightfog, {bad.Count} incomplete");
        foreach (var b in bad) TestContext.Out.WriteLine("  " + b);
        Assert.That(fogs, Is.GreaterThan(0));
        Assert.That(bad, Is.Empty);
    }

    /// <summary>Every mapxblock on both data sets, by the depth fog the field load gives it: off when the block has no fog
    /// element (FieldFog.Fog false, so GraphicGates.IsFogEnabled false and ApplyFieldFog's SetFogRange(false)), else on with
    /// the block's range, counted by its form and the range the shaders get for it (CameraFrustum.ShaderFogRange under the
    /// field camera: the block's when far is above near; near to the camera's far when near equals far; a far end past 1e9,
    /// next to no fog, when near is above far), which ms2_fog.gdshaderinc draws as the client's vertex fog
    /// (res://tests/fog_share.tscn).</summary>
    [TestCase("MS2_DATA")]
    [TestCase("MS2_GMS2_DATA")]
    public void EveryMapxblockDepthFogSwitch(string env)
    {
        var d = Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) Assert.Ignore($"{env} is not set");
        using var packs = new Ms2.Formats.Pack.PackSet(d);
        var count = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var example = new Dictionary<string, string>();
        var bad = new List<string>();
        foreach (var entry in packs.List(MapXBlock.Archive).Where(n => n.StartsWith("mapxblock/") && n.EndsWith(".xml")).OrderBy(n => n, StringComparer.Ordinal))
        {
            var x = MapXBlock.Parse(packs.Read(MapXBlock.Archive, entry));
            var ff = FieldFog.Parse(x);
            bool on = Ms2.Formats.Options.GraphicGates.IsFogEnabled(ff.Fog);
            if (on != (x.Root?.Element("fog") != null)) bad.Add($"{entry}: on {on}");
            // the range the shaders get (CameraFrustum.ShaderFogRange): the block's when far is above near, else rebuilt
            var (sn, sf) = Ms2.Formats.World.CameraFrustum.GameplayDefault.ShaderFogRange(ff.FogNear, ff.FogFar);
            string k = !on ? "off (no fog element)"
                : ff.FogFar > ff.FogNear ? "on, far above near (shaders get the block's range)"
                : ff.FogFar == ff.FogNear ? "on, near equal to far (shaders fog from near to the camera's far)"
                : "on, near above far (shaders' far end past 1e9: next to no fog)";
            if (on && ff.FogFar > ff.FogNear && ff.FogNear < 100000 && (MathF.Abs(sn - ff.FogNear) > 1 || MathF.Abs(sf - ff.FogFar) > 1))
                bad.Add($"{entry}: {ff.FogNear} / {ff.FogFar} becomes {sn} / {sf}");
            if (on && ff.FogFar == ff.FogNear && (sn != ff.FogNear || sf != 100000)) bad.Add($"{entry}: {ff.FogNear} / {ff.FogFar} becomes {sn} / {sf}");
            if (on && ff.FogFar < ff.FogNear && sf < 1e9f) bad.Add($"{entry}: {ff.FogNear} / {ff.FogFar} becomes {sn} / {sf}");
            count[k] = count.GetValueOrDefault(k) + 1;
            if (example.TryGetValue(k, out var e)) { if (k.Contains("near equal") || k.Contains("near above")) example[k] = e + " " + entry; }
            else example[k] = entry;
        }
        TestContext.Out.WriteLine($"{env}:");
        foreach (var (k, c) in count) TestContext.Out.WriteLine($"  {k}: {c} (e.g. {example[k]})");
        Assert.That(bad, Is.Empty);
        Assert.That(count.GetValueOrDefault("off (no fog element)"), Is.GreaterThan(100));
        Assert.That(count.GetValueOrDefault("on, far above near (shaders get the block's range)"), Is.GreaterThan(100));
    }
}
