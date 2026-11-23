using System.Collections.Generic;
using Godot;
using Ms2.Formats.World;
using Ms2.Ui;

namespace Ms2Godot.World;

/// <summary>
/// The drawn side of the field's lifts (CLiftableObject, liftableobject.cpp): the server's Liftable states (OnLiftable,
/// scenestategame.cpp:11941, applied by <see cref="LiftableObjects.Apply"/>) set each field lift's active flag
/// (<see cref="LiftableObjects.ActiveSet"/>). SetActive(0), a lift picked up (state 1), fades the entity's alpha 1 to 0
/// over 500 ms and then hides it (:386-390, the timer :313-321); SetActive(1), states 0 and 2, shows it at alpha 1 at
/// once (:375-381). A lift whose entity is removed (Liftable mode 4, DestroyLiftByName, worldservice_1.cpp:4451-4456; or
/// a list that no longer names it, scenestategame.cpp:11973-11991) is hidden at once (<see cref="LiftableObjects.Removed"/>).
/// Each lift entity's instances (drawn and shadow-only, FieldBuilder.LiftableMeshDraws) form one
/// <see cref="TriggerFade.Target"/> in a <see cref="TriggerFade"/> of its own. Nothing runs per frame unless a fade does.
/// STILL-OPEN: SetState's collision groups (9 when active, 5 when lifted, :298-304) and the entity's shadow, which the
/// fade drops with the instances here; a lift bound to a placed cube's entity (cubemapinformationservice_3.cpp:5689-5694),
/// as the placed cubes have no entity here; a lift whose model is drawn as an animated prop (world/FieldProps, counted in
/// the field load's "lifts" line) is not faded.
/// </summary>
public sealed class FieldLiftables
{
    readonly Dictionary<string, (int Id, TriggerFade.Target Target)> targets = new(System.StringComparer.Ordinal);
    readonly LiftableObjects lifts;

    /// <summary>The node that runs the fades, in the tree with the field.</summary>
    public readonly TriggerFade Fader = new() { Name = "LiftableFade" };

    public int Count => targets.Count;

    public FieldLiftables(LiftableObjects lifts, IReadOnlyDictionary<FieldEntity, List<(MultiMesh Mm, int Index)>> meshDraws,
                          IReadOnlyDictionary<FieldEntity, List<FieldBuilder.FadeDraw>> fadeDraws)
    {
        this.lifts = lifts;
        int id = 0;
        foreach (var (e, draws) in meshDraws)
        {
            // the lift's name: the entity's lowercased DataFileID (BindEntity, liftableobject.cpp:186-191)
            string name = e.Raw?.Id;
            if (draws.Count == 0 || string.IsNullOrEmpty(name)) continue;
            var t = new TriggerFade.Target();
            foreach (var (mm, i) in draws) t.All.Add((mm, i, mm.GetInstanceTransform(i)));
            if (fadeDraws.TryGetValue(e, out var drawn)) t.Draws.AddRange(drawn);
            targets[name.ToLowerInvariant()] = (id++, t);
        }
        if (lifts == null || targets.Count == 0) return;
        lifts.ActiveSet += OnActive;
        lifts.Removed += OnRemoved;
        // states the session applied before this field was drawn (its entities attach during the field load)
        foreach (var (name, (_, t)) in targets)
            if (lifts.Find(name) is { HasEntity: true, Active: false }) TriggerFade.Switch(t, false);
    }

    /// <summary>Stops following the session's states (the field leaves).</summary>
    public void Detach()
    {
        if (lifts == null) return;
        lifts.ActiveSet -= OnActive;
        lifts.Removed -= OnRemoved;
    }

    /// <summary>Whether the lift's entity is drawn (null: no drawn instances).</summary>
    public bool? Shown(string name) => name != null && targets.TryGetValue(name, out var t) ? t.Target.Shown : null;

    void OnActive(string name, bool active)
    {
        if (!targets.TryGetValue(name, out var t)) return;
        GD.Print($"live: lift {name} {(active ? "shown" : "fades out")}");
        if (active) Fader.Stop(t.Id, t.Target, true);
        else Fader.FadeEntity(t.Id, t.Target, false, 0.5f);
    }

    void OnRemoved(string name)
    {
        if (!targets.Remove(name, out var t)) return;
        GD.Print($"live: lift {name} removed");
        Fader.Stop(t.Id, t.Target, false);
    }
}
