using Ms2.Formats.World;

namespace Ms2.Ui.Hud;

/// <summary>The live field's portal marks: one per portal the server registered (<see cref="LivePortals"/>).</summary>
public sealed partial class MinimapController
{
    /// <summary>
    /// The portals' marks of a live field: WorldService's proxies are the registered portals (RegisterPortal's
    /// AddProxyObject, worldservice_1.cpp:9106-9110), so a portal the xblock holds but the server never lists has no proxy and
    /// no mark, and a spawned one (no xblock entity) has its mark. The portals registered now are marked, then each later
    /// register adds its mark (AddProxy) and each remove takes it away (RemoveProxy). Returns the unwatch.
    /// </summary>
    public Action WatchPortals(LivePortals portals, MinimapIcons icons, int fieldId)
    {
        foreach (var lp in portals.All) AddPortal(PortalMark(icons, fieldId, lp));
        Action<LivePortal> added = lp => AddPortal(PortalMark(icons, fieldId, lp));
        Action<LivePortal> removed = lp => RemovePortal(lp.Id);
        portals.Added += added;
        portals.Removed += removed;
        return () => { portals.Added -= added; portals.Removed -= removed; };
    }

    /// <summary>
    /// A registered portal's mark (AddProxy -> UpdatePortal(true), uiminimapentity.cpp:3275): the destination is the entity's
    /// target prop (FUN_14169d280, 0xa162d321: the packet's map when it sent one, else the xblock's TargetFieldSN); the point
    /// is the portal's at the add (UpdateProxyPosition returns for kind 4, so the entity does not follow a later move); the
    /// flags are FUN_14169d1d0 (the minimap flag and IsShown). Null for a portal into the field itself.
    /// </summary>
    public static Mark PortalMark(MinimapIcons icons, int fieldId, LivePortal lp)
    {
        int type = lp.Type, id = lp.Id;
        int Target() => lp.TargetMap != 0 ? lp.TargetMap : (int)(lp.Entity?.Number("TargetFieldSN") ?? 0);
        bool Flags() => lp.Minimap && lp.Shown;
        if (icons.Portal(fieldId, Target(), type, Flags(), true) is not { } e) return null;
        var at = lp.Position;
        return new Mark
        {
            Entity = e, Position = () => at, ShowGuide = true, HasPortal = true, PortalType = type, PortalId = id,
            PortalVisible = Flags, FromPortal = () => icons.Portal(fieldId, Target(), type, Flags(), true),
        };
    }
}
