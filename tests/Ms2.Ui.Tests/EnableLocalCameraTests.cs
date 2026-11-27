using System.Numerics;
using System.Xml.Linq;
using Maple2.PacketLib.Tools;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Formats.World;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>
/// The trigger's EnableLocalCamera: the Maple2 server adds field property 10 with the flag. The client's apply sets the
/// camera's local cameras flag to it and its unapply sets it true (FieldPropertyEnableLocalCameraCli vfunc_4 @0x140b27ca0 and
/// vfunc_5 @0x140b27cd0, fieldpropertycli.cpp:243-245, into CameraMgr::FUN_140aeb900, camera.cpp:1003); with it off the auto
/// check enters no setup (FUN_140aece50, camera.cpp:1199) and a local camera mode moves back (CLocalTargetCameraMode::vfunc_8,
/// localtargetcameramode.cpp:161-164).
/// </summary>
public class EnableLocalCameraTests
{
    sealed class Seen
    {
        public readonly List<bool> Values = new();
        public Seen(FieldProperties f) => f.LocalCamerasChanged += on => Values.Add(on);
    }

    /// <summary>A player whose local cameras flag is the field properties' one, as the live session hands it on.</summary>
    sealed class Target(FieldProperties f, Vector3 at) : ILocalCameraTarget
    {
        public Vector3? Position { get; set; } = at;
        public bool IsPc => true;
        public Vector3 Rotation { get; set; }
        public bool LocalCamerasOn => f.LocalCamerasOn;
        public bool BoxContains(int boxId, Vector3 p) => true;   // an Operate 3 setup's player stands in its box
    }

    static void Recv(FieldProperties f, string hex, ProtocolProfile profile) =>
        f.Apply(FieldPropertyMessage.Read(new ByteReader(Convert.FromHexString(hex)), profile));

    static readonly ProtocolProfile[] Profiles = { ProtocolProfile.Kms2525, ProtocolProfile.Gms2 };

    [Test]
    public void KindTenSetsTheLocalCamerasUntilItsRemoveOrAFieldLeave()
    {
        foreach (var profile in Profiles)
        {
            var f = new FieldProperties();
            var seen = new Seen(f);
            Assert.That(f.LocalCamerasOn, Is.True, profile.Name);              // CameraMgr(): m_24c = 1 (camera.cpp:755)
            Recv(f, "020A", profile);                                          // not registered: RemoveFieldProperty returns before vfunc_5
            Assert.That((f.LocalCamerasOn, seen.Values.Count), Is.EqualTo((true, 0)), profile.Name);
            Recv(f, "010A00", profile);
            Assert.That(f.LocalCamerasOn, Is.False, profile.Name);
            Recv(f, "010A01", profile);                                        // a second add applies its own value
            Assert.That(f.LocalCamerasOn, Is.True, profile.Name);
            Recv(f, "010A00", profile);
            Recv(f, "020A", profile);                                          // the remove's vfunc_5 turns them on
            Assert.That((f.LocalCamerasOn, f.Count), Is.EqualTo((true, 0)), profile.Name);
            Recv(f, "00010000000A00", profile);                                  // the field's list on entry: kind 10 off
            Assert.That(f.LocalCamerasOn, Is.False, profile.Name);
            f.Clear();                                                         // the leave: vfunc_5 and the camera's release
            Assert.That((f.LocalCamerasOn, f.Count), Is.EqualTo((true, 0)), profile.Name);
            Assert.That(seen.Values, Is.EqualTo(new[] { false, true, false, true, false, true }), profile.Name);
        }
    }

    [Test]
    public void OtherKindsLeaveTheLocalCamerasOn()
    {
        var f = new FieldProperties();
        var seen = new Seen(f);
        f.Apply(new FieldPropertyMessage.Add(new FieldProperty.HideMyPC()));
        f.Apply(new FieldPropertyMessage.Add(new FieldProperty.Weather(1)));
        f.Apply(new FieldPropertyMessage.Remove(3));
        f.Clear();
        Assert.That((f.LocalCamerasOn, seen.Values.Count), Is.EqualTo((true, 0)));
    }

    [Test]
    public void AHeldLocalModeMovesBackOnceTheyAreTurnedOff()
    {
        var f = new FieldProperties();
        var s = new LocalCameraSetup
        {
            Setting = new LocalCameraSetting(45, 100, 9000, new Vector3(1000, 2000, 300), new Vector3(0, 20, 135)),
            Operate = 2, LocalCameraId = 1, TargetDistance = 600, InterpolationTime = 0.5f, StartInterpolator = 1, EndInterpolator = 1,
        };
        var t = new Target(f, s.Setting.Position);
        var cam = Hold(s, t);
        Assert.That(cam.Steady, Is.True);
        Recv(f, "010A00", ProtocolProfile.Kms2525);
        Run(cam, t, 0.6f);
        Assert.That(cam.PendingMode, Is.EqualTo(2), "the move back ends in the follow mode");
    }

