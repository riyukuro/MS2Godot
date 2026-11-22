using Godot;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Quest;
using Ms2.Ui.Guide;

namespace Ms2Godot.Net;

/// <summary>
/// The live session's guide (CGuideManager, FUN_1409331e0): made with the field entry, its occurrences and triggers raised
/// from the packets as the client's packet handlers raise them, its update run each frame (the client's game loop runs
/// Tick from CSceneStateGame's update), and its saved steps sent when they changed (MyPC::SendFunctionCube, the quit and
/// portal-move sends; a fire-and-forget, like the client's).
/// <para>
/// STILL-OPEN: the client also sends the steps before a portal move request (MyPC FUN_141529ae0, mypc.cpp:3075); that
/// request is not sent live yet, so the steps go out only at the session's end, the analogue of the exit box's
/// SendRequestQuitAndDisconnect.
/// </para>
/// </summary>
public partial class LiveSession
{
    GuideManager guide;

    /// <summary>The live guide; null before the field entry.</summary>
    public GuideManager Guide => guide;

    /// <summary>The guide event pane (UIGuideEvent 0x4e53) while the HUD runs it, null without it; the guide's world reads
    /// it (UpdateEntities does nothing until the pane is there and loaded, guideentity_1b.cpp).</summary>
    public IGuideEventPane GuidePane { get; set; }

    /// <summary>
    /// The HUD's dialog registry (CUIService::FindObjectByName over the scene's open movies), which the guide's dialog
    /// conditions read; null before the HUD runs. The lookups are by type name — every dialog reference the guide's
    /// tables hold names the dialog by, an id always with its name.
    /// </summary>
    public Func<string, GuideDialog> FindDialogOf { get; set; }

