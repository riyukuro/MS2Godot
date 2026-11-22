using System.Collections.Generic;
using Godot;
using Ms2.Formats.World;
using Ms2.Ui;

namespace Ms2Godot.World;

/// <summary>
/// The drawn side of the field's breakable objects (CBreakableObject::SetState @0x140ade420, breakableobject.cpp:270-343):
/// the server's Breakable state (OnBreakable, scenestategame.cpp:3667, applied by <see cref="BreakableObjects.Apply"/>)
/// shows the entity for states 2 (idle), 3 (broken), 5 (regen) and 6 (damage), and state 4 hides it with
/// FUN_140ae07d0(0): a 500 ms fade, then the entity is invisible. Each breakable entity's instances (drawn and
/// shadow-only, FieldBuilder.BreakableMeshDraws) form one <see cref="TriggerFade.Target"/>; the fade runs in a
/// <see cref="TriggerFade"/> of its own, so its ids do not meet the trigger meshes'. A show drops a running fade.
/// Nothing runs per frame unless a fade does (the fader's process is off otherwise).
/// STILL-OPEN: an Actor breakable (MS2BreakableActor, KfmAsset) is not drawn at all: FieldNpcs places actors from the npc
/// archive only and counts these as without a model (02000088_bf: 48), so it has no instances here; its state animations
/// and the dead/regen/damage effects (<see cref="BreakableObjects.Marker.Play"/>, <see cref="BreakableObjects.Marker.Effect"/>);
/// the physics actor a hide releases.
/// </summary>
public sealed class FieldBreakables
{
    readonly Dictionary<FieldEntity, (int Id, TriggerFade.Target Target)> targets = new();
    readonly BreakableObjects markers;

    /// <summary>The node that runs the hide fades, in the tree with the field.</summary>
    public readonly TriggerFade Fader = new() { Name = "BreakableFade" };

    public int Count => targets.Count;

    public FieldBreakables(BreakableObjects markers, IReadOnlyDictionary<FieldEntity, List<(MultiMesh Mm, int Index)>> meshDraws,
                           IReadOnlyDictionary<FieldEntity, List<FieldBuilder.FadeDraw>> fadeDraws)
    {
        this.markers = markers;
        int id = 0;
        foreach (var (e, draws) in meshDraws)
        {
            if (draws.Count == 0) continue;
            var t = new TriggerFade.Target();
            foreach (var (mm, i) in draws) t.All.Add((mm, i, mm.GetInstanceTransform(i)));
            if (fadeDraws.TryGetValue(e, out var drawn)) t.Draws.AddRange(drawn);
            targets[e] = (id++, t);
        }
        if (markers != null && targets.Count > 0) markers.StateSet += OnState;
    }

    /// <summary>Stops following the session's states (the field leaves).</summary>
    public void Detach()
    {
        if (markers != null) markers.StateSet -= OnState;
    }

    /// <summary>Whether the breakable entity is drawn (null: no drawn instances).</summary>
    public bool? Shown(FieldEntity e) => e != null && targets.TryGetValue(e, out var t) ? t.Target.Shown : null;

    void OnState(string name, int state)
    {
        if (markers.Find(name) is not { Entity: { } e } || !targets.TryGetValue(e, out var t)) return;
        switch (state)
        {
            case 4: Fader.FadeEntity(t.Id, t.Target, false, 0.5f); break;
            case 2: case 3: case 5: case 6: Fader.Stop(t.Id, t.Target, true); break;
        }
    }
}
