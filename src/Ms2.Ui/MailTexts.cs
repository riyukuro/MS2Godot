using System.Globalization;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Net.Packets;
using Ms2.Quest;

namespace Ms2.Ui;

/// <summary>
/// The mail texts: the system mail templates (CSystemMailContent, commonfiles/systemmailcontent.cpp), the text resolution
/// CMailInfo::Read does after the packet fields (mailattachitem.cpp:630-670, with MailText of commonfiles/mailtextutil.cpp),
/// the mail list's date string and the common strings the mail windows use. The client's tables come in as delegates.
/// </summary>
public sealed class MailTexts
{
    /// <summary>One CSystemMailContent: the template a system mail's content names by id.</summary>
    public sealed record SystemMail(int Id, string Sender, string Subject, string Content);

    readonly Dictionary<int, SystemMail> system;

    MailTexts(Dictionary<int, SystemMail> system) => this.system = system;

    /// <summary>GetStringCommonByKey.</summary>
    public Func<string, string> CommonByKey = _ => "";
    /// <summary>CStringTable::FUN_141811980(table, id): 1 is the item names.</summary>
    public Func<int, long, string> TableString = (_, _) => "";
    /// <summary>GetLocalizedText (@0x14083d340): see ServerStringGroup.LocalizedText.</summary>
    public Func<string, string> LocalizedText = s => s;
    /// <summary>CReservedWordTable::ReplaceValueWords.</summary>
    public Func<string, string> ReplaceValueWords = s => s;
    /// <summary>CReservedWordTable::ReplaceObjectAndValueWords (@0x1416fb300): the object words, then the value words.</summary>
    public Func<string, string> ReplaceObjectAndValueWords = s => s;

    /// <summary>
    /// LoadSystemMailContent (@0x140851750): "systemMailContent" + the locale (FUN_140651390, DAT_143ab0258) + ".xml" under
    /// String (FUN_14071ae20), else "systemMailContent.xml"; neither, or no root "ms2", leaves no template. Each "mail" gives
    /// id, sender, subject and content, each through ReplaceObjectWords; a repeated id keeps the last one (SetAt). The pack
    /// stores the names in lower case. STILL-OPEN: the client reads the file with TinyXML, here with XDocument.
    /// </summary>
    public static MailTexts Load(PackSet packs, string[] languages, string locale, Func<string, string> objectWords)
    {
        var map = new Dictionary<int, SystemMail>();
        var root = Root(packs, languages, $"systemmailcontent{(locale ?? "").ToLowerInvariant()}.xml", out bool found);
        if (!found) root = Root(packs, languages, "systemmailcontent.xml", out _);
        if (root != null)
            foreach (var e in root.Elements("mail"))
            {
                int id = Ms2.Formats.World.ReservedWords.Atoi((string)e.Attribute("id") ?? "0");
                map[id] = new SystemMail(id, objectWords((string)e.Attribute("sender") ?? ""),
                    objectWords((string)e.Attribute("subject") ?? ""), objectWords((string)e.Attribute("content") ?? ""));
            }
        return new MailTexts(map);
    }

    /// <summary>An empty set (no template), for tests and a missing file.</summary>
    public static MailTexts Empty() => new(new Dictionary<int, SystemMail>());

    /// <summary>The file's root: found is false when no language has the file or it does not parse (LoadXmlFile fails).</summary>
    static XElement Root(PackSet packs, string[] langs, string file, out bool found)
    {
        found = false;
        foreach (var lang in langs)
        {
            string entry = $"string/{lang}/{file}";
            if (!packs.Exists("Xml", entry)) continue;
            try
            {
                var root = Ms2.Formats.World.GameStrings.Parse(packs.Read("Xml", entry)).Root;
                found = true;
                return root != null && root.Name == "ms2" ? root : null;
            }
            catch (System.Xml.XmlException) { return null; }
        }
        return null;
    }

    /// <summary>CSystemMailContent::GetMail: null for an id with no template (0 included).</summary>
    public SystemMail GetMail(int id) => system.TryGetValue(id, out var m) ? m : null;

