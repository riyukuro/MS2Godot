using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Ui.Hud;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>The live minimap's portal marks are WorldService's portal proxies: the portals the server registered.</summary>
public partial class HudTests
{
    const int PortalField = 2000001;

    static FieldEntity PortalXblock(int id, int targetField, Vec3 at)
    {
        var raw = new XEntity { Id = "e" + id, ModelName = "PortalModel", Name = "portal" + id };
        raw.Props["PortalID"] = id.ToString();
        raw.Props["Position"] = $"{at.X}, {at.Y}, {at.Z}";
        raw.Props["TargetFieldSN"] = targetField.ToString();
        var flat = new FlatModel { Name = "PortalModel" };
        flat.Mixins.Add("Portal");
        return new FieldEntity { Raw = raw, Flat = flat };
    }

    static FieldPortalMessage PortalAdd(int id, byte type = 0, int target = 0, bool minimap = true) => new()
    {
        Mode = 0, Id = id, Type = type, TargetMap = target, Visible = true, Enabled = true, Minimap = minimap,
        Position = new System.Numerics.Vector3(300, 400, 0), Dimension = new System.Numerics.Vector3(200, 200, 250),
    };

    [Test]
    public void MinimapMarksAreTheServerRegisteredPortals()
    {
        // WorldService::RegisterPortal ends with AddProxyObject (worldservice_1.cpp:9106-9110) and RemoveFieldObject with
        // RemoveProxyObject (:4054): the minimap's AddProxy / RemoveProxy. BuildEntities (uiminimapentity.cpp:2900) walks only
        // those proxies, so an xblock portal the server never registers has no mark and a spawned one has its mark.
        var c = new MinimapController { ImageName = "map/minimap/02000001.dds", LocalPlayer = () => (default, 90f, false), Viewport = NpcView };
        Model();
        var icons = new MinimapIcons(packs, new NpcTable(packs, "en"), new GameStrings(packs, "en"),
            new Dictionary<int, List<int>>(), Array.Empty<int>());
        c.Marks.Add(new() { Entity = MinimapIcons.Taxi(false, false), Position = () => default, IsTaxi = true });

        var portals = new LivePortals(PortalField, null, null, null);
        portals.Attach(new[] { PortalXblock(1, 2000002, new Vec3(10, 20, 0)), PortalXblock(2, 2000003, new Vec3(30, 40, 0)), PortalXblock(3, PortalField, default) });
        portals.Apply(PortalAdd(1));                            // target 0: the xblock's TargetFieldSN
        portals.Apply(PortalAdd(9, type: 4, target: 2000004));  // spawned: no xblock entity
        portals.Apply(PortalAdd(3));                            // into the field itself: UpdatePortal makes no entity
        var unwatch = c.WatchPortals(portals, icons, PortalField);
        int[] Ids() => c.Marks.Where(m => m.HasPortal).Select(m => m.PortalId).ToArray();
        Assert.That(Ids(), Is.EqualTo(new[] { 1, 9 }), "portal 2 is the xblock's only");
        Assert.That(c.Marks[^1].IsTaxi, Is.True, "the portals come before the other marks");
        var one = c.Marks.First(m => m.PortalId == 1);
        Assert.That(one.Entity.Label, Is.EqualTo(icons.MapName(2000002)), "the xblock's TargetFieldSN");
        Assert.That(one.Position(), Is.EqualTo(new Vec3(10, 20, 0)));
        var nine = c.Marks.First(m => m.PortalId == 9);
        Assert.That(nine.Entity.Label, Is.EqualTo(icons.MapName(2000004)));
        Assert.That(nine.Position(), Is.EqualTo(new Vec3(300, 400, 0)));

        var h = Load(c, Model());
        Assert.That(c.OnCallback("onFieldLoadComplete", new object[] { c.FieldClip }), Is.True);
        Assert.That(c.Marks.All(m => m.Clip != null), Is.True);
        Assert.That(c.PortalMarkShown(1), Is.True);
        Assert.That(c.PortalMarkShown(2), Is.Null);

        // a later register adds its proxy on the built field
        portals.Apply(PortalAdd(2));
        Assert.That(Ids(), Is.EqualTo(new[] { 1, 9, 2 }));
        Assert.That(c.PortalMarkShown(2), Is.True);
        Assert.That(c.Marks[^1].IsTaxi, Is.True);
        // mode 1 takes the proxy away; a register brings it back
        portals.Apply(new FieldPortalMessage { Mode = 1, Id = 9 });
        Assert.That(Ids(), Is.EqualTo(new[] { 1, 2 }));
        Assert.That(c.PortalMarkShown(9), Is.Null);
        portals.Apply(PortalAdd(9, type: 4, target: 2000004));
        Assert.That(Ids(), Is.EqualTo(new[] { 1, 2, 9 }));
        Assert.That(c.PortalMarkShown(9), Is.True);
        // a portal without the minimap flag keeps its proxy, hidden (FUN_14169d1d0)
        portals.Apply(new FieldPortalMessage { Mode = 1, Id = 1 });
        portals.Apply(PortalAdd(1, minimap: false));
        Assert.That(c.PortalMarkShown(1), Is.False);

        unwatch();
        portals.Apply(new FieldPortalMessage { Mode = 1, Id = 2 });
        portals.Apply(PortalAdd(5, type: 4, target: 2000004));
        Assert.That(Ids(), Is.EqualTo(new[] { 2, 9, 1 }), "unwatched: no change");
        Assert.That(h.RT.Errors, Is.Empty);
    }
}
