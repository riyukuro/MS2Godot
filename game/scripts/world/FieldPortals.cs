using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.World;

namespace Ms2Godot.World;

/// <summary>
/// The field's portals, from its entity set (entities with the Portal mixin). The client gets them from the server (packet 0x48
/// mode 0), finds its own xblock entity with the same PortalID, and builds the portal volume from it (Unk_PortalBox): a box of
/// PortalDimension, turned by the Z rotation only, centred on Position and then moved by PortalOffset (not rotated). Entering is
/// an action-key object (PortalAction): with the player's position inside an enabled portal's box, the action key sends
/// RequestMoveField mode 0 (target map, portal id). The offline sandbox answers that itself: the target field loads and the player
/// stands at the target portal.
/// </summary>
public sealed class FieldPortals
{
    public sealed class Portal
    {
        public FieldEntity Entity;
        public int Id, TargetField, TargetPortal;
        public bool Visible, Enabled;
        public Vec3 Position, Center, Half;
        public double RotZDeg;

        /// <summary>Unk_PortalBox::ContainsPoint (MS2 axes).</summary>
        public bool Contains(Vec3 p)
        {
            double a = -RotZDeg * Math.PI / 180;
            double dx = p.X - Center.X, dy = p.Y - Center.Y;
            double lx = dx * Math.Cos(a) - dy * Math.Sin(a), ly = dx * Math.Sin(a) + dy * Math.Cos(a);
            return Math.Abs(lx) <= Half.X && Math.Abs(ly) <= Half.Y && Math.Abs(p.Z - Center.Z) <= Half.Z;
        }
    }

    public readonly List<Portal> All = new();
    public int Masked, Hidden;

    /// <summary>
    /// The portals of <paramref name="map"/>. With <paramref name="masks"/>, the client's quest masks apply as for a fresh character
    /// (every quest untaken): CQuestMaskTable's questmaskportal rows and the portal's own MaskQuestID / MaskQuestState
    /// (HasPortalMask), and a portal failing its mask is hidden and disabled (SetShownAndGlow(false, false)). Field 2000499's
    /// portal 2 is always shown, as in the client. Portals the xblock itself keeps invisible are opened by a trigger or the server,
    /// so the sandbox keeps them closed.
    /// </summary>
    public FieldPortals(FieldMap map, int fieldId = 0, QuestMaskTable masks = null)
    {
        foreach (var e in map.Entities)
        {
            if (!e.HasMixin("Portal")) continue;
            var dim = e.Vector("PortalDimension", new Vec3(200, 200, 250));
            var off = e.Vector("PortalOffset");
            var pos = e.Position;
            All.Add(new Portal
            {
                Entity = e,
                Id = (int)e.Number("PortalID"),
                TargetField = (int)e.Number("TargetFieldSN"),
                TargetPortal = (int)e.Number("TargetPortalID"),
                Visible = e.Bool("IsVisible", true),
                Enabled = e.Bool("PortalEnable", true),
                Position = pos,
                Center = new Vec3(pos.X + off.X, pos.Y + off.Y, pos.Z + off.Z),
                Half = new Vec3(dim.X / 2, dim.Y / 2, dim.Z / 2),
                RotZDeg = e.RotationDeg.Z,
            });
        }
        if (masks == null) return;
        foreach (var p in All)
        {
            if (fieldId == 2000499 && p.Id == 2) continue;
            var own = QuestMaskTable.FromEntity(p.Entity.Get("MaskQuestID"), p.Entity.Get("MaskQuestState"));
            if (masks.HasPortalMask(fieldId, p.Id, own) && !masks.CheckPortal(fieldId, p.Id, own, QuestMaskTable.NoQuests))
            { p.Visible = false; p.Enabled = false; Masked++; }
            else if (!p.Visible && p.Enabled) { p.Enabled = false; Hidden++; }
        }
    }

    public Portal ById(int id) => All.FirstOrDefault(p => p.Id == id);

    /// <summary>PortalAction::vfunc_1: of the portals containing the point, nearest first, the first enabled one.</summary>
    public Portal At(Vec3 p) =>
        All.Where(x => x.Enabled && x.TargetField != 0 && x.Contains(p))
           .OrderBy(x => Dist2(x.Position, p)).FirstOrDefault();

    static double Dist2(Vec3 a, Vec3 b) { var d = a - b; return d.X * d.X + d.Y * d.Y + d.Z * d.Z; }

    /// <summary>Godot point -> MS2 point (inverse of FieldBuilder.ToGodotPoint).</summary>
    public static Vec3 ToMs2(Vector3 g) => new(g.X, -g.Z, g.Y);
}
