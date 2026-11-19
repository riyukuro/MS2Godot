using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Quest;

namespace Ms2.Ui;

/// <summary>
/// What the community dialog is told about a buddy: the UIBuddyEventDispatcher calls of CBuddyManager::OnBuddy
/// (Unk_BuddyNotifications, its vfunc_1..vfunc_11), which UIBuddyEventListener passes on to UICommunityDialog.
/// </summary>
public enum BuddyEventKind
{
    /// <summary>vfunc_1: a request came in (protocol 9, Received).</summary>
    RequestReceived = 1,
    /// <summary>vfunc_2: our request was declined (protocol 4).</summary>
    Declined = 2,
    /// <summary>vfunc_3: an entry became a friend or our request is waiting (protocols 3, 9, 11).</summary>
    Accepted = 3,
    /// <summary>vfunc_4(id, n): an entry was blocked (protocol 5, n 1; protocol 9 of a blocked entry, n 0).</summary>
    Blocked = 4,
    /// <summary>vfunc_5: a friend or request was removed (protocol 7).</summary>
    Removed = 5,
    /// <summary>vfunc_6: an entry was unblocked (protocol 6), before it is removed.</summary>
    Unblocked = 6,
    /// <summary>vfunc_7(character id): after the unblocked entry is removed (protocol 6).</summary>
    UnblockedRemoved = 7,
    /// <summary>vfunc_8: our request was cancelled (protocol 17).</summary>
    Cancelled = 8,
    /// <summary>vfunc_9: an entry was read again (protocol 8).</summary>
    Updated = 9,
    /// <summary>vfunc_10(id, old field): a friend moved to another map or channel (protocol 8).</summary>
    Moved = 10,
    /// <summary>vfunc_11: a block memo was changed (protocol 10).</summary>
    MemoChanged = 11,
}

/// <summary>
/// The friend list, CBuddyManager (client/ms2/buddymanager.cpp), with what CBuddyManager::OnBuddy (@0x140ae2e90) does on
/// each Buddy packet (0x61). The list keeps the add order (m_list) with two maps, by id and by the character id. The client's
/// UI calls come out as events.
/// </summary>
public sealed class BuddyBook
{
    readonly List<BuddyInfo> list = new();                    // list, in add order
    readonly Dictionary<long, BuddyInfo> ids = new();         // ids
    readonly Dictionary<long, BuddyInfo> accounts = new();    // accounts, keyed on CBuddy's account field (FUN_1401cc230)

    /// <summary>GetStringCommon: the common string of an id.</summary>
    public Func<int, string> Common = _ => "";
    /// <summary>FUN_140710590: the send (SendOp::Buddy 0x30).</summary>
    public Action<IOutPacket> Send = _ => { };
    /// <summary>CGameOption's buddy auto reject (+0x144). STILL-OPEN: the game option is not ported, so false.</summary>
    public Func<bool> AutoReject = () => false;

    /// <summary>Unk_AkoMessage(id, 1, 1, x) and ShowSystemNotice: a notice of a text, its type and whether it also goes to
    /// the chat log.</summary>
    public event Action<string, int, bool> Notice;
    /// <summary>FUN_1412751b0(text, kind): a chat log line (OnBuddy always passes kind 12).</summary>
    public event Action<string, int> ChatLine;
    /// <summary>PlaySystemSound(key).</summary>
    public event Action<string> Sound;
    /// <summary>ShowMessageBox(text, 0, null, ""): a message box (protocol 18).</summary>
    public event Action<string> Box;
    /// <summary>The UIBuddyEventDispatcher calls: the kind, the entry id (the character id for UnblockedRemoved) and the
    /// second argument (Blocked: 1 when blocked now; Moved: the old field).</summary>
    public event Action<BuddyEventKind, long, int> BuddyEvent;
    /// <summary>Protocol 18: the query dialog (0x4e4e) closes when open, the community dialog (0x4e4d) runs
    /// BuildBuddyList(GetTab()).</summary>
    public event Action ListRebuild;
    /// <summary>The game menu's FUN_141937e70(protocol) at the end of OnBuddy: FUN_141938ae0(0x4e4d, 0).</summary>
    public event Action MenuCount;

    /// <summary>GetAllBuddies: the entries in add order.</summary>
    public IReadOnlyList<BuddyInfo> Buddies => list;

    /// <summary>GetBuddyCount.</summary>
    public int Count => list.Count;

    /// <summary>FUN_140ae4f50: the entries online.</summary>
    public int OnlineCount => list.Count(b => b.Online);

    /// <summary>FUN_140ae5070: the requests from others (f0), the game menu's community count.</summary>
    public int ReceivedCount => list.Count(b => b.Received);

