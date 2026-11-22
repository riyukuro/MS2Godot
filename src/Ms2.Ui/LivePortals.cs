using System.Xml.Linq;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Net.Wire;
using Ms2.Ui.Hud;

namespace Ms2.Ui;

/// <summary>What the live portals read from the game and how they answer: the local player's state, the field table, the
/// quest log, the packet sink and the boxes.</summary>
public interface IPortalHost
{
    /// <summary>WorldService::GetLocalPlayer() != null (the MyPC).</summary>
    bool HasPlayer { get; }
    /// <summary>MyPC::IsInCubeMode.</summary>
    bool InCubeMode { get; }
    /// <summary>CGuideManager::IsLimitActive (IsRestricted) of a limit kind.</summary>
    bool IsRestricted(int kind);
    /// <summary>WorldService::GetFieldId (and DAT_143ab2870, the map RequestMoveField mode 0 sends): the current field.</summary>
    int FieldId { get; }
    /// <summary>GetServerTime: the server's clock in seconds (the portal locks).</summary>
    long ServerTime { get; }
    /// <summary>CFieldDataCli::Find: the field's row, null when the table has none.</summary>
    FieldData Field(int id);
    /// <summary>CFieldDataCli::GetShadowContinent of a field's continent code.</summary>
    bool IsShadowContinent(int continentCode);
    /// <summary>CQuestManager::IsQuestRewarded.</summary>
    bool IsQuestRewarded(int questId);
    /// <summary>GetQuestName.</summary>
    string QuestName(int questId);
    void Send(IOutPacket packet);
    /// <summary>FUN_140d73bc0(id, type, listener, tag): a box of common string <paramref name="commonId"/>. A box with a tag is
    /// answered through <see cref="PortalAction.OnMoveBox"/> (the MyPC listener); a box without one has no listener.</summary>
    void ShowCommonBox(int commonId, int type, string tag);
    /// <summary>A type 10 box of common string <paramref name="commonId"/> with a text input (SetInput(5, "", 6, 0, 0)):
    /// <paramref name="answer"/> gets the button and the typed text.</summary>
    void ShowInputBox(int commonId, Action<int, string> answer);
    /// <summary>ShowSystemNotice(text, 1, 1).</summary>
    void SystemNotice(string text);
    /// <summary>GetMessageBoxLayer()->UpdateErrorMessage(GetStringCommon(id)): the error line of the box already open.</summary>
    void UpdateBoxError(int commonId);
    /// <summary>ShowMessageBox(text, 0, 0, ""): a plain box without a listener.</summary>
    void ShowTextBox(string text);
    /// <summary>A client path that is not ported: the portal sends nothing.</summary>
    void StillOpen(string what);
}

/// <summary>
/// One registered portal (CPortal, portal.cpp) and the entity it shows through: the packet's fields (OnFieldPortal mode 0,
/// scenestategame.cpp:7227) and the entity properties the client keeps (shown 0xa0b6b179, glow 0x14969c28, minimap
/// 0xe084c8de, target 0xa162d321, Position 0xfd95ca52, Rotation 0x68c7c29d, PortalDimension 0xfb190620, PortalOffset 0x1e1de032).
/// </summary>
public sealed class LivePortal
{
    public int Id;
    /// <summary>m_nType.</summary>
    public byte Type;
    /// <summary>m_148: the packet's target map (0 when the packet sent 0); entity prop 0xa162d321.</summary>
    public int TargetMap;
    /// <summary>m_14c: the proxy's object id.</summary>
    public int ObjectId;
    /// <summary>m_150: the action kind (entity prop 0x1b8b1161, set only for spawned types 4 and 0xb).</summary>
    public int ActionKind;
    /// <summary>The entity has the action kind prop (spawned types 4 / 0xb); null otherwise.</summary>
    public int? Kind;
    /// <summary>m_158: the home's owner.</summary>
    public long Owner;
    /// <summary>m_nEndTime, m_nEventTime (the start tick: the portal stays closed until the server tick passes it), m_188.</summary>
    public int EndTick, EventTime;
    public ushort Extra;
    /// <summary>m_b18c: entering asks for a password.</summary>
    public bool Password;
    public string Model = "", OwnerName = "", Feature = "", Locale = "";

