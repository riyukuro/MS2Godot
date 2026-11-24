namespace Ms2.Formats.World;

/// <summary>
/// The client's CReservedWordTable: the "$name:argument$" words of game texts and the formatters behind them.
/// <list type="bullet">
/// <item>Replace (FUN_1416fc320): scans from the start for a "$", takes the text up to the next "$" as the token and looks
/// up its name (up to the first ":", else without the closing "$"). A known word is replaced by its formatter's output and
/// the scan goes on at the same place, so what it inserted is read again. An unknown one is skipped past its closing "$".
/// The formatter gets the token itself as its starting output, the argument (after the ":", without the closing "$") and
/// the whole text as it is then.</item>
/// <item>Object words (ReplaceObjectWords), run on npc script text when it is loaded (CScriptContent::vfunc_2 and its
/// distractors): $script (the script string), $map, $item, $skill, $npcName, $npcTitle, $npcFunction and $achieve (string
/// tables 3, 1, 4, 2, 19, 10 and 24), $npc ("title name", or the name when the npc has no title), $pp and $pp2, and the plural words $itemPlural,
/// $npcPlural and $npcNamePlural: in Korean, Japanese and Chinese (CGameOption m_language 0, 1, 3) the same as $item, $npc
/// and $npcName; in any other language the plural tables (62 itemNamePlural, 61 npcNamePlural; $npcPlural is "title
/// plural", FUN_141700820).</item>
/// <item>Value words (ReplaceValueWords), run when the page is shown (CCinematicComponent::vfunc_5): $MyPCName, $male,
/// $female, $pp, $ppr and $pp2.</item>
/// </list>
/// Arguments are read as the client does: _wtoi (the leading digits, so "11000001[gender:1]" is 11000001) and, for $script,
/// _wcstoui64. A missing string is empty. Words whose formatter is not ported here ($quest, $maview, $externalWeb, $petCollectGroup, $masteryRecipe, $job; the value words
/// $MyPCHp, $MyPCLevel, $paneltyPrice, the roulette words and $key) stay as they are.
/// </summary>
public sealed class ReservedWords
{
    /// <summary>A formatter: (the token as the starting output, the argument, the whole text) -> the output.</summary>
    public delegate string Word(string token, string argument, string whole);

    readonly GameStrings text;
    readonly Func<ulong, string> script;
    readonly Dictionary<string, Dictionary<int, string>> tables = new(StringComparer.Ordinal);
    readonly Dictionary<string, Word> objectWords;
    readonly bool singular;

    /// <param name="text">The string tables.</param>
    /// <param name="script">The script strings (CScriptTable::GetScript), null for none.</param>
    public ReservedWords(GameStrings text, Func<ulong, string> script)
    {
        this.text = text;
        this.script = script;
        // the language of the first string table language (FUN_140652030: 0 KR, 1 JP, 2 EN, 3 CN, 4 FR, 5 DE, 6 PT, 7 ES)
        singular = (text?.Languages.FirstOrDefault() ?? "").ToLowerInvariant() is "kr" or "jp" or "cn";
        objectWords = new(StringComparer.Ordinal)
        {
            ["$item"] = (_, a, _) => Lookup("itemname", Atoi(a)),
            ["$map"] = (_, a, _) => Lookup("mapname", Atoi(a)),
            ["$skill"] = (_, a, _) => Lookup("skillname", Atoi(a)),
            ["$npc"] = (_, a, _) => NpcDisplayName(Atoi(a)),
            ["$npcTitle"] = (_, a, _) => Lookup("npctitle", Atoi(a)),
            ["$npcName"] = (_, a, _) => Lookup("npcname", Atoi(a)),
            ["$npcFunction"] = (_, a, _) => Lookup("npcfunctionname", Atoi(a)),
            ["$pp"] = Ppr,
            ["$pp2"] = Pp2,
            ["$achieve"] = (_, a, _) => Atoi(a) == 0 ? "" : Lookup("achievename", Atoi(a)),
            // Rw_DungeonTitle @0x1416fdc80: FUN_1416ff950 = string table 20 (stringFieldEnterance)
            ["$dungeonTitle"] = (_, a, _) => Atoi(a) == 0 ? "" : Lookup("stringfieldenterance", Atoi(a)),
            ["$script"] = (_, a, _) => ParseUInt64(a) == 0 ? "" : script?.Invoke(ParseUInt64(a)) ?? "",
            ["$itemPlural"] = (_, a, _) => Lookup(singular ? "itemname" : "itemnameplural", Atoi(a)),
            ["$npcPlural"] = (_, a, _) => singular ? NpcDisplayName(Atoi(a)) : NpcDisplayName(Atoi(a), "npcnameplural"),
            ["$npcNamePlural"] = (_, a, _) => Lookup(singular ? "npcname" : "npcnameplural", Atoi(a)),
        };
    }

