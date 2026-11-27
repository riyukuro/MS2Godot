using System.Numerics;
using Ms2.Net.Packets;

namespace Ms2.Ui.Hud;

/// <summary>
/// The field's properties by kind (WorldService m_780, filled by 0xCA, <see cref="FieldPropertyMessage"/>): a list or an add
/// sets each record's kind (SetAt replaces the entry; the old one is not unapplied), a remove drops the kind, and a new field
/// clears them all (ClearFieldProperties, worldservice_1.cpp:5368).
/// The client reads two of them back here:
/// <list type="bullet">
/// <item><see cref="SightRange"/>: each world update copies kind 6's range into m_a90, and resets it without one
/// (WorldService::FUN_141bd6810, worldservice_1.cpp:7783, called from the update at :9818).</item>
/// <item><see cref="Weather"/>: kind 7's apply sets the effect service's weather to its value and its unapply sets 0
/// (FieldPropertyWeatherCli, fieldpropertycli.cpp:197-199), so the weather is kind 7's value, else 0.</item>
/// </list>
/// Kinds 8 and 9 set the field's ambient and sun light through <see cref="Lights"/> (FieldPropertyAmbientLightCli and
/// FieldPropertyDirectionalLightCli vfunc_4, fieldpropertycli.cpp:202-237), at once or, with the record's flag, as a fade over
/// the record's seconds that <see cref="StepLights"/> runs each frame (WorldService::UpdateAmbientLight @0x141bc4fc0 and
/// UpdateDirectionalLight @0x141bc51d0, worldservice_1.cpp:4362-4437, called from the world update at :9736). Neither light
/// class overrides the unapply (vfunc_5 stays FieldProperty's empty @0x1403808d0, fieldpropertycli.h:12-22), so a remove or a
/// clear only drops the record and stops a running fade: the light keeps the colour it has.
/// Kind 3 hides the own player while it is registered (<see cref="HideMyPCApplied"/>): a list or an add applies it, and a
/// remove or a clear unapplies it (RemoveFieldProperty and ClearFieldProperties call vfunc_5, worldservice_1.cpp:5354-5378).
/// Kind 10 turns the field's automatic local cameras on or off (<see cref="LocalCamerasOn"/>).
/// STILL-OPEN: the other kinds' effects (gravity, music concert, lock of the own player, user tag symbol, photo studio); the sight range's war fog and npc visibility (FUN_141bd6810's rest); the weather's world
/// effect (CEffectService::FUN_1411a7e80).
/// </summary>
public sealed class FieldProperties
{
    readonly Dictionary<byte, FieldProperty> byKind = new();
    readonly Dictionary<byte, LightFade> fades = new();
    IFieldLights lights;

    /// <summary>The field's ambient and sun light entities (WorldService::FUN_141bd0420 with the model keys 0x169ea355 and
    /// 0x628f2cf9), null without a field. A light record applied while this is null does nothing, as in the client when the
    /// tag finds no entity (fieldpropertycli.cpp:204); setting it applies the stored light records (the field is built after
    /// the record arrived).</summary>
    public IFieldLights Lights
    {
        get => lights;
        set
        {
            lights = value;
            if (value == null) return;
            foreach (byte k in new byte[] { 8, 9 })
                if (byKind.TryGetValue(k, out var p)) ApplyLight(p);
        }
    }

    /// <summary>Applies one 0xCA message (null: the client did nothing).</summary>
    public void Apply(FieldPropertyMessage m)
    {
        switch (m)
        {
            case FieldPropertyMessage.List l:
                foreach (var p in l.Properties) { ApplyOne(p); byKind[p.Kind] = p; }   // vfunc_4, then SetAt (worldservice_1.cpp:5336-5337)
                break;
            case FieldPropertyMessage.Add a:
                ApplyOne(a.Property);
                byKind[a.Property.Kind] = a.Property;
                break;
            case FieldPropertyMessage.Remove r:
                // RemoveFieldProperty (@0x141bca9b0): a kind not registered does nothing; else vfunc_5, then RemoveKey
                if (!byKind.TryGetValue(r.Kind, out var old)) break;
                Unapply(old);
                byKind.Remove(r.Kind);
                fades.Remove(r.Kind);
                break;
        }
    }

