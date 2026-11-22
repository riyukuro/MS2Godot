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
/// STILL-OPEN: the other kinds' effects (gravity, music concert, hide and lock of the own player, user tag symbol, the lights,
/// the local camera, photo studio); the sight range's war fog and npc visibility (FUN_141bd6810's rest); the weather's world
/// effect (CEffectService::FUN_1411a7e80).
/// </summary>
public sealed class FieldProperties
{
    readonly Dictionary<byte, FieldProperty> byKind = new();

    /// <summary>Applies one 0xCA message (null: the client did nothing).</summary>
    public void Apply(FieldPropertyMessage m)
    {
        switch (m)
        {
            case FieldPropertyMessage.List l:
                foreach (var p in l.Properties) byKind[p.Kind] = p;
                break;
            case FieldPropertyMessage.Add a:
                byKind[a.Property.Kind] = a.Property;
                break;
            case FieldPropertyMessage.Remove r:
                byKind.Remove(r.Kind);
                break;
        }
    }

    /// <summary>A new field: none of the old field's properties stays.</summary>
    public void Clear() => byKind.Clear();

    /// <summary>The property of <paramref name="kind"/>, null without one.</summary>
    public FieldProperty Find(byte kind) => byKind.TryGetValue(kind, out var p) ? p : null;

    public int Count => byKind.Count;

    /// <summary>m_a90: kind 6's range, null without one.</summary>
    public float? SightRange => Find(6) is FieldProperty.SightRange s ? s.Range : null;

    /// <summary>The weather kind 7 set (0 without one).</summary>
    public byte Weather => Find(7) is FieldProperty.Weather w ? w.Value : (byte)0;
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
