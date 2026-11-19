using System.Globalization;
using Ms2.Net.Packets;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// TextToHtml (texttohtml.cpp), the html of a user's chat line: FUN_14183f310 (:419) and its helpers. The format strings
/// are common strings (FUN_14028dee0: <see cref="ClientFormat.Format"/> of GetStringCommon(id)); the colours are
/// s_color0 .. s_color24 (<see cref="GameTables.ChatColor"/>).
/// </summary>
public static class ChatHtml
{
    // texttohtml.cpp:40-53
    public const string IconMegaphone2 = "export_common_icon_megaphone2", IconMegaphone3 = "export_common_icon_megaphone3",
        IconMegaphone5 = "export_common_icon_megaphone5", IconMegaphone6 = "export_common_icon_megaphone6",
        IconMoveToHouse = "export_common_icon_movetohouse", IconOxQuiz = "export_common_icon_ox_quiz",
        IconReturn = "export_common_icon_return", IconSystem = "export_common_icon_system", IconWhisper = "export_common_icon_whisper";

    /// <summary>CFeatureManager ids FUN_14183f310 tests: 0xde picks the chat formats without the gender argument.</summary>
    public const int ChatFormatFeature = 0xde;

    /// <summary>The local player FUN_14183f310 compares and the features it tests.</summary>
    public sealed class Context
    {
        public GameTables Tables = GameTables.Empty;
        /// <summary>The local player's name (vfunc_77); null without one.</summary>
        public string LocalName;
        public Func<int, bool> FeatureEnabled = _ => false;
        /// <summary>CClubClientManager::GetClubName. STILL-OPEN: the club manager is not ported, so "".</summary>
        public Func<long, string> ClubName = _ => "";
    }

    static string F(Context c, int id, params object[] args) => ClientFormat.Format(c.Tables.Common(id), args);

