namespace Ms2Godot.Ui;

/// <summary>
/// Minimap entity icons as MinimapDialog.createEntity builds them (uiminimapdialog's AS): the class
/// "minimap_entity_" + mcname, played at the entity's frame label. Its m_hitarea becomes the icon's hitArea and is hidden
/// (the grey hit box is never drawn). The entity scale is 1 / the field's scale, and npcs that are only in the field data
/// (inFieldNpc) are drawn at alpha 0.7.
/// </summary>
public static class MinimapEntities
{
    public const string ClassPrefix = "minimap_entity_";

    /// <summary>createEntity({mcname, frame, inFieldNpc}): the icon, or null when the movie has no such class.</summary>
    public static MovieClipNode Create(UiMovie movie, string mcname, string frame, double scale, bool inFieldNpc = false)
    {
        var e = movie.BuildClass(ClassPrefix + mcname, scale);
        if (e == null) return null;
        if (!string.IsNullOrEmpty(frame) && e.HasLabel(frame)) e.GotoLabel(frame);
        e.HideNamed("m_hitarea");            // l4.hitArea = m_hitarea; m_hitarea.visible = false
        // The clips inside the icon are placed playing, and one without frame code never stops: it loops (the player's
        // pulse, 80 frames; the portal's turning swirl, minimap_entity_normalportal, 180 frames).
        foreach (var c in e.AllClips().Skip(1)) if (!c.FrameScripts) c.Loop();
        if (inFieldNpc) e.Modulate = new Godot.Color(1, 1, 1, 0.7f);     // getEntityAlpha
        return e;
    }
}
