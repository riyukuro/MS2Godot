using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>
/// CUserCommandCli (ms2/client/ms2/usercommandcli.cpp), the slash commands of the chat input: Table/chatCommand.xml's rows
/// (<see cref="Load"/>) give each alias (string table 0x38, stringchatcommand.xml) its chat type and function, and the
/// registered functions (RegisterChat @0x141362e60, RegisterDebug @0x141363160; RegisterInternalCommands registers none)
/// run a command (<see cref="Execute"/>). The ctor (@0x141361c50) loads, then registers. The maps are CAtlMap with
/// CStringElementTraitsI, so the names are case-blind; the help order is a CRBMap by stringOrder.
/// Of the functions only Help is ported (<see cref="ShowHelp"/>); the ones whose body starts with the GM level test (return 0
/// under 50) return 0 below it. STILL-OPEN: every other function's body is not ported; <see cref="Hooks.Unported"/> runs it
/// (by default it does nothing and returns 1, as most of them do; some return 0 for wrong arguments or a missing actor,
/// which then lets the line go out as chat).
/// </summary>
public sealed class UserCommands
{
    /// <summary>TUserCommandInfo: the string id, chat type (27 when missing), function, first alias and help suffix.</summary>
    public sealed class Info
    {
        public int StringId, ChatType = NoChatType;
        public string Function = "", FirstAlias = "", Suffix = "", Names = "";
    }

    /// <summary>The chat type of a line that is not a chat command (ChatType's "not found", FUN_141a11c60's 0x1b).</summary>
    public const int NoChatType = 27;
    /// <summary>The chat types the help leaves out of the tooltip (Tooltip @0x141362480): the functions (21) and the
    /// commands sent as chat text (10).</summary>
    public const int FunctionType = 21, CommandType = 10;
    /// <summary>The GM level (DAT_143ab2914) from which Help shows nothing and under which the gated debug functions return 0.</summary>
    public const int GmCommandLevel = 0x32;

    readonly Dictionary<string, Info> aliases = new(StringComparer.OrdinalIgnoreCase);      // aliases
    readonly SortedDictionary<int, Info> ordered = new();                                   // ordered
    readonly Dictionary<string, Func<string, Hooks, int>> commands = new(StringComparer.OrdinalIgnoreCase);   // commands

    /// <summary>The common string of a key (FUN_14028dfa0's GetStringCommonByKey).</summary>
    public Func<string, string> Common = _ => null;

    /// <summary>What a command reads and writes outside the table (the table is shared by every window, as the
    /// client's singleton).</summary>
    public sealed class Hooks
    {
        /// <summary>DAT_143ab2914, the GM level the server sets (FUN_140930150, scenestategame.cpp:5854); 0 for a player.</summary>
        public Func<int> GmLevel = () => 0;
        /// <summary>FUN_1412751b0(text, kind): a system line of the chat log; null without the log.</summary>
        public Action<string, int> ChatLine;
        /// <summary>A registered function whose body is not ported, by its registered name and argument text; returns its
        /// result. STILL-OPEN: none is ported, so 1.</summary>
        public Func<string, string, int> Unported = (_, _) => 1;
    }

    static readonly Hooks NoHooks = new();

    /// <summary>The functions whose body starts `if(DAT_143ab2914&lt;50) return 0;` (SendAttendance, RequestSaleMarket,
    /// ToggleStat, PlayVoiceSound, PlayBgm, ShowBgmState, ShowSmartRecommend, SetWeather, SetMaxFps) and ShowServerTime,
    /// which tests 50 at region 0 (51 at region 2).</summary>
    static readonly HashSet<string> GmGated = new(StringComparer.OrdinalIgnoreCase)
    {
        "\uCD9C\uC11D" /* attendance */, "SaleMarket", "Stat", "ShowServerTime", "PlayVoiceSound", "PlayBgm", "ShowBgmState", "\uC2A4\uB9C8\uD2B8\uCD94\uCC9C" /* smart recommend */, "Weather", "MaxFPS",
    };

