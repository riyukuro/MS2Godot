using System;
using System.Collections.Generic;
using Ms2.Formats.World;

namespace Ms2.Formats.Model;

/// <summary>
/// The last result of one model's dummy lookups, kept with what it was computed from. A dummy's world point is a function of
/// the rows, the dummy's name, the model root's name and the model root's transform, so while a model stands still (an idle
/// npc, drawn every frame by its name tag, HP bar and emoticon) the point is the one computed before, and no matrices are
/// built. A lookup with any of them changed is computed again.
/// </summary>
public sealed class DummyMemo
{
    /// <summary>A transform as its twelve floats (the basis columns, then the origin).</summary>
    public readonly record struct Key(float A, float B, float C, float D, float E, float F, float G, float H, float I, float J, float K, float L);

    /// <summary>The point, or none (the dummy or its parent is missing).</summary>
    public readonly record struct Point(float X, float Y, float Z, bool Has);

    struct Slot
    {
        public IReadOnlyList<EffectDummy> Rows;
        public int Count;
        public string Root;
        public Key At;
        public Point Result;
    }

    readonly Dictionary<string, Slot> slots = new();

    /// <summary>The remembered point of <paramref name="name"/> if it was made from these very inputs.</summary>
    public bool TryGet(IReadOnlyList<EffectDummy> rows, string name, string rootName, in Key at, out Point result)
    {
        if (slots.TryGetValue(name, out var s) && ReferenceEquals(s.Rows, rows) && s.Count == rows.Count && s.Root == rootName && s.At == at)
        {
            result = s.Result;
            return true;
        }
        result = default;
        return false;
    }

    public void Set(IReadOnlyList<EffectDummy> rows, string name, string rootName, in Key at, Point result) =>
        slots[name] = new Slot { Rows = rows, Count = rows.Count, Root = rootName, At = at, Result = result };
}
