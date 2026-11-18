namespace Ms2.Ui.Hud;

/// <summary>A message box's answer listener (IMessageListener with UIMsg 0: the button, then the tag). The return is the
/// listener's "handled" flag (UIMessageBox::FUN_1419f9410 closes the box when it is 0).</summary>
public interface IMessageBoxListener
{
    bool OnMessageBox(MessageBoxController sender, int button, string tag);
}

/// <summary>
/// CMsgBoxData (client/ms2/uimessageboxdata.h; ctor and setters unit_141a00db0.cpp, FillObj uimessageboxdata.cpp): the
/// record of one message box: its type, texts, buttons, tag, listener or callback, arguments, item uid, first item entry,
/// check boxes, text input and size. The second item, timers, buffs, group lists and terms of the other box types are not
/// ported.
/// </summary>
public sealed class MessageBoxData
{
    static uint lastId;

    public readonly int Type;
    public readonly uint Id;
    public string Msg = "", Msg2 = "", Title = "", Btn0 = "", Btn1 = "", Btn2 = "", Strike = "", Extra = "", Sound = "";
    /// <summary>m_str218: the tag of the last click (SetClickedTag).</summary>
    public string ClickedTag = "";
    public int Align = 4, Width, Height, Flags;
    public bool Modaless;
    /// <summary>m_20e: Enter answers the box (FUN_141a03090); FUN_141a024a0 clears it.</summary>
    public bool EnterAllowed = true;
    /// <summary>m_20f: set by FUN_141a024c0, Esc then does nothing (FUN_141a03100).</summary>
    public bool NoEscape;
    public IMessageBoxListener Listener;
    /// <summary>Delegate&lt;bool(int, const CMsgBoxData&amp;)&gt;: answers the box instead of the listener.</summary>
    public Func<int, MessageBoxData, bool> Callback;
    /// <summary>m_movie: the box's movie object of the click (InvokeCallback stores it).</summary>
    public object MovieBox;

    // check boxes (flag 8)
    public string Check0 = "", Check1 = "";
    public bool Check0Enabled = true, Check1Enabled = true, Check0Selected = true, Check1Selected = true, MustCheck;
    // Unk_MsgBoxChecks() @0x141a07430: both enabled and both selected.
    // text input (flag 2)
    public int InputType, InputMaxChars = 20, InputMaxLine, InputShowLimit;
    public string InputDefault = "", MaxLineMsg = "", CountMsg = "";

    /// <summary>m_args (a CAtlArray&lt;CStringW&gt;): the arguments a tag handler reads back, kept as text.</summary>
    public readonly List<string> Args = new();
    /// <summary>m_30 (SetItemUid @0x141a01600): the item a tag handler finds again (FindItemByUid).</summary>
    public long ItemUid;
    /// <summary>m_spItem (SetItemEntry @0x141a01ee0, flag 1): the item FillObj binds as "MsgBoxData_&lt;id&gt;".</summary>
    public MessageBoxItem ItemEntry;

    static readonly string[] AlignNames = { "left", "center", "right", "justify" };

    /// <summary>GetStringCommon by id for the ids the record uses itself (commonstringtable.cpp): the button presets and
    /// the input messages.</summary>
    public static readonly Dictionary<int, string> CommonKeys = new()
    {
        [0x475] = "s_word_ok", [0x476] = "s_word_cancel", [0x477] = "s_word_yes", [0x478] = "s_word_no",
        [0x5a0] = "s_word_exit_yes", [0x5a1] = "s_word_exit_no", [0x982] = "s_common_max_size", [0x984] = "s_common_text_count",
    };

    /// <summary>The common string table by key (set by the host; empty text until it is).</summary>
    public static Func<string, string> CommonByKey = _ => "";

    public static string Common(int id) => CommonKeys.TryGetValue(id, out var k) ? CommonByKey(k) ?? "" : "";

    /// <summary>CMsgBoxData(type) @0x141a00db0: a new id, then the type's buttons and sound.</summary>
    public MessageBoxData(int type)
    {
        Type = type;
        Id = Interlocked.Increment(ref lastId);
        SetLayoutAndSound();
    }