    /// <summary>RegisterChat (:770) and RegisterDebug (:784) in their order.</summary>
    public static readonly string[] Registered =
    {
        "SearchMaview", "GuildInvite", "GuildLeave", "PartyInvite", "PartyLeave", "PartyVoteReady", "HomeInvite", "Help",
        "SuperWorldChat", "MovePrevChannel", "MoveNextChannel",
        "\uCD9C\uC11D" /* attendance */, "\uCE74\uBA54\uB77C" /* camera */, "LocalCamera", "TargetCamera", "UiDev", "SaleMarket", "Stat", "GuideDebug", "GuideShowState",
        "GuideChangeState", "GuideResetClear", "GuideTestCondition", "GuideTestFunction", "GuideClearAll", "ShowServerTime",
        "PlayVoiceSound", "PlayBgm", "ShowBgmState", "ReloadString", "ReloadGuideString", "\uC2A4\uB9C8\uD2B8\uCD94\uCC9C" /* smart recommend */, "showcodename",
        "showupperversiontext", "CacheKeepTime", "RemoveNif", "RemoveTexture", "Weather", "MaxFPS", "AddDevEffect",
        "RemoveDevEffect", "SetHomePwd", "CommendHome", "GoIntoHome", "HomeNotice", "ExportUGCMap", "GoIntoDesignHome",
        "OpenVIP", "optimize", "SamplingFrame", "SetNPS", "SetForceGMId", "FindNpcGameOID", "NpcDebugMode", "NpcDebugModeByID",
        "NetStreamURL", "CutScene", "HideUI", "HideMe", "PhysXItem", "ShowNode", "CountUI", "ClubCmd", "TakeInteriorGift",
        "TakeInteriorGradeGift", "SetUGCMapBackground", "SetEventBoardTextSpeed", "SetEventBoardText", "SetUGCMapBalance",
        "AddUGCMapPriv", "RemoveUGCMapPriv", "SellMyHome", "EnableFeature", "EnableSkipFrame", "FaceEmotion", "ReloadScript",
        "SetClientItemTransferType", "PacketSendDelay", "PacketRecvDelay", "DungeonVoiceChatEnter", "DungeonVoiceChatLeave",
        "DungeonVoiceChatShowUi", "TraceQuest", "CompleteQuest", "CompleteEpicChapter", "CompleteEpicQuest",
        "SetEpicChapterStatus", "SetEpicQuestStatus", "PopupText", "DPSLog", "UseGamePad", "ClientLogLevel", "savekey",
        "combinekey", "m", "\uB9F5" /* map */, "RecordFishing", "ReportFishing", "effect_afterimage", "TestDisconnect", "EnableFog",
        "EnableEffectLimit", "GetDamageMovieCount", "SkillNotFind", "ResetOpendUgcTerms", "FishingCastingBarMode",
        "LearnAllAbility", "ShowKey", "SendPacket", "SetPosition", "ChatLog", "NoWaitRoomExit", "DamageUseOld", "DamageTestLoop",
        "SetDisableClonePC", "ShowMyPCNodes", "SkipLoadingUGCCube",
    };

    /// <summary>No table (declared after the static tables its ctor reads).</summary>
    public static readonly UserCommands Empty = new();

    public UserCommands() { Register(); }

    /// <summary>The ctor (@0x141361c50) over the client's data: <see cref="Load"/>, then the registered functions.</summary>
    public UserCommands(PackSet packs, IReadOnlyDictionary<string, string> names, Func<string, string> common) : this()
    {
        Common = common ?? Common;
        if (packs == null || !packs.Exists("Xml", "table/chatcommand.xml")) return;
        Load(GameStrings.Parse(packs.Read("Xml", "table/chatcommand.xml")), e => FeatureElements.IsAvailable(packs, e), names);
    }

    void Register()
    {
        foreach (var name in Registered)
        {
            string n = name;
            commands[n] = n == "Help" ? (_, h) => ShowHelp(h)
                : GmGated.Contains(n) ? (args, h) => h.GmLevel() < 50 ? 0 : h.Unported(n, args)
                : (args, h) => h.Unported(n, args);
        }
    }

    public IReadOnlyDictionary<string, Info> Aliases => aliases;
    public IReadOnlyDictionary<int, Info> Ordered => ordered;

    /// <summary>
    /// Load (@0x141362920): each &lt;ms2&gt;&lt;chatCommand&gt; the client takes (IsElementAvailable) with a non-empty
    /// names text (string table 0x38 by its id) split at "\n" (FUN_14025a680, empty parts dropped): its chatType
    /// (FUN_14136f660, XmlQuery: kept when missing) and function, each trimmed non-empty name an alias (a later row's
    /// replacing an earlier one), the first the row's first alias; a stringOrder above 0 with a stringSuffix puts the row
    /// in the help order.
    /// </summary>
    public void Load(XDocument doc, Func<XElement, bool> available, IReadOnlyDictionary<string, string> names)
    {
        var root = doc?.Root;
        if (root == null || root.Name.LocalName != "ms2") return;
        foreach (var e in root.Elements("chatCommand"))
        {
            if (!available(e)) continue;
            var info = new Info { StringId = Int(e, "id", 0) };
            string text = names != null && names.TryGetValue(info.StringId.ToString(CultureInfo.InvariantCulture), out var t) ? t ?? "" : "";
            if (text.Length == 0) continue;
            var items = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (items.Length == 0) continue;
            info.Names = text;
            info.ChatType = Int(e, "chatType", info.ChatType);
            info.Function = (string)e.Attribute("function") ?? "";
            foreach (var item in items)
            {
                string name = Trim(item);
                if (name.Length == 0) continue;
                if (info.FirstAlias.Length == 0) info.FirstAlias = name;
                aliases[name] = info;
            }
            int order = Int(e, "stringOrder", 0);
            if (order > 0 && (string)e.Attribute("stringSuffix") is { Length: > 0 } suffix)
            {
                info.Suffix = suffix;
                ordered[order] = info;
            }
        }
    }

