using System.Text;
using System.Xml.Linq;
using Ms2.Formats.Pack;

namespace Ms2.Formats.Text;

/// <summary>
/// The client's text checker (CBanTextChecker): a length and character class scan, then word tables from the locale's
/// string folder (string/&lt;lang&gt;/reserveIDAll.xml, reserveIDAny.xml, reserveWord.xml, banWord.xml, ugcBanWord.xml,
/// startWithWord.xml, banWordAll.xml). Each table entry is "word" or "word,replacement", lower-cased; the lookups lower-case
/// the text as they go and skip &lt;...&gt; tags. Results and messages follow the client: 0 is OK, 1-3 are limit errors
/// (too short, too long, bad character), 5-9 are table hits; the message is a common string id with its {0} filled.
/// </summary>
public sealed class BanTextChecker
{
    /// <summary>The checks of CheckerOption.m_nTypes.</summary>
    [Flags]
    public enum Checks : uint
    {
        Limit = 1, ReserveIdAll = 2, ReserveIdAny = 4, ReserveWord = 8, BanWord = 0x10, UgcBanWord = 0x20, StartWith = 0x40, BanWordAll = 0x80,
    }

    /// <summary>CheckerLimit: length bounds and the character class rules.</summary>
    public readonly record struct Limit(int Min, int Max, bool AllowSpace, bool SkipTrimCheck, bool NoCharCheck, bool WideCountsTwice, bool AllowPunct);

    /// <summary>The client country (DAT_143ab0248) selects the extra letters ScanLimit accepts.</summary>
    public enum Country { Korea = 0, Region1 = 1, Region2 = 2, Region3 = 3 }

    sealed class Node
    {
        public readonly Dictionary<char, Node> Children = new();
        public int Depth;
        public bool WordEnd;
    }

    readonly Node banWord = new(), banWordAll = new(), reserveWord = new(), reserveIdAny = new(), ugcBanWord = new(), startWith = new();
    readonly HashSet<string> reserveIdAll = new(StringComparer.Ordinal);
    readonly Dictionary<int, int> charClass = new();
    readonly Func<string, string> commonString;

    public Country ClientCountry = Country.Korea;

    /// <param name="readTable">Returns a table file's xml text by file name (for example "banWord.xml"), or null.</param>
    /// <param name="commonString">Looks up a common string by its key (for example "s_ban_check_err_min_length"), or null.</param>
    public BanTextChecker(Func<string, string> readTable, Func<string, string> commonString = null)
    {
        this.commonString = commonString;
        LoadWords(readTable("banWord.xml"), banWord);
        LoadWords(readTable("reserveWord.xml"), reserveWord);
        foreach (var name in Names(readTable("reserveIDAll.xml"))) reserveIdAll.Add(name);
        LoadWords(readTable("reserveIDAny.xml"), reserveIdAny);
        LoadWords(readTable("ugcBanWord.xml"), ugcBanWord);
        LoadWords(readTable("startWithWord.xml"), startWith);
        LoadWords(readTable("banWordAll.xml"), banWordAll);
    }

    /// <summary>The checker over the pack's Xml string folder for <paramref name="lang"/> (en, kr, ...).</summary>
    public static BanTextChecker FromPacks(PackSet packs, string lang)
    {
        string Read(string entry)
        {
            entry = entry.ToLowerInvariant();
            return packs.Exists("Xml", entry) ? Encoding.UTF8.GetString(packs.Read("Xml", entry)).TrimStart('\uFEFF') : null;
        }
        Dictionary<string, string> strings = null;
        string Common(string key)
        {
            if (strings == null)
            {
                strings = new Dictionary<string, string>();
                var xml = Read($"string/{lang}/stringcommon.xml");
                if (xml != null)
                    foreach (var k in XDocument.Parse(xml).Root.Elements("key"))
                        if ((string)k.Attribute("id") is { } id) strings.TryAdd(id, ((string)k.Attribute("value") ?? "").Replace("\\n", "\n"));
            }
            return strings.GetValueOrDefault(key);
        }
        return new BanTextChecker(f => Read($"string/{lang}/{f}"), Common);
    }

    static IEnumerable<string> Names(string xml)
    {
        if (xml == null) yield break;
        var root = XDocument.Parse(xml).Root;
        if (root == null || root.Name != "ms2") yield break;
        foreach (var k in root.Elements("key"))
        {
            var name = (string)k.Attribute("name");
            if (!string.IsNullOrEmpty(name)) yield return name.ToLowerInvariant();
        }
    }

