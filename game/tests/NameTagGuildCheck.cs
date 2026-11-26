using Godot;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2Godot.Net;
using Ms2Godot.Ui;

namespace Ms2Godot.Tests;

/// <summary>
/// Headless check (res://tests/name_tag_guild.tscn) of the guild line on the players' name tags (PC::vfunc_129, pc.cpp:1511,
/// then FUN_141a60f00's refresh, setData's StringFormat("&lt;{0}&gt;", GetGuildName()), uinametag.cpp:475-476):
/// - another player added with a guild shows "&lt;guild&gt;", one without shows none;
/// - a guild name set on a tag (0x4d: a join or a rename) shows it, "" (0x4e: a leave or a break) clears it, and the
///   symbol line moves with it (m_symbol at y -88 over a guild line, -66 without);
/// - the own tag (the MyPC's entity) does the same: no guild line until one is set, then set, renamed and cleared;
/// - a root with no tag is not changed (false).
/// Quits with 0 when all hold, else 1.
/// </summary>
public partial class NameTagGuildCheck : Node3D
{
    public override void _Ready()
    {
        var packs = PackSet.Shared(Settings.DataDir);
        var tags = new LiveNameTags(packs);
        AddChild(tags);
        var fails = new System.Collections.Generic.List<string>();
        void Check(bool ok, string what) { if (!ok) fails.Add(what); }
        // a symbol with an icon, so the symbol line is placed
        var table = Ms2.Ui.GameTables.Shared(packs, GameOptions.StringLanguage).NametagSymbols;
        var level = new Ms2.Ui.NametagSymbols.Player(50, 10, 0, System.Array.Empty<uint>(), 0);
        ushort symbol = 0;
        for (int id = 1; id < 1000 && symbol == 0; id++)
            if (table.Get((ushort)id) is { } s && s.Condition != Ms2.Ui.NametagSymbols.Condition.Enchant
                && !string.IsNullOrEmpty(table.IconRanked((ushort)id, level)))
                symbol = (ushort)id;
        Check(symbol != 0, "no symbol with an icon");

        Node3D Add(string name, string guild, int objectId)
        {
            var root = new Node3D { Name = name }; var model = new Node3D { Name = "Model" };
            AddChild(root); root.AddChild(model);
            tags.AddPc(root, model, "Model", new FieldAddUser
            {
                ObjectId = objectId,
                User = new UserBlock { CharacterId = objectId * 10, Name = name, Level = 50, PrestigeLevel = 10, GuildName = guild },
                Insignia = (short)symbol, InsigniaShown = true,
            }, false);
            return root;
        }
        var nodes = new System.Collections.Generic.List<MovieClipNode>();
        float SymbolY(MovieClipNode n) => n?.FindClip("m_symbol")?.Position.Y ?? float.NaN;
        var a = Add("A", "Guild", 1);
        nodes.Add(tags.GetChild<MovieClipNode>(tags.GetChildCount() - 1));
        var b = Add("B", "", 2);
        nodes.Add(tags.GetChild<MovieClipNode>(tags.GetChildCount() - 1));
        Check(tags.ShownGuild(a) == "<Guild>", $"a player added in a guild shows '{tags.ShownGuild(a)}'");
        Check(tags.ShownGuild(b) == "", $"a player added outside a guild shows '{tags.ShownGuild(b)}'");
        Check(SymbolY(nodes[0]) == -88 && SymbolY(nodes[1]) == -66, $"symbol lines at {SymbolY(nodes[0])} / {SymbolY(nodes[1])}, want -88 / -66");

        Check(tags.SetGuild(b, "Joined") && tags.ShownGuild(b) == "<Joined>", $"a join shows '{tags.ShownGuild(b)}'");
        Check(SymbolY(nodes[1]) == -88, $"the symbol line after a join at {SymbolY(nodes[1])}");
        Check(tags.SetGuild(a, "Renamed") && tags.ShownGuild(a) == "<Renamed>", $"a rename shows '{tags.ShownGuild(a)}'");
        Check(tags.SetGuild(b, "") && tags.ShownGuild(b) == "", $"a leave shows '{tags.ShownGuild(b)}'");
        Check(SymbolY(nodes[1]) == -66, $"the symbol line after a leave at {SymbolY(nodes[1])}");
        Check(tags.SetGuild(a, null) && tags.ShownGuild(a) == "" && SymbolY(nodes[0]) == -66, $"a break shows '{tags.ShownGuild(a)}'");
        var stray = new Node3D();
        Check(!tags.SetGuild(stray, "X"), "a root with no tag was changed");
        stray.Free();

        // the own tag (the MyPC's UINameTag entity)
        var own = new PlayerNameTag(new UiAssets(packs), Ms2.Ui.FeatureElements.Constants(packs), "Me", new Vector2(1280, 720));
        AddChild(own);
        var ownNode = own.GetChild<MovieClipNode>(0);
        string OwnLine() => LiveNameTags.LineText(ownNode.FindClip("m_guild"));
        own.SetSymbol(symbol, true, 50, 10);
        Check(OwnLine() == "" && SymbolY(ownNode) == -66, $"the own tag before a guild: '{OwnLine()}' symbol at {SymbolY(ownNode)}");
        own.SetGuild("Guild");
        Check(OwnLine() == "<Guild>" && own.ShownGuild == "<Guild>" && SymbolY(ownNode) == -88,
            $"the own tag in a guild: '{OwnLine()}' symbol at {SymbolY(ownNode)}");
        own.SetGuild("Renamed");
        Check(OwnLine() == "<Renamed>", $"the own tag after a rename: '{OwnLine()}'");
        own.SetSymbol(symbol, true, 51, 10);
        Check(SymbolY(ownNode) == -88, $"a level up keeps the symbol over the guild line: {SymbolY(ownNode)}");
        own.SetGuild("");
        Check(OwnLine() == "" && SymbolY(ownNode) == -66, $"the own tag after a leave: '{OwnLine()}' symbol at {SymbolY(ownNode)}");

        GD.Print($"name tag guild: symbol {symbol}");
        GD.Print($"name tag guild: {(fails.Count == 0 ? "ok" : "FAIL\n  " + string.Join("\n  ", fails))}");
        GetTree().Quit(fails.Count == 0 ? 0 : 1);
    }
}