    void StartGuide()
    {
        if (guide != null) return;
        try
        {
            guide = new GuideManager(new LiveGuideWorld(this));
            guide.Load(GuideTable.Load(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir)));
            GuideManager.Current = guide;
            GD.Print($"live: guide {guide.EntityCount} events");
        }
        catch (Exception e) { GD.PushWarning($"live: guide tables unavailable ({e.Message})"); guide = null; }
    }

    string guideStatusShown;
    ulong guideStatusAt;

    /// <summary>Why the guide is idle: the pane missing or not loaded, no player; printed when it changes, at most every 2 s,
    /// so a log shows whether the guide ever ran (UpdateEntities waits on all three).</summary>
    void LogGuideStatus()
    {
        if (guide == null || Time.GetTicksMsec() - guideStatusAt < 2000) return;
        guideStatusAt = Time.GetTicksMsec();
        string now = $"pane {(GuidePane == null ? "missing" : GuidePane.Loaded ? "loaded" : "loading")}, player {(Me.User != null && player != null && GodotObject.IsInstanceValid(player) ? "bound" : "not bound")}, field {FieldId}";
        if (now == guideStatusShown) return;
        guideStatusShown = now;
        GD.Print("live: guide status: " + now);
    }

    void StopGuide()
    {
        if (guide == null) return;
        SendGuideStates();
        if (GuideManager.Current == guide) GuideManager.Current = null;
        guide = null;
    }

    /// <summary>MyPC::SendFunctionCube's guide part (mypc.cpp:2835): the saved steps when they changed since the last
    /// send. The client sends them before a portal move request (MyPC FUN_141529ae0, mypc.cpp:3075) and in
    /// SendRequestQuit / SendRequestQuitAndDisconnect (networkservice.cpp:503 / :509), always as a fire-and-forget send
    /// (FUN_140710590), never a wait; the teardown's is the quit analogue.</summary>
    void SendGuideStates()
    {
        if (guide == null || !guide.StateChanged || game == null) return;
        Send(guide.SendStates());
    }

    /// <summary>
    /// The guide's occurrences and triggers of one packet the session read (the client's handlers raise them,
    /// <see cref="GuideQuests"/>), with the field's portal registry for the summon portal.
    /// </summary>
    void GuideOnQuest(QuestMessage q, bool existedBefore)
    {
        if (guide == null) return;
        GuideQuests.OnQuest(guide, q, Quests, existedBefore, TargetPortalExists);
    }

    /// <summary>WorldService::FUN_141bc2800 (worldservice_1.cpp:4003): whether the current field holds the portal of
    /// an id, the registry the summon portal's trigger asks (questmanager_2.cpp:1129). The field's portals are the
    /// xblock's here (FieldPortals.ById); a portal the server alone knows (an object it adds at runtime, not in the
    /// xblock) is not found, and the client's registry of server-added field objects is STILL-OPEN with the live
    /// world's own object list.</summary>
    bool TargetPortalExists(int id) =>
        GetTree().GetFirstNodeInGroup(Ms2Godot.World.FieldContents.Group) is Ms2Godot.World.FieldContents { Portals: { } fc }
        && fc.ById(id) != null;

    /// <summary>
    /// What the guide reads from the live session. The scene is the game scene (0x620000c) once the field is entered;
    /// the guide event pane (UIGuideEvent 0x4e53) is the HUD's, so an event takes a step only while the field's HUD runs.
    /// STILL-OPEN: the battle state and every member the base class answers with its default.
    /// </summary>
    sealed class LiveGuideWorld : GuideWorld
    {
        readonly LiveSession s;
        QuestTable quests;

        public LiveGuideWorld(LiveSession s) => this.s = s;

        public override long ServerTime => s.GameSession?.ServerTime ?? base.ServerTime;
        public override int SceneId => s.fieldEntry.Answered ? 0x620000c : 0;
        public override IGuideEventPane Pane => s.GuidePane;
        /// <summary>The HUD's dialog registry (its FindDialogOf); the base's null leaves the dialog conditions off,
        /// as the client's while no dialog object is there.</summary>
        public override GuideDialog FindDialog(string name) => s.FindDialogOf?.Invoke(name);
        public override void GuideLog(string text) => GD.Print(text);
        public override void Send(Ms2.Net.Wire.IOutPacket packet) { GD.Print($"live: guide sends {packet.GetType().Name}"); s.Send(packet); }

        public override bool HasPlayer => s.Me.User != null && s.player != null && GodotObject.IsInstanceValid(s.player);

        /// <summary>The player's field position: Godot (x, y, z) is the field's (x, -z, y).</summary>
        public override System.Numerics.Vector3 PlayerPosition
        {
            get
            {
                if (!HasPlayer) return System.Numerics.Vector3.Zero;
                var p = s.player.GlobalPosition;
                return new System.Numerics.Vector3(p.X, -p.Z, p.Y);
            }
        }

        public override bool Dead => s.Me.Dead;
        public override int FieldId => s.FieldId ?? 0;
        public override int JobGroup => s.Me.User?.JobCode ?? 0;
        /// <summary>STILL-OPEN: the client's sub job is PC m_spJobFB8 (pc.cpp:1275); the user block's job, which the Job packet does not refresh here.</summary>
        public override int SubJob => s.Me.User?.Job ?? 0;
        public override int Level => s.Me.User?.Level ?? 0;
        public override int PlayerState => s.Me.State;

        /// <summary>
        /// CEquipment::GetItemAt(FUN_1405451c0(slot name), layer) (equipment.cpp:806): the item at the slot's layer, null
        /// when the slot is empty or the name is not a slot. Layers 0 and 1 are the worn gear (by the item's worn layer),
        /// 2 the second outfit. Valid is CItem::FUN_140528980 (item.cpp:1717) without its pet, maid and score exceptions
        /// (STILL-OPEN): not expired (IsExpired, item.cpp:1303: expiry &gt; 0 and not after the server time).
        /// </summary>
        public override GuideEquipSlot? Equip(string slotName, int slotType)
        {
            if (!Enum.TryParse<EquipSlot>(slotName, out var slot) || slot == EquipSlot.Unknown) return null;
            var list = slotType == 2 ? s.Me.Outfit2 : s.Me.Equips;
            if (list == null) return null;
            foreach (var it in list)
            {
                if (it.Slot != slot) continue;
                if (slotType != 2 && (WornLayer(it.Id) ?? 0) != slotType) continue;
                long now = ServerTime;
                return new GuideEquipSlot(it.Id, !(it.ExpiryTime > 0 && it.ExpiryTime <= now));
            }
            return null;
        }

        /// <summary>CQuestManagerCommon::IsQuestRewarded (questdata.cpp:419): CQuest::IsRewarded, state 2.</summary>
        public override bool IsQuestRewarded(int questId) => s.Quests.Get(questId)?.State == 2;
        /// <summary>IsQuestActive (questdata.cpp:427): CQuest::IsActive, state 1.</summary>
        public override bool IsQuestActive(int questId) => s.Quests.Get(questId)?.State == 1;

        /// <summary>IsQuestComplete (questdata.cpp:435): CQuest::IsComplete, the goals met by the progress.</summary>
        public override bool IsQuestComplete(int questId)
        {
            var q = s.Quests.Get(questId);
            if (q == null) return false;
            QuestData data;
            try { data = (quests ??= Ms2Godot.World.NpcTalk.ClientQuests(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir))).Quest(q.Id); }
            catch (Exception e) { GD.Print($"live: quest data unavailable ({e.Message})"); return false; }
            return data != null && QuestRules.IsComplete(data, q.State, q.Progress);
        }

        public override bool? NearestInteractableUsable
        {
            get
            {
                if (!HasPlayer || s.InteractObjects == null) return null;
                var p = PlayerPosition;
                return s.InteractObjects.NearestUsable(new Ms2.Formats.Model.Vec3(p.X, p.Y, p.Z));
            }
        }

        public override (float Distance, int State)? InteractObject(int interactId)
        {
            if (!HasPlayer || s.InteractObjects == null) return null;
            var p = PlayerPosition;
            return s.InteractObjects.NearestOfRow(new Ms2.Formats.Model.Vec3(p.X, p.Y, p.Z), interactId);
        }
    }
}