    /// <summary>
    /// The text part of CMailInfo::Read (mailattachitem.cpp:630-670), in place:
    /// <list type="number">
    /// <item>A template kind (FUN_140692690) whose content holds only digits (an empty one too) names a template
    /// (_wtoi): the sender is taken when the mail's is empty, the title and content are the template's, then each gets
    /// ReplaceValueWords. With no template the content gets ReplaceObjectAndValueWords.</item>
    /// <item>A kind that is not a player's (FUN_1406926e0): the sender, title and content through GetLocalizedText, then
    /// MailText(0).Load(sender, ""), (1).Load(title, title args), (2).Load(content, content args), each field its
    /// ResolveTitle.</item>
    /// <item>A words kind (FUN_140692720): the content through ReplaceObjectAndValueWords.</item>
    /// </list>
    /// </summary>
    public void Resolve(MailInfo m)
    {
        if (MailInfo.TemplateKind(m.Kind))
        {
            var tpl = AllDigits(m.Content) ? GetMail(Ms2.Formats.World.ReservedWords.Atoi(m.Content)) : null;
            if (tpl != null)
            {
                if (m.Sender.Length == 0) m.Sender = tpl.Sender;
                m.Title = tpl.Subject;
                m.Content = tpl.Content;
                m.Sender = ReplaceValueWords(m.Sender);
                m.Title = ReplaceValueWords(m.Title);
                m.Content = ReplaceValueWords(m.Content);
            }
            else m.Content = ReplaceObjectAndValueWords(m.Content);
        }
        if (!MailInfo.PlayerKind(m.Kind))
        {
            m.Sender = LocalizedText(m.Sender);
            m.Title = LocalizedText(m.Title);
            m.Content = LocalizedText(m.Content);
            var sender = new MailText(this); sender.Load(m.Sender, "");
            var title = new MailText(this); title.Load(m.Title, m.TitleArgs);
            var content = new MailText(this); content.Load(m.Content, m.ContentArgs);
            m.Sender = sender.ResolveTitle();
            m.Title = title.ResolveTitle();
            m.Content = content.ResolveTitle();
        }
        if (MailInfo.WordsKind(m.Kind)) m.Content = ReplaceObjectAndValueWords(m.Content);
    }

    /// <summary>SpanIncluding(set) == s: every character is in the set (an empty string too).</summary>
    static bool OnlyOf(string s, string set) => s.All(c => set.IndexOf(c) >= 0);
    static bool AllDigits(string s) => OnlyOf(s, "0123456789");

    /// <summary>
    /// MailTextValue (mailtextutil.cpp): a kind and a value. Load (@0x14065b240) takes the first attribute of str (kind 0;
    /// digits with commas and dots lose the commas and are grouped again, FUN_14083cb20), key (1), item (2), money (3; only
    /// digits are grouped); none asserts and fails. Resolve (@0x14065ba60): 0 and 3 the value, 1 the common string by key,
    /// 2 the item name of _wtoi(value) (FUN_140718200).
    /// </summary>
    sealed class MailTextValue
    {
        int kind;
        string value = "";

        public static MailTextValue Text(string text) => new() { kind = 0, value = text ?? "" };

        public bool Load(XElement e)
        {
            if (e.Attribute("str") is { } s)
            {
                kind = 0;
                string v = s.Value;
                if (OnlyOf(v, "0123456789,.") && v.Contains(','))
                    v = NumberText.Group(v.Replace(",", ""));
                value = v;
                return true;
            }
            if (e.Attribute("key") is { } k) { kind = 1; value = k.Value; return true; }
            if (e.Attribute("item") is { } i) { kind = 2; value = i.Value; return true; }
            if (e.Attribute("money") is { } m)
            {
                kind = 3;
                string v = m.Value;
                if (AllDigits(v)) v = NumberText.Group(v);
                value = v;
                return true;
            }
            return false;
        }

        public string Resolve(MailTexts t) => kind switch
        {
            0 or 3 => value,
            1 => t.CommonByKey(value),
            2 => t.TableString(1, Ms2.Formats.World.ReservedWords.Atoi(value)),
            _ => "",
        };
    }

