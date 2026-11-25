using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.World;
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
/// stubs. The launcher's Connect picks the login server (<see cref="UseServer"/>); the command line's --ip and --port (the Lith launcher's own arguments, <see cref="LaunchArgs"/>) override it
/// and starts the live mode without the launcher, and MS2_LIVE_ACCOUNT (user:password) overrides the id and password typed
/// in the client's login dialog. Offline stays the default and nothing here runs then. MS2_LIVE_AUTO=1 drives the screens
/// by itself (first character, no input), for a headless run. The transports only queue packets on their network thread;
/// this node drains them in _Process.
/// </summary>
public partial class LiveSession : Node
{
    static string launchHost;
    static int launchPort;

    /// <summary>The in-client launcher's picked login server; the command line's --ip/--port override it at login time.</summary>
    public static void UseServer(string host, int port) { launchHost = host; launchPort = port; }
    /// <summary>
    /// The command line the game was started with: the engine's own arguments and the user arguments (after "--"). The Lith launcher's
    /// form (<c>1234 --ticket=.. --ip=.. --port=..</c>) is read from either: an exported exe takes it as is (OS.GetCmdlineArgs), and
    /// a project run through the editor binary needs it after "--" (OS.GetCmdlineUserArgs). Parsed once per call; the ticket is never logged.
    /// </summary>
    public static Ms2.Net.LauncherArgs LaunchArgs() => Ms2.Net.LauncherArgs.Parse(OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()).ToArray());

    /// <summary>Drops the launcher's server, so a later start does not inherit it.</summary>
    public static void ClearServer() { launchHost = null; launchPort = 0; }

    /// <summary>The address from --ip/--port when it starts the live mode without the in-client launcher (with --ticket or MS2_LIVE_ACCOUNT); null otherwise.</summary>
    public static string CommandLineLogin
    {
        get
        {
            var a = LaunchArgs();
            if (a.Problem != null) { WarnArgs(a); return null; }
            return Ms2.Ui.Launch.LaunchRoute.LiveAddress(a, System.Environment.GetEnvironmentVariable("MS2_LIVE_ACCOUNT"));
        }
    }

    static string warnedArgs;
    /// <summary>Names the problem with the launcher arguments once; never the values (the ticket is a credential).</summary>
    static void WarnArgs(Ms2.Net.LauncherArgs a)
    {
        if (warnedArgs == a.Problem) return;
        warnedArgs = a.Problem;
        GD.PushWarning("live: launcher arguments ignored, " + a.Problem);
    }

    public static bool Enabled => CommandLineLogin != null || (!string.IsNullOrEmpty(launchHost) && launchPort > 0);

    /// <summary>CNetworkService::IsConnected for the login scene: the login connection is open.</summary>
    public bool LoginConnected => login is { Session: not null, IsClosed: false };
    public static LiveSession Instance { get; private set; }
    /// <summary>A live session is running: the field's offline npcs, mobs and portal walking stay off; the server's replace them.</summary>
    public static bool Active => Instance != null;

    readonly byte[] machine = new byte[16];
    TcpTransport login, game;
    Func<bool> inGame;
    long accountId;
    GameEntry entry;
    string host;
    int selfObject = -1;     // the own FieldAddUser object id
    /// <summary>StateWaitingSelect's id, which OnUserBattle leaves the emotion alone in.</summary>
    const int StateWaitingSelect = 0xe;

    /// <summary>The own player's battle stance flag (FieldAddUser.InBattle and OnUserBattle 0x2F).</summary>
    public bool SelfInBattle { get; private set; }
    byte syncNumber;         // CNetworkService::OnSyncNumber: the number UserSync carries
    Player player;           // the own player, once the field is built

    /// <summary>
    /// MyPC::vfunc_137's vfunc_59(0xc) on the body (<see cref="Player.EnterDead"/>): StateDead::vfunc_4's revive-in-place case
    /// for a field with HasAutoRevivalType1 plays MODEPVP_DEAD entry ShadowWorldAutoReviveDeadAction (clientconstants +0xd248,
    /// default 1). STILL-OPEN: the PvP point case (the team as the entry) is not fed, no PvP info is kept.
    /// </summary>
    void OwnDeathState()
    {
        if (player == null || !IsInstanceValid(player) || Me.State != 0xc) return;
        bool inPlace = false; int anim = 1;
        try
        {
            var packs = Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir);
            if (FieldId is int id) inPlace = FieldData.FindById(packs, id)?.HasAutoRevivalType1 == true;
            if (inPlace) anim = FieldContents.Constants(packs).Int("ShadowWorldAutoReviveDeadAction", 1);
        }
        catch (Exception ex) { GD.PushWarning($"death state: {ex.Message}"); }
        bool entering = player.State != Player.MoveState.Dead;
        player.EnterDead(inPlace, anim);
        // the MyPC block (statedead.cpp:155-167): the revive in place plays dead_Start_MyPC_PvP (CommonResource +0x278,
        // kept in m_nEffect), the normal death dead_Start_MyPC (+0x280)
        if (entering) PlayOwnCommonEffect(inPlace ? DeathEffectInPlaceKey : DeathEffectKey);
        // StateDead::vfunc_4's normal branch (statedead.cpp:143-145): PC::FUN_14165cac0 builds the tomb on entering
        if (entering && !inPlace && triggerField != null && IsInstanceValid(triggerField) && fieldBuilder != null)
        {
            if (Tomb != null && IsInstanceValid(Tomb)) Tomb.QueueFree();
            var body = player;
            Tomb = Movement.PlayerTombstone.Build(triggerField, triggerField.Packs, fieldBuilder, body,
                () => body.State == Player.MoveState.Dead, OwnRevivalBlocked);
        }
    }
    /// <summary>CommonResource keys of StateDead's own effects (+0x278 m_strDeadStartMyPCPvP, +0x280 m_strDeadStartMyPC) and of
    /// OnRevivalConfirm's (+0x358 m_strEffectResolvePanelty).</summary>
    public const string DeathEffectInPlaceKey = "dead_Start_MyPC_PvP", DeathEffectKey = "dead_Start_MyPC", RevivalPenaltyEndKey = "EFFECT_RESOLVE_PANELTY";
    /// <summary>The own tombstone (PC m_spFC8) while it stands, else null or a freed node.</summary>
    public Movement.PlayerTombstone Tomb { get; private set; }
    /// <summary>The hit sound pick before the field's layers exist (the tomb is a field object, so not reached in practice).</summary>
    static readonly Func<uint> NoHitSoundRand = () => 0u;
    ModelBuilder fieldBuilder;          // the field build's model builder, for the tomb
    /// <summary>The own buffs' IsDisableRevivalHere, set by the HUD that holds the buff list; false without one.</summary>
    public Func<bool> BuffsDisableRevival = () => false;
    /// <summary>PC::IsRevivalBlocked (pc.cpp:2009) for the own player.</summary>
    public bool OwnRevivalBlocked => Me.RevivalBlocked || BuffsDisableRevival();
    /// <summary>The own body's <see cref="Player.DeadFlag"/> follows MyCharacter.DeathOnlyStates (PC::vfunc_124, pc.cpp:1199)
    /// after each packet: the own FieldAddUser with no health, DeadUser, Revival and the health stat move it.</summary>
    void SyncDeadFlag()
    {
        if (player != null && IsInstanceValid(player)) player.DeadFlag = Me.DeathOnlyStates;
    }
    RemoteLayer remote;      // the other players and npcs, once the field is built
    DropLayer drops;         // the dropped items and meso, once the field is built
    /// <summary>The "get" action key is held (set by the HUD's key tracking); the drop layer asks it each frame.</summary>
    public bool PickupKeyHeld;
    readonly FieldEntry fieldEntry = new();   // RequestFieldEnter: the current entry and its one ResponseFieldEnter
    public bool Busy { get; private set; }
    /// <summary>The last create the server refused (null after a create that went through).</summary>
    /// <summary>The account's character limit from CharMaxCount 0xB8 (DAT_1439f4a84); 0 until the server sent it.</summary>
    public int CharacterLimit;
    /// <summary>CharMaxCount's second value (DAT_1439f4a88): the character list's row count (RefreshLobby's maxRow,
    /// uicharacterlistdialog.cpp:1352); 0 until the server sent it.</summary>
    public int CharacterRows;
    /// <summary>The last character list's EndList flag (m_1e8, OnCharacterListPacket mode 4): the game start checks the
    /// name first (<see cref="Ms2.Ui.InvalidNameRename"/>).</summary>
    public bool CheckNames;
    /// <summary>The quest state the quest packets build (live only; the quest log reads it, offline it stays empty).</summary>
    public readonly QuestBook Quests = new();
    /// <summary>CPartyManager's party from the party packets (0x53); live only.</summary>
    public readonly PartyBook Party = new();
    /// <summary>TSingleton&lt;CBattleRecordManager&gt; (ms2.cpp:2031): the DPS meters' tallies, fed by the damage records, the exp and
    /// meso packets, with the party's member records. A field pet's damage goes to its owner (the tracker's FieldAddPet owners);
    /// the chaos group runs in a dungeon room whose row has chaosDamageMeter.</summary>
    public readonly BattleRecordManager BattleRecords = new();
    // the battle records' lookups, made once rather than per SkillDamage packet
    Func<int, bool> objectInField, isRegionSkill;
    bool ObjectInField(int id) => remote?.Tracker.HasObject(id) ?? (id == selfObject);
    /// <summary>DAT_143ab2884: the dungeon room id of the last field entry (FUN_140930ba0 of RequestFieldEnter's fifth int).</summary>
    int dungeonRoomId;
    /// <summary>DAT_143ab2884: the dungeon room id of the field entered (0 outside a dungeon).</summary>
    public int DungeonRoomId => dungeonRoomId;
    /// <summary>DungeonRoomTable::GetRoom over the HUD's room table; null before the HUD sets it or without the row.</summary>
    public Func<int, Ms2.Quest.DungeonRoom> DungeonRoomOf;

    /// <summary>CubeMapInformationService::FUN_1410d0310: the cube map's values and the home option rows go together (<see cref="Ms2.Ui.HomeOptionRows.ResetService"/>).</summary>
    void ResetCubeService() => HomeRows.ResetService(fieldEntry.CubeMap);

    /// <summary>The cut scene movie pane's state (UICutSceneMovie 0x4e7e): the Trigger UI's start and stop reach it, its skip sends the exit.</summary>
    public Ms2.Ui.Hud.CutSceneMovie Movie { get; }

    /// <summary>The cut scene cinematic pane's state (UICutSceneCinematic 0x4e52): the Cinematic packet drives it.</summary>
    public Ms2.Ui.Hud.CutSceneCinematic Cinematic { get; } = new();

    public LiveSession()
    {
        InitDungeonRecords();
        Movie = new Ms2.Ui.Hud.CutSceneMovie(Send) { SoundFade = Ms2Godot.FieldAudio.CutSceneSoundFade };
        fieldEntry.CubeServiceCleared += ResetCubeService;   // FUN_1410b76a0 at each field teardown (worldservice_1.cpp:10015)
        Party.BattleRecords = BattleRecords;
        FieldProperties.UserTagSymbolApplied += () => remote?.RefreshFieldSymbols();   // FieldPropertyUserTagSymbolCli::vfunc_4
        BattleRecords.CharacterIdOf = id => remote?.Tracker.CharacterIdOf(id) ?? (id == selfObject ? CharacterId : 0);
        BattleRecords.NpcClassOf = id => remote?.NpcClassOf(id) ?? 0;
        BattleRecords.PetOwnerOf = id => remote?.Tracker.PetOwnerOf(id) ?? 0;
        // battlerecordmanager.cpp:188: the room id set, its row found, and the row's chaosDamageMeter
        BattleRecords.ChaosRoomMeter = () => dungeonRoomId != 0 && DungeonRoomOf?.Invoke(dungeonRoomId) is { ChaosDamageMeter: true };
        AutoEquip.FeatureEnabled = AutoEquipFeature;
        WireMe();
    }

    void WireMe()
    {
        // the own put-on of the auto-equip's pending item and the own take-off's exclusion (OnItemPutOn :3125, MyPC::vfunc_128)
        Me.OwnItemPutOn += OwnPutOnAutoEquip;
        Me.OwnItemTakenOff += ExcludeOwnTakenOff;
        // the server's state change (a death included) leaves the running interaction, react or ride summon state
        Me.StateEntered += st => InteractKey.LeaveForServerState(st, InteractObjects, InteractNpcs, reactState, rideSummon);
        Me.StateEntered += LeaveRecallForServerState;
        Me.StateEntered += EnterOwnStateExit;   // a server state the body has no move state for runs the client's own exit
        ownStateExit.TalkUp = OwnTalkUp;
        ownStateExit.OwnBalloonActive = OwnBalloonActive;
    }

    /// <summary>OnLevelUp's own part (scenestategame.cpp:9565): the auto-equip of the level's items.</summary>
    void AutoEquipOnLevelUp(int level)
    {
        AutoEquip.OnLevelUp(level);
        if (LiveCheck)
            GD.Print($"live: auto-equip level {level}: on {AutoEquip.IsAutoEquipEnabled}, pending {AutoEquip.PendingItem}, queued {AutoEquip.QueuedCount}");
    }

    /// <summary>MyPC::vfunc_128 (mypc.cpp:4903): an item with a model preset the own PC took off is excluded from the auto-equip.</summary>
    void ExcludeOwnTakenOff(long uid)
    {
        AutoEquip.ExcludeFromAutoEquip(uid);
        if (LiveCheck) GD.Print($"live: auto-equip excluded uid {uid} (taken off), {AutoEquip.ExcludedUids.Count} excluded");
    }

    /// <summary>OnItemPutOn for the own PC (scenestategame.cpp:3125-3138): the pending item put on clears it, and the HUD shows
    /// the notice and closes the bind box.</summary>
    void OwnPutOnAutoEquip(EquipItem item)
    {
        if (!AutoEquip.OnOwnPutOn(item.Uid)) return;
        if (LiveCheck) GD.Print($"live: auto-equip put on {item.Id} uid {item.Uid}");
        AutoEquipPutOn?.Invoke(item.Id);
    }

    /// <summary>The own pc's exit from a state the server put it into (Ms2.Ui.OwnStateExit), with no move state of the body.</summary>
    readonly Ms2.Ui.OwnStateExit ownStateExit = new();

    /// <summary>The live run's count of server states entered and the exit time the last one started with (seconds, below 0
    /// none, 0 at once).</summary>
    internal int ServerStatesEntered;
    internal double ServerStateExitLeft;
    internal int OwnStateExitRunning => ownStateExit.State;
    /// <summary>The sequence the running own state plays on the body (null none).</summary>
    internal string OwnStatePlayed => ownStateExit.Played;
    internal bool OwnStatePlayedLoops => ownStateExit.PlayedLoops;
    /// <summary>The sequence the last fresh entry of a state run here asked for (a state left on its entry frame shows it
    /// for no frame, as the client's next update leaves it).</summary>
    internal string OwnStateEnterPlayed;

    /// <summary>The live run's raw UserState (OnUserState: i32 object id, u8 state) for the own pc, as the server's debug
    /// packet command takes it: each byte in hex, the opcode first.</summary>
    /// <summary>A ResponseRide for the own pc as the server's debug packet command takes it (hex bytes): mode 0 with a
    /// RideOnAction of type 0 (ride id, the mount's object id, and KMS's flag byte), or mode 1 with a plain RideOffAction.</summary>
    internal string OwnRideBytes(bool on, int rideId, int mountObjectId)
    {
        var w = new Maple2.PacketLib.Tools.ByteWriter();
        w.WriteShort((short)game.Profile.Number(Ms2.Net.Inbound.ResponseRide));
        w.WriteByte(on ? (byte)0 : (byte)1);
        w.WriteInt(Me.ObjectId);
        w.WriteByte(0);
        if (on)
        {
            w.WriteInt(rideId);
            w.WriteInt(mountObjectId);
            if (!game.Profile.IsGms2) w.WriteBool(false);
        }
        else w.WriteByte(0);
        return string.Join(" ", w.ToArray().Select(b => b.ToString("X2")));
    }

    static string Hex(IEnumerable<byte> b) => string.Join(" ", b.Select(x => x.ToString("X2")));
    static void Op(List<byte> b, ushort op) { b.Add((byte)op); b.Add((byte)(op >> 8)); }
    static void I32(List<byte> b, int v) { b.Add((byte)v); b.Add((byte)(v >> 8)); b.Add((byte)(v >> 16)); b.Add((byte)(v >> 24)); }
    static void Narrow(List<byte> b, string s) { b.Add((byte)s.Length); b.Add((byte)(s.Length >> 8)); b.AddRange(System.Text.Encoding.ASCII.GetBytes(s)); }
    static void Unicode(List<byte> b, string s) { b.Add((byte)s.Length); b.Add((byte)(s.Length >> 8)); b.AddRange(System.Text.Encoding.Unicode.GetBytes(s)); }

    /// <summary>The live run's raw NpcNotice (the layout OnNpcNotice reads, scenestategame.cpp:9145-9215): mode 0 an announce
    /// of <paramref name="text"/> for <paramref name="ms"/>; mode 3 a talk side popup (kind 0) with the illustration
    /// <paramref name="icon"/>, as the Maple2 server writes an npc AI's sidePopup node (no name, voice or sound here).</summary>
    internal string NpcNoticeBytes(byte mode, string text, int ms, string icon = "")
    {
        var b = new List<byte>();
        Op(b, game.Profile.Number(Ms2.Net.Inbound.NpcNotice)); b.Add(mode);
        if (mode == 0) { Unicode(b, text); I32(b, ms); }
        else { b.Add(0); I32(b, ms); Narrow(b, ""); Narrow(b, icon); Narrow(b, ""); Narrow(b, ""); Unicode(b, text); }
        return Hex(b);
    }

    /// <summary>The live run's raw NpcRide (the layout OnNpcRide and CNpc::FUN_141092c50 read): i32 npc object id, u8 mode, then
    /// mode 0 an i64 accumulated damage, mode 1 the owner's i32 object id.</summary>
    internal string NpcRideBytes(int npc, byte mode, long value = 0)
    {
        var b = new List<byte>();
        Op(b, game.Profile.Number(Ms2.Net.Inbound.NpcRide)); I32(b, npc); b.Add(mode);
        if (mode == 0) { I32(b, (int)value); I32(b, (int)(value >> 32)); }
        else if (mode == 1) I32(b, (int)value);
        return Hex(b);
    }

    /// <summary>The live run's raw Insignia for the own pc (OnNametag: i32 object, u16 symbol, u8 shown).</summary>
    internal string OwnInsigniaBytes(ushort symbol, bool shown)
    {
        var b = new List<byte>();
        Op(b, game.Profile.Number(Ms2.Net.Inbound.Insignia)); I32(b, Me.ObjectId);
        b.Add((byte)symbol); b.Add((byte)(symbol >> 8)); b.Add(shown ? (byte)1 : (byte)0);
        return Hex(b);
    }

    internal string OwnUserStateBytes(int state)
    {
        ushort op = game.Profile.Number(Ms2.Net.Inbound.UserState);
        int id = Me.ObjectId;
        return $"{op & 0xff:X2} {op >> 8:X2} {id & 0xff:X2} {(id >> 8) & 0xff:X2} {(id >> 16) & 0xff:X2} {(id >> 24) & 0xff:X2} {state & 0xff:X2}";
    }

    /// <summary>The live run's raw OnBuff for the own pc, as <see cref="OwnUserStateBytes"/>: mode 0 adds effect
    /// <paramref name="id"/> at <paramref name="level"/> under <paramref name="key"/> for <paramref name="ms"/> from now
    /// (the owner header, then the state: start and end tick, id, level, overlap 1, enabled; a shield of 0), mode 1 removes
    /// the key (the header only).</summary>
    /// <summary>The last OnBuff packet of the own pc (for the live run's report).</summary>
    internal BuffPacket LastOwnBuff;

    internal string OwnBuffBytes(byte mode, int key, uint id, ushort level, uint ms)
    {
        var b = new List<byte>();
        void I32(uint v) { b.Add((byte)v); b.Add((byte)(v >> 8)); b.Add((byte)(v >> 16)); b.Add((byte)(v >> 24)); }
        ushort op = game.Profile.Number(Ms2.Net.Inbound.Buff);
        b.Add((byte)op); b.Add((byte)(op >> 8)); b.Add(mode);
        I32(unchecked((uint)Me.ObjectId)); I32(unchecked((uint)key)); I32(unchecked((uint)Me.ObjectId));
        if (mode == 0)
        {
            uint now = unchecked((uint)game.Session.ServerTick);
            I32(now); I32(now + ms); I32(id); b.Add((byte)level); b.Add((byte)(level >> 8)); I32(1); b.Add(1);
            I32(0); I32(0);
        }
        return string.Join(" ", b.Select(x => x.ToString("X2")));
    }

    /// <summary>The live run's raw notice (inbound 0x71, <see cref="Ms2.Net.Packets.NoticeMessage"/>): kind 4 with
    /// <paramref name="flags"/>, the common string <paramref name="stringId"/> as the client numbers it, then the u16 for
    /// flags 0x10 or the i32 for 0x200, as the server fork writes them.</summary>
    internal string NoticeBytes(ushort flags, int stringId, int value)
    {
        var b = new List<byte>();
        void I32(int v) { b.Add((byte)v); b.Add((byte)(v >> 8)); b.Add((byte)(v >> 16)); b.Add((byte)(v >> 24)); }
        ushort op = game.Profile.Number(Ms2.Net.Inbound.Notice);
        b.Add((byte)op); b.Add((byte)(op >> 8)); b.Add(4); b.Add((byte)flags); b.Add((byte)(flags >> 8));
        b.Add(1); I32(1); I32(stringId); I32(0);
        if ((flags & 0x10) != 0) { b.Add((byte)value); b.Add((byte)(value >> 8)); }
        else if ((flags & 0x200) != 0) I32(value);
        return string.Join(" ", b.Select(x => x.ToString("X2")));
    }

    void EnterOwnStateExit(int st)
    {
        if (ownStunEntry) return;   // the stun buff's entry of the running StateStun (TickOwnStateExit)
        bool fresh = ownStateExit.State != st;
        bool atOnce = ownStateExit.Enter(st, Me.ObjectId);
        if (fresh && ownStateExit.State == st) OwnStateEnterPlayed = ownStateExit.Played;
        ServerStatesEntered++;
        ServerStateExitLeft = atOnce ? 0 : ownStateExit.State == st ? ownStateExit.Left : -1;
        if (fresh && ownStateExit.State == st)
            if (LiveCheck) GD.Print($"live: server state 0x{st:x} runs its own exit: {ownStateExit.Cause}{(ownStateExit.Sequence != null ? " " + ownStateExit.Sequence : "")}, {(ownStateExit.Left < 0 ? "none comes" : $"{ownStateExit.Left:0.###} s")}{(ownStateExit.Played != null ? ", plays " + ownStateExit.Played : "")}{(ownStateExit.Hidden ? ", hidden" : "")}");
        int fallback = ownStateExit.TakeFallback();
        bool dash = ownStateExit.TakeDashEffect();
        if (atOnce && Me.LeaveServerState(st) && LiveCheck) GD.Print($"live: server state 0x{st:x} left at once (no state data){(fallback != 0 ? $" to its fallback 0x{fallback:x}" : "")}");
        if (dash) StartSwimDashEffect();   // after the fallback's enter, inside StateSwimDash::vfunc_4
        PlayOwnStateFace();
        SyncOwnStateCast();
    }

    /// <summary>The casting bar of <see cref="ownStateExit"/>'s casting-bar states: the left state's vfunc_5 stops the bar
    /// (FUN_1418e1390), then the entered state's vfunc_4 starts its cast (StartCastingText, or StartCasting with the param's
    /// defaults for StateAutoInteraction), whose OnCastingFinished sets the state's m_n48.</summary>
    void SyncOwnStateCast()
    {
        if (ownStateExit.TakeBarStopped()) CastingBar?.Cancel();
        if (ownStateExit.TakeCast() is not { } c) return;
        ownStateCasts++;
        LastOwnStateCast = c;
        if (CastingBar == null) { GD.Print("live: server state cast with no casting bar"); return; }
        string text = c.StringCommonId == Ms2.Ui.OwnStateExit.NoCaption ? "" : Ms2.Ui.Hud.MessageBoxData.Common(c.StringCommonId);
        if (c.StringCommonId == Ms2.Ui.OwnStateExit.NoCaption)
            CastingBar.StartCasting(ownStateCastListener, new Ms2.Ui.Hud.CastingParam { Duration = c.DurationMs });
        else
            CastingBar.StartCastingText(ownStateCastListener, text, c.DurationMs, showTime: false, activeBar: true, endText: "");
        if (LiveCheck) GD.Print($"live: server state 0x{ownStateExit.State:x} casts {c.DurationMs} ms \"{text}\"");
    }

    /// <summary>The live run's count of the casts the server-set states started, and the last one.</summary>
    internal int ownStateCasts;
    internal Ms2.Ui.OwnStateExit.CastRequest LastOwnStateCast;
    /// <summary>The HUD's casting bar runs a cast (UICastingBar::IsCasting).</summary>
    internal bool CastingBarRuns => CastingBar?.IsCasting == true;

    /// <summary>The server-set casting-bar state as the bar's ICastingEventListener: its finish sets m_n48; a replacing cast
    /// calls the base's empty OnCastingCancelled (none of the four overrides it).</summary>
    sealed class OwnStateCastListener : Ms2.Ui.Hud.ICastingEventListener
    {
        readonly Ms2.Ui.OwnStateExit exit;
        public OwnStateCastListener(Ms2.Ui.OwnStateExit exit) { this.exit = exit; }
        public void OnCastingFinished() => exit.CastFinished();
        public void OnCastingCancelled() { }
    }
    OwnStateCastListener ownStateCastListenerField;
    OwnStateCastListener ownStateCastListener => ownStateCastListenerField ??= new OwnStateCastListener(ownStateExit);

    bool ownStunEntry;
    /// <summary>The own buffs' stun this frame (AdditionalBuffList.StunEntry for the pc); the HUD sets it with the buff list.</summary>
    public Func<(int Stun, int Sub, string Animation)?> OwnBuffStun = () => null;
    /// <summary>The own PC's AdditionalCliList (the HUD holds it); null without the HUD.</summary>
    public Ms2.Ui.Skills.AdditionalBuffList OwnBuffList;
    /// <summary>The live run's count of the stun buff's entries of StateStun.</summary>
    internal int BuffStunsEntered;

    /// <summary>
    /// The frame of <see cref="ownStateExit"/>: AdditionalCliList::FUN_1409713b0 (additionalcli_2.cpp:786-833) first, which
    /// enters StateStun each frame while an enabled effect stuns the living pc that is not in StateGrabTarget nor held by a
    /// riding pc with event buff 2 (<see cref="OwnStunMayEnter"/>); then the running state's frame, whose exit enters
    /// StateIdle (the body's state from then); then what it plays on the body (the sequence, StateSpawn's hide, StateStun's
    /// pause and StateTransform's effect).
    /// STILL-OPEN: the list's stun (m_motion.m_nStun, cleared when the list is summed again) is taken as an enabled stun
    /// effect being held.
    /// </summary>
    void TickOwnStateExit(double delta)
    {
        bool stunned = false;
        if (player != null && IsInstanceValid(player) && !Me.DeathOnlyStates && OwnBuffStun() is { } s && OwnStunMayEnter())
        {
            stunned = true;
            if (ownStateExit.EnterBuffStun(s.Stun, s.Sub, s.Animation))
            {
                BuffStunsEntered++;
                if (LiveCheck) GD.Print($"live: stun buff {s.Stun} enters StateStun, sub 0x{s.Sub:x}, plays {ownStateExit.Played}");
                ownStunEntry = true;
                try { Me.EnterOwnState(0x12); }
                finally { ownStunEntry = false; }
                player.EnterServerState(Ms2.Ui.BodyEntry.Idle);
                PlayOwnStateFace();
            }
        }
        int st = ownStateExit.State;
        if (st != 0)
        {
            if (ownStateExit.Advance(delta, Me.State, false, stunned) && Me.LeaveServerState(st)) { if (LiveCheck) GD.Print($"live: server state 0x{st:x} left by its own exit"); }
            else if (ownStateExit.State == 0 && Me.State != st && LiveCheck) GD.Print($"live: server state 0x{st:x} replaced by state {Me.State}");
        }
        SyncOwnStateCast();
        if (player == null || !IsInstanceValid(player)) return;
        player.SetStateSequence(ownStateExit.Played, ownStateExit.PlayedLoops, ownStateExit.PlayedIsAction, ownStateExit.Plays);
        player.SetSpawnHidden(ownStateExit.Hidden);
        player.SetAnimationPaused(ownStateExit.Paused);
        if (ownStateExit.TakeEffect() is { } fx) PlayOwnEffect(fx);
        EndTalkOfOwnState();
    }

    bool ServerStateHoldsBody() => ownStateExit.HoldsBody && Me.State == ownStateExit.State;

    /// <summary>The body's own Idle, Land and Fall while a state runs here: its frame moves by gravity only (StateStun::vfunc_3,
    /// statestun.cpp:126), so standing, falling and landing stay in it; another body state replaces it.</summary>
    void BodyStateEntered(int id)
    {
        if (ownStateExit.State != 0 && Me.State == ownStateExit.State && id is 1 or 4 or 5) return;
        Me.LeaveForBodyState(id);
    }

    double? OwnSequenceSeconds(string name) => player != null && IsInstanceValid(player) ? player.SequenceSeconds(name) : null;
    /// <summary>CGroupChatManager's chats from the group chat packets (0x8B); live only. The HUD sets its texts and its browser.</summary>
    public readonly Ms2.Ui.GroupChatBook GroupChats = new();
    /// <summary>The party window is loaded and open (FUN_141924630), for OnPartyMemberJoin's add-or-open; set by the HUD.</summary>
    public Func<bool> PartyWindowOpen = () => false;
    /// <summary>The game server session (its time sync), null before the field enter.</summary>
    public ClientSession GameSession => game?.Session;
    /// <summary>CMainSystem::GetServerTime (mainsystem.cpp:390): game or login session server clock, else UTC now.</summary>
    public long ServerTime => (game?.Session ?? login?.Session)?.ServerTime ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    /// <summary>The entered character's id (DAT_143ab2828), 0 before the field enter.</summary>
    public long CharacterId => entry?.Enter?.CharacterId ?? 0;
    /// <summary>The field the server sent the character to (RequestFieldEnter), null before the field enter.</summary>
    public int? FieldId => entry?.Field?.MapId;
    /// <summary>The item inventory from the ItemInventory packets (OnItemInventory); live only.</summary>
    public readonly ItemBook Items = new();
    /// <summary>The bank (CStore, DAT_143ab2d80): the StorageInventory packets (0x22) applied as CStore::Dispatch does.</summary>
    public readonly StorageBook Storage = new();
    /// <summary>The bank sends (RequestItemStorage 0x18).</summary>
    public void SendStorage(Ms2.Net.Wire.IOutPacket packet)
    {
        if (game != null) Send(packet);
    }
    /// <summary>The item lock (CItemLockManager): the ItemLock packets (0xDB) and the RequestItemLock sends (0x87).</summary>
    public readonly ItemLockBook LockBook = new();
    /// <summary>CItemBreakManager: the dismantle list and its sends; the HUD wires its delegates when the dialog opens.</summary>
    public readonly Ms2.Ui.Items.ItemBreakManager BreakManager = new();
    public void SendLock(Ms2.Net.Wire.IOutPacket packet)
    {
        if (game != null) Send(packet);
    }
    /// <summary>The item enchant dialog's model (UIItemEnchantDialog): the ItemEnchant packets (0x97) and the RequestItemEnchant sends (0x5E).
    /// Find (the UI item of a uid) is set by the scene that opens the dialog.</summary>
    public readonly Ms2.Ui.Items.ItemEnchantBook EnchantBook = new();
    public void SendEnchant(Ms2.Net.Wire.IOutPacket packet)
    {
        if (game != null) Send(packet);
    }
    /// <summary>The inventory dialog model over Items; a UI binds it. Only its Sort sends RequestItemInventory (the sort request, 0x14199c230); a tab switch sends nothing.</summary>
    public InventoryView Inventory { get; private set; }
    /// <summary>The field properties 0xCA sets (FieldProperties); a new field clears them.</summary>
    public readonly Ms2.Ui.Hud.FieldProperties FieldProperties = new();
    public readonly GlobalFactor Factors = new();   // CGloabalFactor: exp/meso rates of OnGlobalFactor
    /// <summary>CKeyTable's server side: the key bindings and quick slot pages (current page included) of OnKeyTable; live only.</summary>
    public readonly ServerKeyTable KeyTable = new();
    /// <summary>Raised after a KeyTable packet was applied (its mode: 0 load, 7 pages).</summary>
    public event Action<int> KeyTableChanged;
    /// <summary>Outbound KeyTable (0x3A) mode 1: bind a single key (CKeyTable::SendKeyTableMode1, keytable.cpp:378).</summary>
    public void SendKeyTableBind(KeyBinding binding) { if (game != null) Send(new KeyTableBindRequest(binding)); }
    /// <summary>Outbound KeyTable (0x3A) mode 2: save all key bindings (CKeyTable::SendKeysToServer, keytable.cpp:399).</summary>
    public void SendKeyTableSaveAll(IReadOnlyList<KeyBinding> bindings) { if (game != null) Send(new KeyTableSaveAllRequest(bindings)); }
    /// <summary>Outbound KeyTable (0x3A) mode 3: set a quick slot on a page (unit_14092f420_accountsessionstate.cpp:352).</summary>
    public void SendKeyTableSetQuickSlot(int page, int slot, QuickSlotEntry entry) { if (game != null) Send(new KeyTableSetQuickSlotRequest((short)page, entry.SkillId, entry.ItemId, entry.ItemUid, slot)); }
    /// <summary>Outbound KeyTable (0x3A) mode 4: add a quick slot on a page (unit_14092f420_accountsessionstate.cpp:418).</summary>
    public void SendKeyTableAddQuickSlot(int page, QuickSlotEntry entry) { if (game != null) Send(new KeyTableAddQuickSlotRequest(page, entry)); }
    /// <summary>Outbound KeyTable (0x3A) mode 5: remove a quick slot on a page (unit_14092f420_accountsessionstate.cpp:481).</summary>
    public void SendKeyTableRemoveQuickSlot(int page, int skillId, long itemUid) { if (game != null) Send(new KeyTableRemoveQuickSlotRequest((short)page, skillId, itemUid)); }
    /// <summary>Outbound KeyTable (0x3A) mode 8: set the active quick slot page (CKeyTable::SetQuickSlotPage, keytable.cpp:2547).</summary>
    public void SendKeyTableSetPage(int page) { if (game != null) Send(new KeyTableSetPageRequest(page)); }
    /// <summary>OnBadgeEquip mode 4: the error code (shown as a system notice, <see cref="Ms2.Ui.Hud.MyInfoController.GemEquipErrorKey"/>).</summary>
    public event Action<short> GemEquipError;
    /// <summary>OnBadgeEquip modes 0 and 1, for any PC of the field (the own one included): a gem put on or taken off.</summary>
    public event Action<BadgeEquipMessage> BadgeEquipped;
    /// <summary>A PC's battle stance changed (OnUserBattle, scenestategame.cpp:3269-3288, on a change: PC::vfunc_66), the own one
    /// included: the object id and the new emotion (m_unk_160; nonzero is the stance).</summary>
    public event Action<int, int> BattleStanceChanged;
    int selfEmotion;
    /// <summary>A PC's battle stance (PCBase::vfunc_65): the own one's or a shown remote one's.</summary>
    public bool InBattle(int objectId) => objectId == selfObject ? SelfInBattle : remote?.InBattle(objectId) ?? false;
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
    /// <summary>WorldService::OnObjectCreated for the MyPC (OnAddUser, scenestategame.cpp:11214): the own FieldAddUser, after its
    /// stats and skill book are in; the game scene's HUD dialogs that wait for a local player open from it
    /// (<see cref="Ms2.Ui.Hud.GameSceneHudOpen"/>).</summary>
    public event Action OwnPcCreated;
    /// <summary>The skill point state of OnSkillPoint (0x44, scenestategame.cpp:4241).</summary>
    public readonly Ms2.Ui.Skills.SkillPoints SkillPoints = new();
    /// <summary>A SkillBookTree (0x10C) packet, for the skill window's vfunc_79.</summary>
    public event Action<SkillBookTreePacket> SkillBookTree;
    /// <summary>After each 0x44 (UIStatusEventDispatcher event 0xf).</summary>
    public event Action SkillPointsChanged;
    /// <summary>RequestSkillBookTree mode 0 (UISkillBookTreeDialog::OnLoadComplete): a read request for the saved tabs.</summary>
    public void RequestSkillBookTree() => Send(new SkillBookTreeRequest());
    /// <summary>The character window's stat point requests (CStatPointManager::RequestAdd / RequestReset, FUN_140710590): as
    /// <see cref="SendSkillBook"/>, false when the packet went nowhere.</summary>
    public bool SendAttributePoint(Ms2.Net.Wire.IOutPacket packet) => SendSkillBook(packet);
    /// <summary>The skill window's own requests (FUN_140710590): the tab save (RequestSkillBookTree mode 1) and Job.</summary>
    /// <summary>False when the packet went nowhere (no game connection, or the profile has no number for it): the window must not
    /// wait for an answer then.</summary>
    public bool SendSkillBook(Ms2.Net.Wire.IOutPacket packet)
    {
        var g = game;
        if (g == null) return false;
        if (!g.Profile.Has(packet.Op)) { GD.PushWarning($"live: send {packet.GetType().Name} failed: no {g.Profile.Name} number for {packet.Op}"); return false; }
        Send(packet);
        return true;
    }
    /// <summary>A SkillMacro (0xD1) mode 0 packet, the compact skill control window's vfunc_79 (@0x140f9fee0).</summary>
    public event Action<SkillMacroPacket> SkillMacro;
    /// <summary>CSceneStateGame::OnCheckCharNameResult (scenestategame.cpp:4698): the name change check's answer.</summary>
    public event Action<CheckCharNameResult> CharNameChecked;
    /// <summary>UIMessageBox::HandleChangeCharNameButton's send (uimessagebox.cpp:2109): SendOp CheckCharName.</summary>
    public void CheckCharName(string name, long itemUid) => Send(new CheckCharNameRequest(name, itemUid));
    /// <summary>The compact tabs of the last SkillMacro mode 2 (CSkillSystem::FUN_141767ae0 @0x141767ae0, m_lstCompact), or of
    /// the window's SetCompactConfigs; empty before either.</summary>
    public IReadOnlyList<SkillMacroTab> CompactConfigs { get; set; } = Array.Empty<SkillMacroTab>();
    /// <summary>SendOp::SkillMacro mode 0 (UISkillCompactControlDialog::OnLoadComplete @0x140fa06a0): a read request.</summary>
    public void RequestSkillMacro() { if (game != null) Send(new SkillMacroRequest()); }
    /// <summary>SendOp::SkillMacro mode 1 (SendSkillMacroMode1 @0x140fa1240, the window's close): every tab.</summary>
    public void SaveSkillMacro(IReadOnlyList<SkillMacroTab> tabs) { if (game != null) Send(new SkillMacroSave(tabs)); }
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
    /// <summary>The Meret packet (0x3A, OnMeret :7025) after <see cref="Me"/>'s balances were left to the HUD: the wallet and delta.</summary>
    public event Action<MeretUpdate> MeretReceived;
    /// <summary>The MeretUse packet (0x117, OnMeretUse :7053): the red balance and its delta.</summary>
    public event Action<MeretUseUpdate> MeretUseReceived;
    /// <summary>The CurrencyToken packet (0x3B, OnCurrencyToken :7094).</summary>
    public event Action<CurrencyTokenUpdate> CurrencyTokenReceived;
    /// <summary>PlaySystemSound (0xC3, :4389): the sound key.</summary>
    public event Action<SystemSound> SystemSoundReceived;
    /// <summary>SmartPush (0xD7, :7335).</summary>
    public event Action<SmartPushMessage> SmartPushReceived;
    /// <summary>GlobalPortal (0xC1, OnGlobalPortal :4369): UIGlobalPortalDialog::OnGlobalPortalPacket.</summary>
    public event Action<GlobalPortalPacket> GlobalPortalReceived;
    /// <summary>RecallScroll (0xBC, OnRecallScroll :8038): UIRecallScrollNotifyDialog's open and close.</summary>
    public event Action<RecallScrollPacket> RecallScrollReceived;
    /// <summary>NoticeDialog (0xCF, UINoticeDialog::OnNoticeDialog).</summary>
    public event Action<NoticeDialogMessage> NoticeDialogReceived;
    /// <summary>NewsNotification (0x9F, :3789).</summary>
    public event Action<NewsNotification> NewsNotificationReceived;
    /// <summary>OnItemDropNotice: a drop notice for the chat log.</summary>
    public event Action<ItemDropNotice> ItemDropNoticed;
    /// <summary>ItemUse mode 4 named a dialog to open (the item function's notifyDlg, e.g. "TimeRunEventDialog"); the dialog's own
    /// show (FUN_141924680) is the subscriber's. STILL-OPEN: no subscriber yet.</summary>
    public event Action<string> ItemNotifyDialog;

    /// <summary>CSceneStateGame::OnItemUse mode 4 (scenestategame.cpp:8627): the item's function notifyDlg names a dialog to open; else its notifyMsg
    /// shows a box: string 0x5f6 with the item's name, a line break, the common string of the key.</summary>
    void ItemUseNotify(int itemId)
    {
        var data = ItemDataOf(itemId);
        if (data == null) return;
        string dlg = data.NotifyDlg, msg = data.NotifyMsg;
        if (dlg.Length != 0) { ItemNotifyDialog?.Invoke(dlg); return; }
        if (msg.Length == 0) return;
        string name = Ms2.Ui.GameTables.Shared(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir), GameOptions.StringLanguage).ItemName(itemId);
        string text = Ms2.Quest.ClientFormat.Format(Ms2.Ui.Hud.MessageBoxData.Common(0x5f6), name) + "\n" + (Ms2.Ui.Hud.MessageBoxData.CommonByKey(msg) ?? "");
        Ms2.Ui.Hud.MessageBoxQueue.ShowMessageBox(text, 0, null, "");
    }

    /// <summary>The Job packet's mode 3 (scenestategame.cpp:7537-7565, <see cref="Ms2.Ui.JobErrorNotice"/>): error 18's box, then
    /// the notice. The client shows it for any PC FindPCById finds; the server sends it with the requester's own object, so
    /// only that one is taken here.</summary>
    void ShowJobError(int error, int value)
    {
        var (notice, box) = Ms2.Ui.JobErrorNotice.Text(error, value, Ms2.Ui.Hud.MessageBoxData.Common);
        if (box != null) Ms2.Ui.Hud.MessageBoxQueue.ShowMessageBox(box, 0, null, "");
        if (notice.Length != 0) SystemNoticeSink?.Invoke(notice);
        if (LiveCheck) GD.Print($"live: job error {error} notice \"{notice}\"{(box != null ? " with a box" : "")}");
    }

    /// <summary>The changeJobs effects played on the own PC (the scripted run counts them).</summary>
    public int JobChangeEffects { get; private set; }

    /// <summary>Raised after ItemUse mode 6 sent the Beauty request.</summary>
    public event Action<BeautyReq> BeautyRequested;
    /// <summary>OnItemUpdate (scenestategame.cpp:3214) for the local player's worn item: the item event dispatcher's vfunc_23 (the item's id), whose listeners (the item colour dialog, vfunc_24 @0x1419adf80) take it.</summary>
    public event Action<int> OwnItemUpdated;
    /// <summary>The own player's skin colour changed (OnUserSkinColor, scenestategame.cpp:3252-3257: the item event hub's slot 0, which the beauty shop listens to).</summary>
    public event Action OwnSkinColor;
    /// <summary>CSceneStateGame::OnItemBox (@0x141739e40): the box result; the consumer checks the item's function (OpenItemBox / SelectItemBox) as the client does.</summary>
    public event Action<ItemBoxResult> ItemBoxResulted;
    /// <summary>Inbound 0x31 body: UIBeautyShopDialog::OnBeautyShopPacket (uibeautyshopdialog.cpp:1510) or UIBeautyShopStyleDialog's
    /// FUN_140f05a80 (uibeautyshopstyledialog.cpp:1205) reads it, whichever is open (<see cref="Ms2.Ui.Hud.BeautyPacketRouter"/>).</summary>
    public event Action<byte[]> BeautyPacketReceived;
    /// <summary>The wire profile of the game session (KMS until one is connected); the beauty readers differ by profile.</summary>
    public ProtocolProfile GameProfile => game?.Profile ?? ProtocolProfile.Kms2525;
    /// <summary>The adventure data the client keeps in globals (DAT_143ab2958..3318): session-lived, so a field change keeps
    /// the exp, the filled level, the claimed levels and the missions.</summary>
    public readonly Ms2.Ui.AdventureData Adventure = new();
    /// <summary>The global inventory's auto-equip (feature 232): its pending item, queue, excluded items and switch live as long
    /// as the session, as the client's inventory; the HUD sets what it reads of the field. Reset by the server enter
    /// (Unk_Inventory::Clear) and the own FieldAddUser.</summary>
    public readonly Ms2.Ui.Hud.AutoEquip AutoEquip = new();
    /// <summary>The auto-equip's put-on of its pending item (OnItemPutOn, scenestategame.cpp:3125-3138): the item id, for the
    /// HUD's s_msg_item_auto_install_equipment notice and the bind box close.</summary>
    public event Action<int> AutoEquipPutOn;
    bool? autoEquipFeature;
    /// <summary>Feature 232 (0xe8, the auto-equip), as UiModel.IsFeatureEnabled reads it.</summary>
    bool AutoEquipFeature()
    {
        if (autoEquipFeature is { } on) return on;
        try { on = Ms2.Ui.LobbyFeatures.IsEnabledByName(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir), Ms2.Ui.FeatureTable.Names[Ms2.Ui.Hud.AutoEquip.FeatureId - 1]); }
        catch (Exception e) { GD.Print($"live: feature table unavailable ({e.Message})"); on = false; }
        autoEquipFeature = on;
        return on;
    }
    /// <summary>The worn lapenshards (MyPC's table, filled by OnLapenshard), session-lived like the PC.</summary>
    public readonly Ms2.Ui.LapenshardData Lapenshards = new();
    /// <summary>Inbound 0x11B, every mode the client reads (the worn table is already updated).</summary>
    public event Action<LapenshardMessage> LapenshardReceived;
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
    bool? codiFeature;
    /// <summary>Feature 0x8e (the outfit tab's period, FUN_1411df7d0), as UiModel.IsFeatureEnabled reads it.</summary>
    bool CodiFeature()
    {
        if (codiFeature is { } on) return on;
        try { on = Ms2.Ui.LobbyFeatures.IsEnabledByName(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir), Ms2.Ui.FeatureTable.Names[0x8e - 1]); }
        catch (Exception e) { GD.Print($"live: feature table unavailable ({e.Message})"); on = false; }
        codiFeature = on;
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
    /// <summary>The fishing book's catch records (the Fishing packets, 0xC4, modes 7 and 8; scenestategame.cpp, FUN_140cd9840);
    /// session-lived, so a field change keeps them.</summary>
    public readonly Ms2.Ui.FishingBookData FishingBook = new();
    /// <summary>The breakable objects' and liftable cubes' server states (RecvOp Breakable 0x4F, Liftable 0x73).</summary>
    public readonly Ms2.Ui.BreakableObjects Breakables = new();
    public readonly Ms2.Ui.LiftableObjects Liftables = new();
    /// <summary>The field's liftable target boxes (WorldService m_540).</summary>
    public readonly Ms2.Ui.LiftableTargetBoxes LiftTargets = new();
    /// <summary>The field's vibrate objects and their running vibrators (WorldService m_5e8 and m_468; RecvOp Vibrate 0x75,
    /// HideVibrate 0x76, ShowVibrate 0x77).</summary>
    public readonly Ms2.Ui.FieldVibrates Vibrates = new();
    /// <summary>The own pc's cube mode and craft (PC::m_nCubeModeFFC, m_pCubeCraft).</summary>
    public readonly Ms2.Ui.CubeCraftState CubeCraft = new();
    CubeCraftOwner cubeCraftOwner;
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
    /// <summary>A damage number the field's skill damage records show (RemoteLayer, UIDamageText's show test passed): the
    /// number and the npc it is on (null when the target is not an npc).</summary>
    public event Action<Ms2.Ui.Hud.DamageNumber, FieldNpcs.Placed, Ui.DamageTarget> DamageNumbered;
    /// <summary>OnExpUp's number for the own PC (scenestategame.cpp:4152): mode 0, a gain, no npc of the packet's npc id
    /// (an npc of the id keeps the gain: <see cref="NpcExpShown"/>); the gain and its exp type.</summary>
    public event Action<long, int> ExpGained;

    /// <summary>OnExpUp mode 0 (scenestategame.cpp:4180): the packet and whether the rest exp was above 0 before it, for the
    /// exp chat lines (FUN_1413606c0).</summary>
    public event Action<ExpUp, bool> ExpLogged;

    /// <summary>OnLevelUp of the own PC (scenestategame.cpp:9545-9567): the old level and the packet's, raised for each own
    /// LevelUp packet (a rise or not) before NotifyChangeMyLevel.</summary>
    public event Action<int, int> OwnLevelChanged;

    /// <summary>OnPrestige kind 2 of the own PC (scenestategame.cpp:8851-8862): the old adventure level and the packet's.</summary>
    public event Action<int, int> OwnAdventureLevelChanged;

    /// <summary>DAT_143ab2900, OnAdventurerBar's count (scenestategame.cpp:4082).</summary>
    public int AdventurerBarCount { get; private set; }
    /// <summary>OnAdventurerBar: the game menu's FUN_141939620 follows.</summary>
    public event Action AdventurerBarChanged;

    /// <summary>The commonResource.xml keys of CommonResource +0x328 and +0x5b0 (commonresource.h).</summary>
    public const string LevelUpEffectKey = "EFFECT_LEVEL_UP", AdventureLevelUpEffectKey = "EFFECT_ADVENTURE_LEVEL_UP";

    /// <summary>
    /// The effect query of OnLevelUp (scenestategame.cpp:9520-9540) and OnPrestige kind 2 (:8838-8849): a CommonResource
    /// effect whose actor is the PC's ridee while it rides, else the PC, with the PC's object kind (1 own, 3 another).
    /// </summary>
    void PlayPcEffect(int objectId, bool own, string key)
    {
        string path;
        try { path = Ms2.Ui.GameTables.Shared(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir), GameOptions.StringLanguage).CommonResource(key); }
        catch (Exception ex) { GD.PushWarning($"common effect {key}: {ex.Message}"); return; }
        if (string.IsNullOrEmpty(path)) return;
        if (!own)
        {
            bool played = remote?.PlayActorEffect(objectId, path) ?? false;
            if (LiveCheck) GD.Print($"live: effect {key} on object {objectId}: {(played ? "played" : "no actor")}");
            return;
        }
        if (player == null || !IsInstanceValid(player)) return;
        var actor = player.Mount?.Visual ?? player.Visual;
        if (actor == null) return;
        if (LiveCheck) GD.Print($"live: effect {key} on the own {(player.Mount?.Visual != null ? "ride" : "PC")}: {path}");
        OwnEffectsPlayed?.Invoke(path);
        ReleaseKeptEffect(SpawnKeptEffect(path, actor, Ms2.Audio.SoundMusicEffectState.CasterMyPc));
    }
    /// <summary>An npc's exp gain shown when its death sequence ended (<see cref="NpcLayer.ExpShown"/>): the npc and the exp.</summary>
    public event Action<FieldNpcs.Placed, long> NpcExpShown;
    /// <summary>A live npc made (FieldAddNpc) and removed (FieldRemoveNpc, its object id), the quest navigator's notifies.</summary>
    public event Action<FieldNpcs.Placed> NpcAdded;
    public event Action<int> NpcRemoved;
    /// <summary>The live field's npcs, dead or not; none before the field is entered.</summary>
    public IEnumerable<FieldNpcs.Placed> LiveNpcs => remote?.Npcs ?? Enumerable.Empty<FieldNpcs.Placed>();
    /// <summary>The live npc of the object id is dead (CNpc IsDead); false when there is none.</summary>
    public bool NpcDead(int objectId) => remote?.NpcDead(objectId) ?? false;
    public (int NpcId, long Hp, long MaxHp)? NpcHp(int objectId) => remote?.NpcHp(objectId);
    /// <summary>A drop reached a player who picked it (CDropItem::SetState(3), dropitem.cpp:671-695): the player (null for the
    /// own PC), then the meso, or the count and icon path of a type 3 item; nothing for another picker or item.</summary>
    public event Action<Ui.DamageTarget, long> MesoGained;
    public event Action<Ui.DamageTarget, int, string> ItemGained;
    /// <summary>A FieldAddItem read, and whether the field made a drop of it (InitDrop's tests: the owner, the item data, the model).</summary>
    public event Action<FieldAddItem, bool> DropAdded;
    /// <summary>A notice of the drop layer's pickup request (0x43 the bag is full, 0x45 the drop is out of reach).</summary>
    public event Action<int> DropNotice;
    /// <summary>A system sound of the drop layer's pickup request (the key, and whether a sound played).</summary>
    public event Action<string, bool> DropSystemSound;
    /// <summary>Auto loot asked for a drop (its object id).</summary>
    public event Action<int> DropAutoRequest;
    /// <summary>The server refused a pickup: the drop's object id and the answer's mode.</summary>
    public event Action<int, int> DropRefused;
    /// <summary>The field's drop of the object id: where it is, its state and its effects (<see cref="DropLayer.Shown"/>).</summary>
    public (Vector3 At, int State, bool Glow, bool BeEffect)? DropShown(int objectId) => drops?.Shown(objectId);
    /// <summary>The constants of the field's drop of the object id, and whether they are the SP and EP orbs' (<see cref="DropLayer.SetOf"/>).</summary>
    public (Ms2.Formats.World.DropConstants Set, bool SpepBall)? DropSet(int objectId) => drops?.SetOf(objectId);
    /// <summary>An OnFieldPickupItem read for a drop of the field (picked or refused).</summary>
    public event Action<FieldPickupItem> DropPickupRead;
    /// <summary>The region skill objects' ids of the field (OnRegionSkill 0x4C modes 0 and 1): mode 5's damage needs one
    /// (OnRegionSkillDamage drops the packet when GetRegionSkillObject finds none).</summary>
    readonly Dictionary<int, (int Skill, short Level)> regionSkills = new();
    /// <summary>The field's OnRegionSkill messages in order, for a <see cref="World.RegionSkills"/> node made after them; cleared
    /// with the ids on a new field.</summary>
    public readonly List<Ms2.Net.Packets.RegionSkillMessage> RegionSkillLog = new();
    /// <summary>An OnRegionSkill message (0x4C modes 0-2) of the current field.</summary>
    public event Action<Ms2.Net.Packets.RegionSkillMessage> RegionSkillReceived;
    /// <summary>CMainSystem::GetServerTick (mainsystem.cpp:312); 0 without a session.</summary>
    public int ServerTick => game?.Session.ServerTick ?? 0;
    /// <summary>UserEnv mode 8 or 9 replaced the gathering counts or the received rewards.</summary>
    public event Action MasteryEnvChanged;

    /// <summary>The local player's record for the character window: the own FieldAddUser, Stat, AttributePoint, UserEnv,
    /// HomeCommand and own AddPlayer proxy; live only.</summary>
    public MyCharacter Me { get; private set; } = new();
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

    /// <summary>OnAutoRevive (scenestategame.cpp:4332): the milliseconds until the automatic revive; the HUD opens
    /// UIAutoReviveCountDownDialog (0x4ea2) and arms it.</summary>
    public Action<int> AutoReviveStarted;
    /// <summary>The mail dialogs' sends (Mail 0x2D).</summary>
    public void SendMail(Ms2.Net.Wire.IOutPacket packet) => Send(packet);

    /// <summary>The revive dialog's sends (Revival 0x1F, RequestMoveField): fire and forget like the client's.</summary>
    public void SendRevival(Ms2.Net.Wire.IOutPacket packet) => Send(packet);
    /// <summary>The field breakables' tag pass sends (Breakable 0x27, WorldService::FUN_141bc3560).</summary>
    public void SendBreakable(Ms2.Net.Packets.BreakableRequest packet) => Send(packet);
    /// <summary>The vibrate objects' tag pass sends (Vibrate 0x26, WorldService::FUN_141bc3560).</summary>
    public void SendVibrate(Ms2.Net.Packets.VibrateRequest packet) => Send(packet);

    /// <summary>
    /// OnVibrate's cast record (CSkillRecord::Init, then GetBrokenOffence, skillrecord.cpp:382): the skill's level row, else
    /// null (Init fails and OnVibrate does nothing); the motion's attack's brokenOffence, 0 when the level has no such attack.
    /// </summary>
    int? VibrateOffence(Ms2.Net.Packets.Vibrate v)
    {
        if (v.Mode is not (0 or 1)) return 0;
        var level = SkillRows?.Invoke((uint)v.SkillId)?.GetLevelData(v.Level);
        if (level == null) return null;
        if (v.Motion >= level.Motions.Count) return 0;
        var attacks = level.Motions[v.Motion].Attacks;
        return v.AttackPoint < attacks.Count ? attacks[v.AttackPoint].BrokenOffence : 0;
    }
    /// <summary>CBuddyManager: the friends, requests and blocks from the Buddy packets (0x61, CSceneStateGame::OnBuddy); live
    /// only. Its sends (Buddy 0x30) go out while the game connection is up.</summary>
    public Ms2.Ui.BuddyBook Buddies => buddies ??= new Ms2.Ui.BuddyBook { Send = SendBuddy };
    Ms2.Ui.BuddyBook buddies;
    /// <summary>CGuildManager: the guild id, this character's join requests and the search results from the Guild packets
    /// (0x8A); live only. Its sends (Guild 0x4B) go out while the game connection is up.</summary>
    public Ms2.Ui.GuildBook Guilds => guilds ??= new Ms2.Ui.GuildBook { Send = SendGuild };
    Ms2.Ui.GuildBook guilds;
    /// <summary>OnMinimapPing @0x14173de10 (scenestategame.cpp:6806-6815): the ping for the minimap dialog's AddPing (set by the HUD).</summary>
    public event Action<MinimapPingMessage> MinimapPinged;
    public event Action<MassiveEventMessage> MassiveEventReceived;
    /// <summary>WorldService's field timer (m_800-m_808), which the Trigger 0xf kinds set (triggerservice.cpp:527).</summary>
    public readonly Ms2.Ui.Hud.WorldTimer FieldTimer = new();
    /// <summary>Trigger 0xf kind 1 (triggerservice.cpp:531): the trigger timer dialog (0x4e95) opens or closes.</summary>
    public event Action<TriggerDialogCommand> TriggerTimerReceived;
    public event Action<RoomTimerMessage> RoomTimerReceived;
    /// <summary>Trigger 0x13 (OnSidePopupTalk, triggerservice.cpp:683): a side popup talk for UISidePopupTalk (0x4f57).</summary>
    public event Action<TriggerSidePopup> SidePopupReceived;
    /// <summary>The NpcNotice modes the HUD shows (0 the timed system line, 3 the side popup talk).</summary>
    public event Action<NpcNotice> NpcNoticeReceived;
    public event Action<TriggerDuelHpBar> DuelHpBarReceived;      // Trigger type 0x15 (OnNpcDuelHpBar), the HUD's when it listens
    /// <summary>The minimap dialog's ping send (MinimapPing 0xB1).</summary>
    public void SendMinimapPing(Ms2.Net.Wire.IOutPacket packet)
    {
        if (game != null) Send(packet);
    }
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
    /// <summary>UITalkManager::GetTalking() != 0: a talk with an npc is open (set by the field's talk).</summary>
    public Func<bool> IsTalking = () => false;
    /// <summary>A dialog opened from the talk ends it: UITalkManager::SendNpcTalkMode0 (NpcTalk 0) and the talk's state clears.</summary>
    public void EndTalk() { SendTalk(new NpcTalkEndRequest()); TalkEnded?.Invoke(); }
    public event Action TalkEnded;
    /// <summary>The Shop packets (0x51, UIShopDialog::OnShopPacket) for the npc shop; live only.</summary>
    public event Action<ShopPacket> ShopReceived;
    /// <summary>The system shop open result (0xA1, CSceneStateGame::OnSystemShop); live only.</summary>
    public event Action<SystemShopPacket> SystemShopReceived;
    /// <summary>A merat market buy answer (0x6C modes 0x1e / 0x1f, CMeratMarketMan::OnBuySucceeded / OnBuyFailed); live only.</summary>
    public event Action<MeretMarketPacket> MeretMarketReceived;
    /// <summary>The game connection closed: a run waiting for an answer cannot get it.</summary>
    public event Action GameLinkLost;
    /// <summary>SendMeretMarketMode30 (0x39, sub-mode 0x1e): one queued merat market purchase.</summary>
    public bool SendMeretMarketBuy(MeretMarketBuy buy) { if (game == null) return false; Send(new MeretMarketBuyRequest(buy)); return true; }
    /// <summary>An item merge packet (0x74, CSceneStateGame::OnItemFusion); the item body of modes 5/6 is left in the reader.</summary>
    public event Action<ItemMergePacket, Maple2.PacketLib.Tools.ByteReader> ItemMergeReceived;
    /// <summary>An item dismantle packet (0x96, CItemBreakManager::OnItemDismantlePacket).</summary>
    public event Action<ItemDismantlePacket> ItemDismantleReceived;
    /// <summary>The merge dialogs' sends (ItemMerge 0x3F, UIItemMergeOptionDialog / UIItemMergeRevertDialog); live only.</summary>
    public void SendItemMerge(Ms2.Net.Wire.IOutPacket packet) { if (game != null) Send(packet); }
    /// <summary>RequestItemBox (0x1A, UIItemMultiOpenBoxDialog::vfunc_15 @0x140c3f8a0).</summary>
    public void SendItemBox(Ms2.Net.Packets.ItemBoxRequest request) { if (game != null) Send(request); }
    /// <summary>SendOp ItemDismantle (0x5A): the CItemBreakManager sends (itembreakmanager.cpp:321-451).</summary>
    public void SendItemDismantle(Ms2.Net.Wire.IOutPacket packet) { if (game != null) Send(packet); }
    /// <summary>The npc shop's sends (Shop 0x28).</summary>
    public void SendShop(Ms2.Net.Wire.IOutPacket packet) { if (game != null) Send(packet); }
    public void SendBeauty(Ms2.Net.Wire.IOutPacket packet) { if (game != null) Send(packet); }
    /// <summary>CInstrumentManager's currently playing score uid (FUN_140c8dab0, petmanager.cpp:141; STILL-OPEN:
    /// instrument play packets are not ported yet, so 0).</summary>
    public long PlayingScoreUid { get; private set; }
    /// <summary>CNpc::OpenTalkUi / CloseTalkUi (unit_14108e1e0_npcactorandvibrate.cpp:1350): talk UI dialog id (0x4e3b shop).</summary>
    public event Action<int> TalkUiOpen, TalkUiClose;
    public void RaiseTalkUiOpen(int dialogId) => TalkUiOpen?.Invoke(dialogId);
    public void RaiseTalkUiClose(int dialogId) => TalkUiClose?.Invoke(dialogId);
    /// <summary>NpcTalk mode 3 sub 4 (FUN_141b1eb90): open the named dialog with its argument.</summary>
    public event Action<string, string> TalkDialogOpen;
    /// <summary>The object id of the npc the open talk is with (0 when none): the enchant dialog's step reports carry it.</summary>
    public int TalkNpcId;
    public void RaiseTalkDialogOpen(string dialog, string argument) => TalkDialogOpen?.Invoke(dialog, argument);
    /// <summary>The party window's and the popup menu's sends (Party 0x2C, RoomDungeon, DungeonReward, Club).</summary>
    public void SendParty(Ms2.Net.Wire.IOutPacket packet) => Send(packet);

    /// <summary>The quick slot edits (KeyTable 0x3A, modes 3 and 5).</summary>
    public void SendKeyTable(Ms2.Net.Wire.IOutPacket packet) => Send(packet);

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
    /// <summary>The talk key's nearest npc search over the live npcs (<see cref="RemoteLayer.NearestTalkNpc"/>), no allocation.</summary>
    public FieldNpcs.Placed NearestTalkNpc(int friendly, Vector3 me, Vector3 face, float range, bool frontOnly) =>
        remote?.NearestTalkNpc(friendly, me, face, range, frontOnly);
    /// <summary>The field's other players (the remote layer's), none before the field is built.</summary>
    public IEnumerable<RemoteLayer.Pc> FieldPcs => remote?.Pcs ?? Enumerable.Empty<RemoteLayer.Pc>();
    /// <summary>The other pcs the field shows now, as the minimap's proxies read them.</summary>
    public IEnumerable<RemoteLayer.ShownPc> ShownPcs => remote?.ShownPcs ?? Enumerable.Empty<RemoteLayer.ShownPc>();
    /// <summary>Another pc entered the field (UIMinimapDialog::vfunc_3) or left it (vfunc_4, by object id); a new field's
    /// layer starts empty.</summary>
    public event Action<RemoteLayer.ShownPc> FieldPcShown;
    public event Action<int> FieldPcLeft;
    /// <summary>The remote layer's tomb tag pass (the own magics register their areas into it), null before the field is built.</summary>
    public Ms2.Ui.Hud.TombTagPass TombTags => remote?.TombTags;
    /// <summary>The own FieldAddUser object id, -1 before it.</summary>
    public int SelfObjectId => selfObject;

    /// <summary>DAT_143ab2820, the account id of the login.</summary>
    public long OwnAccountId => accountId;

    /// <summary>ItemUse mode 5, CSceneStateGame::OnActiveCodiTab (scenestategame.cpp:8662, 0x14173c2d0): the player's layer
    /// period (SetLayerPeriod); for the own player the completion box (<see cref="CodiTab.CompleteKey"/>).
    /// STILL-OPEN: the own player's look rebuild and the NotifyActiveMyCodiTab post (scenestategame.cpp:8686), which the equipment
    /// window's avatar follows (UIMyInfoDialog::OnActiveMyCodiTabNotify, uimyinfodialog.cpp:1030); only the stored period and the box here.</summary>
    void ActiveCodiTab(CodiTab t)
    {
        if (t.ActorId != selfObject) { remote?.ActiveCodiTab(t); return; }
        Me.CodiStart = t.Start; Me.CodiEnd = t.End;
        string key = t.CompleteKey(out int days);
        if (key == null) return;
        string text = Ms2.Ui.Hud.MessageBoxData.CommonByKey(key) ?? "";
        if (key == CodiTab.PeriodKey) text = Ms2.Quest.ClientFormat.Format(text, days);
        Ms2.Ui.Hud.MessageBoxQueue.ShowMessageBox(text, 0, null, "");
    }
    /// <summary>The other player whose PC body the ray hit (<see cref="RemoteLayer.PopupTargetOf"/>); null without the field.</summary>
    public (int ObjectId, Ms2.Ui.Hud.PopupMenuTarget Target)? PopupTargetOf(ulong colliderId, Func<long, string, string> profileImage) =>
        remote?.PopupTargetOf(colliderId, profileImage);

    /// <summary>The other player of a character id as the couple cast reads it (<see cref="RemoteLayer.CouplePeerOf"/>).</summary>
    public Ms2.Ui.Skills.CouplePeer CouplePeerOf(long characterId) => remote?.CouplePeerOf(characterId);

    /// <summary>
    /// WorldService::FUN_141bd1530 (worldservice_1.cpp:6210): the field's user object with this character id, here whether
    /// the field's users (FieldAddUser, kept by the tracker) hold one. False before the field is built.
    /// </summary>
    public bool UserInField(long characterId) => remote?.Tracker.Snapshot().Any(a => a.Add?.User?.CharacterId == characterId) ?? false;
    /// <summary>FindProxyObjectByOwner (worldservice): the field's other user with this character id (FieldAddUser); null = none.</summary>
    public UserBlock OtherUserInField(long characterId) => remote?.Tracker.Snapshot().FirstOrDefault(a => a.Add?.User?.CharacterId == characterId)?.Add.User;

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
        WorldIdleTimer.Current = Instance.IdleTimer;   // WorldService::GetInstance() for the callers outside the session
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
        StopCombatEffects();                  // nor a time scale change
        ResetReconnect();
        game?.Dispose(); login?.Dispose();
        if (Instance == this) Instance = null;
        if (WorldIdleTimer.Current == idleTimer) WorldIdleTimer.Current = null;
    }

    /// <summary>The server's LoginResult when the last login was refused (null when it failed otherwise or succeeded).</summary>
    public LoginResult LastLoginRefusal { get; private set; }

    /// <summary>
    /// Login and the character list; the list replaces the stub list. The id and password come from the client's login
    /// dialog (UILoginDialog::SendLoginRequest, uilogindialog.cpp:204-246) unless MS2_LIVE_ACCOUNT overrides them. False
    /// when the server refuses.
    /// </summary>
    public Task<bool> LoginAsync(string id = null, string password = null) => LoginAsync(id, password, false);

    /// <summary>
    /// The character list's server list button on the KMS client's platform (vfunc_1 4): UICharacterListDialog::OnEndPopdown
    /// with m_1d4 0x620001a sends RequestServerList on the login connection and goes to the lobby's server scene
    /// (uicharacterlistdialog.cpp:1644-1648, CSceneStateMulti::ExitToServerList, scenestatemulti.cpp:529-535). That scene opens
    /// the server list dialog (UISceneLobbyServer::OnLoadInit, uiscenelobbyselect.cpp:516-520), whose OnLoadComplete on this
    /// platform connects at once (uiserverlistdialog.cpp:346-371): the saved server and channel picked from the list it keeps
    /// (BuildServerList, FUN_141ad2560), ConnectTo closing the old connection first (networkservice.cpp:465-471), then the
    /// type 2 login and the character list. False when that login fails; the reason is in the same fields as a first login's.
    /// </summary>
    public async Task<bool> ServerListAgainAsync()
    {
        // busy before the first await, so no other request starts on this connection; and the old connection's close from
        // here on is the move: the client's ConnectTo closes it itself right after (networkservice.cpp:465-471), so a reset
        // by the server never reaches window message 0x465 on the server scene
        Busy = true;
        leavingLogin = true;
        var old = login;
        if (old != null && !old.IsClosed && old.Profile.Has(Ms2.Net.Outbound.RequestServerList))
        {
            try { await old.SendAsync(new RequestServerList()); }
            catch (Exception ex) { GD.PushWarning("live: RequestServerList send failed: " + ex.Message); }
        }
        if (LiveCheck) GD.Print("live: RequestServerList sent; the server list connects again");
        return await LoginAsync(null, null, true);
    }

    /// <summary>
    /// The AlreadyLogin box's OK while the lobby is up (UIMessageBox::HandleAlreadyReconnectButton, uimessagebox.cpp:1572-1577):
    /// ConnectToConfiguredServer, the configured login address; its RequestLogin gets the server list dialog's type 2 login with
    /// the kept channel, since the lobby scene is up (vfunc_79 case 9 -> SendLoginRequest, uiserverlistdialog.cpp:578-602).
    /// </summary>
    public Task<bool> ConnectConfiguredAgainAsync() => LoginAsync(null, null, true, true);

    /// <summary>The last ServerList and the login dialog's id and password (DAT_143ab29d8 / DAT_143ab2a28), kept for the server list's next connect.</summary>
    ServerList keptList;
    string keptUser, keptPassword;

    async Task<bool> LoginAsync(string id, string password, bool again, bool configured = false)
    {
        Busy = true;
        LastLoginRefusal = null;
        LastConnectError = 0;
        LastServerListEmpty = false;
        try
        {
            var args = LaunchArgs();
            if (args.Problem != null) { WarnArgs(args); return false; }
            int port;
            if (args.HasAddress) { host = args.Host; port = args.Port; }
            else { host = launchHost; port = launchPort; }
            if (string.IsNullOrEmpty(host) || port <= 0) { GD.PushWarning("live: no login server"); return false; }
            string user = id, pass = password ?? "";
            string envAccount = System.Environment.GetEnvironmentVariable("MS2_LIVE_ACCOUNT");
            if (again)
            {
                if (keptList == null) { GD.PushWarning("live: no server list kept to connect again"); return false; }
                user = keptUser; pass = keptPassword;
            }
            else if (!string.IsNullOrEmpty(envAccount))
            {
                var up = envAccount.Split(':', 2);
                user = up[0];
                pass = up.Length > 1 ? up[1] : "";
            }
            // no empty-id check: the client sends the request as typed (SendLoginRequest, uilogindialog.cpp:204-246) and the server answers
            user ??= "";
            login?.Dispose();
            // the real client appends its region code to the id
            // GMS2: the launcher ticket (--ticket) goes in the token field (the packet drops id and password); only a GMS2 profile uses it
            string ticket = args.HasAddress ? args.Ticket : null;   // the ticket goes only to the server it was given with
            string machinePath = ticket != null ? ProjectSettings.GlobalizePath("user://machine-id") : null;
            // UILoginDialog::SendLoginRequest (uilogindialog.cpp:204-246): the login dialog's request is type 1 on its own connection; the server
            // answers ServerList and drops it (CSceneStateLogin::OnPacket, scenestatelogin.cpp:117-132)
            ServerList list;
            if (again)
            {
                // the dialog's own list, with the picked server's channels as DynamicChannel last set them (SetChannels, uiserverlistdialog.cpp:681-697)
                list = Ms2.Ui.ServerListPick.WithChannels(keptList, pickedServer, channelTabs);
            }
            else
            {
                ServerListAnswer listed;
                using (var first = new TcpTransport())
                    listed = await new LoginClient(first) { Ticket = ticket, MachineIdPath = machinePath }.ServerListAsync(host, port, user + "KR", pass, machine);
                if (listed.Refusal is { } refused) { GD.PushWarning($"live: login refused, state {refused.State} {refused.Message}"); LastLoginRefusal = refused; return false; }
                list = keptList = listed.List;
                keptUser = user; keptPassword = pass;
            }
            // UIServerListDialog: ReceiveServerList, then BuildServerList / OnSelectServer from the options' saved server and
            // channel (m_strServer in the ctor, uiserverlistdialog.cpp:249, or OnReset, :378-383; atoi(m_strChannel) in OnLoadComplete, :350)
            var saved = GameOptions.Current.ConnectOption;
            var choice = Ms2.Ui.ServerListPick.Pick(list, saved.WorldName, saved.ChannelNumber);
            if (choice == null)
            {
                // a list without servers: box 0x20e with 0, whose OK runs the dialog's failure state (scenestatelogin.cpp:121-129)
                GD.PushWarning("live: the server list has no server");
                LastServerListEmpty = true;
                return false;
            }
            Session.ServerId = choice.ServerId;
            Session.Channel = choice.Channel;
            pickedServer = choice.Server.Name;
            channelTabs = choice.Channels.ToArray();   // the picked server's m_channels; DynamicChannel kind 1 replaces them (SetChannels)
            // ConnectToServer (uiserverlistdialog.cpp:502-517): one of the server's addresses at random; none, no connect
            var addresses = choice.Server.Addresses;
            if (addresses.Length == 0) { GD.PushWarning("live: the picked server has no address"); return false; }
            var (serverHost, serverPort) = addresses[System.Random.Shared.Next(addresses.Length)];
            if (configured) (serverHost, serverPort) = (host, (ushort)port);   // ConnectToConfiguredServer: the login address itself
            if (LiveCheck) GD.Print($"live: server list {list.Servers.Count} server(s), picked {choice.ServerId} channel {choice.Channel} of {choice.Channels.Count}, login at {GameClient.EndpointText(serverHost, serverPort)}");
            UseLogin(new TcpTransport());
            var o = await new LoginClient(login) { Ticket = ticket, MachineIdPath = machinePath, Channel = (short)choice.Channel }.LoginAsync(serverHost, serverPort, user + "KR", pass, machine);
            if (!o.Success) { GD.PushWarning($"live: login refused, state {o.Result.State} {o.Result.Message}"); LastLoginRefusal = o.Result; return false; }
            accountId = o.Result.AccountId;
            try
            {
                // unknown data (no folder, unreadable) gives no warning, and never fails a good login
                string dataLocale = !string.IsNullOrEmpty(Settings.DataDir) && System.IO.Directory.Exists(Settings.DataDir) ? Ms2.Ui.LobbyFeatures.LocaleOf(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir)) : null;
                if (Ms2.Ui.LobbyFeatures.MismatchWarning(login.Profile.IsGms2, dataLocale) is { } mismatch) GD.PushWarning("live: " + mismatch);
            }
            catch (Exception ex) { GD.Print("live: data/server check skipped: " + ex.Message); }
            if (o.Endpoint is { } ugc)
            {
                // FUN_1410268a0 / CSoapClient::FUN_14176fcf0: the download host and the upload base
                ProfileImageService.Images.Host = ugc.Host;
                ProfileImageService.Images.UploadBase = ugc.UploadBase;
                GD.Print($"live: web host {ugc.Host}, uploads {(ugc.UploadBase.Length > 0 ? "on" : "off")}");
            }
            Session.AccountId = user;
            Session.Characters = o.Characters.ToList();
            if (o.CharacterLimit > 0) CharacterLimit = o.CharacterLimit;
            if (o.CharacterRows > 0) CharacterRows = o.CharacterRows;
            CheckNames = o.CheckNames;
            GD.Print($"live: logged in, {o.Characters.Count} character(s): {string.Join(", ", o.Characters.Select(c => c.User.Name))}");
            return true;
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            // the connect itself failed: window message 0x467 (CClientPlatformBase::vfunc_31 from the connect event's error)
            LastConnectError = (int)ex.SocketErrorCode;
            GD.PushWarning($"live: login connect failed, socket error {LastConnectError}: {ex.Message}");
            return false;
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

    /// <summary>
    /// The forced rename (CharacterManagement 5) of <paramref name="characterId"/> and the login server's answer: CharacterList 7
    /// with its code, or the list sent again, which becomes the session's (with its EndList flag). Null when it was not
    /// answered.
    /// </summary>
    public async Task<RenameAnswer> RenameAsync(long characterId, string name)
    {
        Busy = true;
        try
        {
            var a = await new LoginClient(login).RenameAsync(characterId, name);
            if (a.List is { } o)
            {
                Session.Characters = o.Characters.ToList();
                CheckNames = o.CheckNames;
                GD.Print($"live: rename of {characterId} to '{name}': the list again, {o.Characters.Count} character(s): {string.Join(", ", o.Characters.Select(c => c.User.Name))}");
            }
            else
            {
                GD.Print($"live: rename of {characterId} to '{name}': CharacterList 7, code {a.NameChanged.Error}");
                if (a.NameChanged.Error == 1 && Session.Characters?.Find(c => c.User.CharacterId == characterId) is { } e) e.User.Name = a.NameChanged.Name;
            }
            return a;
        }
        catch (Exception ex) { GD.PushWarning("live: rename failed: " + ex.Message); return null; }
        finally { Busy = false; }
    }

    /// <summary>
    /// A character list delete request (CharacterManagement 2 BeginDelete, 3 CancelDelete, 4 ConfirmDelete) and the server's
    /// answer, the CharacterList DeleteEntry, BeginDelete or CancelDelete for that character. The session's list follows it:
    /// DeleteEntry with error 0 drops the entry, BeginDelete with error 0 sets its delete time, CancelDelete with error 0
    /// clears it. A refusal changes nothing here; the dialog shows it. Returns null when the request was not answered.
    /// </summary>
    public async Task<CharacterListMessage> ManageAsync(CharacterIdCommand command, CharacterEntry entry)
    {
        Busy = true;
        try
        {
            long id = entry.User.CharacterId;
            var m = await new LoginClient(login).ManageAsync(command, id);
            if (m.Error != 0)
            {
                GD.Print($"live: {command} of {entry.User.Name} refused: {m.Command} error {m.Error} (time {m.DeleteTime})");
                return m;
            }
            switch (m.Command)
            {
                case CharacterListCommand.DeleteEntry:
                    Session.Characters.RemoveAll(c => c.User.CharacterId == id);
                    if (Session.SelectedIndex >= Session.Characters.Count) Session.SelectedIndex = Math.Max(0, Session.Characters.Count - 1);
                    GD.Print($"live: character deleted: {entry.User.Name} id {id}");
                    break;
                case CharacterListCommand.BeginDelete:
                    entry.DeleteTime = m.DeleteTime;
                    GD.Print($"live: delete pending for {entry.User.Name}, until unix {m.DeleteTime}");
                    break;
                case CharacterListCommand.CancelDelete:
                    entry.DeleteTime = 0;
                    GD.Print($"live: delete of {entry.User.Name} cancelled");
                    break;
            }
            return m;
        }
        catch (Exception ex) { GD.PushWarning($"live: {command} failed: " + ex.Message); return null; }
        finally { Busy = false; }
    }

    static string Describe(ItemMessage m) => m switch {
        ItemMessage.LoadTab t => $"LoadTab {t.Tab} x{t.Items.Count} [{string.Join(", ", t.Items.Select(i => $"{i.Id}@{i.InventorySlot}x{i.Amount}"))}]",
        ItemMessage.Load l => $"Load x{l.Items.Count} [{string.Join(", ", l.Items.Select(i => $"{i.Id}@{i.InventorySlot}x{i.Amount}"))}]",
        _ => m.GetType().Name };

    void ApplyItems(Ms2.Net.Wire.RawPacket p) {
        if (System.Environment.GetEnvironmentVariable("MS2_LIVE_DUMP") == "1") GD.Print("live: item raw " + Convert.ToHexString(p.Body));
        try {
            var r = p.Reader(); var m = ItemMessage.Read(r, Items.Get, game.Profile);
            bool check = LiveCheck || LogPackets;      // the skipped body's check runs in live checks only
            if (check && m is ItemMessage.Remove rm) lastRemovedItem = Items.Get(rm.Uid);
            Items.Apply(m);
            if (m is ItemMessage.Update { Item: null, Uid: not 0 } u && r.Available != 0) { if (check) CheckSkippedUpdate(r, u.Uid); }
            else if (r.Available != 0) GD.PushWarning($"live: item {Describe(m)} !! {r.Available} bytes left");
            else if (LogPackets) GD.Print("live: item " + Describe(m));
        }
        catch (Exception e) { GD.PushWarning($"live: item packet: {e.Message}"); }
    }

    /// <summary>The bag item the last Remove took out, for <see cref="CheckSkippedUpdate"/> (live checks only; cleared once used).</summary>
    EquipItem lastRemovedItem;

    /// <summary>
    /// OnItemInventory mode 0xe (scenestategame.cpp:2960) reads the body only for an uid that Unk_Inventory::FindItemByUid
    /// (inventory.cpp:561) finds in the bag's item map, which the Remove packet (RemoveItem, inventory.cpp:532) took the item
    /// out of. An equip removes the item from the bag and then sends its bind update before the item is worn, so the client,
    /// and the port, skip that body. In live checks this reads the skipped body against <see cref="ItemReader.SkippedUpdateItem"/>
    /// as a check of the item layout only; nothing is applied.
    /// </summary>
    void CheckSkippedUpdate(Maple2.PacketLib.Tools.ByteReader r, long uid) {
        int skipped = r.Available;
        var known = ItemReader.SkippedUpdateItem(lastRemovedItem, uid, Me);
        lastRemovedItem = null;
        if (known == null) { GD.Print($"live: item Update of {uid}: not in the bag, {skipped} bytes skipped as the client does (no item to check them with)"); return; }
        try { ItemReader.ReadSkippedBody(r, known, game.Profile); }
        catch (Exception e) { GD.PushWarning($"live: item Update of {uid} ({known.Id}): the skipped body does not read: {e.Message}"); return; }
        if (r.Available != 0) GD.PushWarning($"live: item Update of {uid} ({known.Id}): the skipped body of {skipped} bytes reads with {r.Available} bytes left");
        else GD.Print($"live: item Update of {uid} ({known.Id}): not in the bag, {skipped} bytes skipped as the client does; they read as the item's body with 0 bytes left");
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
    public static int? WornLayer(int itemId) {
        try { return (itemData ??= new Ms2.Ui.Items.ItemDataTable(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir))).WornLayer(itemId); }
        catch (Exception e) { GD.Print($"live: item data unavailable ({e.Message})"); return null; }
    }

    /// <summary>CreateCube's item checks (cubemapinformationservice_3.cpp:2614-2620): the item's subtype and its install
    /// object's data (GetObjectData(install.objCode)); null without item data, install object or object file.</summary>
    internal static (int Subtype, Ms2.Formats.World.ObjectData Object)? InstallObjectOf(int itemId) {
        var d = ItemDataOf(itemId);
        if (d == null || d.InstallObjCode == 0) return null;
        try {
            var o = Ms2.Ui.GameTables.Shared(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir), GameOptions.StringLanguage).ObjectDataOf(d.InstallObjCode);
            return o == null ? null : (d.Subtype, o);
        }
        catch (Exception e) { GD.Print($"live: object data unavailable ({e.Message})"); return null; }
    }

    /// <summary>FUN_14053f870: the item's data; null when unknown or the item data is missing.</summary>
    internal static Ms2.Ui.Items.ItemData ItemDataOf(int itemId) {
        try { return (itemData ??= new Ms2.Ui.Items.ItemDataTable(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir))).Get(itemId); }
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

    /// <summary>
    /// Select the character and migrate to the game server, up to RequestFieldEnter. The chosen character's map becomes the
    /// server's. How it ended (<see cref="EnterResult"/>): a refused start is the LoginToGame answer, a failed connect the
    /// socket error of the game server's connect, a lost login connection its socket error (0 for a clean close).
    /// </summary>
    public async Task<EnterResult> EnterAsync(CharacterEntry chosen)
    {
        Busy = true;
        regionSkills.Clear(); RegionSkillLog.Clear(); ClearFieldEffects(); FieldProperties.Clear();   // a new field: no region skill object or field property survives it
        var from = login;
        try
        {
            if (from == null || from.IsClosed) return Dropped(from, null);
            LoginToGame go;
            short asked = BeginEnterGame();
            gameStartPending = true;   // WorldService::FUN_141bca1e0, the same place in EnterGame (uiserverlistdialog.cpp:634)
            var select = GameClient.SelectAsync(from, chosen.User.CharacterId, asked);
            RememberEnterGame(chosen.User.CharacterId, asked);   // EnterGame saves the character, server and channel options right after the request (uiserverlistdialog.cpp:641-649)
            try { go = await select; }
            catch (System.IO.IOException e) when (from.IsClosed) { return Dropped(from, e); }
            if (!go.Ok)
            {
                GD.Print($"live: start refused, result 0x{go.Error:x} '{go.Message}'");
                return new EnterResult(EnterEnd.Refused, go.Error, go);
            }
            leavingLogin = true;   // the login server ends this session now
            GD.Print($"live: game entry connects to {GameClient.EndpointText(GameClient.HostOf(go.Address, host), go.Port)}");
            try { (entry, game) = await GameClient.EnterAsync(go, accountId, machine, host, requestGlobalFactor: !Factors.Applied); }
            catch (System.Net.Sockets.SocketException e)
            {
                GD.PushWarning($"live: game connect failed, socket error {(int)e.SocketErrorCode}: {e.Message}");
                return new EnterResult(EnterEnd.ConnectFailed, (int)e.SocketErrorCode);
            }
            catch (MoveRefusedException e)
            {
                GD.PushWarning("live: enter failed: " + e.Message);
                return new EnterResult(EnterEnd.MoveRefused, e.Error);
            }
            NoteGameEndpoint(GameClient.HostOf(go.Address, host), go.Port);
            AttachGameReconnect(game);
            Quests.QuestInfo = QuestTable();
            ItemReader.SlotOf ??= ItemSlotTable();
            ItemReader.ExtraOf ??= id => { try { return Kinds().ExtraOf(id); } catch (Exception e) { GD.Print($"live: item kinds unavailable ({e.Message})"); return null; } };
            ItemReader.UgcMode ??= () => ProfileImageService.Images.Mode;      // CUgcManager::GetMode, set by UgcSetMode
            ItemReader.Known ??= id => { try { return Kinds().Known(id); } catch { return true; } };   // data unavailable: read every id
            Items.TabOf = id => { try { return Kinds().TabOf(id); } catch { return 7; } };
            Items.BaseSlots ??= BaseSlotTable();
            if (Inventory == null) { Inventory = new InventoryView(Items); Inventory.RequestSort = (tab, stack) => Send(new ItemInventorySortRequest((byte) tab, stack)); Inventory.RequestMove = (uid, slot) => Send(new ItemInventoryMoveRequest(uid, slot)); Inventory.RequestEquip = (uid, slot, mode) => Send(new ItemEquipRequest(uid, slot, mode)); Inventory.RequestUnequip = uid => Send(new ItemUnequipRequest(uid)); Inventory.RequestSwapEquip = (uid, slot) => Send(new ItemSwapEquipRequest(uid, slot)); Inventory.RequestEquipGem = (uid, slot) => Send(new GemEquipRequest(uid, slot)); Inventory.RequestUnequipGem = slot => Send(new GemUnequipRequest(slot)); Inventory.RequestUse = (uid, arg) => Send(new ItemUseRequest(uid, arg)); Inventory.RequestDrop = (uid, n) => Send(new ItemInventoryDropRequest(uid, n)); Inventory.RequestDestroy = uid => Send(new ItemInventoryDestroyRequest(uid)); Inventory.RequestExpand = tab => Send(new ItemInventoryExpandRequest((byte) tab)); Inventory.ServerTime = () => GameSession?.ServerTime ?? 0; }
            Items.MaxStackOf = id => { try { return Kinds().SlotMax(id); } catch { return uint.MaxValue; } };
            StartGuide();   // before the early packets: the guide record (0x89) is among them
            // what the server sent before the field enter, in arrival order, through the read loop's handler (no HUD exists yet):
            // the adventure level, quest, item, trophy, title and key table loads among them
            login.Dispose(); login = null;
            this.chosen = chosen;
            TakeEntry();
            return new EnterResult(EnterEnd.Entered);
        }
        catch (Exception ex) { GD.PushWarning("live: enter failed: " + ex.Message); return new EnterResult(EnterEnd.Failed); }
        finally { Busy = false; }
    }

    /// <summary>The login connection was gone when the start was sent or answered: its socket error, 0 for a clean close.</summary>
    static EnterResult Dropped(TcpTransport t, Exception e)
    {
        int code = Ms2.Ui.FrontEndConnection.CodeOf(t?.Failure ?? e);
        GD.PushWarning($"live: start not sent, the login connection is closed (socket error {code})");
        return new EnterResult(EnterEnd.Dropped, code);
    }

    CharacterEntry chosen;

    /// <summary>A game server's entry (the login's, or the next server's after a GameToGame): what it sent before the field
    /// enter, in arrival order, through the read loop's handler, then its RequestFieldEnter becomes the current entry.</summary>
    void TakeEntry()
    {
        if (entry.Enter != null) { NoteServerEnter(entry.Enter.Channel); Housing.ServerEnter(); UpdateReconnectIdentity(); }
        // a new game scene: the old one's OnLeave ran GameEventManagerCli::Clear (scenestategame.cpp:1314)
        InteractShutdowns.Clear();
        Construction.Clear();
        HotTimes.Clear();
        SaleEvents.Clear();
        BeautySales.Clear();
        MesoRevivals.Clear();
        MasteryReduce.Clear();
        DungeonExtraRewards.Clear();
        Attend.Clear();
        ReactorRules.Clear();
        // the adventure level, quest, item, trophy, title and key table loads among them
        entry.Replay(Handle, () =>
        {
            AutoEquip.ResetAutoEquip();   // OnServerEnter's FUN_14092f420 (scenestategame.cpp:2070): Unk_Inventory::Clear, inventory.cpp:407
            Adventure.ServerEnter();
            StubHudData.Instance.Wallet = entry.Enter?.Wallet;
            // the found-taxi list DAT_143ab3078: ServerEnter's second int list (OnServerEnter, scenestategame.cpp:2115-2122);
            // without the enter packet it starts empty, so the stub's offline value never reaches a live session
            taxiCall = null;               // a new server enter drops the call (the state is reset)
            TaxiAction.ServerEnter();      // DAT_143ab3098.RemoveAll() (scenestategame.cpp:2115)
            StubHudData.Instance.FoundTaxis = entry.Enter?.UnlockedTaxis is { } taxis ? new HashSet<int>(taxis) : new HashSet<int>();
            StubHudData.Instance.VisitedFields = entry.Enter?.UnlockedMaps is { } maps ? new HashSet<int>(maps) : new HashSet<int>();   // DAT_143ab3058 (:2108-2114)
            StubHudData.Instance.LoadedFields.Clear();
            Me.Apply(entry.Enter);                 // the exp counters (OnServerEnter, scenestategame.cpp:2085-2086)
        });
        GD.Print($"live: {entry.Early.Count} packets before the field: {Quests.All.Count} quests, {Items.All.Count} items, adventure claimed {Adventure.Claimed.Count}, missions {Adventure.Missions.Count}");
        Ms2.Ui.FieldEnter.Take(fieldEntry, entry.Field, Breakables, Liftables, LiftTargets, Vibrates);   // the login's field: its breakables and lifts wait for its entities
        NewFieldPortals(entry.Field.MapId);
        NewFieldInteractObjects(entry.Field.MapId);
        NewFieldReactors();
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

    Field triggerField;                                  // the field the Trigger packets apply to
    readonly List<byte[]> pendingTriggers = new();       // Trigger packets that came before the field was built
    /// <summary>WorldService m_848: the field's trigger effect nifs by object id (Trigger types 0x10-0x12).</summary>
    public readonly Ms2.Ui.FieldEffectNifs EffectNifs = new();

    /// <summary>
    /// Trigger (RecvOp 0x4E): CSceneStateGame::OnTrigger (@0x141721270, scenestategame.cpp:3653) hands it to
    /// CTriggerService::FUN_141861840; the object events of types 2 and 3 go to the field's trigger objects
    /// (World.FieldTriggers). A packet before the field is built waits for it. The guide type (9) goes to the guide event
    /// pane at once (Ms2.Ui.Guide.TriggerGuide), as it needs no field.
    /// </summary>
    void OnTrigger(byte[] body)
    {
        if (LogPackets) GD.Print($"live: Trigger in {Convert.ToHexString(body, 0, Math.Min(body.Length, 24))}{(body.Length > 24 ? ".." : "")}");
        if (Ms2.Ui.Guide.TriggerGuide.Apply(body, game.Profile, GuidePane, Movie, guide,
                z => { if (player != null && IsInstanceValid(player)) player.TurnToRotZ(z); }, c => TriggerNotApplied(TriggerObjects.Ui, c.Sub),
                player != null && IsInstanceValid(player) ? player.EnterTriggerEmotion : null, TriggerFace)) return;
        if (ReadUiTrigger(body)) return;
        if (EffectNifs.ApplyTrigger(body, game.Profile)) return;     // types 0x10-0x12: WorldService m_848 and the npcs' Eff_UI nifs
        if (body.Length > 0 && TriggerObjects.ClientType(body[0], game.Profile) is var ct && (TriggerObjects.NotPorted(ct) || TriggerObjects.ClientIgnores(ct)))
        {
            if (TriggerObjects.NotPorted(ct)) TriggerNotApplied(ct, 0);     // the client applies it; this port does not (logged once)
            return;                                                          // a type without a client case does nothing
        }
        if (triggerField != null && GodotObject.IsInstanceValid(triggerField) && triggerField.Triggers != null) ApplyTrigger(triggerField, body);
        else pendingTriggers.Add(body);
    }

    /// <summary>OnCameraInterpolation (@0x14173a5b0, scenestategame.cpp:4061-4064): the trigger service's camera reset on the
    /// field's camera (<see cref="World.FieldTriggers.ResetCamera"/>).</summary>
    void OnCameraInterpolation(CameraInterpolation m)
    {
        bool on = triggerField != null && GodotObject.IsInstanceValid(triggerField) && triggerField.Triggers != null;
        if (on) triggerField.Triggers.ResetCamera(m.Seconds);
        if (LiveCheck) GD.Print($"live: camera reset over {m.Seconds:0.##} s{(on ? "" : " (no field yet, nothing to reset)")}");
    }

    /// <summary>
    /// OnPremiumClub (scenestategame.cpp:8426): mode 0 sets the premium end time of the player it names (own or shown), which the item use
    /// checks (PC::IsVip). The client also refreshes the inventory button and a main-player-info flag, and modes 1 and 2 feed the VIP dialog's
    /// claimed list; the dialog is not ported (STILL-OPEN), so those are read and logged once.
    /// </summary>
    void OnPremiumClub(Ms2.Net.Packets.PremiumClub m)
    {
        if (m.Kind != Ms2.Net.Packets.PremiumClubKind.Time || !m.HasPlayerId) { ReadNotApplied("PremiumClub", m.Mode); return; }
        if (m.PlayerId == selfObject) Me.PremiumTime = m.EndTime;
        else if (remote == null || !remote.SetPremiumTime(m.PlayerId, m.EndTime)) return;   // WorldService::FindPCById found no player: the client returns
    }

    readonly HashSet<(string, int)> packetsNotApplied = new();

    /// <summary>A packet read whole but not applied (its client state has no home here yet): logged once per packet name and mode.</summary>
    void ReadNotApplied(string name, int mode)
    {
        if (packetsNotApplied.Add((name, mode))) GD.Print($"live: {name} mode {mode} read, not applied");
    }

    readonly HashSet<int> triggersNotApplied = new();

    /// <summary>The trigger's Ui sub 9 (FUN_1418641e0): the face emotion of the object of that id
    /// (WorldService::FUN_141bd1200), the own pc's here. STILL-OPEN: npcs and other pcs; they are logged as not applied.</summary>
    void TriggerFace(int objectId, string name)
    {
        if (objectId == selfObject && player != null && IsInstanceValid(player)) player.TriggerFace(name);
        else TriggerNotApplied(TriggerObjects.Ui, TriggerObjects.UiFaceEmotion);
    }

    /// <summary>A UI trigger command read but not applied: logged once per type and sub.</summary>
    void TriggerNotApplied(byte type, byte sub)
    {
        if (triggersNotApplied.Add(type << 8 | sub)) GD.Print($"live: Trigger type 0x{type:x} sub {sub} read, not applied");
    }

    /// <summary>
    /// The Trigger types whose dialogs are not ported, read whole (CTriggerService::FUN_141861840, triggerservice.cpp:366):
    /// Dialog 0xf (the PvP observer 0x4e96, 0x4e99/0x4e9a, 0x4ed3; kind 1's timer 0x4e95 goes to the HUD), the side popup talk 0x13
    /// (UISidePopupTalk 0x4f57, the HUD's when it listens), the set-visible list 0x14 (FindObjectByName, the object's state) and the npc duel bar
    /// 0x15 (UINpcDuelHPBarDialog 0x4f62, the HUD's when it listens). STILL-OPEN: the PvP dialogs and 0x14; each is logged once per type and kind.
    /// </summary>
    bool ReadUiTrigger(byte[] body)
    {
        if (body.Length < 1) return false;
        var r = new Maple2.PacketLib.Tools.ByteReader(body);
        byte type = TriggerObjects.ClientType(r.ReadByte(), game.Profile);
        try
        {
            switch (type)
            {
                case TriggerObjects.Dialog:
                {
                    var d = TriggerObjects.ReadDialog(r);
                    if (d.Kind is >= TriggerDialogCommand.Timer and <= TriggerDialogCommand.PvpDuel) FieldTimer.Set(d.Start, d.Duration, d.AutoRemove);   // WorldService::FUN_141bc6f60, every kind 1-5
                    if (d.Kind == TriggerDialogCommand.Timer) TriggerTimerReceived?.Invoke(d);
                    else TriggerNotApplied(type, d.Kind);
                    return true;
                }
                case TriggerObjects.SidePopup:
                {
                    var popup = TriggerObjects.ReadSidePopup(r);
                    if (SidePopupReceived != null) SidePopupReceived(popup); else TriggerNotApplied(type, popup.Kind);
                    return true;
                }
                case TriggerObjects.SetVisibleUi: TriggerNotApplied(type, TriggerObjects.ReadSetVisibleUi(r).State); return true;
                case TriggerObjects.DuelHpBar: { var bar = TriggerObjects.ReadDuelHpBar(r); if (DuelHpBarReceived != null) DuelHpBarReceived(bar); else TriggerNotApplied(type, bar.Show ? (byte)1 : (byte)0); return true; }
                default: return false;
            }
        }
        catch (Exception e) { GD.PushWarning($"live: Trigger type 0x{type:x}: {e.Message}"); return true; }
    }

    static void ApplyTrigger(Field field, byte[] body)
    {
        if (field.Triggers == null) return;
        int n = field.Triggers.Apply(new Maple2.PacketLib.Tools.ByteReader(body));
        if (n > 0 && LiveCheck) GD.Print($"live: Trigger applied {n} object event(s)");
    }

    /// <summary>A node's position in the client's z-up coordinates (the inverse of the field's G); null without a live node.</summary>
    static System.Numerics.Vector3? ClientPositionOf(Node3D n) =>
        n != null && IsInstanceValid(n) && n.IsInsideTree() ? new System.Numerics.Vector3(n.GlobalPosition.X, -n.GlobalPosition.Z, n.GlobalPosition.Y) : null;

    /// <summary>CDropItem::CanStackIntoInventory (@0x1411906a0, dropitem.cpp): a bag stack of the item's id with room for the drop's amount.</summary>
    bool DropCanStack(FieldAddItem d)
    {
        if (Items == null || d.Item == null) return false;
        uint max = Items.MaxStackOf?.Invoke(d.ItemId) ?? 1u;
        if (max <= 1) return false;
        foreach (var have in Items.All)
            if (have.Id == d.ItemId && (long)max >= (long)have.Amount + d.Amount) return true;
        return false;
    }

    void OnFieldBuilt(Field field, ModelBuilder builder)
    {
        if (entry == null) return;
        triggerField = field;
        fieldBuilder = builder;
        FieldProperties.Lights = field;   // the field's ambient and sun light entities: stored light records apply now
        foreach (var body in pendingTriggers) ApplyTrigger(field, body);
        pendingTriggers.Clear();
        regionSkills.Clear(); RegionSkillLog.Clear();
        HitEffects.Clear();
        var layer = remote = new RemoteLayer(field, builder, entry.Enter.CharacterId) { Feature8e = CodiFeature, ServerNow = () => ServerTime, HotTimes = HotTimes, ItemExists = id => ItemExists?.Invoke(id) ?? true, HitEffects = HitEffects, SightRange = () => FieldProperties.SightRange, NpcTalking = () => IsTalking(), OwnPosition = () => ClientPositionOf(player) };
        remote.Tracker.Profile = game.Profile;
        remote.Tracker.Entered(entry.Enter.ObjectId);   // the ServerEnter the entry took: the own damage records' owner
        // WorldService::FUN_141bd1200 finds MyPC too: other players' StateHold on the own mount reads its seats
        remote.SelfPc = id => id == selfObject && player != null && IsInstanceValid(player) ? player : null;
        remote.SelfPassengers = OwnPassengers;
        remote.Numbered = (n, npc, pc) => DamageNumbered?.Invoke(n, npc, pc);
        remote.NpcExpShown = (npc, exp) => NpcExpShown?.Invoke(npc, exp);
        remote.NpcAdded = npc => NpcAdded?.Invoke(npc);
        remote.NpcRemoved = id => NpcRemoved?.Invoke(id);
        remote.UseEffectNifs(EffectNifs);
        remote.RegionSkillOf = id => regionSkills.TryGetValue(id, out var s) ? s : null;
        remote.Viewer = () => player != null && IsInstanceValid(player) ? player.GlobalPosition : null;
        remote.BattleStanceChanged = (id, on) => BattleStanceChanged?.Invoke(id, on);
        remote.PcShown += pc => FieldPcShown?.Invoke(pc);
        remote.PcLeft += id => FieldPcLeft?.Invoke(id);
        var kinds = Kinds();
        var dropLayer = drops = new DropLayer(field, builder, entry.Enter.CharacterId, kinds.Known,
            id => kinds.ExtraOf(id) is { } x && x.HasFlag(ItemExtra.Pet), kinds.IconPath);
        dropLayer.Send = Send;
        dropLayer.Reached = (item, picker, meso) =>
        {
            // the picker must be a PC (kind 0x6200004)
            Ui.DamageTarget pc = picker == selfObject ? null : remote?.DamageOf(picker);
            if (picker != selfObject && pc == null) return;
            if (item.IsMeso) MesoGained?.Invoke(pc, meso);
            else if (kinds.TypeOf(item.ItemId) == 3) ItemGained?.Invoke(pc, item.Amount, kinds.IconPath(item.ItemId));
        };
        dropLayer.GetKeyDown = () => PickupKeyHeld;
        dropLayer.PickupBlocked = () => !DropPickup.PickupAllowed(Me.Dead, player != null && IsInstanceValid(player) && player.Mount is { } ride && ride.Data.DisablePickUp);
        dropLayer.PlayerPosition = () => ClientPositionOf(player);
        dropLayer.ActorPosition = id => id == selfObject ? ClientPositionOf(player) : remote?.PositionOf(id);
        dropLayer.TabOf = id => Inventory?.TabOfItem(id) ?? 7;
        dropLayer.HasEmptyCell = tab => Inventory?.HasEmptyCell(tab) ?? true;
        dropLayer.CanStack = DropCanStack;
        dropLayer.Effects = () => FieldEffects;
        dropLayer.ObjectKind = id => remote?.ObjectKind(id, selfObject) ?? (id == selfObject && selfObject != 0 ? 1 : 0);
        dropLayer.Sound = (path, kind, at) =>
        {
            if (LogPackets) GD.Print($"live: drop sound {path} kind {kind}");
            FieldAudio.Instance?.PlaySoundEvent(path, kind, new Vector3(at.X, at.Z, -at.Y), player != null && IsInstanceValid(player) ? player.GlobalPosition : null);
        };
        dropLayer.SelfObjectId = () => selfObject;
        dropLayer.PickedByMe = DropPickedByMe;
        // the character's block, read once for the field (a new character is a new field; the item-get dialog edits this object)
        var autoLoot = GameOptions.AutoLoot;
        dropLayer.AutoLoot = () => autoLoot;
        dropLayer.ItemType = kinds.TypeOf;
        dropLayer.AutoRequested = d =>
        {
            if (LogPackets) GD.Print($"live: drop auto request {d.ObjectId} item {d.ItemId} kind 0x{d.DropKind:x}");
            DropAutoRequest?.Invoke(d.ObjectId);
        };
        dropLayer.Level = () => player != null && IsInstanceValid(player) && Me.User is { } u ? u.Level : null;
        dropLayer.Notice = id =>
        {
            string text = Ms2.Ui.Hud.MessageBoxData.Common(id);
            if (!string.IsNullOrEmpty(text)) SystemNoticeSink?.Invoke(text);
            if (LogPackets) GD.Print($"live: drop notice 0x{id:x}");
            DropNotice?.Invoke(id);
        };
        dropLayer.SystemSound = key =>
        {
            bool played = FieldAudio.Instance?.PlayUi(key) == true;
            if (LogPackets) GD.Print($"live: drop system sound {key}: {(played ? "played" : "not played")}");
            DropSystemSound?.Invoke(key, played);
        };
        field.TreeExiting += () => { if (drops == dropLayer) { drops = null; PickupKeyHeld = false; } };
        // the field is freed: its tracker stops filling Events (a newer field's layer is left alone); the guide stops and
        // updates on the map's unload (worldservice_1.cpp:10021)
        // the field's effects go with it: the own buffs' keep effect ids are forgotten, kept anew in the next field
        field.TreeExiting += () => { if (remote == layer) remote = null; guide?.StopAndUpdate(); OwnBuffList?.ForgetEffects(); };
        AddReactorLayer(field, builder);
        // the field is whole: the entry's answer (the login's comes through the flow as well; only one goes out)
        field.Loaded += FieldLoaded;
        GD.Print("live: remote layer ready");
    }

    /// <summary>SetMyPC(0) of a field change (RequestFieldEnter in game, GameToGame): the own PC's running invincible ends with
    /// it (<see cref="DropOwnPcInvincible"/>), the old field's player stops sending and the own object is unknown until the
    /// new field's FieldAddUser. A running ride item summon (StateSummon) goes with the old MyPC: its bar is cancelled and
    /// nothing is sent.</summary>
    void DropOwnPc()
    {
        DropOwnPcInvincible();
        rideSummon?.Cancel();
        recallCast?.Cancel();
        DropCashCall();
        if (player != null && IsInstanceValid(player)) { player.Live = false; player.DeadFlag = false; }
        player = null;
        selfObject = -1;
        Home.Clear();   // ~MyPC: m_sp1618 = 0 (mypc.cpp:897)
        OwnPassengers.Clear();   // ~MyPC drops m_mapB28 with the pc
        DropOwnSkillSets();      // ~MyPC's skill system
        bindPending = false;
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
    /// same map id): a repeat entry for the map in load changes the entry's values only; the load goes on with the first
    /// entry's portal, and its field-loaded sends and answer go out once (<see cref="Ms2.Net.FieldEntry.Enter"/>).
    /// STILL-OPEN: DAT_143ab2890 = 0 (the free-roam test field flag),
    /// FUN_140930ba0's group type (DAT_143ab2888; the room id is kept for the battle records' chaos test), CUIService FUN_141adc400 (uiservice.cpp:1574: each
    /// registered dialog with m_158 set and m_15c clear is opened again, vfunc_61(0) then vfunc_65; this HUD is rebuilt with
    /// the field scene, so its dialogs come back with it, but no dialog registry with those flags exists), and, after either
    /// outcome, GFxSceneService FUN_1422ba0d0 and SetSubScene(0x6200086) when the instance type (DAT_143ab287c, the entry's
    /// second byte; the last good entry's after an error) is 0xd have no port here.
    /// </summary>
    void OnRequestFieldEnter(RequestFieldEnter f)
    {
        // a refused entry or the loading map again keeps the field's breakables and lifts; a new field resets them
        var taken = Ms2.Ui.FieldEnter.Take(fieldEntry, f, Breakables, Liftables, LiftTargets, Vibrates); // perf-ok: FieldEnter.Take is a static method, not LINQ
        if (taken == Ms2.Ui.FieldEnterResult.Refused)
        {
            string text = Ms2.Ui.FieldMoveError.Text(f.Error, Ms2.Ui.Hud.MessageBoxData.CommonByKey);
            GD.Print($"live: RequestFieldEnter error {f.Error}");
            Ms2.Ui.Hud.MessageBoxQueue.ShowMessageBox(text, 0, null, "");   // ShowMessageBox(GetFieldMoveErrorMessage(err), 0, 0, L"")
            return;
        }
        entry = entry with { Field = f };
        dungeonRoomId = f.DungeonId;                    // FUN_140930ba0: DAT_143ab2884
        if (chosen != null) chosen.User.MapA = f.MapId;
        if (taken == Ms2.Ui.FieldEnterResult.LoadingAlready)
        {
            GD.Print($"live: RequestFieldEnter in game: map {f.MapId} is loading already, the load goes on (pos {f.Position} portal {f.Portal})");
            return;
        }
        // a new field: no region skill or trigger object survives it (an error or the loading map again keeps the field)
        regionSkills.Clear(); RegionSkillLog.Clear(); ClearFieldEffects(); pendingTriggers.Clear(); triggerField = null;
        EffectNifs.Clear();        // the field's load empties m_848 (WorldService::FUN_141bcda90, worldservice_1.cpp:6950)
        FieldProperties.Clear();   // ClearFieldProperties on field leave (worldservice_1.cpp:5368)
        // LoadMap tears the old map down (LoadMap -> FUN_141bbc1d0 -> FUN_141bbc580, worldservice_1.cpp:3320, :3353): a trigger
        // that moves the player with the cinematic key group on leaves it there
        Cinematic.FieldUnloaded();
        taxiCall = null;        // the call's state goes with the old field's own player (0x6a does not run on in the new field)
        // the old field's own player stops sending: SetMyPC(0) until the new field's FieldAddUser
        DropOwnPc();
        moveClock.Restart();
        NewFieldPortals(f.MapId);
        NewFieldInteractObjects(f.MapId);
        NewFieldReactors();
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
    /// <summary>What the field's LoadUgcMap stored (CubeMapInformationService: the owner FUN_1410c3180, the plot mode f330
    /// FUN_1410c3650, the house setting, permissions and layouts); cleared on each new field load.</summary>
    public Ms2.Net.CubeMapInfo CubeMap => fieldEntry.CubeMap;

    /// <summary>UIGameMenuDialog::MarkDirty: the game menu runs RefreshMenu on its next frame (ItemUse modes 4 and 7, a raised level).</summary>
    public Action GameMenuDirty;

    /// <summary>A raised level of any PC (OnLevelUp, scenestategame.cpp:9534): the inventory dialog, when it exists, runs
    /// UpdateButtonState.</summary>
    public Action InventoryButtonsDirty;

    /// <summary>The picked-up item's reward toast (UIRewardNotify::FUN_141abfa10): item id and grade.</summary>
    public Action<int, int> PickupRewardSink;

    /// <summary>OnItemScript mode 4's cash coupon toast (UIRewardNotify::FUN_141ac1d10): the item id.</summary>
    public Action<int> CashCouponSink;

    /// <summary>ItemScript modes 0 and 5, the Lullu box's and the gacha's result (their dialogs' vfunc_79); live only.</summary>
    public event Action<ItemScriptBoxResult> ItemScriptBoxResulted;

    /// <summary>
    /// CDropItem::OnPickedUp with the local player as picker (dropitem.cpp:1007-1020), after the get sound: the reward toast for an
    /// item of rarity 1 or more that is not a meso, a token or a merat (<see cref="DropPickup.Toasts"/>), then guide trigger 0xc with
    /// the item id when the item is valid (<see cref="DropPickup.GuideCounts"/>).
    /// </summary>
    void DropPickedByMe(FieldAddItem item)
    {
        bool toast = DropPickup.Toasts(item.ItemId, item.Rarity, item.IsMeso);
        if (toast) PickupRewardSink?.Invoke(item.ItemId, item.Rarity);
        var k = Kinds();
        bool pet = k.ExtraOf(item.ItemId) is { } x && x.HasFlag(ItemExtra.Pet);
        bool counts = DropPickup.GuideCounts(item.Item?.ExpiryTime ?? 0, ServerTime, pet, k.HasMaidId(item.ItemId));
        if (counts) guide?.AddTrigger(GuideTrigger.ItemPickup, item.ItemId, 0);
        if (LiveCheck) GD.Print($"live: picked up item {item.ItemId} rarity {item.Rarity}: toast {toast} guide {counts}");
    }

    /// <summary>The own Stat (0x2E; CSceneStateGame::OnStat, scenestategame.cpp:9424): health, spirit and stamina reach the HUD data.</summary>
    void ApplyStat(PlayerStat st)
    {
        var hud = StubHudData.Instance;
        if (st.TryGet(PlayerStat.Health, out long hm, out long hc)) { hud.Player.HpMax = hm; hud.Player.HpCurrent = hc; }
        if (st.TryGet(PlayerStat.Spirit, out long sm, out long sc)) { hud.SpMax = (int)sm; hud.SpCurrent = (int)sc; }
        if (st.TryGet(PlayerStat.Stamina, out long em, out long ec)) { hud.EpMax = (int)em; hud.EpCurrent = (int)ec; }
        if (LogPackets) GD.Print($"live: Stat hp {hud.Player.HpCurrent}/{hud.Player.HpMax} sp {hud.SpCurrent}/{hud.SpMax} ep {hud.EpCurrent}/{hud.EpMax} ({st.Values.Count} attribute(s))");
    }

    /// <summary>The field is built and shown: the end of the field load (CSceneStateGame::vfunc_1, scenestategame.cpp:1995-2001)
    /// sends RequestLoadUgcMap, then Ugc 0x12 (<see cref="Ms2.Net.FieldEntry.FieldLoaded"/>). ResponseFieldEnter waits for the
    /// server's LoadUgcMap (<see cref="OnLoadUgcMap"/>) or the field's cubes (<see cref="FieldCubesMade"/>), as the client's.</summary>
    public void FieldLoaded()
    {
        if (entry == null) return;
        fieldEntry.FieldLoaded(Send);
    }

    /// <summary>ResponseFieldEnter went out (from LoadUgcMap or the cube drain); <paramref name="first"/> when the entry had
    /// no answer yet: the field entry is done.</summary>
    void FieldAnswered(ResponseFieldEnter answer, bool first)
    {
        if (answer == null || !first) return;
        // MyPC::FUN_1415302e0's tables, loaded with the field rather than at the first placement, after the answer so the
        // field entry does not wait on them; a failed load stays null (ConfirmPlacement stops at InventoryCheckOpen) and is
        // tried again at the next field
        CubeCraft.Locks ??= CubeLocks();
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
        g.SendAsync(pk).ContinueWith(t => EngineCalls.Warn($"live: send {pk.GetType().Name} failed: {t.Exception?.GetBaseException().Message}"),
            System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>The own FieldAddUser makes the MyPC (OnAddUser, at the login's field and after each move); it sends movement
    /// from then on. The field's player node may come a frame later than the packet: the bind waits for it.</summary>
    bool bindPending;
    (System.Numerics.Vector3 Position, float RotZ)? selfAddAt;   // the own FieldAddUser's position and facing, until the bind

    /// <summary>OnAddUser makes the MyPC at the packet's position (scenestategame.cpp:11136-11147: kPos.z += 25, vfunc_19,
    /// the facing vfunc_21), then snaps it down to the ground (:11174-11181), as for the other players
    /// (<see cref="RemoteLayer.SpawnPoint"/>). The RequestFieldEnter position only goes to the world service before the map
    /// loads (OnRequestFieldEnter, scenestategame.cpp:2348); where the two differ (a trigger's move to a portal of
    /// another field), the player stands at the FieldAddUser's.</summary>
    void PlaceAtAddUser(Player found)
    {
        if (selfAddAt is not { } add) return;
        selfAddAt = null;
        var g = new Vector3(add.Position.X, add.Position.Z, -add.Position.Y);
        var at = remote?.SpawnPoint(g) ?? g + Vector3.Up * 25f;
        found.GlobalPosition = at; found.SpawnPoint = at; found.Velocity = Vector3.Zero;
        double a = add.RotZ * Math.PI / 180;
        found.Face(new Vector3((float)Math.Sin(a), 0, (float)Math.Cos(a)));
        GD.Print($"live: MyPC at the FieldAddUser position {add.Position} -> godot {at}");
    }

    void TryBindPlayer()
    {
        if (!bindPending) return;
        var found = GetTree().Root.FindChild("Player", true, false) as Player;
        if (found == null || found.IsQueuedForDeletion()) return;
        bindPending = false;
        PlaceAtAddUser(found);
        CubeCraft.ModelAttached();   // PC::vfunc_12 (pc.cpp:844-852): a craft that waited for the model is made
        if (found == player && player.Live) return;   // a repeat of the own FieldAddUser on the same field
        BindPlayer(found);
        AutoFindOwnPcIn();   // MyPC::vfunc_12 (mypc.cpp:1016): StartAutoFind (LiveSession.AutoFind.cs)
        SyncCarriedModel();   // a new body: the craft's carried model goes on it
    }

    void SendSync(System.Collections.Generic.List<(StateSync Record, int Tick)> records, bool ride)
    {
        if (game == null) return;
        var pk = new UserSyncOut { SyncNumber = syncNumber, ServerTick = game.Session.ServerTick, ClientTick = game.Session.ElapsedMs, Ride = ride };   // state.cpp:491-493: GetServerTick, then GetElapsedTime
        foreach (var r in records) pk.Records.Add(r);
        Send(pk);
        var l = records[^1].Record;
        if (LogPackets) GD.Print($"live: {(ride ? "RideSync" : "UserSync")} sent, {records.Count} record(s), last state {l.State} pos ({l.Position.X},{l.Position.Y},{l.Position.Z}) sync {l.SyncNumber}");
    }

    void BindPlayer(Player found)
    {
        player = found;
        InitOwnSkillSets();      // the new MyPC's CSkillSystem::Init
        player.Live = true;
        player.WalkRecordByAge = game?.Profile.IsGms2 ?? false;   // the recorded GMS2 client's walk cadence, see Player.WalkRecordByAge
        player.TaxiHold = TaxiHoldsMove;
        player.ServerStateHold = ServerStateHoldsBody;
        ownStateExit.SequenceLength ??= OwnSequenceSeconds;
        ownStateExit.ActionLength ??= n => player != null && IsInstanceValid(player) ? player.ActionSeconds(n) : null;
        ownStateExit.SummonRideeDurationMs = SummonRideeDuration;   // Client::Constants SummonRideeDuration and RecallCastingTime
        ownStateExit.RecallCastingTimeMs = Constants()?.Int("RecallCastingTime", 5000) ?? 5000;
        player.SetBattleStance(SelfInBattle);
        player.ServerTick = () => game?.Session.ServerTick ?? 0;
        SyncDeadFlag();   // an own FieldAddUser with no health: the flag, no state (scenestategame.cpp:11185)
        player.InteractAnimEnded += () => reactState?.SequenceEnded();
        player.StateSyncOut += records => SendSync(records, false);
        player.RideSyncOut += records => SendSync(records, true);   // StateRideeController::vfunc_0 (state.cpp:537-562)
        player.HeldStateEntered += st => HeldItem.EnterState(st, Send);
        // StateEmotion / StateEmotionIdle: CSkillSystem::IsEmotionLearned(id, 1) of the own pc, and the bore constants
        player.EmotionLearned = id => Emotions.IsLearned(id, 1, ServerTime);
        player.BoreWaitingTickMs = Constants()?.Int("BoreWaitingTick", 20000) ?? 20000;
        player.EmotionBoreChance = Constants()?.Int("EmotionBoreAnimProbability", 100) ?? 100;
        player.MoveStateEntered += BodyStateEntered;     // ChangeState: the body's own state replaces the server's
        player.HoldLookup = (id, node) => remote?.FindHoldTarget(selfObject, id, node) ?? default;
        WireRunX(player);
        player.HoldLeft += () => { if (game != null) Send(new RideLeaveRequest()); };   // MyPC::SendRequestRideMode4
        player.FallDamageHeight += h => { if (game != null) Send(new StateFallDamageRequest(h)); };   // PCBase::SendFallDamage (pcbase.cpp:4350)
        player.StateChangeOut += (mode, arg, pos) =>
        {
            if (game == null) return;
            Send(new StateChange(mode, arg, game.Session.ServerTick, new System.Numerics.Vector3(pos.X, pos.Y, pos.Z)));   // mypc.cpp:3661: GetServerTick
            if (LogPackets) GD.Print($"live: State sent, mode {mode} arg {arg}");
        };
        OwnPlayerBound?.Invoke();
    }

    /// <summary>MS2_LIVE_PACKETS=1: print each packet this session does not read itself, and each own movement packet it sends
    /// (UserSync, RideSync, StateChange: one per step while walking); a debug switch, off by default (most received ones go to the
    /// field tracker; in a town, NpcControl alone arrives about a thousand times a second).</summary>
    internal static readonly bool LogPackets = System.Environment.GetEnvironmentVariable("MS2_LIVE_PACKETS") == "1";

    /// <summary>The lines a scripted live check reads that fire per packet or per event in play (regions, other players and
    /// npcs, own casts, chat, items, exp and meso, quests, notices, event sounds): on with MS2_LIVE_AUTO=1, the switch every
    /// scripted live run sets, or with MS2_LIVE_PACKETS=1; off in a normal run, where a map's trap region alone prints its
    /// damage line many times a minute. MS2_LIVE_CHECK_LOG=0 or 1 overrides it (0 shows a scripted run what a normal run
    /// prints).</summary>
    internal static readonly bool LiveCheck = LiveCheckOn(System.Environment.GetEnvironmentVariable("MS2_LIVE_CHECK_LOG"),
        System.Environment.GetEnvironmentVariable("MS2_LIVE_AUTO"), LogPackets);

    internal static bool LiveCheckOn(string forced, string auto, bool packets) =>
        forced == "0" ? false : forced == "1" || auto == "1" || packets;

    public override void _Process(double delta)
    {
        FieldProperties.StepLights((float)delta);   // WorldService::UpdateAmbientLight/UpdateDirectionalLight (worldservice_1.cpp:9736-9737)
        TickTimeScale();   // before the game check: a ramp started in the game still ends when the link is gone
        if (game == null) { PumpLogin(); return; }
        while (game != null && game.Incoming.TryDequeue(out var p)) { Handle(p); SyncDeadFlag(); }
        if (game == null) return;   // a GameToGame let the connection go: the next server's comes with the migration
        guide?.OwnPcTick();   // MyPC::vfunc_53 (mypc.cpp:2976): only once the own pc is in the entered field
        LogGuideStatus();
        TickTaxi(delta);
        TickAutoFind(delta);   // the OnceCallbackTimer and FUN_1409f1cb0 (LiveSession.AutoFind.cs)
        Me.ExpireRevival((uint)game.Session.ServerTick);   // PC::FUN_141661ac0 (pc.cpp:2043), in PC's per-frame update
        if (player != null && IsInstanceValid(player)) Me.LeaveFallOnGround(player.Grounded);   // StateFall::vfunc_9, the state's update
        TickOwnStateExit(delta);
        if (bindPending) TryBindPlayer();
        TickGameStart();   // WorldService::FUN_141bca240 (worldservice_1.cpp:817): OnGameStart once the own pc is in
        CubeCraft.Owner ??= cubeCraftOwner ??= new CubeCraftOwner(this);
        CubeCraft.Update();          // MyPC::vfunc_53 (mypc.cpp:2969-2970): the craft's Update while in cube mode
        Portals?.Tick((uint)game.Session.ServerTick);
        TickCombatEffects();
        IdleTimer.CheckIdleTimeout();   // WorldService's update, among the sub-system updates (worldservice_1.cpp:9824)
        inGame ??= () => player != null && player.Live;   // IReconnectHost.InGame: the field was entered
        if (game.InGame != inGame) game.InGame = inGame;
        if (game.IsClosed) { StopCombatEffects(); GameLinkLost?.Invoke(); GD.PushWarning("live: game connection closed" + (game.Failure != null ? ": " + game.Failure.Message : "")); int lost = game.ReconnectEnded ? 0 : Ms2.Ui.FrontEndConnection.CodeOf(game.Failure); game.Dispose(); game = null; if (player != null) { player.Live = false; player.DeadFlag = false; } OnGameConnectionLost(lost); }   // offline fall-return emulation comes back
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
                var u = FieldAddUser.Read(p.Reader(), game.Profile);
                bool self = u.User.CharacterId == entry.Enter.CharacterId;
                if (self)
                {
                    AutoEquip.ResetAutoEquip();   // the new MyPC (scenestategame.cpp:11123)
                    selfObject = u.ObjectId; SelfInBattle = u.InBattle; selfEmotion = u.InBattle ? 1 : 0; Me.Apply(u); player?.SetBattleStance(u.InBattle);
                    // a new MyPC, then its cube mode (PC::vfunc_135, scenestategame.cpp:11189-11192) before its model is in (vfunc_12)
                    CubeCraft.ConstructionUnlockOnEnter ??= ConstructionUnlockOnEnter;
                    CubeCraft.CraftChanged ??= SyncCarriedModel;
                    CubeCraft.NewPc(); CubeCraft.Set(u.CubeMode, u.CraftMode, u.CubeItem, Me.Dead);
                    selfAddAt = (u.Position, u.Rotation.Z);
                    bindPending = true; TryBindPlayer();   // OnAddUser builds the MyPC
                    // a new MyPC holds nothing; PC::vfunc_12 (pc.cpp:853-855) holds the AddUser i32 (scenestategame.cpp:11218)
                    HeldItem.Release();
                    HeldItem.AddUser(u.HeldObject, ItemExists?.Invoke(u.HeldObject) ?? true);
                }
                if (self)
                {
                    OwnBuff(null);
                    foreach (var b in u.Buffs) OwnBuff(BuffPacket.FromAdd(b) with { FromList = true });
                }
                if (self && u.Book != null) { SkillBook = u.Book; SkillBookChanged?.Invoke(-1); }
                if (self) OwnPcCreated?.Invoke();
                if (self || LiveCheck) GD.Print($"live: FieldAddUser object {u.ObjectId} {(self ? "SELF" : "other")} char {u.User.CharacterId} '{u.User.Name}' pos {u.Position}");
            }
            else if (game.Is(p, Inbound.FieldAddItem))
            {
                var item = FieldAddItem.Read(p.Reader(), game.Profile);
                drops?.Add(item);
                bool made = drops?.Describe(item.ObjectId).Known == true;
                if (LogPackets) GD.Print($"live: drop object {item.ObjectId} item {item.ItemId} amount {item.Amount} rarity {item.Rarity} owner type {item.OwnerType} char {item.OwnerUid} from {item.SourceObjectId}: {(made ? "made" : "not made")}");
                DropAdded?.Invoke(item, made);
            }
            else if (game.Is(p, Inbound.FieldRemoveItem)) drops?.OnRemovePacket(FieldRemoveItem.Read(p.Reader()).ObjectId);
            else if (game.Is(p, Inbound.FieldPickupItem) && drops != null)
            {
                var pick = FieldPickupItem.Read(p.Reader(), drops.Describe);
                if (pick.Picked && pick.EpBall && pick.PickerId == selfObject && selfObject > 0)
                {
                    // scenestategame.cpp:2438-2446: the own EP gauge, not the stat
                    var (ep, epMax) = pick.EpGauge(Me.Stats2[PlayerStat.Stamina], Me.Stats0[PlayerStat.Stamina]);
                    StubHudData.Instance.EpCurrent = (int)ep; StubHudData.Instance.EpMax = (int)epMax;
                }
                drops.OnPickupPacket(pick);
                if (pick.DropKnown && !pick.Picked) DropRefused?.Invoke(pick.ObjectId, pick.Mode);
                if (pick.DropKnown) DropPickupRead?.Invoke(pick);
                if (!pick.DropKnown) { if (LogPackets) GD.Print($"live: pickup object {pick.ObjectId}: no drop of this field, the rest unread"); }
                else if (LogPackets || (pick.Picked && LiveCheck)) GD.Print($"live: pickup object {pick.ObjectId} picked {pick.Picked} by {pick.PickerId}{(pick.Picked ? "" : " mode " + pick.Mode)}{(pick.Meso != 0 ? " meso " + pick.Meso : "")}{(pick.EpBall ? " ep +" + pick.Ep : "")}");
            }
            else if (game.Is(p, Inbound.UserBattle) && p.Body.Length >= 5 && BitConverter.ToInt32(p.Body, 0) == selfObject
                     && OwnCurStateId(OwnSkillStateRunsOf?.Invoke() ?? false) != StateWaitingSelect)
            {
                // CSceneStateGame::OnUserBattle (@0x141717580, scenestategame.cpp:3269-3288): SetEmotion(nEmotion), skipped
                // while the actor is in StateWaitingSelect (GetCurStateId() != 0xe, scenestategame.cpp:3276)
                int was = selfEmotion;
                selfEmotion = p.Body[4];
                SelfInBattle = selfEmotion != 0;
                player?.SetBattleStance(SelfInBattle);
                if (was != selfEmotion) BattleStanceChanged?.Invoke(selfObject, selfEmotion);   // SetEmotion's change guard (pcbase.cpp:1268)
            }
            else if (game.Is(p, Inbound.Buff))
            {
                var b = BuffPacket.Read(p.Reader());
                if (b.OwnerObjectId == selfObject) { LastOwnBuff = b; OwnBuff(b); }
            }
            else if (game.Is(p, Inbound.ExpUp))
            {
                var e = ExpUp.Read(p.Reader());   // CSceneStateGame::OnExpUp (scenestategame.cpp:4129); no ExpectEnd: the client ignores trailing bytes
                bool rested = Me.RestExp > 0;     // :4139 bRested: DAT_143ab28a8 before the packet's
                Me.Apply(e);
                // scenestategame.cpp:4145: an npc of the packet's id keeps the gain (CNpc::FUN_141094df0), else the own PC shows it
                if (e.Mode == 0 && e.Gained > 0 && remote?.AddNpcExp(e.NpcId, e.Gained) != true) ExpGained?.Invoke(e.Gained, e.Type);
                // :4174: the own character's exp tally for any mode 0 gain, whoever kept the number (with a local player)
                if (e.Mode == 0 && e.Gained > 0 && selfObject > 0) BattleRecords.RecordExp(CharacterId, e.Gained);
                if (e.Mode == 0) ExpLogged?.Invoke(e, rested);   // :4180 FUN_1413606c0 for every mode 0 packet, a gain of 0 too
                if (LiveCheck) GD.Print($"live: exp up mode {e.Mode} +{e.Gained} exp {Me.Exp} rest {Me.RestExp}");
            }
            else if (game.Is(p, Inbound.ItemDrop)) { }   // CSceneStateGame::FUN_141717560 (scenestategame.cpp:3262) has an empty body: the client does nothing with it
            else if (game.Is(p, Inbound.ItemUpdate))
            {
                var u = ItemUpdate.Read(p.Reader(), (obj, uid) =>
                {
                    if (obj == selfObject) return (true, true, Me.EquipByUid(uid));
                    var (known, eq) = remote?.EquipOf(obj, uid) ?? (false, null);
                    return (known, false, eq);
                });
                if (u.Own && Me.EquipByUid(u.Uid) is { } worn) OwnItemUpdated?.Invoke(worn.Id);
                if (u.Updated && u.ObjectId != selfObject) remote?.ItemUpdated(u.ObjectId, remote.EquipOf(u.ObjectId, u.Uid).Equip);
                if (LiveCheck) GD.Print($"live: item update object {u.ObjectId} uid {u.Uid}{(u.Updated ? " (read again)" : u.Own ? " (own, item event)" : "")}");
            }
            else if (game.Is(p, Inbound.UserSkinColor))
            {
                var c = UserSkinColor.Read(p.Reader());   // CSceneStateGame::OnUserSkinColor (scenestategame.cpp:3239)
                if (c.ObjectId == selfObject && selfObject > 0) OwnSkinColor?.Invoke();   // :3252-3257: only the own player raises the item event hub's slot 0
                if (LiveCheck) GD.Print($"live: skin colour object {c.ObjectId}");
            }
            else if (game.Is(p, Inbound.BindItem))
            {
                var b = BindItem.Read(p.Reader(), Items.Get, Me.EquipByUid, game.Profile);   // CSceneStateGame::OnBindItem (scenestategame.cpp:8542)
                if (b.Found && !b.Worn) Items.Apply(new ItemMessage.Update(b.Uid, b.Item));   // the bag's item read again: the inventory redraws
                GD.Print($"live: bind item uid {b.Uid}{(b.Found ? (b.Worn ? " (worn)" : " (bag)") : " (unknown)")}");
            }
            else if (game.Is(p, Inbound.ItemScript))
            {
                if (ItemScriptCoupon.TryRead(p.Reader(), out int coupon)) { CashCouponSink?.Invoke(coupon); if (LiveCheck) GD.Print($"live: item script cash coupon {coupon}"); }
                // the open box dialogs read the same packet in their hook (vfunc_79): the gacha's mode 5, the Lullu box's mode 0
                else if (ItemScriptBoxResult.TryRead(p.Reader()) is { } box)
                {
                    if (LiveCheck) GD.Print($"live: item script box mode {box.Mode}, {box.Items.Count} item(s){(box.Items.Count > 0 ? $", first {box.Items[0].ItemId} x{box.Items[0].Count} rank {box.Items[0].Rank}{(box.Items[0].Special ? " special" : "")}" : "")}");
                    ItemScriptBoxResulted?.Invoke(box);
                }
            }
            else if (game.Is(p, Inbound.ItemUse))
            {
                var n = ItemUseNotice.Read(p.Reader());   // CSceneStateGame::OnItemUse (scenestategame.cpp:8603)
                if (n.Mode == 4) ItemUseNotify(n.ItemId);
                if (n.MarksDialogDirty) GameMenuDirty?.Invoke();   // FindGameMenuDialog(0x4e28)->MarkDirty()
                if (n.NoticeStringId != 0)
                    Ms2.Ui.Hud.MessageBoxQueue.ShowMessageBox(Ms2.Ui.Hud.MessageBoxData.Common(n.NoticeStringId), 0, null, "");   // FUN_140d73bc0(id, 0, 0, "")
                if (n.Codi is { } codi) ActiveCodiTab(codi);
                if (n.Beauty is { Type: not 0 } bq) BeautyRequested?.Invoke(bq);   // SendBeauty (:8692): the beauty dialog 0x4eee opens from this event and sends the open request (0x17) only when it opened (HudScene.Beauty, BeautyShopFlow.OpenFor)
                GD.Print($"live: item use mode {n.Mode}{(n.ItemId != 0 ? " item " + n.ItemId : "")}");
            }
            else if (game.Is(p, Inbound.ItemBox))
            {
                var n = ItemBoxResult.Read(p.Reader());   // CSceneStateGame::OnItemBox (scenestategame.cpp:8407)
                ItemBoxResulted?.Invoke(n);
                GD.Print($"live: item box {n.ItemId} opened {n.Opened} result {n.Result}");
            }
            else if (game.Is(p, Inbound.ItemDropNotice))
            {
                var n = ItemDropNotice.Read(p.Reader());   // CSceneStateGame::OnItemDropNotice (scenestategame.cpp:8023)
                ItemDropNoticed?.Invoke(n);
                if (LiveCheck) GD.Print($"live: item drop notice item {n.ItemId} uid {n.Uid}");
            }
            else if (game.Is(p, Inbound.Meso))
            {
                var m = MesoUpdate.Read(p.Reader());
                long delta = Me.Apply(m);
                if (delta > 0 && selfObject > 0) BattleRecords.RecordMeso(CharacterId, delta);   // :7016, with a local player
                if (LiveCheck) GD.Print($"live: meso {m.Balance} ({delta:+#;-#;0}, kind {m.Kind})");
                MesoChanged?.Invoke(delta, m.Kind);
            }
            else if (game.Is(p, Inbound.Meret)) MeretReceived?.Invoke(MeretUpdate.Read(p.Reader()));
            else if (game.Is(p, Inbound.MeretUse)) MeretUseReceived?.Invoke(MeretUseUpdate.Read(p.Reader()));
            else if (game.Is(p, Inbound.CurrencyToken)) CurrencyTokenReceived?.Invoke(CurrencyTokenUpdate.Read(p.Reader()));
            else if (game.Is(p, Inbound.PlaySystemSound)) SystemSoundReceived?.Invoke(SystemSound.Read(p.Reader()));
            else if (game.Is(p, Inbound.SmartPush)) SmartPushReceived?.Invoke(SmartPushMessage.Read(p.Reader()));
            else if (game.Is(p, Inbound.GlobalPortal)) GlobalPortalReceived?.Invoke(GlobalPortalPacket.Read(p.Reader()));   // no ExpectEnd: mode 1 leaves its event id unread, as the client
            else if (game.Is(p, Inbound.RecallScroll)) RecallScrollReceived?.Invoke(RecallScrollPacket.Read(p.Reader()));
            else if (game.Is(p, Inbound.BuddyEmote)) OnBuddyEmote(BuddyEmoteMessage.Read(p.Reader()));
            else if (game.Is(p, Inbound.NoticeDialog)) NoticeDialogReceived?.Invoke(NoticeDialogMessage.Read(p.Reader()));
            else if (game.Is(p, Inbound.NewsNotification)) NewsNotificationReceived?.Invoke(NewsNotification.Read(p.Reader()));
            else if (game.Is(p, Inbound.UiText))
            {
                var t = UiText.Read(p.Reader());   // CSceneStateGame::FUN_141731190 (:7878): a recover number on the actor
                if (t.Grade >= 0)
                {
                    var n = new Ms2.Ui.Hud.DamageNumber(t.ObjectId, t.Value, t.Grade, t.ObjectId);
                    if (t.ObjectId == SelfObjectId) DamageNumbered?.Invoke(n, null, null);
                    else remote?.ShowNumber(n);
                }
            }
            else if (game.Is(p, Inbound.AdventurerBar))
            {
                AdventurerBarCount = p.Reader().ReadInt();   // CSceneStateGame::OnAdventurerBar (scenestategame.cpp:4082): DAT_143ab2900
                AdventurerBarChanged?.Invoke();              // the game menu's FUN_141939620
                if (LiveCheck) GD.Print($"live: adventurer bar {AdventurerBarCount}");
            }
            else if (game.Is(p, Inbound.LevelUp))
            {
                var l = LevelUp.Read(p.Reader());  // CSceneStateGame::OnLevelUp (scenestategame.cpp:9511)
                int selfOld = Me.User?.Level ?? 0;
                bool selfLevel = Me.Apply(l);
                int? oldLevel = selfLevel ? selfOld : remote?.LevelUp(l.ObjectId, l.Level);
                if (oldLevel is { } was && l.Raises(was))
                {
                    InventoryButtonsDirty?.Invoke();   // :9534-9536 the inventory dialog (0x4e2d)'s UpdateButtonState
                    GameMenuDirty?.Invoke();   // :9537-9539 FindGameMenuDialog(0x4e28)->MarkDirty()
                    PlayPcEffect(l.ObjectId, selfLevel, LevelUpEffectKey);   // :9520-9540 EFFECT_LEVEL_UP on the PC or its ridee
                }
                if (selfLevel) OwnLevelChanged?.Invoke(selfOld, l.Level);   // :9541-9561, before status 6 and NotifyChangeMyLevel
                if (selfLevel) guide?.OnChangeMyLevel();   // NotifyChangeMyLevel (CGuideManager::OnChangeMyLevel)
                if (selfLevel) AutoEquipOnLevelUp(l.Level);   // :9565 Unk_Inventory::FUN_140a66990(nLevel), before UpdateExp(3)
                if (selfLevel || LiveCheck) GD.Print($"live: level up object {l.ObjectId} level {l.Level}{(selfLevel ? " SELF" : "")}");
            }
            else if (game.Is(p, Inbound.SyncNumber))
            {
                syncNumber = SyncNumber.Read(p.Reader()).Number;
                player?.OnSyncNumber();
                GD.Print($"live: SyncNumber {syncNumber}");
            }
            else if (game.Is(p, Inbound.RequestFieldEnter))
                OnRequestFieldEnter(RequestFieldEnter.Read(p.Reader()));
            else if (game.Is(p, Inbound.Trigger))
                OnTrigger(p.Body);
            else if (game.Is(p, Inbound.Cinematic))
            {
                var cine = CinematicPacket.Read(p.Reader());
                if (LogPackets) GD.Print($"live: cinematic {cine}");
                Cinematic.Apply(cine);      // the HUD's pane (UICutSceneCinematic) runs the movie calls
            }
            else if (game.Is(p, Inbound.CameraInterpolation))
                OnCameraInterpolation(CameraInterpolation.Read(p.Reader()));
            else if (game.Is(p, Inbound.UserMoveByPortal))
            {
                var m = UserMoveByPortal.Read(p.Reader());
                if (LiveCheck) GD.Print($"live: MoveByPortal object {m.ObjectId} pos {m.Position} rot {m.Rotation} portal {m.IsPortal}");
                if (m.ObjectId == selfObject) player?.ServerMoveByPortal(new Vector3(m.Position.X, m.Position.Y, m.Position.Z), m.Rotation.Z, m.IsPortal);
            }
            else if (game.Is(p, Inbound.NpcTalk))
            {
                var t = NpcTalkMessage.Read(p.Reader(), game.Profile);   // UITalkManager::OnTalkPacket (uitalkcinematic.cpp:971)
                GD.Print($"live: npc talk {t}");
                NpcTalkReceived?.Invoke(t);
            }
            else if (game.Is(p, Inbound.Beauty))
            {
                BeautyPacketReceived?.Invoke(p.Body);
            }
            else if (game.Is(p, Inbound.Shop))
            {
                var sp = ShopPacket.Read(p.Reader(), game.Profile);   // UIShopDialog::OnShopPacket (uishopdialog.cpp:1524)
                GD.Print($"live: shop cmd {sp.Command}");
                ShopReceived?.Invoke(sp);
            }
            else if (game.Is(p, Inbound.MeretMarket))
            {
                var mm = MeretMarketPacket.Read(p.Reader(), game.Profile);
                if (mm != null)
                {
                    GD.Print($"live: merat market buy mode 0x{mm.Mode:x}");
                    MeretMarketReceived?.Invoke(mm);
                }
            }
            else if (game.Is(p, Inbound.ItemMerge))
            {
                var mr = p.Reader();
                var im = ItemMergePacket.Read(mr);   // CSceneStateGame::OnItemFusion (scenestategame.cpp:5906)
                GD.Print($"live: item merge mode {im.Mode}");
                ItemMergeReceived?.Invoke(im, mr);
            }
            else if (game.Is(p, Inbound.ItemDismantle))
            {
                var idp = ItemDismantlePacket.Read(p.Reader());   // CItemBreakManager::OnItemDismantlePacket (itembreakmanager.cpp:373)
                GD.Print($"live: item dismantle mode {idp.Mode}");
                ItemDismantleReceived?.Invoke(idp);
            }
            else if (game.Is(p, Inbound.SystemShop))
            {
                var ss = SystemShopPacket.Read(p.Reader());   // CSceneStateGame::OnSystemShop (scenestategame.cpp:7928)
                GD.Print($"live: system shop type {ss.Type} ok {ss.Ok}");
                SystemShopReceived?.Invoke(ss);
            }
            else if (game.Is(p, Inbound.Quest))
            {
                var q = ReadQuest(p);
                bool known = GuideQuests.KnownBefore(Quests, q);   // RemoveQuest's early return on an unknown quest
                Quests.Apply(q);
                GuideOnQuest(q, known);
                if (LiveCheck) GD.Print($"live: quest {q.GetType().Name} ({Quests.All.Count} quests)");
            }
            else if (game.Is(p, Inbound.Party))
            {
                var m = PartyMessage.Read(p.Reader(), game.Profile);   // CPartyManager::OnParty (partymanager.cpp:447); the decoded modes end with ExpectEnd
                Party.Apply(m, CharacterId, PartyWindowOpen());
                if (m is PartyInvite invite)        // OnPartyInvite (partymanager.cpp:968); no PvP state yet, so no PvP point
                    Ms2.Ui.Hud.PartyInviteBox.OnInvite(invite, GameOptions.AccountCommunity.DisablePartyRequest, false, AnswerPartyInvite);
                if (LiveCheck) GD.Print($"live: party {m.GetType().Name} ({Party.Members.Count} members)");
            }
            else if (game.Is(p, Inbound.PartySearch))
            {
                var m = PartySearchMessage.Read(p.Reader());   // CPartyManager::OnMatchParty: the listing's register, remove, results and errors
                Party.Apply(m);
                GD.Print($"live: party search {m.GetType().Name}");
            }
            else if (game.Is(p, Inbound.GroupChat))
            {
                var m = GroupChatMessage.Read(p.Reader());   // CGroupChatManager::OnGroupChat (@0x14137f2f0, groupchatmanager.cpp:577)
                GroupChats.Apply(m);
                groupChatPackets++;
                if (LiveCheck) GD.Print($"live: group chat {m.GetType().Name} ({GroupChats.ChatCount} chats)");
            }
            else if (game.Is(p, Inbound.ItemInventory))
            {
                ApplyItems(p);
                if (LiveCheck) GD.Print($"live: item packet ({Items.All.Count} items)");
            }
            else if (game.Is(p, Inbound.ItemLock))
            {
                try
                {
                    LockBook.Find = Items.Get;
                    LockBook.StoreItem = i => Items.Apply(new ItemMessage.Update(i.Uid, i));
                    var m = ItemLockMessage.Read(p.Reader(), Items.Get);   // CItemLockManager::OnItemLock (itemlockmanager.cpp)
                    LockBook.Apply(m);
                    GD.Print($"live: item lock {m.GetType().Name}");
                }
                catch (Exception e) { GD.PushWarning($"live: item lock packet: {e.Message}"); }
            }
            else if (game.Is(p, Inbound.ItemEnchant))
            {
                try
                {
                    EnchantBook.Send = SendEnchant;
                    EnchantBook.StoreItem = i => Items.Apply(new ItemMessage.Update(i.Uid, i));
                    var m = ItemEnchantMessage.Read(p.Reader(), Items.Get, game.Profile);   // CSceneStateGame::OnItemEnchant (scenestategame.cpp:6101)
                    EnchantBook.Apply(m);
                    GD.Print($"live: item enchant {m.GetType().Name}");
                }
                catch (Exception e) { GD.PushWarning($"live: item enchant packet: {e.Message}"); }
            }
            else if (game.Is(p, Inbound.StorageInventory))
            {
                try
                {
                    var m = StorageMessage.Read(p.Reader(), Storage.Id, game.Profile);   // CStore::Dispatch (store.cpp, 0x140de3480)
                    Storage.Apply(m);
                    GD.Print($"live: storage {m.GetType().Name} ({Storage.Count} items)");
                }
                catch (Exception e) { GD.PushWarning($"live: storage packet: {e.Message}"); }
            }
            else if (game.Is(p, Inbound.ItemPutOn))
            {
                var o = ItemPutOn.Read(p.Reader(), game.Profile);        // CSceneStateGame::OnItemPutOn (scenestategame.cpp:3080); display only, no c2s answer
                bool ownEquipment = Me.Apply(o, WornLayer, ItemExists);
                if (!ownEquipment && o.ObjectId != selfObject) remote?.PutOn(o, WornLayer, ItemExists);
                if (LiveCheck) GD.Print($"live: item put on {o.Item.Id} uid {o.Item.Uid} slot {o.Item.Slot} kind {o.Kind} by {o.ObjectId}{(ownEquipment ? " (own equipment)" : "")}");
            }
            else if (game.Is(p, Inbound.ItemPutOff))
            {
                var o = ItemPutOff.Read(p.Reader());       // CSceneStateGame::OnItemPutOff (scenestategame.cpp:3144)
                bool ownEquipment = Me.Apply(o, WornLayer);
                if (!ownEquipment && o.ObjectId != selfObject) remote?.PutOff(o, WornLayer);
                if (LiveCheck) GD.Print($"live: item put off uid {o.ItemUid} by {o.ObjectId}{(ownEquipment ? " (own equipment)" : "")}");
            }
            else if (game.Is(p, Inbound.Notice))
            {
                var n = NoticeMessage.Read(p.Reader());     // CSceneStateGame::OnNotice (scenestategame.cpp:9216); no ExpectEnd: the client ignores trailing bytes
                Notice?.Invoke(n);
                if (LiveCheck) GD.Print($"live: notice kind {n.Kind} flags 0x{n.Flags:x} text kind {n.Text.Kind}{(n.Text.Formatted ? $" id {n.Text.Id} args {n.Text.Args.Length}" : $" \"{n.Text.Text}\"")}");
            }
            else if (game.Is(p, Inbound.FollowNpc))
            {
                var f = FollowNpc.Read(p.Reader());         // CSceneStateGame::OnFollowNpc (scenestategame.cpp:5862-5869): no local player, nothing
                if (remote != null && IsInstanceValid(remote)) remote.CatchUp(f.NpcId, npc: true);   // the dummy npc's FieldAddNpc came just before
                if (player != null && IsInstanceValid(player))
                {
                    player.FindNpc = id => remote != null && IsInstanceValid(remote) ? remote.FollowPoseOf(id) : null;
                    player.EnterTriggerFollowNpc(f.NpcId);
                }
                if (LiveCheck) GD.Print($"live: follow npc {f.NpcId}: state {player?.State}{(player?.FollowedNpc == f.NpcId ? " (following)" : "")}");
            }
            else if (game.Is(p, Inbound.NpcNotice))
            {
                var n = NpcNotice.Read(p.Reader());         // CSceneStateGame::OnNpcNotice (scenestategame.cpp:9145-9215)
                string applied;
                switch (n.Mode)
                {
                    case NpcNotice.OwnerEffect:
                        applied = remote != null && remote.SetOwnerEffect(n.ObjectId, n.Model) ? $"owner effect {n.Model} on npc {n.ObjectId}" : $"no npc {n.ObjectId}";
                        break;
                    case NpcNotice.NpcScript:
                        // CNpc::FUN_1410951d0 (unit_14108e1e0_npcactorandvibrate.cpp:1608-1613): the npc's model changes to the named
                        // kfm (ChangeModel)
                        applied = remote != null && remote.ChangeNpcModel(n.ObjectId, n.Script) ? $"npc {n.ObjectId} model {n.Script}" : $"no npc {n.ObjectId}";
                        break;
                    case NpcNotice.Announce or NpcNotice.SidePopup:
                        applied = NpcNoticeReceived != null ? "shown" : "no HUD";
                        NpcNoticeReceived?.Invoke(n);
                        break;
                    default: applied = "nothing"; break;
                }
                if (LiveCheck) GD.Print($"live: npc notice mode {n.Mode}: {applied}{(n.Mode == NpcNotice.SidePopup ? $" kind {n.Kind} npc {n.NpcId} icon {n.Icon}" : "")}");
            }
            else if (game.Is(p, Inbound.NpcRide))
            {
                var n = Ms2.Net.Packets.NpcRide.Read(p.Reader());   // CSceneStateGame::OnNpcRide (scenestategame.cpp:4871-4877)
                bool found = remote != null && IsInstanceValid(remote) && remote.NpcRide(n);
                if (LiveCheck) GD.Print($"live: npc ride {n.ObjectId} mode {n.Mode}{(n.Mode == 0 ? $" damage {n.AccumulatedDamage}" : n.Mode == 1 ? $" owner {n.Owner}" : "")}{(found ? "" : " (no npc)")}");
            }
            else if (game.Is(p, Inbound.Insignia))
            {
                var n = Nametag.Read(p.Reader());           // CSceneStateGame::OnNametag (scenestategame.cpp:7314-7331)
                bool ownTag = Me.Apply(n);
                bool tagged = !ownTag && remote != null && remote.SetSymbol(n.ObjectId, n.Symbol, n.Shown);
                if (LiveCheck) GD.Print($"live: name tag symbol {n.Symbol} shown {n.Shown} on {n.ObjectId}{(ownTag ? " (own)" : tagged ? $" (tag shows {remote.ShownSymbol(n.ObjectId)})" : " (not here)")}");
            }
            else if (game.Is(p, Inbound.UserChat))
            {
                var r = p.Reader();
                var c = ChatMessage.Read(r);                // UIMessengerBrowser::vfunc_84 (uimessenger.cpp:2829); no ExpectEnd: the client ignores trailing bytes
                UserChat?.Invoke(c, () => WeddingChatHall.Read(r));
                if (LiveCheck) GD.Print($"live: chat type 0x{c.Type:x} from {c.Name}{(c.Common ? $" common {c.CommonId}" : "")}");
            }
            else if (game.Is(p, Inbound.UserChatItemLink))
            {
                var links = ChatItemLinks.Read(p.Reader());  // UIMessengerBrowser::vfunc_85 (uimessenger.cpp:2963); no ExpectEnd: the client ignores trailing bytes
                UserChatItemLink?.Invoke(links);
                if (LiveCheck) GD.Print($"live: chat item links {links.Items.Count}");
            }
            else if (game.Is(p, Inbound.BadgeEquip))
            {
                var b = BadgeEquipMessage.Read(p.Reader(), game.Profile);     // CSceneStateGame::OnBadgeEquip (scenestategame.cpp:11006); no ExpectEnd: the client ignores trailing bytes
                bool ownGems = Me.Apply(b);
                if (b.Mode == 4) GemEquipError?.Invoke(b.Error);
                else if (b.Mode is 0 or 1) BadgeEquipped?.Invoke(b);
                if (LiveCheck) GD.Print($"live: badge equip mode {b.Mode} by {b.ObjectId}{(b.Mode == 4 ? $" error {b.Error}" : "")}{(ownGems ? " (own gems)" : "")}");
            }
            else if (game.Is(p, Inbound.Taxi))
            {
                var t = Ms2.Net.Packets.TaxiFound.Read(p.Reader());   // CSceneStateGame::OnTaxi (scenestategame.cpp:7902): the ids join DAT_143ab3078
                if (t.Fields.Length > 0)                            // the client returns on an empty list
                {
                    StubHudData.Instance.FoundTaxis.UnionWith(t.Fields);
                    TaxiFound?.Invoke(t);
                    autoFind?.OnTaxiFound();   // OnTaxi's last call, FUN_1409f1970 (scenestategame.cpp:7923)
                }
                GD.Print($"live: taxi found {string.Join(",", t.Fields)} open {t.Open}");
            }
            else if (game.Is(p, Inbound.UserState))
            {
                var u = UserState.Read(p.Reader());     // OnUserState (scenestategame.cpp:5758): the state, then state 12 runs PC::vfunc_137
                bool letIn = u.ObjectId == selfObject && !Me.DeathOnlyStates;   // PC::vfunc_124 (pc.cpp:1199)
                Me.ApplyUserState(u.ObjectId, u.State);
                if (u.ObjectId == selfObject) EndTaxiCall();    // any state set on the own player leaves StateTaxiCall (ChangeState, vfunc_5)
                bool died = u.ObjectId == selfObject && u.State == 12 && Me.Dead;  // the dead branch runs each time, not only on the flip
                // every state goes through vfunc_59, so ride, rope, climb and ladder from the server let go of the held item too
                if (u.ObjectId == selfObject) HeldItem.UserState(u.State, Me.Dead, Send);
                if (died)
                {
                    GD.Print($"live: own player dead (state {u.State})");
                    guide?.QueueOccurrence(GuideOccurrence.Dead, 0);   // MyPC::vfunc_137 when dead (mypc.cpp:1350)
                }
                // the body follows the state vfunc_59(state) left (scenestategame.cpp:5766, gated by PC::vfunc_124): state 12 enters
                // StateDead with or without the dead flag (StateDead::vfunc_4, statedead.cpp:152), 0x18 StateRevival, and another
                // state let in leaves them
                if (u.ObjectId == selfObject && player != null && IsInstanceValid(player))
                {
                    SyncDeadFlag();
                    if (Me.State == 0xc) OwnDeathState();
                    else if (Me.State == 0x18) player.EnterRevival();
                    else
                    {
                        player.LeaveDeath();
                        // the class's enter with no data on the body (Ms2.Ui.OwnStateExit.BodyEntryOf)
                        if (letIn && u.State is not (0xc or 0x18))
                        {
                            var entry = Ms2.Ui.OwnStateExit.BodyEntryOf(u.State);
                            player.EnterServerState(entry);
                            // StateLadder / StateRope with nothing in reach enter StateIdle (vfunc_59(1), stateladder.cpp:198-201,
                            // statemanager.cpp:1257-1260), also when the body already stands
                            if (entry is Ms2.Ui.BodyEntry.Ladder or Ms2.Ui.BodyEntry.Rope && player.State is not (Player.MoveState.Ladder or Player.MoveState.Rope)
                                && Me.LeaveServerState(u.State))
                                if (LiveCheck) GD.Print($"live: server state 0x{u.State:x} found nothing in reach and enters StateIdle");
                        }
                    }
                }
            }
            else if (game.Is(p, Inbound.DeadUser))
            {
                var d = DeadUser.Read(p.Reader());      // OnDeadUser (scenestategame.cpp:6532)
                Me.Die(d.ObjectId, d.Flag);
                if (d.ObjectId == selfObject) EndTaxiCall();
                if (d.ObjectId == selfObject && Me.Dead)        // the dead branch runs each time, not only on the flip
                {
                    GD.Print("live: own player dead");
                    guide?.QueueOccurrence(GuideOccurrence.Dead, 0);   // MyPC::vfunc_137 when dead (mypc.cpp:1349)
                }
                // MyPC::vfunc_137 (mypc.cpp:1346-1348): IsDead -> vfunc_59(0xc), StateDead::vfunc_4 (statedead.cpp:152)
                if (d.ObjectId == selfObject && Me.Dead) { HeldItem.EnterState(HeldItemState.Dead, Send); OwnDeathState(); }
            }
            else if (game.Is(p, Inbound.Revival))
            {
                var v = Revival.Read(p.Reader());       // OnRevival (scenestategame.cpp:7387)
                bool revived = Me.Revive(v.ObjectId, v.Flag);
                // MyPC's vfunc_59(0x18) (scenestategame.cpp:7400), run for the own actor whatever the flag: StateRevival on the body
                if (v.ObjectId == selfObject && player != null && IsInstanceValid(player)) { SyncDeadFlag(); player.EnterRevival(); }
                if (revived)
                {
                    GD.Print("live: own player revived");
                    guide?.QueueOccurrence(GuideOccurrence.Dead, 0);   // OnRevival of the own player (scenestategame.cpp:7405)
                }
                // OnRevival (scenestategame.cpp:7413): any revived PC's FUN_141661370(1, UserRevivalInvincibleTick, 100000004)
                // (the other players' through the field tracker's UserRevivalEvent, RemoteLayer)
                if (v.ObjectId == selfObject && selfObject >= 0) StartInvincible(Ms2.Ui.Hud.InvincibleBlink.RevivalTimer);
            }
            else if (game.Is(p, Inbound.RevivalConfirm))
            {
                var c = RevivalConfirm.Read(p.Reader());    // OnRevivalConfirm (scenestategame.cpp:7356)
                var (effect, guideTrigger) = Me.ApplyRevivalConfirm(c);
                if (effect) PlayOwnCommonEffect(RevivalPenaltyEndKey);     // InsertEffect of CommonResource +0x358 at the own PC
                if (guideTrigger) guide?.AddTrigger(GuideTrigger.RevivalPanelty, 0, 0);
                GD.Print($"live: revival confirm object {c.ObjectId} end tick {c.EndTick} count {c.Count}: effect {effect} guide {guideTrigger}");
            }
            else if (game.Is(p, Inbound.StateConsumeEp))
            {
                long ep = Me.Apply(StateConsumeEp.Read(p.Reader()));   // OnStateConsumeEp (scenestategame.cpp:5799): absolute EP
                StubHudData.Instance.EpCurrent = (int)ep;
            }
            else if (game.Is(p, Inbound.StateFallDamage))
            {
                var fall = StateFallDamage.Read(p.Reader());
                int g = Me.ApplyFallDamage(fall);    // OnStateFallDamage (scenestategame.cpp:5815)
                if (g == -1 && player != null && IsInstanceValid(player)) player.EnterFallDamage();   // the own PC not dead: vfunc_59(0x1f)
                if (g != 0) guide?.AddTrigger(GuideTrigger.FallDamage, g, 0);
                if (Me.FallDamageVoice(fall, player != null && IsInstanceValid(player) && player.Mount != null) is { } voice)
                    PlayOwnCommonEffect(voice);
            }
            else if (game.Is(p, Inbound.CharacterInfo))
            {
                // UIPCInfoDialog::vfunc_79 (uipcinfodialog.cpp:505): the answer to the CharacterInfo request for the character window
                var answer = CharacterInfoAnswer.Read(p.Reader(), game.Profile);
                CharacterInfoReceived?.Invoke(answer);
            }
            else if (game.Is(p, Inbound.RecallUser)) OnRecallUser(RecallUserPacket.Read(p.Reader()));   // OnRecallUser (scenestategame.cpp:4733): no ExpectEnd, the client reads no further
            else if (game.Is(p, Inbound.Tombstone))
            {
                var t = TombstoneUpdate.Read(p.Reader());      // OnTombstone (scenestategame.cpp:9644) -> PC::FUN_14165d300
                GD.Print($"live: tombstone object {t.ObjectId} kind {t.Kind} value {t.Value} hitter {t.Hitter} pet {t.Pet} (own tomb {Tomb != null && IsInstanceValid(Tomb)})");
                if (Me.Apply(t) && Tomb != null && IsInstanceValid(Tomb))
                {
                    // PC::FUN_14165d300 (pc.cpp:3464) on the own tomb: the hit sound, the break's myPC_Resurrection voice, then the state
                    if (Tomb.PlayPacketSounds(t.Kind, t.Hitter, true, Constants(), remote?.ViewerPosition, remote?.HitSoundRand ?? NoHitSoundRand)
                        && player != null && IsInstanceValid(player))
                        FieldAudio.Instance?.PlayEventVoice(Ms2.Audio.EventSoundTable.MyPcResurrection, Me.User.Gender, Me.User.JobCode, true,
                            Ms2.Audio.SoundMusicEffectState.CasterMyPc,
                            player.GlobalPosition, player.GlobalPosition);
                    Tomb.Hit(t.Kind, t.Value);
                }
            }
            else if (game.Is(p, Inbound.ResponseRide)) OnResponseRide(ResponseRide.Read(p.Reader(), selfObject, game.Profile));   // OnResponseRide (:12026)
            else if (game.Is(p, Inbound.RevivalCount))
            {
                var rc = RevivalCount.Read(p.Reader());
                int before = Me.MesoRevivalsUsed;
                Me.Apply(rc);   // OnRevivalCount (scenestategame.cpp:4092)
                GD.Print($"live: revival count {rc.Used}: meso revives used {before} -> {Me.MesoRevivalsUsed}");
            }
            else if (game.Is(p, Inbound.DungeonReviveCount))
            {
                Me.Apply(DungeonReviveCount.Read(p.Reader()));   // OnDungeonReviveCount (:5074)
                GD.Print($"live: dungeon revive count {Me.DungeonRevivalsUsed}");
            }
            else if (game.Is(p, Inbound.AutoRevive)) AutoReviveStarted?.Invoke(AutoRevive.Read(p.Reader()).DurationMs);   // OnAutoRevive (:4332)
            else if (game.Is(p, Inbound.ItemPutOnOrOff))
            {
                var o = ItemPutOnOrOff.Read(p.Reader());   // CSceneStateGame::OnItemPutOnOrOff (scenestategame.cpp:2575); read only, no c2s answer
                if (LiveCheck) GD.Print($"live: item put on/off {o}");
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
                var line = new System.Text.StringBuilder($"live: guide record {r.Steps.Count} step(s) bits 0x{r.Bits:x}");
                for (int i = 0; i < r.Steps.Count; i++) line.Append(' ').Append(r.Steps[i].SaveId).Append(':').Append(r.Steps[i].Step);
                GD.Print(line.ToString());
            }
            else if (game.Is(p, Inbound.Job))
            {
                var j = JobPacket.Read(p.Reader(), game.Profile);   // CSceneStateGame::OnJob (scenestategame.cpp:7449); no ExpectEnd: the client ignores trailing bytes
                bool selfJob = j.ObjectId == selfObject;   // FindPCById: a PC not in the field yet drops the packet, as here
                if (selfJob && j.Book != null) { SkillBook = j.Book; SkillBookChanged?.Invoke(j.Mode); }
                if (selfJob && j.Mode is 0 or 2 or 7) guide?.OnJobChanged(Me.User?.Job ?? 0);   // NotifyStatusChanged(4) with the sub job (scenestategame.cpp:7463/7485/7506)
                if (selfJob && j.Mode == 3) ShowJobError(j.Error, j.ErrorValue);
                // mode 2: the common resource's changeJobs effect on the PC, any PC the field has (scenestategame.cpp:7476-7481)
                if (j.Mode == 2 && remote?.PlayJobChangeEffect(j.ObjectId) == true)
                {
                    if (selfJob) JobChangeEffects++;
                    if (LiveCheck) GD.Print($"live: job change effect on object {j.ObjectId}");
                }
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
                if (LiveCheck) GD.Print($"live: achieve {m?.GetType().Name ?? "other mode"} (tally {Achieves.Sum})");
            }
            else if (game.Is(p, Inbound.Mail))
            {
                var m = MailMessage.Read(p.Reader(), game.Profile);             // CSceneStateGame::OnMail (scenestategame.cpp:11496)
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
                var m = BlackMarketMessage.Read(p.Reader(), game.Profile);      // CBlackMarketMan::OnBlackMarket (networkservice.cpp:690, @0x140a20650)
                BlackMarket.OnBlackMarket(m);
                GD.Print($"live: black market mode {m.Mode} ({BlackMarket.OwnProducts.Count} own, {BlackMarket.SearchProducts.Count} found)");
            }
            else if (game.Is(p, Inbound.Buddy))
            {
                var m = BuddyMessage.Read(p.Reader());            // CSceneStateGame::OnBuddy -> CBuddyManager::OnBuddy (buddymanager.cpp:95)
                if (m != null) { Buddies.Apply(m); BuddyPacket?.Invoke(m); }
                if (LiveCheck) GD.Print($"live: buddy protocol {m?.Protocol.ToString() ?? "?"} ({Buddies.Count} entries, {Buddies.ReceivedCount} received)");
            }
            else if (game.Is(p, Inbound.Guild))
            {
                var m = GuildMessage.Read(p.Reader(), game.Profile);            // CGuildManager::FUN_1413b6370 (guildmanager.cpp:301)
                Guilds.Apply(m);
                if (LiveCheck) GD.Print($"live: guild type 0x{m.Type:x2} (guild {Guilds.GuildId}, {Guilds.MyRequests.Count} mine, {Guilds.RequestsForGuild.Count} for the guild, {Guilds.SearchResults.Count} results)");
            }
            else if (game.Is(p, Inbound.Prestige))
            {
                var m = PrestigeMessage.Read(p.Reader());         // OnPrestige (scenestategame.cpp:8815); other kinds are dropped
                // kind 2: FindPCById; the own PC's old level fills level 1 (:8856), then vfunc_140 (:8836). Other PCs'
                // user blocks take it in the field tracker
                bool ownLevel = m is PrestigeMessage.LevelUp up && up.ObjectId == selfObject && Me.User != null;
                int ownOld = ownLevel ? Me.User.PrestigeLevel : 0;
                if (m != null) Adventure.Apply(m, ownLevel ? Me.User.PrestigeLevel : null, MissionFeature());
                if (ownLevel) Me.User.PrestigeLevel = ((PrestigeMessage.LevelUp)m).Value;
                if (m is PrestigeMessage.LevelUp lu)
                {
                    // :8833-8849: the PC's old level, the new; on a rise EFFECT_ADVENTURE_LEVEL_UP on it or its ridee
                    int? was = ownLevel ? ownOld : lu.ObjectId == selfObject ? null : remote?.AdventureLevelUp(lu.ObjectId, lu.Value);
                    if (was is { } w && lu.Value > w) PlayPcEffect(lu.ObjectId, ownLevel, AdventureLevelUpEffectKey);
                    if (ownLevel) OwnAdventureLevelChanged?.Invoke(ownOld, lu.Value);
                }
                if (m != null) PrestigeReceived?.Invoke(m);
                if (LiveCheck) GD.Print($"live: prestige {m?.GetType().Name ?? "other kind"}");
            }
            else if (game.Is(p, Inbound.Lapenshard))
            {
                var m = LapenshardMessage.Read(p.Reader());        // OnLapenshard (scenestategame.cpp:8743); other modes are dropped
                if (m != null) { Lapenshards.Apply(m); LapenshardReceived?.Invoke(m); }
                GD.Print($"live: lapenshard {m?.GetType().Name ?? "other mode"} ({Lapenshards.Count} worn)");
            }
            else if (game.Is(p, Inbound.CharacterAbility))
            {
                var m = CharacterAbilityMessage.Read(p.Reader());  // CCharacterAbilityManager::OnCharacterAbility via DispatchPacket
                var table = CharacterAbilityTable;
                if (m != null && CharacterAbility.Apply(m, id => table == null || table.GetAbility(id) != null))
                    CharacterAbilityReceived?.Invoke(m);
                GD.Print($"live: character ability {m?.GetType().Name ?? "other mode"} ({CharacterAbility.Ids.Count} learned)");
            }
            else if (HandleOwnSkill(p)) { }
            else if (HandleCombatEffect(p)) { }
            else if (game.Is(p, Inbound.RegionSkill))
            {
                // OnRegionSkill (scenestategame.cpp:7718-7749): mode 0 adds the region skill object of the id, mode 1 removes
                // it, mode 2 stops its loop effects. The ids serve mode 5's damage; the field's RegionSkills node draws the effects.
                var m = Ms2.Net.Packets.RegionSkillMessage.Read(p.Reader());
                if (m is Ms2.Net.Packets.RegionSkillMessage.Add add) regionSkills[m.Id] = (add.SkillId, add.Level);
                else if (m is Ms2.Net.Packets.RegionSkillMessage.Remove) regionSkills.Remove(m.Id);
                if (m != null) { RegionSkillLog.Add(m); RegionSkillReceived?.Invoke(m); }
                if (!LiveCheck) { }
                else if (m is Ms2.Net.Packets.RegionSkillMessage.Add ra) GD.Print($"live: region {ra.Id} add, skill {ra.SkillId} level {ra.Level}, owner {ra.OwnerId}, {ra.Points.Length} point(s), first {(ra.Points.Length > 0 ? ra.Points[0] : default)}");
                else if (m != null) GD.Print($"live: region {m.Id} {m.GetType().Name}");
            }
            else if (game.Is(p, Inbound.FieldProperty))
                FieldProperties.Apply(Ms2.Net.Packets.FieldPropertyMessage.Read(p.Reader(), game.Profile));   // OnFieldProperty (scenestategame.cpp:3895)
            else if (game.Is(p, Inbound.FieldEntrance))
                OnFieldEntrance(Ms2.Net.Packets.FieldEntrance.Read(p.Reader()));   // OnFieldEntrance (scenestategame.cpp:2366)
            else if (game.Is(p, Inbound.RoomDungeon))
                OnRoomDungeon(Ms2.Net.Packets.RoomDungeonReply.Read(p.Reader(), game.Profile));   // OnRoomDungeon (scenestategame.cpp:4531)
            else if (game.Is(p, Inbound.WorldShareInfo))
                ReadNotApplied("WorldShareInfo", Ms2.Net.Packets.WorldShareInfo.Read(p.Reader()).Mode);   // OnWorldShareInfo (scenestategame.cpp:9756): the boss-notify npc flag, the world map tile and the world share event dialog are not ported
            else if (game.Is(p, Inbound.BannerList))
                { Ms2.Net.Packets.BannerList.Read(p.Reader()); ReadNotApplied("BannerList", 0); }   // CAdBannerManagerCli::OnBannerList (adbannermanagercli.cpp:31): the ad banner manager and its consumers (cash shop, title) are not ported
            else if (game.Is(p, Inbound.ServerList)) { }   // the server list sent again after the field entry: CSceneStateGame::OnPacket has no case for it (only CSceneStateLogin::OnPacket reads it, scenestatelogin.cpp:117), so the client drops it unread
            else if (game.Is(p, Inbound.World)) { }   // CSceneStateGame::OnWorld (scenestategame.cpp:3883) reads no field: it sets WorldService m_2bc = 1 (worldservice_1.cpp:459), which no port code reads
            else if (game.Is(p, Inbound.Wedding))
                ReadNotApplied("Wedding", Ms2.Net.Packets.WeddingPacket.Read(p.Reader()).Mode);   // the wedding manager and its dialogs are not ported (the client reads the packet only with feature 0x104)
            else if (game.Is(p, Inbound.Survival))
                ReadNotApplied("Survival", Ms2.Net.Packets.SurvivalPacket.Read(p.Reader()).Mode);   // the survival season pass and medals; no client handler is named in the decompile and the survival UI is not ported
            else if (game.Is(p, Inbound.Mentoring))
                ReadNotApplied("Mentoring", Ms2.Net.Packets.MentoringPacket.Read(p.Reader()).Mode);   // CMentoringManager::OnPacket (mentoringmanager.cpp:255): the mentor and mentee lists, the dialogs and the notices; the mentoring manager is not ported
            else if (game.Is(p, Inbound.LegionBattle))
                ReadNotApplied("LegionBattle", Ms2.Net.Packets.LegionBattle.Read(p.Reader()).Mode);   // CSceneStateGame::OnLegionBattle (scenestategame.cpp:8480): fills the field table's guaranteed NPC spawn times; that table is not ported
            else if (game.Is(p, Inbound.InGameRank))
                ReadNotApplied("InGameRank", Ms2.Net.Packets.InGameRankAnswer.Read(p.Reader()).Mode);   // CGameRankingCli::OnPacket (gamerankingcli.cpp:350): sets the ranking answer lifetimes; the ranking client (boards, cache) is not ported
            else if (game.Is(p, Inbound.PremiumClub))
                OnPremiumClub(Ms2.Net.Packets.PremiumClub.Read(p.Reader(), game.Profile));   // OnPremiumClub (scenestategame.cpp:8426)
            else if (game.Is(p, Inbound.ResponsePet))
                ReadNotApplied("ResponsePet", Ms2.Net.Packets.PetResponse.Read(p.Reader(), game.Profile).Mode);   // CPetManager::OnResponsePet (petmanager.cpp:722): the pet system (summon, pet window, counts) is not ported
            else if (game.Is(p, Inbound.FunctionCube))
                OnFunctionCube(p);
            else if (game.Is(p, Inbound.FurnishingInventory))
                ReadNotApplied("FurnishingInventory", Ms2.Net.Packets.FurnishingInventory.Read(p.Reader(), game.Profile).Mode);   // OnFurnishingInventory (scenestategame.cpp:9373): the cube stock has no house editor here yet
            else if (game.Is(p, Inbound.FurnishingStorage))
                OnFurnishingStorage(Ms2.Net.Packets.FurnishingStorage.Read(p.Reader(), game.Profile));
            else if (game.Is(p, Inbound.Vibrate))
            {
                var v = Ms2.Net.Packets.Vibrate.Read(p.Reader(), game.Profile);   // OnVibrate (scenestategame.cpp:5981)
                var outcome = Vibrates.Apply(v, VibrateOffence(v), (uint)ServerTick);
                if (LogPackets) GD.Print($"live: Vibrate mode {v.Mode} '{v.Entity}' skill {v.SkillId}/{v.Level}: {outcome}");
            }
            else if (game.Is(p, Inbound.HideVibrate))
                Vibrates.Hide(Ms2.Net.Packets.VibrateName.Read(p.Reader()), (uint)ServerTick);   // OnHideVibrate (scenestategame.cpp:6018)
            else if (game.Is(p, Inbound.ShowVibrate))
                Vibrates.Show(Ms2.Net.Packets.VibrateName.Read(p.Reader()));   // OnShowVibrate (scenestategame.cpp:6026)
            else if (game.Is(p, Inbound.SkillDamage))
            {
                // The records that hit the own player; MasteryEvents applies the client's mode drops (3 needs its PC, 5 its
                // region skill object). One event per target record (MyPC::vfunc_113 posts each).
                var d = SkillDamage.Read(p.Reader(), game.Profile);
                // OnRegionSkillDamage's region hit effect, here in packet order: the remote layer's queue takes only damage
                // with targets, and is drained after the region's remove when both came in one read
                if (d.Mode == 5 && d.Raw == null)
                {
                    var hit = FieldEffects?.PlayHit(d.RegionId);
                    if (LiveCheck) GD.Print($"live: region {d.RegionId} damage, {d.Targets.Count} target(s), hit effect {hit ?? "no field"}");
                }
                // mode 3's caster test is PCBase (players, npcs and pets; the tracker keeps the first two, and a pet
                // is a PCBase in the client, so a pet's DoT on the own player cancels there but not here yet)
                foreach (var total in Ms2.Ui.Hud.MasteryEvents.SelfRecords(d, selfObject,
                             id => remote?.Tracker.HasObject(id) ?? (id == selfObject),
                             id => remote?.Tracker.HasObject(id) ?? (id == selfObject), regionSkills.ContainsKey))
                    SelfDamaged?.Invoke(total);
                ReactToHits(d);   // PC::vfunc_33 on the own PC (LiveSession.Hits.cs)
                // PCBase::vfunc_113 (RecordDamage) at the end of the hit object's vfunc_33/34/35, for every record
                BattleRecords.Record(d, objectInField ??= ObjectInField, objectInField, isRegionSkill ??= regionSkills.ContainsKey);
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
                if (LiveCheck) GD.Print($"live: mastery {m?.GetType().Name ?? "other mode"}");
            }
            else if (game.Is(p, Inbound.Fishing))
            {
                var m = FishingBook.Receive(p.Reader(), FishingFeature(), out var changed);   // feature 0xb, then modes 7 (the list) and 8 (a catch); the others are not the book's
                if (changed)
                    FishingReceived?.Invoke(m);
                if (LiveCheck) GD.Print($"live: fishing {m?.GetType().Name ?? "other mode"} ({FishingBook.Count} fish)");
            }
            else if (game.Is(p, Inbound.Breakable))
                Breakables.Apply(BreakableUpdate.Read(p.Reader()));     // OnBreakable (scenestategame.cpp:3667)
            else if (game.Is(p, Inbound.Liftable))
                Liftables.Apply(LiftableUpdate.Read(p.Reader()));       // OnLiftable (scenestategame.cpp:11941)
            else if (game.Is(p, Inbound.MassiveEvent))
                MassiveEventReceived?.Invoke(MassiveEventMessage.Read(p.Reader()));     // OnMassiveEvent (scenestategame.cpp:3858): the pane 0x4e4c reads it
            else if (game.Is(p, Inbound.RoomTimer))
                RoomTimerReceived?.Invoke(RoomTimerMessage.Read(p.Reader()));     // RoomTimerManager::Dispatch (scenestategame.cpp:1574)
            else if (game.Is(p, Inbound.MinimapPing))
                MinimapPinged?.Invoke(MinimapPingMessage.Read(p.Reader()));     // OnMinimapPing (scenestategame.cpp:6806)
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
                SkillPoints.Apply(SkillPointPacket.Read(p.Reader(), game.Profile));   // CSceneStateGame::OnSkillPoint (scenestategame.cpp:4241)
                SkillPointsChanged?.Invoke();
                GD.Print($"live: skill points rank 1 {SkillPoints.RankTotal(1)} rank 2 {SkillPoints.RankTotal(2)} extra {SkillPoints.Extra}");
            }
            else if (game.Is(p, Inbound.CheckCharNameResult))
            {
                var m = CheckCharNameResult.Read(p.Reader());
                GD.Print($"live: char name check: used {m.Used}, item {m.ItemUid}");
                CharNameChecked?.Invoke(m);
            }
            else if (game.Is(p, Inbound.SkillMacro))
            {
                var m = SkillMacroPacket.Read(p.Reader());      // CSceneStateGame::OnSkillMacro @0x141737d60
                GD.Print($"live: skill macro mode {m.Mode} ({m.Tabs.Count} tabs)");
                if (m.Mode == 2) CompactConfigs = m.Tabs;       // CSkillSystem::FUN_141767ae0
                else if (m.Mode == 0) SkillMacro?.Invoke(m);    // the open window's vfunc_79
            }
            else if (game.Is(p, Inbound.SkillBookTree))
            {
                var t = SkillBookTreePacket.Read(p.Reader());   // UISkillBookTreeDialog::vfunc_79 (uiskillbookautodistributedialog.cpp:1962)
                GD.Print($"live: skill book tree mode {t.Mode} ({t.Tabs.Count} tabs)");
                SkillBookTree?.Invoke(t);
            }
            else if (game.Is(p, Inbound.FieldPortal)) OnFieldPortal(FieldPortalMessage.Read(p.Reader()));
            else if (game.Is(p, Inbound.InteractObject)) OnInteractObject(InteractObjectMessage.Read(p.Reader()));
            else if (game.Is(p, Inbound.InteractNpc)) OnInteractNpc(InteractNpcMessage.Read(p.Reader()));
            else if (game.Is(p, Inbound.GameEvent))
            {
                var ge = GameEventMessage.Read(p.Reader());
                OnGameEvent(ge);
                ReactorRules.Apply(ge);   // GameEventManagerCli's reactor context (CGameRule)
            }
            else if (game.Is(p, Inbound.GameEventUserValue)) UserValues.Apply(GameEventUserValueMessage.Read(p.Reader()));   // FUN_140930fe0 @0x140930fe0
            else if (game.Is(p, Inbound.Attendance)) OnAttendance(AttendanceMessage.Read(p.Reader()));
            else if (game.Is(p, Inbound.Reactor)) OnReactor(ReactorMessage.Read(p.Reader()));
            else if (game.Is(p, Inbound.ResponseCube)) OnResponseCube(p);
            else if (game.Is(p, Inbound.LoadCubes)) OnLoadCubes(p);
            else if (game.Is(p, Inbound.LoadUgcMap)) OnLoadUgcMap(p);
            else if (game.Is(p, Inbound.SetCraftMode)) OnSetCraftMode(SetCraftMode.Read(p.Reader()));
            else if (game.Is(p, Inbound.Emote)) OnEmote(EmoteMessage.Read(p.Reader(), game.Profile));
            else if (game.Is(p, Inbound.ChatStamp)) OnChatStamp(ChatStampMessage.Read(p.Reader()));
            else if (game.Is(p, Inbound.GameToLogin)) OnGameToLogin(GameToLogin.Read(p.Reader()));
            else if (game.Is(p, Inbound.GameToGame)) OnGameToGame(GameToGame.Read(p.Reader()));
            else if (game.Is(p, Inbound.DynamicChannel)) OnDynamicChannel(DynamicChannelMessage.Read(p.Reader()));
            else if (game.Is(p, Inbound.Gvg)) OnGvg(p);
            else if (HandleSessionPacket(p)) { }
            else if (game.Is(p, Inbound.Reconnect) || game.Is(p, Inbound.ReconnectUnk) || game.Is(p, Inbound.FinalizeReconnect)) { }   // handled on the session's network loop
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
        if (LiveCheck) GD.Print($"live: profile picture object {pi.ObjectId} char {pi.CharacterId} '{key}'{(id == 0 ? " (no such PC)" : "")}");
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
        l.SendAsync(pk).ContinueWith(t => EngineCalls.Warn($"live: login send {pk.GetType().Name} failed: {t.Exception?.GetBaseException().Message}"),
            System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>CUgcManager's SendUGCMode* packets, while the game connection is up.</summary>
    public void SendUgc(Ms2.Net.Wire.IOutPacket packet)
    {
        if (game != null) Send(packet);
    }

    /// <summary>CPersonalInfoAgreement::SendAgreement (@0x141261dd0, gameoptionstruct.cpp:3445-3452).</summary>
    public void SendPersonalInfoAgreement(int group, int flags)
    {
        if (game != null) Send(new PersonalInfoAgreement(group, flags));
    }

    /// <summary>MyPC::SendHouseSetting's packet (mypc.cpp:4379-4384), while the game connection is up.</summary>
    public void SendHouseSetting(byte kind, string value, bool flag)
    {
        if (game != null) Send(new HouseSettingRequest(kind, value, flag));
    }

    /// <summary>UIHomeOptionDialog::SendRequestCubeMode42 / Mode43 (uihomeoptiondialog.cpp:254-275), the two packets of one permission row.</summary>
    public void SendHomeOptionRow(int row, bool on, int level)
    {
        if (game == null) return;
        Send(new HomeOptionRowSwitchRequest((byte) row, on));
        Send(new HomeOptionRowLevelRequest((byte) row, (byte) level));
    }

    /// <summary>UIHomeOptionDialog::SendRequestCubeMode49 (uihomeoptiondialog.cpp:276-283): the packet that sends the home's visitors out.</summary>
    public void SendHomeKickUsers()
    {
        if (game != null) Send(new HomeKickUsersRequest());
    }

    /// <summary>UIHomeInsideOptionDialog::FUN_140c280d0 (uihomeinsideoptiondialog.cpp:200-233): one of the three selections.</summary>
    public void SendHomeInsideOption(byte mode, byte index) { if (game != null) Send(new HomeInsideOptionRequest(mode, index)); }

    /// <summary>UIHomeMenuDialog::SendRequestCube (uihomemenudialog.cpp:759-776): the packet that demolishes the home.</summary>
    public void SendHomeDemolish()
    {
        if (game != null) Send(new HomeDemolishRequest());
    }

    /// <summary>UIHomeSaveSlotDialog's sends (unit_140fa9eb0_uihomesaveslotdialog.cpp): RequestCube 0x2e / 0x2f / 0x23 (design) and 0x40 / 0x41 (blueprint).</summary>
    public void SendHomeDesignSave(int slot, string name) { if (game != null) Send(new HomeDesignSaveRequest(slot, name)); }
    public void SendHomeDesignLoadDirect(int slot) { if (game != null) Send(new HomeDesignLoadDirectRequest(slot)); }
    public void SendHomeBlueprintSave(int slot, string name) { if (game != null) Send(new HomeBlueprintSaveRequest(slot, name)); }
    public void SendHomeBlueprintLoad(int slot) { if (game != null) Send(new HomeBlueprintLoadRequest(slot)); }

    /// <summary>CNetworkService::SendRequestQuit (@0x141538820, networkservice.cpp:501-505): flushes guide state (SendFunctionCube) and sends RequestQuit(0).</summary>
    public void SendRequestQuit()
    {
        SendGuideStates();
        if (game != null) Send(new RequestQuit(RequestQuit.SelectCharacter));
    }

    /// <summary>CNetworkService::SendRequestQuitAndDisconnect (@0x1415388a0, networkservice.cpp:507-512): flushes guide state, sends RequestQuit(1), and closes the active connection.
    /// The returned task completes when the packet is written and the connection closed, so the caller lets the process quit only then
    /// (the client's net thread writes the queued packet before the process ends).</summary>
    public System.Threading.Tasks.Task SendRequestQuitAndDisconnect()
    {
        System.Threading.Tasks.Task flushed = System.Threading.Tasks.Task.CompletedTask;
        SendGuideStates();
        var active = game ?? login;
        StopCombatEffects();   // before the player goes, so the blink's end restores its alpha (as OnGameToLogin)
        if (player != null && IsInstanceValid(player)) { player.Live = false; player.DeadFlag = false; }
        player = null;
        var g = game; game = null;
        var l = login; login = null;
        // The client queues the packet and marks the socket to disconnect (networkservice.cpp:507-512): the send is not awaited here;
        // the connection closes once the packet is written.
        if (active != null && !active.IsClosed)
        {
            try { flushed = active.SendAsync(new RequestQuit(RequestQuit.Disconnect)).ContinueWith(_ => active.Dispose(), System.Threading.Tasks.TaskScheduler.Default); }
            catch (Exception ex) { GD.PushWarning("live: RequestQuit send failed: " + ex.Message); active.Dispose(); }
        }
        if (g != null && g != active) g.Dispose();
        if (l != null && l != active) l.Dispose();
        return flushed;
    }

    /// <summary>Headless drive (MS2_LIVE_AUTO=1): the login button, then start with the first character.</summary>
    public static async Task AutoDrive(GameFlow flow)
    {
        if (System.Environment.GetEnvironmentVariable("MS2_LIVE_AUTO") != "1") return;
        var tree = flow.GetTree();
        async Task Frames(int n) { for (int i = 0; i < n; i++) await flow.ToSignal(tree, SceneTree.SignalName.ProcessFrame); }
        async Task Until(Func<bool> c, int max = 6000) { for (int i = 0; i < max && !c(); i++) await Frames(1); }
        await Until(() => tree.CurrentScene?.SceneFilePath == GameFlow.LoginScene); await Frames(30);
        if (!GameFlow.PassportStart) UiBridge.Raise(UILoginDialogContract.Movie, UILoginDialogContract.ToHost.OnConnect);   // the ticket's start connects by itself
        await Until(() => flow.Current == GameFlow.State.Lobby && tree.CurrentScene?.SceneFilePath == GameFlow.LobbyScene, 600); await Frames(30);
        if (flow.Current != GameFlow.State.Lobby) { GD.PushWarning("live: no character list"); tree.Quit(1); return; }
        string newName = System.Environment.GetEnvironmentVariable("MS2_LIVE_CREATE");   // create this character first (slice 6), then enter with it
        if (!string.IsNullOrEmpty(newName))
        {
            UiBridge.Raise(UICharacterListDialogContract.Movie, UICharacterListDialogContract.ToHost.OnCreatePC);
            await Until(() => flow.Current == GameFlow.State.Create && tree.CurrentScene?.SceneFilePath == GameFlow.CreateScene, 600); await Frames(120);
            int before = Session.List().Count;
            // MS2_LIVE_CREATE_JOB=<job code>: that job's tab first (the page opens on a random job)
            if (int.TryParse(System.Environment.GetEnvironmentVariable("MS2_LIVE_CREATE_JOB"), out int createJob))
            {
                GD.Print($"live: create job {createJob} tab: {(Ms2Godot.Ui.Create.CharacterCreateScene.PickJobTabScripted(createJob) ? "clicked" : "none")}");
                await Frames(90);
            }
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
            // MS2_LIVE_DELETE_NAME: that character instead of the last
            int at = Session.List().FindIndex(c => c.User.Name == System.Environment.GetEnvironmentVariable("MS2_LIVE_DELETE_NAME"));
            Session.SelectedIndex = at >= 0 ? at : n0 - 1;
            UiBridge.Raise(UICharacterListDialogContract.Movie, UICharacterListDialogContract.ToHost.OnDeletePC);
            await Frames(30);   // the confirmation box: the checkbox, the character's name, OK
            MessageBoxLayer.Instance?.Tick(0);
            MessageBoxLayer.Instance?.Type(Session.List()[Session.SelectedIndex].User.Name);
            await Frames(30);
            MessageBoxLayer.Instance?.Click(0);
            await Until(() => Session.List().Count < n0, 600); await Frames(60);
            bool gone = Session.List().Count == n0 - 1 && flow.Current == GameFlow.State.Lobby;
            GD.Print(gone ? $"live: delete run done, {n0} -> {Session.List().Count}" : "live: delete run failed");
            tree.Quit(gone ? 0 : 1); return;
        }
        // MS2_LIVE_CHAR: enter with this character name, "@last" the last row, "@selected" the row the list selected
        // (the saved CharSN, uicharacterlistdialog.cpp:1365); default the first
        string pick = System.Environment.GetEnvironmentVariable("MS2_LIVE_CHAR");
        int dialogRow = -1;
        if (pick == "@selected")   // as a player: Start once the dialog takes input (after the select scene's fade-in)
        {
            await Until(() => tree.CurrentScene is Ui.CharacterListScene { Controller: { Host.Entity: not null } d } && d.InputOn, 1800);
            if (tree.CurrentScene is Ui.CharacterListScene { Controller: { Host.Entity: not null } dl }) dialogRow = dl.ListIndex;
        }
        var listed = Session.List();
        var selectedRow = Session.SelectedIndex >= 0 && Session.SelectedIndex < listed.Count ? listed[Session.SelectedIndex] : null;
        GD.Print($"live: the list selected row {Session.SelectedIndex} of {listed.Count} (dialog row {dialogRow}), character {selectedRow?.User.CharacterId}; saved CharSN {GameOptions.Current.ConnectOption.CharSN}");
        var first = newName != null && newName != "" ? listed.Last()
            : pick == "@last" ? listed.LastOrDefault() : pick == "@selected" ? selectedRow
            : !string.IsNullOrEmpty(pick) ? listed.FirstOrDefault(c => c.User.Name == pick) : listed.FirstOrDefault();
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
        await CombatRun(flow, Frames, async (c, max) => await Until(c, max));   // MS2_LIVE_COMBAT_STEP (LiveSession.Combat.cs)
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
        // MS2_LIVE_BM_SHOTS=<dir> with MS2_LIVE_BM="sell:<item id>:<price>" or "buy": the black market opened as its open
        // answer does. sell registers one of that item twice (the register page's OnClickRegister, as the slot and the
        // price box send it, then the boxes' OK) and cancels the second from its row's cancel button; buy searches from the
        // search page's button and buys the first row. A shot after each step as bm_<step>.png.
        string bmShots = System.Environment.GetEnvironmentVariable("MS2_LIVE_BM_SHOTS");
        string bm = System.Environment.GetEnvironmentVariable("MS2_LIVE_BM");
        if (!string.IsNullOrEmpty(bmShots) && !string.IsNullOrEmpty(bm) && tree.CurrentScene is Ui.HudScene bmHud)
        {
            DirAccess.MakeDirRecursiveAbsolute(bmShots);
            void Shot(string name) { flow.GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(bmShots, name)); GD.Print("live: shot " + name); }
            async Task Boxes()
            {
                for (int i = 0; i < 3 && (MessageBoxLayer.Instance?.Shown ?? false); i++)
                {
                    Shot($"bm_box_{System.DateTime.Now:HHmmss}_{i}.png");
                    MessageBoxLayer.Instance.Tick(0);
                    await Frames(20);
                    MessageBoxLayer.Instance.Click(0);
                    await Frames(90);
                }
            }
            await Frames(240);
            GD.Print("live: bm open: " + bmHud.OpenBlackMarketScripted());
            await Until(() => bmHud.BlackMarketDialog != null, 600); await Frames(180);
            Shot("bm_open.png");
            var book = Instance?.BlackMarket;
            var parts = bm.Split(':');
            if (parts[0] == "sell" && parts.Length == 3 && book != null)
            {
                int itemId = int.Parse(parts[1]); long price = long.Parse(parts[2]);
                GD.Print("live: bm tab register: " + bmHud.ClickBlackMarket("m_buttonBar", "tab:1"));
                await Frames(120);
                for (int n = 0; n < 2; n++)
                {
                    var item = Instance.Items.All.FirstOrDefault(i => i.Id == itemId);
                    if (item == null) { GD.Print("live: bm no item " + itemId); break; }
                    int before = book.OwnProducts.Count;
                    bmHud.BlackMarketDialog?.OnCallback("OnClickRegister", new object[] { item.Uid.ToString(), price.ToString(), 1 });
                    await Frames(30);
                    await Boxes();
                    await Until(() => book.OwnProducts.Count > before, 60 * 8); await Frames(60);
                    GD.Print($"live: bm register {n}: {before} -> {book.OwnProducts.Count} own");
                    Shot($"bm_register_{n}.png");
                }
                int had = book.OwnProducts.Count;
                var cancel = new[] { "find:m_cancelSell" };
                GD.Print("live: bm cancel click: " + bmHud.ClickBlackMarket(cancel));
                await Frames(30);
                await Boxes();
                await Until(() => book.OwnProducts.Count < had, 60 * 8); await Frames(60);
                GD.Print($"live: bm cancel: {had} -> {book.OwnProducts.Count} own");
                Shot("bm_cancel.png");
            }
            else if (parts[0] == "buy" && book != null)
            {
                GD.Print("live: bm search click: " + bmHud.ClickBlackMarket("m_searchPage", "find:m_search"));
                await Until(() => book.SearchProducts.Count > 0, 60 * 8); await Frames(90);
                GD.Print($"live: bm search: {book.SearchProducts.Count} rows");
                Shot("bm_search.png");
                GD.Print("live: bm buy click: " + bmHud.ClickBlackMarket("m_searchPage", "find:m_buy"));
                await Frames(30);
                await Boxes();
                await Frames(60 * 4);
                Shot("bm_buy.png");
            }
            await Frames(120);
        }

        // MS2_LIVE_CHAT="line|line|...": each line sent from the first chat window, waiting for a chat line to come back (at
        // most 5 s) and 2 s more; "~N" waits N s instead. With MS2_LIVE_CHAT_SHOTS=<dir> the screen is shot after each as
        // chat_<n>.png; "@chatup:N" first scrolls the log N lines up from the bottom. "@warp:<map>", "@skillapply", "@lapenunmount:N", "@statplus:N", "@boxclose",
        // "@statusoptions" and "@optionsclose" press the field limit checks' buttons and open the status options (below).
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
                else if (line.StartsWith("@chatup:") && int.TryParse(line.AsSpan(8), out int up)) { chatHud.ScrollChatUp(up); await Frames(30); }   // "@chatup:N": the first chat window's log N lines up from the bottom
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
                else if (line == "@groupinvite") { GD.Print($"live: group invite dialog {n}: {chatHud.OpenGroupInviteDialog()}"); await Frames(60); }   // "@groupinvite": the current group window's invite button
                else if (line.StartsWith("@invite:") && Instance != null)
                {
                    // "@invite:<name>": the name typed into the open invite dialog and Enter; waits for a group chat packet (at most 5 s)
                    int before = Instance.groupChatPackets;
                    GD.Print($"live: group invite {n}: {chatHud.InviteFromGroupDialog(line.Substring(8))}");
                    await Until(() => Instance == null || Instance.groupChatPackets > before, 60 * 5);
                    GD.Print($"live: group invite {n}: {(Instance != null && Instance.groupChatPackets > before ? "a packet came" : "nothing came")}");
                    await Frames(120);
                }
                else if (line.StartsWith("@group:") && Instance != null)
                {
                    // "@group:<text>": the text sent from the current group window; waits for a group chat packet (at most 5 s)
                    int before = Instance.groupChatPackets;
                    GD.Print($"live: group chat line {n}: {(chatHud.SubmitGroupLine(line.Substring(7)) ? "sent" : "not sent")}");
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
                else if ((line == "@skillapply" || line.StartsWith("@lapenunmount:")) && tree.CurrentScene is Ui.HudScene lh)
                {
                    // "@skillapply": the skill book (opened first) and its apply button; "@lapenunmount:<slot>": the lapenshard
                    // window (opened first) and its equip page's unmount of that slot. The HUD is the current one (a field move
                    // makes it again). Logs the field's limit content and the box the press showed; the shot keeps the box up.
                    bool skill = line == "@skillapply";
                    bool open = skill ? lh.OpenSkillBookScripted() : lh.OpenLapenshardScripted();
                    await Frames(300);
                    var shown = Ms2.Ui.Hud.MessageBoxQueue.Current();
                    bool pressed = skill ? lh.PressSkillApplyScripted() : int.TryParse(line.AsSpan(14), out int slot) && lh.UnmountLapenshardScripted(slot);
                    await Frames(90);
                    var box = Ms2.Ui.Hud.MessageBoxQueue.Current();
                    GD.Print($"live: {(skill ? "skill apply" : "lapenshard unmount")} {n}: field {Instance?.FieldId}, limitContent 0x{Instance?.CurrentFieldData()?.LimitContent ?? 0:x}, "
                             + $"open {open}, pressed {pressed}, box {(box != null && box != shown ? "\"" + box.Msg + "\"" : "none")}");
                }
                else if (line.StartsWith("@statplus:") && int.TryParse(line.AsSpan(10), out int statRow) && tree.CurrentScene is Ui.HudScene sh)
                {
                    // "@statplus:<row>": the character window (opened first) and the plus button of that stat row (1 to 6); logs the
                    // field's limit content, the box the press showed and the own stat points before and after the server's answer
                    bool open = sh.OpenMyInfoScripted();
                    await Frames(300);
                    int stat = statRow >= 1 && statRow <= Ms2.Net.Packets.StatPointBook.CostIds.Length ? Ms2.Net.Packets.StatPointBook.CostIds[statRow - 1] : -1;
                    var before = sh.StatPointsScripted(stat);
                    var shown = Ms2.Ui.Hud.MessageBoxQueue.Current();
                    bool pressed = sh.PressStatPlusScripted(statRow);
                    await Frames(240);
                    var box = Ms2.Ui.Hud.MessageBoxQueue.Current();
                    var after = sh.StatPointsScripted(stat);
                    GD.Print($"live: stat plus {n}: field {Instance?.FieldId}, limitContent 0x{Instance?.CurrentFieldData()?.LimitContent ?? 0:x}, open {open}, "
                             + $"pressed {pressed}, box {(box != null && box != shown ? "\"" + box.Msg + "\"" : "none")}, stat 0x{stat:x} points "
                             + $"{before.Stat} -> {after.Stat}, remaining {before.Remaining} -> {after.Remaining}, used {before.Used} -> {after.Used}");
                }
                else if (line.StartsWith("@warp:") && tree.CurrentScene is Ui.HudScene wh)
                {
                    // "@warp:<map>": the GM warp line sent from the current HUD's chat window (the first one is gone after a field
                    // move); waits for the field's entry (at most 20 s)
                    int was = Instance?.FieldId ?? 0;
                    GD.Print($"live: warp {n}: /warp {line.Substring(6)} {(wh.SubmitChatLine("/warp " + line.Substring(6)) ? "sent" : "no chat window")}");
                    await Until(() => Instance == null || (Instance.FieldId ?? was) != was, 60 * 20);
                    await Frames(240);
                    GD.Print($"live: warp {n}: field {Instance?.FieldId}");
                }
                else if (line == "@boxclose")
                {
                    // "@boxclose": the shown message box taken off without an answer
                    if (Ms2.Ui.Hud.MessageBoxQueue.Current() is { } shownBox) Ms2.Ui.Hud.MessageBoxQueue.Remove(shownBox);
                    await Frames(60);
                }
                else if (line == "@statusoptions" && tree.CurrentScene is Ui.HudScene oh)
                {
                    // "@statusoptions": the options dialog on its status page; logs the pet check boxes (null: the movie has no such box)
                    int tab = oh.OpenStatusOptionsScripted();
                    for (int i = 0; i < 600 && !oh.StatusOptionsReadyScripted(); i++) await Frames(1);
                    await Frames(60);
                    GD.Print($"live: status options {n}: tab {tab}, ready {oh.StatusOptionsReadyScripted()}, m_displayHideMyPet "
                             + $"{oh.OptionSelectedScripted("m_displayHideMyPet")?.ToString() ?? "absent"}, m_displayHideOtherPet "
                             + $"{oh.OptionSelectedScripted("m_displayHideOtherPet")?.ToString() ?? "absent"}");
                }
                else if (line == "@optionsclose" && tree.CurrentScene is Ui.HudScene ch) { ch.CloseOptionsScripted(); await Frames(60); }
                else if (await CallChatStep(line, chatHud, n, Frames, Until)) { }   // the recall and air taxi lines (LiveSession.CallRuns.cs)
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
        // MS2_LIVE_EMOTE="step|step|...": "dialog" opens the dynamic action dialog; "use:<id>" clicks that emotion in it
        // ("use:first" the first valid learned one with a row, "use:idle" the first with an idleAnim too); "/<word>" types that line in the first chat window ("word:first"
        // the first reserved word of a valid emotion other than the one playing); "sticker"
        // opens the sticker dialog and sends its first usable sticker from the preview; "useitem:<id>" double clicks that bag
        // item and answers its box yes; "close" closes both dialogs; "~N" waits N s.
        // The own state and emotion are logged each half second for 6 s after a use; with MS2_LIVE_EMOTE_SHOTS=<dir> a shot after each
        // step as emote_<n>.png.
        await SoundRun(tree, Frames);   // MS2_LIVE_SOUND (LiveSession.SoundScript.cs)
        string emoteRun = System.Environment.GetEnvironmentVariable("MS2_LIVE_EMOTE");
        if (!string.IsNullOrEmpty(emoteRun) && tree.CurrentScene is Ui.HudScene eh)
        {
            string emoteShots = System.Environment.GetEnvironmentVariable("MS2_LIVE_EMOTE_SHOTS");
            if (!string.IsNullOrEmpty(emoteShots)) DirAccess.MakeDirRecursiveAbsolute(emoteShots);
            await Frames(240);
            GD.Print($"live: emote: {Instance?.Emotions.All.Count ?? 0} learned, {Instance?.ChatEmoticons.Groups.Count ?? 0} sticker groups");
            int n = 0;
            foreach (var step in emoteRun.Split('|'))
            {
                if (step.StartsWith('~') && int.TryParse(step.AsSpan(1), out int wait)) await Frames(60 * wait);
                else if (step == "dialog")
                {
                    eh.OpenEmotionDialogScripted();
                    await Frames(180);
                    GD.Print($"live: emote dialog {n}: {eh.EmotionDialogListed} listed");
                }
                else if (step.StartsWith("use:"))
                {
                    uint id = 0;
                    if (step == "use:first") id = eh.FirstValidEmotionScripted(false);
                    else if (step == "use:idle") id = eh.FirstValidEmotionScripted(true);
                    else uint.TryParse(step.AsSpan(4), out id);
                    GD.Print($"live: emote use {n}: {id} {(eh.SelectEmotionScripted(id) ? "clicked" : "dialog closed")}");
                    // the state each half second for 6 s (wall clock: the frame rate of a two-client run is low)
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    string seen = "";
                    for (int half = 1; half <= 12; half++)
                    {
                        while (clock.ElapsedMilliseconds < half * 500) await Frames(1);
                        var (st, em) = eh.OwnEmotionState();
                        seen += $" {half * 0.5:0.0}s:{st:x}/{em}";
                    }
                    GD.Print($"live: emote use {n}: state/emotion{seen}");
                }
                else if (step.StartsWith('/') || step == "word:first")
                {
                    string line = step;
                    if (step == "word:first") line = eh.FirstEmotionWordScripted((uint)eh.OwnEmotionState().Emotion) ?? "";
                    GD.Print($"live: emote word {n}: {line} {(line.Length > 0 && eh.SubmitChatLine(line) ? "typed" : "not typed")}");
                    await Frames(60);
                    var (st, em) = eh.OwnEmotionState();
                    GD.Print($"live: emote word {n}: after 1 s state {st:x} emotion {em}");
                }
                else if (step == "sticker" && Instance != null)
                {
                    bool back = false;
                    void Stamp(Ms2.Net.Packets.ChatMessage m, Func<Ms2.Net.Packets.WeddingChatHall> _) => back = true;
                    uint id = eh.OpenStickerDialogScripted();
                    await Frames(180);
                    Instance.UserChat += Stamp;
                    GD.Print($"live: emote sticker {n}: {id} {(id != 0 && eh.SendStickerScripted(id) ? "sent" : "not sent")}");
                    await Until(() => back, 60 * 5);
                    Instance.UserChat -= Stamp;
                    GD.Print($"live: emote sticker {n}: chat line {(back ? "came back" : "did not come back")}");
                    await Frames(60);
                }
                else if (step == "close") { eh.CloseEmoteDialogsScripted(); await Frames(60); }
                else if (step.StartsWith("key:"))
                {
                    // "key:<Godot key name>:<ms>": that key held down for the time (wall clock), then let go
                    var parts = step.Split(':');
                    if (Enum.TryParse<Key>(parts[1], true, out var key) && parts.Length > 2 && int.TryParse(parts[2], out int ms))
                    {
                        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
                        var held = System.Diagnostics.Stopwatch.StartNew();
                        while (held.ElapsedMilliseconds < ms) await Frames(1);
                        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
                        await Frames(30);
                        GD.Print($"live: key {n}: {key} held {held.ElapsedMilliseconds} ms");
                    }
                }
                else if (step.StartsWith("couple:"))
                {
                    // "couple:<name>[:<emotion id>]": a right press on that player's PC, the popup's couple action item,
                    // then the couple dialog's onSelectEmotion with the emotion (the first valid couple one by default)
                    var parts = step.Split(':');
                    string who = parts[1];
                    await Until(() => Instance != null && Instance.FieldPcs.Any(p => p.Name == who), 60 * 90);
                    System.Collections.Generic.IReadOnlyList<int> items = null;
                    for (int i = 0; i < 20 && (items = eh.RightClickPcScripted(who)) == null; i++) await Frames(30);
                    GD.Print($"live: couple popup {n}: {(items == null ? "no menu" : string.Join(",", items.Select(x => $"0x{x:x}")))}");
                    if (items != null && !string.IsNullOrEmpty(emoteShots))
                    {
                        await Frames(60);
                        flow.GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(emoteShots, $"emote_{n}_popup.png"));
                    }
                    eh.ClickPopupItemScripted(Ms2.Ui.Hud.PopupMenuController.CoupleAction);
                    await Frames(180);
                    GD.Print($"live: couple dialog {n}: {eh.CoupleDialogListed} listed");
                    if (!string.IsNullOrEmpty(emoteShots))
                        flow.GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(emoteShots, $"emote_{n}_dialog.png"));
                    uint id = parts.Length > 2 && uint.TryParse(parts[2], out var given) ? given : eh.FirstCoupleEmotionScripted();
                    GD.Print($"live: couple use {n}: {id} {(id != 0 && eh.SelectCoupleEmotionScripted(id) ? "clicked" : "not clicked")}");
                    await Frames(30);
                    GD.Print($"live: couple use {n}: {eh.CoupleStatesScripted(who)}");
                }
                else if (step.StartsWith("coupleanswer"))
                {
                    // "coupleanswer[:no]": the couple invite box (tag coupleEmotion) waited for (at most 300 s), shot and answered
                    var asked = System.Diagnostics.Stopwatch.StartNew();   // wall clock: the asker's own steps come first
                    while (asked.Elapsed.TotalSeconds < 300 && Ms2.Ui.Hud.MessageBoxQueue.Current()?.Extra != "coupleEmotion") await Frames(10);
                    bool boxed = Ms2.Ui.Hud.MessageBoxQueue.Current()?.Extra == "coupleEmotion";
                    GD.Print($"live: couple box {n}: {(boxed ? "shown" : "never shown")}");
                    if (boxed)
                    {
                        await Frames(120);
                        if (!string.IsNullOrEmpty(emoteShots))
                            flow.GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(emoteShots, $"emote_{n}_box.png"));
                        MessageBoxLayer.Instance?.Click(step.EndsWith(":no") ? 1 : 0);
                        await Frames(30);
                        GD.Print($"live: couple box {n}: {(step.EndsWith(":no") ? "no" : "yes")} clicked, box {(MessageBoxLayer.Instance?.Shown == true ? "still shown" : "closed")}");
                    }
                }
                else if (step.StartsWith("partyask:") && Instance is { } pa)
                {
                    // "partyask:<name>": once that player is in the field, the chat link's party invite, then the join waited for
                    string who = step[9..];
                    await Until(() => pa.FieldPcs.Any(p => p.Name == who), 60 * 120);
                    await Frames(120);
                    eh.PartyLinkClick("inviteParty," + who);
                    await Until(() => pa.Party.IsInParty && pa.Party.Members.Count > 1, 60 * 60);
                    GD.Print($"live: emote party {n}: {who} invited, in a party {pa.Party.IsInParty} with {pa.Party.Members.Count}");
                }
                else if (step == "attack")
                {
                    // "attack": the attack key's skill cast once (the server puts the caster in battle and relays the stance)
                    GD.Print($"live: emote attack {n}: {(eh.AttackScripted() ? "cast" : "no cast")}");
                    await Frames(30);
                }
                else if (step == "partyanswer" && Instance is { } pb)
                {
                    // "partyanswer": the party invite box answered yes, then the join waited for
                    await Until(() => MessageBoxLayer.Instance?.Shown == true, 60 * 180);
                    bool shown = MessageBoxLayer.Instance?.Shown == true;
                    if (shown) { await Frames(60); MessageBoxLayer.Instance?.Click(0); }
                    await Until(() => pb.Party.IsInParty && pb.Party.Members.Count > 1, 60 * 60);
                    GD.Print($"live: emote party {n}: invite box {(shown ? "answered yes" : "never shown")}, in a party {pb.Party.IsInParty} with {pb.Party.Members.Count}");
                }
                else if (step.StartsWith("watchpc:") && Instance is { } pw)
                {
                    // "watchpc:<name>:<s>": that player's state, emotion and head offsets each half second, a shot each 2 s
                    var parts = step.Split(':');
                    int secs = parts.Length > 2 && int.TryParse(parts[2], out var w) ? w : 10;
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    for (int half = 1; half <= secs * 2; half++)
                    {
                        while (clock.ElapsedMilliseconds < half * 500) await Frames(1);
                        int obj = pw.FieldPcs.FirstOrDefault(p => p.Name == parts[1]).ObjectId;
                        GD.Print($"live: watch {n} {half * 0.5:0.0}s: {parts[1]} {(obj != 0 ? pw.RemoteWatchLine(obj) : "not in the field")}");
                        if (half % 4 == 0 && !string.IsNullOrEmpty(emoteShots))
                            flow.GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(emoteShots, $"watch_{n}_{half / 2:000}s.png"));
                    }
                }
                else if (step.StartsWith("couplewatch:"))
                {
                    // "couplewatch:<name>:<s>": both couple states each half second, a shot each 2 s
                    var parts = step.Split(':');
                    int secs = parts.Length > 2 && int.TryParse(parts[2], out var w) ? w : 10;
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    for (int half = 1; half <= secs * 2; half++)
                    {
                        while (clock.ElapsedMilliseconds < half * 500) await Frames(1);
                        GD.Print($"live: couple watch {n} {half * 0.5:0.0}s: {eh.CoupleStatesScripted(parts[1])}");
                        if (half % 4 == 0 && !string.IsNullOrEmpty(emoteShots))
                            flow.GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(emoteShots, $"emote_{n}_{half / 2:00}s.png"));
                    }
                }
                else if (step.StartsWith("useitem:") && int.TryParse(step.AsSpan(8), out int itemId))
                {
                    // "useitem:<item id>": that item double clicked in the bag; a box that comes up is shot and answered yes
                    long uid = 0;
                    for (int i = 0; i < 300 && (uid = eh.UseItemScripted(itemId)) <= 0; i++)
                    {
                        if (i % 30 == 0) GD.Print($"live: emote useitem {n}: try {i}: {eh.UseItemWhy}");
                        await Frames(uid < 0 ? 10 : 60);
                    }
                    GD.Print($"live: emote useitem {n}: {itemId} {(uid > 0 ? $"double clicked ({uid})" : "not in the bag")}: {eh.UseItemWhy}");
                    await Until(() => MessageBoxLayer.Instance?.Shown == true, 60 * 3);
                    bool boxed = MessageBoxLayer.Instance?.Shown == true;
                    GD.Print($"live: emote useitem {n}: box {(boxed ? "shown" : "not shown")}");
                    if (boxed) await Frames(120);                       // the box movie loads before its buttons take a click
                    if (boxed && !string.IsNullOrEmpty(emoteShots))
                        flow.GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(emoteShots, $"emote_{n}_box.png"));
                    int groups = Instance?.ChatEmoticons.Groups.Count ?? 0, learned = Instance?.Emotions.All.Count ?? 0;
                    if (boxed && Instance?.Inventory is { } bag)
                    {
                        var send = bag.RequestUse;
                        string sent = "no use request";
                        bag.RequestUse = (u, arg) => { sent = $"use request {u} \"{arg}\" sent"; send?.Invoke(u, arg); };
                        MessageBoxLayer.Instance.Click(0);
                        await Frames(30);
                        bag.RequestUse = send;
                        GD.Print($"live: emote useitem {n}: yes clicked, box {(MessageBoxLayer.Instance?.Shown == true ? "still shown" : "closed")}, {sent}");
                    }
                    await Until(() => (Instance?.ChatEmoticons.Groups.Count ?? 0) > groups, 60 * 5);
                    GD.Print($"live: emote useitem {n}: sticker groups {groups} -> {Instance?.ChatEmoticons.Groups.Count ?? 0}, "
                        + $"emotions {learned} -> {Instance?.Emotions.All.Count ?? 0}");
                    await Frames(60);
                }
                if (!string.IsNullOrEmpty(emoteShots))
                {
                    flow.GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(emoteShots, $"emote_{n}.png"));
                    GD.Print($"live: shot emote_{n}.png");
                }
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
        // MS2_LIVE_PARTY: "ask:<name>" or "answer", on two clients in one field (MS2_LIVE_MOVES warps both first); shots in
        // MS2_LIVE_PARTY_SHOTS. Round one: the asker clicks the chat link "inviteParty,<name>", the answerer accepts the invite
        // box; both windows open; the asker starts a ready check and both answer its box; the asker hands over the lead; the new
        // leader kicks the asker. Round two: the asker invites again, the answerer accepts, the asker leaves. The party book
        // (members, leader) is logged at each step on both sides.
        string partyRun = System.Environment.GetEnvironmentVariable("MS2_LIVE_PARTY");
        if (!string.IsNullOrEmpty(partyRun) && Instance is { } pl && tree.CurrentScene is Ui.HudScene ph)
        {
            var pp = partyRun.Split(':');
            bool typedChat = pp[0] is "askchat" or "answerchat";               // the party chat run: typed party lines in both directions
            bool frames = pp[0] is "askframes" or "answerframes";       // the member frames run: levels, deaths and a quit seen in the window
            bool voteRun = pp[0] is "askvote" or "answervote";                   // the vote kick run: four clients
            bool searchRun = pp[0] is "asksearch" or "answersearch";            // the party search run: two clients
            bool joinLink = pp[0] == "joinlink";                                 // the dungeon join links run: one client
            bool dpsRun = pp[0] is "askdps" or "answerdps";                     // the party damage meter run: two clients
            bool dungeonRun = pp[0] == "dungeonroom";                            // the dungeon room window run: one client
            bool asker = pp[0] is "ask" or "askframes" or "askchat" or "askvote" or "asksearch" or "askdps";
            string partner = asker && pp.Length > 1 ? pp[1] : "";
            string role = asker ? "A" : "B";
            string partyShots = System.Environment.GetEnvironmentVariable("MS2_LIVE_PARTY_SHOTS");
            if (!string.IsNullOrEmpty(partyShots)) DirAccess.MakeDirRecursiveAbsolute(partyShots);
            void PShot(string name)
            {
                GD.Print($"live: party {role}: {name}: {ph.PartyText()}");
                if (!string.IsNullOrEmpty(partyShots))
                    flow.GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(partyShots, $"{role}_{name}.png"));
            }
            async Task<bool> Box(string what, int seconds)
            {
                await Until(() => MessageBoxLayer.Instance?.Shown == true, 60 * seconds);
                bool shown = MessageBoxLayer.Instance?.Shown == true;
                GD.Print($"live: party {role}: {what} box {(shown ? "shown" : "never shown")}");
                if (!shown) return false;
                await Frames(60);
                PShot(what + "_box");
                MessageBoxLayer.Instance?.Click(0);
                await Frames(60);
                return true;
            }
            async Task<bool> Joined(bool joined, int seconds)
            {
                await Until(() => pl.Party.IsInParty == joined && (!joined || pl.Party.Members.Count > 1), 60 * seconds);
                await Frames(120);
                return pl.Party.IsInParty == joined;
            }
            long Other() => pl.Party.Members.FirstOrDefault(m => m.CharacterId != pl.CharacterId)?.CharacterId ?? 0;
            await Frames(240);
            // a new character's opening cut scene movie covers the field: the HUD's Esc skips it, as in the movie scene's own run
            await Until(() => pl.Movie.IsPlaying, 60 * 20);
            if (pl.Movie.IsPlaying)
            {
                ph.Escape();
                GD.Print($"live: party {role}: opening movie {pl.Movie.CutSceneId} skipped");
                await Until(() => !pl.Movie.IsPlaying, 60 * 15);
                await Frames(120);
            }
            if (searchRun)
            {
                // MS2_LIVE_PARTY "asksearch" / "answersearch": A (alone) opens the search window, registers a listing, opens the register
                // window on it (view and remove only) and removes the listing while B's list still shows it (B's join then gets the server's
                // error and a refresh); then A lists again and B joins through the list. B opens the search window, refreshes until the
                // listing shows, whispers and right clicks its chief. Each step is logged with the party book and shot.
                const string title1 = "Search listing one", title2 = "Search listing two";
                await Until(() => ph.PartySearchOpenScripted(), 60 * 10);
                await Frames(60 * 2);
                PShot("opened");
                if (asker)
                {
                    // round one: the listing is removed while B's list still shows it (B's join then gets the server's error)
                    GD.Print($"live: party A: register: {ph.PartySearchRegisterScripted(title1, true, false)}");
                    await Frames(60 * 3);
                    PShot("register_sent");
                    await Until(() => !pl.Party.Recruitment.IsEmpty, 60 * 30);
                    await Frames(60 * 2);
                    GD.Print($"live: party A: listing {pl.Party.Recruitment.Title} party {pl.Party.IsInParty}");
                    PShot("listed");
                    await Frames(60 * 40);
                    GD.Print($"live: party A: modify window: {ph.PartySearchRegisterScripted("", false, true)}: {ph.PartySearchRegisterText()}");
                    await Frames(60 * 3);
                    PShot("modify_window");
                    GD.Print($"live: party A: remove: {ph.PartySearchRemoveScripted()}");
                    await Frames(60);
                    PShot("remove_box");
                    MessageBoxLayer.Instance?.Click(0);
                    await Until(() => pl.Party.Recruitment.IsEmpty, 60 * 20);
                    await Frames(60 * 3);
                    PShot("removed");
                    GD.Print($"live: party A: listing {(pl.Party.Recruitment.IsEmpty ? "gone" : pl.Party.Recruitment.Title)}");
                    // round two: a new listing, B joins through the list
                    await Frames(60 * 30);
                    GD.Print($"live: party A: register again: {ph.PartySearchRegisterScripted(title2, true, false)}");
                    await Until(() => !pl.Party.Recruitment.IsEmpty, 60 * 30);
                    await Frames(60 * 2);
                    PShot("listed_again");
                    await Until(() => pl.Party.IsInParty && pl.Party.Members.Count > 1, 60 * 240);
                    await Frames(60 * 3);
                    PShot("joined");
                    await Frames(60 * 10);
                    PShot("after");
                }
                else
                {
                    async Task<bool> Seen(string title)
                    {
                        bool seen = false;
                        for (int i = 0; i < 80 && !seen; i++)
                        {
                            ph.PartySearchRefreshScripted();
                            await Frames(60 * 3);
                            seen = pl.Party.Results.Any(r => r.Title == title);
                        }
                        return seen;
                    }
                    GD.Print($"live: party B: listing seen {await Seen(title1)}");
                    PShot("list");
                    GD.Print($"live: party B: whisper {ph.PartySearchRowScripted(title1, "whisper")}");
                    await Frames(60 * 2);
                    PShot("whisper");
                    GD.Print($"live: party B: chief menu {ph.PartySearchRowScripted(title1, "chief")}");
                    await Frames(60 * 2);
                    PShot("chief_menu");
                    await Frames(60 * 45);
                    // the listing is gone on the server, the row still shows: the join is answered with a party result message
                    PShot("stale_list");
                    GD.Print($"live: party B: stale join {ph.PartySearchRowScripted(title1, "join")}");
                    await Frames(60 * 5);
                    PShot("stale_join");
                    GD.Print($"live: party B: after the stale join: {ph.PartySearchText()}");
                    GD.Print($"live: party B: listing two seen {await Seen(title2)}");
                    PShot("list_two");
                    GD.Print($"live: party B: join {ph.PartySearchRowScripted(title2, "join")}");
                    await Frames(60 * 4);
                    PShot("join_sent");
                    await Box("join", 5);
                    bool joined = await Joined(true, 40);
                    GD.Print($"live: party B: joined {joined}, window {ph.PartyWindowShown}");
                    PShot("party");
                    await Frames(60 * 10);
                    PShot("after");
                }
            }
            else if (dpsRun)
            {
                // MS2_LIVE_PARTY "askdps:<name>" / "answerdps": after the invite and the join both clients open the party damage meter
                // (Ctrl+Q's toggle), each spawns a mob (MS2_LIVE_COMBAT_MOB) and attacks it until it dies (the combat run's attack step);
                // the ranking, the group's timer and each mode of the meter are logged and shot while the other's hits arrive.
                if (asker)
                {
                    UserBlock who = null;
                    await Until(() => (who = pl.FieldUsers().FirstOrDefault(u => u.Name == partner)) != null, 60 * 120);
                    await Frames(120);
                    ph.PartyLinkClick("inviteParty," + partner);
                }
                else await Box("invite", 120);
                bool joined = await Joined(true, 60);
                GD.Print($"live: party {role}: joined {joined}");
                await Frames(60 * 3);
                async Task PressKeyOf(Godot.Key key, bool ctrl, KeyLocation where = KeyLocation.Unspecified)
                {
                    foreach (bool down in new[] { true, false })
                    {
                        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = down, CtrlPressed = ctrl && down, Location = where });
                        await Frames(4);
                    }
                }
                // Ctrl+Q as the keyboard gives it: the left Ctrl down, Q down and up under it, the Ctrl up
                Input.ParseInputEvent(new InputEventKey { Keycode = Godot.Key.Ctrl, PhysicalKeycode = Godot.Key.Ctrl, Pressed = true, CtrlPressed = true, Location = KeyLocation.Left });
                await Frames(4);
                await PressKeyOf(Godot.Key.Q, true);
                Input.ParseInputEvent(new InputEventKey { Keycode = Godot.Key.Ctrl, PhysicalKeycode = Godot.Key.Ctrl, Pressed = false, Location = KeyLocation.Left });
                await Frames(60);
                GD.Print($"live: party {role}: Ctrl+Q opened the meter: {ph.PartyDpsText().StartsWith("open True")}");
                if (!ph.PartyDpsText().StartsWith("open True")) GD.Print($"live: party {role}: dps toggle (scripted): {ph.PartyDpsToggleScripted()}");
                await Frames(60 * 3);
                GD.Print($"live: party {role}: dps before: {ph.PartyDpsText()}");
                PShot("dps_open");
                System.Environment.SetEnvironmentVariable("MS2_LIVE_COMBAT_STEP", "attack");
                System.Environment.SetEnvironmentVariable("MS2_LIVE_COMBAT_SHOTS", System.IO.Path.Combine(partyShots ?? ".", "combat" + role));
                await CombatRun(flow, Frames, async (c, max) => await Until(c, max));
                await Frames(60 * 4);
                GD.Print($"live: party {role}: dps after the attack: {ph.PartyDpsText()}");
                PShot("dps_after");
                for (int mode = 1; mode <= 7; mode++)
                {
                    GD.Print($"live: party {role}: next mode {ph.PartyDpsNextModeScripted()}");
                    await Frames(60 * 2);
                    GD.Print($"live: party {role}: dps mode {mode % 7}: {ph.PartyDpsText()}");
                    PShot("dps_mode" + (mode % 7));
                }
                await Frames(60 * 20);                                   // the other client's attacks arrive meanwhile
                GD.Print($"live: party {role}: dps at the end: {ph.PartyDpsText()}");
                PShot("dps_end");
                await PressKeyOf(Godot.Key.Escape, false);
                await Frames(60 * 2);
                GD.Print($"live: party {role}: Esc closed the meter: {!ph.PartyDpsText().StartsWith("open True")}");
                PShot("dps_closed");
            }
            else if (typedChat)
            {
                // MS2_LIVE_PARTY "askchat:<name>" / "answerchat": after the invite and the join the asker selects the party target with
                // the party command and types a line, the answerer answers with one; the asker then leaves and types again: no party,
                // so the client sends nothing and shows notice 0x335 (SendChat's party case).
                string partyCmd = System.Environment.GetEnvironmentVariable("MS2_LIVE_PARTY_CMD") ?? "/p";
                if (asker)
                {
                    UserBlock who = null;
                    await Until(() => (who = pl.FieldUsers().FirstOrDefault(u => u.Name == partner)) != null, 60 * 120);
                    await Frames(120);
                    ph.PartyLinkClick("inviteParty," + partner);
                    bool joined = await Joined(true, 60);
                    GD.Print($"live: party A: in party {joined}");
                    await Frames(60 * 3);
                    GD.Print($"live: party A: select {partyCmd}: {ph.SubmitChatLine(partyCmd)}");
                    await Frames(60);
                    GD.Print($"live: party A: line sent: {ph.SubmitChatLine("party line one from the asker")}");
                    await Frames(60 * 3);
                    PShot("chat_sent");
                    await Frames(60 * 8);
                    PShot("chat_both");
                    pl.SendParty(new Ms2.Net.Packets.PartyLeaveRequest());
                    await Joined(false, 30);
                    GD.Print($"live: party A: left, in party {pl.Party.IsInParty}");
                    GD.Print($"live: party A: line outside a party: {ph.SubmitChatLine("party line outside")}");
                    await Frames(60 * 3);
                    PShot("chat_no_party");
                }
                else
                {
                    await Box("invite", 120);
                    bool joined = await Joined(true, 60);
                    GD.Print($"live: party B: joined {joined}");
                    await Frames(60 * 8);
                    PShot("chat_recv");
                    GD.Print($"live: party B: select {partyCmd}: {ph.SubmitChatLine(partyCmd)}");
                    await Frames(60);
                    GD.Print($"live: party B: line sent: {ph.SubmitChatLine("party line two from the answerer")}");
                    await Frames(60 * 4);
                    PShot("chat_both");
                    await Until(() => !pl.Party.IsInParty, 60 * 30);
                    await Frames(60 * 3);
                    PShot("chat_after_leave");
                }
            }
            else if (voteRun)
            {
                // MS2_LIVE_PARTY "askvote:<b>:<c>:<d>" (the leader) and "answervote:yes|no|target" (b and c vote, d is the one voted
                // out): the leader invites the three, the four are in one party; the leader picks "vote to kick" on d's slot menu
                // and answers the confirm box; b and c see the vote page and answer; the kick follows the votes (server's rule) and
                // d's window closes. Each step is logged with the party book and shot.
                string voteMode = pp.Length > 1 ? pp[1] : "";
                if (asker)
                {
                    string[] names = pp.Skip(1).ToArray();
                    foreach (var n in names)
                    {
                        UserBlock who = null;
                        await Until(() => (who = pl.FieldUsers().FirstOrDefault(u => u.Name == n)) != null, 60 * 180);
                        GD.Print($"live: party A: {n} {(who != null ? "is in the field" : "never came")}");
                    }
                    await Frames(120);
                    for (int i = 0; i < names.Length; i++)
                    {
                        ph.PartyLinkClick("inviteParty," + names[i]);
                        await Until(() => pl.Party.IsInParty && pl.Party.Members.Count == i + 2, 60 * 90);
                        GD.Print($"live: party A: invited {names[i]}, members {pl.Party.Members.Count}");
                        await Frames(60 * 3);
                    }
                    PShot("four");
                    long target = pl.Party.FindByName(names[^1])?.CharacterId ?? 0;
                    GD.Print($"live: party A: slot menu of {names[^1]}: {ph.PartySlotMenuOpen(target)}");
                    await Frames(60 * 3);
                    PShot("slot_menu");
                    GD.Print($"live: party A: vote to kick picked: {ph.PartySlotMenuPick(Ms2.Ui.Hud.PopupMenuController.VoteToKick)}");
                    await Box("kick_confirm", 20);
                    await Until(() => pl.Party.Vote != null, 60 * 20);
                    await Frames(60 * 3);
                    GD.Print($"live: party A: vote running {pl.Party.Vote != null}");
                    PShot("vote_started");
                    await Until(() => pl.Party.Vote != null && pl.Party.Vote.Yes.Count + pl.Party.Vote.No.Count >= 1, 60 * 90);
                    await Frames(60 * 2);
                    PShot("first_answer");
                    await Until(() => pl.Party.Members.Count < 4 || pl.Party.Vote == null, 60 * 90);
                    await Frames(60 * 3);
                    GD.Print($"live: party A: after the votes: members {pl.Party.Members.Count}, vote {(pl.Party.Vote != null ? "running" : "none")}");
                    PShot("vote_end");
                    await Frames(60 * 10);
                    PShot("vote_after");
                }
                else if (voteMode == "target")
                {
                    await Box("invite", 180);
                    bool joined = await Joined(true, 60);
                    GD.Print($"live: party D: joined {joined}, window {ph.PartyWindowShown}");
                    await Frames(60 * 3);
                    PShot("party");
                    await Until(() => pl.Party.Vote != null, 60 * 240);
                    await Frames(60 * 3);
                    GD.Print($"live: party D: a vote is running: {pl.Party.Vote != null}");
                    PShot("vote_seen");
                    await Until(() => !pl.Party.IsInParty, 60 * 240);
                    await Frames(60 * 3);
                    GD.Print($"live: party D: kicked, in party {pl.Party.IsInParty}, window {ph.PartyWindowShown}");
                    PShot("kicked");
                }
                else
                {
                    bool yes = voteMode == "yes";
                    await Box("invite", 180);
                    bool joined = await Joined(true, 60);
                    GD.Print($"live: party {role}: joined {joined}, window {ph.PartyWindowShown}");
                    await Until(() => pl.Party.Vote != null, 60 * 240);
                    await Frames(20);                                   // the vote runs out in seconds: answer at once
                    GD.Print($"live: party {role}: vote page: {pl.Party.Vote?.ShowsPage}");
                    PShot("vote_page");
                    await Frames(yes ? 10 : 40);
                    GD.Print($"live: party {role}: answer {(yes ? "yes" : "no")}: {ph.PartyVoteAnswer(yes)}");
                    await Frames(60 * 3);
                    PShot("answered");
                    await Until(() => pl.Party.Vote == null || pl.Party.Members.Count < 4, 60 * 120);
                    await Frames(60 * 3);
                    PShot("vote_end");
                }
            }
            else if (dungeonRun)
            {
                // MS2_LIVE_PARTY "dungeonroom:<room>:<portal id>": the open key opens the window and shuts it; the party link
                // "dungeonRoom,<room>" opens it on the room; the portal types 0xc (mode 3) and 0x11 and the portal's leave open and
                // close it. The state is logged and shot at each step.
                uint dRoom = pp.Length > 1 && uint.TryParse(pp[1], out var dr) ? dr : 0;
                int dPortal = pp.Length > 2 && int.TryParse(pp[2], out var dp) ? dp : 7;
                async Task Step(string what, Action act)
                {
                    try { act(); }
                    catch (Exception e) { GD.Print($"live: party {role}: {what}: threw {e}"); }
                    await Frames(240);
                    GD.Print($"live: party {role}: {what}: {ph.DungeonRoomText()}");
                    PShot(what);
                }
                await Step("key_open", () => ph.DungeonRoomKey());
                await Step("key_close", () => ph.DungeonRoomKey());
                await Step("link_room", () => ph.PartyLinkClick($"dungeonRoom,{dRoom}"));
                await Step("leave_close", () => pl.LeavePortal());
                await Step("portal_mode3", () => pl.OpenDungeonRoomDialogWithMode?.Invoke(dPortal, 3));
                await Step("portal_room", () => pl.OpenDungeonRoomDialogRoom?.Invoke((int)dRoom));
                await Step("portal_type8", () => pl.OpenDungeonRoomDialog?.Invoke(dPortal));
                await Step("leave_close2", () => pl.LeavePortal());
            }
            else if (pp[0] == "dungeonopen")
            {
                // MS2_LIVE_PARTY "dungeonopen:<room>": the three openers that are not portals, one shot and one log line each (the line
                // carries the window's group): the world map's dungeon icon on the room, a click on a type 8 portal mark of the minimap
                // (the field must have one; MS2_LIVE_MOVES warps there first) and the guild window's dungeon button.
                uint oRoom = pp.Length > 1 && uint.TryParse(pp[1], out var orr) ? orr : 0;
                async Task Open(string what, Func<string> act)
                {
                    string note = "";
                    try { note = act() ?? ""; }
                    catch (Exception e) { GD.Print($"live: party {role}: {what}: threw {e}"); }
                    GD.Print($"live: party {role}: {what}: {note} -> at once {ph.DungeonRoomText()}");   // before any frame: a server may drop the session
                    await Frames(4);
                    PShot(what + "_early");
                    await Frames(56);
                    GD.Print($"live: party {role}: {what}: {note} -> {ph.DungeonRoomText()}");
                    PShot(what);
                    await Frames(180);
                    GD.Print($"live: party {role}: {what}: 3 s later -> {ph.DungeonRoomText()}");
                }
                string Field() => $"field {pl.FieldId}";
                int wantField = int.TryParse(System.Environment.GetEnvironmentVariable("MS2_LIVE_DUNGEON_FIELD"), out var wf) ? wf : 0;
                if (wantField != 0) { await Until(() => pl.FieldId == wantField, 60 * 60); await Frames(300); }   // the warp has landed and the minimap is built
                GD.Print($"live: party {role}: dungeonopen start {Field()}");
                await Open("worldmap_icon", () => ph.WorldMapDungeonIcon(oRoom) ? "icon clicked" : "world map did not open");
                ph.CloseWorldMapLive();
                await Open("minimap_type8", () => ph.MinimapType8Click());
                ph.DungeonRoomKey();                                   // closes the window the click opened
                await Frames(60);
                await Open("guild_button", () => ph.GuildDungeonButton() ? "button pressed" : "guild window did not open");
            }
            else if (joinLink)
            {
                // MS2_LIVE_PARTY "joinlink:<helper|member>:<room>": one client clicks the chat link "joinPartyDungeonHelper" or
                // "joinPartyDungeonMember" for the room (party 1, a leader that is not the player) as the messenger's hyperLinkLeft runs
                // it. The notice or the yes / no box it gives is shot; the own record of the room is logged first. With
                // MS2_LIVE_PARTY_CMD set to a link text, that text is clicked instead.
                uint linkRoom = pp.Length > 2 && uint.TryParse(pp[2], out var lr) ? lr : 0;
                var rec = pl.DungeonRecordOf(linkRoom);
                GD.Print($"live: party {role}: records {pl.DungeonRecordCount}, room {linkRoom}: " +
                    (rec == null ? "no record" : $"total clears {rec.TotalClears} flags {rec.Flags}"));
                string link = System.Environment.GetEnvironmentVariable("MS2_LIVE_PARTY_CMD")
                    ?? $"{(pp.Length > 1 && pp[1] == "member" ? "joinPartyDungeonMember" : "joinPartyDungeonHelper")},1,Nobody,{linkRoom}";
                ph.PartyLinkClick(link);
                await Frames(30);
                bool boxUp = MessageBoxLayer.Instance?.Shown == true;
                GD.Print($"live: party {role}: link {link}: box {(boxUp ? "shown" : "not shown")}");
                await Frames(60);
                PShot(boxUp ? "link_box" : "link_notice");
                if (boxUp) { MessageBoxLayer.Instance?.Click(1); await Frames(60); }
            }
            else if (frames)
            {
                // MS2_LIVE_PARTY "askframes:<name>" / "answerframes": the asker invites and the answerer accepts (no ready check, no kick);
                // then the asker sets its level (GM command: the own Level status event -> UpdateMemberLevel), kills the answerer and
                // itself (server's mode 0x13 for the member, the own Hp event for the asker) and watches the answerer's frame go
                // offline when the answerer quits: each step is logged with the party book and shot.
                if (asker)
                {
                    UserBlock who = null;
                    await Until(() => (who = pl.FieldUsers().FirstOrDefault(u => u.Name == partner)) != null, 60 * 120);
                    GD.Print($"live: party A: {partner} {(who != null ? "is in the field" : "never came")}");
                    await Frames(120);
                    ph.PartyLinkClick("inviteParty," + partner);
                    bool joined = await Joined(true, 60);
                    GD.Print($"live: party A: invite answered, in party {joined}, window {ph.PartyWindowShown}");
                    await Frames(60 * 3);
                    PShot("party");
                    string own = System.Environment.GetEnvironmentVariable("MS2_LIVE_CHAR") ?? "";
                    short level0 = pl.Party.Find(pl.CharacterId)?.User.Level ?? 0;
                    ph.SubmitChatLine("/player level " + (level0 + 7));
                    await Until(() => (pl.Party.Find(pl.CharacterId)?.User.Level ?? 0) != level0, 60 * 10);
                    await Frames(60 * 2);
                    PShot("own_level");
                    ph.SubmitChatLine("/kill player " + partner);
                    await Until(() => Other() != 0 && pl.Party.Find(Other()).User.TombType != 0, 60 * 10);
                    await Frames(60 * 2);
                    PShot("other_dead");
                    ph.SubmitChatLine("/kill player " + own);
                    await Until(() => (pl.Party.Find(pl.CharacterId)?.User.Hp ?? 1) == 0, 60 * 10);
                    await Frames(60 * 2);
                    PShot("own_dead");
                    await Until(() => pl.Party.Members.Any(m => m.Offline), 60 * 120);
                    await Frames(60 * 2);
                    GD.Print($"live: party A: a member went offline: {pl.Party.Members.Any(m => m.Offline)}");
                    PShot("other_offline");
                }
                else
                {
                    await Box("invite", 120);
                    bool joined = await Joined(true, 60);
                    GD.Print($"live: party B: joined {joined}, window {ph.PartyWindowShown}");
                    await Frames(60 * 3);
                    PShot("party");
                    await Frames(60 * 20);
                    PShot("after_kills");
                    await Frames(60 * 10);
                    PShot("before_quit");
                    GD.Print("live: party B: quitting");
                }
            }
            else if (asker)
            {
                UserBlock who = null;
                await Until(() => (who = pl.FieldUsers().FirstOrDefault(u => u.Name == partner)) != null, 60 * 120);
                GD.Print($"live: party A: {partner} {(who != null ? "is in the field" : "never came")}");
                await Frames(120);
                PShot("before");
                ph.PartyLinkClick("inviteParty," + partner);
                bool joined = await Joined(true, 60);
                GD.Print($"live: party A: invite answered, in party {joined}, window {ph.PartyWindowShown}");
                PShot("party");
                // the ready check: the popup menu's entry sends this; each side answers the box the start opens
                pl.SendParty(new Ms2.Net.Packets.PartyReadyCheckRequest());
                await Box("ready", 20);
                await Frames(60 * 4);
                PShot("ready_marks");
                await Frames(60 * 12);
                PShot("ready_end");
                // the lead goes to the answerer
                long other = Other();
                pl.SendParty(new Ms2.Net.Packets.PartyLeaderChangeRequest(partner));
                await Until(() => pl.Party.LeaderId == other, 60 * 20);
                GD.Print($"live: party A: leader is now {pl.Party.LeaderId} (answerer {other})");
                await Frames(60);
                PShot("leader_changed");
                await Until(() => !pl.Party.IsInParty, 60 * 60);
                await Frames(120);
                GD.Print($"live: party A: after the kick: in party {pl.Party.IsInParty}, window {ph.PartyWindowShown}");
                PShot("kicked");
                // round two: invite again and leave
                await Frames(60 * 5);
                ph.PartyLinkClick("inviteParty," + partner);
                joined = await Joined(true, 60);
                GD.Print($"live: party A: second invite, in party {joined}");
                PShot("party2");
                await Frames(60 * 3);
                pl.SendParty(new Ms2.Net.Packets.PartyLeaveRequest());
                await Joined(false, 30);
                GD.Print($"live: party A: left, in party {pl.Party.IsInParty}, window {ph.PartyWindowShown}");
                PShot("left");
            }
            else
            {
                await Box("invite", 120);
                bool joined = await Joined(true, 60);
                GD.Print($"live: party B: joined {joined}, window {ph.PartyWindowShown}");
                PShot("party");
                await Box("ready", 40);
                await Frames(60 * 4);
                PShot("ready_marks");
                await Frames(60 * 12);
                PShot("ready_end");
                long self = pl.CharacterId;
                await Until(() => pl.Party.LeaderId == self, 60 * 40);
                GD.Print($"live: party B: leader is now {pl.Party.LeaderId} (self {self})");
                await Frames(60);
                PShot("leader_changed");
                long other = Other();
                GD.Print($"live: party B: kicking {other}");
                pl.SendParty(new Ms2.Net.Packets.PartyKickRequest(other));
                await Frames(60 * 3);
                PShot("kicked_other");
                await Box("invite", 120);
                joined = await Joined(true, 60);
                GD.Print($"live: party B: second join {joined}");
                PShot("party2");
                await Until(() => pl.Party.Members.Count < 2 || !pl.Party.IsInParty, 60 * 60);
                await Frames(120);
                PShot("after_leave");
            }
        }
        await Frames(60 * hold);
        bool ok = flow.Current == GameFlow.State.Game;
        GD.Print(ok ? "live: auto run done" : "live: auto run failed");
        // the end of the run quits as the system menu does (GameFlow.QuitNow): RequestQuit(1) is written, and the connection closed, before the process ends,
        // so the server frees the session; a stuck socket holds the end for a second
        if (ok && Instance?.SendRequestQuitAndDisconnect() is { } flushed)
            await System.Threading.Tasks.Task.WhenAny(flushed, System.Threading.Tasks.Task.Delay(1000));
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
