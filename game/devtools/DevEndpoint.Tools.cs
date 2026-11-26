#if MS2_DEVTOOLS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;
using Ms2.Devtools;
using Ms2Godot.Ui;

namespace Ms2Godot;

public partial class DevEndpoint
{
    // ---- pick -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// pick {x, y}: the nodes under a viewport point, front to back. 3D: a ray from the camera against the world box of every
    /// visible VisualInstance3D (an effect with no collider is found too; a box hit is a candidate, not a pixel test).
    /// UI: Controls whose rect holds the point (topmost first), and the movie objects under it for each As3View.
    /// </summary>
    JsonNode Pick(Request r)
    {
        var p = new Vector2((float)r.GetDouble("x"), (float)r.GetDouble("y"));
        var vp = GetViewport();
        var res = new JsonObject { ["point"] = Struct(p) };

        var hits3 = new JsonArray();
        if (vp.GetCamera3D() is { } cam)
        {
            var origin = cam.ProjectRayOrigin(p);
            var dir = cam.ProjectRayNormal(p);
            var found = new List<(float t, VisualInstance3D n)>();
            void Visit(Node n)
            {
                if (n is VisualInstance3D v && v.IsVisibleInTree() && RayBox(origin, dir, v, out float t)) found.Add((t, v));
                for (int i = 0, c = n.GetChildCount(); i < c; i++) Visit(n.GetChild(i));
            }
            Visit(GetTree().Root);
            foreach (var (t, n) in found.OrderBy(f => f.t).Take(40))
                hits3.Add(new JsonObject { ["path"] = n.GetPath().ToString(), ["type"] = n.GetClass(), ["distance"] = t });
        }
        res["nodes3d"] = hits3;

        var controls = new List<Control>();
        var views = new List<As3View>();
        void Walk(Node n)
        {
            if (n is Control c && c.IsVisibleInTree() && c.MouseFilter != Control.MouseFilterEnum.Ignore && c.GetGlobalRect().HasPoint(p)) controls.Add(c);
            if (n is As3View v && v.IsVisibleInTree()) views.Add(v);
            for (int i = 0, k = n.GetChildCount(); i < k; i++) Walk(n.GetChild(i));
        }
        Walk(GetTree().Root);
        var ui = new JsonArray();
        for (int i = controls.Count - 1; i >= 0 && ui.Count < 40; i--)
            ui.Add(new JsonObject { ["path"] = controls[i].GetPath().ToString(), ["type"] = controls[i].GetClass() });
        res["controls"] = ui;

        var movies = new JsonArray();
        foreach (var v in views)
        {
            try
            {
                var local = v.GetGlobalTransformWithCanvas().AffineInverse() * p;
                var top = v.Player.TopMost(local.X, local.Y, false);
                if (top == null) continue;
                movies.Add(new JsonObject { ["view"] = v.GetPath().ToString(), ["movie"] = v.Player.Main?.Key, ["object"] = As3Path(top), ["class"] = top.Class?.Name ?? top.GetType().Name });
            }
            catch (Exception e) { movies.Add(new JsonObject { ["view"] = v.GetPath().ToString(), ["error"] = e.GetType().Name }); }
        }
        res["movies"] = movies;
        return res;
    }

    static bool RayBox(Vector3 origin, Vector3 dir, VisualInstance3D v, out float t)
    {
        t = 0;
        var box = v.GetAabb();
        if (box.Size == Vector3.Zero) return false;
        var inv = v.GlobalTransform.AffineInverse();
        var o = inv * origin;
        var d = inv.Basis * dir;
        float tmin = 0, tmax = float.PositiveInfinity;
        for (int a = 0; a < 3; a++)
        {
            float lo = box.Position[a], hi = lo + box.Size[a];
            if (Mathf.Abs(d[a]) < 1e-9f) { if (o[a] < lo || o[a] > hi) return false; continue; }
            float t1 = (lo - o[a]) / d[a], t2 = (hi - o[a]) / d[a];
            if (t1 > t2) (t1, t2) = (t2, t1);
            tmin = Mathf.Max(tmin, t1); tmax = Mathf.Min(tmax, t2);
            if (tmin > tmax) return false;
        }
        t = tmin;
        return true;
    }

    // ---- screenshot -----------------------------------------------------------------------------------------------

