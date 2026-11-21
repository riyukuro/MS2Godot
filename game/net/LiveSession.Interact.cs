using Godot;
using Ms2.Formats.Model;
using Ms2.Formats.Pack;
using Ms2.Formats.Text;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Ui;
using Ms2.Ui.Hud;
using Ms2Godot.World;

namespace Ms2Godot.Net;

/// <summary>
/// The live field's interact objects (WorldService m_678, OnInteractObject 0x63, InteractAction / GatheringAction /
/// StateInteraction, <see cref="LiveInteractObjects"/>). Created per RequestFieldEnter and attached once the field's
/// xblock entities are loaded, mirroring <see cref="Portals"/>.
/// </summary>
public partial class LiveSession
{
    public LiveInteractObjects InteractObjects { get; private set; }
    /// <summary>The field npcs InteractNpcAction (key 0x11) reads and its StateInteractionNpc cast; RemoteLayer fills it.</summary>
    public LiveInteractNpc InteractNpcs { get; private set; }
    bool npcPolled;
    /// <summary>The HUD's casting bar (UICastingBar 0x4e72); set by HudScene while the field HUD is up.</summary>
    public CastingBarController CastingBar;
    /// <summary>Cross-channel/field interact notices (FUN_1412751b0); the HUD sets it when chat is up.</summary>
    public Action<string> ChatNoticeSink;
    /// <summary>GetItemRank: an item's own rank from the item table; the HUD sets it with its tables.</summary>
    public Func<int, int> ItemRank;
    public int InteractSends { get; private set; }
    /// <summary>Whether an item id is in the item data (CItem::FUN_140523c40); the HUD sets it with its tables. Unset:
    /// every id is taken as an item.</summary>
    public Func<int, bool> ItemExists;
    /// <summary>The local pc's held item (PC::m_spItem1040): set by InteractObject mode 15, let go by SendRequestCube and
    /// ResponseCube 0x12. A new field makes a new pc, so it starts empty.</summary>
    public readonly HeldItem HeldItem = new();
    /// <summary>The game event list's interact object shutdowns (GameEvent 0xB2, CGameEventValueManager
    /// ShutdownInteractObjectId); kept across fields, cleared with a new game scene.</summary>
    public readonly InteractObjectShutdowns InteractShutdowns = new();

    void NewFieldInteractObjects(int mapId)
    {
        InteractObjects?.CancelCast();
        HeldItem.Release();
        InteractNpcs ??= new LiveInteractNpc(new InteractNpcHost(this)) { Held = HeldItem };
        InteractNpcs.Clear();
        npcPolled = false;
        var packs = PackSet.Shared(Settings.DataDir);
        InteractObjectTable tbl = null;
        try { tbl = InteractObjectTable.Shared(packs); }
        catch (Exception e) { GD.Print($"live: interact object table unavailable ({e.Message})"); }
        InteractObjects = new LiveInteractObjects(new InteractHost(this, mapId), tbl) { Held = HeldItem };
    }

    /// <summary>Binds the loaded field's xblock MS2InteractObject entities and applies any held 0x63 messages.</summary>
    public void AttachInteractObjects(string xblock, IReadOnlyList<FieldEntity> entities)
    {
        if (InteractObjects == null || InteractObjects.Attached) return;
        string want = null;
        try { want = FieldData.FindById(PackSet.Shared(Settings.DataDir), fieldEntry.Current?.MapId ?? 0)?.XBlock; } catch { }
        bool same = want != null && string.Equals(want, xblock, StringComparison.OrdinalIgnoreCase);
        InteractObjects.Attach(same ? entities : Array.Empty<FieldEntity>());
        GD.Print($"live: interact objects attached to xblock {xblock}{(same ? "" : $" (different xblock: no entities)")}: {InteractObjects.All.Count} registered");
    }

    void OnInteractObject(InteractObjectMessage m)
    {
        if (InteractObjects == null) return;
        InteractObjects.Apply(m);
        if (m.Mode == InteractObjectMessage.ModeStates)
            GD.Print($"live: InteractObject mode 8 ({m.Entries?.Count ?? 0} states)");
        else if (m.Mode is InteractObjectMessage.ModeState or InteractObjectMessage.ModeAdd or InteractObjectMessage.ModeRemove or InteractObjectMessage.ModeResult or InteractObjectMessage.ModeMessage)
            GD.Print($"live: InteractObject mode {m.Mode} name '{m.Name}' kind {m.Kind} state {m.State} result {m.Result}");
    }