    /// <summary>The bound xblock entity (types other than the spawned ones), null for a spawned portal.</summary>
    public FieldEntity Entity;
    /// <summary>The portal has an entity to show through (a spawned one, or a bound xblock entity); without one the flag
    /// setters do nothing and PortalAction skips it.</summary>
    public bool HasEntity;
    /// <summary>The entity's IsHomePrivilege prop (0xe725ea17, default False in the Portal flat models): a house portal that
    /// needs the house privilege.</summary>
    public bool HomePrivilege;
    /// <summary>The entity's own quest mask (FUN_14169b650, MaskQuestID / MaskQuestState), empty for a spawned portal.</summary>
    public QuestMaskTable.Mask OwnMask = new();
    public bool Shown, Glow, Minimap;

    // the entity's pose props and the box built from them (Unk_PortalBox::FUN_14169b080)
    public Vec3 Position, Rotation, Dimension, Offset;
    public bool HasBox;
    public Vec3 Center, Half;

    /// <summary>FUN_14169b080: half = PortalDimension * 0.5, posed at Position turned by the Z rotation, PortalOffset added
    /// unrotated.</summary>
    public void BuildBox()
    {
        HasBox = true;
        Half = Dimension * 0.5;
        Center = Position + Offset;
    }

    /// <summary>Unk_PortalBox::ContainsPoint (MS2 axes); no box contains nothing.</summary>
    public bool Contains(Vec3 p)
    {
        if (!HasBox) return false;
        double a = -Rotation.Z * Math.PI / 180;
        double dx = p.X - Center.X, dy = p.Y - Center.Y;
        double lx = dx * Math.Cos(a) - dy * Math.Sin(a), ly = dx * Math.Sin(a) + dy * Math.Cos(a);
        return Math.Abs(lx) <= Half.X && Math.Abs(ly) <= Half.Y && Math.Abs(p.Z - Center.Z) <= Half.Z;
    }
}

/// <summary>
/// The field's portals as the server registers them (packet 0x48, CSceneStateGame::OnFieldPortal, and
/// WorldService::RegisterPortal @worldservice_1.cpp:9024): the client knows a portal only from the server's list, binds it to
/// its own xblock entity or spawns one, and keeps its shown and glow flags from the packet, the quest masks and the start tick.
/// </summary>
public sealed class LivePortals
{
    readonly Dictionary<int, LivePortal> byId = new();
    readonly List<LivePortal> order = new();
    readonly int fieldId;
    readonly QuestMaskTable masks;
    readonly Func<string, string, bool> available;
    readonly Func<int, QuestMaskTable.QuestStatus> quest;
    IReadOnlyList<FieldEntity> entities;
    readonly List<FieldPortalMessage> pending = new();

    /// <param name="available">FUN_14169c330: the portal's feature is on and its locale list holds the client's
    /// (<see cref="LobbyFeatures.IsEnabledByName"/> and <see cref="LobbyFeatures.IsLocaleInList"/>).</param>
    /// <param name="quest">The player's quest standing for the masks.</param>
    public LivePortals(int fieldId, QuestMaskTable masks, Func<string, string, bool> available, Func<int, QuestMaskTable.QuestStatus> quest)
    {
        this.fieldId = fieldId;
        this.masks = masks;
        this.available = available ?? ((_, _) => true);
        this.quest = quest ?? QuestMaskTable.NoQuests;
    }

    public static LivePortals ForPacks(PackSet packs, int fieldId, QuestMaskTable masks, Func<int, QuestMaskTable.QuestStatus> quest) =>
        new(fieldId, masks, (f, l) => LobbyFeatures.IsEnabledByName(packs, f) && LobbyFeatures.IsLocaleInList(l), quest);

