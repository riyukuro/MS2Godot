#if MS2_DEVTOOLS
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Godot;
using Ms2.Avm2;
using Ms2.Avm2.Flash;
using Ms2.Devtools;
using Ms2Godot.Ui;

namespace Ms2Godot;

public partial class DevEndpoint
{
    /// <summary>The dotted chain of instance names from a movie's root to <paramref name="o"/> (what as3 {path} takes).</summary>
    static string As3Path(AsDisplayObject o)
    {
        var parts = new List<string>();
        for (var c = o; c != null && c.Parent != null; c = c.Parent) parts.Add(string.IsNullOrEmpty(c.InstanceName) ? "#" + c.Parent.Children.IndexOf(c) : c.InstanceName);
        parts.Reverse();
        return string.Join('.', parts);
    }

    /// <summary>
    /// as3 {movie, path, depth}: the display list of a movie run by the AS3 runtime. Without a movie, lists the views. path is
    /// the dotted instance names from the movie's root ("#n" = the n-th child of an unnamed object). Read-only: no getter or
    /// script runs; variables are the object's own dynamic members.
    /// </summary>
    JsonNode As3(Request r)
    {
        var views = new List<As3View>();
        void Walk(Node n)
        {
            if (n is As3View v) views.Add(v);
            for (int i = 0, c = n.GetChildCount(); i < c; i++) Walk(n.GetChild(i));
        }
        Walk(GetTree().Root);

        string movie = r.GetString("movie");
        if (movie == null)
        {
            var list = new JsonArray();
            foreach (var v in views)
                list.Add(new JsonObject { ["view"] = v.GetPath().ToString(), ["movie"] = v.Player.Main?.Key, ["host"] = v.Player.HostId, ["visible"] = v.IsVisibleInTree() });
            return new JsonObject { ["views"] = list };
        }
        var view = views.Find(v => v.Player.Main?.Key == movie || v.Player.HostId == movie)
            ?? throw new ArgumentException("no movie " + movie);
        AsDisplayObject cur = view.Player.Root;
        foreach (var part in (r.GetString("path") ?? "").Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (cur is not AsContainer box) throw new ArgumentException(cur.InstanceName + " has no children");
            AsDisplayObject next = null;
            if (part.StartsWith('#') && int.TryParse(part[1..], out int ix)) { if (ix >= 0 && ix < box.Children.Count) next = box.Children[ix]; }
            else foreach (var ch in box.Children) if (ch.InstanceName == part) { next = ch; break; }
            cur = next ?? throw new ArgumentException("no child " + part);
        }
        return DescribeAs3(cur, Math.Clamp(r.GetInt("depth", 1), 0, 6), 0);
    }

    static JsonObject DescribeAs3(AsDisplayObject o, int depth, int level)
    {
        var j = new JsonObject
        {
            ["name"] = o.InstanceName, ["class"] = o.Class?.Name ?? o.GetType().Name, ["visible"] = o.IsVisible,
            ["x"] = o.Matrix.Tx, ["y"] = o.Matrix.Ty, ["alpha"] = o.Color.Am, ["depth"] = o.Depth,
        };
        if (o is AsTextField tf) j["text"] = tf.Text.Length > 200 ? tf.Text[..200] + "…" : tf.Text;
        var vars = new JsonObject();
        int n = 0;
        foreach (var k in o.DynamicKeys)
        {
            if (n++ >= 40) break;
            object v = o.GetDynamic(k);
            vars[k] = v switch
            {
                null => null, string s => s.Length > 200 ? s[..200] + "…" : s, bool b => b, int i => i, double d => double.IsFinite(d) ? d : d.ToString(),
                AsObject ao => "<" + (ao.Class?.Name ?? ao.GetType().Name) + ">", _ => v.ToString(),
            };
        }
        if (vars.Count > 0) j["vars"] = vars;
        if (o is AsContainer c)
        {
            j["numChildren"] = c.Children.Count;
            if (level < depth)
            {
                var kids = new JsonArray();
                int shown = 0;
                foreach (var ch in c.Children) { if (shown++ >= 100) break; kids.Add(DescribeAs3(ch, depth, level + 1)); }
                j["children"] = kids;
            }
        }
        return j;
    }
}
#endif
