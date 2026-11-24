using Ms2.Formats.World;
using Ms2.Net.Wire;
using Ms2.Net.Packets;
using Ms2.Quest;

namespace Ms2.Ui.Hud;

/// <summary>The menu's target (Unk_PmTarget): what Open shows in the header and what the item adders test.</summary>
public sealed class PopupMenuTarget
{
    public float X, Y;              // m_pos: the info's x, y for modes 2 and 3
    public System.Numerics.Vector3 World;   // m_pos for mode 1: the PC's position (vfunc_18, MS2 axes)
    public string Name = "";        // m_strName
    public long CharacterId;        // m_nCharId
    public int Level;               // m_nLevel
    public string Image = "";       // m_strImage
    public bool Alive;              // m_b38
    public bool Online;             // m_bOnline
    public int Trophy;              // m_nTrophy: the trophy triple's sum
    public int JobGroup;            // m_n20
    public int JobCode;             // m_nJobCode: the header's job icon frame ("_" + code)
    public bool ByName;             // OpenFromCharName: the buddy items read the list by the name
}

/// <summary>
/// The C++ side of the character popup menu, UIPopUpMenu (client/ms2/uipartydialog.cpp:1799; UI id 0x4e39, LoadSwf(0x1d) =
/// movie uipopupmenu, AS PopUpMenuFactory). Ported for the party window's member slots (UIPartyDialog::FUN_141a77760, :794):
/// the item list by the target and the own party state (FUN_141a83810, :2723) and the movie's "open" object (Open, :2454).
/// Of the items' handlers (RegisterMenuHandlers, :2417) the party ones are ported (leave, invite, kick and leader change
/// with their confirm boxes, vote kick, ready check), Whisper (the chat window's whisper start), Invite to My Home, View
/// Own Character, View Character (the other player's window, UIPCInfoDialog 0x4ec8), the furnishing rights grant
/// (listed only in the own home, see InOwnHome), the guild invite (<see cref="Guild"/>) and the buddy and block items (FUN_141a83ed0 to the community query dialog
/// 0x4e4e, <see cref="BuddyQuery"/>); the others raise <see cref="ItemClicked"/>.
/// 500 Trade is SendTradeRequest (<see cref="TradeBook.RequestTrade"/>; its block list check is STILL-OPEN there).
/// STILL-OPEN, by handler: EditBuddyMemo's
/// community dialog branch (0x4e4d open on its memo tab; the party slots' m_d0 is 0, so it is not taken); 0x1f5 Report (CUserReportData to the client platform's
/// vfunc_48); 0x1f6 group chat (FUN_141a86920 :3371 -> UIGroupMessengerBrowser FUN_141951f70, uigroupmessenger.cpp:1784: with no
/// group chat CGroupChatManager::Create sends GroupChat u8 1 + name, with any a choice box (type 0x16, common 0x81a) of
/// the chats; the chat count comes from the GroupChat packets, not decoded); 0x1f7 Visit House (FUN_141a86970 :3378: the checks
/// of FUN_1418fd330, uicommon.cpp:384, read the cube map service and PC states not ported, then the 0x879 box whose yes,
/// FUN_1418fd740, uicommon.cpp:539, moves by the party member's account, RequestMoveField mode 2).
/// Also opened for the guild dialog's members (<see cref="OpenForGuildMember"/>).
/// </summary>
public sealed class PopupMenuController : UiController, IMessageBoxListener
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
    public const int CoupleAction = 0x1fc;

    /// <summary>The party window's Unk_PopupMenuInfo (FUN_141a77760): mode 3 (the movie position as given), the flags, type 1
    /// (TYPE_WITHCHARINFO, the header with the character) and the info string.</summary>
    public const int PartySlotMode = 3, PartySlotFlags = 0x63fe, PartySlotType = 1;
    public const string PartySlotInfo = "partySlot";
    /// <summary>UIPartySearchDialog::FUN_140f8e790's Unk_PopupMenuInfo: flags 0x40da (view, group chat, friend, guild invite, block,
    /// report; no party, trade or house items) and the info string.</summary>
    public const int PartySearchFlags = 0x40da;
    public const string PartySearchInfo = "partySearchSlot";
    /// <summary>OnGuildMemberPopupMenu's Unk_PopupMenuInfo (uiguilddialog.cpp:823): mode 3, flags 0x63de (the party slot's
    /// without trade), type 1, an empty info string.</summary>
    public const int GuildMemberFlags = 0x63de;
    /// <summary>FUN_141a8ab90's Unk_PopupMenuInfo for a PC clicked in the field (uipartydialog.cpp:3776): mode 1 (placed beside
    /// the PC on screen), flags 0x3f3fe, type 1, an empty info string; PopUpMenu's own type 10.</summary>
    public const int FieldPcMode = 1, FieldPcFlags = 0x3f3fe, FieldPcType = 1;

    /// <summary>OpenCoupleActionDialog (:3435), the couple action item (0x1fc): the dialog 0x4f4e opened when closed, then
    /// SetSelectedCharacter(m_a8), the target's character id.</summary>
    public event Action<long> OpenCoupleAction;
    /// <summary>CameraMgr::FUN_140af0700 (camera.cpp:1553) times GetMovieScale: a world point (MS2 axes) to the movie's
    /// position; null when it does not project (no camera, or WorldPtToScreenPt3 fails).</summary>
    public Func<System.Numerics.Vector3, (float X, float Y)?> WorldToMovie = _ => null;
    /// <summary>GetCameraHeightOffset (@0x141a83db0, <see cref="CameraHeightOffset"/>) of the field camera, times the movie
    /// scale; 0 without a camera mode.</summary>
    public Func<float> MenuHeightOffset = () => 0;
    /// <summary>The client constant OffsetPcPopupMenu (+0x798, 0 unless table/constants.xml sets it): CalcMenuPosition's step
    /// along the world x axis.</summary>
    public float OffsetPcPopupMenu;

    /// <summary>
    /// GetCameraHeightOffset (@0x141a83db0, uipartydialog.cpp:2795): with near the camera mode's nearest zoom (vfunc_15), far
    /// its farthest (vfunc_17, m_54) and height its distance (vfunc_19, m_50): 0 when far equals near; else
    /// ceil((far / height) * ((far - height) / (far - near)) * 60 * screenHeight / 1050), in floats as the client computes it.
    /// </summary>
    public static float CameraHeightOffset(float near, float far, float height, uint screenHeight)
    {
        float depth = far - near;
        if (depth == 0.0f) return 0;
        float a = -1.0f / depth, b = far / depth;
        float t = a * height + b;
        t *= 60.0f;
        float offset = far / height * t;
        float scale = screenHeight / 1050.0f;
        return MathF.Ceiling(offset * scale);
    }

    /// <summary>CGuildManager: the own guild (IsInGuild, FindMemberByName) for the guild invite item (FUN_141a84b40) and its
    /// handler (InviteToGuild, FUN_1413b71c0). Null = no guild.</summary>
    public GuildBook Guild;

    /// <summary>CPartyManager's party.</summary>
    public PartyBook Party = new();
    /// <summary>CBuddyManager's list (the buddy and block items' FindByAccount, FUN_141a83ed0's FindBuddy).</summary>
    public BuddyBook Buddies = new();
    /// <summary>FUN_141a83ed0's UIMsg to the 0x4e4e dialog's OnMessage (FUN_140ae5a30(0x4e4e)): the query, the kind, the name,
    /// the buddy id and the character id (FUN_140719530 strings). Null = no dialog, nothing happens.</summary>
    public Action<int, int, string, string, string> BuddyQuery;
    /// <summary>m_d0: the buddy id the menu was opened with. The party window's Unk_PopupMenuInfo (FUN_141a77760) leaves it
    /// 0, so FUN_141a83ed0 sends id 0 and skips FindBuddy for a party slot.</summary>
    public long BuddyId;
    /// <summary>DAT_143ab2828: the own character id.</summary>
    public long SelfCharacterId;
    /// <summary>The current field's data (GetFieldData); null = none, the menu stays allowed.</summary>
    public FieldData Field;
    /// <summary>WorldService::FUN_141bd1530: the character's PC is in the field (the trade item's test).</summary>
    public Func<long, bool> PlayerInField = _ => false;
    /// <summary>WorldService::FindProxyObjectByOwner (uipartysearchdialog.cpp:808): the field's user (FieldAddUser) with this
    /// character id, whose proxy object FUN_141a7eb60 reads; null = none in the field.</summary>
    public Func<long, UserBlock> FieldUser = _ => null;
    /// <summary>FUN_1418fd280 (uicommon.cpp:353): the current house is the own home (the furnishing rights item): the house
    /// owner (CubeMapInformationService FUN_1410c3180) is the own account (DAT_143ab2820). STILL-OPEN: the owner is set by
    /// CSceneStateGame::OnLoadUgcMap (RecvOp LoadUgcMap 0x7D, scenestategame.cpp:2252), which is not decoded, so false.</summary>
    public Func<bool> InOwnHome = () => false;
    /// <summary>FUN_141028be0 (ugcimagepath.cpp:417) with no download: the character's profile image path for its picture.</summary>
    public Func<long, string, string> ProfileImagePath = (_, _) => "";
    /// <summary>CAchieveManager::GetTally (the own PC's proxy tally, proxygameobject.cpp:354), summed. STILL-OPEN: the tally
    /// is RebuildTally over the started achievements (achievemanager.cpp:756) of RecvOp Achieve 0x5E, which is not decoded,
    /// so 0 (the own party record's trophy is not what the client reads).</summary>
    public Func<int> OwnTrophy = () => 0;
    /// <summary>PCBase::IsDead of the local player (the live MyCharacter.Dead); null: no live state, the current health stands in.</summary>
    public Func<bool?> LocalPlayerDead = () => null;

    /// <summary>MenuItemClick (vfunc_28, :1830) for an item whose handler is not ported (see the class note), with the target
    /// the menu was opened for.</summary>
    public event Action<int, PopupMenuTarget> ItemClicked;
    /// <summary>FUN_141a84d40 (:3116), View Own Character on the own PC: OpenDialog(0x2711), UIMyInfoDialog.</summary>
    public event Action OpenMyInfo;
    /// <summary>Unk_PmGetDialog(0x4ec8): the other player's window's request state; null = no such dialog, View Character
    /// does nothing.</summary>
    public PcInfoRequests CharacterInfo;
    /// <summary>The 0x4ec8 window is open (FUN_141924630: vfunc_55 == 0), for TryBeginRequest.</summary>
    public Func<bool> CharacterInfoOpen = () => false;
    /// <summary>CUIService::RequestCloseDialog(0x4ec8) before View Character's request.</summary>
    public event Action CloseCharacterInfo;
    /// <summary>The messenger state's browser (Unk_MdState m_48, UIMessengerBrowser); null = none, Whisper does nothing.</summary>
    public MessengerBrowser Messenger;
    /// <summary>FUN_140710590: a packet to the game server.</summary>
    public Action<IOutPacket> Send = _ => { };
    /// <summary>The trade manager (CTradeManagerEx) the Trade item (500) asks through.</summary>
    public TradeBook TradeBook;
    /// <summary>PlaySystemSound by its table/systemsound.xml key. STILL-OPEN: PC_InviteSuccess is byGender/byJob (a voice);
    /// the host's table resolves a key to its first soundID, not the own gender's and job's.</summary>
    public Action<string> PlaySound = _ => { };
    /// <summary>The message box queue's show (FUN_14151f150).</summary>
    public Action<MessageBoxData> ShowBox = MessageBoxQueue.Show;
    /// <summary>FUN_14151f200: the queue's next box, after OnMessage's answer.</summary>
    public Action NextBox = MessageBoxQueue.Next;

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

    /// <summary>
    /// vfunc_ugc_1 (@0x141a7e1a0), UIUGCEventDispatcher's profile picture: an open menu on that character shows it,
    /// SetProfileImage (:2618) setProfileImage(path) for a non-empty path. <paramref name="path"/> is
    /// GetProfileImagePath(key, 0).
    /// </summary>
    public void OnProfileImage(long characterId, string path)
    {
        if (closed || target == null || target.CharacterId != characterId || string.IsNullOrEmpty(path)) return;
        target.Image = path;
        Host?.Invoke(Host.Entity, "setProfileImage", path);
    }

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
    void OpenForSelf(float x, float y, PlayerState self, int flags = PartySlotFlags, string infoString = PartySlotInfo)
    {
        if (mode == PartySlotMode && entityKey == SelfCharacterId && SelfCharacterId != 0) return;
        ClosePopup();
        if (string.IsNullOrEmpty(self.Name)) return;                    // GetTargetName gave nothing: "Empty Menu"
        ApplyMenuInfo(PartySlotMode, PartySlotType, infoString);
        entityKey = SelfCharacterId;
        var own = Party.Find(SelfCharacterId)?.User;
        var t = new PopupMenuTarget
        {
            X = x, Y = y, Name = self.Name, CharacterId = SelfCharacterId, Level = (ushort)self.Level,
            Image = ProfileImagePath(SelfCharacterId, own?.Picture ?? ""),
            Alive = !(LocalPlayerDead() ?? self.HpCur <= 0),           // IsDead == 0; no live state (offline): the HP stands in
            Online = true, JobGroup = self.JobGroup, JobCode = self.SubJob, Trophy = OwnTrophy(),
        };
        if (!Open(t, flags, 1)) ResetState();
    }

    /// <summary>
    /// UIPartySearchDialog::FUN_140f8e790 (@0x140f8e790, uipartysearchdialog.cpp:791), the right click on a listing's chief name at
    /// the movie position (x, y), the info (mode 3, <see cref="PartySearchFlags"/>, "partySearchSlot"). The chief is the local
    /// character: PopUpMenu on the own PC (kind 1, type 1). Another character: FindProxyObjectByOwner gives a proxy in the field
    /// (<see cref="OpenForFieldPlayer"/>, FUN_141a7eb60, type 1, kind 2);
    /// none: FUN_141a7f090 (:2102), OpenFromCharName (:2537) with type 0 for the chief's name, the account and the character id
    /// (kind 2). No local player (the world's), nothing. <paramref name="accountId"/> is accepted and ignored: the client's m_a0
    /// only feeds CUserReportData, which this menu does not send.
    /// </summary>
    public void OpenForPartySearch(float x, float y, long accountId, long characterId, string name, PlayerState self)
    {
        if (self == null || SelfCharacterId == 0) return;
        if (characterId == SelfCharacterId) { OpenForSelf(x, y, self, PartySearchFlags, PartySearchInfo); return; }
        if (FieldUser(characterId) is { } proxy) { OpenForFieldPlayer(x, y, proxy, PartySearchFlags, PartySearchInfo); return; }
        ClosePopup();
        ApplyMenuInfo(PartySlotMode, 0, PartySearchInfo);
        if (!OpenFromCharName(x, y, accountId, characterId, name, PartySearchFlags)) ResetState();
    }

    /// <summary>
    /// UIPopUpMenu::FUN_141a7eb60 (@0x141a7eb60, uipartydialog.cpp:2065), the menu of a player in the field (a proxy that is
    /// another player or the own PC), the info of mode 3, type 1, the given flags and string: ClosePopup; a proxy with no
    /// name opens nothing; ApplyMenuInfo; the target from the proxy (its level, the profile image of its portrait, the name,
    /// online, alive, the trophy tally's sum, the job group and the job code); the buddy it finds by FindBuddy(0, id, name) gives
    /// m_d0; Open with the flags, kind 1 for the own PC and 2 for another. STILL-OPEN: the home block lookup (m_d8,
    /// HomeBlockManager::FindByAccountId) is only read by handlers not ported; GetTally is RebuildTally over the achievements
    /// (RecvOp Achieve 0x5E, not decoded), so the three achievement counts of the FieldAddUser block stand in for the trophy.
    /// </summary>
    void OpenForFieldPlayer(float x, float y, UserBlock proxy, int flags, string infoString)
    {
        ClosePopup();
        if (string.IsNullOrEmpty(proxy.Name)) return;
        ApplyMenuInfo(PartySlotMode, PartySlotType, infoString);
        var t = new PopupMenuTarget
        {
            X = x, Y = y, Name = proxy.Name, CharacterId = proxy.CharacterId, Level = (ushort)proxy.Level,
            Image = ProfileImagePath(proxy.CharacterId, proxy.Picture ?? ""), Alive = true, Online = true,
            Trophy = proxy.AchievementCombat + proxy.AchievementAdventure + proxy.AchievementLifestyle,
            JobGroup = proxy.JobCode, JobCode = proxy.Job,              // the user block's two job ints: the group, then the code
        };
        if (Buddies.FindBuddy(0, proxy.CharacterId, proxy.Name) is { } buddy) BuddyId = buddy.Id;
        int kind = proxy.CharacterId == SelfCharacterId ? 1 : 2;
        if (!Open(t, flags, kind)) ResetState();
    }

    /// <summary>OpenFromCharName (:2537) through FUN_141a834e0 (:2686) with nMode 2 and nDir 1: the header is the name only (no
    /// level, picture, trophy or job), the character counts as online, the buddy items read the list by the name. STILL-OPEN: the client returns 0 when the
    /// camera manager's FUN_140a18070() is 0 (uipartydialog.cpp:2540); that check is not modelled.</summary>
    bool OpenFromCharName(float x, float y, long accountId, long characterId, string name, int flags)
    {
        if (Host?.Entity == null) return false;                         // m_30
        if (string.IsNullOrEmpty(name)) return false;
        entityKey = 0;
        var t = new PopupMenuTarget { X = x, Y = y, Name = name, CharacterId = characterId, Alive = true, Online = true, ByName = true };
        items.Clear();
        BuildItems(flags, 2, t);
        if (items.Count == 0) return false;
        var menu = new List<object>();
        foreach (int id in items)
            menu.Add(new Dictionary<string, object> { ["id"] = (uint)id, ["label"] = Model.Tables.PopupMenuName(id) });
        var obj = new Dictionary<string, object>
        {
            ["type"] = menuType, ["menu"] = menu, ["posx"] = x, ["posy"] = y, ["name"] = name, ["dirx"] = 1.0f, ["diry"] = 1,
        };
        var result = Host.Invoke(Host.Entity, "open", obj);
        if (result is not Avm2.AsObject) return false;
        closed = false;
        target = t;
        list = result;
        Host.SetVisible(Host.Entity, true);
        return true;
    }

    /// <summary>PopUpMenuFromPartyMember (:2118): the target from the member's user record, Open with kind 2.</summary>
    void OpenForPartyMember(float x, float y, PartyMember m)
    {
        ClosePopup();
        var u = m.User;
        if (string.IsNullOrEmpty(u.Name)) return;
        ApplyMenuInfo(PartySlotMode, PartySlotType, PartySlotInfo);
        var t = new PopupMenuTarget
        {
            X = x, Y = y, Name = u.Name, CharacterId = u.CharacterId, Level = (ushort)u.Level,
            Image = ProfileImagePath(u.CharacterId, u.Picture), Alive = u.Hp > 0, Online = !m.Offline,
            Trophy = u.Trophy.Item1 + u.Trophy.Item2 + u.Trophy.Item3, JobGroup = u.JobGroup, JobCode = u.Job,
        };
        if (!Open(t, PartySlotFlags, 2)) ResetState();
    }

    /// <summary>
    /// PopUpMenuFromGuildMember (:2150), the guild dialog's member right click at the movie position (x, y): the target from
    /// the member's data (its level, character id, profile picture, name, alive, online when f158 is 0, the trophy sum, the
    /// job group and job code), then Open with the guild member's flags, kind 1 for the own character and 2 for another.
    /// </summary>
    public void OpenForGuildMember(float x, float y, GuildMember m)
    {
        ClosePopup();
        if (string.IsNullOrEmpty(m.Name)) return;                       // "UIPopUpMenu::PopUpMenuFromGuildMember Empty Menu"
        ApplyMenuInfo(PartySlotMode, PartySlotType, "");
        var t = new PopupMenuTarget
        {
            X = x, Y = y, Name = m.Name, CharacterId = m.CharId, Level = (ushort)m.Level,
            Image = ProfileImagePath(m.CharId, m.Code), Alive = true, Online = m.Offline == 0,
            Trophy = m.TrophyTotal, JobGroup = m.F14, JobCode = m.JobCode,
        };
        int kind = m.CharId != 0 && m.CharId == SelfCharacterId ? 1 : 2;
        if (!Open(t, GuildMemberFlags, kind)) ResetState();
    }

    /// <summary>
    /// FUN_141a8ab90 (uipartydialog.cpp:3760) for a PC picked in the field, then PopUpMenu (:1973): nothing when the menu is
    /// already open in mode 1 on that PC (<paramref name="entityKey"/>, GetEntityIDValue); else the open menu closes, and
    /// with the target's kind (GetTargetKind: 1 the own PC or the own mount, 2 another PC) and a name the target is
    /// opened in mode 1 with <see cref="FieldPcFlags"/>. The target is the PC's: its position, level, character id,
    /// profile image, name, alive, online, job group, job code and trophy sum.
    /// </summary>
    public void OpenOnFieldPc(long entityKey, int kind, PopupMenuTarget t)
    {
        if (mode == FieldPcMode && this.entityKey == entityKey) return;      // ResetState zeroes both
        ClosePopup();
        if (kind == 0 || kind == 3) return;
        if (string.IsNullOrEmpty(t?.Name)) return;                      // "UIPopUpMenu::PopUpMenu Empty Menu"
        ApplyMenuInfo(FieldPcMode, FieldPcType, "");
        this.entityKey = entityKey;
        t.Online = true;
        if (!Open(t, FieldPcFlags, kind)) ResetState();
    }

    /// <summary>ApplyMenuInfo (:2403): the info's mode (m_80), type (m_84) and string (m_88).</summary>
    void ApplyMenuInfo(int infoMode, int type, string infoString)
    {
        mode = infoMode;
        menuType = type;
        info = infoString;
        entityKey = 0;
        BuddyId = 0;                                                    // m_d0 = 0
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
        float dirX = 1.0f, x = t.X, y = t.Y, offset = 0;
        int dirY = 1;
        if (mode == FieldPcMode)
        {
            // :2475-2489: beside the PC (CalcMenuPosition: + dir * OffsetPcPopupMenu on the world x axis), the other side
            // when that does not project; then the camera height offset
            dirY = 2;
            var at = WorldToMovie(t.World + new System.Numerics.Vector3(dirX * OffsetPcPopupMenu, 0, 0));
            if (at == null)
            {
                dirX = -1.0f;
                at = WorldToMovie(t.World + new System.Numerics.Vector3(dirX * OffsetPcPopupMenu, 0, 0));
            }
            if (at is not { } placed) return false;                     // !bPlaced
            (x, y) = placed;
            offset = MenuHeightOffset();
        }
        var menu = new List<object>();
        foreach (int id in items)                                       // BuildMenuItemArray (:2876)
            menu.Add(new Dictionary<string, object> { ["id"] = (uint)id, ["label"] = Model.Tables.PopupMenuName(id) });
        var obj = new Dictionary<string, object>
        {
            ["type"] = menuType, ["menu"] = menu, ["posx"] = x, ["posy"] = y - offset, ["name"] = t.Name,
            ["dirx"] = dirX, ["diry"] = dirY, ["level"] = t.Level, ["imgpath"] = t.Image, ["trophy"] = t.Trophy, ["jobcode"] = t.JobCode,
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
    /// FUN_141a83810 (:2723) for the party slot's (0x63fe) or guild member's (0x63de) flags: trade, the character items, the group chat, the house
    /// visit, the home invite, the buddy item, the party items, the guild invite, the furnishing rights, the block items
    /// and the report, in that order. The buddy and block items read the buddy list by the target's character id
    /// (CBuddyManager::FindByAccount, :2748 and :2759). The guild invite item reads <see cref="Guild"/> (CGuildManager).
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
        if ((flags & 8) != 0 && kind != 1 && (id != 0 || t.ByName)) AddBuddyItem(t.ByName ? Buddies.FindByName(t.Name) : Buddies.FindByAccount(id));             // FUN_141a847c0 (:2963)
        if ((flags & 4) != 0) AddPartyItems(kind, t.Name, online);
        if ((flags & 0x4000) != 0 && kind != 1 && Guild is { InGuild: true } g && g.FindMemberByName(t.Name) == null)
            Add(InviteToGuild);                                                                            // FUN_141a84b40 (:3075)
        if ((flags & 0x2000) != 0 && kind == 2 && OtherCharacter(id) && InOwnHome() && Registered(GrantFurnishingRights))
            Add(GrantFurnishingRights);                                                                    // AddRequestCubeMenu: FUN_1418fd670
        if ((flags & 0x10) != 0 && kind != 1 && (id != 0 || t.ByName)) AddBlockItems(t.ByName ? Buddies.FindByName(t.Name) : Buddies.FindByAccount(id));         // FUN_141a84860 (:2984)
        // STILL-OPEN: 0x10000 AddProposeMenu, 0x20000 FUN_141a84cb0 (the home block items), 0x400 AddRequestPetMenu, 0x800
        // FUN_141a84a90 and 0x1000 (the home action items under feature 0x39) list nothing here
        if ((flags & 0x8000) != 0 && kind != 1) Add(CoupleAction);                                         // AddCoupleActionMenu (:3066)
        if ((flags & 0x40) != 0 && kind != 1 && online) Add(Report);                                       // AddReportMenu (:3011)
    }

    /// <summary>FUN_141a847c0 (:2963): no entry, 0x12d (add); a friend (IsFriend), 300 (remove); another entry, 0x12d unless
    /// it is blocked (FUN_1401cc5b0) or its request is waiting (FUN_1401cc660). (Its null local player test is
    /// FUN_141a83810's own.)</summary>
    void AddBuddyItem(BuddyInfo buddy)
    {
        if (buddy == null) Add(AddFriend);
        else if (!buddy.IsFriend)
        {
            if (buddy.Blocked || buddy.Waiting) return;
            Add(AddFriend);
        }
        else Add(Unfriend);
    }

    /// <summary>FUN_141a84860 (:2984): not blocked (or no entry), 400 (block); blocked, 0x191 (unblock) and 0x192 (the block
    /// memo).</summary>
    void AddBlockItems(BuddyInfo buddy)
    {
        if (buddy == null || !buddy.Blocked)
        {
            Add(Block);
            return;
        }
        Add(Unblock);
        Add(EditBlockReason);
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

    /// <summary>vfunc_28 (:1830) "MenuItemClick"(id): the item's handler from m_160; the menu stays as it is (the movie hides
    /// its list, and <see cref="Update"/> closes it).</summary>
    public override bool OnCallback(string name, object[] args)
    {
        if (!string.Equals(name, "MenuItemClick", StringComparison.OrdinalIgnoreCase)) return false;
        int id = args.Length > 0 ? (int)(uint)Avm2.Rt.ToNumber(args[0]) : 0;     // GetUInt
        if (Registered(id)) ItemClick(id);
        return false;
    }

    /// <summary>The handler of item <paramref name="id"/> (RegisterMenuHandlers, :2417) on the open target (m_90 its name,
    /// m_a8 its character id).</summary>
    public void ItemClick(int id)
    {
        string name = target?.Name ?? "";                               // m_90
        long charId = target?.CharacterId ?? 0;                         // m_a8
        switch (id)
        {
            case ViewOwnCharacter:                                      // FUN_141a84d40 (:3116)
                if (SelfCharacterId != 0 && SelfCharacterId == charId) OpenMyInfo?.Invoke();
                break;
            case ViewCharacter:                                         // SendCharacterInfo (:3135)
                ViewOtherCharacter(charId);
                break;
            case Trade:                                                 // SendTradeRequest (:3261)
                TradeBook?.RequestTrade(charId);
                break;
            case Whisper:                                               // FUN_141a84da0 (:3127): the state's browser (+0x48)
                Messenger?.Whisper(name);
                break;
            case LeaveParty:                                            // SendPartyMode3 (:3160) -> CPartyManager (partymanager.cpp:819)
                Send(new PartyLeaveRequest());
                break;
            case InviteToParty:                                         // InviteToParty (:3166) -> SendPartyMode1 (partymanager.cpp:761)
                if (name.Length != 0)
                {
                    Send(new PartyInviteRequest(name));
                    PlaySound("PC_InviteSuccess");                     // PlaySystemSound(DAT_143ae2ca8) after the send
                }
                break;
            case KickFromParty:                                         // ConfirmExpelPartyMember (:3173)
                if (name.Length != 0) Confirm("s_party_check_expel_member", "expelMember", name);
                break;
            case MakePartyLeader:                                       // ConfirmChangePartyChief (:3183)
                if (name.Length != 0) Confirm("s_party_check_change_chief", "changeChief", name);
                break;
            case VoteToKick:                                            // ShowVoteKickConfirm (:3193)
                if (charId != 0)
                {
                    // ShowYesNoMessageBox with FUN_14028dfa0 (the key's text formatted with the name); the box keeps the
                    // character id (Append<__int64>, read back by OnVoteKickMsgBox with FUN_141a02f10): captured here
                    string text = ClientFormat.Format(Model?.Tables.Common("s_party_vote_kick_user_confirm") ?? "", name);
                    ShowBox(MessageBoxQueue.CreateWithCallback(text, 1, (button, _) => OnVoteKickAnswer(button, charId)));
                }
                break;
            case ReadyCheck:                                            // FUN_141a853a0 -> SendPartyMode46 (partymanager.cpp:810)
                Send(new PartyReadyCheckRequest());
                break;
            case InviteToMyHome:                                        // SendRequestHome (:3385): FUN_1418fd580, uicommon.cpp:439
                if (Model != null && Model.HasLocalPlayer && OtherCharacter(charId)) Send(new HomeInviteRequest(name));
                break;
            case GrantFurnishingRights:                                 // SendRequestCube (:3424): feature 0x100
                if (Model != null && Model.IsFeatureEnabled(0x100)) Send(new FurnishingRightsRequest(name));
                break;
            case Unfriend:                                              // FUN_141a853c0 (:3211)
                if (name.Length != 0) QueryBuddy(1, 6, name, charId);
                break;
            case AddFriend:                                             // FUN_141a85410 (:3217)
                if (name.Length != 0) QueryBuddy(0, 1, name, charId);
                break;
            case Block:                                                 // FUN_141a85460 (:3224)
                if (name.Length != 0) QueryBuddy(0, 2, name, charId);
                break;
            case Unblock:                                               // FUN_141a854b0 (:3231)
                if (name.Length != 0) QueryBuddy(1, 7, name, charId);
                break;
            case EditBlockReason:                                       // EditBuddyMemo (:3238): the 0x4e4d branch needs m_d0
                if (name.Length != 0) QueryBuddy(0, 4, name, charId);
                break;
            case InviteToGuild:                                         // InviteToGuild (:3482): FUN_1413b71c0(m_90)
                if (name.Length != 0) Guild?.Invite(name);
                break;
            case CoupleAction:                                          // OpenCoupleActionDialog (:3435)
                OpenCoupleAction?.Invoke(charId);
                break;
            default:
                ItemClicked?.Invoke(id, target);
                break;
        }
    }

    /// <summary>
    /// FUN_141a83ed0 (:2816): with a buddy id (m_d0), the entry FindBuddy(m_d0, m_a8, m_90) gives its own id, and an add query
    /// (kind 1) for an entry whose request was received (FUN_1401cc5d0) shows the 0x623 box
    /// (s_buddy_err_already_receive_from_this_char, FUN_140d73bc0) instead. Then UIMsg(query, kind, m_90, the buddy id and
    /// m_a8 as strings, FUN_140719530) to the 0x4e4e dialog's OnMessage.
    /// </summary>
    void QueryBuddy(int query, int kind, string name, long charId)
    {
        long buddyId = BuddyId;
        if (buddyId != 0 && Buddies.FindBuddy(BuddyId, charId, name) is { } buddy)
        {
            buddyId = buddy.Id;
            if (kind == 1 && buddy.Received)
            {
                ShowBox(MessageBoxQueue.Create(0, Model?.Tables.Common(0x623) ?? "", null, ""));
                return;
            }
        }
        BuddyQuery?.Invoke(query, kind, name, buddyId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            charId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// SendCharacterInfo (:3135): with the 0x4ec8 dialog, nothing for no character (m_a8 0) or a closed menu (m_100);
    /// TryBeginRequest 1 shows ShowMessageBox(s_char_info_already_request, 0, none, ""), 2 does nothing, else the window
    /// is asked to close (RequestCloseDialog(0x4ec8)) and SendOp::CharacterInfo goes out with the id.
    /// </summary>
    void ViewOtherCharacter(long charId)
    {
        if (CharacterInfo == null) return;
        if (charId == 0 || closed) return;
        int state = CharacterInfo.TryBeginRequest(charId, CharacterInfoOpen());
        if (state == 1)
        {
            ShowBox(MessageBoxQueue.Create(0, Model?.Tables.Common("s_char_info_already_request") ?? "", null, ""));
            return;
        }
        if (state == 2) return;
        CloseCharacterInfo?.Invoke();
        Send(new CharacterInfoRequest(charId));
    }

    /// <summary>ShowMessageBox(FormatStringId(id, name), 1, this, "<paramref name="tag"/>:name"); the ids 0x33f and 0x340
    /// are the common strings s_party_check_change_chief and s_party_check_expel_member (commonstringtable.cpp:2432-2433).</summary>
    void Confirm(string key, string tag, string name)
    {
        string text = ClientFormat.Format(Model?.Tables.Common(key) ?? "", name);
        ShowBox(MessageBoxQueue.Create(1, text, this, $"{tag}:{name}"));
    }

    /// <summary>OnVoteKickMsgBox (:1946): yes sends SendPartyMode45 (partymanager.cpp:730), which needs the member
    /// (FindMemberByCharId); the box closes either way.</summary>
    bool OnVoteKickAnswer(int button, long charId)
    {
        if (button != 0) return true;
        if (Party.Find(charId) != null) Send(new PartyVoteKickRequest(charId));
        return true;
    }

    /// <summary>
    /// OnMessage (:1886) from the message box: on yes (0) the tag split on ":" into two: "changeChief" sends the leader
    /// change with the name, "expelMember" SendPartyMode4 (partymanager.cpp:717: the member by name, case-insensitive,
    /// and its character id; nothing without one). Then always FUN_14151f200, handled.
    /// </summary>
    public bool OnMessageBox(MessageBoxController sender, int button, string tag)
    {
        if (button == 0)
        {
            var parts = (tag ?? "").Split(':', 2);                      // FUN_14028cd00(tag, args, ":", 2)
            if (parts.Length == 2)
            {
                if (parts[0] == "changeChief") Send(new PartyLeaderChangeRequest(parts[1]));
                else if (parts[0] == "expelMember" && Party.FindByName(parts[1]) is { } m) Send(new PartyKickRequest(m.CharacterId));
            }
        }
        NextBox();
        return true;
    }
}