    /// <summary>
    /// FUN_14183f310 (:419): the line's html by its type, at the font size (ReplaceFontSize). The user link is
    /// 'event:userSelect,name,account,character' and the home link 'event:moveToHome,..' the same; the name and text
    /// colours go through <see cref="OwnColor"/>, and after each user line <see cref="FixColor"/> turns the format's
    /// COLOR="#FFFFFE" into the text colour. An unknown type gives "".
    /// </summary>
    public static string Line(Context c, ChatMessage m, string text, uint size)
    {
        var t = c.Tables;
        string name = m.Name ?? "";
        string user = $"'event:userSelect,{name},{(ulong)m.AccountId},{(ulong)m.CharacterId}'";
        string home = $"'event:moveToHome,{name},{(ulong)m.AccountId},{(ulong)m.CharacterId}'";
        bool me = c.LocalName != null && c.LocalName == name;                            // FUN_1418426b0
        string img = HasImage(t, m) ? ClientFormat.Format(t.Common("s_html_chat_img"), IconReturn) : "";
        bool chatFormat = c.FeatureEnabled(ChatFormatFeature);
        uint A(int n) => OwnColor(t, m.Gm, me, t.ChatColor(n));
        string result = "";
        switch (m.Type)
        {
            case 0x10:
            {
                uint a = A(6), b = A(18);
                SuperChatIcon(t, m.ItemId, out string icon, out uint iconSize, ref a, ref b);
                result = F(c, 0x67c, iconSize);
                result = chatFormat
                    ? F(c, 0x66e, result, IconMegaphone6, icon, user, a, size, name, img, home, IconMoveToHouse, b, size, text)
                    : F(c, 0x66e, result, IconMegaphone6, icon, user, a, size, name, m.Gender, img, home, IconMoveToHouse, b, size, text);
                result = FixColor(result, b);
                break;
            }
            case 0xb:
            {
                uint a = A(7), b = A(19);
                result = chatFormat
                    ? F(c, 0x66f, IconMegaphone3, user, a, size, name, img, home, IconMoveToHouse, b, size, text)
                    : F(c, 0x66f, IconMegaphone3, user, a, size, name, m.Gender, img, home, IconMoveToHouse, b, size, text);
                result = FixColor(result, b);
                break;
            }
            case 0xc:
            {
                uint a = A(8), b = A(20);
                result = FixColor(F(c, 0x670, IconMegaphone2, user, a, size, name, img, home, IconMoveToHouse, b, size, text), b);
                break;
            }
            case 0: result = UserLine(c, 0x671, user, A(0), A(10), size, name, img, text, true); break;
            case 7: result = UserLine(c, chatFormat ? 0x672 : 0x671, user, A(4), A(16), size, name, img, text, true); break;
            case 0x12:
            {
                uint a = A(5), b = A(17);
                result = FixColor(F(c, 0x67d, IconMegaphone5, user, a, size, name, img, b, size, text), b);
                break;
            }
            case 8: result = UserLine(c, chatFormat ? 0x673 : 0x671, user, A(5), A(17), size, name, img, text, true); break;
            case 1 or 2:
            {
                uint a = A(3), b = A(12);
                result = FixColor(F(c, 0x670, IconMegaphone2, user, a, size, name, img, b, size, text), b);
                break;
            }
            case 4:
            {
                uint a = A(2), b = A(13);
                result = FixColor(F(c, 0x674, IconWhisper, user, a, size, name, img, b, size, text), b);
                break;
            }
            case 3:
            {
                uint a = A(2), b = A(15);
                result = FixColor(F(c, 0x675, IconWhisper, user, a, size, name, img, b, size, text), b);
                break;
            }
            case 5 or 6: result = F(c, 0x676, t.ChatColor(14), size, text); break;
            case 9: result = F(c, 0x677, IconSystem, t.ChatColor(21), size, text); break;
            case 0xf: result = F(c, 0x679, t.ChatColor(21), size, text); break;
            case 0x13: result = UserLine(c, chatFormat ? 0x67e : 0x671, user, A(5), A(17), size, name, img, text, false); break;
            case 0x14:
            {
                uint a = A(9), b = A(24);
                result = FixColor(F(c, 0x67a, user, a, size, c.ClubName(m.ClubId) ?? "", name, img, b, size, text), b);
                break;
            }
            case 0x16:
            {
                // FUN_14025a680(text, parts, ","): CStringW::Tokenize, empty tokens dropped. STILL-OPEN: the client indexes
                // parts 0 and 3..5 unchecked (CAtlArray asserts past the end); a missing part is "" here.
                var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries);
                string P(int i) => i < parts.Length ? parts[i] : "";
                string join = $"'event:joinUGCEvent,{name},{(ulong)m.AccountId},{(ulong)m.CharacterId},{P(3)},{P(4)},{P(5)}'";
                result = F(c, 0x67b, user, name, join, IconOxQuiz, P(0));
                break;
            }
        }
        return ReplaceFontSize(size, result);
    }

    static string UserLine(Context c, int id, string user, uint a, uint b, uint size, string name, string img, string text, bool fix)
    {
        string s = F(c, id, user, a, size, name, img, b, size, text);
        return fix ? FixColor(s, b) : s;
    }

    /// <summary>FUN_141842680 (:751): a gm's line (m_34) not from the local player takes s_color23 (chatTextGMColor).</summary>
    public static uint OwnColor(GameTables t, bool gm, bool me, uint color) => gm && !me ? t.ChatColor(23) : color;

    /// <summary>
    /// FUN_141842770 (:771): the return icon after the name, with MentoringDataTable's isShowIcon (m_30) and the line's last
    /// byte (m_4c) set, for the types 0x10, 0xb, 0xc, 0, 0x19, 7, 8, 0x12, 1, 2, 4, 3, 0x13 and 0x14.
    /// </summary>
    public static bool HasImage(GameTables t, ChatMessage m) =>
        t.MentoringShowIcon && m.ShowImage != 0 && m.Type is 0x10 or 0xb or 0xc or 0 or 0x19 or 7 or 8 or 0x12 or 1 or 2 or 4 or 3 or 0x13 or 0x14;

    /// <summary>
    /// FUN_141842560 (@0x141842560, texttohtml.cpp:736): every COLOR="#FFFFFE" (MakeUpper of color="#fffffe") becomes COLOR='#&lt;colour&gt;' (MakeUpper
    /// of color='#%x'), case-sensitive.
    /// </summary>
    public static string FixColor(string html, uint color) =>
        html.Replace("COLOR=\"#FFFFFE\"", ("color='#" + color.ToString("x", CultureInfo.InvariantCulture) + "'").ToUpperInvariant(), StringComparison.Ordinal);

    /// <summary>
    /// FUN_14183eba0 (:350), the super chat icon of the item: icon megaphone6 and size 1; for an item whose function is
    /// "SuperWorldChat", with more than one argument a non-empty argument 0 gives "export_super_icon" + it and argument 1
    /// the size (FUN_1408d1f60: _wtoi, 0 when missing); with more than two, argument 2 read as hex (_wcstoi64 base 16, made
    /// int) is both colours.
    /// </summary>
    public static void SuperChatIcon(GameTables t, int itemId, out string icon, out uint size, ref uint colorA, ref uint colorB)
    {
        size = 1;
        icon = IconMegaphone6;
        var d = t.Items.Get(itemId);
        if (d == null || d.FunctionName != "SuperWorldChat") return;
        var args = d.FunctionArgs;
        if (args.Count > 1)
        {
            if (args[0].Length > 0) icon = "export_super_icon" + args[0];
            size = (uint)Ms2.Ui.Items.ItemData.Atoi(args[1]);
        }
        if (args.Count > 2)
        {
            colorB = (uint)(int)Wcstoi64Hex(args[2]);
            colorA = colorB;
        }
    }

    /// <summary>_wcstoi64(s, NULL, 16): white space, a sign, an optional 0x, hex digits; 0 when none; saturates.</summary>
    public static long Wcstoi64Hex(string s)
    {
        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        bool neg = false;
        if (i < s.Length && (s[i] == '+' || s[i] == '-')) neg = s[i++] == '-';
        if (i + 1 < s.Length && s[i] == '0' && (s[i + 1] == 'x' || s[i + 1] == 'X') && i + 2 < s.Length && Uri.IsHexDigit(s[i + 2])) i += 2;
        ulong v = 0;
        bool over = false;
        for (; i < s.Length && Uri.IsHexDigit(s[i]); i++)
        {
            if (v > (ulong.MaxValue >> 4)) over = true;
            v = (v << 4) | (uint)Uri.FromHex(s[i]);
        }
        if (neg) return over || v > (ulong)long.MaxValue + 1 ? long.MinValue : -(long)v;
        return over || v > long.MaxValue ? long.MaxValue : (long)v;
    }

    /// <summary>
    /// The chat log kind of a chat type (FUN_141482f00, messengerdata.cpp:849): 0x10 / 0x19 super 0x17, 0xb / 0x16 world
    /// 0x15, 0xc channel 0x16, 8 guild 3, 0x12 guild megaphone 0x18, 7 party 2, 3 / 4 whisper 4, 0xf 0x11, 0x14 club 0x1a,
    /// any other social 1.
    /// </summary>
    public static int Kind(int type) => type switch
    {
        0x10 or 0x19 => 0x17, 0xb or 0x16 => 0x15, 0xc => 0x16, 8 => 3, 0x12 => 0x18, 7 => 2, 3 or 4 => 4, 0xf => 0x11, 0x14 => 0x1a, _ => 1,
    };

    /// <summary>ReplaceFontSize (@0x1418423c0, texttohtml.cpp:717): each size='12' .. size='18' and SIZE="12" .. SIZE="18" becomes the size.</summary>
    public static string ReplaceFontSize(uint size, string html)
    {
        string sq = $"size='{size}'", dq = $"SIZE=\"{size}\"";
        for (int i = 12; i <= 18; i++)
            html = html.Replace($"size='{i}'", sq, StringComparison.Ordinal).Replace($"SIZE=\"{i}\"", dq, StringComparison.Ordinal);
        return html;
    }
}
