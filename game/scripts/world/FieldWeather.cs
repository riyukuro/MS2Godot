using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Nif;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Ui;
using Ms2Godot.Net;

namespace Ms2Godot.World;

/// <summary>
/// The field's weather: the sky object of a "weather" part in the field's map effect (fielddata &lt;ui mapEffectId&gt;, e.g.
/// BG/weather/Eff_Snow.xml with fallType 1). CEffectService::vfunc_1 starts the map effect on field entry; its WeatherEffect
/// (weathereffectparam.cpp:57) calls CEffectService::SetSkyObject(fallType), which attaches the preloaded nif of that index
/// (CEffectService::OnInit, effectservice.cpp:2263-2285: CommonResource weatherSnowSoft .. weatherLeafFall, index 1..7) to the
/// world effect root. Each tick FUN_1411aab90 (effectservice.cpp:2443) puts it 1500 units ahead of the camera, translation
/// only, so the systems keep the world axes and fall straight down whichever way the camera looks.
/// STILL-OPEN: the map effect's other parts (monochrome saturation, postShimmer), the game-event field effect that replaces
/// mapEffectId (CGameEventValueFieldEffect), the weather a field property sets at run time (FieldPropertyWeather,
/// fieldpropertycli.cpp:197; FUN_1411a7e80, effectservice.cpp:1934-1966: LiveSession.FieldProperties.Weather holds the
/// value, nothing applies it yet), the rain drop system SetRainEnabled adds for fallType 3-4, and world-space
/// particles: our systems simulate in the effect's own space, so the emitted flakes move with the camera.
/// </summary>
public sealed partial class FieldWeather : Node3D
{
    /// <summary>CommonResource keys of SetSkyObject's indexes 1..7 (CommonResource +0x208..+0x238, commonresource.cpp:146-152).</summary>
    static readonly string[] SkyKeys = { "weatherSnowSoft", "weatherSnowHeavy", "weatherRainSoft", "weatherRainHeavy", "weatherSandstorm", "weatherCherryBlossom", "weatherLeafFall" };
    const string EffectArchive = "Resource/Model/Effect";
    const float AheadOfCamera = 1500f;   // FUN_1411aab90 fDistance

    /// <summary>The weather of a map effect, or null when it has no weather part with a sky object.</summary>
    public static FieldWeather Create(PackSet packs, ModelBuilder builder, string mapEffectId)
    {
        if (string.IsNullOrEmpty(mapEffectId)) return null;
        var part = new EffectTable(packs).Get(mapEffectId)?.LastOrDefault(p => p.Type == "weather");
        if (part == null || part.FallType < 1 || part.FallType > SkyKeys.Length) return null;
        string path = GameTables.Shared(packs, GameOptions.StringLanguage).CommonResource(SkyKeys[part.FallType - 1]);
        const string prefix = "./Data/" + EffectArchive + "/";
        if (!path.Replace('\\', '/').StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        string entry = path.Replace('\\', '/')[prefix.Length..].ToLowerInvariant();
        try
        {
            if (!packs.Exists(EffectArchive, entry)) { GD.PushWarning($"weather {mapEffectId}: no {entry}"); return null; }
            var scene = EffectScene.Load(NifFile.Load(builder.Spec, packs.Read(EffectArchive, entry)));
            var w = new FieldWeather { Name = "FieldWeather" };
            w.AddChild(new EffectInstance(scene, builder, 1e7f, 0) { Name = "Sky" });
            GD.Print($"field weather: {mapEffectId} fallType {part.FallType} {entry}, {scene.Systems.Count} systems");
            return w;
        }
        catch (Exception ex) { GD.PushWarning($"weather {entry}: {ex.Message}"); return null; }
    }

    public override void _Ready() => TopLevel = true;

    public override void _Process(double delta)
    {
        var cam = GetViewport().GetCamera3D();
        if (cam == null) return;
        var t = cam.GlobalTransform;
        Position = t.Origin - t.Basis.Z * AheadOfCamera;
    }
}
