using Godot;
using Ms2.Net;
using Ms2.Net.Packets;
using Ms2.Quest;
using Ms2.Ui.Guide;

namespace Ms2Godot.Net;

/// <summary>
/// The live session's guide (CGuideManager, FUN_1409331e0): made with the field entry, its occurrences and triggers raised
/// from the packets as the client's packet handlers raise them, its update run each frame (the client's game loop runs
/// Tick from CSceneStateGame's update), and its saved steps sent when the session ends.
/// <para>
/// STILL-OPEN: the client also sends the steps (MyPC::SendFunctionCube, mypc.cpp:2835) before a portal move request
/// (MyPC FUN_141529ae0) and in SendRequestQuit; neither request is sent live yet, so the steps go out only at the session's
/// end, the analogue of the exit box's SendRequestQuitAndDisconnect.
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

    void StopGuide()
    {
        if (guide == null) return;
        SendGuideStates(wait: true);
        if (GuideManager.Current == guide) GuideManager.Current = null;
        guide = null;
    }

    /// <summary>MyPC::SendFunctionCube's guide part: the saved steps when they changed since the last send.</summary>
    void SendGuideStates(bool wait = false)
    {
        if (guide == null || !guide.StateChanged || game == null) return;
        var pk = guide.SendStates();
        if (wait) { try { game.SendAsync(pk).Wait(1000); } catch (Exception e) { GD.PushWarning($"live: guide steps not sent ({e.Message})"); } }
        else Send(pk);
    }

    /// <summary>The guide's occurrences and triggers of one packet the session read (the client's handlers raise them).</summary>
    void GuideOnQuest(QuestMessage q, bool existedBefore)
    {
        var g = guide;
        if (g == null) return;
        switch (q)
        {
            case QuestMessage.Load:                              // OnQuest 0x16 (questmanager_2.cpp:984)
                g.QueueOccurrence(GuideOccurrence.QuestProgress, 0);
                g.QueueOccurrence(GuideOccurrence.QuestComplete, 0);
                break;
            case QuestMessage.Start s:                           // mode 2 (:1021)
                g.QueueOccurrence(GuideOccurrence.QuestProgress, s.Id);
                g.AddTrigger(GuideTrigger.QuestAccept, s.Id);
                break;
            case QuestMessage.Update u when Quests.Get(u.Id) is { } r:   // mode 3 (:1032): GetProgress(0) after DecodeProgress
                g.AddTrigger(GuideTrigger.QuestProgress, u.Id, r.Progress.Length > 0 ? (int)r.Progress[0] : 0);
                g.QueueOccurrence(GuideOccurrence.QuestProgress, u.Id);
                break;
            case QuestMessage.Complete c when Quests.Get(c.Id) != null:  // mode 4 (:1048)
                g.AddTrigger(GuideTrigger.QuestComplete, c.Id);
                g.QueueOccurrence(GuideOccurrence.QuestComplete, c.Id);
                break;
            case QuestMessage.Result:                            // mode 5 (:1055); RemoveQuest (:865) queues the same
                g.QueueOccurrence(GuideOccurrence.QuestProgress, 0);
                g.QueueOccurrence(GuideOccurrence.QuestComplete, 0);
                break;
            case QuestMessage.Abandon or QuestMessage.Expired when existedBefore:   // modes 6 and 7: RemoveQuest of a known quest
                g.QueueOccurrence(GuideOccurrence.QuestProgress, 0);
                g.QueueOccurrence(GuideOccurrence.QuestComplete, 0);
                break;
            case QuestMessage.Other { Mode: 0x12 }:              // mode 0x12 (:1140): the quest summon portal
                g.AddTrigger(GuideTrigger.QuestSummonPortal, 0);
                break;
        }
    }

    /// <summary>Whether a quest packet names a quest the book has before it is applied (RemoveQuest returns early on an
    /// unknown quest).</summary>
    bool QuestKnownBefore(QuestMessage q) => q switch
    {
        QuestMessage.Abandon a => Quests.Get(a.Id) != null,
        QuestMessage.Expired e => e.Ids.Any(id => Quests.Get(id) != null),
        _ => false,
    };

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
        public override int SceneId => s.answered ? 0x620000c : 0;
        public override IGuideEventPane Pane => s.GuidePane;
        public override void GuideLog(string text) => GD.Print(text);
        public override void Send(Ms2.Net.Wire.IOutPacket packet) => s.Send(packet);

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
    }
}