    /// <summary>ReplaceObjectWords.</summary>
    public string ReplaceObjectWords(string s) => Replace(s, objectWords);

    /// <summary>The local player's gender (0 male, 1 female), null without a local player. Rw_Gender reads it from
    /// WorldService::GetLocalPlayer (reservedwordtable.cpp:803-805), so a text that has $male / $female words gets the
    /// own gender whichever window shows it.</summary>
    public Func<int?> LocalPlayerGender { get; set; }

    /// <summary>ReplaceValueWords, with the local player's name for $MyPCName and gender (0 male, 1 female) for $male and
    /// $female (Rw_Gender: empty without a player, a null gender). A null <paramref name="playerGender"/> is the local
    /// player's (<see cref="LocalPlayerGender"/>), as the client's Rw_Gender takes it.</summary>
    public string ReplaceValueWords(string s, string playerName, int? playerGender = null) => Replace(s, new Dictionary<string, Word>(StringComparer.Ordinal)
    {
        ["$MyPCName"] = (_, _, _) => playerName ?? "",
        ["$male"] = (t, _, _) => Gender(t, playerGender ?? LocalPlayerGender?.Invoke()),
        ["$female"] = (t, _, _) => Gender(t, playerGender ?? LocalPlayerGender?.Invoke()),
        ["$ppr"] = Ppr,
        ["$pp"] = Ppr,
        ["$pp2"] = Pp2,
    });

    /// <summary>
    /// Rw_Gender ($male, $female): the token without its "$" is split at ","; of the parts that split at ":" into exactly
    /// two, "male:x" (any case) picks x for gender 0 and "female:y" picks y for gender 1, the last pick winning. Empty when
    /// nothing is picked or there is no player.
    /// </summary>
    public static string Gender(string token, int? gender)
    {
        if (gender == null) return "";
        string pick = "";
        foreach (var item in Split(token.Replace("$", ""), ","))
        {
            var parts = Split(item, ":");
            if (parts.Length != 2) continue;
            if (string.Equals(parts[0], "male", StringComparison.OrdinalIgnoreCase)) { if (gender == 0) pick = parts[1]; }
            else if (string.Equals(parts[0], "female", StringComparison.OrdinalIgnoreCase) && gender == 1) pick = parts[1];
        }
        return pick;
    }

    /// <summary>FUN_1416fc320: the scan over the text with one word map.</summary>
    public static string Replace(string s, IReadOnlyDictionary<string, Word> words)
    {
        if (string.IsNullOrEmpty(s)) return s ?? "";
        int pos = 0;
        while ((pos = s.IndexOf('$', pos)) != -1)
        {
            int end = s.IndexOf('$', pos + 1);
            if (end == -1) break;
            string token = s.Substring(pos, end - pos + 1);
            if (words.TryGetValue(TokenName(token), out var word))
                s = s[..pos] + (word(token, TokenArgument(token), s) ?? "") + s[(end + 1)..];
            else
                pos = end + 1;
        }
        return s;
    }

    /// <summary>GetTokenName: up to the first ":", else all but the last character.</summary>
    public static string TokenName(string token)
    {
        int colon = token.IndexOf(':');
        return colon != -1 ? token[..colon] : token[..^1];
    }

    /// <summary>GetTokenArgument: after the first ":" up to the last character, empty without a ":".</summary>
    public static string TokenArgument(string token)
    {
        int colon = token.IndexOf(':');
        return colon != -1 ? token.Substring(colon + 1, Math.Max(0, token.Length - colon - 2)) : "";
    }

    /// <summary>
    /// Rw_Ppr ($pp, $ppr): the josa pair "a, b" picked by the word before the token. The whole text is split at "$" and " "
    /// (FUN_14025a680, CString::Tokenize: empty parts dropped); the first part after the first that equals the token
    /// without its "$" is the token, and the part before it, with its tags and "]" and ")" removed, is the word: its last
    /// character with a final consonant (GetFinalConsonantType 1) takes b, anything else a. Without such a part, or when
    /// the argument is not two forms, the output is the token without its "$".
    /// </summary>
    public static string Ppr(string token, string argument, string whole) => Josa(token, argument, whole, FinalConsonantType);

    /// <summary>Rw_Pp2 ($pp2): as $pp, with FUN_1413fab50, which counts a final rieul as none.</summary>
    public static string Pp2(string token, string argument, string whole) => Josa(token, argument, whole, FinalConsonantTypeRieul);

