using System.Collections.Generic;
using Godot;
using Ms2.Formats.World;
using Ms2.Ui;

namespace Ms2Godot.World;

/// <summary>
/// The drawn side of the lift target boxes' markers (CLiftableTarget CreateEffect / DestroyEffect, liftabletarget.cpp:
/// 137-160): a box <see cref="LiftableTargetBoxes.RefreshMarkers"/> marks plays <see cref="LiftableTargetBoxes.MarkerEffect"/>
/// as a kept effect through the field's effect service (CEffectService FUN_1411a49c0) at
/// <see cref="LiftableTargetBoxes.MarkerAt"/>; an unmarked one removes it (FUN_1411a6060). The effects go with the field.
/// </summary>
public sealed class LiftTargetMarkers
{
    readonly LiftableTargetBoxes boxes;
    readonly IRegionSkillEffects effects;
    readonly Dictionary<LiftableTargetBoxes.Box, int> handles = new();

    public LiftTargetMarkers(LiftableTargetBoxes boxes, IRegionSkillEffects effects)
    {
        this.boxes = boxes;
        this.effects = effects;
        if (boxes == null || effects == null) return;
        boxes.MarkerSet += OnMarker;
        foreach (var b in boxes.All)
            if (b.Marked) OnMarker(b, true);
    }

    /// <summary>The markers that run.</summary>
    public int Running => handles.Count;

    /// <summary>Stops following the boxes (the field leaves).</summary>
    public void Detach()
    {
        if (boxes != null && effects != null) boxes.MarkerSet -= OnMarker;
    }

    void OnMarker(LiftableTargetBoxes.Box b, bool on)
    {
        if (handles.Remove(b, out int h) && h != 0) effects.FadeOut(h, 0);
        if (!on)
        {
            GD.Print($"live: lift target {b.Id} marker off");
            return;
        }
        int spawned = effects.Spawn(LiftableTargetBoxes.MarkerEffect, LiftableTargetBoxes.MarkerAt(b), false, 0);
        if (spawned != 0) handles[b] = spawned;
        GD.Print($"live: lift target {b.Id} marker {(spawned != 0 ? "on" : "not spawned")}");
    }
}