    /// <summary>
    /// MailTextParagraph: a value and its "p" children. Load (@0x14065c410) fails when the value does not load; each child
    /// is added whether it loads or not. Resolve (@0x14065c560) formats the value with the children's resolves (StringFormat),
    /// up to 5; more asserts and gives the value alone.
    /// </summary>
    sealed class MailTextParagraph
    {
        readonly MailTextValue value = new();
        readonly List<MailTextValue> children = new();

        public bool Load(XElement root)
        {
            if (!value.Load(root)) return false;
            foreach (var p in root.Elements("p"))
            {
                var c = new MailTextValue();
                c.Load(p);
                children.Add(c);
            }
            return true;
        }

        public string Resolve(MailTexts t)
        {
            string r = value.Resolve(t);
            if (children.Count == 0) return r;
            if (children.Count > 5) return r;
            return ClientFormat.Format(r, children.Select(c => (object)c.Resolve(t)).ToArray());
        }
    }

    /// <summary>
    /// MailText: a title value and paragraphs. Load (@0x14065d590) is LoadTitle then LoadBody, failing when either fails.
    /// LoadTitle (@0x14065d600) sets the text as the title (kind 0); a text starting "&lt;ms2&gt;" is read as XML and its
    /// first "v" under "ms2" loads the title (a load failure asserts, Load still succeeds); XML that does not parse, or
    /// with no ms2 or v, fails. LoadBody (@0x14065d7b0) clears the paragraphs; an "&lt;ms2&gt;" text gives one paragraph
    /// per "v" (each added whether it loads or not), XML without any fails. ResolveTitle (@0x14065d9d0) formats the title with
    /// the paragraphs' resolves, up to 10; more asserts and gives the title alone.
    /// </summary>
    sealed class MailText
    {
        readonly MailTexts t;
        MailTextValue title = MailTextValue.Text("");
        readonly List<MailTextParagraph> paragraphs = new();

        public MailText(MailTexts t) => this.t = t;

        public bool Load(string titleText, string body) => LoadTitle(titleText) && LoadBody(body);

        bool LoadTitle(string text)
        {
            title = MailTextValue.Text(text);
            if (!(text ?? "").StartsWith("<ms2>", StringComparison.Ordinal)) return true;
            var v = Parse(text)?.Element("v");
            if (v == null) return false;
            var loaded = new MailTextValue();
            if (loaded.Load(v)) title = loaded;
            return true;
        }

        bool LoadBody(string text)
        {
            paragraphs.Clear();
            if (!(text ?? "").StartsWith("<ms2>", StringComparison.Ordinal)) return true;
            var root = Parse(text);
            if (root == null || root.Element("v") == null) return false;
            foreach (var v in root.Elements("v"))
            {
                var p = new MailTextParagraph();
                p.Load(v);
                paragraphs.Add(p);
            }
            return true;
        }

        /// <summary>LoadXmlFromString then FirstChild("ms2"): null when it does not parse or has no ms2 root.</summary>
        static XElement Parse(string text)
        {
            try
            {
                var root = XDocument.Parse(text).Root;
                return root != null && root.Name == "ms2" ? root : null;
            }
            catch (System.Xml.XmlException) { return null; }
        }

        public string ResolveTitle()
        {
            string r = title.Resolve(t);
            if (paragraphs.Count == 0 || paragraphs.Count > 10) return r;
            return ClientFormat.Format(r, paragraphs.Select(p => (object)p.Resolve(t)).ToArray());
        }
    }