    static int Int(XElement e, string name, int v) =>
        (string)e.Attribute(name) is { } s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : v;

    /// <summary>CStringW::Trim: the white space (iswspace) at both ends.</summary>
    public static string Trim(string s) => s.Trim(' ', '\t', '\n', '\v', '\f', '\r');

    /// <summary>Find (@0x141362330): the alias's row; null when none.</summary>
    public Info Find(string name) => name != null && aliases.TryGetValue(name, out var info) ? info : null;

    /// <summary>ChatType (@0x141362400): the alias's chat type, else 27.</summary>
    public int ChatType(string name) => Find(name)?.ChatType ?? NoChatType;

    /// <summary>
    /// Execute (@0x141361f00): the first word (Tokenize " ") is an alias's function, else the word without its first
    /// character; the rest (Mid at the token's end) is the function's arguments. 0 for an empty or unregistered name, else
    /// the function's result.
    /// </summary>
    public int Execute(string command, Hooks hooks = null)
    {
        hooks ??= NoHooks;
        int start = 0;
        string name = Tokenize(command, " ", ref start);
        string arguments = Mid(command, start);
        if (aliases.TryGetValue(name, out var info)) name = info.Function;
        else name = Mid(name, 1);
        if (name.Length == 0) return 0;
        return commands.TryGetValue(name, out var callback) ? callback(arguments, hooks) : 0;
    }

    /// <summary>
    /// ShowHelp (:2037): nothing (0) from the GM level 50; else each help row's s_usercommand_fmt_&lt;suffix&gt; with its
    /// names (", " between) and first alias, all in s_usercommand_fmt, as a system line of kind 20.
    /// </summary>
    public int ShowHelp(Hooks hooks = null)
    {
        hooks ??= NoHooks;
        if (hooks.GmLevel() >= GmCommandLevel) return 0;
        hooks.ChatLine?.Invoke(Help("s_usercommand_fmt", _ => false), TalkKind.SystemNotice);
        return 1;
    }

    /// <summary>Tooltip (@0x141362480): the same with s_chattooltip_fmt, the chat types 21 and 10 left out.</summary>
    public string Tooltip() => Help("s_chattooltip_fmt", i => i.ChatType is FunctionType or CommandType);

    string Help(string key, Func<Info, bool> skip)
    {
        var text = new System.Text.StringBuilder();
        foreach (var info in ordered.Values)
        {
            if (info.Suffix.Length == 0 || skip(info)) continue;
            text.Append(ClientFormat.Format(Common($"{key}_{info.Suffix}") ?? "", info.Names.Replace("\n", ", "), info.FirstAlias));
        }
        return ClientFormat.Format(Common(key) ?? "", text.ToString());
    }

    /// <summary>
    /// CStringW::Tokenize(delimiters, start): from <paramref name="start"/> (inside the text) the delimiters are skipped;
    /// the token runs to the next delimiter and start moves past it. With nothing left, "" and start -1.
    /// </summary>
    public static string Tokenize(string s, string delimiters, ref int start)
    {
        s ??= "";
        if (start >= 0 && start < s.Length)
        {
            int begin = start;
            while (begin < s.Length && delimiters.IndexOf(s[begin]) >= 0) begin++;
            if (begin < s.Length)
            {
                int end = begin;
                while (end < s.Length && delimiters.IndexOf(s[end]) < 0) end++;
                start = end + 1;
                return s.Substring(begin, end - begin);
            }
        }
        start = -1;
        return "";
    }

    /// <summary>CStringW::Mid(first): a negative first is 0, one past the end gives "".</summary>
    public static string Mid(string s, int first)
    {
        s ??= "";
        if (first < 0) first = 0;
        return first >= s.Length ? "" : s.Substring(first);
    }
}