    /// <summary>CSceneStateGame::OnResponseCube (scenestategame.cpp:9876), mode 0x12 (:10176-10192): error 0 lets the
    /// pc's held item go (FindPCById, then PC::FUN_14165f1f0(0, 0)). STILL-OPEN: the other modes, and the build notice
    /// (ShowBuildNotice) an error shows to its character.</summary>
    void OnResponseCube(ResponseCubeHeldItem m)
    {
        if (m == null) return;
        if (m.Error == 0 && m.Id == selfObject) HeldItem.Release();
        GD.Print($"live: ResponseCube 0x12 error {m.Error} id {m.Id}");
    }

    /// <summary>GameEvent 0xB2 (GameEventManagerCli::Dispatch, gameeventmanagercli.cpp:77): the shutdown events are applied.
    /// STILL-OPEN: the other event types are not read; the reader stops at the first one (Truncated).</summary>
    void OnGameEvent(GameEventMessage m)
    {
        InteractShutdowns.Apply(m);
        GD.Print($"live: GameEvent kind {m.Kind}: {m.Shutdowns.Count} shutdown events, {m.RemovedIds.Count} removed{(m.Truncated ? ", stopped at a type not ported" : "")}");
    }

    void OnInteractNpc(InteractNpcMessage m)
    {
        bool applied = InteractNpcs != null && InteractNpcs.Apply(m);
        GD.Print($"live: InteractNpc npc {m.ObjectId} talkable {(m.HasTalkable ? m.Talkable.ToString() : "-")}{(applied ? "" : " (no such npc)")}");
    }

    /// <summary>
    /// Per-frame action-key poll before the portal, by ascending action key: GatheringAction (4), InteractNpcAction
    /// (0x11, <see cref="LiveInteractNpc.Poll"/>), InteractAction (0x12). 0 B/frame.
    /// </summary>
    public bool PollInteract(Vec3 at) => PollInteract(at, at);

    /// <summary>As <see cref="PollInteract(Vec3)"/>, with the npc search from <paramref name="npcAt"/> (the ridee's
    /// position when riding, WorldService::FindNearestNpc via worldservice_1.cpp:9679-9682).</summary>
    public bool PollInteract(Vec3 at, Vec3 npcAt) => InteractKey.Poll(InteractObjects, InteractNpcs, at, npcAt, out npcPolled);

    /// <summary>The billboard action's poll (key 0x18, after the portal; 0 B/frame).</summary>
    public bool PollInteractBillBoard(Vec3 at) => InteractObjects != null && InteractObjects.PollBillBoard(at);

    /// <summary>Per-frame tick for gathering respawns and active StateInteraction cast (0 B/frame).</summary>
    public void TickInteract(Vec3 at, bool playerMoved)
    {
        InteractObjects?.Tick(at, playerMoved);
        InteractNpcs?.Tick(at, playerMoved);
    }

    /// <summary>Action key press on the polled interact object: only the polled action runs, and its own state gate refuses it
    /// while the other interaction's cast runs (that cast is not cancelled).</summary>
    public bool UseInteract(Vec3 at) => InteractKey.Use(InteractObjects, InteractNpcs, npcPolled, at);

    /// <summary>The client state id as far as it is known here (<see cref="InteractKey.StateId"/>): 0x19 / 0x1a while an
    /// interaction's cast runs; riding 0x21; on the spawn or idle state, 1 while the body stands and 2 while it walks; a
    /// state the server set otherwise. A move the client makes on its own that is not idle or walk (jump, fall, swim, climb,
    /// ...) is -1, which the interactions refuse.</summary>
    int InteractPlayerStateId()
    {
        if (selfObject < 0 || player == null || !IsInstanceValid(player)) return -1;
        int move;
        if (player.Mount != null) move = 0x21;
        else
        {
            int st = Me.State;
            move = st is not (0 or 1 or 2) ? st : player.State switch
            {
                Player.MoveState.Idle => 1,
                Player.MoveState.Walk => 2,
                _ => -1,
            };
        }
        return InteractKey.StateId(InteractObjects, InteractNpcs, move);
    }

    sealed class InteractNpcHost : IInteractNpcHost
    {
        readonly LiveSession s;
        float? interactDist;
        public InteractNpcHost(LiveSession s) { this.s = s; }

        public bool HasPlayer => s.selfObject >= 0 && s.player != null && IsInstanceValid(s.player);