    public int FieldId => fieldId;
    public IReadOnlyList<LivePortal> All => order;
    public LivePortal ById(int id) => byId.GetValueOrDefault(id);
    /// <summary>The field's entities are attached (the portal messages before it are held, as the client's field is built
    /// before it reads the field's packets).</summary>
    public bool Attached => entities != null;

    /// <summary>The built field's entities; the held messages are applied in order.</summary>
    public void Attach(IReadOnlyList<FieldEntity> fieldEntities)
    {
        entities = fieldEntities ?? Array.Empty<FieldEntity>();
        foreach (var m in pending) Apply(m);
        pending.Clear();
    }

    /// <summary>OnFieldPortal by mode; another mode does nothing.</summary>
    public void Apply(FieldPortalMessage m)
    {
        if (entities == null) { pending.Add(m); return; }
        switch (m.Mode)
        {
            case 0: Register(m); break;
            case 1: Remove(m.Id); break;
            case 2: if (byId.TryGetValue(m.Id, out var p)) SetFlags(p, m.Visible, m.Enabled, m.Minimap, m.Extra); break;
            case 3: if (byId.TryGetValue(m.Id, out var q)) Move(q, ToVec(m.Position), ToVec(m.Rotation)); break;
        }
    }

    static Vec3 ToVec(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>Spawned types (RegisterPortal: "Portal_Type_A" for type 4, "MS2RoomEnterPortal" for the others).</summary>
    public static bool IsSpawned(byte type) => type is 3 or 4 or 5 or 6 or 7 or 10 or 0xb or 0x10;

    /// <summary>
    /// WorldService::RegisterPortal: an id already registered is dropped. A spawned type gets its pose, glow, shown,
    /// dimension (and for types 4 / 0xb the action kind) from the packet; another type binds the xblock entity with the Portal
    /// mixin (0x12cd8b5) and the same PortalID, and keeps that entity's pose. With an entity: minimap, then the target map prop
    /// (the packet's, also for a bound entity); the box; SetGlow(enabled), SetShown(visible); then the quest mask
    /// (FUN_14169d0e0). A portal no entity binds to is not registered (worldservice_1.cpp:9024). The client also aborts when
    /// the bound entity has no PortalID; a Portal-mixin entity always has one, so that cannot happen here.
    /// STILL-OPEN: the spawned templates' own PortalOffset default (taken as 0) and the proxy (m_14c).
    /// </summary>
    void Register(FieldPortalMessage m)
    {
        if (byId.ContainsKey(m.Id)) return;
        var p = new LivePortal
        {
            Id = m.Id, Type = m.Type, ObjectId = m.ObjectId, ActionKind = m.ActionKind, Owner = m.Owner, EndTick = m.EndTick,
            EventTime = m.StartTick, Extra = m.Extra, Password = m.Password, Model = m.Model ?? "", OwnerName = m.OwnerName ?? "",
            Feature = m.Feature ?? "", Locale = m.Locale ?? "",
        };
        if (m.TargetMap != 0) p.TargetMap = m.TargetMap;
        if (IsSpawned(m.Type))
        {
            p.HasEntity = true;
            p.Position = ToVec(m.Position); p.Rotation = ToVec(m.Rotation); p.Dimension = ToVec(m.Dimension);
            p.Glow = m.Enabled; p.Shown = m.Visible;
            if (m.Type is 4 or 0xb) p.Kind = m.ActionKind;
        }
        else
        {
            var e = entities.FirstOrDefault(x => x.HasMixin("Portal") && (int)x.Number("PortalID") == m.Id);
            if (e != null)
            {
                p.Entity = e; p.HasEntity = true;
                p.Position = e.Position; p.Rotation = e.RotationDeg;
                // the Portal mixin's PortalDimension default (the flat model's), as FieldPortals reads it
                p.Dimension = e.Vector("PortalDimension", new Vec3(200, 200, 250));
                p.Offset = e.Vector("PortalOffset");
                p.Glow = e.Bool("PortalEnable", true); p.Shown = e.Bool("IsVisible", true);
                p.HomePrivilege = e.Bool("IsHomePrivilege", false);
                p.OwnMask = QuestMaskTable.FromEntity(e.Get("MaskQuestID"), e.Get("MaskQuestState"));
            }
        }
        // no entity bound: the client returns 0 before m_350.SetAt (worldservice_1.cpp:9098), so the id is not registered
        // and a later add of it is taken
        if (!p.HasEntity) return;
        p.Minimap = m.Minimap;
        p.BuildBox();
        byId[p.Id] = p; order.Add(p);
        SetGlow(p, m.Enabled);
        SetShown(p, m.Visible);
        ApplyMask(p);
    }

    public void Remove(int id)
    {
        if (byId.Remove(id, out var p)) order.Remove(p);
    }

    bool Available(LivePortal p) => available(p.Feature, p.Locale);

    void SetShown(LivePortal p, bool shown) { if (p.HasEntity) p.Shown = shown && Available(p); }
    void SetGlow(LivePortal p, bool glow) { if (p.HasEntity) p.Glow = glow && Available(p); }

    /// <summary>CPortal::SetShownAndGlow: both forced off when the portal is not available.</summary>
    void SetShownAndGlow(LivePortal p, bool shown, bool glow)
    {
        if (!p.HasEntity) return;
        bool on = Available(p);
        p.Shown = shown && on; p.Glow = glow && on;
    }

    /// <summary>OnFieldPortal mode 2 (SetFieldObjectFlags, CPortal::FUN_14169ce50): shown, glow and minimap from the packet
    /// (all off when not available); the masks are not run again.</summary>
    void SetFlags(LivePortal p, bool shown, bool glow, bool minimap, ushort extra)
    {
        p.Extra = extra;
        if (!p.HasEntity) return;
        bool on = Available(p);
        p.Shown = shown && on; p.Glow = glow && on; p.Minimap = minimap && on;
    }

    /// <summary>OnFieldPortal mode 3 (CPortal::FUN_14169cc70): the entity's Position and Rotation, then the box again.</summary>
    void Move(LivePortal p, Vec3 pos, Vec3 rot)
    {
        if (!p.HasEntity) return;
        p.Position = pos; p.Rotation = rot;
        p.BuildBox();
    }

    /// <summary>CPortal::FUN_14169d0e0: a portal with a quest mask is shown and glows only when the mask passes; field
    /// 2000499's portal 2 is always shown.</summary>
    void ApplyMask(LivePortal p)
    {
        if (masks != null && masks.HasPortalMask(fieldId, p.Id, p.OwnMask))
        {
            bool ok = masks.CheckPortal(fieldId, p.Id, p.OwnMask, quest);
            SetShownAndGlow(p, ok, ok);
        }
        if (fieldId == 0x1e8673 && p.Id == 2) SetShownAndGlow(p, true, true);
    }

    /// <summary>WorldService FUN_141bd3880: every portal's mask again (the quest manager's dirty flag,
    /// questmanager_2.cpp:1182 / 1417).</summary>
    public void RefreshMasks()
    {
        foreach (var p in order) ApplyMask(p);
    }

    /// <summary>CPortal's frame update (portal.cpp:281): while the start tick is set, the portal is closed until the server
    /// tick passes it, then opened once.</summary>
    public void Tick(uint serverTick)
    {
        foreach (var p in order)
        {
            if (p.EventTime == 0) continue;
            if (serverTick > (uint)p.EventTime) { p.EventTime = 0; SetShownAndGlow(p, true, true); }
            else SetShownAndGlow(p, false, false);
        }
    }

    /// <summary>WorldService FUN_141bc2c90: the portals whose box holds the point, nearest first by Position.</summary>
    public IEnumerable<LivePortal> At(Vec3 point) =>
        order.Where(p => p.Contains(point)).OrderBy(p => Dist2(p.Position, point));

    static double Dist2(Vec3 a, Vec3 b) { var d = a - b; return d.X * d.X + d.Y * d.Y + d.Z * d.Z; }
}

/// <summary>Unk_MyPCPortalLockMap (DAT_143ab3288): the home password locks by (field, a, b, c).</summary>
public sealed class PortalLocks
{
    public sealed class Lock { public int Tries; public long Until; }
    readonly Dictionary<(int, int, int, long), Lock> locks = new();