    /// <summary>SetLayoutAndSound @0x141a050b0.</summary>
    void SetLayoutAndSound()
    {
        int sound = 3;
        switch (Type)
        {
            case 1: case 0x14: case 0x18: SetButtonPreset(1); break;
            case 0: sound = 2; SetButtonPreset(3); break;
            case 5: case 6: case 7: case 8: case 9: case 10: case 0xb: case 0xc: case 0xd: case 0xf: case 0x12: case 0x15:
            case 0x16: case 0x19: case 0x1a:
                SetButtonPreset(2); break;
            case 0x17: SetButtonPreset(5); break;
            default: SetButtonPreset(3); break;
        }
        Sound = SoundName(sound);
    }

    /// <summary>GetSoundName @0x141a06ff0.</summary>
    public static string SoundName(int n) => n switch
    {
        1 => "dialog_open", 2 => "system_notice_error", 3 => "system_notice_confirm", _ => "",
    };

    /// <summary>SetButtonPreset @0x141a01800: 1 yes/no, 2 ok/cancel, 3 ok, 4 cancel, 5 exit yes/no.</summary>
    public void SetButtonPreset(int n)
    {
        switch (n)
        {
            case 1: (Btn0, Btn1, Btn2) = (Common(0x477), Common(0x478), ""); break;
            case 2: (Btn0, Btn1, Btn2) = (Common(0x475), Common(0x476), ""); break;
            case 3: (Btn0, Btn1, Btn2) = (Common(0x475), "", ""); break;
            case 4: (Btn0, Btn1, Btn2) = (Common(0x476), "", ""); break;
            case 5: (Btn0, Btn1, Btn2) = (Common(0x5a0), Common(0x5a1), ""); break;
        }
    }

    /// <summary>SetButtonLabels @0x141a01690.</summary>
    public void SetButtonLabels(string b0, string b1, string b2) => (Btn0, Btn1, Btn2) = (b0 ?? "", b1 ?? "", b2 ?? "");

    /// <summary>SetButtonLabelIds @0x141a01700: GetStringCommon of each id.</summary>
    public void SetButtonLabelIds(int a, int b, int c) => (Btn0, Btn1, Btn2) = (Common(a), Common(b), Common(c));

    /// <summary>Append&lt;unsigned&gt; (0x140534050, uimessageboxdata.h:216): an argument, as its decimal text.</summary>
    public void Append(uint n) => Args.Add(n.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>FUN_141a02e90 (unit_141a00db0.cpp:565): GetIntArg (actionkeyobject.cpp:3363) of the argument, _wtoi of its
    /// text; <paramref name="def"/> past the last argument.</summary>
    public uint ArgUInt(int index, uint def) => (uint)index < (uint)Args.Count ? (uint)WToI(Args[index]) : def;

    /// <summary>_wtoi: leading blanks, a sign, then the digits up to the first other character; 0 without digits.</summary>
    static int WToI(string s)
    {
        int i = 0, sign = 1;
        long v = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) sign = s[i++] == '-' ? -1 : 1;
        for (; i < s.Length && s[i] >= '0' && s[i] <= '9'; i++) v = Math.Min(v * 10 + (s[i] - '0'), 1L << 32);
        v *= sign;
        return v > int.MaxValue ? int.MaxValue : v < int.MinValue ? int.MinValue : (int)v;
    }

    /// <summary>SetItemEntry @0x141a01ee0: with an item, a new CMsgBoxDataItem (flag 1), its name through SetName (coloured
    /// when <paramref name="colorMode"/> is 1, by the item's rarity), the item, count, max, price and unit count.
    /// <paramref name="textColor"/> is CItemDataTable::GetTextColor.</summary>
    public void SetItemEntry(Items.Item item, string name, int colorMode, int count, int max, int price, int unitCount,
        Func<int, uint> textColor)
    {
        if (item == null) return;
        var e = new MessageBoxItem();
        Flags |= 1;
        e.SetName(name, colorMode == 1, item.Rarity, textColor);
        e.Item = item;
        e.Count = count;
        e.Max = max;
        e.Price = price;
        e.UnitCount = unitCount;
        ItemEntry = e;
    }

