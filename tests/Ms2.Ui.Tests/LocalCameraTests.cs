using System.Numerics;
using System.Xml.Linq;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Formats.World;
using Ms2.Net;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The local camera mode (0x10, CLocalTargetCameraMode, localtargetcameramode.cpp) over a field's local camera setups
/// (WorldService::FUN_141bbd640, worldservice_1.cpp:10223-10253): the trigger's SetLocalCamera finds the first Operate 2
/// setup of its id (CameraMgr::FUN_140aecbb0, camera.cpp:1143-1194), the auto check the first Operate 1 or 3 setup the
/// player stands in (FUN_140aece50, :1197-1238), and the mode moves to the setup's view, holds it and moves back on an abort.
/// </summary>
public class LocalCameraTests
{
    sealed class Target : ILocalCameraTarget
    {
        public Vector3? Position { get; set; } = new Vector3(1000, 2000, 300);
        public bool IsPc { get; set; } = true;
        public Vector3 Rotation { get; set; } = new Vector3(0, 0, 90);
        public bool LocalCamerasOn { get; set; } = true;
        public Func<int, Vector3, bool> Box = (_, _) => false;
        public bool BoxContains(int boxId, Vector3 p) => Box(boxId, p);
    }

    const float Yaw = 0.6f, Pitch = 1.0f, Dist = 800f;

    /// <summary>A follow camera around the target with no look height (zoom heights 0).</summary>
    static LocalCameraPrev Prev(Vector3 center)
    {
        var eye = center + new Vector3(Dist * MathF.Sin(Yaw), Dist * MathF.Cos(Yaw), Dist * MathF.Cos(Pitch));
        var (rx, ry, rz) = CameraMath.Euler(eye.X, eye.Y, eye.Z, center.X, center.Y, center.Z);
        var rot = new Vector3(rx, ry, rz);
        return new LocalCameraPrev
        {
            Type = 2, Yaw = Yaw, Pitch = Pitch, Distance = Dist,
            Setting = new LocalCameraSetting(34, 250, 18000, eye, rot),
            Center = center, Shown = new CameraPose(eye, rot), Placed = new CameraPose(eye, rot),
            LooseFinal = 2, LooseNow = 2, LooseUpdate = 1, Nearest = 150, Farthest = 1000,
        };
    }

    static LocalCameraSetup Setup(int operate = 2, int id = 8002) => new()
    {
        Setting = new LocalCameraSetting(45, 100, 9000, new Vector3(1000, 2000, 300), new Vector3(0, 20, 135)),
        Operate = operate, LocalCameraId = id, Scope = 500, BoxId = 7,
        TargetDistance = 600, InterpolationTime = 1f, OffsetPosition = new Vector3(0, 0, 50),
        CharacterAngle = new Vector3(0, 0, 200), ChangeCharacterAngle = true, CharacterMovable = false,
        StartInterpolator = 1, EndInterpolator = 1, ZoomMin = -2, ZoomMax = 3, ZoomValue = 100,
    };

    static Vector3 Dir(Vector3 rot)
    {
        Span<double> m = stackalloc double[9];
        CameraMath.FromEuler(rot.X, rot.Y, rot.Z, m);
        return new Vector3((float)m[0], (float)m[3], (float)m[6]);
    }

    static void Near(Vector3 a, Vector3 b, string what, float tol = 0.05f) =>
        Assert.That((a - b).Length(), Is.LessThan(tol), $"{what}: {a} vs {b}");

    static void Run(LocalTargetCamera cam, Target t, float seconds, float dt = 1 / 60f, int zoom = 0)
    {
        for (float s = 0; s < seconds && cam.PendingMode == 0; s += dt) cam.Frame(dt, zoom, t);
    }

