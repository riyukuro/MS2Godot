using Ms2.Formats.World;
using Ms2.Net.Packets;

namespace Ms2.Ui.Hud;

/// <summary>The menu's target (Unk_PmTarget): what Open shows in the header and what the item adders test.</summary>
public sealed class PopupMenuTarget
{
    public float X, Y;              // m_pos: the info's x, y for modes 2 and 3
    public string Name = "";        // m_strName
    public long CharacterId;        // m_nCharId
    public int Level;               // m_nLevel
    public string Image = "";       // m_strImage
    public bool Alive;              // m_b38
    public bool Online;             // m_bOnline
    public int Trophy;              // m_nTrophy: the trophy triple's sum
    public int JobGroup;            // m_n20
    public int JobCode;             // m_nJobCode: the header's job icon frame ("_" + code)
}

/// <summary>
/// The C++ side of the character popup menu, UIPopUpMenu (client/ms2/uipartydialog.cpp:1799; UI id 0x4e39, LoadSwf(0x1d) =
/// movie uipopupmenu, AS PopUpMenuFactory). Ported for the party window's member slots (UIPartyDialog::FUN_141a77760, :794):
/// the item list by the target and the own party state (FUN_141a83810, :2723) and the movie's "open" object (Open, :2454).
/// The items' handlers (RegisterMenuHandlers, :2417: leave, invite, kick, leader change, trade, whisper ...) are not
/// ported: a click is raised only.
/// </summary>
public sealed class PopupMenuController : UiController
{
    public override string Movie => "uipopupmenu";
    protected override string EntityMethod => "test_makeData";     // only PopUpMenuFactory has it
    public override string TypeName => "PopUpMenu";                // GetTypeName (:1809)
    protected override bool IsDialog => false;                     // a UISwfEntity, loaded once (vfunc_2, :1815)

    // The menu ids (Unk_PmMenuId; their labels are string table 7 "popupMenu").
    public const int ViewOwnCharacter = 100, Whisper = 0x65, ViewCharacter = 0x66;
    public const int LeaveParty = 200, InviteToParty = 0xc9, KickFromParty = 0xca, MakePartyLeader = 0xcb, VoteToKick = 0xcc, ReadyCheck = 0xcd;
    public const int Unfriend = 300, AddFriend = 0x12d, Block = 400, Unblock = 0x191, EditBlockReason = 0x192;
    public const int Trade = 500, Report = 0x1f5, InviteToGroupChat = 0x1f6, VisitHouse = 0x1f7, InviteToMyHome = 0x1f8, GrantFurnishingRights = 0x1fb;
    public const int InviteToGuild = 0x2bd;

    /// <summary>The party window's Unk_PopupMenuInfo (FUN_141a77760): mode 3 (the movie position as given), the flags, type 1
    /// (TYPE_WITHCHARINFO, the header with the character) and the info string.</summary>
    public const int PartySlotMode = 3, PartySlotFlags = 0x63fe, PartySlotType = 1;
    public const string PartySlotInfo = "partySlot";

    /// <summary>CPartyManager's party.</summary>
    public PartyBook Party = new();
    /// <summary>DAT_143ab2828: the own character id.</summary>
    public long SelfCharacterId;
    /// <summary>The current field's data (GetFieldData); null = none, the menu stays allowed.</summary>
    public FieldData Field;
    /// <summary>WorldService::FUN_141bd1530: the character's PC is in the field (the trade item's test).</summary>
    public Func<long, bool> PlayerInField = _ => false;
    /// <summary>FUN_1418fd280 (uicommon.cpp:353): the current house is the own home (the furnishing rights item). STILL-OPEN:
    /// no house feed, so false.</summary>
    public Func<bool> InOwnHome = () => false;
    /// <summary>FUN_141028be0 (ugcimagepath.cpp:417) with no download: the character's profile image path for its picture.</summary>
    public Func<long, string, string> ProfileImagePath = (_, _) => "";
    /// <summary>CAchieveManager::GetTally (the own PC's proxy tally, proxygameobject.cpp:354), summed. STILL-OPEN: no
    /// achievement feed, so 0.</summary>
    public Func<int> OwnTrophy = () => 0;