    /// <summary>FindById (@0x140ae4b70).</summary>
    public BuddyInfo FindById(long id) => ids.TryGetValue(id, out var b) ? b : null;

    /// <summary>FindByAccount (@0x140ae4880): keyed on CBuddy's account field, the character id.</summary>
    public BuddyInfo FindByAccount(long characterId) => accounts.TryGetValue(characterId, out var b) ? b : null;

    /// <summary>FindByName (@0x140ae4950): CompareNoCase, the first in add order.</summary>
    public BuddyInfo FindByName(string name) =>
        list.FirstOrDefault(b => string.Compare(b.Name, name, StringComparison.OrdinalIgnoreCase) == 0);

    /// <summary>FUN_140ae4d70: the character is a friend (not blocked, no request either way).</summary>
    public bool IsFriend(long characterId) => FindByAccount(characterId) is { IsFriend: true };

    /// <summary>FUN_140ae51b0: the character is blocked.</summary>
    public bool IsBlocked(long characterId) => FindByAccount(characterId) is { Blocked: true };

    /// <summary>FUN_140ae5240: the named character is blocked.</summary>
    public bool IsBlockedName(string name) => FindByName(name) is { Blocked: true };

    /// <summary>AddBuddy (@0x140ae5630): a known id is left.</summary>
    public void Add(BuddyInfo b)
    {
        if (ids.ContainsKey(b.Id)) return;
        list.Add(b);
        ids[b.Id] = b;
        accounts[b.CharacterId] = b;
    }

    /// <summary>RemoveBuddy (@0x140ae5850).</summary>
    public void Remove(BuddyInfo b)
    {
        list.Remove(b);
        ids.Remove(b.Id);
        accounts.Remove(b.CharacterId);
    }

    /// <summary>ClearBuddies (@0x140ae5910), FUN_140ae5190.</summary>
    public void Clear() { list.Clear(); ids.Clear(); accounts.Clear(); }

    /// <summary>SendBuddyRequest (@0x140ae4820).</summary>
    public void Request(BuddyRequest r) => Send(r);

    /// <summary>SendBuddyMode20 (@0x140ae55a0).</summary>
    public void SendAutoReject() => Send(new BuddyAutoRejectRequest(AutoReject()));

    string F(int id, string name) => ClientFormat.Format(Common(id), name);
    void Chat(string text) => ChatLine?.Invoke(text, 12);
    void Ev(BuddyEventKind k, long id, int arg = 0) => BuddyEvent?.Invoke(k, id, arg);