    [Test]
    public void NormalizeDegreesAndEase()
    {
        Assert.That(new[] { LocalCameraSetup.NormalizeDegrees(-90), LocalCameraSetup.NormalizeDegrees(725.7f), LocalCameraSetup.NormalizeDegrees(360) },
            Is.EqualTo(new[] { 270f, 5f, 0f }));
        Assert.That(LocalTargetCamera.Ease(0.25f, 1), Is.EqualTo(0.25f));
        Assert.That(LocalTargetCamera.Ease(0.5f, 2), Is.EqualTo(MathF.Log(6f) / 2.397895f).Within(1e-6));
        Assert.That(LocalTargetCamera.Ease(0.5f, 3), Is.EqualTo((MathF.Exp(2.5f) - 1f) / 147.41316f).Within(1e-6));
        Assert.That(new[] { LocalTargetCamera.Ease(1, 2), LocalTargetCamera.Ease(1, 3), LocalTargetCamera.Ease(2, 0) }, Has.All.EqualTo(1f).Within(1e-5));
    }

    [Test]
    public void BoxContainsTurnsTheBoxAboutZ()
    {
        var c = new Vector3(100, 100, 0); var size = new Vector3(400, 100, 200);
        Assert.That(LocalCameraSetup.BoxContains(c, size, 0, c + new Vector3(190, 0, 0)), Is.True);
        Assert.That(LocalCameraSetup.BoxContains(c, size, 0, c + new Vector3(0, 60, 0)), Is.False);
        Assert.That(LocalCameraSetup.BoxContains(c, size, 90, c + new Vector3(190, 0, 0)), Is.False);
        Assert.That(LocalCameraSetup.BoxContains(c, size, 90, c + new Vector3(0, 190, 0)), Is.True);
        Assert.That(LocalCameraSetup.BoxContains(c, size, 0, c + new Vector3(0, 0, 101)), Is.False);
    }

    [Test]
    public void TheTriggerFindsTheFirstOperateTwoSetupOfItsId()
    {
        var a = Setup(1, 5); var b = Setup(2, 5); b.TargetDistance = 1; var b2 = Setup(2, 5); b2.TargetDistance = 2;
        var list = new List<LocalCameraSetup> { a, b, b2, Setup(2, 0) };
        Assert.That(LocalCameraSetup.FindTrigger(list, 5)?.TargetDistance, Is.EqualTo(1f));
        Assert.That(LocalCameraSetup.FindTrigger(list, 0), Is.Null);
        Assert.That(LocalCameraSetup.FindTrigger(list, 6), Is.Null);
        Assert.That(LocalCameraSetup.FindTrigger(new List<LocalCameraSetup> { a }, 5), Is.Null);
    }

    [Test]
    public void TheAutoCheckFindsTheFirstSetupTheTargetStandsIn()
    {
        var near = Setup(1); var box = Setup(3); box.BoxId = 9; var trig = Setup(2);
        var list = new List<LocalCameraSetup> { trig, box, near };
        var at = near.Setting.Position;
        Assert.That(LocalCameraSetup.FindAuto(list, at + new Vector3(500, 0, 0), (_, _) => false)?.Operate, Is.EqualTo(1));
        Assert.That(LocalCameraSetup.FindAuto(list, at + new Vector3(501, 0, 0), (_, _) => false), Is.Null);
        Assert.That(LocalCameraSetup.FindAuto(list, at, (id, _) => id == 9)?.Operate, Is.EqualTo(3));
        Assert.That(LocalCameraSetup.FindAuto(new List<LocalCameraSetup> { trig }, trig.Setting.Position, (_, _) => true), Is.Null);
    }

    /// <summary>vfunc_8's move in (:265-320): the camera eases from the follow camera to -dir * TargetDistance + the look
    /// point + OffsetPosition, the player turns to CharacterAngle (x, y kept), then the steady view takes the zoom steps.</summary>
    [Test]
    public void TheMoveInEndsAtTheSetupsViewAndTheZoomStepsClamp()
    {
        var t = new Target();
        var cam = new LocalTargetCamera(Prev(t.Position!.Value));
        var s = Setup();
        cam.Start(s, t);
        cam.Frame(1 / 60f, 0, t);
        Assert.That(cam.Steady, Is.False);
        Assert.That((cam.Placed.Position - Prev(t.Position.Value).Setting.Position).Length(), Is.LessThan(60f), "the first frame starts at the follow camera");
        Run(cam, t, 1.02f);
        var c = t.Position.Value + s.OffsetPosition;
        Assert.That(cam.Steady, Is.True);
        Near(cam.Placed.Position, c - Dir(s.Setting.Rotation) * 600, "steady");
        Assert.That(cam.Placed.Rotation, Is.EqualTo(s.Setting.Rotation));
        Assert.That(cam.Fov, Is.EqualTo(45f));
        Assert.That(t.Rotation, Is.EqualTo(new Vector3(0, 0, 200)));
        Run(cam, t, 5f);
        Near(cam.Shown.Position, cam.Placed.Position, "the shown camera catches up", 1f);
        Run(cam, t, 0.2f, zoom: -1);
        Assert.That(cam.ZoomStep, Is.EqualTo(3));
        Near(cam.Placed.Position, c - Dir(s.Setting.Rotation) * 900, "zoomed out to the max");
        Run(cam, t, 0.2f, zoom: 1);
        Assert.That(cam.ZoomStep, Is.EqualTo(-2));
        Near(cam.Placed.Position, c - Dir(s.Setting.Rotation) * 400, "zoomed in to the min");
        Assert.That(cam.PendingMode, Is.Zero);
    }

