using Ms2.Avm2;

namespace Ms2.Ui.Hud;

/// <summary>
/// The trigger's SetVisibleUI (type 0x14, CTriggerService::FUN_141862d30 @0x141862d30, triggerservice.cpp:707-722): the
/// state byte and a comma list of UI object names; each name CUIService::FindObjectByName finds (uiservice.cpp:1726
/// @0x141adcf50) whose vfunc_9 is not the state gets vfunc_8(state). A name with no object is skipped.
/// </summary>
public static class TriggerVisibleUi
{
    /// <summary>UIMessengerBrowser::GetTypeName @0x141a21a60 (uimessenger.cpp:2721): the chat windows' owner, which is no
    /// movie of its own here (the HUD's <see cref="MessengerBrowser"/> holds the windows).</summary>
    public const string MessengerBrowserName = "MessengerBrowser";

    /// <summary>Unk_SplitA(strNames, ",") (xmlattr.h:54, CString::Tokenize): the non-empty tokens, nothing trimmed.</summary>
    public static string[] Names(string list) =>
        string.IsNullOrEmpty(list) ? Array.Empty<string>() : list.Split(',', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// UISwfEntity::vfunc_9 @0x1422dbcc0 (uiswfentity.cpp:193; UIDialog's @ uidialog.cpp:480 only forwards): the root's
    /// visibility when the root is an ASMovieClip (<see cref="UiController.LoadSetsVisibility"/>) and loaded, else 0.
    /// </summary>
    public static int Visible(UiController c) =>
        c?.Host?.Entity != null && c.LoadSetsVisibility && Rt.ToBoolean(c.Host.Get(c.Host.Entity, "visible")) ? 1 : 0;

    /// <summary>
    /// UISwfEntity::vfunc_8 @0x1422dbc20 (uiswfentity.cpp:184; UIDialog's @ uidialog.cpp:473 forwards to it): the root
    /// ASMovieClip's visibility (nothing for an unloaded movie or another root), then the subclass's own part
    /// (<see cref="UiController.SetEntityVisible"/>).
    /// </summary>
    public static void SetVisible(UiController c, int state)
    {
        if (c?.Host?.Entity == null || !c.LoadSetsVisibility) return;
        c.Host.SetVisible(c.Host.Entity, state != 0);
        c.SetEntityVisible(state != 0);
    }

    /// <summary>UIMessengerBrowser::vfunc_9 @0x141a21c50 (uimessenger.cpp:2751): 1 when any window answers 1.</summary>
    public static int Visible(MessengerBrowser b)
    {
        for (int i = 0; i < b.Windows.Count; i++)
            if (Visible(b.Windows[i]) != 0) return 1;
        return 0;
    }

    /// <summary>UIMessengerBrowser::vfunc_8 @0x141a21ce0 (uimessenger.cpp:2762): every window's vfunc_8.</summary>
    public static void SetVisible(MessengerBrowser b, int state)
    {
        for (int i = 0; i < b.Windows.Count; i++) SetVisible(b.Windows[i], state);
    }

    /// <summary>
    /// FindObjectByName over the HUD's objects: the map (m_mapC, case blind: CStringElementTraitsI, uiservice.h:240) holds
    /// one object per GetTypeName (RegisterObject @0x141aebfa0, uiservice.cpp:1766). Here the loaded movies' controllers
    /// by <see cref="UiController.TypeName"/>, and the messenger browser by its name. Null for a name no object here has.
    /// </summary>
    public static object Find(string name, IReadOnlyList<UiController> movies, MessengerBrowser browser)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (browser != null && string.Equals(name, MessengerBrowserName, StringComparison.OrdinalIgnoreCase)) return browser;
        for (int i = 0; i < movies.Count; i++)
            if (string.Equals(movies[i].TypeName, name, StringComparison.OrdinalIgnoreCase)) return movies[i];
        return null;
    }

    /// <summary>
    /// FUN_141862d30's loop (triggerservice.cpp:712-721): for each name, the found object's vfunc_9 against the state
    /// (an int compare of the byte), then vfunc_8(state) when they differ. Returns the names that found an object.
    /// </summary>
    public static List<string> Apply(int state, string names, IReadOnlyList<UiController> movies, MessengerBrowser browser)
    {
        var found = new List<string>();
        foreach (var name in Names(names))
        {
            switch (Find(name, movies, browser))
            {
                case MessengerBrowser b:
                    found.Add(name);
                    if (Visible(b) != state) SetVisible(b, state);
                    break;
                case UiController c:
                    found.Add(name);
                    if (Visible(c) != state) SetVisible(c, state);
                    break;
            }
        }
        return found;
    }
}
