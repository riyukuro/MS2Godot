using System.Xml.Linq;
using Ms2.Avm2;

namespace Ms2.Ui;

/// <summary>
/// UIAlignWidgetManager (gfxui/uialignwidget.cpp): table/alignwidget.xml's widgets, each a row of entities (by type
/// name) that one after another take the widget's align type and offset, each shown one moving the offset on by its
/// width. The entities are the loaded controllers (UISwfEntityManager::FindEntityByTypeName).
/// </summary>
public sealed class AlignWidgetManager
{
    sealed class Widget
    {
        public int OffsetX, OffsetY, AlignType;
        public readonly List<string> Entities = new();   // m_listEntities

        /// <summary>FUN_1422e65a0 (@0x1422e65a0): the entity is listed and loaded; unless any, not one the player moved
        /// (vfunc_6, UIDialog m_74).</summary>
        public bool Holds(AlignWidgetManager m, string name, bool any)
        {
            if (!Entities.Contains(name)) return false;
            var e = m.Find(name);
            if (e == null) return false;
            return any || e.EditAlign == 0;
        }

        /// <summary>LayoutEntities (@0x1422e66a0): each loaded (m_54) and visible (vfunc_9) entity that the widget holds
        /// is placed by vfunc_44 at the running offset; align types 0, 2, 4, 6, 8 move it left by the entity's width,
        /// 3, 5, 7, 9 right.</summary>
        public void Layout(AlignWidgetManager m, bool any)
        {
            int x = OffsetX, y = OffsetY;
            foreach (var name in Entities)
            {
                var e = m.Find(name);
                if (e?.Host?.Entity == null || !Rt.ToBoolean(e.Host.Get(e.Host.Entity, "visible"))) continue;
                if (!Holds(m, e.TypeName, any)) continue;
                int w = e.AlignTo(AlignType, x, y);
                switch (AlignType)
                {
                    case 0: case 2: case 4: case 6: case 8: x -= w; break;
                    case 3: case 5: case 7: case 9: x += w; break;
                }
            }
        }
    }

    readonly List<Widget> widgets = new();
    readonly List<UiController> entities = new();
    bool pending, pendingAny;    // m_bLayoutPending, m_nLayoutArg

    /// <summary>LoadWidgets (@0x1422e6960): each &lt;widget offsetX offsetY alignType&gt; with its named &lt;entity&gt; rows,
    /// a name listed once.</summary>
    public static AlignWidgetManager Load(XElement root)
    {
        var m = new AlignWidgetManager();
        if (root == null || root.Name != "ms2") return m;
        foreach (var w in root.Elements("widget"))
        {
            var g = new Widget
            {
                OffsetX = (int?)w.Attribute("offsetX") ?? 0,
                OffsetY = (int?)w.Attribute("offsetY") ?? 0,
                AlignType = (int?)w.Attribute("alignType") ?? 0,
            };
            foreach (var e in w.Elements("entity"))
            {
                var name = (string)e.Attribute("name");
                if (!string.IsNullOrEmpty(name) && !g.Entities.Contains(name)) g.Entities.Add(name);
            }
            m.widgets.Add(g);
        }
        return m;
    }

    UiController Find(string typeName) => entities.FirstOrDefault(e => e.TypeName == typeName && e.Host?.Entity != null);

    /// <summary>A loaded entity (UISwfEntityManager's loaded list).</summary>
    public void Add(UiController e) { if (!entities.Contains(e)) entities.Add(e); }
    public void Remove(UiController e) => entities.Remove(e);

    /// <summary>HasWidgetForEntity (@0x1422e6bf0).</summary>
    public bool HasWidgetForEntity(UiController e, bool any) => e != null && widgets.Any(w => w.Holds(this, e.TypeName, any));

    /// <summary>RequestLayout (@0x1422e6d00): laid out on the next update.</summary>
    public void RequestLayout(bool any) { pending = true; pendingAny = any; }

    /// <summary>ProcessPendingLayout (@0x1422e6d30), at the end of UISwfEntityManager::UpdateEntities each frame.</summary>
    public void ProcessPendingLayout()
    {
        if (!pending) return;
        foreach (var w in widgets) w.Layout(this, pendingAny);     // LayoutAllWidgets
        pending = false;
        pendingAny = false;
    }
}