    /// <summary>Start (:352-372): the shorter way round goes through 360.</summary>
    [Test]
    public void ThePlayerTurnsTheShortWayRound()
    {
        var t = new Target { Rotation = new Vector3(3, 4, 350) };
        var s = Setup(); s.CharacterAngle = new Vector3(0, 0, 10);
        var cam = new LocalTargetCamera(Prev(t.Position!.Value));
        cam.Start(s, t);
        cam.Frame(0.5f, 0, t);
        Assert.That(t.Rotation.Z, Is.EqualTo(360f).Within(0.01f));
        Assert.That((t.Rotation.X, t.Rotation.Y), Is.EqualTo((3f, 4f)));
        Run(cam, t, 0.6f);
        Assert.That(t.Rotation, Is.EqualTo(new Vector3(3, 4, 370)));
        var u = new Target { Rotation = new Vector3(0, 0, 10) };
        s.CharacterAngle = new Vector3(0, 0, 350);
        cam = new LocalTargetCamera(Prev(u.Position!.Value));
        cam.Start(s, u);
        cam.Frame(0.5f, 0, u);
        Assert.That(u.Rotation.Z, Is.EqualTo(360f).Within(0.01f));
    }

    /// <summary>The abort (FUN_140d4f830, :376-397) and the move back (:189-262): it takes at most the move in's time, ends
    /// at the follow camera's yaw, pitch and distance about the look point, and sets the previous mode (2) pending with
    /// its setting.</summary>
    [Test]
    public void TurningTheLocalCamerasOffMovesBackAndSetsTheFollowModePending()
    {
        var t = new Target();
        var cam = new LocalTargetCamera(Prev(t.Position!.Value));
        cam.Start(Setup(), t);
        Run(cam, t, 0.5f);
        t.LocalCamerasOn = false;
        cam.Frame(1 / 60f, 0, t);
        Assert.That(cam.Returning, Is.True);
        t.LocalCamerasOn = true;
        Run(cam, t, 0.45f);
        Assert.That(cam.PendingMode, Is.Zero, "the move back takes the half second the move in had");
        Run(cam, t, 0.1f);
        Assert.That(cam.PendingMode, Is.EqualTo(2));
        var c = t.Position.Value;
        var eye = c + new Vector3(Dist * MathF.Sin(Yaw), Dist * MathF.Cos(Yaw), Dist * MathF.Cos(Pitch));
        Near(cam.Placed.Position, eye, "back at the follow camera");
        Assert.That(cam.ManagerSetting?.Position, Is.EqualTo(cam.Placed.Position));
        Assert.That(cam.ManagerSetting?.Fov, Is.EqualTo(34f));
        Assert.That(cam.Fov, Is.EqualTo(34f));
    }

