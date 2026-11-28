using Godot;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;
using Ms2Godot.World;

namespace Ms2Godot.Net;

/// <summary>
/// The live portals: the server's portal list of the current field (OnFieldPortal 0x48, <see cref="LivePortals"/>), the
/// action key's portal action (<see cref="Ms2.Ui.PortalAction"/>) and the home portal answers (OnEnterUgcMap 0xAA). The
/// list is new with each RequestFieldEnter (the old field's portals go with its teardown, FUN_141bbc580); the portal locks
/// and the last-used portal are the client's globals (DAT_143ab3288, DAT_143ab288c) and stay across moves.
/// </summary>
public partial class LiveSession
{
    PortalAction portalAction;
    bool portalQuestsWatched;
    /// <summary>The current field's registered portals, null before the first field entry.</summary>
    public LivePortals Portals { get; private set; }
    public PortalAction PortalAction => portalAction ??= new PortalAction(new PortalHost(this));
    /// <summary>ShowSystemNotice(text, 1, 1); the HUD sets it.</summary>
    public Action<string> SystemNoticeSink;
    /// <summary>The RequestMoveField / RoomDungeon packets the portal action sent (a scripted run counts them).</summary>
    public int PortalSends { get; private set; }

    /// <summary>The current field's live npc quest masks (CNpc::UpdateQuestVisibility's table and quest standing), null
    /// before the first field entry.</summary>
    public NpcQuestVisibility NpcQuestMasks { get; private set; }

    /// <summary>A new field entry: an empty portal list for <paramref name="mapId"/>; its messages wait for the field's entities.</summary>
    void NewFieldPortals(int mapId)
    {
        var packs = PackSet.Shared(Settings.DataDir);
        QuestMaskTable masks = null;
        try { masks = FieldContents.Masks(packs); } catch (Exception e) { GD.Print($"live: quest masks unavailable ({e.Message})"); }
        Portals = LivePortals.ForPacks(packs, mapId, masks, QuestStatusOf);
        NpcQuestMasks = new NpcQuestVisibility(masks, mapId, QuestStatusOf);
        PortalAction.Portals = Portals;
        PortalAction.Current = null;
        if (!portalQuestsWatched)
        {
            portalQuestsWatched = true;
            // the quest manager's dirty flag runs every portal's mask again (WorldService FUN_141bd3880), then every npc's
            // (FUN_141bd3ae0, worldservice_1.cpp:9833-9839)
            Quests.Changed += _ => { Portals?.RefreshMasks(); remote?.UpdateNpcQuestVisibility(); };
        }
    }

    /// <summary>The built field's entities: the held portal messages apply. A field built from another xblock (the default
    /// field when the data lacks the entry's map) has none of the server's portals.</summary>
    public void AttachPortals(string xblock, IReadOnlyList<FieldEntity> entities)
    {
        if (Portals == null || Portals.Attached) return;
        string want = null;
        try { want = FieldData.FindById(PackSet.Shared(Settings.DataDir), Portals.FieldId)?.XBlock; } catch { }
        bool same = want != null && string.Equals(want, xblock, StringComparison.OrdinalIgnoreCase);
        Portals.Attach(same ? entities : Array.Empty<FieldEntity>());
        GD.Print($"live: portals attached to xblock {xblock}{(same ? "" : $" (map {Portals.FieldId} is {want ?? "unknown"}: no entities)")}: {Portals.All.Count} registered");
    }

    /// <summary>A quest's standing for the masks (portals and interact objects), from its record and the client's quest data:
    /// <see cref="QuestMaskStatus.Of"/>.</summary>
    QuestMaskTable.QuestStatus QuestStatusOf(int id)
    {
        Ms2.Quest.QuestTable table = null;
        try { table = NpcTalk.ClientQuests(PackSet.Shared(Settings.DataDir)); } catch { }
        return QuestMaskStatus.Of(Quests.Get(id), table == null ? null : table.Quest);
    }

    /// <summary>OnFieldPortal (0x48). Every message applies, as in the client; a mode 2 that leaves the portal's flags as they
    /// were is not logged (a field script can send the same flags every frame).</summary>
    void OnFieldPortal(FieldPortalMessage m)
    {
        if (Portals == null) return;
        var p = Portals.ById(m.Id);
        var before = p == null ? default : (p.Shown, p.Glow, p.Minimap, p.Extra);
        Portals.Apply(m);
        p = Portals.ById(m.Id);
        if (m.Mode == 2 && p != null && Portals.Attached && before == (p.Shown, p.Glow, p.Minimap, p.Extra)) return;
        if (LiveCheck) GD.Print($"live: FieldPortal mode {m.Mode} portal {m.Id}" + (p == null ? "" :
            $" type {p.Type} target {p.TargetMap} shown {p.Shown} glow {p.Glow}{(p.HasEntity ? "" : " (no entity)")}{(Portals.Attached ? "" : " (held)")}"));
    }