    public Lock Find(int map, int a, int b, long c) => locks.GetValueOrDefault((map, a, b, c));
    public Lock GetOrAdd(int map, int a, int b, long c)
    {
        if (!locks.TryGetValue((map, a, b, c), out var l)) locks[(map, a, b, c)] = l = new Lock();
        return l;
    }
    public void Remove(int map, int a, int b, long c) => locks.Remove((map, a, b, c));
}

/// <summary>
/// PortalAction (actionkeyobject.cpp:845-1210) and the MyPC sends behind it (mypc.cpp): which portal the action key uses,
/// what each portal type does, the checks before RequestMoveField mode 0, and the boxes that answer them. A type whose path
/// is not ported sends nothing (<see cref="IPortalHost.StillOpen"/>); nothing falls back to mode 0.
/// </summary>
public sealed class PortalAction
{
    readonly IPortalHost host;
    public readonly PortalLocks Locks = new();
    public LivePortals Portals;

    /// <summary>DAT_143ab288c: the portal used last; standing in it again does nothing until the player has left every portal.</summary>
    public int LastUsed;
    /// <summary>m_spEntity / m_spPortal: the portal <see cref="Poll"/> picked.</summary>
    public LivePortal Current;

    /// <summary>MyPC::m_kPortalMove (OnEnterUgcMap mode 3): the home portal waiting for its password.</summary>
    public EnterUgcMapMessage PendingMove;