    [Test]
    public void AnAutoSetupAbortsOnceTheTargetLeavesItsScopeOrBox()
    {
        var t = new Target();
        var cam = new LocalTargetCamera(Prev(t.Position!.Value));
        cam.Start(Setup(1), t);
        Run(cam, t, 1.1f);
        t.Position = t.Position.Value + new Vector3(499, 0, 0);
        cam.Frame(1 / 60f, 0, t);
        Assert.That(cam.Returning, Is.False);
        t.Position = t.Position.Value + new Vector3(2, 0, 0);
        cam.Frame(1 / 60f, 0, t);
        Assert.That(cam.Returning, Is.True);

        var b = new Target { Box = (id, _) => id == 7 };
        cam = new LocalTargetCamera(Prev(b.Position!.Value));
        cam.Start(Setup(3), b);
        Run(cam, b, 1.1f);
        Assert.That(cam.Returning, Is.False);
        b.Box = (_, _) => false;
        cam.Frame(1 / 60f, 0, b);
        Assert.That(cam.Returning, Is.True);

        var trig = new Target();
        cam = new LocalTargetCamera(Prev(trig.Position!.Value));
        cam.Start(Setup(2), trig);
        Run(cam, trig, 1.1f);
        trig.Position = trig.Position.Value + new Vector3(5000, 0, 0);
        cam.Frame(1 / 60f, 0, trig);
        Assert.That(cam.Returning, Is.False, "a trigger setup stays until its leave");
    }

    static string Attr(XElement e, string name) =>
        (string)e.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? "";