    /// <summary>
    /// GetDateString: FUN_14083bdc0(CMsTime(sent), 24 hours, "/", ":") in the client language: 2 (EN) month/day/year, 7
    /// day/month/year, both with a trailing space; 4, 5 and 6 day.month.year with GetDateSeparator (5 "." else "/"); any
    /// other year/month/day. The time is the server's local time (CMsTime, MsTime.LocalTm).
    /// </summary>
    public static string DateString(long unix, int lang)
    {
        var t = Ms2.Net.MsTime.LocalTm(unix);
        string sep = lang == 5 ? "." : "/";
        string time = t.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        string d2(int n) => n.ToString("00", CultureInfo.InvariantCulture);
        string y = t.Year.ToString("0000", CultureInfo.InvariantCulture);
        return lang switch
        {
            2 => $"{d2(t.Month)}/{d2(t.Day)}/{y} {time} ",
            7 => $"{d2(t.Day)}/{d2(t.Month)}/{y} {time} ",
            4 or 5 or 6 => $"{d2(t.Day)}{sep}{d2(t.Month)}{sep}{y} {time}",
            _ => $"{y}/{d2(t.Month)}/{d2(t.Day)} {time}",
        };
    }

    // ---- common strings by id (stringcommon ids the mail windows use; commonstring source m_strings[id]) ----

    static readonly Dictionary<int, string> CommonKeys = new()
    {
        [0x34] = "s_err_lack_meso",
        [0x228] = "s_mail_send", [0x229] = "s_mail_return", [0x22a] = "s_mail_delete", [0x22b] = "s_notify_mail_recieve",
        [0x22d] = "s_mail_read_list", [0x22e] = "s_mail_delete_list", [0x22f] = "s_mail_receive_list",
        [0x231] = "s_mail_delete_list_attached_mail", [0x232] = "s_mail_delete_list_attached_mail_checkbox",
        [0x235] = "s_mail_delete_confirm", [0x236] = "s_mail_delete_exist_ad", [0x237] = "s_mail_delete_exist_attach",
        [0x23a] = "s_mail_error", [0x23d] = "s_mail_error_not_select", [0x23e] = "s_mail_error_username",
        [0x23f] = "s_mail_error_cannot_attach_item", [0x240] = "s_mail_error_attachcount",
        [0x241] = "s_mail_error_already_receive", [0x242] = "s_mail_error_recipient_equal_sender",
        [0x243] = "s_mail_error_createmail", [0x244] = "s_mail_error_sendmail", [0x249] = "s_mail_error_alreadyread",
        [0x24a] = "s_mail_error_receiveitem_to_inven", [0x24b] = "s_mail_error_receive_expired",
        [0x24c] = "s_mail_error_ad_expired", [0x24d] = "s_mail_error_block_from_me", [0x24e] = "s_mail_error_block_from_other",
        [0x24f] = "s_mail_error_admin_character", [0x250] = "s_mail_error_from_admin_to_user",
        [0x251] = "s_mail_error_bancheck", [0x252] = "s_mail_error_admin_block",
        [0x257] = "s_mail_period_item_include", [0x258] = "s_mail_period_item_include_chat",
        [0x805] = "s_notify_mail_reply", [0x806] = "s_notify_mail_reply_content",
        [0x807] = "s_notify_mail_remainDay", [0x808] = "s_notify_mail_remainDay_tooltip",
        [0x809] = "s_notify_mail_remainDay_today", [0x80a] = "s_notify_mail_remainDay_tooltip_today",
        [0x80b] = "s_notify_mail_remainDay_expire",
        [0xc07] = "s_anti_addiction_cannot_receive",
    };

    /// <summary>GetStringCommon(id) for the ids above ("" for any other).</summary>
    public string Common(int id) => CommonKeys.TryGetValue(id, out var k) ? CommonByKey(k) : "";

    /// <summary>StringFormat(GetStringCommon(id), args) (FormatStringId).</summary>
    public string CommonF(int id, params object[] args) => ClientFormat.Format(Common(id), args);

    /// <summary>FUN_14028dfa0(key, args): StringFormat(GetStringCommonByKey(key), args).</summary>
    public string CommonKeyF(string key, params object[] args) => ClientFormat.Format(CommonByKey(key), args);

    /// <summary>FUN_141b71910 (util.cpp:1234): the id as text, as an unsigned 64-bit number.</summary>
    public static string IdText(long id) => unchecked((ulong)id).ToString(CultureInfo.InvariantCulture);
}
