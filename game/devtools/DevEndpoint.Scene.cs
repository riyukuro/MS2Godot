#if MS2_DEVTOOLS
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Godot;
using Ms2.Devtools;

namespace Ms2Godot;

public partial class DevEndpoint
{
    const int MaxTreeNodes = 3000, MaxFound = 300;

    /// <summary>tree {path, depth, filter}: node names, types, visibility and global transforms below a node.</summary>
    JsonNode Tree(Request r)
    {
        var start = Resolve(r);
        int depth = Math.Clamp(r.GetInt("depth", 3), 0, 32);
        string filter = r.GetString("filter");
        int budget = MaxTreeNodes;
        return TreeNode(start, depth, filter, ref budget, true) ?? new JsonObject { ["note"] = "nothing matched the filter" };
    }

    static bool Matches(Node n, string filter) =>
        string.IsNullOrEmpty(filter) || n.Name.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase)
        || n.GetClass().Contains(filter, StringComparison.OrdinalIgnoreCase) || n.GetType().Name.Contains(filter, StringComparison.OrdinalIgnoreCase);

    JsonObject TreeNode(Node n, int depth, string filter, ref int budget, bool force)
    {
        if (budget <= 0) return null;
        var children = new JsonArray();
        if (depth > 0)
            for (int i = 0, c = n.GetChildCount(); i < c; i++)
                if (TreeNode(n.GetChild(i), depth - 1, filter, ref budget, false) is { } j) children.Add(j);
        if (!force && !Matches(n, filter) && children.Count == 0) return null;
        budget--;
        var o = Describe(n);
        if (depth == 0 && n.GetChildCount() > 0) o["children"] = n.GetChildCount();
        else if (children.Count > 0) o["children"] = children;
        return o;
    }

    static JsonObject Describe(Node n)
    {
        var o = new JsonObject { ["name"] = n.Name.ToString(), ["type"] = n.GetClass() };
        string cs = n.GetType().FullName;
        if (cs != null && cs != "Godot." + n.GetClass()) o["cs"] = cs;
        if (n.GetScript().Obj is Script s && !string.IsNullOrEmpty(s.ResourcePath)) o["script"] = s.ResourcePath;
        switch (n)
        {
            case Node3D n3:
                o["visible"] = n3.Visible; o["inTree"] = n3.IsVisibleInTree();
                o["xf"] = Struct(n3.GlobalTransform);
                break;
            case Node2D n2:
                o["visible"] = n2.Visible; o["inTree"] = n2.IsVisibleInTree();
                o["xf"] = Struct(n2.GlobalTransform);
                break;
            case Control c:
                o["visible"] = c.Visible; o["inTree"] = c.IsVisibleInTree();
                o["rect"] = Struct(c.GetGlobalRect());
                break;
            case CanvasItem ci:
                o["visible"] = ci.Visible; o["inTree"] = ci.IsVisibleInTree();
                break;
        }
        return o;
    }

    /// <summary>node {path, props[], csharp, member, depth, items, statics, getters}: engine properties and/or the C# state.</summary>
    JsonNode NodeProps(Request r)
    {
        var n = Resolve(r);
        var result = Describe(n);
        result["path"] = n.GetPath().ToString();
        var props = r.GetStrings("props");
        if (props.Count > 0)
        {
            var known = new HashSet<string>();
            foreach (var d in n.GetPropertyList()) known.Add(d["name"].AsString());
            var values = new JsonObject();
            foreach (var p in props)
                values[p] = known.Contains(p) ? FromVariant(n.Get(p), 0) : "<no such property>";
            result["props"] = values;
        }
        else if (!r.GetBool("csharp"))
        {
            var names = new JsonArray();
            foreach (var d in n.GetPropertyList())
            {
                var usage = (PropertyUsageFlags)d["usage"].AsInt64();
                if ((usage & (PropertyUsageFlags.Editor | PropertyUsageFlags.ScriptVariable)) != 0 && (usage & PropertyUsageFlags.Category) == 0)
                    names.Add(d["name"].AsString());
            }
            result["propertyNames"] = names;
        }
        if (r.GetBool("csharp"))
        {
            var dump = new ReflectionDump
            {
                MaxDepth = Math.Clamp(r.GetInt("depth", 3), 0, 8),
                MaxItems = Math.Clamp(r.GetInt("items", 24), 1, 256),
                IncludeStatics = r.GetBool("statics"),
                CallGetters = r.GetBool("getters"),
                Custom = Struct,
                StopAt = t => t.Assembly == typeof(GodotObject).Assembly,
            };
            object target = n;
            if (r.GetString("member") is { Length: > 0 } member)
            {
                target = ReflectionDump.Navigate(n, member, out var err);
                if (err != null) throw new ArgumentException(err);
                result["member"] = member;
            }
            result["csharp"] = dump.Dump(target);
        }
        return result;
    }

    /// <summary>find {type|name|group, under}: matching node paths (type matches the engine class or the C# class).</summary>
    JsonNode Find(Request r)
    {
        var under = Resolve(r, "under");
        string type = r.GetString("type"), name = r.GetString("name"), group = r.GetString("group");
        var found = new JsonArray();
        int count = 0;
        void Visit(Node n)
        {
            bool ok = true;
            if (type != null) ok &= n.IsClass(type) || n.GetType().Name.Equals(type, StringComparison.OrdinalIgnoreCase) || (n.GetType().FullName ?? "").EndsWith("." + type, StringComparison.OrdinalIgnoreCase);
            if (name != null) ok &= n.Name.ToString().Contains(name, StringComparison.OrdinalIgnoreCase);
            if (group != null) ok &= n.IsInGroup(group);
            if (ok && (type != null || name != null || group != null))
            {
                count++;
                if (found.Count < MaxFound) found.Add(n.GetPath().ToString());
            }
            for (int i = 0, c = n.GetChildCount(); i < c; i++) Visit(n.GetChild(i));
        }
        Visit(under);
        return new JsonObject { ["count"] = count, ["paths"] = found, ["truncated"] = count > MaxFound };
    }
}
#endif