    /// <summary>SetCheckbox0 @0x141a024e0.</summary>
    public void SetCheckbox0(string label, bool selected) { Flags |= 8; Check0 = label ?? ""; Check0Selected = selected; }

    /// <summary>SetCheckbox1 @0x141a02560.</summary>
    public void SetCheckbox1(string label, bool selected) { Flags |= 8; Check1 = label ?? ""; Check1Selected = selected; }

    /// <summary>SetInput @0x141a01d30.</summary>
    public void SetInput(int mode, string def, int max, int showLimit, int maxLine)
    {
        Flags |= 2;
        InputType = mode; InputDefault = def ?? ""; InputMaxChars = max; InputShowLimit = showLimit; InputMaxLine = maxLine;
        MaxLineMsg = Common(0x982);
        CountMsg = Common(0x984);
    }

    /// <summary>SetSize @0x141a02770.</summary>
    public void SetSize(int w, int h) { Flags |= 0x10; Width = w; Height = h; }

    public bool HasCallback => Callback != null;

    /// <summary>HasTimer @0x141a049a0.</summary>
    public bool HasTimer => (Flags & 4) != 0;

    /// <summary>FUN_141a04900: a box without a timer is always live (the timer is not ported, so a timed box is not).</summary>
    public bool IsLive => !HasTimer;

    /// <summary>InvokeCallback @0x141a014d0.</summary>
    public bool InvokeCallback(int n, object movieBox)
    {
        if (Callback == null) return false;
        MovieBox = movieBox;
        return Callback(n, this);
    }

    /// <summary>HasDefaultButton @0x141a030b0: the input boxes take Enter themselves.</summary>
    public bool HasDefaultButton => Type is not (5 or 6 or 10 or 11 or 15);

    /// <summary>GetBoxTypeName @0x141a066e0.</summary>
    public static string BoxTypeName(int t) => t switch
    {
        0 or 1 => "ok", 2 => "ok_title", 3 => "three_button", 4 => "three_button_title", 5 => "input", 6 => "input_reset",
        7 => "input_meso", 8 => "input_meso_merat", 9 => "input_meso", 10 => "input_showerror", 0xb => "input_textarea",
        0xc => "input_apartment", 0xd => "input_item", 0xe => "input_checkbox", 0xf => "input_multi", 0x10 => "scrollText",
        0x11 => "itemNotify", 0x12 => "shopTrade", 0x13 => "maintenance", 0x14 => "profile_ok", 0x15 => "groupEdit",
        0x16 => "groupList", 0x17 => "imageYesNo", 0x18 => "vertical", 0x19 => "checkBox", 0x1a => "checkbox_slot",
        0x1b => "terms", 0x1c => "terms_coupon", 0x1d => "market_retract", 0x1e => "ugcterms", 0x1f => "simple_terms",
        0x20 => "buffNotify", 0x21 => "itemlock_confirm", 0x22 => "itemlock_notify", 0x23 => "itemCrafting",
        0x24 => "buffNotify_autoAction", 0x25 => "clubbuff", 0x26 => "buffNotify_inputBox", 0x27 => "ugcevent",
        0x28 => "monologue", 0x29 => "multiOpenBox", 0x2a => "two_slot", _ => "ok",
    };

    /// <summary>GetTextTypeName @0x141a06e80.</summary>
    public static string TextTypeName(int t) => t switch
    {
        1 => "number", 2 => "charname", 3 => "housename", 4 => "maidname", 5 => "numberText", _ => "text",
    };

