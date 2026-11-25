using Ms2.Formats.World;

namespace Ms2.Ui;

/// <summary>
/// The field's draws of the portal entities, following CPortal's shown flag. The client draws an entity by its IsVisible
/// (0xa0b6b179, the Renderable mixin's), and CPortal sets that flag on the portal's entity, with IsStatic = !shown and
/// PortalEnable = glow (CPortal::SetShownAndGlow @0x14169cd50, FUN_14169ce50 @0x14169ce50, SetShown @0x14169cf90;
/// portal.cpp:420-464). It does so on the register (WorldService::RegisterPortal: SetGlow, SetShown, then the quest mask
/// FUN_14169d0e0), on every quest mask run (WorldService FUN_141bd3880 on the quest manager's dirty flag), on FieldPortal
/// mode 2 (SetFieldObjectFlags, worldservice_1.cpp:4131-4138) and on the start tick (portal.cpp:281-295). A portal the server
/// never registers keeps its xblock IsVisible, and mode 1 (RemoveFieldObject, worldservice_1.cpp:4054-4079) leaves a bound
/// xblock entity's flags as they were. A portal's model is built whatever its load IsVisible, and drawn from here.
/// </summary>
public sealed class PortalDraws
{
    readonly Action<FieldEntity, bool> show;
    LivePortals bound;

    /// <param name="show">Shows or hides the draws of an entity; an entity with no draw is ignored.</param>
    public PortalDraws(Action<FieldEntity, bool> show) => this.show = show;

    /// <summary>The field's registered portals: each bound entity is drawn by its shown flag now, then on every change.</summary>
    public void Bind(LivePortals portals)
    {
        Detach();
        if (portals == null) return;
        bound = portals;
        foreach (var p in portals.All) Apply(p);
        portals.ShownChanged += Apply;
    }

    public void Detach()
    {
        if (bound != null) bound.ShownChanged -= Apply;
        bound = null;
    }

    void Apply(LivePortal p)
    {
        if (p.Entity != null) show(p.Entity, p.Shown);
    }
}