    public PortalAction(IPortalHost host) => this.host = host;

    /// <summary>
    /// PortalAction::vfunc_1: no player, cube mode or the guide's limit 9 gives none; of the portals holding the player's
    /// point, the first with glow. When it is the one used last, the action is offered but nothing is picked (a press does
    /// nothing). With none at the point the last-used latch is cleared.
    /// </summary>
    public bool Poll(Vec3 playerPos)
    {
        if (!host.HasPlayer || host.InCubeMode || host.IsRestricted(9)) return false;
        Current = null;
        if (Portals == null) { LastUsed = 0; return false; }
        bool any = false;
        foreach (var p in Portals.At(playerPos))
        {
            any = true;
            if (!p.HasEntity) continue;
            if (!p.Glow) continue;
            if (LastUsed == p.Id) return true;
            Current = p;
            return true;
        }
        if (!any) LastUsed = 0;
        return false;
    }

    /// <summary>
    /// PortalAction::vfunc_4 (the action object's auto step when it is picked): an action kind of 1 enters at once (unless the
    /// house service's timer holds it, STILL-OPEN: not ported, taken as not holding); another kind waits for the key. The
    /// kind is known only for the spawned types 4 and 0xb; a bound portal's kind prop is not read (STILL-OPEN), so it waits for
    /// the key. Returns whether the action stays on the key.
    /// </summary>
    public bool Accept()
    {
        if (Current == null) return false;
        if (Current.Kind == 1) { Use(); return false; }
        return true;
    }