    /// <summary>FillObj @0x141a034d0, for the members this port keeps: the common members, the input (2), the first item
    /// (1), the check boxes (8) and the size (0x10); the timer, second item, link and per-type members are not ported.
    /// <paramref name="showModaless"/> is FUN_14151f9c0, <paramref name="rbMerat"/> is feature 0x61. <paramref name="model"/>
    /// holds the item slot bindings (CreateItemInfoSlot); without it the item block is left out.</summary>
    public Dictionary<string, object> FillObj(bool showModaless, bool rbMerat = false, UiModel model = null)
    {
        var o = new Dictionary<string, object>
        {
            ["boxType"] = BoxTypeName(Type), ["sID"] = Id, ["msg"] = Msg, ["msg2"] = Msg2, ["btnLabel0"] = Btn0,
            ["btnLabel1"] = Btn1, ["btnLabel2"] = Btn2, ["title"] = Title, ["modaless"] = Modaless,
            ["showModaless"] = showModaless && Modaless ? 1 : 0, ["strikeStr"] = Strike,
        };
        if (Align != 4) o["textAlign"] = AlignNames[Align];
        if (Extra.Length > 0) o["extra"] = Extra;
        if ((Flags & 2) != 0)
        {
            o["textType"] = TextTypeName(InputType);
            o["inputDefault"] = InputDefault;
            o["inputMaxChars"] = InputMaxChars;
            o["inputTextShowLimit"] = InputShowLimit;
            o["inputMaxLine"] = InputMaxLine;
            o["maxLineMsg"] = MaxLineMsg;
            o["textCountMsg"] = CountMsg;
        }
        if ((Flags & 1) != 0 && ItemEntry != null && model != null)
        {
            // uimessageboxdata.cpp:156-176: the bag "MsgBoxData_<id>" is bound again (DestroyItemInfoSlot, then
            // CreateItemInfoSlot(bag, item, 0, m_48, 0)) and is the entry's bind id.
            var e = ItemEntry;
            string bag = "MsgBoxData_" + Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            model.ItemSlots.Remove(bag);
            model.ItemSlots[bag] = new Items.ItemInfoSlot(bag, e.Item, 0, e.IconType, 0);
            e.BindId = bag;
            o["bagId"] = e.BindId;
            o["itemName"] = e.Name;
            if (e.Count != -1)
            {
                o["itemCount"] = e.Count;
                o["itemMax"] = e.Max;
                o["itemUnitCount"] = e.UnitCount;
                if (e.Price != -1)
                {
                    o["itemTotalPrice"] = (long)e.Price * e.Count;
                    o["itemPrice"] = e.Price;
                }
            }
        }
        if ((Flags & 8) != 0)
        {
            if (Check0.Length > 0) o["checkbox0"] = Check0;
            if (Check1.Length > 0) o["checkbox1"] = Check1;
            if (!Check0Enabled) o["check0Enabled"] = false;
            if (!Check1Enabled) o["check1Enabled"] = false;
            o["check0Selected"] = Check0Selected;
            o["check1Selected"] = Check1Selected;
            o["mustCheck"] = MustCheck;
        }
        if ((Flags & 0x10) != 0)
        {
            o["width"] = Width;
            o["height"] = Height;
        }
        if (rbMerat) o["rbmerat"] = true;
        return o;
    }

    /// <summary>The record's item bindings go (CMsgBoxDataItem's destructor @0x141a06590: DestroyItemInfoSlot of its bind
    /// id). The layer calls it when the box is let go.</summary>
    public void ReleaseItems(UiModel model)
    {
        if (ItemEntry is { BindId.Length: > 0 } e) model?.ItemSlots.Remove(e.BindId);
    }
}

/// <summary>CMsgBoxDataItem (uimessageboxdata.h:50, 0x50): one item of a box: its shown name, the item, the counts and price,
/// the slot's icon type (m_48) and the bind id FillObj gives it. The constructor @0x141a064e0 sets count, max and price -1,
/// the unit count 1 and m_48 0.</summary>
public sealed class MessageBoxItem
{
    public string Name = "", BindId = "";
    public Items.Item Item;
    public int Count = -1, Max = -1, Price = -1, UnitCount = 1;
    /// <summary>m_48: CreateItemInfoSlot's icon type (0: the slot's own 0x415).</summary>
    public int IconType;

    /// <summary>SetName @0x141a06630: the name; with colour, FUN_14183e930(name, GetTextColor(rarity)), the
    /// &lt;FONT color='#%x'&gt;%s&lt;/FONT&gt; wrap.</summary>
    public void SetName(string name, bool color, int rarity, Func<int, uint> textColor)
    {
        Name = name ?? "";
        if (color && textColor != null) Name = $"<FONT color='#{textColor(rarity):x}'>{name}</FONT>";
    }
}
