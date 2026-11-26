#if MS2_DEVTOOLS
using System;
using System.Text.Json.Nodes;
using Godot;
using Ms2.Devtools;

namespace Ms2Godot;

public partial class DevEndpoint
{
    /// <summary>
    /// creative {action=toggle|open|close|search|tab|spawn|status, search, tab, spawn, amount, rarity, stack}: the debug item
    /// browser (game/devtools/CreativeMenu.cs). "spawn" sends the same chat line a click does and returns it with the
    /// menu's state; every other action returns the state, so a run can open it, filter it and spawn without the mouse.
    /// The catalog is built on the first open (once per pack set).
    /// </summary>
    JsonNode Creative(Request r)
    {
        var menu = CreativeMenu.Current;
        if (menu == null || !GodotObject.IsInstanceValid(menu))
            return new JsonObject { ["error"] = "no creative menu in this game (it needs MS2_DEBUG=1 and a built HUD)" };
        string action = r.GetString("action", "status");
        switch (action)
        {
            case "open": menu.Open(); break;
            case "close": menu.Close(); break;
            case "toggle": menu.Toggle(); break;
            case "search": menu.SetSearch(r.GetString("search", "")); break;
            case "tab": menu.SetTab(r.GetInt("tab", -1)); break;
            case "spawn":
                string line = menu.SpawnId(r.GetInt("spawn", 0), r.GetInt("amount", 1), r.GetInt("rarity", 0), r.GetBool("stack", false));
                return new JsonObject { ["line"] = line, ["sent"] = line != null, ["state"] = menu.StatusJson() };
            case "status": break;
            default: throw new ArgumentException("unknown creative action " + action);
        }
        return menu.StatusJson();
    }
}
#endif