    /// <summary>The kinds whose apply this port has (each FieldPropertyXCli::vfunc_4 above, fieldpropertycli.cpp): the hide
    /// (3), the tag symbols (5), the sight range (6), the weather (7), the two lights (8, 9) and the local cameras (10). The
    /// others are read and kept but do nothing here: the gravity (1), the music concert (2), the lock of the own player (4)
    /// and the photo studio (11).</summary>
    public static bool Applies(byte kind) => kind is 3 or 5 or 6 or 7 or 8 or 9 or 10;

    /// <summary>A record's vfunc_4, for the kinds the port applies.</summary>
    void ApplyOne(FieldProperty p)
    {
        ApplyLight(p);
        if (p.Kind == 5) UserTagSymbolApplied?.Invoke();
        if (p is FieldProperty.HideMyPC) HideMyPCApplied?.Invoke(true);
        if (p is FieldProperty.EnableLocalCamera e) SetLocalCameras(e.Value);
    }

    /// <summary>A record's vfunc_5, for the kinds the port unapplies (the lights and kind 5 keep FieldProperty's empty one).</summary>
    void Unapply(FieldProperty p)
    {
        if (p is FieldProperty.HideMyPC) HideMyPCApplied?.Invoke(false);
        if (p is FieldProperty.EnableLocalCamera) SetLocalCameras(true);
    }

    /// <summary>CameraMgr::FUN_140aeb900 (@0x140aeb900, camera.cpp:1003): m_24c = b.</summary>
    void SetLocalCameras(bool on)
    {
        LocalCamerasOn = on;
        LocalCamerasChanged?.Invoke(on);
    }

    /// <summary>CameraMgr m_24c (camera.h:310), read by FUN_140aed0d0 (camera.cpp:1281): the field's local cameras are on.
    /// Kind 10's apply sets its value and its unapply sets true (FieldPropertyEnableLocalCameraCli vfunc_4 @0x140b27ca0 and
    /// vfunc_5 @0x140b27cd0, fieldpropertycli.cpp:243-245), so a remove or a field leave's clear turns them on again; the
    /// camera's release on that leave sets it too (CameraMgr::FUN_140aead90, camera.cpp:877, from worldservice_1.cpp:10103).
    /// While it is false the auto check enters no setup (FUN_140aece50, camera.cpp:1199) and a local camera mode moves back
    /// (CLocalTargetCameraMode::vfunc_8, localtargetcameramode.cpp:161-164). It starts true (CameraMgr(), camera.cpp:755).</summary>
    public bool LocalCamerasOn { get; private set; } = true;

    /// <summary>Kind 10's apply or unapply set <see cref="LocalCamerasOn"/> to this value.</summary>
    public event Action<bool> LocalCamerasChanged;

    /// <summary>A new field (ClearFieldProperties @0x141bcaa80): each registered property is unapplied, then none of the old
    /// field's properties stays, and the lights are the new field's.</summary>
    public void Clear()
    {
        foreach (var p in byKind.Values.ToArray()) Unapply(p);
        byKind.Clear(); fades.Clear(); lights = null;
        LocalCamerasOn = true;   // CameraMgr::FUN_140aead90 on the same leave (worldservice_1.cpp:10103, camera.cpp:877)
    }

    /// <summary>Kind 3 is registered: the own player is hidden for it.</summary>
    public bool HidesMyPC => Find(3) is FieldProperty.HideMyPC;

    /// <summary>Kinds 8 and 9's apply: a record with the flag starts a fade from the light's colour now (m_bCached, m_fCached 0
    /// and the old colour read back, fieldpropertycli.cpp:208-213 and :228-233); one without sets the colour. The directional
    /// light's colour is the first rgb, written to its diffuse and specular colours (:235-236); the second rgb is unused.</summary>
    void ApplyLight(FieldProperty p)
    {
        if (lights == null) return;
        switch (p)
        {
            case FieldProperty.AmbientLight a:
                if (a.Flag) fades[8] = new LightFade(lights.Ambient); else { fades.Remove(8); lights.Ambient = LightFade.Rgb(a.R, a.G, a.B); }
                break;
            case FieldProperty.DirectionalLight d:
                if (d.Flag) fades[9] = new LightFade(lights.Sun); else { fades.Remove(9); lights.Sun = LightFade.Rgb(d.R1, d.G1, d.B1); }
                break;
        }
    }

