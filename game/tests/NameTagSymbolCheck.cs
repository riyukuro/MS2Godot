using Godot;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2Godot.Net;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/name_tag_symbol.tscn) of the Insignia packet on a player's name tag (CSceneStateGame::OnNametag,
/// scenestategame.cpp:7314-7331; the tag's setData again, FUN_141a5ff60):
/// - a player added with no symbol shown shows none; a symbol shown builds that symbol's mark;
/// - another symbol replaces the mark (the old one goes); the symbol hidden hides m_symbol and keeps the mark, and shown again
///   shows the same mark (NameTag.setSymbol);
/// - the player's level up and adventure level up (PC::vfunc_131 / vfunc_140's tag refresh) set the symbol line again with them;
/// - the Display Player Symbol option turned off hides m_symbol on the next refresh, and turned on shows it again;
/// - an npc's tag and a root with no tag are not changed (false).
/// The two symbols are the first two of the table that are not enchant symbols and have an icon; their classes are commonNameTag's. Quits with 0 when all hold, else 1.
/// </summary>
public partial class NameTagSymbolCheck : Node3D
{
    public override void _Ready()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var tags = new LiveNameTags(packs);
        AddChild(tags);
        var fails = new System.Collections.Generic.List<string>();
        void Check(bool ok, string what) { if (!ok) fails.Add(what); }
        var table = Ms2.Ui.GameTables.Shared(packs, Ms2Godot.GameOptions.StringLanguage).NametagSymbols;
        var player = new Ms2.Ui.NametagSymbols.Player(50, 10, 0, System.Array.Empty<uint>(), 0);
        var ids = new System.Collections.Generic.List<ushort>();
        for (int id = 1; id < 1000 && ids.Count < 2; id++)
            if (table.Get((ushort)id) is { } s && s.Condition != Ms2.Ui.NametagSymbols.Condition.Enchant
                && !string.IsNullOrEmpty(table.IconRanked((ushort)id, player)))
                ids.Add((ushort)id);
        Check(ids.Count == 2, $"symbols with an icon: {ids.Count}");
        var root = new Node3D { Name = "Pc" }; var model = new Node3D { Name = "Model" };
        AddChild(root); root.AddChild(model);
        var add = new FieldAddUser
        {
            ObjectId = 7,
            User = new UserBlock { CharacterId = 70, Name = "Pc", Level = 50, PrestigeLevel = 10 },
            Insignia = 0, InsigniaShown = false,
        };
        tags.AddPc(root, model, "Model", add, false);
        Check(tags.Count == 1, "no tag for the player");
        Check(tags.ShownSymbol(root) == 0, "a symbol shown before any was set");
        if (ids.Count == 2)
        {
            Check(tags.SetSymbol(root, ids[0], true) && tags.ShownSymbol(root) == ids[0], $"symbol {ids[0]} not shown: {tags.ShownSymbol(root)}");
            Check(tags.SetSymbol(root, ids[1], true) && tags.ShownSymbol(root) == ids[1], $"symbol {ids[1]} not shown: {tags.ShownSymbol(root)}");
            Check(tags.MarkCount(root) == 1, $"marks after a second symbol: {tags.MarkCount(root)}");
            Check(tags.SetSymbol(root, ids[1], false) && tags.ShownSymbol(root) == 0, "the hidden symbol still shown");
            Check(tags.MarkCount(root) == 1, $"setSymbol with no symbol only hides m_symbol, the mark stays: {tags.MarkCount(root)}");
            Check(tags.SetSymbol(root, ids[1], true) && tags.ShownSymbol(root) == ids[1] && tags.MarkCount(root) == 1, "the same symbol again");
        }
        // the player's level and adventure level changes set the symbol line again with them (PC::vfunc_131 / vfunc_140 refresh
        // the tag). No symbol of either data set shows the level: the one level symbol's icon is the bare "level", and
        // commonNameTag has no nametag_mark_level class (setSymbol leaves the mark as it is), so the line's inputs are checked
        if (ids.Count > 0) tags.SetSymbol(root, ids[0], true);
        Check(tags.SymbolLevels(root) == (50, 10), $"the line's levels before: {tags.SymbolLevels(root)}");
        Check(tags.SetLevels(root, 51, 12) && tags.SymbolLevels(root) == (51, 12), $"the line's levels after the level up: {tags.SymbolLevels(root)}, want (51, 12)");
        ushort levelId = 0;
        for (int id = 1; id < 1000 && levelId == 0; id++)
            if (table.Get((ushort)id) is { Condition: Ms2.Ui.NametagSymbols.Condition.Level }) levelId = (ushort)id;
        // the Display Player Symbol option
        var status = GameOptions.Current?.StatusOption;
        Check(status != null, "no option store");
        if (status != null && ids.Count == 2)
        {
            bool was = status.DisplayPlayerSymbol;
            tags.SetSymbol(root, ids[0], true);
            status.DisplayPlayerSymbol = false;
            tags.RefreshSymbols();
            Check(tags.ShownSymbol(root) == 0, $"the option off still shows symbol {tags.ShownSymbol(root)}");
            status.DisplayPlayerSymbol = true;
            tags.RefreshSymbols();
            Check(tags.ShownSymbol(root) == ids[0], $"the option on again shows {tags.ShownSymbol(root)}, want {ids[0]}");
            status.DisplayPlayerSymbol = was;
            tags.RefreshSymbols();
        }
        Check(!tags.SetSymbol(new Node3D(), 1, true), "a root with no tag was changed");
        Check(!tags.SetLevels(new Node3D(), 1, 1), "a root with no tag took a level");
        GD.Print($"name tag symbol: symbols {string.Join(",", ids)}, level symbol {levelId}");
        GD.Print($"name tag symbol: {(fails.Count == 0 ? "ok" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