    /// <summary>
    /// PortalAction::vfunc_2 (the action key, actionkeyobject.cpp:905): the portal becomes the last used; a house portal
    /// (entity prop 0xe725ea17, IsHomePrivilege) without the house privilege shows box 0x656 and stops, before the owner and
    /// type branches (actionkeyobject.cpp:918-925); then by its owner and type.
    /// STILL-OPEN: the privilege itself (CHomePrivilege::FUN_14050d6f0, homeprivilege.cpp:63: the own house's map is this
    /// field and its UGC group's privilegePortal list holds the portal) needs the house info and the UGC group table, neither
    /// ported, so a house portal sends nothing; an owned portal other than type 0xb needs the cube map (RequestMoveFromCube,
    /// or UsePortal when no cube is in front) and sends nothing.
    /// </summary>
    public void Use()
    {
        if (!host.HasPlayer || host.InCubeMode) return;
        var p = Current;
        if (p == null || !p.HasEntity) return;
        LastUsed = p.Id;
        if (p.HomePrivilege)
        {
            host.StillOpen($"portal {p.Id}: house privilege (the own house's map and its UGC group's privilegePortal list); box 0x656 when denied");
            return;
        }
        if (p.Owner != 0)
        {
            if (p.Type == 0xb) { SendMode2(0, 0, 0, p.Owner, ""); return; }
            host.StillOpen($"portal {p.Id}: owned portal of type {p.Type} needs the cube map (RequestMoveFromCube)");
            return;
        }
        UsePortal(p, p.TargetMap, p.Id);
    }

    /// <summary>PortalAction::vfunc_3: close the dungeon room dialog (0x4ebd) when open. STILL-OPEN: that dialog is not ported.</summary>
    public void Leave() { }

    /// <summary>PortalAction::UsePortal by type.</summary>
    void UsePortal(LivePortal p, int target, int portalId)
    {
        switch (p.Type)
        {
            case 8: host.StillOpen($"portal {portalId}: type 8 opens the dungeon room dialog (OpenDungeonRoomDialog)"); break;
            case 9: host.Send(new RoomDungeonPortalRequest()); break;
            case 0xc: host.StillOpen($"portal {portalId}: type 0xc opens the dungeon room dialog (mode 3)"); break;
            case 0xd: MoveTo(p, 0, 1); break;
            case 0xe: host.StillOpen($"portal {portalId}: type 0xe opens the dungeon room dialog (mode 4)"); break;
            case 0x10: host.StillOpen($"portal {portalId}: type 0x10 (FUN_140dff580) is not ported"); break;
            case 0x11: host.StillOpen($"portal {portalId}: type 0x11 (FUN_140956a20) is not ported"); break;
            default: MoveTo(p, target, portalId); break;
        }
    }

    /// <summary>
    /// PortalAction::FUN_140956370: the checks before mode 0. Into a shadow continent's pk field from a field without pk: a
    /// yes/no box 0x971 ("moveportal,t,p", answered by <see cref="OnMoveBox"/>). An unknown target field: box 0x99d. A target
    /// field whose required quest is not rewarded: the field-enter-limit notice. A portal with a password: the password box
    /// 0xb07. Otherwise mode 0.
    /// </summary>
    void MoveTo(LivePortal p, int target, int portalId)
    {
        if (!host.HasPlayer) return;
        var cur = host.Field(host.FieldId);
        var tgt = host.Field(target);
        if (cur != null && tgt != null && host.IsShadowContinent(tgt.ContinentCode) && cur.PkMode == 0 && tgt.PkMode != 0)
        {
            host.ShowCommonBox(0x971, 1, $"moveportal,{(uint)target},{(uint)portalId}");
            return;
        }
        if (tgt == null && target != 0) { host.ShowCommonBox(0x99d, 0, ""); return; }
        if (tgt != null && tgt.RequireQuest != 0 && !host.IsQuestRewarded(tgt.RequireQuest))
        {
            host.SystemNotice(Ms2.Quest.ClientFormat.Format(MessageBoxData.CommonByKey("s_quest_error_field_enter_limit") ?? "",
                host.QuestName(tgt.RequireQuest)));
            return;
        }
        if (p.Password)
        {
            host.ShowInputBox(0xb07, (button, input) => OnPasswordAnswer(button, input, target, portalId));
            return;
        }
        SendMode0(target, portalId, 0, 0, "", "");
    }