    /// <summary>One world update's light fades (UpdateAmbientLight then UpdateDirectionalLight): each light with a running fade
    /// and a record still registered moves towards the record's colour, and the colour is set exactly when the seconds are up.</summary>
    public void StepLights(float dt)
    {
        if (lights == null || fades.Count == 0) return;
        if (fades.TryGetValue(8, out var fa) && Find(8) is FieldProperty.AmbientLight a
            && fa.Step(dt, LightFade.Rgb(a.R, a.G, a.B), a.Value) is Vector3 ca) lights.Ambient = ca;
        if (fades.TryGetValue(9, out var fd) && Find(9) is FieldProperty.DirectionalLight d
            && fd.Step(dt, LightFade.Rgb(d.R1, d.G1, d.B1), d.Value) is Vector3 cd) lights.Sun = cd;
    }

    /// <summary>The property of <paramref name="kind"/>, null without one.</summary>
    public FieldProperty Find(byte kind) => byKind.TryGetValue(kind, out var p) ? p : null;

    public int Count => byKind.Count;

    /// <summary>m_a90: kind 6's range, null without one.</summary>
    public float? SightRange => Find(6) is FieldProperty.SightRange s ? s.Range : null;

    /// <summary>The weather kind 7 set (0 without one).</summary>
    public byte Weather => Find(7) is FieldProperty.Weather w ? w.Value : (byte)0;

    /// <summary>Kind 5 (FieldPropertyUserTagSymbolCli): the name tag symbols of team 1 and team 2; null without one.</summary>
    public (string Team1, string Team2)? UserTagSymbol => Find(5) is FieldProperty.UserTagSymbol s ? (s.A, s.B) : null;

    /// <summary>Kind 5's apply (FieldPropertyUserTagSymbolCli::vfunc_4, fieldpropertycli.cpp:193-194: WorldService::FUN_141bcabf0
    /// refreshes every player's name tag entity). It has no remove, so a removed kind 5 refreshes nothing.</summary>
    public event Action UserTagSymbolApplied;

    /// <summary>Kind 3 (FieldPropertyHideMyPCCli): true on its apply (vfunc_4 @0x140b27700: the local player, when there is one,
    /// AddHideReason(1), and its ridee too), false on its unapply (vfunc_5 @0x140b27790: RemoveHideReason(1) on both;
    /// fieldpropertycli.cpp:146-171). The third call there passes the local player again, so it changes nothing.</summary>
    public event Action<bool> HideMyPCApplied;
}

/// <summary>
/// The field's sight range (<see cref="FieldProperties.SightRange"/>) and the local player's position (client space), null
/// without them. <see cref="IsOutside"/> is WorldService::IsOutsideSightRange (worldservice_1.cpp:7956).
/// </summary>
public readonly record struct FieldSight(float? Range, Vector3? Player)
{
    /// <summary>True when there is a range and a local player and <paramref name="at"/> is farther than the range from it.</summary>
    public bool IsOutside(Vector3 at) => Range is float r && Player is Vector3 p && Vector3.Distance(at, p) > r;
}

/// <summary>The field's ambient and sun light colours (0..1 rgb). The sun colour is the light's diffuse colour: reading gives it,
/// writing sets its diffuse and specular colours (entity properties 0x2d8cf386 and 0xc3f0329e, lighteffectparam.cpp:373-374).
/// The ambient colour is the ambient light's AmbientColor (0xb086f4a8).</summary>
public interface IFieldLights
{
    Vector3 Ambient { get; set; }
    Vector3 Sun { get; set; }
}

/// <summary>One light's fade between field property records: from the colour it had when the record applied to the record's colour
/// over the record's seconds (UpdateAmbientLight: elapsed += dt; while seconds &gt; elapsed the colour is old + elapsed / seconds *
/// (target - old); then the target is set once and the fade ends, worldservice_1.cpp:4374-4387).</summary>
public sealed class LightFade
{
    readonly Vector3 old;
    float elapsed;
    bool active = true;

    public LightFade(Vector3 from) => old = from;

    public bool Active => active;

    /// <summary>Unk_Rgb3b::ToColor (@0x1406c3780): each byte over 255.</summary>
    public static Vector3 Rgb(byte r, byte g, byte b) => new(r / 255f, g / 255f, b / 255f);

    /// <summary>One frame: the colour to set, null when the fade has ended already.</summary>
    public Vector3? Step(float dt, Vector3 target, float seconds)
    {
        if (!active) return null;
        elapsed += dt;
        if (seconds > elapsed) return old + elapsed / seconds * (target - old);
        active = false;
        return target;
    }
}