    /// <summary>CBuddyManager::OnBuddy (@0x140ae2e90, buddymanager.cpp:95): the entries first, then the protocol's UI.</summary>
    public void Apply(BuddyMessage m)
    {
        BuddyInfo buddy = null;
        int field = 0, channel = 0;
        switch (m.Protocol)
        {
            case 15: Clear(); break;
            case 1:
                foreach (var e in m.Entries) Add(e);
                break;
            case 8: case 9:
            {
                var read = m.Entries.Count > 0 ? m.Entries[0] : null;
                if (read == null) break;
                buddy = FindById(read.Id);
                if (buddy == null)
                {
                    buddy = read;
                    if (m.Protocol == 9 && AutoReject() && buddy.Received)
                    {
                        Request(BuddyRequest.Decline(read.Id));
                        return;                     // before the game menu's count
                    }
                    Add(buddy);
                    buddy.IsNew = true;
                }
                else
                {
                    field = buddy.HomeMapId;        // FUN_1401cc3e0
                    channel = buddy.HomePlot;       // FUN_1401cc3a0
                    CopyFrom(buddy, read);          // ReadFromPacket into the known entry
                }
                break;
            }
        }
        switch (m.Protocol)
        {
            case 2:
                if (m.Result == 0) Notice?.Invoke(F(0x600, m.Name), 1, true);
                break;
            case 9:
                if (m.Result == 0 && buddy != null)
                {
                    if (buddy.Waiting) Ev(BuddyEventKind.Accepted, buddy.Id);
                    else if (buddy.Received)
                    {
                        Sound?.Invoke("buddy_request");
                        Chat(Common(0x607));
                        Ev(BuddyEventKind.RequestReceived, buddy.Id);
                    }
                    else if (buddy.Blocked) Ev(BuddyEventKind.Blocked, buddy.Id, 0);
                }
                break;
            case 8:
                if (buddy == null) break;
                Ev(BuddyEventKind.Updated, buddy.Id);
                if (buddy.IsFriend && (field != buddy.HomeMapId || channel != buddy.HomePlot))
                    Ev(BuddyEventKind.Moved, buddy.Id, field);
                break;
            case 7:
                if (m.Result == 0 && FindById(m.Id) is { } b7)
                {
                    if (b7.Waiting) Notice?.Invoke(F(0x602, b7.Name), 1, true);
                    else Chat(F(b7.Received ? 0x603 : 0x605, b7.Name));
                    Ev(BuddyEventKind.Removed, b7.Id);
                    Remove(b7);
                }
                break;
            case 6:
                if (m.Result == 0 && FindById(m.Id) is { } b6)
                {
                    Chat(F(0x606, b6.Name));
                    Ev(BuddyEventKind.Unblocked, b6.Id);
                    long cid = b6.CharacterId;
                    Remove(b6);
                    Ev(BuddyEventKind.UnblockedRemoved, cid);
                }
                break;
            case 3:
                if (m.Result == 0 && FindById(m.Id) is { } b3)
                {
                    b3.Received = false;
                    b3.IsNew = true;
                    Sound?.Invoke("buddy_agree");
                    Chat(F(0x5fd, b3.Name));
                    Ev(BuddyEventKind.Accepted, b3.Id);
                }
                break;
            case 4:
                if (m.Result == 0 && FindById(m.Id) is { } b4)
                {
                    Sound?.Invoke("buddy_deny");
                    Chat(F(0x601, b4.Name));
                    Ev(BuddyEventKind.Declined, b4.Id);
                    Remove(b4);
                }
                break;
            case 5:
                if (m.Result == 0 && FindById(m.Id) is { } b5)
                {
                    b5.Blocked = true;
                    b5.IsNew = true;
                    Chat(F(0x5fe, b5.Name));
                    Ev(BuddyEventKind.Blocked, b5.Id, 1);
                }
                break;
            case 11:
                if (FindById(m.Id) is { } b11)
                {
                    b11.Waiting = false;
                    b11.IsNew = true;
                    Chat(F(0x5fd, b11.Name));
                    Ev(BuddyEventKind.Accepted, b11.Id);
                }
                break;
            case 12:
                if (m.Result == 0) Chat(F(0x5fe, m.Name));
                else if (m.Result == 10) Notice?.Invoke(F(0x61b, m.Name), 1, true);
                break;
            case 13:
                foreach (var (name, action, _) in m.Answers)
                {
                    string message = "";
                    // operator==(CStringW, const char*): case-sensitive
                    if (action == "approved")
                    {
                        if (FindByName(name) is { } b) b.IsNew = true;
                        message = F(0x5fd, name);
                    }
                    else if (action == "declined") message = F(0x602, name);
                    else if (action == "removed") message = F(0x605, name);
                    Chat(message);                  // also when empty
                }
                break;
            case 14:
            {
                string message = m.Flag switch { 0 => F(0x608, m.Name), 1 => F(0x609, m.Name), _ => "" };
                Chat(message);
                break;
            }
            case 10:
            {
                var b10 = FindById(m.Id);
                if (m.Result == 0)
                {
                    Notice?.Invoke(F(0x5ff, m.Name), 1, true);
                    if (b10 != null)
                    {
                        b10.Memo = m.Memo;
                        Ev(BuddyEventKind.MemoChanged, b10.Id);
                    }
                }
                break;
            }
            case 17:
                if (m.Result == 0 && FindById(m.Id) is { } b17)
                {
                    Chat(F(0x604, b17.Name));
                    Ev(BuddyEventKind.Cancelled, b17.Id);
                    Remove(b17);
                }
                break;
            case 18:
                ListRebuild?.Invoke();
                Box?.Invoke(Common(0x8ba));         // BlockFormat(0x8ba) with no arguments
                break;
            case 20:
                SendAutoReject();
                break;
        }
        MenuCount?.Invoke();
    }

    /// <summary>CBuddy::ReadFromPacket into a known entry: every read field is replaced, IsNew is kept.</summary>
    static void CopyFrom(BuddyInfo to, BuddyInfo from)
    {
        bool isNew = to.IsNew;
        to.Id = from.Id; to.CharacterId = from.CharacterId; to.AccountId = from.AccountId;
        to.Name = from.Name; to.Message = from.Message;
        to.Channel = from.Channel; to.MapId = from.MapId; to.JobGroup = from.JobGroup; to.JobCode = from.JobCode; to.Level = from.Level;
        to.Received = from.Received; to.Waiting = from.Waiting; to.Blocked = from.Blocked; to.Online = from.Online; to.Away = from.Away;
        to.Value50 = from.Value50; to.Value58 = from.Value58;
        to.ProfilePath = from.ProfilePath; to.TodayMemo = from.TodayMemo; to.Memo = from.Memo;
        to.HomeMapId = from.HomeMapId; to.HomePlot = from.HomePlot; to.HomeApartment = from.HomeApartment; to.HomeName = from.HomeName;
        to.Value90 = from.Value90; to.Trophy = from.Trophy;
        to.IsNew = isNew;
    }
}