    /// <summary>PortalAction::FUN_140956be0: on button 0, the typed password (trimmed of ',') goes with mode 0, unless the
    /// portal is gone ("s_action_portal_invalid_portal").</summary>
    public void OnPasswordAnswer(int button, string input, int target, int portalId)
    {
        if (!host.HasPlayer || button != 0) return;
        var pwd = (input ?? "").Trim(',');
        if (Portals?.ById(portalId) == null)
        {
            host.SystemNotice(MessageBoxData.CommonByKey("s_action_portal_invalid_portal") ?? "");
            return;
        }
        SendMode0(target, portalId, 0, 0, pwd, "");
    }

    /// <summary>
    /// MyPC::FUN_141529ae0: a lock still running shows box 0xb0c and stops the send; an ended lock is removed.
    /// STILL-OPEN: the cube function send and the state reset after it (SendFunctionCube, m_kStateCtl.vfunc_0).
    /// </summary>
    bool CheckLock(int map, int a, int b, long c)
    {
        var l = Locks.Find(map, a, b, c);
        if (l != null)
        {
            if (l.Until > host.ServerTime) { host.ShowCommonBox(0xb0c, 0, ""); return false; }
            if (l.Until != 0) Locks.Remove(map, a, b, c);
        }
        return true;
    }

    /// <summary>
    /// MyPC::SendRequestMoveFieldMode0(t, d, a, b, strA, strPwd): the lock of (field, a, b, 0), then u8 0, the current map,
    /// d, a, b, wstr strPwd, wstr strA. The target <paramref name="target"/> itself is not sent.
    /// STILL-OPEN: the guide trigger AddTrigger(0x19, d) (the guide manager is not ported).
    /// </summary>
    public void SendMode0(int target, int d, int a, int b, string strA, string strPwd)
    {
        if (!CheckLock(host.FieldId, a, b, 0)) return;
        host.Send(new RequestMoveFieldPortal(host.FieldId, d, a, b, strPwd ?? "", strA ?? ""));
    }

    /// <summary>MyPC::SendRequestMoveFieldMode2: the lock of (portal, a, b, c), then mode 2.</summary>
    public void SendMode2(int portalId, int a, int b, long c, string pwd)
    {
        if (!CheckLock(portalId, a, b, c)) return;
        host.Send(new RequestMoveFieldHome(portalId, a, b, c, pwd ?? ""));
    }

    /// <summary>
    /// MyPC::OnMessage for a box with a move tag. "moveportal,t,p": on button 0, mode 0 to t's portal p when the field t is
    /// known. Returns whether the tag was a move tag.
    /// </summary>
    public bool OnMoveBox(int button, string tag)
    {
        if (tag == null || !tag.Contains("moveportal", StringComparison.Ordinal)) return false;
        if (button != 0) return true;
        var tok = tag.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (tok.Length != 3) return true;
        int map = (int)WToUI(tok[1]);
        if (host.Field(map) == null) return true;
        SendMode0(map, WToI(tok[2]), 0, 0, "", "");
        return true;
    }

    /// <summary>
    /// The "movePortalWithPwd" box of <see cref="EnterUgcMapMessage"/> mode 3: on button 0, a running lock of (field, a, b, c)
    /// shows box 0xb0c; else the typed text goes with mode 2 (a home asking for a password) or as the first string of mode 0.
    /// Another button forgets the move.
    /// </summary>
    public void OnHomePasswordAnswer(int button, string input)
    {
        var m = PendingMove;
        if (m == null) return;
        if (button != 0) { PendingMove = null; return; }
        // the box tag "movePortalWithPwd,<text>" split on ',' must give 2 tokens
        var tok = ("movePortalWithPwd," + (input ?? "")).Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (tok.Length != 2) return;
        var pwd = tok[1];
        var l = Locks.Find(host.FieldId, m.A, m.B, m.C);
        if (l != null && l.Until > host.ServerTime) { host.ShowCommonBox(0xb0c, 0, ""); return; }
        if (m.WithPassword) SendMode2(m.PortalId, m.A, m.B, m.C, pwd);
        else SendMode0(m.PortalId, m.D, m.A, m.B, "", pwd);
    }