    /// <summary>screenshot {file}: the viewport as a PNG named <c>file</c> (a plain name) in the directory of env MS2_DEBUG_OUT (default: the user dir).</summary>
    JsonNode Screenshot(Request r)
    {
        string name = Path.GetFileName(r.GetString("file") ?? $"shot-{Engine.GetProcessFrames()}.png");
        if (name.Length == 0) name = "shot.png";
        if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) name += ".png";
        string dir = OS.GetEnvironment(EnvOut);
        if (string.IsNullOrEmpty(dir)) dir = ProjectSettings.GlobalizePath("user://");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
        if (new FileInfo(path).LinkTarget != null) throw new IOException("refusing to write through a symbolic link: " + name);
        var img = GetViewport().GetTexture()?.GetImage();
        if (img == null || img.IsEmpty()) throw new InvalidOperationException("the viewport has no image (headless display?)");
        var err = img.SavePng(path);
        if (err != Error.Ok) throw new IOException("SavePng failed: " + err);
        return new JsonObject { ["file"] = path, ["width"] = img.GetWidth(), ["height"] = img.GetHeight() };
    }

    // ---- monitors -------------------------------------------------------------------------------------------------

    static readonly string[] DefaultMonitors =
    {
        "TimeFps", "TimeProcess", "TimePhysicsProcess", "MemoryStatic", "ObjectCount", "ObjectNodeCount", "ObjectResourceCount",
        "ObjectOrphanNodeCount", "RenderTotalObjectsInFrame", "RenderTotalPrimitivesInFrame", "RenderTotalDrawCallsInFrame",
        "RenderVideoMemUsed", "RenderTextureMemUsed", "RenderBufferMemUsed",
    };

    /// <summary>monitors {names[]}: Godot Performance monitors (enum names, any case) plus the managed GC counts and bytes.</summary>
    JsonNode Monitors(Request r)
    {
        var names = r.GetStrings("names");
        if (names.Count == 0) names.AddRange(DefaultMonitors);
        var engine = new JsonObject();
        foreach (var raw in names)
        {
            string key = raw.Replace("_", "");
            if (Enum.TryParse<Performance.Monitor>(key, true, out var m)) engine[raw] = Performance.GetMonitor(m);
            else engine[raw] = "<no such monitor>";
        }
        var info = GC.GetGCMemoryInfo();
        return new JsonObject
        {
            ["engine"] = engine,
            ["managed"] = new JsonObject
            {
                ["gen0"] = GC.CollectionCount(0), ["gen1"] = GC.CollectionCount(1), ["gen2"] = GC.CollectionCount(2),
                ["heapBytes"] = GC.GetTotalMemory(false), ["allocatedBytes"] = GC.GetTotalAllocatedBytes(false),
                ["committedBytes"] = info.TotalCommittedBytes, ["pauseMs"] = GC.GetTotalPauseDuration().TotalMilliseconds,
            },
            ["frame"] = (long)Engine.GetProcessFrames(),
        };
    }

    // ---- input ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// input {key|text|mouse|action, pressed, ...}: key "A" / "Enter" (pressed omitted = press and release); text types each
    /// character as a key press and release that carries it (a focused text field takes it as typed); mouse "move" | "left" |
    /// "right" | "middle" | "wheel_up" | "wheel_down" with x, y (pressed omitted = click); action name with pressed, strength.
    /// </summary>
    JsonNode Input(Request r)
    {
        bool? pressed = r.Has("pressed") ? r.GetBool("pressed") : null;
        if (r.GetString("key") is { } keyName)
        {
            var code = OS.FindKeycodeFromString(keyName);
            if (code == Key.None) throw new ArgumentException("unknown key " + keyName);
            foreach (bool down in pressed is { } one ? new[] { one } : new[] { true, false })
                Godot.Input.ParseInputEvent(new InputEventKey { Keycode = code, PhysicalKeycode = code, Pressed = down, ShiftPressed = r.GetBool("shift"), CtrlPressed = r.GetBool("ctrl"), AltPressed = r.GetBool("alt") });
            return new JsonObject { ["sent"] = "key " + code };
        }
        if (r.GetString("text") is { } typed)
        {
            foreach (char c in typed)
            {
                var kc = c < 128 && char.IsLetterOrDigit(c) ? (Key)char.ToUpperInvariant(c) : c == ' ' ? Key.Space : c == '/' ? Key.Slash : c == '-' ? Key.Minus : Key.None;
                foreach (bool down in new[] { true, false })
                    Godot.Input.ParseInputEvent(new InputEventKey { Keycode = kc, Unicode = c, Pressed = down });
            }
            return new JsonObject { ["sent"] = "text " + typed.Length };
        }
        if (r.GetString("mouse") is { } what)
        {
            var pos = new Vector2((float)r.GetDouble("x"), (float)r.GetDouble("y"));
            if (what == "move")
            {
                Godot.Input.ParseInputEvent(new InputEventMouseMotion { Position = pos, GlobalPosition = pos, Relative = Vector2.Zero });
                return new JsonObject { ["sent"] = "mouse move", ["at"] = Struct(pos) };
            }
            MouseButton button = what switch
            {
                "left" => MouseButton.Left, "right" => MouseButton.Right, "middle" => MouseButton.Middle,
                "wheel_up" => MouseButton.WheelUp, "wheel_down" => MouseButton.WheelDown, _ => throw new ArgumentException("unknown mouse " + what),
            };
            var mask = button switch { MouseButton.Left => MouseButtonMask.Left, MouseButton.Right => MouseButtonMask.Right, MouseButton.Middle => MouseButtonMask.Middle, _ => (MouseButtonMask)0 };
            Godot.Input.ParseInputEvent(new InputEventMouseMotion { Position = pos, GlobalPosition = pos });
            foreach (bool down in pressed is { } one ? new[] { one } : new[] { true, false })
                Godot.Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = button, Pressed = down, Position = pos, GlobalPosition = pos, ButtonMask = down ? mask : 0 });
            return new JsonObject { ["sent"] = "mouse " + what, ["at"] = Struct(pos) };
        }
        if (r.GetString("action") is { } action)
        {
            if (!InputMap.HasAction(action)) throw new ArgumentException("unknown action " + action);
            foreach (bool down in pressed is { } one ? new[] { one } : new[] { true, false })
                Godot.Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = down, Strength = (float)r.GetDouble("strength", 1) });
            return new JsonObject { ["sent"] = "action " + action };
        }
        throw new ArgumentException("input needs key, mouse or action");
    }

    // ---- log ------------------------------------------------------------------------------------------------------

    /// <summary>log {since, max}: the game's log lines after sequence number <c>since</c> (print output, errors, warnings).</summary>
    JsonNode Log(Request r)
    {
        var (last, items) = logger.Since(r.GetInt("since"), Math.Clamp(r.GetInt("max", 200), 1, 2000));
        var lines = new JsonArray();
        foreach (var (seq, kind, text) in items) lines.Add(new JsonObject { ["seq"] = seq, ["kind"] = kind, ["text"] = text });
        return new JsonObject { ["last"] = last, ["lines"] = lines };
    }
}
#endif
