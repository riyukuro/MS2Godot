using Godot;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2Godot.Net;
using Ms2Godot.Ui;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/name_tag_symbol.tscn) of the Insignia packet on a player's name tag (CSceneStateGame::OnNametag,
/// scenestategame.cpp:7314-7331; the tag's setData again, FUN_141a5ff60):
/// - a player added with no symbol shown shows none; a symbol shown builds that symbol's mark;
/// - another symbol replaces the mark (the old one goes); the symbol hidden hides m_symbol and keeps the mark, and shown again
///   shows the same mark (NameTag.setSymbol);
/// - the player's level up and adventure level up (PC::vfunc_131 / vfunc_140's tag refresh) set the symbol line again with them;
/// - the Display Player Symbol option turned off hides m_symbol on the next refresh, and turned on shows it again;
/// - the field's property 5 (team 1 / 2 its names) and a PvP mode 3 team (red_team / blue_team) show before the insignia and
///   without its shown flag; property 5 applied sets the lines again;
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
        // the field's property 5 and the PvP mode 3 team symbols come before the insignia, without its shown flag
        // (FUN_141a5ff60, uinametag.cpp:480-511); property 5 applied sets every player's line again (FUN_141bcabf0)
        (string, string)? field = null;
        tags.FieldSymbol = () => field;
        var red = new Node3D { Name = "Red" }; var redModel = new Node3D { Name = "Model" };
        AddChild(red); red.AddChild(redModel);
        tags.AddPc(red, redModel, "Model", new FieldAddUser
        {
            ObjectId = 8, User = new UserBlock { CharacterId = 80, Name = "Red", Level = 50, PrestigeLevel = 10 },
            Insignia = (short)(ids.Count > 0 ? ids[0] : 0), InsigniaShown = false, Team = 1, PvpPoint = 0xc9, PvpMode = 3,
        }, false);
        var blue = new Node3D { Name = "Blue" }; var blueModel = new Node3D { Name = "Model" };
        AddChild(blue); blue.AddChild(blueModel);
        tags.AddPc(blue, blueModel, "Model", new FieldAddUser
        {
            ObjectId = 9, User = new UserBlock { CharacterId = 90, Name = "Blue", Level = 50, PrestigeLevel = 10 },
            Insignia = 0, InsigniaShown = false, Team = 2, PvpPoint = 0xca, PvpMode = 3,
        }, false);
        var other = new Node3D { Name = "Other" }; var otherModel = new Node3D { Name = "Model" };
        AddChild(other); other.AddChild(otherModel);
        tags.AddPc(other, otherModel, "Model", new FieldAddUser
        {
            ObjectId = 10, User = new UserBlock { CharacterId = 100, Name = "Other", Level = 50, PrestigeLevel = 10 },
            Insignia = 0, InsigniaShown = false, Team = 1, PvpPoint = 0xc9, PvpMode = 2,
        }, false);
        Check(tags.ShownMark(red) == "nametag_mark_red_team", $"the mode 3 point 0xc9 player shows {tags.ShownMark(red)}");
        Check(tags.ShownMark(blue) == "nametag_mark_blue_team", $"the mode 3 point 0xca player shows {tags.ShownMark(blue)}");
        Check(tags.ShownMark(other) == null, $"a mode 2 player shows {tags.ShownMark(other)}");
        Check(tags.ShownSymbol(red) == 0, $"a team symbol counted as the insignia {tags.ShownSymbol(red)}");
        field = ("blue_team", "red_team");
        Check(tags.ShownMark(red) == "nametag_mark_red_team", "the line changed before the property applied");
        tags.RefreshFieldSymbols();
        Check(tags.ShownMark(red) == "nametag_mark_blue_team", $"team 1 with property 5 shows {tags.ShownMark(red)}, want its first name");
        Check(tags.ShownMark(blue) == "nametag_mark_red_team", $"team 2 with property 5 shows {tags.ShownMark(blue)}, want its second name");
        Check(tags.ShownMark(other) == "nametag_mark_blue_team", $"team 1 outside the PvP with property 5 shows {tags.ShownMark(other)}");
        if (ids.Count > 0) Check(tags.ShownSymbol(root) == ids[0], $"the team 0 player's insignia changed: {tags.ShownSymbol(root)}");
        Check(NameTagSymbolLine.FieldOrTeam(("a", "b"), 0, 0xc9, 3) == "red_team", "team 0 with property 5 skips to the PvP team");
        Check(NameTagSymbolLine.FieldOrTeam(null, 1, 0xcb, 3) == null, "a mode 3 point other than 0xc9/0xca has a team symbol");
        field = null;
        tags.RefreshFieldSymbols();
        // the own tag (the MyPC's UINameTag entity) takes the same line
        var own = new PlayerNameTag(new UiAssets(packs), Ms2.Ui.FeatureElements.Constants(packs), "Me", new Vector2(1280, 720));
        AddChild(own);
        own.SetSymbol(0, false, 50, 10, NameTagSymbolLine.FieldOrTeam(("blue_team", "red_team"), 2, 0, 0));
        Check(own.ShownMark == "nametag_mark_red_team", $"the own tag of team 2 with property 5 shows {own.ShownMark}");
        own.SetSymbol(0, false, 50, 10, null);
        Check(own.ShownMark == null, $"the own tag without a team symbol or insignia shows {own.ShownMark}");
        Check(tags.ShownMark(red) == "nametag_mark_red_team" && tags.ShownMark(other) == null, "without property 5 the PvP team again");
        Check(!tags.SetSymbol(new Node3D(), 1, true), "a root with no tag was changed");
        Check(!tags.SetLevels(new Node3D(), 1, 1), "a root with no tag took a level");
        GD.Print($"name tag symbol: symbols {string.Join(",", ids)}, level symbol {levelId}");
        GD.Print($"name tag symbol: {(fails.Count == 0 ? "ok" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