    /// <summary>MenuItemClick (vfunc_28, :1830): the item's handler. Raised only, with the target it was opened for.</summary>
    public event Action<int, PopupMenuTarget> ItemClicked;

    int mode;                                   // m_80
    int menuType;                               // m_84
    string info = "";                           // m_88
    long entityKey;                             // m_98: the PC the menu was opened on (PopUpMenu), 0 for a party member
    bool closed = true;                         // m_100
    readonly List<int> items = new();           // m_118
    object list;                                // m_f0: the movie's PopupMenu, as "open" returned it
    PopupMenuTarget target;

    /// <summary>IsClosed (:2351).</summary>
    public bool IsClosed => closed;
    /// <summary>GetInfoString (:2363): the opener's tag ("partySlot").</summary>
    public string InfoString => info;
    /// <summary>The open menu's item ids, in order (m_118).</summary>
    public IReadOnlyList<int> Items => items;
    public PopupMenuTarget Target => target;

    /// <summary>
    /// UIPartyDialog::FUN_141a77760 (:794), a slot's right click at the movie position (x, y): the own slot opens PopUpMenu
    /// on the own PC (target kind 1), another member PopUpMenuFromPartyMember (kind 2).
    /// </summary>
    public void OpenForPartySlot(float x, float y, long characterId, PlayerState self)
    {
        if (characterId == SelfCharacterId) OpenForSelf(x, y, self);
        else if (Party.Find(characterId) is { } m) OpenForPartyMember(x, y, m);
    }

    /// <summary>
    /// PopUpMenu (:1973) for the own PC from a party slot: nothing when it is already open on that PC in the same mode; else
    /// the target from the PC (online, the profile image of its picture, the proxy's tally) and Open with kind 1.
    /// </summary>
    void OpenForSelf(float x, float y, PlayerState self)
    {
        if (mode == PartySlotMode && entityKey == SelfCharacterId && SelfCharacterId != 0) return;
        ClosePopup();
        if (string.IsNullOrEmpty(self.Name)) return;                    // GetTargetName gave nothing: "Empty Menu"
        ApplyMenuInfo();
        entityKey = SelfCharacterId;
        var own = Party.Find(SelfCharacterId)?.User;
        var t = new PopupMenuTarget
        {
            X = x, Y = y, Name = self.Name, CharacterId = SelfCharacterId, Level = (ushort)self.Level,
            Image = ProfileImagePath(SelfCharacterId, own?.Picture ?? ""),
            Alive = self.HpCur > 0,                                     // IsDead == 0 (STILL-OPEN: the HP stands in for the dead state)
            Online = true, JobGroup = self.JobGroup, JobCode = self.SubJob, Trophy = OwnTrophy(),
        };
        if (!Open(t, PartySlotFlags, 1)) ResetState();
    }

    /// <summary>PopUpMenuFromPartyMember (:2118): the target from the member's user record, Open with kind 2.</summary>
    void OpenForPartyMember(float x, float y, PartyMember m)
    {
        ClosePopup();
        var u = m.User;
        if (string.IsNullOrEmpty(u.Name)) return;
        ApplyMenuInfo();
        var t = new PopupMenuTarget
        {
            X = x, Y = y, Name = u.Name, CharacterId = u.CharacterId, Level = (ushort)u.Level,
            Image = ProfileImagePath(u.CharacterId, u.Picture), Alive = u.Hp > 0, Online = !m.Offline,
            Trophy = u.Trophy.Item1 + u.Trophy.Item2 + u.Trophy.Item3, JobGroup = u.JobGroup, JobCode = u.Job,
        };
        if (!Open(t, PartySlotFlags, 2)) ResetState();
    }