    /// <summary>The player's point in the action key's poll: the portal it stands in (PortalAction::vfunc_1). True when the
    /// portal action applies.</summary>
    public bool PollPortal(Ms2.Formats.Model.Vec3 at) => PortalAction.Poll(at);

    /// <summary>PortalAction::vfunc_4, asked when the poll would make the portal the current action: false without a picked
    /// portal, and for a kind 1 portal, which enters at once.</summary>
    public bool AcceptPortal() => PortalAction.Accept();

    /// <summary>The action key's poll left the portal (Unk_ActionKeyManager::Update, actionkeyobject.cpp:577: the old action's vfunc_3).</summary>
    public void LeavePortal() => PortalAction.Leave();

    /// <summary>The action key on the portal (PortalAction::vfunc_2).</summary>
    public void UsePortal() => PortalAction.Use();

    sealed class PortalHost : IPortalHost
    {
        readonly LiveSession s;
        HashSet<int> shadow;
        public PortalHost(LiveSession s) => this.s = s;
        static PackSet Packs => PackSet.Shared(Settings.DataDir);

        public bool HasPlayer => s.selfObject >= 0 && s.player != null && IsInstanceValid(s.player);
        /// <summary>The cube (house build) mode is not ported: never on.</summary>
        public bool InCubeMode => false;
        /// <summary>The session guide's limit of this kind (kind 9 = PortalUse holds the portal action).</summary>
        public bool IsRestricted(int kind) => Ms2.Ui.Guide.GuideManager.IsLimitActive(kind);
        public int FieldId => s.fieldEntry.Current?.MapId ?? 0;
        public long ServerTime => s.game?.Session.ServerTime ?? 0;
        public FieldData Field(int id)
        {
            try { return FieldData.FindById(Packs, id); } catch { return null; }
        }
        public bool IsShadowContinent(int code)
        {
            try { shadow ??= PortalAction.ShadowContinents(Packs); } catch { shadow = new HashSet<int>(); }
            return shadow.Contains(code);
        }
        public bool IsQuestRewarded(int q) => s.Quests.Get(q)?.State == 2;
        public string QuestName(int q)
        {
            try { return NpcTalk.ClientQuests(Packs).QuestName(q) ?? ""; } catch { return ""; }
        }
        public void Send(Ms2.Net.Wire.IOutPacket p)
        {
            s.Send(p);
            s.PortalSends++;
            GD.Print($"live: portal send {p}");
        }
        public void ShowCommonBox(int id, int type, string tag)
        {
            GD.Print($"live: portal box 0x{id:x} type {type}{(string.IsNullOrEmpty(tag) ? "" : $" tag '{tag}'")}");
            MessageBoxQueue.ShowCommon(id, type, string.IsNullOrEmpty(tag) ? null : new MoveListener(s), tag ?? "");
        }
        public void ShowInputBox(int id, Action<int, string> answer)
        {
            GD.Print($"live: portal input box 0x{id:x}");
            // the movie answers with "<tag>," + the typed text (m_str218); the part after the first ',' is the text
            var box = MessageBoxQueue.CreateWithCallback(MessageBoxData.Common(id), 10, (n, d) =>
            {
                string t = d.ClickedTag ?? "";
                int c = t.IndexOf(',');
                answer(n, c < 0 ? "" : t[(c + 1)..]);
                return true;
            });
            box.SetInput(5, "", 6, 0, 0);
            MessageBoxQueue.Show(box);
        }
        public void SystemNotice(string text)
        {
            GD.Print($"live: portal notice '{text}'");
            s.SystemNoticeSink?.Invoke(text);
        }
        public void UpdateBoxError(int id)
        {
            GD.Print($"live: portal box error 0x{id:x}");
            MessageBoxQueue.Layer?.UpdateErrorMessage(MessageBoxData.Common(id));
        }
        public void ShowTextBox(string text)
        {
            GD.Print($"live: portal text box '{text}'");
            MessageBoxQueue.ShowMessageBox(text, 0, null, "");
        }
        public void StillOpen(string what) => GD.Print($"live: STILL-OPEN {what}; nothing sent");
        public void OpenDungeonRoomDialog(int portalId) => s.OpenDungeonRoomDialog?.Invoke(portalId);
        public void OpenDungeonRoomDialogWithMode(int portalId, int mode) => s.OpenDungeonRoomDialogWithMode?.Invoke(portalId, mode);
        public void OpenDungeonRoomDialogRoom(int portalId) => s.OpenDungeonRoomDialogRoom?.Invoke(portalId);
        public void CloseDungeonRoomDialog() => s.CloseDungeonRoomDialog?.Invoke();
    }

    /// <summary>MyPC's box listener (MyPC::OnMessage) for the portal move boxes.</summary>
    sealed class MoveListener : IMessageBoxListener
    {
        readonly LiveSession s;
        public MoveListener(LiveSession s) => this.s = s;
        public bool OnMessageBox(MessageBoxController sender, int button, string tag)
        {
            MessageBoxQueue.Next();
            s.PortalAction.OnMoveBox(button, tag);
            return true;
        }
    }
}