    /// <summary>
    /// Every local camera of every field and every SetLocalCamera of the trigger xmls. Each setup reads; each Operate 1
    /// setup is found by the auto check at its own position and each Operate 3 one inside its trigger box; each use whose
    /// id names an Operate 2 setup of its field runs the move in to the setup's view and the move back, and each other use
    /// finds no setup (the client's walk ends with nothing to do).
    /// </summary>
    static void EveryUse(string variable)
    {
        using var packs = new PackSet(RcGap.Data(variable));
        var flats = new FlatLibrary(packs);
        var xblocks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var withCameras = new List<string>();
        foreach (var x in packs.List(XBlock.Archive))
        {
            if (!x.StartsWith("xblock/", StringComparison.OrdinalIgnoreCase) || !x.EndsWith(".xblock", StringComparison.OrdinalIgnoreCase)) continue;
            string name = Path.GetFileNameWithoutExtension(x);
            xblocks[name] = name;
            if (packs.Read(XBlock.Archive, x).AsSpan().IndexOf("MS2LocalTargetCamera"u8) >= 0) withCameras.Add(name);
        }
        var maps = new Dictionary<string, FieldMap>(StringComparer.OrdinalIgnoreCase);
        FieldMap Map(string field) => maps.TryGetValue(field, out var m) ? m
            : maps[field] = xblocks.TryGetValue(field, out var n) ? FieldMap.Load(packs, flats, n) : null;

        static Func<int, Vector3, bool> Boxes(FieldMap map)
        {
            var boxes = new Dictionary<int, FieldEntity>();
            foreach (var e in map.Entities)
                if (e.HasMixin("MS2TriggerBox")) boxes[(int)e.Number("TriggerObjectID")] = e;
            static Vector3 V(Vec3 v) => new((float)v.X, (float)v.Y, (float)v.Z);
            return (id, p) => boxes.TryGetValue(id, out var e)
                && LocalCameraSetup.BoxContains(V(e.Position), V(e.Vector("ShapeDimensions")), (float)e.RotationDeg.Z, p);
        }

        int setups = 0, autoFound = 0, boxless = 0;
        var operates = new SortedDictionary<int, int>();
        foreach (var field in withCameras)
        {
            var map = Map(field);
            var list = LocalCameraSetup.All(map.Entities);
            var box = Boxes(map);
            foreach (var s in list)
            {
                setups++;
                operates[s.Operate] = operates.GetValueOrDefault(s.Operate) + 1;
                Assert.That(s.Operate, Is.InRange(0, 3), field);
                Assert.That(s.InterpolationTime, Is.GreaterThanOrEqualTo(0f), field);
                Assert.That(s.CharacterAngle.Z, Is.InRange(0f, 359f), field);
                if (s.Operate == 1)
                {
                    Assert.That(LocalCameraSetup.FindAuto(list, s.Setting.Position, box), Is.Not.Null, $"{field} {s.LocalCameraId}");
                    autoFound++;
                }
                else if (s.Operate == 3)
                {
                    var b = map.Entities.FirstOrDefault(e => e.HasMixin("MS2TriggerBox") && (int)e.Number("TriggerObjectID") == s.BoxId);
                    if (b == null) { boxless++; continue; }
                    var p = new Vector3((float)b.Position.X, (float)b.Position.Y, (float)b.Position.Z);
                    Assert.That(LocalCameraSetup.FindAuto(list, p, box), Is.Not.Null, $"{field} box {s.BoxId}");
                    autoFound++;
                }
            }
        }

        int uses = 0, hits = 0, misses = 0, enters = 0, leaves = 0, noField = 0;
        foreach (var entry in packs.List("Xml").Where(e => e.StartsWith("trigger/", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e, StringComparer.Ordinal))
        {
            var bytes = packs.Read("Xml", entry);
            if (bytes.AsSpan().IndexOf("SetLocalCamera"u8) < 0) continue;
            XDocument doc;
            try { doc = PackXml.Parse(bytes); } catch (System.Xml.XmlException) { continue; }
            string field = entry.Split('/')[1];
            foreach (var a in doc.Descendants("action").Where(a => Attr(a, "name").Equals("SetLocalCamera", StringComparison.OrdinalIgnoreCase)))
            {
                uses++;
                Assert.That(int.TryParse(Attr(a, "cameraId"), out int id), Is.True, $"{entry}: cameraId '{Attr(a, "cameraId")}'");
                string en = Attr(a, "enable").Trim();
                bool enter = en == "1" || en.Equals("true", StringComparison.OrdinalIgnoreCase);
                if (enter) enters++; else leaves++;
                var map = Map(field);
                if (map == null) { noField++; continue; }
                var list = LocalCameraSetup.All(map.Entities);
                if (LocalCameraSetup.FindTrigger(list, id) is not { } s)
                {
                    misses++;
                    Assert.That(list.Any(x => x.Operate == 2 && x.LocalCameraId == id && id != 0), Is.False, entry);
                    continue;
                }
                hits++;
                // the player somewhere in the field: the setup's own position
                var t = new Target { Position = s.Setting.Position, Rotation = new Vector3(0, 0, 45) };
                var cam = new LocalTargetCamera(Prev(t.Position.Value));
                cam.Start(s, t);
                Run(cam, t, s.InterpolationTime + 0.05f);
                cam.Frame(1 / 60f, 0, t);
                Assert.That(cam.Steady, Is.True, $"{entry} {id}");
                var c = t.Position.Value + s.OffsetPosition;
                Near(cam.Placed.Position, c - Dir(s.Setting.Rotation) * s.TargetDistance, $"{entry} {id}", 0.5f);
                Assert.That(cam.Fov, Is.EqualTo(s.Setting.Fov), $"{entry} {id}");
                if (s.ChangeCharacterAngle) Assert.That(t.Rotation.Z % 360f, Is.EqualTo(s.CharacterAngle.Z % 360f).Within(0.01f), $"{entry} {id}");
                else Assert.That(t.Rotation, Is.EqualTo(new Vector3(0, 0, 45)), $"{entry} {id}");
                t.LocalCamerasOn = false;
                Run(cam, t, s.InterpolationTime + 0.1f);
                Assert.That(cam.PendingMode, Is.EqualTo(2), $"{entry} {id}");
            }
        }
        TestContext.Progress.WriteLine($"{variable}: {withCameras.Count} fields with {setups} local cameras (Operate {string.Join(", ", operates.Select(kv => $"{kv.Key}: {kv.Value}"))}), "
            + $"{autoFound} found by the auto check, {boxless} Operate 3 without their box; {uses} SetLocalCamera uses ({enters} enter, {leaves} leave): "
            + $"{hits} name an Operate 2 setup, {misses} none, {noField} in a field without an xblock");
        Assert.That(setups, Is.GreaterThan(100));
        Assert.That(hits, Is.GreaterThan(50));
        Assert.That(operates.GetValueOrDefault(1), Is.GreaterThan(0));
        Assert.That(operates.GetValueOrDefault(3), Is.GreaterThan(0));
    }

    [Test] public void EveryKmsUse() => EveryUse("MS2_DATA");
    [Test] public void EveryGms2Use() => EveryUse("MS2_GMS2_DATA");
}