        public float InteractionDistance
        {
            get
            {
                if (interactDist is { } d) return d;
                float v = 0f;     // Constants InteractionDistance.Define(.., 0.0f) (unit_1402ab440_constantsblocks.cpp:774)
                try { v = FieldContents.Constants(PackSet.Shared(Settings.DataDir)).Float("InteractionDistance", 0f); } catch { }
                interactDist = v;
                return v;
            }
        }

        public Vec3 PlayerForward
        {
            get
            {
                if (s.player == null || !IsInstanceValid(s.player)) return new Vec3(0, 0, 0);
                var f = s.player.FacingDirection;      // godot (x, y, z) -> MS2 (x, -z, y), FieldPortals.ToMs2
                return new Vec3(f.X, -f.Z, f.Y);
            }
        }

        // STILL-OPEN: PvPInfo point (CPvPInfo::HasPoint / GetPoint, actionkeyobject.cpp:1667) is not tracked
        public int? PvPPoint => null;
        public int PlayerStateId => s.InteractPlayerStateId();

        public void Send(Ms2.Net.Wire.IOutPacket packet)
        {
            s.Send(packet);
            s.InteractSends++;
            GD.Print($"live: interact npc send {packet}");
        }

        public bool StartCasting(ICastingEventListener listener, int durationMs, bool showBar)
        {
            if (s.CastingBar == null) return false;
            s.CastingBar.StartCastingText(listener, "", durationMs, showTime: false, activeBar: showBar, endText: "");
            return true;
        }

        public long NowMs => (long)Time.GetTicksMsec();

        public void CancelCasting() => s.CastingBar?.Cancel();

        public void PlayPlayerAnim(string animName, bool loop)
        {
            if (s.player != null && IsInstanceValid(s.player)) s.player.StartInteractAnim(animName, loop);
        }

        public void StopPlayerInteractAnim()
        {
            if (s.player != null && IsInstanceValid(s.player)) s.player.StopInteractAnim();
        }
    }

    sealed class InteractHost : IInteractHost
    {
        readonly LiveSession s;
        readonly int fieldId;
        Dictionary<string, string> actionNames, noticeTexts, itemNames;
        float? masteryDist;

        public InteractHost(LiveSession s, int fieldId) { this.s = s; this.fieldId = fieldId; }
        static PackSet Packs => PackSet.Shared(Settings.DataDir);
        static GameStrings Strings => new(Packs, GameOptions.StringLanguage);

        public bool HasPlayer => s.selfObject >= 0 && s.player != null && IsInstanceValid(s.player);
        /// <summary>CGuideManager::IsRestricted(ActionInteraction); no limit before the guide is made.</summary>
        public bool IsRestricted(int kind) => s.guide != null && s.guide.IsRestricted((Ms2.Ui.Guide.GuideLimit)kind);
        public int FieldId => fieldId;
        /// <summary>The ServerEnter channel.</summary>
        public ushort Channel => (ushort)(s.entry?.Enter?.Channel ?? 0);
        public long NowMs => (long)Time.GetTicksMsec();
        public bool PlayerRiding => s.player != null && IsInstanceValid(s.player) && s.player.Mount != null;
        public int LocalObjectId => s.selfObject;
        public bool ItemExists(int itemId) => s.ItemExists?.Invoke(itemId) ?? true;

        public int PlayerStateId => s.InteractPlayerStateId();

        public bool IsInteractObjectShutdown(int fieldId, int rowId) => s.InteractShutdowns.IsShutdown(fieldId, rowId);

        // STILL-OPEN: MyPC::FUN_14152e0c0's state controller update has no counterpart; the move sync runs on its own
        public void FlushPlayerState() { }

        public float MasteryInteractionDistance
        {
            get
            {
                if (masteryDist is { } d) return d;
                float v = 300f;
                try { v = FieldContents.Constants(Packs).Float("MasteryObjectInteractionDistance", 300f); } catch { }
                masteryDist = v;
                return v;
            }
        }

        public QuestMaskTable.QuestStatus QuestStatusOf(int questId) => s.QuestStatusOf(questId);

        /// <summary>CInteractObject::CheckItemRequirement (interactobject.cpp:515-533): rank 0 is the item's own rank
        /// (GetItemRank); the first bag item of that code and rarity (Unk_Inventory::FUN_140a5e5c0, inventory.cpp:671) must
        /// hold the consume count. STILL-OPEN: PC::CanUseItem on that item.</summary>
        public bool HasRequiredItem(uint code, int rank, uint consume)
        {
            if (code == 0) return true;
            int rarity = rank != 0 ? rank : s.ItemRank?.Invoke((int)code) ?? 0;
            foreach (var it in s.Items.All)
            {
                if ((uint)it.Id != code || it.Rarity != rarity) continue;
                return (uint)it.Amount >= consume;
            }
            return false;
        }