    static LocalTargetCamera Hold(in LocalCameraSetup s, Target t)
    {
        var c = t.Position!.Value;
        var eye = c + new Vector3(0, 800, 400);
        var (rx, ry, rz) = CameraMath.Euler(eye.X, eye.Y, eye.Z, c.X, c.Y, c.Z);
        var rot = new Vector3(rx, ry, rz);
        var cam = new LocalTargetCamera(new LocalCameraPrev
        {
            Type = 2, Yaw = 0, Pitch = 1, Distance = 900, Setting = new LocalCameraSetting(34, 250, 18000, eye, rot),
            Center = c, Shown = new CameraPose(eye, rot), Placed = new CameraPose(eye, rot),
            LooseFinal = 2, LooseNow = 2, LooseUpdate = 1, Nearest = 150, Farthest = 1000,
        });
        cam.Start(s, t);
        Run(cam, t, s.InterpolationTime + 0.05f);
        cam.Frame(1 / 60f, 0, t);
        return cam;
    }

    static void Run(LocalTargetCamera cam, Target t, float seconds, float dt = 1 / 60f)
    {
        for (float x = 0; x < seconds && cam.PendingMode == 0; x += dt) cam.Frame(dt, 0, t);
    }

    static string Attr(XElement e, string name) =>
        (string)e.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? "";

    /// <summary>
    /// Every EnableLocalCamera of the trigger xmls: its isEnable decodes as the Maple2 server's bool, the add of kind 10 it
    /// sends reads on both protocols, and applied in the script's order from both starting states it leaves the local cameras
    /// on exactly when the use turns them on; the field leave turns them on again. On a field with local camera setups each
    /// of its setups held (the player at an auto setup's position or in its box) stays held while on and moves back while off.
    /// </summary>
    static void EveryUse(string variable)
    {
        using var packs = new PackSet(RcGap.Data(variable));
        var flats = new FlatLibrary(packs);
        var xblocks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var x in packs.List(XBlock.Archive))
            if (x.StartsWith("xblock/", StringComparison.OrdinalIgnoreCase) && x.EndsWith(".xblock", StringComparison.OrdinalIgnoreCase))
                xblocks.Add(Path.GetFileNameWithoutExtension(x));

        int uses = 0, offs = 0, ons = 0, files = 0, held = 0, withSetups = 0;
        var shapes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var fields = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in packs.List("Xml").Where(e => e.StartsWith("trigger/", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e, StringComparer.Ordinal))
        {
            var bytes = packs.Read("Xml", entry);
            if (bytes.AsSpan().IndexOf("EnableLocalCamera"u8) < 0) continue;
            XDocument doc;
            try { doc = PackXml.Parse(bytes); } catch (System.Xml.XmlException) { continue; }
            var acts = doc.Descendants("action").Where(a => Attr(a, "name").Equals("EnableLocalCamera", StringComparison.OrdinalIgnoreCase)).ToList();
            if (acts.Count == 0) continue;
            files++;
            string field = entry.Split('/')[1];
            fields.Add(field);
            var setups = xblocks.Contains(field) ? LocalCameraSetup.All(FieldMap.Load(packs, flats, field).Entities) : new List<LocalCameraSetup>();
            if (setups.Count > 0) withSetups++;
            foreach (var profile in Profiles)
                foreach (bool start in new[] { false, true })
                {
                    var f = new FieldProperties();
                    if (!start) Recv(f, "010A00", profile);
                    foreach (var a in acts)
                    {
                        string raw = Attr(a, "isEnable").Trim();
                        Assert.That(raw is "0" or "1" || bool.TryParse(raw, out _), Is.True, $"{entry}: isEnable '{raw}'");
                        bool on = raw == "1" || raw.Equals("true", StringComparison.OrdinalIgnoreCase);
                        Recv(f, on ? "010A01" : "010A00", profile);
                        Assert.That(f.LocalCamerasOn, Is.EqualTo(on), $"{entry} {profile.Name}: isEnable '{raw}'");
                        if (profile == ProtocolProfile.Kms2525 && !start)
                        {
                            uses++;
                            if (on) ons++; else offs++;
                            shapes[raw] = shapes.GetValueOrDefault(raw) + 1;
                            foreach (var s in setups.Where(s => s.Operate is 1 or 2 or 3))
                            {
                                var t = new Target(f, s.Setting.Position);
                                Recv(f, "010A01", profile);
                                var cam = Hold(s, t);
                                Assert.That(cam.Steady, Is.True, $"{entry} {s.LocalCameraId}");
                                Recv(f, on ? "010A01" : "010A00", profile);
                                Run(cam, t, s.InterpolationTime + 0.1f);
                                Assert.That(cam.PendingMode, Is.EqualTo(on ? 0 : 2), $"{entry} {s.LocalCameraId} isEnable '{raw}'");
                                held++;
                            }
                        }
                    }
                    f.Clear();
                    Assert.That(f.LocalCamerasOn, Is.True, entry);
                }
        }
        TestContext.Progress.WriteLine($"{variable}: {uses} EnableLocalCamera uses in {files} files of {fields.Count} fields ({string.Join(", ", fields)}), "
            + $"{offs} off, {ons} on; isEnable {string.Join(", ", shapes.Select(kv => $"'{kv.Key}' {kv.Value}"))}; "
            + $"{withSetups} files on a field with local cameras, {held} setups held through a use");
        Assert.That(uses, Is.GreaterThan(10));
        Assert.That(offs, Is.GreaterThan(0));
        Assert.That(ons, Is.GreaterThan(0));
        Assert.That(held, Is.GreaterThan(0));
    }

    [Test] public void EveryKmsUse() => EveryUse("MS2_DATA");
    [Test] public void EveryGms2Use() => EveryUse("MS2_GMS2_DATA");
}