    /// <summary>
    /// CSceneStateGame::OnEnterUgcMap (0xAA, scenestategame.cpp:4581) by mode: the result boxes; mode 3 keeps the move and
    /// opens the password box only with a MyPC; mode 4 (wrong password) puts 0xb0b as the error line of the box already open
    /// and counts the lock's tries (the fifth locks the portal for 10 s); mode 11 is a plain box of
    /// s_home_visit_failed_private. Mode 1 is STILL-OPEN.
    /// </summary>
    public void OnEnterUgcMap(EnterUgcMapMessage m)
    {
        switch (m.Mode)
        {
            case 0: host.ShowCommonBox(0x9cb, 0, ""); break;
            case 1: host.StillOpen("EnterUgcMap mode 1"); break;
            case 2: host.ShowCommonBox(0x9cd, 0, ""); break;
            case 3:
                if (!host.HasPlayer) break;
                PendingMove = m;
                host.ShowInputBox(0xb07, OnHomePasswordAnswer);
                break;
            case 4:
            {
                host.UpdateBoxError(0xb0b);
                var l = Locks.GetOrAdd(host.FieldId, m.A, m.B, m.C);
                if (l.Until > host.ServerTime) break;
                l.Tries++;
                if (l.Tries >= 5) l.Until = host.ServerTime + 10;
                break;
            }
            case 5: host.ShowCommonBox(0x9ce, 0, ""); break;
            case 6: host.ShowCommonBox(0x87c, 0, ""); break;
            case 7: host.ShowCommonBox(0x87b, 0, ""); break;
            case 8: host.ShowCommonBox(0x87d, 0, ""); break;
            case 9: host.ShowCommonBox(0x87a, 0, ""); break;
            case 10: host.ShowCommonBox(0x87e, 0, ""); break;
            case 11: host.ShowTextBox(MessageBoxData.CommonByKey("s_home_visit_failed_private") ?? ""); break;
        }
    }

    static int WToI(string s) => int.TryParse(s.Trim(), out int v) ? v : 0;
    static uint WToUI(string s) => uint.TryParse(s.Trim(), out uint v) ? v : 0;

    /// <summary>MapContinentTable.xml (table/kr first): the continents with shadowContinent set.</summary>
    public static HashSet<int> ShadowContinents(PackSet packs)
    {
        var set = new HashSet<int>();
        foreach (var (id, s) in ContinentShadows(packs))
            if (s != 0) set.Add(id);
        return set;
    }

    /// <summary>
    /// MapContinentTable.xml (table/kr first): each continent id's shadowContinent (CContinentRegionTable m_nShadowContinent,
    /// what CFieldData::GetShadowContinent returns for the field's continentCode, fielddata.cpp:817).
    /// </summary>
    public static Dictionary<int, int> ContinentShadows(PackSet packs)
    {
        var map = new Dictionary<int, int>();
        foreach (var p in TablePath.Resolve(packs, "mapcontinenttable.xml") is { } mp ? new[] { mp } : Array.Empty<string>())
        {
            foreach (var c in GameStrings.Parse(packs.Read("Xml", p))?.Root?.Elements("continent") ?? Enumerable.Empty<XElement>())
                if (int.TryParse((string)c.Attribute("id"), out int id))
                    map[id] = int.TryParse((string)c.Attribute("shadowContinent"), out int s) ? s : 0;
            break;
        }
        return map;
    }
}