    /// <summary>ApplyMenuInfo (:2403) for the party slot's info.</summary>
    void ApplyMenuInfo()
    {
        mode = PartySlotMode;
        menuType = PartySlotType;
        info = PartySlotInfo;
        entityKey = 0;
    }

    /// <summary>
    /// Open (:2454): the movie loaded, the field allowing popup menus (m_bPopupMenu); the items (none: "Empty Menu"); the
    /// position as given for mode 3; then "open" with the menu and the header. A returned object opens it (m_100 0).
    /// The camera test FUN_140a18070 (CameraMgr m_1b0) has no feed and passes.
    /// </summary>
    bool Open(PopupMenuTarget t, int flags, int kind)
    {
        if (Host?.Entity == null) return false;
        if (Field != null && !Field.PopupMenu) return false;
        items.Clear();
        BuildItems(flags, kind, t);
        if (items.Count == 0) return false;
        var menu = new List<object>();
        foreach (int id in items)                                       // BuildMenuItemArray (:2876)
            menu.Add(new Dictionary<string, object> { ["id"] = (uint)id, ["label"] = Model.Tables.PopupMenuName(id) });
        var obj = new Dictionary<string, object>
        {
            ["type"] = menuType, ["menu"] = menu, ["posx"] = t.X, ["posy"] = t.Y, ["name"] = t.Name,
            ["dirx"] = 1.0f, ["diry"] = 1, ["level"] = t.Level, ["imgpath"] = t.Image, ["trophy"] = t.Trophy, ["jobcode"] = t.JobCode,
        };
        var result = Host.Invoke(Host.Entity, "open", obj);
        if (result is not Avm2.AsObject) return false;
        closed = false;
        target = t;
        list = result;
        Host.SetVisible(Host.Entity, true);                             // vfunc_8(1)
        return true;
    }

    /// <summary>
    /// FUN_141a83810 (:2723) for the party slot's flags (0x63fe): trade, the character items, the group chat, the house
    /// visit, the home invite, the buddy item, the party items, the guild invite, the furnishing rights, the block items
    /// and the report, in that order. No buddy list or guild is ported: every target counts as no buddy and the own
    /// character as in no guild.
    /// </summary>
    void BuildItems(int flags, int kind, PopupMenuTarget t)
    {
        if (string.IsNullOrEmpty(t.Name)) return;
        bool online = t.Online;
        long id = t.CharacterId;
        if ((flags & 0x20) != 0 && kind != 1 && online && id != 0 && PlayerInField(id)) Add(Trade);       // AddTradeMenu (:2996)
        if ((flags & 2) != 0)                                                                              // FUN_141a84520 (:2899)
        {
            if (kind == 1) Add(ViewOwnCharacter);
            else
            {
                if (id != 0) Add(ViewCharacter);
                if (online) Add(Whisper);
            }
        }
        if ((flags & 0x80) != 0 && kind == 2 && online) Add(InviteToGroupChat);                           // FUN_141a84980 (:3018)
        if ((flags & 0x100) != 0 && kind == 2 && OtherCharacter(id)) Add(VisitHouse);                     // FUN_141a849c0 (:3025)
        if ((flags & 0x200) != 0 && kind == 2 && OtherCharacter(id)) Add(InviteToMyHome);                 // AddRequestHomeMenu: FUN_1418fd5d0
        if ((flags & 8) != 0 && kind != 1 && id != 0) Add(AddFriend);                                      // FUN_141a847c0 (:2963), no buddy
        if ((flags & 4) != 0) AddPartyItems(kind, t.Name, online);
        // 0x4000 FUN_141a84b40 (:3074): InviteToGuild needs the own guild (CGuildManager::IsInGuild): none
        if ((flags & 0x2000) != 0 && kind == 2 && OtherCharacter(id) && InOwnHome() && Registered(GrantFurnishingRights))
            Add(GrantFurnishingRights);                                                                    // AddRequestCubeMenu: FUN_1418fd670
        if ((flags & 0x10) != 0 && kind != 1 && id != 0) Add(Block);                                       // FUN_141a84860 (:2984), no buddy
        if ((flags & 0x40) != 0 && kind != 1 && online) Add(Report);                                       // AddReportMenu (:3011)
    }

