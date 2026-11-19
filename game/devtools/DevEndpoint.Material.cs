#if MS2_DEVTOOLS
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using Godot;
using Ms2.Devtools;

namespace Ms2Godot;

public partial class DevEndpoint
{
    /// <summary>material {path, surface}: every material the node draws with, with shader parameters and render state.</summary>
    JsonNode MaterialInfo(Request r)
    {
        var n = Resolve(r);
        int surface = r.GetInt("surface", -1);
        var list = new JsonArray();
        void Add(string where, Material m, GeometryInstance3D owner) { if (m != null) list.Add(DescribeMaterial(where, m, owner)); }

        var res = new JsonObject { ["path"] = n.GetPath().ToString(), ["type"] = n.GetClass(), ["materials"] = list };
        if (n is GeometryInstance3D g)
        {
            res["geometry"] = new JsonObject
            {
                ["transparency"] = g.Transparency, ["castShadow"] = g.CastShadow.ToString(), ["sortingOffset"] = g.SortingOffset,
                ["sortingUseAabbCenter"] = g.SortingUseAabbCenter, ["layers"] = (long)g.Layers,
            };
            Add("materialOverride", g.MaterialOverride, g);
            Add("materialOverlay", g.MaterialOverlay, g);
            if (n is MeshInstance3D mi && mi.Mesh != null)
                for (int i = 0, c = mi.Mesh.GetSurfaceCount(); i < c; i++)
                {
                    if (surface >= 0 && i != surface) continue;
                    Add($"surface{i}.override", mi.GetSurfaceOverrideMaterial(i), g);
                    Add($"surface{i}.mesh", mi.Mesh.SurfaceGetMaterial(i), g);
                }
            if (n is MultiMeshInstance3D mm && mm.Multimesh?.Mesh is { } mmesh)
                for (int i = 0, c = mmesh.GetSurfaceCount(); i < c; i++) Add($"multimesh.surface{i}", mmesh.SurfaceGetMaterial(i), g);
            if (n is GpuParticles3D gp)
            {
                Add("processMaterial", gp.ProcessMaterial, g);
                Mesh[] passes = { gp.DrawPass1, gp.DrawPass2, gp.DrawPass3, gp.DrawPass4 };
                for (int p = 0; p < passes.Length; p++)
                    if (passes[p] != null)
                        for (int i = 0, c = passes[p].GetSurfaceCount(); i < c; i++) Add($"drawPass{p + 1}.surface{i}", passes[p].SurfaceGetMaterial(i), g);
            }
            if (n is CpuParticles3D cp && cp.Mesh != null)
                for (int i = 0, c = cp.Mesh.GetSurfaceCount(); i < c; i++) Add($"particles.surface{i}", cp.Mesh.SurfaceGetMaterial(i), g);
        }
        else if (n is CanvasItem ci) Add("canvasItem", ci.Material, null);
        return res;
    }

    static readonly Regex RenderModeRx = new(@"render_mode\s+([^;]+);", RegexOptions.Compiled);

    JsonObject DescribeMaterial(string where, Material m, GeometryInstance3D owner)
    {
        var j = new JsonObject
        {
            ["where"] = where, ["type"] = m.GetClass(), ["renderPriority"] = m.RenderPriority,
        };
        if (!string.IsNullOrEmpty(m.ResourcePath)) j["res"] = m.ResourcePath;
        if (m.NextPass != null) j["nextPass"] = m.NextPass.GetClass();
        switch (m)
        {
            case ShaderMaterial sm:
            {
                var shader = sm.Shader;
                if (shader == null) { j["shader"] = null; break; }
                j["shader"] = string.IsNullOrEmpty(shader.ResourcePath) ? "<inline>" : shader.ResourcePath;
                j["shaderMode"] = shader.GetMode().ToString();
                if (RenderModeRx.Match(shader.Code ?? "") is { Success: true } rm)
                    j["renderMode"] = rm.Groups[1].Value.Trim();
                var inst = new HashSet<string>();
                if (owner != null)
                    foreach (var d in owner.GetPropertyList())
                    {
                        string pn = d["name"].AsString();   // perf-ok: dev endpoint request, not per frame
                        if (pn.StartsWith("instance_shader_parameters/")) inst.Add(pn["instance_shader_parameters/".Length..]);
                    }
                var parms = new JsonObject();
                foreach (var uv in shader.GetShaderUniformList())
                {
                    var u = uv.AsGodotDictionary();
                    string name = u["name"].AsString();
                    var entry = new JsonObject { ["uniformType"] = u["type"].AsInt64() };
                    var v = sm.GetShaderParameter(name);
                    entry["value"] = FromVariant(v, 0);
                    if (v.VariantType == Variant.Type.Nil) entry["default"] = FromVariant(RenderingServer.ShaderGetParameterDefault(shader.GetRid(), name), 0);
                    if (inst.Contains(name)) entry["instance"] = FromVariant(owner.Get("instance_shader_parameters/" + name), 0);
                    parms[name] = entry;
                }
                j["params"] = parms;
                break;
            }
            case BaseMaterial3D bm:
                j["transparency"] = bm.Transparency.ToString(); j["blendMode"] = bm.BlendMode.ToString();
                j["shadingMode"] = bm.ShadingMode.ToString(); j["cullMode"] = bm.CullMode.ToString();
                j["depthDrawMode"] = bm.DepthDrawMode.ToString(); j["noDepthTest"] = bm.NoDepthTest;
                j["props"] = StoredProps(m);
                break;
            case CanvasItemMaterial cm:
                j["blendMode"] = cm.BlendMode.ToString(); j["lightMode"] = cm.LightMode.ToString();
                break;
            default:
                j["props"] = StoredProps(m);
                break;
        }
        return j;
    }

    static JsonObject StoredProps(GodotObject o)
    {
        var props = new JsonObject();
        int n = 0;
        foreach (var d in o.GetPropertyList())
        {
            var usage = (PropertyUsageFlags)d["usage"].AsInt64();   // perf-ok: dev endpoint request, not per frame
            if ((usage & PropertyUsageFlags.Storage) == 0 || (usage & PropertyUsageFlags.Category) != 0) continue;
            string name = d["name"].AsString();   // perf-ok: dev endpoint request, not per frame
            if (name.StartsWith("resource_") || name == "script") continue;
            var v = o.Get(name);
            if (v.VariantType == Variant.Type.Nil) continue;
            if (n++ >= 96) break;
            props[name] = FromVariant(v, 0);
        }
        return props;
    }
}
#endif
