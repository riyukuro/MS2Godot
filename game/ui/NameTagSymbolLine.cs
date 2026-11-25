using Godot;
using Ms2.Ui;

namespace Ms2Godot.Ui;

/// <summary>
/// A player's name tag symbol line (m_symbol), for the own tag (<see cref="PlayerNameTag"/>) and the other players'
/// (Net.LiveNameTags). UINameTag::Entity::FUN_141a5ff60 (uinametag.cpp:515-530) sets "symbol" only with the insignia shown
/// (PC m_n10fc), to the symbol table's icon (GetIconRanked), and "level" for a level symbol (condition 1: the PC's level;
/// 0xb: the adventure level); then NameTag.setSymbol (uinametag.gfx):
/// - no symbol: m_symbol hides and its mark stays;
/// - the class nametag_mark_&lt;symbol&gt; through getDefinitionByName, which looks in every movie of the shared root, so
///   commonNameTag's own classes resolve though uinametag imports only Namemark and Nameplate from it; a class not found
///   leaves the mark as it is;
/// - the same class as the mark: only its text; another class: the mark is replaced; the text is the level when the
///   symbol's name holds "level";
/// - m_symbol shows per visibleSymbol (the Display Player Symbol option) at y -88 under a guild line, else -66.
/// The inputs (the symbol, its shown flag, the levels, the guild line and the option) are the line's key: the PC's level and
/// adventure level setters refresh the tag (PC::vfunc_131 / vfunc_140 -> FUN_141a60f00, pc.cpp), and the tag layers set it
/// again when the option changed since (<see cref="OptionChanged"/>).
/// Before the insignia FUN_141a5ff60 (uinametag.cpp:480-511) takes the field's property 5 symbol for a PC of team 1 or 2,
/// else a PvP mode 3 team's red_team / blue_team (<see cref="FieldOrTeam"/>); either shows without the shown flag.
/// STILL-OPEN: an enchant symbol (condition 2) needs the equipment's grade and rank (PC::FUN_141661e40) and is left out.
/// </summary>
public sealed class NameTagSymbolLine
{
    const string Library = "commonNameTag";
    string markClass;
    ushort markSymbol;
    (ushort Id, bool Shown, int Level, int AdventureLevel, bool GuildLine, bool Visible, string Special)? last;

    /// <summary>
    /// FUN_141a5ff60's symbols before the insignia (uinametag.cpp:480-511): with a field property 5 (FindFieldProperty(5)),
    /// team 1 (PCBase::FUN_141678320, m_unk_af8) takes its first name and team 2 its second; else, in a PvP of mode 3
    /// (CPvPInfo::IsMode3), the point 0xc9 is "red_team" and 0xca "blue_team". Null for none (the insignia's turn).
    /// </summary>
    public static string FieldOrTeam((string Team1, string Team2)? field, byte team, int pvpPoint, byte pvpMode)
    {
        if (field is { } f)
        {
            if (team == 1) return f.Team1 ?? "";
            if (team == 2) return f.Team2 ?? "";
        }
        if (pvpMode == 3)
        {
            if (pvpPoint == 0xc9) return "red_team";
            if (pvpPoint == 0xca) return "blue_team";
        }
        return null;
    }

    /// <summary>visibleSymbol: the Display Player Symbol option (on without the option store).</summary>
    static bool OptionShown => GameOptions.Current?.StatusOption?.DisplayPlayerSymbol ?? true;

    /// <summary>Whether the option changed since the line was last set.</summary>
    public bool OptionChanged => last is { } k && k.Visible != OptionShown;

    /// <summary>The line set again with its last inputs (after an option change).</summary>
    public void Refresh(MovieClipNode node, UiMovie movie, double scale, NametagSymbols table)
    {
        if (last is { } k) Set(node, movie, scale, table, k.Id, k.Shown, k.Level, k.AdventureLevel, k.GuildLine, k.Special);
    }

    /// <summary>The levels the line was last set with ((-1, -1) before any); the checks' view.</summary>
    public (int Level, int AdventureLevel) SetLevels => last is { } k ? (k.Level, k.AdventureLevel) : (-1, -1);

    /// <summary>The mark class the line shows while m_symbol is visible (nametag_mark_...), else null.</summary>
    public string ShownMark(MovieClipNode node) => markClass != null && node?.FindClip("m_symbol") is { Visible: true } ? markClass : null;

    /// <summary>The symbol the line shows: the insignia its mark was built for while m_symbol is visible, else 0 (0 too for a
    /// field or team symbol).</summary>
    public ushort Shown(MovieClipNode node) =>
        markClass != null && node?.FindClip("m_symbol") is { Visible: true } ? markSymbol : (ushort)0;

    /// <summary>setData's symbol part for the player's insignia, or for <paramref name="special"/> (<see cref="FieldOrTeam"/>)
    /// when set; the same inputs again change nothing.</summary>
    public void Set(MovieClipNode node, UiMovie movie, double scale, NametagSymbols table, ushort id, bool shown, int level,
                    int adventureLevel, bool guildLine, string special = null)
    {
        bool visible = OptionShown;
        var key = (id, shown, level, adventureLevel, guildLine, visible, special);
        if (last == key || node == null) return;
        last = key;
        var symbol = node.FindClip("m_symbol");
        if (symbol == null) return;
        string icon, text;
        if (special != null)
        {
            // no "level" member is set for these: int(undefined)
            icon = special;
            text = icon.Contains("level", StringComparison.OrdinalIgnoreCase) ? "0" : "";
        }
        else if (!shown || table?.Get(id) is not { } sym || sym.Condition == NametagSymbols.Condition.Enchant)
        {
            node.SetNamedVisible("m_symbol", false);
            return;
        }
        else
        {
            var player = new NametagSymbols.Player(level, adventureLevel, 0, Array.Empty<uint>(), 0);
            icon = table.IconRanked(id, player);
            text = !icon.Contains("level", StringComparison.OrdinalIgnoreCase) ? "" : sym.Condition switch
            {
                NametagSymbols.Condition.Level => level.ToString(System.Globalization.CultureInfo.InvariantCulture),
                NametagSymbols.Condition.AdventureLevel => adventureLevel.ToString(System.Globalization.CultureInfo.InvariantCulture),
                _ => "0",                                   // int(undefined)
            };
        }
        string cls = "nametag_mark_" + icon;
        if (symbol.FindClip("m_mark") is { } mark && movie != null)
        {
            if (markClass == cls && mark.GetChildCount() > 0)
            {
                if (text.Length > 0 && mark.GetChild(0) is MovieClipNode first) first.SetText(text);
                markSymbol = special != null ? (ushort)0 : id;
            }
            else if ((movie.BuildClass(cls, scale) ?? movie.External(Library)?.BuildClass(cls, scale)) is { } built)
            {
                for (int i = mark.GetChildCount() - 1; i >= 0; i--) { var c = mark.GetChild(i); mark.RemoveChild(c); c.QueueFree(); }
                if (text.Length > 0) built.SetText(text);
                mark.AddChild(built);
                markClass = cls; markSymbol = special != null ? (ushort)0 : id;
            }
        }
        node.SetNamedVisible("m_symbol", visible);
        symbol.Position = new Vector2(symbol.Position.X, guildLine ? -88 : -66);
    }
}