    /// <summary>AddPartyMenus (:2915).</summary>
    void AddPartyItems(int kind, string name, bool online)
    {
        bool leader = Party.IsLeader(SelfCharacterId);
        if (kind == 1)
        {
            if (Party.IsInParty)
            {
                Add(LeaveParty);
                if (leader) Add(ReadyCheck);
            }
            return;
        }
        if (string.IsNullOrEmpty(name)) return;
        bool member = Party.FindByName(name) != null;
        bool voteKick = Model.IsFeatureEnabled(0xe0);                   // PartyVoteKick
        if (!Party.IsInParty) { if (online) Add(InviteToParty); }
        else if (leader)
        {
            if (member)
            {
                Add(KickFromParty);
                if (online) Add(MakePartyLeader);
                if (voteKick) Add(VoteToKick);
            }
            else if (online) Add(InviteToParty);
        }
        else if (member && voteKick) Add(VoteToKick);
    }

    /// <summary>IsOtherCharacter / FUN_1418fd5d0 (uicommon.cpp:427, :451): not the own character.</summary>
    bool OtherCharacter(long id) => id != SelfCharacterId;

    /// <summary>The ids RegisterMenuHandlers (:2417) gives a handler (m_160); 0x1fb only with feature 0x100.</summary>
    static readonly HashSet<int> Handled = new()
    {
        100, 0x65, 0x66, 200, 0xc9, 0xca, 0xcb, 0xcc, 0xcd, 300, 0x12d, 400, 0x191, 0x192, 500, 0x1f5, 0x1f6, 0x1f7, 0x1f8,
        0x1f9, 0x1fa, 0x1fb, 0x1fc, 600, 0x259, 0x25a, 0x2bd, 0x321, 0x385, 0x386, 0x387,
    };
    bool Registered(int id) => Handled.Contains(id) && (id != GrantFurnishingRights || Model.IsFeatureEnabled(0x100));

    /// <summary>AddPopupMenu (:2890): only ids with a handler.</summary>
    void Add(int id) { if (Registered(id)) items.Add(id); }

    /// <summary>ClosePopup (:2337): the list's close and the state reset.</summary>
    public void ClosePopup()
    {
        if (closed) return;
        if (list != null) Host.Invoke(list, "close");
        ResetState();
    }

    /// <summary>ResetState (:2587).</summary>
    void ResetState()
    {
        list = null;
        info = "";
        entityKey = 0;
        closed = true;
        mode = 0;
        menuType = 0;
        target = null;
        items.Clear();
    }

    /// <summary>vfunc_15 (:1845), each frame: closed when the movie hid its list (a click, a click outside, a key, the wheel).</summary>
    public void Update()
    {
        if (closed) return;
        if (list == null || !Avm2.Rt.ToBoolean(Host.Get(list, "visible"))) ClosePopup();
    }

    /// <summary>FUN_141a80a50 (:2385): an open menu closes on a disband (7), and on a leader change (8) that leaves the own
    /// character the leader.</summary>
    public void OnPartyMessage(PartyMessage message)
    {
        if (closed) return;
        if (message is PartyDisband) ClosePopup();
        else if (message is PartyLeaderChange && Party.IsLeader(SelfCharacterId)) ClosePopup();
    }

    /// <summary>vfunc_28 (:1830) "MenuItemClick"(id).</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (!string.Equals(name, "MenuItemClick", StringComparison.OrdinalIgnoreCase)) return false;
        int id = args.Length > 0 ? (int)(uint)Avm2.Rt.ToNumber(args[0]) : 0;     // GetUInt
        if (Registered(id)) ItemClicked?.Invoke(id, target);
        return false;
    }
}