        // STILL-OPEN: PCBase additional effect active check (FUN_140970020) is not wired to LiveSession buffs yet
        public bool HasAdditionalEffect(uint effectId, ushort level) => false;
        public string AdditionalEffectName(uint effectId, ushort level)
        {
            try { return GameTables.Shared(Packs, GameOptions.StringLanguage).AdditionalDescription(effectId, level, "name") ?? ""; }
            catch { return ""; }
        }

        public bool IsPartyLeader => s.Me.User != null && s.Party.IsLeader(s.Me.User.CharacterId);
        // STILL-OPEN: the mastery grade and the recipe's mastery and limit checks (CInteractGatheringObject::vfunc_7) are
        // not read; a mastery-gated object is refused, and a gathering recipe sends more once MasteryGrade is wired
        public int MasteryGrade(int masteryType) => 0;
        public bool CanGatherRecipe(int recipeId) => true;

        public void Send(Ms2.Net.Wire.IOutPacket packet)
        {
            s.Send(packet);
            s.InteractSends++;
            GD.Print($"live: interact send {packet}");
        }

        public string Common(int id) => CommonStringIds.Key(id) is { } k ? MessageBoxData.CommonByKey(k) ?? "" : "";
        public string CommonByKey(string key) => MessageBoxData.CommonByKey(key) ?? "";

        public string ItemName(uint itemCode)
        {
            try { itemNames ??= Strings.Table("itemname", "name"); } catch { itemNames = new(); }
            return itemNames.TryGetValue(itemCode.ToString(), out var n) ? n : itemCode.ToString();
        }

        public string InteractActionName(int rowId)
        {
            try { actionNames ??= Strings.Table("stringinteractobjectaction", "name"); } catch { actionNames = new(); }
            return actionNames.TryGetValue(rowId.ToString(), out var n) ? n : "";
        }

        public string InteractNoticeText(int nameId)
        {
            try { noticeTexts ??= Strings.Table("stringinteractobjectnotice", "name"); } catch { noticeTexts = new(); }
            return noticeTexts.TryGetValue(nameId.ToString(), out var n) ? n : "";
        }

        public string MasteryTypeName(int type) => type.ToString();
        public string MasteryGradeName(int type, int value) => value.ToString();

        public void SystemNotice(string text, int style = 1)
        {
            if (string.IsNullOrEmpty(text)) return;
            GD.Print($"live: interact notice (style {style}) '{text}'");
            s.SystemNoticeSink?.Invoke(text);
        }

        public void ChatNotice(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            GD.Print($"live: interact chat notice '{text}'");
            if (s.ChatNoticeSink != null) s.ChatNoticeSink(text);
            else s.SystemNoticeSink?.Invoke(text);
        }

        public bool StartCasting(ICastingEventListener listener, string text, int durationMs)
        {
            if (s.CastingBar == null) return false;
            s.CastingBar.StartCastingText(listener, text ?? "", durationMs, showTime: false, activeBar: true, endText: "");
            return true;
        }

        public void CancelCasting() => s.CastingBar?.Cancel();

        public void PlayPlayerAnim(string animName, bool loop)
        {
            if (s.player != null && IsInstanceValid(s.player)) s.player.StartInteractAnim(animName, loop);
        }

        public void StopPlayerInteractAnim()
        {
            if (s.player != null && IsInstanceValid(s.player)) s.player.StopInteractAnim();
        }

        public void OnObjectStateChanged(LiveInteractObject obj)
        {
            // STILL-OPEN: dynamic mesh swap / visibility / PhysX box toggle on the rendered field entity
        }

        public void OnObjectSuccessEffect(LiveInteractObject obj, string effectAsset)
        {
            GD.Print($"live: interact effect '{effectAsset}' at {obj.Name}");
        }

        public void OnOpenUiDialog(string swf, string value)
        {
            GD.Print($"live: interact open UI '{swf}' value '{value}' (STILL-OPEN)");
        }

        public void OnGuideTrigger(int triggerType, int rowId) => s.guide?.AddTrigger((Ms2.Ui.Guide.GuideTrigger)triggerType, rowId);

        public void StillOpen(string what) => GD.Print($"live: STILL-OPEN {what}; nothing sent");
    }
}
