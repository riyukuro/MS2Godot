using Godot;
using Ms2.Formats.Model;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Ui;
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
    bool answered;           // ResponseFieldEnter sent for this entry
    public bool Busy { get; private set; }
    /// <summary>The last create the server refused (null after a create that went through).</summary>
    /// <summary>The account's character limit from CharMaxCount 0xB8 (DAT_1439f4a84); 0 until the server sent it.</summary>
    public int CharacterLimit;
    /// <summary>The quest state the quest packets build (live only; the quest log reads it, offline it stays empty).</summary>
    public readonly QuestBook Quests = new();
    /// <summary>CPartyManager's party from the party packets (0x53); live only.</summary>
    public readonly PartyBook Party = new();
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
    /// <summary>The adventure data the client keeps in globals (DAT_143ab2958..3318): session-lived, so a field change keeps
    /// the exp, the filled level, the claimed levels and the missions.</summary>
    public readonly Ms2.Ui.AdventureData Adventure = new();
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
    public CharacterCreateRefused LastRefusal { get; private set; }
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
    /// <summary>IsDialogClosed(0x4e49) == 0: the mail list is open (set by the HUD).</summary>
    public Func<bool> MailListOpen = () => false;
    /// <summary>The mail dialogs' sends (Mail 0x2D).</summary>
    public void SendMail(Ms2.Net.Wire.IOutPacket packet) => Send(packet);

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

    /// <summary>The NpcTalk packets (0x4B, UITalkManager::OnTalkPacket) for the talk window; live only.</summary>
    public event Action<NpcTalkMessage> NpcTalkReceived;
    /// <summary>The talk window's NpcTalk (0x22) and Quest (0x29) sends.</summary>
    public void SendTalk(Ms2.Net.Wire.IOutPacket packet) => Send(packet);
    /// <summary>The party window's and the popup menu's sends (Party 0x2C, RoomDungeon, DungeonReward, Club).</summary>
    public void SendParty(Ms2.Net.Wire.IOutPacket packet) => Send(packet);
    /// <summary>The chat window's UserChat (0x11) sends (FUN_141276f80).</summary>
    public void SendChat(Ms2.Net.Wire.IOutPacket packet) => Send(packet);
    /// <summary>The field's live npcs (by FieldAddNpc), for the talk; empty before the field is built.</summary>
    public IEnumerable<FieldNpcs.Placed> TalkNpcs => remote?.TalkNpcs ?? Enumerable.Empty<FieldNpcs.Placed>();
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
        try
        {
            var go = await GameClient.SelectAsync(login, chosen.User.CharacterId);
            (entry, game) = await GameClient.EnterAsync(go, accountId, machine, host, requestGlobalFactor: !Factors.Applied);
            Quests.QuestInfo = QuestTable();
            ItemReader.SlotOf ??= ItemSlotTable();
            ItemReader.ExtraOf ??= id => { try { return Kinds().ExtraOf(id); } catch (Exception e) { GD.Print($"live: item kinds unavailable ({e.Message})"); return null; } };
            ItemReader.Known ??= id => { try { return Kinds().Known(id); } catch { return true; } };   // data unavailable: read every id
            Items.TabOf = id => { try { return Kinds().TabOf(id); } catch { return 7; } };
            Items.BaseSlots ??= BaseSlotTable();
            if (Inventory == null) { Inventory = new InventoryView(Items); Inventory.RequestSort = (tab, stack) => Send(new ItemInventorySortRequest((byte) tab, stack)); Inventory.RequestMove = (uid, slot) => Send(new ItemInventoryMoveRequest(uid, slot)); Inventory.RequestEquip = (uid, slot, mode) => Send(new ItemEquipRequest(uid, slot, mode)); Inventory.RequestUnequip = uid => Send(new ItemUnequipRequest(uid)); Inventory.RequestSwapEquip = (uid, slot) => Send(new ItemSwapEquipRequest(uid, slot)); Inventory.RequestEquipGem = (uid, slot) => Send(new GemEquipRequest(uid, slot)); Inventory.RequestUse = (uid, arg) => Send(new ItemUseRequest(uid, arg)); Inventory.ServerTime = () => GameSession?.ServerTime ?? 0; }
            Items.MaxStackOf = id => { try { return Kinds().SlotMax(id); } catch { return uint.MaxValue; } };
            // what the server sent before the field enter, in arrival order, through the read loop's handler (no HUD exists yet):
            // the adventure level, quest, item, trophy, title and key table loads among them
            entry.Replay(Handle, () =>
            {
                Adventure.ServerEnter();
                StubHudData.Instance.Wallet = entry.Enter?.Wallet;
                Me.Apply(entry.Enter);                 // the exp counters (OnServerEnter, scenestategame.cpp:2085-2086)
            });
            GD.Print($"live: {entry.Early.Count} packets before the field: {Quests.All.Count} quests, {Items.All.Count} items ({entry.EarlyItems.Count} packets), adventure claimed {Adventure.Claimed.Count}, missions {Adventure.Missions.Count}");
            login.Dispose(); login = null;
            answered = false;
            syncNumber = entry.Sync?.Number ?? 0;
            var f = entry.Field;
            chosen.User.MapA = f.MapId;
            GD.Print($"live: RequestFieldEnter map {f.MapId} pos {f.Position} rot {f.Rotation} fieldKey {f.FieldKey}");
            return true;
        }
        catch (Exception ex) { GD.PushWarning("live: enter failed: " + ex.Message); return false; }
        finally { Busy = false; }
    }

    /// <summary>The server's arrival (position, rotation, field key) on the move the flow built for the chosen map.</summary>
    public static Travel.Move Decorate(Travel.Move m)
    {
        var f = Instance?.entry?.Field;
        if (m == null || f == null) return m;
        return m with { At = new Vec3(f.Position.X, f.Position.Y, f.Position.Z), RotZDeg = f.Rotation.Z, FieldKey = f.FieldKey };
    }

    void OnFieldBuilt(Field field, ModelBuilder builder)
    {
        if (entry == null) return;
        remote = new RemoteLayer(field, builder, entry.Enter.CharacterId);
        remote.Tracker.Profile = game.Profile;
        field.TreeExiting += () => { remote = null; };   // the field is freed: its tracker stops filling Events
        GD.Print("live: remote layer ready");
    }

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
        if (entry == null || answered) return;
        answered = true;
        Send(new ResponseFieldEnter(entry.Field.FieldKey));
        BindPlayer();
        GD.Print($"live: ResponseFieldEnter sent, fieldKey {entry.Field.FieldKey}");
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

    void BindPlayer()
    {
        player = GetTree().Root.FindChild("Player", true, false) as Player;
        if (player == null) { GD.PushWarning("live: no player to send movement for"); return; }
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
        if (game == null) return;
        while (game.Incoming.TryDequeue(out var p)) Handle(p);
        if (game.IsClosed) { GD.PushWarning("live: game connection closed" + (game.Failure != null ? ": " + game.Failure.Message : "")); game.Dispose(); game = null; if (player != null) player.Live = false; }   // offline fall-return emulation comes back
    }

    /// <summary>One game server packet: the read loop's, and at the entry the ones that came before the field enter
    /// (<see cref="GameEntry.Replay"/>).</summary>
    void Handle(Ms2.Net.Wire.RawPacket p)
    {
        try
        {
            bool own = game.Is(p, Inbound.Stat) && p.Body.Length >= 4 && BitConverter.ToInt32(p.Body, 0) == selfObject;
            if (own) { var st = PlayerStat.Read(p.Reader()); ApplyStat(st); Me.Apply(st); }
            else remote?.Tracker.Inbound(p);     // others' users and npcs (FieldTracker keeps the rest)
            if (own) { }
            else if (game.Is(p, Inbound.FieldAddUser))
            {
                var u = FieldAddUser.Read(p.Reader());
                bool self = u.User.CharacterId == entry.Enter.CharacterId;
                if (self) { selfObject = u.ObjectId; Me.Apply(u); }
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
            else if (game.Is(p, Inbound.LevelUp))
            {
                var l = LevelUp.Read(p.Reader());  // CSceneStateGame::OnLevelUp (scenestategame.cpp:9511)
                bool selfLevel = Me.Apply(l);
                GD.Print($"live: level up object {l.ObjectId} level {l.Level}{(selfLevel ? " SELF" : "")}");
            }
            else if (game.Is(p, Inbound.SyncNumber))
            {
                syncNumber = SyncNumber.Read(p.Reader()).Number;
                player?.OnSyncNumber();
                GD.Print($"live: SyncNumber {syncNumber}");
            }
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
                Quests.Apply(q);
                GD.Print($"live: quest {q.GetType().Name} ({Quests.All.Count} quests)");
            }
            else if (game.Is(p, Inbound.Party))
            {
                var m = PartyMessage.Read(p.Reader());   // CPartyManager::OnParty (partymanager.cpp:447); no ExpectEnd: the client ignores trailing bytes
                Party.Apply(m, CharacterId, PartyWindowOpen());
                GD.Print($"live: party {m.GetType().Name} ({Party.Members.Count} members)");
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
                GD.Print($"live: badge equip mode {b.Mode} by {b.ObjectId}{(b.Mode == 4 ? $" error {b.Error}" : "")}{(ownGems ? " (own gems)" : "")}");
            }
            else if (game.Is(p, Inbound.UserState))
            {
                var u = UserState.Read(p.Reader());     // OnUserState (scenestategame.cpp:5758): the state, then state 12 runs PC::vfunc_137
                if (Me.ApplyUserState(u.ObjectId, u.State)) GD.Print($"live: own player dead (state {u.State})");
            }
            else if (game.Is(p, Inbound.DeadUser))
            {
                var d = DeadUser.Read(p.Reader());      // OnDeadUser (scenestategame.cpp:6532)
                if (Me.Die(d.ObjectId)) GD.Print("live: own player dead");
            }
            else if (game.Is(p, Inbound.Revival))
            {
                var v = Revival.Read(p.Reader());       // OnRevival (scenestategame.cpp:7387)
                if (Me.Revive(v.ObjectId)) GD.Print("live: own player revived");
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
            else if (game.Is(p, Inbound.Job))
            {
                var j = JobPacket.Read(p.Reader());   // CSceneStateGame::OnJob (scenestategame.cpp:7449); no ExpectEnd: the client ignores trailing bytes
                bool selfJob = j.ObjectId == selfObject;   // FindPCById: a PC not in the field yet drops the packet, as here
                if (selfJob && j.Book != null) { SkillBook = j.Book; SkillBookChanged?.Invoke(j.Mode); }
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
            else if (game.Is(p, Inbound.UserEnv))
            {
                var m = UserEnvMessage.Read(p.Reader());          // OnUserEnv (scenestategame.cpp:4414); the other modes are not kept
                if (m is UserEnvMessage.Collection c) { CollectionGrades = c.Grades; CollectionChanged?.Invoke(); }
                else if (m != null) Me.Apply(m);
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
    /// STILL-OPEN: the login server's form (object id 0, the lobby's character list) is not read; the lobby has no packet
    /// pump after the list.
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
        await Frames(60 * hold);
        bool ok = flow.Current == GameFlow.State.Game;
        GD.Print(ok ? "live: auto run done" : "live: auto run failed");
        tree.Quit(ok ? 0 : 1);
    }
}