    static string Josa(string token, string argument, string whole, Func<char, int> final)
    {
        var words = Split(whole, "$ ");
        string output = token.Replace("$", "");
        for (int i = 0; i < words.Length; i++)
        {
            if (!string.Equals(output, words[i], StringComparison.Ordinal) || i == 0) continue;
            string word = StripTags(words[i - 1]).Replace("]", "").Replace(")", "");
            if (word.Length < 1) continue;
            var forms = Split(argument, ", ");
            if (forms.Length != 2) return output;
            return final(word[^1]) != 1 ? forms[0] : forms[1];
        }
        return output;
    }

    /// <summary>GetFinalConsonantType (0x1413faab0): 0 open syllable, 1 final consonant, 2 neither a syllable nor a digit.
    /// Digits 2, 4, 5 and 9 read as open.</summary>
    public static int FinalConsonantType(char c)
    {
        if (c >= 0xac00 && c <= 0xd7a3) return (c - 0xac00) % 28 == 0 ? 0 : 1;
        if (c >= '0' && c <= '9') return c is '2' or '4' or '5' or '9' ? 0 : 1;
        return 2;
    }

    /// <summary>FUN_1413fab50: as <see cref="FinalConsonantType"/>, with the final consonant 8 (rieul) read as open.</summary>
    public static int FinalConsonantTypeRieul(char c)
    {
        if (c >= 0xac00 && c <= 0xd7a3) return (c - 0xac00) % 28 is 0 or 8 ? 0 : 1;
        return FinalConsonantType(c);
    }

    /// <summary>StripTags (0x14083d2b0): removes each "&lt;...&gt;" from the first "&lt;" on, stopping at one with no "&gt;" after it.</summary>
    public static string StripTags(string s)
    {
        int open;
        while ((open = s.IndexOf('<')) != -1)
        {
            int close = s.IndexOf('>', open + 1);
            if (close == -1) break;
            s = s.Remove(open, close - open + 1);
        }
        return s;
    }

    /// <summary>FUN_14025a680 (CString::Tokenize over the separator characters).</summary>
    static string[] Split(string s, string separators) => s.Split(separators.ToCharArray(), StringSplitOptions.RemoveEmptyEntries);

    /// <summary>_wtoi: leading white space, a sign, then the digits up to the first other character (0 without digits).</summary>
    public static int Atoi(string s)
    {
        int i = 0;
        while (i < s.Length && (s[i] == ' ' || (s[i] >= '\t' && s[i] <= '\r'))) i++;
        bool neg = false;
        if (i < s.Length && (s[i] == '+' || s[i] == '-')) neg = s[i++] == '-';
        long v = 0;
        for (; i < s.Length && s[i] >= '0' && s[i] <= '9'; i++)
            v = Math.Min(v * 10 + (s[i] - '0'), (long)int.MaxValue + 1);
        v = neg ? -v : v;
        return v > int.MaxValue ? int.MaxValue : v < int.MinValue ? int.MinValue : (int)v;
    }

    /// <summary>_wcstoui64 base 10: leading white space, a sign, the digits (ulong.MaxValue past its range).</summary>
    public static ulong ParseUInt64(string s)
    {
        int i = 0;
        while (i < s.Length && (s[i] == ' ' || (s[i] >= '\t' && s[i] <= '\r'))) i++;
        bool neg = false;
        if (i < s.Length && (s[i] == '+' || s[i] == '-')) neg = s[i++] == '-';
        ulong v = 0;
        for (; i < s.Length && s[i] >= '0' && s[i] <= '9'; i++)
        {
            ulong d = (ulong)(s[i] - '0');
            if (v > (ulong.MaxValue - d) / 10) return ulong.MaxValue;
            v = v * 10 + d;
        }
        return neg ? unchecked(0 - v) : v;
    }

    /// <summary>FUN_1401aa6a0: "&lt;title&gt; &lt;name&gt;" (string tables 19 and 2), or the name alone when the npc has no title.</summary>
    public string NpcDisplayName(int id) => NpcDisplayName(id, "npcname");

    /// <summary>FUN_1401aa6a0 with the name from <paramref name="names"/> (FUN_141700820 takes it from npcNamePlural).</summary>
    string NpcDisplayName(int id, string names)
    {
        string title = Lookup("npctitle", id), name = Lookup(names, id);
        return title.Length != 0 ? $"{title} {name}" : name;
    }

    /// <summary>CStringTable::FUN_141811980: the string with the numeric id in the named table, empty when missing.</summary>
    public string Lookup(string table, int id)
    {
        if (!tables.TryGetValue(table, out var t))
        {
            t = new Dictionary<int, string>();
            foreach (var (k, v) in text.Table(table, "name"))
                if (int.TryParse(k, out int n) && !t.ContainsKey(n)) t[n] = v ?? "";
            tables[table] = t;
        }
        return t.GetValueOrDefault(id, "");
    }
}
