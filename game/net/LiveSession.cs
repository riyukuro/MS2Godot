using Godot;
using Ms2.Formats.Model;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Guide;
using Ms2Godot.Flow;
using Ms2Godot.Ui;
using Ms2Godot.Ui.Contracts;
using Ms2Godot.World;

namespace Ms2Godot.Net;

/// <summary>
/// The opt-in live mode: the login, character list, select and field entry run against a real server instead of the offline
/// stubs. Off unless MS2_LIVE_LOGIN (host:port of the login server) and MS2_LIVE_ACCOUNT (user:password) are set; offline stays
/// the default and nothing here runs then. MS2_LIVE_AUTO=1 drives the screens by itself (first character, no input), for a
/// headless run. The transports only queue packets on their network thread; this node drains them in _Process.
/// </summary>
public partial class LiveSession : Node
{
    public static bool Enabled => !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("MS2_LIVE_LOGIN"))
                                  && !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("MS2_LIVE_ACCOUNT"));
    public static LiveSession Instance { get; private set; }
    /// <summary>A live session is running: the field's offline npcs, mobs and portal walking stay off; the server's replace them.</summary>
    public static bool Active => Instance != null;

    readonly byte[] machine = new byte[16];
    TcpTransport login, game;
    long accountId;
    GameEntry entry;
    string host;
    int selfObject = -1;     // the own FieldAddUser object id
    byte syncNumber;         // CNetworkService::OnSyncNumber: the number UserSync carries
    Player player;           // the own player, once the field is built
    RemoteLayer remote;      // the other players and npcs, once the field is built
    readonly FieldEntry fieldEntry = new();   // RequestFieldEnter: the current entry and its one ResponseFieldEnter
    public bool Busy { get; private set; }
    /// <summary>The last create the server refused (null after a create that went through).</summary>
    /// <summary>The account's character limit from CharMaxCount 0xB8 (DAT_1439f4a84); 0 until the server sent it.</summary>
    public int CharacterLimit;
    /// <summary>The quest state the quest packets build (live only; the quest log reads it, offline it stays empty).</summary>
    public readonly QuestBook Quests = new();
    /// <summary>CPartyManager's party from the party packets (0x53); live only.</summary>
    public readonly PartyBook Party = new();
    /// <summary>CGroupChatManager's chats from the group chat packets (0x8B); live only. The HUD sets its texts and its browser.</summary>
    public readonly Ms2.Ui.GroupChatBook GroupChats = new();
    /// <summary>The party window is loaded and open (FUN_141924630), for OnPartyMemberJoin's add-or-open; set by the HUD.</summary>
    public Func<bool> PartyWindowOpen = () => false;
    /// <summary>The game server session (its time sync), null before the field enter.</summary>
    public ClientSession GameSession => game?.Session;
    /// <summary>The entered character's id (DAT_143ab2828), 0 before the field enter.</summary>
    public long CharacterId => entry?.Enter?.CharacterId ?? 0;
    /// <summary>The field the server sent the character to (RequestFieldEnter), null before the field enter.</summary>
    public int? FieldId => entry?.Field?.MapId;
    /// <summary>The item inventory from the ItemInventory packets (OnItemInventory); live only.</summary>
    public readonly ItemBook Items = new();
    /// <summary>The inventory dialog model over Items; a UI binds it. Only its Sort sends RequestItemInventory (the sort request, 0x14199c230); a tab switch sends nothing.</summary>
    public InventoryView Inventory { get; private set; }
    public readonly GlobalFactor Factors = new();   // CGloabalFactor: exp/meso rates of OnGlobalFactor
    /// <summary>CKeyTable's server side: the key bindings and quick slot pages (current page included) of OnKeyTable; live only.</summary>
    public readonly ServerKeyTable KeyTable = new();
    /// <summary>Raised after a KeyTable packet was applied (its mode: 0 load, 7 pages).</summary>
    public event Action<int> KeyTableChanged;
    /// <summary>OnBadgeEquip mode 4: the error code (shown as a system notice, <see cref="Ms2.Ui.Hud.MyInfoController.GemEquipErrorKey"/>).</summary>
    public event Action<short> GemEquipError;
    /// <summary>OnBadgeEquip modes 0 and 1, for any PC of the field (the own one included): a gem put on or taken off.</summary>
    public event Action<BadgeEquipMessage> BadgeEquipped;
    /// <summary>OnTaxi, after its ids joined the found-taxi list (<see cref="StubHudData.FoundTaxis"/>).</summary>
    public event Action<Ms2.Net.Packets.TaxiFound> TaxiFound;
    /// <summary>OnNotice (0x71, scenestategame.cpp:9216): a system notice, routed by <see cref="Ms2.Ui.Hud.NoticeController"/>.</summary>
    public event Action<NoticeMessage> Notice;
    /// <summary>UIMessengerBrowser::vfunc_84 (0x1d, uimessenger.cpp:2829): a user's chat line, and the packet's reader for the
    /// <see cref="WeddingChatHall"/> after a type 0x19 line (the handler reads it only with feature 0x104).</summary>
    public event Action<ChatMessage, Func<WeddingChatHall>> UserChat;
    /// <summary>UIMessengerBrowser::vfunc_85 (0x1e, uimessenger.cpp:2963): the items the chat lines link, by uid.</summary>
    public event Action<ChatItemLinks> UserChatItemLink;
    /// <summary>The local player's skill book (JobSystem): the own FieldAddUser's block (OnAddUser, scenestategame.cpp:11135) and the
    /// Job packets (OnJob); null before one arrives.</summary>
    public SkillBook SkillBook { get; private set; }
    /// <summary>The skill point state of OnSkillPoint (0x44, scenestategame.cpp:4241).</summary>
    public readonly Ms2.Ui.Skills.SkillPoints SkillPoints = new();
    /// <summary>A SkillBookTree (0x10C) packet, for the skill window's vfunc_79.</summary>
    public event Action<SkillBookTreePacket> SkillBookTree;
    /// <summary>After each 0x44 (UIStatusEventDispatcher event 0xf).</summary>
    public event Action SkillPointsChanged;
    /// <summary>RequestSkillBookTree mode 0 (UISkillBookTreeDialog::OnLoadComplete): a read request for the saved tabs.</summary>
    public void RequestSkillBookTree() => Send(new SkillBookTreeRequest());
    /// <summary>Raised after the local player's skill book changed (the Job packet's mode).</summary>
    public event Action<int> SkillBookChanged;
    /// <summary>
    /// The local player's AdditionalCliList feed: the own FieldAddUser's list (DecodeListPacket, additionalcli_2.cpp:721, each
    /// one an add) and each OnBuff 0x47 (scenestategame.cpp:7591) whose owner is the local player. Null = the own actor was
    /// replaced (its list's dtor runs ClearAll, additionalcli_2.cpp:184). Kept until the HUD
    /// takes them (<see cref="TakeBuffs"/>), then raised.
    /// </summary>
    public event Action<BuffPacket> BuffReceived;
    /// <summary>g_upperTargetId: the ServerEnter object id (CSceneStateGame::OnServerEnter, scenestategame.cpp:2071), 0 before
    /// the entry.</summary>
    public int UpperTargetId => entry?.Enter?.ObjectId ?? 0;
    readonly Queue<BuffPacket> pendingBuffs = new();
    bool buffsTaken;
    /// <summary>The buff packets kept so far, and raise <see cref="BuffReceived"/> from now on.</summary>
    public BuffPacket[] TakeBuffs()
    {
        buffsTaken = true;
        var all = pendingBuffs.ToArray();
        pendingBuffs.Clear();
        return all;
    }
    void OwnBuff(BuffPacket b)
    {
        if (buffsTaken) BuffReceived?.Invoke(b);
        else pendingBuffs.Enqueue(b);
    }
    /// <summary>
    /// The Prestige packets (0x11C, CSceneStateGame::OnPrestige, scenestategame.cpp:8815), raised after the session's
    /// <see cref="Adventure"/> and the PC's level took them, for the HUD's part (status events, the dialog, the bar). A
    /// packet with no HUD to see it changes only the data, as the client's handler finds no dialog or bar then.
    /// </summary>
    public event Action<PrestigeMessage> PrestigeReceived;
    /// <summary>
    /// The Meso packet (0x39, CSceneStateGame::OnMeso, scenestategame.cpp:7006), raised after <see cref="Me"/> took the new
    /// balance: the change (new - old) and the packet's kind, for FUN_1413612c0's chat line (:7019). The balance itself
    /// reaches the HUD through <see cref="MyCharacter.Changed"/> (status event 8, :7020).
    /// </summary>
    public event Action<long, int> MesoChanged;
    /// <summary>The adventure data the client keeps in globals (DAT_143ab2958..3318): session-lived, so a field change keeps
    /// the exp, the filled level, the claimed levels and the missions.</summary>
    public readonly Ms2.Ui.AdventureData Adventure = new();
    bool? fishingFeature;
    /// <summary>Feature 0xb (the fishing book's packets), read like <see cref="MissionFeature"/>.</summary>
    bool FishingFeature()
    {
        if (fishingFeature is { } on) return on;
        try { on = Ms2.Ui.LobbyFeatures.IsEnabled(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir), Ms2.Ui.FeatureTable.Names[Ms2.Ui.Feature.Fishing - 1]); }
        catch (Exception e) { GD.Print($"live: feature table unavailable ({e.Message})"); on = false; }
        fishingFeature = on;
        return on;
    }
    bool? missionFeature;
    /// <summary>Feature 0xfb (the adventure missions), as UiModel.IsFeatureEnabled reads it.</summary>
    bool MissionFeature()
    {
        if (missionFeature is { } on) return on;
        try { on = Ms2.Ui.LobbyFeatures.IsEnabled(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir), Ms2.Ui.FeatureTable.Names[Ms2.Ui.Hud.AdventureLevelTable.MissionFeature - 1]); }
        catch (Exception e) { GD.Print($"live: feature table unavailable ({e.Message})"); on = false; }
        missionFeature = on;
        return on;
    }
    /// <summary>CCharacterAbilityManager (a singleton): the learned abilities, the last reset and the extra points, from the
    /// CharacterAbility packets (0xDF); session-lived, so the login load and a field change keep them.</summary>
    public readonly Ms2.Ui.CharacterAbilityData CharacterAbility = new();
    /// <summary>CCharacterAbilityTable, which mode 2 checks the id against (FUN_140f29cc0); set by the HUD that reads it.
    /// Before a HUD set it (only the login load comes then) an id is taken as it comes.</summary>
    public Ms2.Ui.Hud.CharacterAbilityTable CharacterAbilityTable;
    /// <summary>A CharacterAbility packet that changed <see cref="CharacterAbility"/> (mode 0, or mode 2 of a known id),
    /// for the dialog and the game menu (OnCharacterAbility tells them only then).</summary>
    public event Action<CharacterAbilityMessage> CharacterAbilityReceived;
    /// <summary>The fishing book's catch records (the Fishing packets, 0x73, modes 7 and 8; scenestategame.cpp, FUN_140cd9840);
    /// session-lived, so a field change keeps them.</summary>
    public readonly Ms2.Ui.FishingBookData FishingBook = new();
    /// <summary>A Fishing packet that changed <see cref="FishingBook"/>, for the open fishing book (it fills its tab again).</summary>
    public event Action<FishingMessage> FishingReceived;
    public CharacterCreateRefused LastRefusal { get; private set; }
    /// <summary>The own mastery exp, grade rewards received and gathering counts (OnMastery, UserEnv modes 8/9);
    /// session-lived, the exp read from the user block on the first use.</summary>
    public readonly Ms2.Ui.MasteryData Mastery = new();
    /// <summary>CMasteryTable, which the grade of an exp value comes from; set by the HUD that reads it.</summary>
    public Ms2.Ui.Items.MasteryRecipeTable MasteryTable;
    /// <summary>A Mastery packet (0xCD) after it was applied to <see cref="Mastery"/>; mode 0 carries the gain, the old
    /// grade and whether the grade changed.</summary>
    public event Action<MasteryMessage, uint, int, bool> MasteryReceived;
    /// <summary>A hit on the own player (OnSkillDamage 0x3D): the damage record's total (negative when the player is the
    /// victim), one per target record (MyPC::vfunc_113 posts NotifyDamagedMyPC of each, mypc.cpp:1265).</summary>
    public event Action<long> SelfDamaged;
    /// <summary>The region skill objects' ids of the field (OnRegionSkill 0x4C modes 0 and 1): mode 5's damage needs one
    /// (OnRegionSkillDamage drops the packet when GetRegionSkillObject finds none).</summary>
    readonly HashSet<int> regionSkills = new();
    /// <summary>UserEnv mode 8 or 9 replaced the gathering counts or the received rewards.</summary>
    public event Action MasteryEnvChanged;

    /// <summary>The local player's record for the character window: the own FieldAddUser, Stat, AttributePoint, UserEnv,
    /// HomeCommand and own AddPlayer proxy; live only.</summary>
    public readonly MyCharacter Me = new();
    /// <summary>CAchieveManager: the local player's trophies and their tally from the Achieve packets (0x5E); live only.
    /// The trophy data (Xml achieve/*.xml) is read at the first packet.</summary>
    public Ms2.Ui.AchieveBook Achieves => achieves ??= AchieveBook();
    Ms2.Ui.AchieveBook achieves;
    /// <summary>DAT_143ab30e0: the UserEnv mode 3 collection grades by id (the trophy rows' reqCondition marks).</summary>
    public IReadOnlyDictionary<uint, byte> CollectionGrades { get; private set; } = new Dictionary<uint, byte>();
    /// <summary>UserEnv mode 3 (OnUserEnv, scenestategame.cpp:4441-4458): the grades were replaced; the trophy dialog redraws
    /// (FUN_1418a8f80).</summary>
    public event Action CollectionChanged;
    /// <summary>The trophy dialog's sends (Achieve 0x46, RequestUserEnv 0x69).</summary>
    public void SendAchieve(Ms2.Net.Wire.IOutPacket packet) => Send(packet);
    /// <summary>GetTally()->Sum(); 0 before an Achieve packet (the data is not read for it).</summary>
    public uint AchieveSum => achieves?.Sum ?? 0;
    /// <summary>Raised when OnPacket refreshes the character window (UIMyInfoDialog::FUN_141a5a560).</summary>
    public event Action AchievesChanged;

    /// <summary>CUserMail (g_143ab2ee0): the player's mails from the Mail packets (0x54, CSceneStateGame::OnMail); live
    /// only. Until the HUD sets its texts the book resolves with an empty set (no system mail template).</summary>
    public Ms2.Ui.MailBook Mails => mails ??= new Ms2.Ui.MailBook(Ms2.Ui.MailTexts.Empty()) { CharacterId = () => CharacterId };
    Ms2.Ui.MailBook mails;
    /// <summary>CBlackMarketMan (blackmarketman.cpp): the own listings and the search results from the BlackMarket packets
    /// (0x98, OnBlackMarket @0x140a20650); live only. Its sends (BlackMarket 0x5F) go out while the game connection is up.
    /// The HUD sets the texts, the item data and the dialog.</summary>
    public Ms2.Ui.BlackMarketBook BlackMarket => blackMarket ??= new Ms2.Ui.BlackMarketBook { Send = SendBlackMarket };
    Ms2.Ui.BlackMarketBook blackMarket;
    /// <summary>The black market sends (BlackMarket 0x5F).</summary>
    public void SendBlackMarket(Ms2.Net.Wire.IOutPacket packet)
    {
        if (game != null) Send(packet);
    }
    /// <summary>IsDialogClosed(0x4e49) == 0: the mail list is open (set by the HUD).</summary>
    public Func<bool> MailListOpen = () => false;
    /// <summary>The mail dialogs' sends (Mail 0x2D).</summary>
    public void SendMail(Ms2.Net.Wire.IOutPacket packet) => Send(packet);
    /// <summary>CBuddyManager: the friends, requests and blocks from the Buddy packets (0x61, CSceneStateGame::OnBuddy); live
    /// only. Its sends (Buddy 0x30) go out while the game connection is up.</summary>
    public Ms2.Ui.BuddyBook Buddies => buddies ??= new Ms2.Ui.BuddyBook { Send = SendBuddy };
    Ms2.Ui.BuddyBook buddies;
    /// <summary>CGuildManager: the guild id, this character's join requests and the search results from the Guild packets
    /// (0x8A); live only. Its sends (Guild 0x4B) go out while the game connection is up.</summary>
    public Ms2.Ui.GuildBook Guilds => guilds ??= new Ms2.Ui.GuildBook { Send = SendGuild };
    Ms2.Ui.GuildBook guilds;
    /// <summary>The guild dialog's sends (Guild 0x4B).</summary>
    public void SendGuild(Ms2.Net.Wire.IOutPacket packet)
    {
        if (game != null) Send(packet);
    }
    /// <summary>The open friend dialogs' vfunc_79 on a Buddy packet, after the book took it (set by the HUD).</summary>
    public event Action<BuddyMessage> BuddyPacket;
    /// <summary>The friend dialogs' sends (Buddy 0x30, EventReward 0x97).</summary>
    public void SendBuddy(Ms2.Net.Wire.IOutPacket packet)
    {
        if (game != null) Send(packet);
    }
    /// <summary>CTradeManagerEx (trademanagerex_1.cpp): the trade with another player from the Trade packets (0x82,
    /// OnTrade @0x14185bd20); live only. Its sends (Trade 0x49) go out while the game connection is up. The partner is
    /// looked up in the field's users (WorldService::FUN_141bd1530): the name and the picture key.</summary>
    public Ms2.Ui.TradeBook Trades => trades ??= new Ms2.Ui.TradeBook { Send = SendTrade, FindPlayer = TradePartner };
    Ms2.Ui.TradeBook trades;
    /// <summary>The trade sends (Trade 0x49).</summary>
    public void SendTrade(Ms2.Net.Wire.IOutPacket packet)
    {
        if (game != null) Send(packet);
    }
    Ms2.Ui.TradePartner TradePartner(long characterId) =>
        FieldUsers().FirstOrDefault(u => u.CharacterId == characterId) is { } u ? new Ms2.Ui.TradePartner(u.Name ?? "", u.Picture ?? "") : null;
    /// <summary>The field's users for the friend dialog's nearby tab: the own one and the others the tracker holds
    /// (FieldAddUser); empty before the field is built.</summary>
    public IEnumerable<UserBlock> FieldUsers()
    {
        if (Me.User != null) yield return Me.User;
        if (remote == null) yield break;
        foreach (var a in remote.Tracker.Snapshot())
            if (a.Add?.User is { } u && u.CharacterId != CharacterId) yield return u;
    }

    /// <summary>The trophy data with its load order (GetListFromTable walks it).</summary>
    static Ms2.Ui.AchieveBook AchieveBook()
    {
        var t0 = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var order = new List<Ms2.Ui.AchieveData>();
            var t = Ms2.Ui.AchieveData.LoadAll(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir), order);
            GD.Print($"live: trophy data {t.Count} trophies in {t0.ElapsedMilliseconds} ms");
            return new Ms2.Ui.AchieveBook(t, order);
        }
        catch (Exception e) { GD.Print($"live: trophy data unavailable ({e.Message})"); return new Ms2.Ui.AchieveBook(new Dictionary<int, Ms2.Ui.AchieveData>()); }
    }

    /// <summary>The CharacterInfo answers (0x78, UIPCInfoDialog::vfunc_79) for the other player's character window; live only.</summary>
    public event Action<CharacterInfoAnswer> CharacterInfoReceived;
    /// <summary>The NpcTalk packets (0x4B, UITalkManager::OnTalkPacket) for the talk window; live only.</summary>
    public event Action<NpcTalkMessage> NpcTalkReceived;
    /// <summary>The talk window's NpcTalk (0x22) and Quest (0x29) sends.</summary>
    public void SendTalk(Ms2.Net.Wire.IOutPacket packet) => Send(packet);
    /// <summary>The party window's and the popup menu's sends (Party 0x2C, RoomDungeon, DungeonReward, Club).</summary>
    public void SendParty(Ms2.Net.Wire.IOutPacket packet) => Send(packet);

    /// <summary>CGroupChatManager's sends (GroupChat 0x4C).</summary>
    public void SendGroupChat(Ms2.Net.Wire.IOutPacket packet) => Send(packet);
    int groupChatPackets;   // the scripted run's count of GroupChat packets read

    /// <summary>CPartyManager::SendPartyMode2 (partymanager.cpp:743): the answer to the last invite, with its name and kind.</summary>
    public void AnswerPartyInvite(int answer)
    {
        GD.Print($"live: party invite answer {answer}");
        Send(new PartyInviteAnswer(Party.PartyName, answer, Party.PartyKind));
    }
    /// <summary>The chat window's UserChat (0x11) sends (FUN_141276f80).</summary>
    public void SendChat(Ms2.Net.Wire.IOutPacket packet) => Send(packet);
    /// <summary>The field's live npcs (by FieldAddNpc), for the talk; empty before the field is built.</summary>
    public IEnumerable<FieldNpcs.Placed> TalkNpcs => remote?.TalkNpcs ?? Enumerable.Empty<FieldNpcs.Placed>();
    /// <summary>The field's other players (the remote layer's), none before the field is built.</summary>
    public IEnumerable<RemoteLayer.Pc> FieldPcs => remote?.Pcs ?? Enumerable.Empty<RemoteLayer.Pc>();
    /// <summary>The own FieldAddUser object id, -1 before it.</summary>
    public int SelfObjectId => selfObject;
    /// <summary>
    /// WorldService::FUN_141bd1530 (worldservice_1.cpp:6210): the field's user object with this character id, here whether
    /// the field's users (FieldAddUser, kept by the tracker) hold one. False before the field is built.
    /// </summary>
    public bool UserInField(long characterId) => remote?.Tracker.Snapshot().Any(a => a.Add?.User?.CharacterId == characterId) ?? false;

    /// <summary>
    /// CreateQuestByType (questmanager_2.cpp:803-830) makes a quest for the id: the client's quest data has it (FUN_14074bc90 gives
    /// type 11 without) and its type is one of 0-10. The login quest list (OnQuest mode 0x16) reads a record's progress only then.
    /// </summary>
    static bool HasQuestData(int id)
    {
        try { return NpcTalk.ClientQuests(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir)).Quest(id) is { Type: >= 0 and <= 10 }; }
        catch (Exception e) { GD.Print($"live: quest data unavailable ({e.Message})"); return true; }
    }

    QuestMessage ReadQuest(Ms2.Net.Wire.RawPacket p)
    {
        var q = QuestMessage.Read(p.Reader(), HasQuestData);   // CQuestManager::OnQuest (questmanager_2.cpp:942); no ExpectEnd: the client ignores trailing bytes
        if (q is QuestMessage.Load { Skipped.Count: > 0 } l)
            GD.PushWarning($"live: login quest list: no quest data for {l.Skipped[0]}, so the client misreads the rest of this list " +
                           $"({l.Quests.Count} quests kept, {l.Skipped.Count} records skipped{(l.Overrun ? ", read past the end" : "")})");
        return q;
    }

    public static void Attach(Node parent)
    {
        if (!Enabled || Instance != null) return;
        parent.AddChild(Instance = new LiveSession { Name = "LiveSession", ProcessMode = ProcessModeEnum.Always });
        Field.Built += Instance.OnFieldBuilt;
        ProfileImageService.Attach(Instance);
        ProfileImageService.Images.Arrived += Instance.PictureArrived;
    }

    public override void _ExitTree()
    {
        Field.Built -= OnFieldBuilt;
        ProfileImageService.Images.Arrived -= PictureArrived;
        exitCts.Cancel();
        StopGuide();
        Ms2.Ui.FieldMoveCover.Lower();        // no field move outlives the session
        game?.Dispose(); login?.Dispose();
        if (Instance == this) Instance = null;
    }

    /// <summary>Login and the character list; the list replaces the stub list. False when the server refuses.</summary>
    /// <summary>The server's LoginResult when the last login was refused (null when it failed otherwise or succeeded).</summary>
    public LoginResult LastLoginRefusal { get; private set; }

    public async Task<bool> LoginAsync()
    {
        Busy = true;
        LastLoginRefusal = null;
        try
        {
            var hp = System.Environment.GetEnvironmentVariable("MS2_LIVE_LOGIN").Split(':');
            var up = System.Environment.GetEnvironmentVariable("MS2_LIVE_ACCOUNT").Split(':', 2);
            host = hp[0];
            login?.Dispose();
            login = new TcpTransport();
            // the real client appends its region code to the id
            var o = await new LoginClient(login).LoginAsync(host, int.Parse(hp[1]), up[0] + "KR", up.Length > 1 ? up[1] : "", machine);
            if (!o.Success) { GD.PushWarning($"live: login refused, state {o.Result.State} {o.Result.Message}"); LastLoginRefusal = o.Result; return false; }
            accountId = o.Result.AccountId;
            if (o.Endpoint is { } ugc)
            {
                // FUN_1410268a0 / CSoapClient::FUN_14176fcf0: the download host and the upload base
                ProfileImageService.Images.Host = ugc.Host;
                ProfileImageService.Images.UploadBase = ugc.UploadBase;
                GD.Print($"live: web host {ugc.Host}, uploads {(ugc.UploadBase.Length > 0 ? "on" : "off")}");
            }
            Session.AccountId = up[0];
            Session.Characters = o.Characters.ToList();
            if (o.CharacterLimit > 0) CharacterLimit = o.CharacterLimit;
            GD.Print($"live: logged in, {o.Characters.Count} character(s): {string.Join(", ", o.Characters.Select(c => c.User.Name))}");
            return true;
        }
        catch (Exception ex) { GD.PushWarning("live: login failed: " + ex.Message); return false; }
        finally { Busy = false; }
    }

    /// <summary>The create screen's request goes to the login server; the new character joins the list. Null (logged) when the server refuses.</summary>
    public async Task<CharacterEntry> CreateAsync(CreateCharacter request)
    {
        Busy = true;
        LastRefusal = null;
        try
        {
            var e = await new LoginClient(login).CreateAsync(request);
            Session.Characters.Add(e);
            GD.Print($"live: character created: {e.User.Name} id {e.User.CharacterId}");
            return e;
        }
        catch (CharacterCreateRefused ex) { GD.PushWarning("live: " + ex.Message); LastRefusal = ex; return null; }
        catch (Exception ex) { GD.PushWarning("live: create failed: " + ex.Message); return null; }
        finally { Busy = false; }
    }

    /// <summary>Delete request for a character (OnDeletePC): BeginDelete (CharacterManagement 2). A low-level character goes at once (CharacterList DeleteEntry, error 0: the entry leaves the list);
    /// otherwise the server answers BeginDelete with the deadline. Returns the reply, null when the request failed. A refusal (error != 0) is logged and the list stays.</summary>
    public async Task<CharacterListMessage> DeleteAsync(CharacterEntry entry)
    {
        Busy = true;
        try
        {
            long id = entry.User.CharacterId;
            var m = await new LoginClient(login).ManageAsync(CharacterIdCommand.BeginDelete, id);
            if (m.Command == CharacterListCommand.DeleteEntry && m.Error == 0)
            {
                Session.Characters.RemoveAll(c => c.User.CharacterId == id);
                if (Session.SelectedIndex >= Session.Characters.Count) Session.SelectedIndex = Math.Max(0, Session.Characters.Count - 1);
                GD.Print($"live: character deleted: {entry.User.Name} id {id}");
            }
            else if (m.Command == CharacterListCommand.BeginDelete && m.Error == 0) GD.Print($"live: delete pending for {entry.User.Name}, until unix {m.DeleteTime}");
            else GD.PushWarning($"live: delete {m.Command} error {m.Error} (time {m.DeleteTime})");
            return m;
        }
        catch (Exception ex) { GD.PushWarning("live: delete failed: " + ex.Message); return null; }
        finally { Busy = false; }
    }

    static string Describe(ItemMessage m) => m switch {
        ItemMessage.LoadTab t => $"LoadTab {t.Tab} x{t.Items.Count} [{string.Join(", ", t.Items.Select(i => $"{i.Id}@{i.InventorySlot}x{i.Amount}"))}]",
        ItemMessage.Load l => $"Load x{l.Items.Count} [{string.Join(", ", l.Items.Select(i => $"{i.Id}@{i.InventorySlot}x{i.Amount}"))}]",
        _ => m.GetType().Name };

    void ApplyItems(Ms2.Net.Wire.RawPacket p) {
        if (System.Environment.GetEnvironmentVariable("MS2_LIVE_DUMP") == "1") GD.Print("live: item raw " + Convert.ToHexString(p.Body));
        try { var r = p.Reader(); var m = ItemMessage.Read(r, Items.Get); Items.Apply(m); GD.Print("live: item " + Describe(m) + (r.Available != 0 ? $" !! {r.Available} bytes left" : "")); }
        catch (Exception e) { GD.PushWarning($"live: item packet: {e.Message}"); }
    }

    /// <summary>The first model slot of an item for the body layout; Unknown when the item data is missing.</summary>
    static Func<int, EquipSlot> ItemSlotTable() {
        ItemModelSlots slots = null;
        return id => {
            try { return (slots ??= new ItemModelSlots(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir))).SlotOf(id); }
            catch (Exception e) { GD.Print($"live: item model table unavailable ({e.Message})"); return EquipSlot.Unknown; }
        };
    }

    /// <summary>The per-tab base slot count (FUN_140a66510) from table/constants.xml; the default 48 when the data is missing.</summary>
    static Func<int, int> BaseSlotTable() {
        Func<int, int> t = null;
        return tab => {
            try { t ??= Ms2Godot.Net.BagSlotTable.Load(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir)); }
            catch (Exception e) { GD.Print($"live: bag slot table unavailable ({e.Message})"); t = _ => Ms2.Net.Packets.BagSlots.Default; }
            return t(tab);
        };
    }

    static Ms2.Ui.Items.ItemDataTable itemData;
    /// <summary>The worn layer of an item (ItemDataTable.WornLayer); null when the item data is missing.</summary>
    static int? WornLayer(int itemId) {
        try { return (itemData ??= new Ms2.Ui.Items.ItemDataTable(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir))).WornLayer(itemId); }
        catch (Exception e) { GD.Print($"live: item data unavailable ({e.Message})"); return null; }
    }

    static ItemKinds kinds;
    static ItemKinds Kinds() => kinds ??= new ItemKinds(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir));

    /// <summary>The quest table columns for QuestBook (questType, repeatable, groupID), read once from the quest XML; no lookup when the data is missing.</summary>
    static Func<int, QuestBook.QuestInfoRow?> QuestTable() {
        System.Collections.Generic.Dictionary<int, (int Type, int Repeatable, int GroupId)> rows = null;
        return id => {
            try { rows ??= Ms2.Quest.QuestKinds.Load(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir)); }
            catch (Exception e) { GD.Print($"live: quest table unavailable ({e.Message})"); rows = new(); }
            return rows.TryGetValue(id, out var r) ? new QuestBook.QuestInfoRow(r.Type, r.Repeatable, r.GroupId) : null;
        };
    }

    /// <summary>Select the character and migrate to the game server, up to RequestFieldEnter. The chosen character's map becomes the server's.</summary>
    public async Task<bool> EnterAsync(CharacterEntry chosen)
    {
        Busy = true;
        regionSkills.Clear();                     // a new field: no region skill object survives it
        try
        {
            var go = await GameClient.SelectAsync(login, chosen.User.CharacterId);
            (entry, game) = await GameClient.EnterAsync(go, accountId, machine, host, requestGlobalFactor: !Factors.Applied);
            Quests.QuestInfo = QuestTable();
            ItemReader.SlotOf ??= ItemSlotTable();
            ItemReader.ExtraOf ??= id => { try { return Kinds().ExtraOf(id); } catch (Exception e) { GD.Print($"live: item kinds unavailable ({e.Message})"); return null; } };
            ItemReader.UgcMode ??= () => ProfileImageService.Images.Mode;      // CUgcManager::GetMode, set by UgcSetMode
            ItemReader.Known ??= id => { try { return Kinds().Known(id); } catch { return true; } };   // data unavailable: read every id
            Items.TabOf = id => { try { return Kinds().TabOf(id); } catch { return 7; } };
            Items.BaseSlots ??= BaseSlotTable();
            if (Inventory == null) { Inventory = new InventoryView(Items); Inventory.RequestSort = (tab, stack) => Send(new ItemInventorySortRequest((byte) tab, stack)); Inventory.RequestMove = (uid, slot) => Send(new ItemInventoryMoveRequest(uid, slot)); Inventory.RequestEquip = (uid, slot, mode) => Send(new ItemEquipRequest(uid, slot, mode)); Inventory.RequestUnequip = uid => Send(new ItemUnequipRequest(uid)); Inventory.RequestSwapEquip = (uid, slot) => Send(new ItemSwapEquipRequest(uid, slot)); Inventory.RequestEquipGem = (uid, slot) => Send(new GemEquipRequest(uid, slot)); Inventory.RequestUse = (uid, arg) => Send(new ItemUseRequest(uid, arg)); Inventory.ServerTime = () => GameSession?.ServerTime ?? 0; }
            Items.MaxStackOf = id => { try { return Kinds().SlotMax(id); } catch { return uint.MaxValue; } };
            StartGuide();   // before the early packets: the guide record (0x89) is among them
            // what the server sent before the field enter, in arrival order, through the read loop's handler (no HUD exists yet):
            // the adventure level, quest, item, trophy, title and key table loads among them
            login.Dispose(); login = null;
            this.chosen = chosen;
            TakeEntry();
            return true;
        }
        catch (Exception ex) { GD.PushWarning("live: enter failed: " + ex.Message); return false; }
        finally { Busy = false; }
    }

    CharacterEntry chosen;

    /// <summary>A game server's entry (the login's, or the next server's after a GameToGame): what it sent before the field
    /// enter, in arrival order, through the read loop's handler, then its RequestFieldEnter becomes the current entry.</summary>
    void TakeEntry()
    {
        // the adventure level, quest, item, trophy, title and key table loads among them
        entry.Replay(Handle, () =>
        {
            Adventure.ServerEnter();
            StubHudData.Instance.Wallet = entry.Enter?.Wallet;
            // the found-taxi list DAT_143ab3078: ServerEnter's second int list (OnServerEnter, scenestategame.cpp:2115-2122);
            // without the enter packet it starts empty, so the stub's offline value never reaches a live session
            StubHudData.Instance.FoundTaxis = entry.Enter?.UnlockedTaxis is { } taxis ? new HashSet<int>(taxis) : new HashSet<int>();
            Me.Apply(entry.Enter);                 // the exp counters (OnServerEnter, scenestategame.cpp:2085-2086)
        });
        GD.Print($"live: {entry.Early.Count} packets before the field: {Quests.All.Count} quests, {Items.All.Count} items, adventure claimed {Adventure.Claimed.Count}, missions {Adventure.Missions.Count}");
        fieldEntry.Enter(entry.Field);
        NewFieldPortals(entry.Field.MapId);
        syncNumber = entry.Sync?.Number ?? 0;
        var f = entry.Field;
        if (chosen != null) chosen.User.MapA = f.MapId;
        guide?.QueueOccurrence(GuideOccurrence.FieldId, f.MapId);   // OnRequestFieldEnter after LoadMap (scenestategame.cpp:2353)
        GD.Print($"live: RequestFieldEnter map {f.MapId} pos {f.Position} rot {f.Rotation} portal {f.Portal}");
    }

    /// <summary>The server's arrival (position, rotation, portal) on the move built for the entry's map.</summary>
    public static Travel.Move Decorate(Travel.Move m)
    {
        var f = Instance?.fieldEntry.Current;
        if (m == null || f == null) return m;
        return m with { At = new Vec3(f.Position.X, f.Position.Y, f.Position.Z), RotZDeg = f.Rotation.Z, EntryPortal = f.Portal };
    }

    void OnFieldBuilt(Field field, ModelBuilder builder)
    {
        if (entry == null) return;
        regionSkills.Clear();
        var layer = remote = new RemoteLayer(field, builder, entry.Enter.CharacterId);
        remote.Tracker.Profile = game.Profile;
        // the field is freed: its tracker stops filling Events (a newer field's layer is left alone); the guide stops and
        // updates on the map's unload (worldservice_1.cpp:10021)
        field.TreeExiting += () => { if (remote == layer) remote = null; guide?.StopAndUpdate(); };
        // the field is whole: the entry's answer (the login's comes through the flow as well; only one goes out)
        field.Loaded += FieldLoaded;
        GD.Print("live: remote layer ready");
    }

    /// <summary>
    /// RequestFieldEnter in game (CSceneStateGame::OnRequestFieldEnter @0x14170f930, scenestategame.cpp:2335), the same
    /// packet the login's entry ends with. An error shows GetFieldMoveErrorMessage's box and changes nothing. Error 0:
    /// WorldService::LoadMap(map, 1, nPortal) — the old field's teardown (FUN_141bbc580, worldservice_1.cpp:9963: every
    /// object, PC, npc, effect and the own MyPC (SetMyPC(0)) go; the loading screen starts, CUIService FUN_141adb900) and
    /// the new field's load — which here is the scene change every field load takes (FieldContents.ServerMove, the
    /// portal move's path: the loading screen, then the field scene with the entry as its move). UITalkManager
    /// FUN_141b21390 ends the talk (m_nTalking, m_nSpecialNpc = 0). The own object comes again with the new field's
    /// FieldAddUser (OnAddUser builds a new MyPC); the sync number stays until the server sends one (OnSyncNumber,
    /// networkservice.cpp:799, is not reset by the move).
    /// LoadMap returns at once when the field is still loading the same map (worldservice_1.cpp:3295, m_state 2 and the
    /// same map id): a repeat entry for the map in load changes the entry's values only; the load goes on.
    /// STILL-OPEN: DAT_143ab2890 = 0 (the free-roam test field flag),
    /// FUN_140930ba0 (the dungeon room id and its group type), CUIService FUN_141adc400 (uiservice.cpp:1574: each
    /// registered dialog with m_158 set and m_15c clear is opened again, vfunc_61(0) then vfunc_65; this HUD is rebuilt with
    /// the field scene, so its dialogs come back with it, but no dialog registry with those flags exists), and, after either
    /// outcome, GFxSceneService FUN_1422ba0d0 and SetSubScene(0x6200086) when the instance type (DAT_143ab287c, the entry's
    /// second byte; the last good entry's after an error) is 0xd have no port here.
    /// </summary>
    void OnRequestFieldEnter(RequestFieldEnter f)
    {
        int loading = moveClock.IsRunning ? fieldEntry.Current?.MapId ?? 0 : 0;   // the map whose ResponseFieldEnter is still to go
        if (!fieldEntry.Enter(f))
        {
            string text = Ms2.Ui.FieldMoveError.Text(f.Error, Ms2.Ui.Hud.MessageBoxData.CommonByKey);
            GD.Print($"live: RequestFieldEnter error {f.Error}");
            Ms2.Ui.Hud.MessageBoxQueue.ShowMessageBox(text, 0, null, "");   // ShowMessageBox(GetFieldMoveErrorMessage(err), 0, 0, L"")
            return;
        }
        entry = entry with { Field = f };
        if (chosen != null) chosen.User.MapA = f.MapId;
        if (loading != 0 && loading == f.MapId)
        {
            GD.Print($"live: RequestFieldEnter in game: map {f.MapId} is loading already, the load goes on (pos {f.Position} portal {f.Portal})");
            return;
        }
        // the old field's own player stops sending: SetMyPC(0) until the new field's FieldAddUser
        if (player != null && IsInstanceValid(player)) player.Live = false;
        player = null;
        selfObject = -1;
        bindPending = false;
        moveClock.Restart();
        NewFieldPortals(f.MapId);
        guide?.QueueOccurrence(GuideOccurrence.FieldId, f.MapId);   // OnRequestFieldEnter after LoadMap (scenestategame.cpp:2353)
        GD.Print($"live: RequestFieldEnter in game: map {f.MapId} type {f.FieldType} instance {f.InstanceType}/{f.InstanceId} dungeon {f.DungeonId} pos {f.Position} portal {f.Portal}");
        FieldMoveStarted?.Invoke(f);
        Ms2Godot.World.FieldContents.ServerMove(GetTree(), f.MapId);
    }

    /// <summary>Raised when an in-game RequestFieldEnter starts a field move (the field scene is about to change).</summary>
    public event Action<RequestFieldEnter> FieldMoveStarted;
    /// <summary>Raised when the entry's answer went out: the field is loaded (the time since the move's RequestFieldEnter, ms; 0 at login).</summary>
    public event Action<int, long> FieldEntered;
    readonly System.Diagnostics.Stopwatch moveClock = new();
    /// <summary>The entry the client is in (or loading).</summary>
    public RequestFieldEnter CurrentEntry => fieldEntry.Current;

    /// <summary>The own Stat (0x2E; CSceneStateGame::OnStat, scenestategame.cpp:9424): health, spirit and stamina reach the HUD data.</summary>
    void ApplyStat(PlayerStat st)
    {
        var hud = StubHudData.Instance;
        if (st.TryGet(PlayerStat.Health, out long hm, out long hc)) { hud.Player.HpMax = (int)hm; hud.Player.HpCurrent = (int)hc; }
        if (st.TryGet(PlayerStat.Spirit, out long sm, out long sc)) { hud.SpMax = (int)sm; hud.SpCurrent = (int)sc; }
        if (st.TryGet(PlayerStat.Stamina, out long em, out long ec)) { hud.EpMax = (int)em; hud.EpCurrent = (int)ec; }
        GD.Print($"live: Stat hp {hud.Player.HpCurrent}/{hud.Player.HpMax} sp {hud.SpCurrent}/{hud.SpMax} ep {hud.EpCurrent}/{hud.EpMax} ({st.Values.Count} attribute(s))");
    }

    /// <summary>The field is built and shown: ResponseFieldEnter(field key) goes out. The client sends it when the map's entities are all created
    /// (CubeMapInformationService::FUN_1410cf5c0, cubemapinformationservice_3.cpp:5701-5705: the pending entity set is empty, then
    /// COutPacket ResponseFieldEnter with the world's field key).</summary>
    public void FieldLoaded()
    {
        if (entry == null) return;
        var answer = fieldEntry.Answer();
        if (answer == null) return;
        Send(answer);
        long ms = moveClock.IsRunning ? moveClock.ElapsedMilliseconds : 0;
        moveClock.Reset();
        GD.Print($"live: ResponseFieldEnter sent, portal {answer.Portal}, map {fieldEntry.Current.MapId}{(ms > 0 ? $", {ms} ms after the move's RequestFieldEnter" : "")}");
        FieldEntered?.Invoke(fieldEntry.Current.MapId, ms);
        // FUN_1413bd5c0 (NotifyCompleteMyPC): a member with the invite right asks for the guild's join requests, the
        // client by its own name (HasMemberPermission(GetMyName(), 2)). The notify comes when the local PC's load
        // completes (MyPC::vfunc_10, mypc.cpp:1004); this rides the field-loaded response, a moment sooner — the ask
        // and its answer are the same either way. STILL-OPEN: the notify's own point.
        string myName = Session.Characters?.FirstOrDefault(c => c.User.CharacterId == CharacterId)?.User.Name;
        if (Guilds.InGuild && myName is not null && Guilds.HasMemberPermission(myName, 2)) Guilds.ListRequests();
    }

    /// <summary>Movement out: the player's record packets and state changes become UserSync (0x12) and State (0x80); its offline
    /// server emulation turns off. The cadence and content are the player's own (PlayerFallReturn.StateSync), not a second copy.</summary>
    /// <summary>Fire-and-forget send whose failure is logged (an unmapped op under the profile, a closed socket) instead of going unobserved.</summary>
    void Send(Ms2.Net.Wire.IOutPacket pk)
    {
        var g = game;
        if (g == null) return;
        g.SendAsync(pk).ContinueWith(t => GD.PushWarning($"live: send {pk.GetType().Name} failed: {t.Exception?.GetBaseException().Message}"),
            System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>The own FieldAddUser makes the MyPC (OnAddUser, at the login's field and after each move); it sends movement
    /// from then on. The field's player node may come a frame later than the packet: the bind waits for it.</summary>
    bool bindPending;
    void TryBindPlayer()
    {
        if (!bindPending) return;
        var found = GetTree().Root.FindChild("Player", true, false) as Player;
        if (found == null || found.IsQueuedForDeletion()) return;
        bindPending = false;
        if (found == player && player.Live) return;   // a repeat of the own FieldAddUser on the same field
        BindPlayer(found);
    }
    void BindPlayer(Player found)
    {
        player = found;
        player.Live = true;
        player.ServerTick = () => game?.Session.ServerTick ?? 0;
        player.StateSyncOut += records =>
        {
            if (game == null) return;
            var pk = new UserSyncOut { SyncNumber = syncNumber, ServerTick = game.Session.ServerTick, ClientTick = game.Session.ElapsedMs };   // state.cpp:491-493: GetServerTick, then GetElapsedTime
            foreach (var r in records) pk.Records.Add(r);
            Send(pk);
            var l = records[^1].Record;
            GD.Print($"live: UserSync sent, {records.Count} record(s), last state {l.State} pos ({l.Position.X},{l.Position.Y},{l.Position.Z}) sync {l.SyncNumber}");
        };
        player.StateChangeOut += (mode, arg, pos) =>
        {
            if (game == null) return;
            Send(new StateChange(mode, arg, game.Session.ServerTick, new System.Numerics.Vector3(pos.X, pos.Y, pos.Z)));   // mypc.cpp:3661: GetServerTick
            GD.Print($"live: State sent, mode {mode} arg {arg}");
        };
    }

    /// <summary>MS2_LIVE_PACKETS=1: print each packet this session does not read itself, a debug switch; off by default (most
    /// of them go to the field tracker; in a town, NpcControl alone arrives about a thousand times a second).</summary>
    static readonly bool LogPackets = System.Environment.GetEnvironmentVariable("MS2_LIVE_PACKETS") == "1";

    public override void _Process(double delta)
    {
        if (game == null) { PumpLogin(); return; }
        while (game != null && game.Incoming.TryDequeue(out var p)) Handle(p);
        if (game == null) return;   // a GameToGame let the connection go: the next server's comes with the migration
        guide?.Tick();
        if (bindPending) TryBindPlayer();
        Portals?.Tick((uint)game.Session.ServerTick);
        if (game.IsClosed) { GD.PushWarning("live: game connection closed" + (game.Failure != null ? ": " + game.Failure.Message : "")); game.Dispose(); game = null; if (player != null) player.Live = false; }   // offline fall-return emulation comes back
    }

    /// <summary>One game server packet: the read loop's, and at the entry the ones that came before the field enter
    /// (<see cref="GameEntry.Replay"/>).</summary>
    void Handle(Ms2.Net.Wire.RawPacket p)
    {
        try
        {
            if (game.Is(p, Inbound.RequestFieldEnter)) regionSkills.Clear();   // a new field: no region skill object survives it
            bool own = game.Is(p, Inbound.Stat) && p.Body.Length >= 4 && BitConverter.ToInt32(p.Body, 0) == selfObject;
            if (own) { var st = PlayerStat.Read(p.Reader()); ApplyStat(st); Me.Apply(st); }
            else remote?.Tracker.Inbound(p);     // others' users and npcs (FieldTracker keeps the rest)
            if (own) { }
            else if (game.Is(p, Inbound.FieldAddUser))
            {
                var u = FieldAddUser.Read(p.Reader());
                bool self = u.User.CharacterId == entry.Enter.CharacterId;
                if (self) { selfObject = u.ObjectId; Me.Apply(u); bindPending = true; TryBindPlayer(); }   // OnAddUser builds the MyPC
                if (self)
                {
                    OwnBuff(null);
                    foreach (var b in u.Buffs) OwnBuff(BuffPacket.FromAdd(b));
                }
                if (self && u.Book != null) { SkillBook = u.Book; SkillBookChanged?.Invoke(-1); }
                GD.Print($"live: FieldAddUser object {u.ObjectId} {(self ? "SELF" : "other")} char {u.User.CharacterId} '{u.User.Name}' pos {u.Position}");
            }
            else if (game.Is(p, Inbound.Buff))
            {
                var b = BuffPacket.Read(p.Reader());
                if (b.OwnerObjectId == selfObject) OwnBuff(b);
            }
            else if (game.Is(p, Inbound.ExpUp))
            {
                var e = ExpUp.Read(p.Reader());   // CSceneStateGame::OnExpUp (scenestategame.cpp:4129); no ExpectEnd: the client ignores trailing bytes
                Me.Apply(e);
                GD.Print($"live: exp up mode {e.Mode} +{e.Gained} exp {Me.Exp} rest {Me.RestExp}");
            }
            else if (game.Is(p, Inbound.Meso))
            {
                var m = MesoUpdate.Read(p.Reader());
                long delta = Me.Apply(m);
                GD.Print($"live: meso {m.Balance} ({delta:+#;-#;0}, kind {m.Kind})");
                MesoChanged?.Invoke(delta, m.Kind);
            }
            else if (game.Is(p, Inbound.LevelUp))
            {
                var l = LevelUp.Read(p.Reader());  // CSceneStateGame::OnLevelUp (scenestategame.cpp:9511)
                bool selfLevel = Me.Apply(l);
                if (selfLevel) guide?.OnChangeMyLevel();   // NotifyChangeMyLevel (CGuideManager::OnChangeMyLevel)
                GD.Print($"live: level up object {l.ObjectId} level {l.Level}{(selfLevel ? " SELF" : "")}");
            }
            else if (game.Is(p, Inbound.SyncNumber))
            {
                syncNumber = SyncNumber.Read(p.Reader()).Number;
                player?.OnSyncNumber();
                GD.Print($"live: SyncNumber {syncNumber}");
            }
            else if (game.Is(p, Inbound.RequestFieldEnter))
                OnRequestFieldEnter(RequestFieldEnter.Read(p.Reader()));
            else if (game.Is(p, Inbound.UserMoveByPortal))
            {
                var m = UserMoveByPortal.Read(p.Reader());
                GD.Print($"live: MoveByPortal object {m.ObjectId} pos {m.Position} rot {m.Rotation} portal {m.IsPortal}");
                if (m.ObjectId == selfObject) player?.ServerMoveByPortal(new Vector3(m.Position.X, m.Position.Y, m.Position.Z), m.Rotation.Z, m.IsPortal);
            }
            else if (game.Is(p, Inbound.NpcTalk))
            {
                var t = NpcTalkMessage.Read(p.Reader());   // UITalkManager::OnTalkPacket (uitalkcinematic.cpp:971)
                GD.Print($"live: npc talk {t}");
                NpcTalkReceived?.Invoke(t);
            }
            else if (game.Is(p, Inbound.Quest))
            {
                var q = ReadQuest(p);
                bool known = GuideQuests.KnownBefore(Quests, q);   // RemoveQuest's early return on an unknown quest
                Quests.Apply(q);
                GuideOnQuest(q, known);
                GD.Print($"live: quest {q.GetType().Name} ({Quests.All.Count} quests)");
            }
            else if (game.Is(p, Inbound.Party))
            {
                var m = PartyMessage.Read(p.Reader());   // CPartyManager::OnParty (partymanager.cpp:447); no ExpectEnd: the client ignores trailing bytes
                Party.Apply(m, CharacterId, PartyWindowOpen());
                if (m is PartyInvite invite)        // OnPartyInvite (partymanager.cpp:968); no PvP state yet, so no PvP point
                    Ms2.Ui.Hud.PartyInviteBox.OnInvite(invite, GameOptions.AccountCommunity.DisablePartyRequest, false, AnswerPartyInvite);
                GD.Print($"live: party {m.GetType().Name} ({Party.Members.Count} members)");
            }
            else if (game.Is(p, Inbound.GroupChat))
            {
                var m = GroupChatMessage.Read(p.Reader());   // CGroupChatManager::OnGroupChat (@0x14137f2f0, groupchatmanager.cpp:577)
                GroupChats.Apply(m);
            groupChatPackets++;
                GD.Print($"live: group chat {m.GetType().Name} ({GroupChats.ChatCount} chats)");
            }
            else if (game.Is(p, Inbound.ItemInventory))
            {
                ApplyItems(p);
                GD.Print($"live: item packet ({Items.All.Count} items)");
            }
            else if (game.Is(p, Inbound.ItemPutOn))
            {
                var o = ItemPutOn.Read(p.Reader());        // CSceneStateGame::OnItemPutOn (scenestategame.cpp:3080); display only, no c2s answer
                bool ownEquipment = Me.Apply(o, WornLayer);
                GD.Print($"live: item put on {o.Item.Id} uid {o.Item.Uid} slot {o.Item.Slot} kind {o.Kind} by {o.ObjectId}{(ownEquipment ? " (own equipment)" : "")}");
            }
            else if (game.Is(p, Inbound.ItemPutOff))
            {
                var o = ItemPutOff.Read(p.Reader());       // CSceneStateGame::OnItemPutOff (scenestategame.cpp:3144)
                bool ownEquipment = Me.Apply(o, WornLayer);
                GD.Print($"live: item put off uid {o.ItemUid} by {o.ObjectId}{(ownEquipment ? " (own equipment)" : "")}");
            }
            else if (game.Is(p, Inbound.Notice))
            {
                var n = NoticeMessage.Read(p.Reader());     // CSceneStateGame::OnNotice (scenestategame.cpp:9216); no ExpectEnd: the client ignores trailing bytes
                Notice?.Invoke(n);
                GD.Print($"live: notice kind {n.Kind} flags 0x{n.Flags:x} text kind {n.Text.Kind}{(n.Text.Formatted ? $" id {n.Text.Id} args {n.Text.Args.Length}" : $" \"{n.Text.Text}\"")}");
            }
            else if (game.Is(p, Inbound.UserChat))
            {
                var r = p.Reader();
                var c = ChatMessage.Read(r);                // UIMessengerBrowser::vfunc_84 (uimessenger.cpp:2829); no ExpectEnd: the client ignores trailing bytes
                UserChat?.Invoke(c, () => WeddingChatHall.Read(r));
                GD.Print($"live: chat type 0x{c.Type:x} from {c.Name}{(c.Common ? $" common {c.CommonId}" : "")}");
            }
            else if (game.Is(p, Inbound.UserChatItemLink))
            {
                var links = ChatItemLinks.Read(p.Reader());  // UIMessengerBrowser::vfunc_85 (uimessenger.cpp:2963); no ExpectEnd: the client ignores trailing bytes
                UserChatItemLink?.Invoke(links);
                GD.Print($"live: chat item links {links.Items.Count}");
            }
            else if (game.Is(p, Inbound.BadgeEquip))
            {
                var b = BadgeEquipMessage.Read(p.Reader());     // CSceneStateGame::OnBadgeEquip (scenestategame.cpp:11006); no ExpectEnd: the client ignores trailing bytes
                bool ownGems = Me.Apply(b);
                if (b.Mode == 4) GemEquipError?.Invoke(b.Error);
                else if (b.Mode is 0 or 1) BadgeEquipped?.Invoke(b);
                GD.Print($"live: badge equip mode {b.Mode} by {b.ObjectId}{(b.Mode == 4 ? $" error {b.Error}" : "")}{(ownGems ? " (own gems)" : "")}");
            }
            else if (game.Is(p, Inbound.Taxi))
            {
                var t = Ms2.Net.Packets.TaxiFound.Read(p.Reader());   // CSceneStateGame::OnTaxi (scenestategame.cpp:7902): the ids join DAT_143ab3078
                if (t.Fields.Length > 0)                            // the client returns on an empty list
                {
                    StubHudData.Instance.FoundTaxis.UnionWith(t.Fields);
                    TaxiFound?.Invoke(t);
                }
                GD.Print($"live: taxi found {string.Join(",", t.Fields)} open {t.Open}");
            }
            else if (game.Is(p, Inbound.UserState))
            {
                var u = UserState.Read(p.Reader());     // OnUserState (scenestategame.cpp:5758): the state, then state 12 runs PC::vfunc_137
                if (Me.ApplyUserState(u.ObjectId, u.State))
                {
                    GD.Print($"live: own player dead (state {u.State})");
                    guide?.QueueOccurrence(GuideOccurrence.Dead, 0);   // MyPC::vfunc_137 when dead (mypc.cpp:1350)
                }
            }
            else if (game.Is(p, Inbound.DeadUser))
            {
                var d = DeadUser.Read(p.Reader());      // OnDeadUser (scenestategame.cpp:6532)
                if (Me.Die(d.ObjectId)) GD.Print("live: own player dead");
            }
            else if (game.Is(p, Inbound.Revival))
            {
                var v = Revival.Read(p.Reader());       // OnRevival (scenestategame.cpp:7387)
                if (Me.Revive(v.ObjectId))
                {
                    GD.Print("live: own player revived");
                    guide?.QueueOccurrence(GuideOccurrence.Dead, 0);   // OnRevival of the own player (scenestategame.cpp:7405)
                }
            }
            else if (game.Is(p, Inbound.ItemPutOnOrOff))
            {
                var o = ItemPutOnOrOff.Read(p.Reader());   // CSceneStateGame::OnItemPutOnOrOff (scenestategame.cpp:2575); read only, no c2s answer
                GD.Print($"live: item put on/off {o}");
            }
            else if (game.Is(p, Inbound.KeyTable))
            {
                int mode = KeyTable.Apply(p.Reader());   // CKeyTable::OnKeyTable (keytable.cpp:285); no ExpectEnd: the client ignores trailing bytes
                GD.Print($"live: key table mode {mode} ({KeyTable.PageCount} pages, page {KeyTable.CurrentPage})");
                KeyTableChanged?.Invoke(mode);
            }
            else if (game.Is(p, Inbound.GuideRecord))
            {
                var r = GuideRecordIn.Read(p.Reader());   // CSceneStateGame::OnGuideRecord (scenestategame.cpp:3878): CGuideManager::ReceiveStates
                guide?.ReceiveStates(r);
                GD.Print($"live: guide record {r.Steps.Count} step(s) bits 0x{r.Bits:x}");
            }
            else if (game.Is(p, Inbound.Job))
            {
                var j = JobPacket.Read(p.Reader());   // CSceneStateGame::OnJob (scenestategame.cpp:7449); no ExpectEnd: the client ignores trailing bytes
                bool selfJob = j.ObjectId == selfObject;   // FindPCById: a PC not in the field yet drops the packet, as here
                if (selfJob && j.Book != null) { SkillBook = j.Book; SkillBookChanged?.Invoke(j.Mode); }
                if (selfJob && j.Mode is 0 or 2 or 7) guide?.OnJobChanged(Me.User?.Job ?? 0);   // NotifyStatusChanged(4) with the sub job (scenestategame.cpp:7463/7485/7506)
                GD.Print($"live: job mode {j.Mode} object {j.ObjectId}{(selfJob ? " SELF" : "")}" + (j.Book != null ? $" ({j.Book.Skills.Count} skills)" : j.Mode == 3 ? $" error {j.Error}" : ""));
            }
            else if (game.Is(p, Inbound.AttributePoint))
            {
                var m = AttributePointMessage.Read(p.Reader());   // OnAttributePoint (scenestategame.cpp:4270): the local player's CStatPointManager
                if (m != null) Me.StatPoints.Apply(m);
                GD.Print($"live: attribute points {m?.GetType().Name ?? "other mode"} (total {Me.StatPoints.Total}, used {Me.StatPoints.Used})");
            }
            else if (game.Is(p, Inbound.Achieve))
            {
                var m = AchieveMessage.Read(p.Reader());          // CAchieveManager::OnPacket (achievemanager.cpp:499, via networkservice.cpp:679)
                bool refresh = m != null && Achieves.Apply(m);
                if (refresh) AchievesChanged?.Invoke();
                GD.Print($"live: achieve {m?.GetType().Name ?? "other mode"} (tally {Achieves.Sum})");
            }
            else if (game.Is(p, Inbound.Mail))
            {
                var m = MailMessage.Read(p.Reader());             // CSceneStateGame::OnMail (scenestategame.cpp:11496)
                if (m != null) Mails.Apply(m, MailListOpen(), GameSession?.ServerTime ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                GD.Print($"live: mail {m?.GetType().Name ?? "other mode"} ({Mails.Mails.Count} mails, {Mails.Unread} unread)");
            }
            else if (game.Is(p, Inbound.Trade))
            {
                var m = TradeMessage.Read(p.Reader());            // CTradeManagerEx::OnTrade (trademanagerex_1.cpp, @0x14185bd20)
                if (m != null) Trades.OnTrade(m);
                GD.Print($"live: trade code {m?.Code.ToString() ?? "?"}");
            }
            else if (game.Is(p, Inbound.BlackMarket))
            {
                var m = BlackMarketMessage.Read(p.Reader());      // CBlackMarketMan::OnBlackMarket (networkservice.cpp:690, @0x140a20650)
                BlackMarket.OnBlackMarket(m);
                GD.Print($"live: black market mode {m.Mode} ({BlackMarket.OwnProducts.Count} own, {BlackMarket.SearchProducts.Count} found)");
            }
            else if (game.Is(p, Inbound.Buddy))
            {
                var m = BuddyMessage.Read(p.Reader());            // CSceneStateGame::OnBuddy -> CBuddyManager::OnBuddy (buddymanager.cpp:95)
                if (m != null) { Buddies.Apply(m); BuddyPacket?.Invoke(m); }
                GD.Print($"live: buddy protocol {m?.Protocol.ToString() ?? "?"} ({Buddies.Count} entries, {Buddies.ReceivedCount} received)");
            }
            else if (game.Is(p, Inbound.Guild))
            {
                var m = GuildMessage.Read(p.Reader());            // CGuildManager::FUN_1413b6370 (guildmanager.cpp:301)
                Guilds.Apply(m);
                GD.Print($"live: guild type 0x{m.Type:x2} (guild {Guilds.GuildId}, {Guilds.MyRequests.Count} mine, {Guilds.RequestsForGuild.Count} for the guild, {Guilds.SearchResults.Count} results)");
            }
            else if (game.Is(p, Inbound.Prestige))
            {
                var m = PrestigeMessage.Read(p.Reader());         // OnPrestige (scenestategame.cpp:8815); other kinds are dropped
                // kind 2: FindPCById; the own PC's old level fills level 1 (:8856), then vfunc_140 (:8836). Other PCs'
                // user blocks take it in the field tracker
                bool ownLevel = m is PrestigeMessage.LevelUp up && up.ObjectId == selfObject && Me.User != null;
                if (m != null) Adventure.Apply(m, ownLevel ? Me.User.PrestigeLevel : null, MissionFeature());
                if (ownLevel) Me.User.PrestigeLevel = ((PrestigeMessage.LevelUp)m).Value;
                if (m != null) PrestigeReceived?.Invoke(m);
                GD.Print($"live: prestige {m?.GetType().Name ?? "other kind"}");
            }
            else if (game.Is(p, Inbound.CharacterAbility))
            {
                var m = CharacterAbilityMessage.Read(p.Reader());  // CCharacterAbilityManager::OnCharacterAbility via DispatchPacket
                var table = CharacterAbilityTable;
                if (m != null && CharacterAbility.Apply(m, id => table == null || table.GetAbility(id) != null))
                    CharacterAbilityReceived?.Invoke(m);
                GD.Print($"live: character ability {m?.GetType().Name ?? "other mode"} ({CharacterAbility.Ids.Count} learned)");
            }
            else if (game.Is(p, Inbound.RegionSkill))
            {
                // OnRegionSkill (scenestategame.cpp:7721): mode 0 adds the region skill object of the id, mode 1 removes it.
                // Only the ids are kept (the object's Init is not ported); mode 5's damage needs one.
                var r = p.Reader();
                int mode = r.ReadByte(), id = r.ReadInt();
                if (mode == 0) regionSkills.Add(id); else if (mode == 1) regionSkills.Remove(id);
            }
            else if (game.Is(p, Inbound.SkillDamage))
            {
                // The records that hit the own player; MasteryEvents applies the client's mode drops (3 needs its PC, 5 its
                // region skill object). One event per target record (MyPC::vfunc_113 posts each).
                var d = SkillDamage.Read(p.Reader());
                // mode 3's caster test is PCBase (players, npcs and pets; the tracker keeps the first two, and a pet
                // is a PCBase in the client, so a pet's DoT on the own player cancels there but not here yet)
                foreach (var total in Ms2.Ui.Hud.MasteryEvents.SelfRecords(d, selfObject,
                             id => remote?.Tracker.HasObject(id) ?? (id == selfObject),
                             id => remote?.Tracker.HasObject(id) ?? (id == selfObject), regionSkills.Contains))
                    SelfDamaged?.Invoke(total);
            }
            else if (game.Is(p, Inbound.Mastery))
            {
                var m = MasteryMessage.Read(p.Reader());          // CSceneStateGame::OnMastery (scenestategame.cpp:3920)
                if (m != null && Me.User != null)                 // only with a local player
                {
                    if (!Mastery.Loaded) Mastery.Load(Me.User.Mastery);
                    uint gain = 0; int oldGrade = 0; bool up = false;
                    if (m is MasteryMessage.ExpChanged e)
                    {
                        var table = MasteryTable ?? Ms2.Ui.Items.MasteryRecipeTable.Empty;
                        (gain, oldGrade, up) = Mastery.SetExp(e.Type, e.Exp, table.Grade);
                        if (Me.User.Mastery is { } mx && (uint)e.Type < mx.Length) mx[e.Type] = e.Exp;
                    }
                    else if (m is MasteryMessage.RewardReceived r) Mastery.MarkReceived(r.RewardId);
                    MasteryReceived?.Invoke(m, gain, oldGrade, up);
                }
                GD.Print($"live: mastery {m?.GetType().Name ?? "other mode"}");
            }
            else if (game.Is(p, Inbound.Fishing))
            {
                var m = FishingBook.Receive(p.Reader(), FishingFeature(), out var changed);   // feature 0xb, then modes 7 (the list) and 8 (a catch); the others are not the book's
                if (changed)
                    FishingReceived?.Invoke(m);
                GD.Print($"live: fishing {m?.GetType().Name ?? "other mode"} ({FishingBook.Count} fish)");
            }
            else if (game.Is(p, Inbound.UserEnv))
            {
                var m = UserEnvMessage.Read(p.Reader());          // OnUserEnv (scenestategame.cpp:4414); the other modes are not kept
                if (m is UserEnvMessage.Collection c) { CollectionGrades = c.Grades; CollectionChanged?.Invoke(); }
                else if (m != null) { Me.Apply(m); if (Mastery.Apply(m)) MasteryEnvChanged?.Invoke(); }
                GD.Print($"live: user env {m?.GetType().Name ?? "other mode"} ({Me.Titles.Count} titles)");
            }
            else if (game.Is(p, Inbound.HomeCommand))
            {
                var m = HomeCommend.Read(p.Reader());             // OnHomeCommand (scenestategame.cpp:7975) mode 1
                if (m != null) Me.Apply(m);
                GD.Print($"live: home command {(m != null ? $"score {m.Point} object {m.ObjectId}" : "other mode")}");
            }
            else if (game.Is(p, Inbound.Ugc))
            {
                switch (UgcMessage.Read(p.Reader()))
                {
                    case UgcProfileImage pi: OnProfileImage(pi); break;
                    case UgcSetMode sm: ProfileImageService.Images.Mode = sm.Value; GD.Print($"live: ugc mode {sm.Value}"); break;
                    case var other: GD.Print($"live: ugc mode 0x{other.Mode:x2} (not read)"); break;
                }
            }
            else if (game.Is(p, Inbound.ProxyGameObj))
            {
                Me.Apply(ProxyGameObj.Read(p.Reader()));          // the own AddPlayer proxy's trophy counts
            }
            else if (game.Is(p, Inbound.SkillPoint))
            {
                SkillPoints.Apply(SkillPointPacket.Read(p.Reader()));   // CSceneStateGame::OnSkillPoint (scenestategame.cpp:4241)
                SkillPointsChanged?.Invoke();
                GD.Print($"live: skill points rank 1 {SkillPoints.RankTotal(1)} rank 2 {SkillPoints.RankTotal(2)} extra {SkillPoints.Extra}");
            }
            else if (game.Is(p, Inbound.SkillBookTree))
            {
                var t = SkillBookTreePacket.Read(p.Reader());   // UISkillBookTreeDialog::vfunc_79 (uiskillbookautodistributedialog.cpp:1962)
                GD.Print($"live: skill book tree mode {t.Mode} ({t.Tabs.Count} tabs)");
                SkillBookTree?.Invoke(t);
            }
            else if (game.Is(p, Inbound.FieldPortal)) OnFieldPortal(FieldPortalMessage.Read(p.Reader()));
            else if (game.Is(p, Inbound.GameToGame)) OnGameToGame(GameToGame.Read(p.Reader()));
            else if (game.Is(p, Inbound.EnterUgcMap))
            {
                var m = EnterUgcMapMessage.Read(p.Reader());
                GD.Print($"live: EnterUgcMap mode {m.Mode}");
                PortalAction.OnEnterUgcMap(m);
            }
            else if (game.Is(p, Inbound.GlobalFactor))
            {
                bool first = Factors.Apply(p.Reader());   // CSceneStateGame::OnGlobalFactor (scenestategame.cpp:4851): taken once; the quest reward rescale (QuestRates) reads it
                GD.Print($"live: global factor {(first ? "applied" : "ignored (already taken)")}");
            }
            else if (LogPackets) GD.Print($"live: packet 0x{p.Opcode:X4} ({p.Body.Length} bytes)");
        }
        catch (Exception ex) { GD.PushWarning($"live: packet 0x{p.Opcode:X4}: {ex.Message}"); }
    }

    /// <summary>
    /// OnCharacterProfileImage (@0x14188e200) in the field: the PC of the object id, if there is one, gets its picture's path
    /// (FUN_141028be0 with a download: one starts when the file is not there), and the windows refresh with the key
    /// (UIUGCEventDispatcher vfunc_1). The PC's own picture changes only when a download arrives (<see cref="PictureArrived"/>).
    /// The login server's form (object id 0) is read in the lobby (<see cref="OnLobbyProfileImage"/>).
    /// </summary>
    void OnProfileImage(UgcProfileImage pi)
    {
        var images = ProfileImageService.Images;
        string key = images.Mode == 1 ? "" : pi.Key ?? "";
        long id = pi.ObjectId == selfObject ? CharacterId
            : remote?.Tracker.Snapshot().FirstOrDefault(a => a.Add?.ObjectId == pi.ObjectId)?.Add?.User?.CharacterId ?? 0;
        GD.Print($"live: profile picture object {pi.ObjectId} char {pi.CharacterId} '{key}'{(id == 0 ? " (no such PC)" : "")}");
        if (id == 0) return;
        images.Path(id, key, false);
        images.Changed(pi.CharacterId, key);
    }

    /// <summary>CUgcDownloadProfile::ProcessResult (@0x1418714a0), outside the lobby with the own PC there: the key becomes the
    /// picture of the own PC when the id is its own, else of the field's user of that id.</summary>
    void PictureArrived(long characterId, string key)
    {
        if (game == null || selfObject < 0) return;
        if (characterId == CharacterId) { if (Me.User != null) Me.User.Picture = key; return; }
        var u = remote?.Tracker.Snapshot().FirstOrDefault(a => a.Add?.User?.CharacterId == characterId)?.Add?.User;
        if (u != null) u.Picture = key;
    }

    /// <summary>CUgcUploadCtrl's user: DAT_143ab2830 (UIServerListDialog::GetServerId(), the picked server), the account and
    /// the own character.</summary>
    public Ms2.Ui.Hud.UgcUser UgcUser => new(Session.ServerId, accountId, CharacterId);

    /// <summary>
    /// The lobby's login packets between requests: a request's own reply comes while it is <see cref="Busy"/>, so the ones
    /// read here would only be skipped by the next request. The Ugc profile picture answer is applied
    /// (<see cref="OnLobbyProfileImage"/>), the rest dropped.
    /// </summary>
    void PumpLogin()
    {
        if (Busy || login == null) return;
        while (login.Incoming.TryDequeue(out var p))
        {
            if (!login.Is(p, Inbound.Ugc)) continue;
            try { if (UgcMessage.Read(p.Reader()) is UgcProfileImage pi) OnLobbyProfileImage(pi); }
            catch (Exception ex) { GD.PushWarning("live: lobby ugc: " + ex.Message); }
        }
    }

    /// <summary>
    /// OnCharacterProfileImage (@0x14188e200) with object id 0, the login server's answer to the list's Ugc 11: the character
    /// list's entry gets the key (SetCharacterText) and its file is downloaded when it is not there (FUN_141884e30); the
    /// download refreshes the row. The client does this only while the character list is the scene (0x620000d); here the
    /// entry is kept in any lobby scene, as the list opens from it after the create screen's deferred scene change.
    /// </summary>
    void OnLobbyProfileImage(UgcProfileImage pi)
    {
        var images = ProfileImageService.Images;
        string key = images.Mode == 1 ? "" : pi.Key ?? "";
        var e = Session.Characters?.FirstOrDefault(c => c.User.CharacterId == pi.CharacterId);
        GD.Print($"live: lobby profile picture char {pi.CharacterId} '{key}'{(e == null ? " (not on the list)" : "")}");
        if (e == null) return;
        e.Picture = key;
        images.Path(pi.CharacterId, key, false);
    }

    /// <summary>
    /// CUgcManager::FUN_141880600's upload for a character just made (<paramref name="png"/>: its 107x107 shot): kind 5 with
    /// the user's type and account and the new character's id (also the upload's value), posted to urq.aspx. The answer
    /// (FUN_141882d80 outside the field, which reads no code: the text is the key) goes to the character list's
    /// FUN_1418ed4e0: Ugc 11 with the key on the login connection when the character is on the list. No upload base
    /// (result 1): SendUGCMode0 on the login connection.
    /// </summary>
    public void UploadNewCharacterPicture(long characterId, byte[] png)
    {
        if (png == null || png.Length == 0 || png.Length >= ProfileImages.MaxUploadSize) return;
        var body = ProfileImages.UploadBody(Session.ServerId, ProfileImages.ProfileKind, accountId, characterId, 0, characterId, png);
        bool sent = ProfileImageService.Post(body, text =>
        {
            GD.Print($"live: new character picture uploaded for {characterId}: '{text}'");
            if (Session.Characters?.Any(c => c.User.CharacterId == characterId) == true) SendLogin(new UgcProfileImageRequest(text ?? ""));
        });
        if (!sent) { GD.Print("live: no upload base, the new character gets no picture"); SendLogin(new UgcUploadFailedRequest()); }
    }

    void SendLogin(Ms2.Net.Wire.IOutPacket pk)
    {
        var l = login;
        if (l == null || game != null) return;
        l.SendAsync(pk).ContinueWith(t => GD.PushWarning($"live: login send {pk.GetType().Name} failed: {t.Exception?.GetBaseException().Message}"),
            System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>CUgcManager's SendUGCMode* packets, while the game connection is up.</summary>
    public void SendUgc(Ms2.Net.Wire.IOutPacket packet)
    {
        if (game != null) Send(packet);
    }

    /// <summary>Headless drive (MS2_LIVE_AUTO=1): the login button, then start with the first character.</summary>
    public static async Task AutoDrive(GameFlow flow)
    {
        if (System.Environment.GetEnvironmentVariable("MS2_LIVE_AUTO") != "1") return;
        var tree = flow.GetTree();
        async Task Frames(int n) { for (int i = 0; i < n; i++) await flow.ToSignal(tree, SceneTree.SignalName.ProcessFrame); }
        async Task Until(Func<bool> c, int max = 6000) { for (int i = 0; i < max && !c(); i++) await Frames(1); }
        await Until(() => tree.CurrentScene?.SceneFilePath == GameFlow.LoginScene); await Frames(30);
        UiBridge.Raise(UILoginDialogContract.Movie, UILoginDialogContract.ToHost.OnConnect);
        await Until(() => flow.Current == GameFlow.State.Lobby && tree.CurrentScene?.SceneFilePath == GameFlow.LobbyScene, 600); await Frames(30);
        if (flow.Current != GameFlow.State.Lobby) { GD.PushWarning("live: no character list"); tree.Quit(1); return; }
        string newName = System.Environment.GetEnvironmentVariable("MS2_LIVE_CREATE");   // create this character first (slice 6), then enter with it
        if (!string.IsNullOrEmpty(newName))
        {
            UiBridge.Raise(UICharacterListDialogContract.Movie, UICharacterListDialogContract.ToHost.OnCreatePC);
            await Until(() => flow.Current == GameFlow.State.Create && tree.CurrentScene?.SceneFilePath == GameFlow.CreateScene, 600); await Frames(120);
            int before = Session.List().Count;
            Ms2Godot.Ui.Create.CharacterCreateScene.PickJobScripted();
            await Frames(10); await Until(() => Ms2Godot.Ui.Create.CharacterCreateScene.OnCreatePage, 600); await Frames(60);
            Ms2Godot.Ui.Create.CharacterCreateScene.ConfirmScripted(newName);
            await Until(() => flow.Current == GameFlow.State.Lobby && Session.List().Count > before, 600); await Frames(60);
            if (Session.List().Count <= before) { GD.PushWarning("live: create did not add a character"); tree.Quit(1); return; }
            // the new character's picture (FUN_141880600's upload, then the login server's Ugc answer) and its download
            var made = Session.List().Last();
            await Until(() => !string.IsNullOrEmpty(made.Picture) && ProfileImageService.Images.Exists(ProfileImages.FileName(made.Picture)), 900); await Frames(60);
            GD.Print($"live: new character {made.User.CharacterId} picture '{made.Picture}'");
            // MS2_LIVE_CREATE_SHOTS=<dir>: the list with the new character's row (create_list.png)
            if (System.Environment.GetEnvironmentVariable("MS2_LIVE_CREATE_SHOTS") is { Length: > 0 } listShots)
            {
                DirAccess.MakeDirRecursiveAbsolute(listShots);
                flow.GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(listShots, "create_list.png"));
            }
        }
        if (System.Environment.GetEnvironmentVariable("MS2_LIVE_DELETE") == "1")   // delete the last character through the dialog's OnDeletePC, then stop
        {
            int n0 = Session.List().Count;
            Session.SelectedIndex = n0 - 1;
            UiBridge.Raise(UICharacterListDialogContract.Movie, UICharacterListDialogContract.ToHost.OnDeletePC);
            await Frames(30);   // the confirmation box: the checkbox, the character's name, OK
            MessageBoxLayer.Instance?.Tick(0);
            MessageBoxLayer.Instance?.Type(Session.List()[n0 - 1].User.Name);
            await Frames(30);
            MessageBoxLayer.Instance?.Click(0);
            await Until(() => Session.List().Count < n0, 600); await Frames(60);
            bool gone = Session.List().Count == n0 - 1 && flow.Current == GameFlow.State.Lobby;
            GD.Print(gone ? $"live: delete run done, {n0} -> {Session.List().Count}" : "live: delete run failed");
            tree.Quit(gone ? 0 : 1); return;
        }
        string pick = System.Environment.GetEnvironmentVariable("MS2_LIVE_CHAR");   // enter with this character name (default: the first)
        var first = newName != null && newName != "" ? Session.List().Last() : !string.IsNullOrEmpty(pick) ? Session.List().FirstOrDefault(c => c.User.Name == pick) : Session.List().FirstOrDefault();
        if (first == null) { GD.PushWarning("live: no character"); tree.Quit(1); return; }
        UiBridge.Raise(UICharacterListDialogContract.Movie, UICharacterListDialogContract.ToHost.OnGameStart, first.User.CharacterId.ToString());
        await Until(() => flow.Current == GameFlow.State.Game);
        // MS2_LIVE_HOLD: seconds to stay in the field (a movement script runs meanwhile)
        int hold = int.TryParse(System.Environment.GetEnvironmentVariable("MS2_LIVE_HOLD"), out var hs) ? hs : 2;
        // MS2_LIVE_PROFILE_SHOTS=<dir>: the profile picture's upload through the character window and the upload dialog
        // (0x4e56): MS2_LIVE_PROFILE_IMAGE (a PNG, DDS or JPEG) copied into the upload folder as a player would, the window's
        // picture clicked, the dialog's first thumbnail clicked (the preview), Upload, the terms box agreed; shots of each
        // step and of the window once the own picture came back
        string profileShots = System.Environment.GetEnvironmentVariable("MS2_LIVE_PROFILE_SHOTS");
        if (!string.IsNullOrEmpty(profileShots) && tree.CurrentScene is Ui.HudScene hud)
        {
            DirAccess.MakeDirRecursiveAbsolute(profileShots);
            void Shot(string name) { flow.GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(profileShots, name)); GD.Print("live: shot " + name); }
            string image = System.Environment.GetEnvironmentVariable("MS2_LIVE_PROFILE_IMAGE");
            if (!string.IsNullOrEmpty(image) && System.IO.File.Exists(image))
            {
                string to = UgcFileService.Global(Ms2.Ui.Hud.UgcManager.ImageFolder + "\\" + System.IO.Path.GetFileName(image));
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(to)!);
                System.IO.File.Copy(image, to, true);
            }
            await Frames(240);
            hud.ClickChangeProfile();       // the window opens
            await Frames(120);
            Shot("profile_before.png");
            hud.ClickChangeProfile();       // its picture: the upload dialog
            await Until(() => hud.UploadDialog != null, 600); await Frames(120);
            Shot("profile_dialog.png");
            GD.Print($"live: upload dialog lists {hud.UploadDialog?.Manager.FileCount} file(s) in {UgcFileService.Global(Ms2.Ui.Hud.UgcManager.ImageFolder, true)}");
            hud.ClickUploadDialog("m_imageList", 0.125, 0.125);     // the first thumbnail: OnPreview
            await Frames(60);
            Shot("profile_preview.png");
            hud.ClickUploadDialog("m_btnUpload");                   // OnUpload: the terms box
            await Frames(60);
            Shot("profile_terms.png");
            bool back = false;
            void Seen(long id, string key) { if (id == Instance?.CharacterId && !string.IsNullOrEmpty(key)) back = true; }
            ProfileImageService.Images.Arrived += Seen;     // the download of the new picture finished
            MessageBoxLayer.Instance?.Tick(0);
            await Frames(10);
            MessageBoxLayer.Instance?.Click(0);
            await Until(() => back, 60 * 20);
            ProfileImageService.Images.Arrived -= Seen;
            GD.Print(back ? "live: own profile picture came back" : "live: no profile picture came back");
            await Frames(60);
            Shot("profile_after.png");
        }
        // MS2_LIVE_MAIL_SHOTS=<dir>: the mail list opened as O does; on each tab (MS2_LIVE_MAIL_TABS, default "0,1": 0 the
        // system mails, 1 the player mails) the list is shot, its first row clicked (the read), the take (the page's button;
        // on tab 1 the row's check mark, Collect and the box's OK), then the page's remove button and the box's OK, with a
        // shot after each as mail_<tab>_<step>.png. MS2_LIVE_MAIL_STEPS=list stops each tab at its list shot (no read, take or
        // remove request is sent)
        string mailShots = System.Environment.GetEnvironmentVariable("MS2_LIVE_MAIL_SHOTS");
        if (!string.IsNullOrEmpty(mailShots) && tree.CurrentScene is Ui.HudScene mailHud)
        {
            DirAccess.MakeDirRecursiveAbsolute(mailShots);
            void Shot(string name) { flow.GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(mailShots, name)); GD.Print("live: shot " + name); }
            await Frames(240);
            mailHud.OpenMailScripted();
            await Until(() => mailHud.MailList != null, 600); await Frames(180);
            Shot("mail_open.png");
            string tabs = System.Environment.GetEnvironmentVariable("MS2_LIVE_MAIL_TABS");
            foreach (var tab in (string.IsNullOrEmpty(tabs) ? "0,1" : tabs).Split(','))
            {
                bool Step(string what, params string[] path)
                {
                    bool hit = mailHud.ClickMail(path);
                    GD.Print($"live: mail tab {tab} {what}: {(hit ? "clicked" : "not there")}");
                    return hit;
                }
                int before = Instance?.Mails.Mails.Count ?? 0;
                Step("tab", "m_tab", "tab:" + tab);
                await Frames(120);
                Shot($"mail_{tab}_list.png");
                if (System.Environment.GetEnvironmentVariable("MS2_LIVE_MAIL_STEPS") == "list") continue;
                if (!Step("row", "m_list", "row:0")) continue;
                await Frames(120);
                Shot($"mail_{tab}_read.png");
                if (tab == "1")
                {
                    // the player mails' page button is the reply: the take is the row's check mark, the list's Collect
                    // (onReceiveList) and its box's OK
                    Step("check", "m_list", "row:0", "m_checkbox");
                    await Frames(30);
                    Step("collect", "m_receiveAll");
                    await Frames(60);
                    Shot($"mail_{tab}_take_box.png");
                    MessageBoxLayer.Instance?.Click(0);
                }
                else Step("take", "m_contents", "#0", "ok");
                await Frames(180);
                Shot($"mail_{tab}_take.png");
                if (!Step("remove", "m_contents", "#0", "remove")) continue;
                await Frames(60);
                Shot($"mail_{tab}_remove_box.png");
                MessageBoxLayer.Instance?.Click(0);
                await Until(() => (Instance?.Mails.Mails.Count ?? 0) < before, 60 * 5); await Frames(120);
                GD.Print($"live: mail tab {tab}: {before} -> {Instance?.Mails.Mails.Count} mails");
                Shot($"mail_{tab}_removed.png");
            }
        }
        // MS2_LIVE_GUILD_SHOTS=<dir> with MS2_LIVE_GUILD_STEP: one scripted step of the guild dialog against the live
        // server, each with its shots. create: the Create page's button, the name (MS2_LIVE_GUILD_NAME) into the box, the
        // box's OK, the created box's OK. apply: the Join page's first row's join. accept: the accept page's first row's
        // accept. look: the Info and Member pages. leave / break: the Info page's button, the box's OK.
        string guildShots = System.Environment.GetEnvironmentVariable("MS2_LIVE_GUILD_SHOTS");
        if (!string.IsNullOrEmpty(guildShots) && tree.CurrentScene is Ui.HudScene guildHud)
        {
            DirAccess.MakeDirRecursiveAbsolute(guildShots);
            void Shot(string name) { flow.GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(guildShots, name)); GD.Print("live: shot " + name); }
            async Task<bool> BoxUp(int max = 60 * 10)
            {
                for (int i = 0; i < max; i++) { if (MessageBoxLayer.Instance?.Shown ?? false) return true; await Frames(1); }
                return false;
            }
            string step = System.Environment.GetEnvironmentVariable("MS2_LIVE_GUILD_STEP") ?? "";
            await Frames(240);
            guildHud.OpenGuildScripted();
            await Until(() => guildHud.GuildDialog != null, 600); await Frames(180);
            switch (step)
            {
                case "create":
                {
                    Shot("guild_before.png");
                    guildHud.GuildDialog.OpenCategoryPage("Create");
                    await Frames(120);
                    Shot("guild_create_page.png");
                    GD.Print($"live: guild create page button {(guildHud.ClickGuild(guildHud.GuildDialog.CreatePage, "m_createGuildButton") ? "clicked" : "not there")}");
                    await Frames(60);
                    await BoxUp();
                    MessageBoxLayer.Instance?.Type(System.Environment.GetEnvironmentVariable("MS2_LIVE_GUILD_NAME") ?? "");
                    await Frames(30);
                    Shot("guild_create_box.png");
                    MessageBoxLayer.Instance?.Click(0);
                    await Until(() => Instance?.Guilds.InGuild ?? false, 60 * 20);
                    await Frames(60);
                    await BoxUp();
                    Shot("guild_created_box.png");
                    MessageBoxLayer.Instance?.Click(0);       // the created box's OK
                    await Frames(60);
                    Shot("guild_created.png");
                    break;
                }
                case "apply":
                {
                    await Until(() => Instance?.Guilds.SearchResults.Count > 0, 60 * 20); await Frames(120);
                    Shot("guild_search_list.png");
                    GD.Print($"live: guild search {(Instance?.Guilds.SearchResults.Count ?? 0)} result(s), join row {(guildHud.ClickGuild(guildHud.GuildDialog.JoinPage, "m_searchGuild", "m_list", "row:0", "m_joinButton") ? "clicked" : "not there")}");
                    await Until(() => Instance?.Guilds.MyRequests.Count > 0, 60 * 10); await Frames(120);
                    Shot("guild_applied.png");
                    break;
                }
                case "accept":
                {
                    await Until(() => Instance?.Guilds.RequestsForGuild.Count > 0, 60 * 20); await Frames(120);
                    guildHud.GuildDialog.OpenCategoryPage("AcceptMember");
                    await Frames(180);
                    Shot("guild_accept_list.png");
                    GD.Print($"live: guild accept row {(guildHud.ClickGuild(guildHud.GuildDialog.AcceptRequestPage, "m_list", "row:0", "m_accept") ? "clicked" : "not there")}");
                    await Until(() => Instance?.Guilds.RequestsForGuild.Count == 0, 60 * 10); await Frames(120);
                    Shot("guild_accept_done.png");
                    guildHud.GuildDialog.OpenCategoryPage("Info");
                    await Frames(180);
                    Shot("guild_info_2.png");
                    guildHud.GuildDialog.OpenCategoryPage("Member");
                    await Frames(180);
                    Shot("guild_members_after_join.png");
                    break;
                }
                case "look":
                {
                    await Until(() => Instance?.Guilds.InGuild ?? false, 60 * 10); await Frames(60);
                    Shot("guild_info.png");
                    guildHud.GuildDialog.OpenCategoryPage("Member");
                    await Frames(180);
                    Shot("guild_members.png");
                    break;
                }
                case "leave":
                {
                    await Until(() => guildHud.GuildDialog?.InfoPage != null, 600); await Frames(60);
                    Shot("guild_info_before_leave.png");
                    GD.Print($"live: guild leave button {(guildHud.ClickGuild(guildHud.GuildDialog.InfoPage, "m_leaveGuild") ? "clicked" : "not there")}");
                    await Frames(60);
                    await BoxUp();
                    Shot("guild_leave_box.png");
                    MessageBoxLayer.Instance?.Click(0);
                    await Until(() => !(Instance?.Guilds.InGuild ?? true), 60 * 10);
                    GD.Print($"live: guild leave {((Instance?.Guilds.InGuild ?? true) ? "STILL IN" : "done")}");
                    await Frames(120);
                    Shot("guild_left.png");
                    break;
                }
                case "break":
                {
                    await Until(() => guildHud.GuildDialog?.InfoPage != null, 600); await Frames(60);
                    Shot("guild_info_before_break.png");
                    GD.Print($"live: guild break button {(guildHud.ClickGuild(guildHud.GuildDialog.InfoPage, "m_breakGuild") ? "clicked" : "not there")}");
                    await Frames(60);
                    await BoxUp();
                    Shot("guild_break_box.png");
                    MessageBoxLayer.Instance?.Click(0);
                    await Until(() => !(Instance?.Guilds.InGuild ?? true), 60 * 10);
                    GD.Print($"live: guild break {((Instance?.Guilds.InGuild ?? true) ? "STILL IN" : "done")}");
                    await Frames(120);
                    Shot("guild_broken.png");
                    break;
                }
            }
        }
        // MS2_LIVE_CHAT="line|line|...": each line sent from the first chat window, waiting for a chat line to come back (at
        // most 5 s) and 2 s more; "~N" waits N s instead. With MS2_LIVE_CHAT_SHOTS=<dir> the screen is shot after each as
        // chat_<n>.png
        string chat = System.Environment.GetEnvironmentVariable("MS2_LIVE_CHAT");
        if (!string.IsNullOrEmpty(chat) && tree.CurrentScene is Ui.HudScene chatHud)
        {
            string chatShots = System.Environment.GetEnvironmentVariable("MS2_LIVE_CHAT_SHOTS");
            if (!string.IsNullOrEmpty(chatShots)) DirAccess.MakeDirRecursiveAbsolute(chatShots);
            await Frames(240);
            int n = 0;
            foreach (var line in chat.Split('|'))
            {
                if (line.StartsWith('~') && int.TryParse(line.AsSpan(1), out int wait)) await Frames(60 * wait);   // "~N": wait N s, then shoot
                else if (line == "@return") { chatHud.PressChatReturn(); await Frames(30); }   // "@return": Return in the chat window
                else if (line == "@groupcreate" && Instance != null)
                {
                    // "@groupcreate": the group chat browser's insert button; waits for the new chat (at most 5 s)
                    int before = Instance.GroupChats.ChatCount;
                    chatHud.CreateGroupChat();
                    await Until(() => Instance == null || Instance.GroupChats.ChatCount > before, 60 * 5);
                    GD.Print($"live: group chat create {n}: {Instance?.GroupChats.ChatCount ?? 0} chats (was {before})");
                    await Frames(120);
                }
                else if (line.StartsWith("@group:") && Instance != null)
                {
                    // "@group:<text>": the text sent from the current group window; waits for a group chat packet (at most 5 s)
                    int before = Instance.groupChatPackets;
                    GD.Print($"live: group chat line {n}: {(chatHud.SubmitGroupLine(line.Substring(7)) ? "sent" : "no group window")}");
                    await Until(() => Instance == null || Instance.groupChatPackets > before, 60 * 5);
                    GD.Print($"live: group chat line {n}: {(Instance != null && Instance.groupChatPackets > before ? "a packet came" : "nothing came")}");
                    await Frames(120);
                }
                else if (line == "@link")
                {
                    // "@link": the first held item shift-clicked into the chat input from the inventory and sent
                    bool back = false, linked = false;
                    void Seen(Ms2.Net.Packets.ChatMessage m, Func<Ms2.Net.Packets.WeddingChatHall> _) => back = true;
                    void Links(Ms2.Net.Packets.ChatItemLinks l) => linked = true;
                    bool onItem = chatHud.OpenInventoryOnAnItem();
                    if (!onItem) { await Frames(180); onItem = chatHud.OpenInventoryOnAnItem(); }
                    GD.Print($"live: chat link {n}: inventory {(onItem ? "on an item" : "has no item")}");
                    await Frames(120);
                    if (!string.IsNullOrEmpty(chatShots))
                        flow.GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(chatShots, $"chat_{n}_inventory.png"));
                    if (Instance != null) { Instance.UserChat += Seen; Instance.UserChatItemLink += Links; }
                    long uid = chatHud.ShiftClickFirstItemAndSend();
                    GD.Print($"live: chat link {n}: {(uid != 0 ? $"item {uid} linked and sent" : "nothing linked")}");
                    await Until(() => back, 60 * 5);
                    if (Instance != null) { Instance.UserChat -= Seen; Instance.UserChatItemLink -= Links; }
                    GD.Print($"live: chat link {n}: line {(back ? "came back" : "did not come back")}, item links {(linked ? "came" : "did not come")}");
                    await Frames(120);
                }
                else
                {
                    bool back = false;
                    void Seen(Ms2.Net.Packets.ChatMessage m, Func<Ms2.Net.Packets.WeddingChatHall> _) => back = true;
                    if (Instance != null) Instance.UserChat += Seen;
                    GD.Print($"live: chat line {n}: {(chatHud.SubmitChatLine(line) ? "sent" : "no chat window")}");
                    await Until(() => back, 60 * 5);
                    if (Instance != null) Instance.UserChat -= Seen;
                    await Frames(120);
                }
                if (!string.IsNullOrEmpty(chatShots))
                    flow.GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(chatShots, $"chat_{n}.png"));
                n++;
            }
        }
        // MS2_LIVE_TRADE: "ask:<name>[:<mesos>]" waits for that player in the field and asks through the popup menu's Trade;
        // "answer[:<mesos>]" accepts the request box. Round one: the asker adds its first two bag items and takes the first
        // back off, the answerer adds its first; both lock with the mesos and confirm after both locks. Round two: the asker
        // asks again, adds its first item and cancels. The bags and mesos are logged before and after each round.
        string tradeRun = System.Environment.GetEnvironmentVariable("MS2_LIVE_TRADE");
        if (!string.IsNullOrEmpty(tradeRun) && tree.CurrentScene is Ui.HudScene th)
        {
            var parts = tradeRun.Split(':');
            bool ask = parts[0] == "ask";
            string mesos = parts.Length > (ask ? 2 : 1) ? parts[ask ? 2 : 1] : "0";
            await Frames(240);
            GD.Print($"live: trade: bag before: {th.BagText()}");
            async Task<bool> Open(string who)
            {
                if (ask)
                {
                    Ms2.Net.Packets.UserBlock partner = null;
                    await Until(() => (partner = Instance?.FieldUsers().FirstOrDefault(u => u.Name == who)) != null, 60 * 90);
                    GD.Print(partner != null ? $"live: trade: {who} is in the field" : $"live: trade: {who} never came");
                    if (partner != null) th.TradeAsk(partner.CharacterId);
                }
                else
                {
                    await Until(() => MessageBoxLayer.Instance?.Shown == true, 60 * 120);
                    GD.Print($"live: trade: request box {(MessageBoxLayer.Instance?.Shown == true ? "shown, accepting" : "never shown")}");
                    await Frames(60);
                    MessageBoxLayer.Instance?.Click(0);
                }
                await Until(() => th.TradeDialogShown, 60 * 30);
                GD.Print($"live: trade: dialog {(th.TradeDialogShown ? "open" : "did not open")}");
                await Frames(120);
                return th.TradeDialogShown;
            }
            async Task Add(int n)
            {
                long uid = th.TradeRightClickItem(n);
                await Frames(60);
                if (uid != 0 && MessageBoxLayer.Instance?.Shown == true) { GD.Print("live: trade: count box, OK"); MessageBoxLayer.Instance.Click(0); }
                await Frames(120);
                GD.Print($"live: trade: table {th.TradeTableText()}");
            }
            string who = ask && parts.Length > 1 ? parts[1] : "";
            if (await Open(who))
            {
                if (ask)
                {
                    await Add(0);
                    await Add(0);   // an added item leaves the bag: the next one is the first held again
                    th.TradeRemoveMine(0);
                    await Frames(120);
                    GD.Print($"live: trade: took slot 0 back, table {th.TradeTableText()}");
                }
                else { await Frames(240); await Add(0); await Frames(240); }   // the asker locks first
                // a change on either side (the other's mesos among them) unlatches both: lock again while mine is off; the
                // mesos sent last are not sent again (SendTradeMode10), so the relock is the latch alone
                th.TradeCallback("OnLock", mesos);
                await Frames(90);
                for (int tries = 0; tries < 10 && !th.TradeBothLatched; tries++)
                {
                    await Until(() => th.TradeBothLatched || !th.TradeMineLatched, 60 * 6);
                    if (!th.TradeMineLatched && th.TradeDialogShown) { GD.Print("live: trade: unlatched, locking again"); th.TradeCallback("OnLock", mesos); await Frames(60); }
                }
                GD.Print($"live: trade: both latched {th.TradeBothLatched}, mine {Instance?.Trades.Confirmed}, table {th.TradeTableText()}");
                await Frames(60);
                th.TradeCallback("OnConfirm");
                await Until(() => !th.TradeDialogShown, 60 * 30);
                GD.Print($"live: trade: dialog {(th.TradeDialogShown ? "still open" : "closed")}");
                await Frames(180);
                GD.Print($"live: trade: bag after the trade: {th.BagText()}");
                // round two: the cancel
                if (ask) await Frames(60 * 8);
                if (await Open(who))
                {
                    if (ask)
                    {
                        await Add(0);
                        await Frames(120);
                        GD.Print("live: trade: cancel");
                        th.TradeCallback("OnCancel");
                    }
                    await Until(() => !th.TradeDialogShown, 60 * 30);
                    GD.Print($"live: trade: dialog {(th.TradeDialogShown ? "still open" : "closed")}");
                    await Frames(180);
                    GD.Print($"live: trade: bag after the cancel: {th.BagText()}");
                }
            }
        }
        // MS2_LIVE_MOVES="step|step|...": field moves in game, each waiting for the new field's entry (FieldEntered, at most
        // 90 s) and 3 s more: "warp:<map>" (the GM chat command, the server's RequestFieldEnter), "dungeon:<room>" (the GM
        // /dungeon command: the server moves the player to the room's server with GameToGame 0x10), "portal:<id>" or
        // "portal:any" (the player put in that open portal's box, then the action key), "~N" (wait N s), "mark:<name>"
        // (the node count and the managed heap after a full collect). With MS2_LIVE_MOVE_SHOTS=<dir> the screen is shot
        // before and after each move as move_<n>_before.png / move_<n>_after.png. With MS2_LIVE_MOVE_PROBE=1 each move's
        // loading screen gets input: the move key (Up) held and the inventory key (I) pressed under it, and the frames in
        // which any of it reached the player or the HUD are counted (Ms2.Ui.FieldMoveCover); once the cover is down the
        // held key must move the player, which shows the injected key works.
        bool probeInput = System.Environment.GetEnvironmentVariable("MS2_LIVE_MOVE_PROBE") == "1";
        string moves = System.Environment.GetEnvironmentVariable("MS2_LIVE_MOVES");
        if (!string.IsNullOrEmpty(moves) && Instance is { } live)
        {
            string moveShots = System.Environment.GetEnvironmentVariable("MS2_LIVE_MOVE_SHOTS");
            if (!string.IsNullOrEmpty(moveShots)) DirAccess.MakeDirRecursiveAbsolute(moveShots);
            void MoveShot(string name)
            {
                if (string.IsNullOrEmpty(moveShots)) return;
                flow.GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(moveShots, name));
            }
            int entered = 0; long lastMs = 0; int lastMap = 0;
            void Entered(int map, long ms) { entered++; lastMs = ms; lastMap = map; }
            live.FieldEntered += Entered;
            await Frames(240);
            int n = 0;
            foreach (var step in moves.Split('|'))
            {
                if (step.StartsWith('~') && int.TryParse(step.AsSpan(1), out int wait)) { await Frames(60 * wait); continue; }
                if (step.StartsWith("mark:"))
                {
                    await Frames(30);
                    System.GC.Collect(); System.GC.WaitForPendingFinalizers(); System.GC.Collect();
                    GD.Print($"live: mark {step[5..]}: nodes {Performance.GetMonitor(Performance.Monitor.ObjectNodeCount)}, objects "
                             + $"{Performance.GetMonitor(Performance.Monitor.ObjectCount)}, managed heap {System.GC.GetTotalMemory(true) / 1024} KiB, "
                             + $"static {Performance.GetMonitor(Performance.Monitor.MemoryStatic) / 1048576:F1} MiB, map {live.CurrentEntry?.MapId}");
                    continue;
                }
                int before = entered, from = live.CurrentEntry?.MapId ?? 0;
                bool sent = false;
                MoveShot($"move_{n}_before.png");
                if (step.StartsWith("warp:"))
                {
                    var parts = step[5..].Split(':');
                    string cmd = "/warp " + parts[0] + (parts.Length > 1 ? " --portal " + parts[1] : "");
                    sent = tree.CurrentScene is Ui.HudScene h && h.SubmitChatLine(cmd);
                    GD.Print($"live: move {n}: {cmd} {(sent ? "sent" : "no chat window")}");
                }
                else if (step.StartsWith("dungeon:"))
                {
                    string cmd = "/dungeon " + step[8..];
                    sent = tree.CurrentScene is Ui.HudScene h && h.SubmitChatLine(cmd);
                    GD.Print($"live: move {n}: {cmd} {(sent ? "sent" : "no chat window")}, {live.Migrations} GameToGame move(s) so far");
                }
                else if ((step.StartsWith("portal:") || step.StartsWith("gated:")) && tree.GetFirstNodeInGroup(FieldContents.Group) is FieldContents fc)
                {
                    // portal:<id|any> walks into a server-listed portal the action key goes to; gated:<id|any> into one it
                    // must not (hidden, without glow, or of a type whose path is not ported): the key there sends nothing
                    bool gated = step.StartsWith("gated:");
                    string id = step[(step.IndexOf(':') + 1)..];
                    var list = live.Portals?.All ?? (IReadOnlyList<Ms2.Ui.LivePortal>)Array.Empty<Ms2.Ui.LivePortal>();
                    bool Open(Ms2.Ui.LivePortal x) => x.Glow && x.TargetMap != 0 && x.Type is not (8 or 9 or 0xc or 0xe or 0x10 or 0x11);
                    var p = id == "any" ? list.FirstOrDefault(x => x.HasBox && (gated ? !Open(x) : Open(x))) : list.FirstOrDefault(x => x.Id.ToString() == id);
                    int sends = live.PortalSends;
                    if (p != null && fc.StandInPortal(p.Id))
                    {
                        await Frames(60);   // the position goes out with the next sync, as a walk into the box would; the poll runs each frame
                        MoveShot($"move_{n}_in_portal.png");
                        if (fc.LivePortalPoll()) fc.LivePortalUse();
                        await Frames(10);
                        sent = live.PortalSends > sends;
                    }
                    string what = p == null ? "not listed here" : $"{p.Id} type {p.Type} shown {p.Shown} glow {p.Glow} -> field {p.TargetMap}";
                    if (gated)
                    {
                        GD.Print($"live: move {n}: gated portal {id} ({what}): {(p == null ? "SKIP" : sent ? "FAIL, a move was sent" : "OK, nothing sent")}");
                        sent = false;
                    }
                    else GD.Print($"live: move {n}: portal {id} ({what}), {(sent ? "sent" : "not sent")}");
                }
                var probe = sent && probeInput ? ProbeUnderCover(n) : null;
                if (sent)
                {
                    await Until(() => entered > before, 60 * 90);
                    if (probe != null) await probe;
                    // the longest frame in the 3 s after the entry: a hitch once the new field is up
                    var clock = System.Diagnostics.Stopwatch.StartNew(); double worst = 0;
                    for (int i = 0; i < 180; i++) { double t0 = clock.Elapsed.TotalMilliseconds; await Frames(1); worst = Math.Max(worst, clock.Elapsed.TotalMilliseconds - t0); }
                    GD.Print(entered > before ? $"live: move {n}: map {from} -> {lastMap} in {lastMs} ms, longest frame after {worst:F0} ms"
                                              : $"live: move {n}: no field entry from map {from}");
                }
                MoveShot($"move_{n}_after.png");
                n++;
            }
            live.FieldEntered -= Entered;
        }
        await Frames(60 * hold);
        bool ok = flow.Current == GameFlow.State.Game;
        GD.Print(ok ? "live: auto run done" : "live: auto run failed");
        tree.Quit(ok ? 0 : 1);
        return;

        static void Key(Godot.Key key, bool down) =>
            Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = down });
        async System.Threading.Tasks.Task ProbeUnderCover(int n)
        {
            await Until(() => Ms2.Ui.FieldMoveCover.Up, 60 * 10);
            if (!Ms2.Ui.FieldMoveCover.Up) { GD.Print($"live: move {n}: probe: the cover never came up"); return; }
            Key(Godot.Key.Up, true);
            Key(Godot.Key.I, true); Key(Godot.Key.I, false);
            int frames = 0, inputFrames = 0, movedFrames = 0, inventoryFrames = 0, players = 0;
            var seen = new Dictionary<ulong, Vector3>();
            var notes = new List<string>();
            while (Ms2.Ui.FieldMoveCover.Up && frames < 60 * 120)
            {
                await Frames(1);
                if (!Ms2.Ui.FieldMoveCover.Up) break;      // the frame the cover went down is the field's
                frames++;
                if (tree.CurrentScene is Ui.HudScene h && h.InventoryOpen) inventoryFrames++;
                bool input = false, moved = false;
                foreach (var node in tree.GetNodesInGroup(Player.Group))
                {
                    if (node is not Player pl || !IsInstanceValid(pl) || !pl.IsInsideTree()) continue;
                    var id = pl.GetInstanceId();
                    var now = pl.GlobalPosition;
                    if (!seen.TryGetValue(id, out var at)) players++;
                    seen[id] = now;
                    // a walk step: across the ground (a fall or the arrival's placement is not input), under a teleport's size
                    var step = new Vector2(now.X - at.X, now.Z - at.Z).Length();
                    bool walk = at != default && step > 0.05f && step < 50f;
                    bool held = pl.Input2 != Vector2.Zero || pl.MouseInput != Vector2.Zero || pl.JumpHeld;
                    if (held) input = true;
                    if (walk) moved = true;
                    if ((held || walk) && notes.Count < 6)
                        notes.Add($"f{frames} {id % 10000}: input {pl.Input2}/{pl.MouseInput}/{pl.JumpHeld} step {step:F2} dy {now.Y - at.Y:F2} state {pl.State}");
                }
                if (input) inputFrames++;
                if (moved) movedFrames++;
            }
            // the cover is down: the same held key now moves the player
            var after = tree.GetFirstNodeInGroup(Player.Group) as Player;
            var start = after?.GlobalPosition ?? Vector3.Zero;
            await Frames(45);
            float walked = after != null && IsInstanceValid(after) ? after.GlobalPosition.DistanceTo(start) : -1;
            Key(Godot.Key.Up, false);
            await Frames(10);
            GD.Print($"live: move {n}: probe under the cover {frames} frames ({players} player(s)): input reached a player in "
                     + $"{inputFrames}, a player walked in {movedFrames}, the inventory was open in {inventoryFrames}; "
                     + $"after the cover the held key moved the player {walked:F0} units");
            foreach (var x in notes) GD.Print($"live: move {n}: probe {x}");
        }
    }
}