    static void LoadWords(string xml, Node trie)
    {
        foreach (var name in Names(xml))
        {
            var parts = name.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length is 1 or 2) Insert(trie, parts[0]);
        }
    }

    static void Insert(Node trie, string word)
    {
        var n = trie;
        foreach (char ch in word)
        {
            if (!n.Children.TryGetValue(ch, out var c)) n.Children[ch] = c = new Node { Depth = n.Depth + 1 };
            n = c;
        }
        n.WordEnd = true;
    }

    /// <summary>TextFilterUnit::Find: the first position at or after <paramref name="start"/> where a word matches (longest word there).</summary>
    static bool Find(Node trie, string s, int start, out int pos, out int len)
    {
        pos = len = 0;
        if (trie.Children.Count == 0) return false;
        for (int i = start; i < s.Length; i++)
        {
            if (s[i] == '<') { i = SkipTag(s, i); continue; }
            if (trie.Children.TryGetValue(char.ToLowerInvariant(s[i]), out var child) && Match(child, s, i, out len)) { pos = i; return true; }
        }
        return false;
    }

    static int SkipTag(string s, int i)
    {
        if (i + 1 < s.Length && s[i + 1] == 'A')
        {
            int end = s.IndexOf("</A>", i, StringComparison.Ordinal);
            return end > 0 ? end - 1 + 4 : i;
        }
        int gt = s.IndexOf('>', i);
        return gt > 0 ? gt : i;
    }

    /// <summary>TextFilterUnit::Match: deepest word first, else this node when it ends a word.</summary>
    static bool Match(Node n, string s, int i, out int len)
    {
        len = 0;
        if (n.Children.Count > 0 && i + 1 < s.Length && n.Children.TryGetValue(char.ToLowerInvariant(s[i + 1]), out var c) && Match(c, s, i + 1, out len))
            return true;
        if (n.WordEnd) { len = n.Depth; return true; }
        return false;
    }

    static bool FindAny(Node trie, string s, out string matched)
    {
        matched = null;
        if (!Find(trie, s, 0, out int pos, out int len)) return false;
        matched = s.Substring(pos, len);
        return true;
    }

    /// <summary>FindStartWithWord: a word at the start of the text or right after a space.</summary>
    bool FindStartWith(string s, out string matched)
    {
        matched = null;
        int start = 0;
        while (Find(startWith, s, start, out int pos, out int len))
        {
            if (pos == start) { matched = s.Substring(pos, len); return true; }
            start = s.IndexOf(' ', pos);
            if (start < 0) break;
            start++;
        }
        return false;
    }

    /// <summary>FindBanWordAll: a table word that is a whole space-separated word of the text.</summary>
    bool FindBanWordAll(string s)
    {
        int total = s.Length, start = 0;
        while (Find(banWordAll, s, start, out int pos, out int len))
        {
            int space = s.IndexOf(' ', pos);
            bool whole = true;
            if (start == pos)
            {
                if (space == -1 && pos + len != total) whole = false;
                else if (space != -1 && pos + len != space) whole = false;
            }
            else
            {
                int prevSpace = s.IndexOf(' ', start);
                if (space - prevSpace - 1 != len) whole = false;
            }
            if (whole) return true;
            start = s.IndexOf(' ', pos);
            if (start < 0) break;
            start++;
        }
        return false;
    }

    /// <summary>ScanLimit: 0 fits, 1 too short (or spaces only), 2 too long, 3 a character the limit does not allow.</summary>
    public int ScanLimit(string s, Limit limit)
    {
        s ??= "";
        int flags = ClientCountry switch { Country.Korea => 5, Country.Region1 => 6, Country.Region2 => 0x34, Country.Region3 => 0xc, _ => 5 };
        int min = Math.Max(limit.Min, 1);
        if (!limit.SkipTrimCheck)
        {
            var trimmed = s.Trim();
            if (trimmed.Length < min) return 1;
            if (trimmed.Length != s.Length) return 3;
        }
        if (s.Length < min) return 1;
        int extra = 0;
        bool bad = false;
        if (!limit.NoCharCheck)
            for (int i = 0; i < s.Length; i++)
            {
                char prev = i > 0 ? s[i - 1] : '\0', ch = s[i];
                if (limit.AllowSpace && ch == ' ' && prev != ' ') continue;
                if (limit.AllowPunct && ch is '\'' or '-' or '_' or '.') continue;
                if (ch is >= '0' and <= '9' or >= 'a' and <= 'z' or >= 'A' and <= 'Z') continue;
                if ((flags & 1) != 0 && ch >= 0xac00 && ch < 0xd7a4) { if (limit.WideCountsTwice) extra++; continue; }
                if ((flags & 8) != 0 && ch >= 0x4e00 && ch < 0x9faf) { if (limit.WideCountsTwice) extra++; continue; }
                if ((flags & 2) != 0 && charClass.TryGetValue(ch, out int cls)) { if (cls != 0 && limit.WideCountsTwice) extra++; continue; }
                if ((flags & 0x10) != 0 && ch is '\u00c4' or '\u00e4' or '\u00d6' or '\u00f6' or '\u00dc' or '\u00fc' or '\u00df') continue;
                if ((flags & 0x20) != 0 && "\u00aa\u00ba\u00c0\u00c1\u00c2\u00c3\u00c7\u00c9\u00ca\u00cd\u00d3\u00d4\u00d5\u00da\u00dc\u00e0\u00e1\u00e2\u00e3\u00e7\u00e9\u00ea\u00ed\u00f3\u00f4\u00f5\u00fa\u00fc".IndexOf(ch) >= 0) continue;
                bad = true;
            }
        if (s.Length + extra > limit.Max) return 2;
        return bad ? 3 : 0;
    }

    /// <summary>CheckTextOption: the table checks in the client's order; 5 reserved name, 6-8 a word inside, 9 a whole banned word.</summary>
    public int CheckTables(string s, Checks checks, out string matched)
    {
        matched = null;
        s ??= "";
        if (checks.HasFlag(Checks.ReserveIdAll) && reserveIdAll.Contains(s.ToLowerInvariant())) { matched = s.ToLowerInvariant(); return 5; }
        if (checks.HasFlag(Checks.ReserveIdAny) && FindAny(reserveIdAny, s, out matched)) return 6;
        if (checks.HasFlag(Checks.ReserveWord) && FindAny(reserveWord, s, out matched)) return 7;
        if (checks.HasFlag(Checks.BanWord) && FindAny(banWord, s, out matched)) return 8;
        if (checks.HasFlag(Checks.UgcBanWord) && FindAny(ugcBanWord, s, out matched)) return 8;
        if (checks.HasFlag(Checks.StartWith) && FindStartWith(s, out matched)) return 8;
        if (checks.HasFlag(Checks.BanWordAll) && FindBanWordAll(s)) { matched = ""; return 9; }
        return 0;
    }

    /// <summary>CheckTextWithLimit: the limit scan (when the checks include it), then the tables; the message is the client's text.</summary>
    public int Check(string s, Checks checks, Limit limit, out string message)
    {
        message = null;
        if (checks.HasFlag(Checks.Limit))
        {
            int r = ScanLimit(s, limit);
            if (r != 0)
            {
                message = r switch
                {
                    1 => Format("s_ban_check_err_min_length", "Enter at least {0} letters.", Math.Max(limit.Min, 1).ToString()),
                    2 => Format("s_ban_check_err_max_length", "A maximum of {0} letters can be entered.", Math.Max(limit.Max, 1).ToString()),
                    _ => limit.AllowSpace
                        ? Format("s_ban_check_err_invalid_char", "Contains forbidden text.", "")
                        : Format("s_ban_check_err_invalid_char_space", "Can't use spaces or special characters.", ""),
                };
                return r;
            }
        }
        int t = CheckTables(s, checks, out var matched);
        if (t != 0)
            // GetOptionErrString with the checkers' flag set: 5 and 9 name the whole text, 6-8 quote the word found
            message = t is 5 or 9
                ? Format("s_ban_check_err_all_name", "This name cannot be used.", matched)
                : Format("s_ban_check_err_any_word", "Contains a forbidden word ({0}).", matched);
        return t;
    }

    /// <summary>
    /// The character name check (CBanTextChecker::FUN_140225210, called by the create and rename dialogs through
    /// FUN_141276a30): 2 to 12 letters (24 for the long form), no spaces or punctuation, Hangul counting twice,
    /// then every table but the UGC one (types 0xdf).
    /// </summary>
    public int CheckCharacterName(string name, out string message, bool shortForm = true) =>
        Check(name, (Checks)0xdf, new Limit(2, shortForm ? 12 : 24, false, false, false, true, false), out message);

    /// <summary>CBanTextChecker::CheckClubName (commonstringtable.cpp:988): CheckTypes(0xd8, true): the reserved words, the
    /// banned words, the start-with words and the whole banned words; no limit scan.</summary>
    public int CheckClubName(string name, out string message) => Check(name, (Checks)0xd8, default, out message);

    /// <summary>A common string by key (stringcommon.xml), or null.</summary>
    public string CommonString(string key) => commonString?.Invoke(key);

    string Format(string key, string fallback, string arg) => (commonString?.Invoke(key) ?? fallback).Replace("{0}", arg);
}
