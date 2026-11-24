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
    /// <summary>Whether the own skill state runs (the HUD's cast), for the guide's state id (<see cref="OwnCurStateId"/>).</summary>
    public Func<bool> OwnSkillStateRunsOf { get; set; }
    /// <summary>The HUD's key table (CKeyTable, GetKeyTable), which the guide's key conditions read; null before the HUD runs.</summary>
    public Func<Ms2.Ui.KeyTable> GuideKeysOf { get; set; }
    /// <summary>The keyboard's IsKeyDown(key, false, 0) over a client key code (the HUD's key map), not gated by a text field.</summary>
    public Func<int, bool> GuideKeyDownOf { get; set; }
    /// <summary>The HUD's dialog state the guide's tab and menu conditions read; null before the HUD runs.</summary>
    public IGuideHudState GuideHud { get; set; }

    /// <summary>The guide's field data (WorldService::GetFieldData) of a field id, kept for the last id asked: the guide asks each tick.</summary>
    Ms2.Formats.World.FieldData GuideFieldData(int id)
    {
        if (id != guideFieldId)
        {
            guideFieldId = id;
            try { guideField = Ms2.Formats.World.FieldData.FindById(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir), id); }
            catch (Exception) { guideField = null; }
        }
        return guideField;
    }
    /// <summary>The item table reads the guide's item conditions pass on, made once per HUD (a tick allocates no delegate).</summary>
    Func<int, int> GuideItemTypeOf(IGuideHudState hud)
    {
        if (guideItemHud != hud)
        {
            guideItemHud = hud;
            guideItemType = hud.ItemType;
            guideItemMesh = hud.ItemHasCubeTextureMesh;
        }
        return guideItemType;
    }
    IGuideHudState guideItemHud;
    Func<int, int> guideItemType;
    Func<int, bool> guideItemMesh;
    int guideFieldId = int.MinValue;
    Ms2.Formats.World.FieldData guideField;

    void StartGuide()
    {
        if (guide != null) return;
        try
        {
            guide = new GuideManager(new LiveGuideWorld(this));
            guide.Load(GuideTable.Load(Ms2.Formats.Pack.PackSet.Shared(Settings.DataDir)));
            GuideManager.Current = guide;
            // MS2_GUIDE_DEBUG=1: the manager's debug log (m_nDebug, CGuideManager::ToggleDebug @0x14138d460) from the start: every trigger, open and close
            guide.Debug = System.Environment.GetEnvironmentVariable("MS2_GUIDE_DEBUG") == "1";
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
    /// STILL-OPEN: every member the base class answers with its default; GuideConditionReadSweepTests lists the reads the
    /// game files' conditions make that this world does not give, with their causes.
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
        public override void Send(Ms2.Net.Wire.IOutPacket packet) { GD.Print($"live: guide sends {packet.GetType().Name}{(packet is Ms2.Net.Packets.GuideServerTriggerEvent t ? " " + t.EventId : "")}"); s.Send(packet); }

        /// <summary>The own PC is in the field: bound, alive as an object and in the tree. The map unload's StopAndUpdate
        /// runs from the field's TreeExiting, which Godot emits after the field's children left the tree; on a field change
        /// the PC is dropped before that (SetMyPC(0), <see cref="DropOwnPc"/>), but when the process quits it is still bound,
        /// and its position (a global transform) cannot be read out of the tree.</summary>
        public override bool HasPlayer => s.Me.User != null && s.player != null && GodotObject.IsInstanceValid(s.player) && s.player.IsInsideTree();
        /// <summary>The field's RegionSkills node, the effect service the guide's cube effects go through.</summary>
        public override Ms2.Formats.World.IRegionSkillEffects Effects => s.FieldEffects;

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
        /// <summary>Input (unit_1413a6580.cpp:108) and ActionInput (:162): the keyboard's IsKeyDown(key, false, 0).</summary>
        public override bool IsKeyDown(int key) => s.GuideKeyDownOf?.Invoke(key) ?? false;
        /// <summary>Next (unit_1413a7bc0.cpp:83): CInputKeyHandler::IsActionKeyDown(action, false, 0), a key the key table binds
        /// to the action is down (CKeyTable::IsActionKeyDown, keytable.cpp:1037).</summary>
        public override bool IsActionKeyDown(int action) =>
            s.GuideKeysOf?.Invoke() is { } k && s.GuideKeyDownOf is { } down && k.IsActionKeyDown(action, down);
        /// <summary>ActionInput (unit_1413a6580.cpp:159, :168): CKeyTable::FindKeyByActionAndPriority.</summary>
        public override int FindKeyByActionAndPriority(int action, int priority) => s.GuideKeysOf?.Invoke()?.FindKeyByActionAndPriority(action, priority) ?? 0;
        /// <summary>ActionInput (unit_1413a6580.cpp:165): CKeyTable::GetPairKey.</summary>
        public override bool GetPairKey(int key, out int pair) => Ms2.Ui.KeyTable.GetPairKey(key, out pair);
        /// <summary>DAT_143ab2890, which FieldId reads (unit_1413a9a50.cpp:92): set by OnGameToGame (networkservice.cpp:952),
        /// cleared by the next OnRequestFieldEnter (scenestategame.cpp:2351); the session's GameToGame flag.</summary>
        public override bool Migrating => s.Migrating;
        /// <summary>PC::IsCubeMode1 (pc.cpp:1848), ConstructMode's read (unit_1413a7bc0.cpp:258): cube mode 1, building.</summary>
        public override bool CubeMode => s.CubeCraft.CubeMode == 1;
        /// <summary>EquipSkill (unit_1413a8370.cpp:130): CKeyTable::FindQuickSlotBySkillAndItemUid(skill, 0) on the current page.</summary>
        public override int FindQuickSlotBySkill(int skillId) => s.KeyTable.FindQuickSlotBySkillAndItemUid(skillId, 0);
        /// <summary>ExistShortcut (unit_1413a9df0.cpp:361): CKeyTable::GetQuickSlotSkillId (keytable.cpp:1478), the skill in
        /// the slot of the current page, 0 when the page or the slot holds none.</summary>
        public override int QuickSlotSkill(int slot) => s.KeyTable.Entry(s.KeyTable.CurrentPage, slot).SkillId;
        /// <summary>ExistItem (unit_1413a8370.cpp:400): Unk_Inventory::HasItemCount (inventory.cpp:731), the count of any rarity.</summary>
        public override bool HasItemCount(int itemId, int count) => s.Items.CountById(itemId, 0) >= count;
        /// <summary>HasMoney (unit_1413a9df0_2.cpp:229-): type 0 is the meso balance DAT_143ab2898. STILL-OPEN: types 1-8
        /// (merat, karma, honor, lu and the four unnamed globals) have no balance here and read 0.</summary>
        public override long Balance(int moneyType) => moneyType == 0 ? s.Me.Meso : 0;
        /// <summary>HasStatPoint (unit_1413a9df0_2.cpp:699-705): the stat point manager's GetRemaining, none without the player.</summary>
        public override int? StatPointsLeft => HasPlayer ? s.Me.StatPoints.Remaining : null;
        /// <summary>Title (unit_1413a9df0.cpp:745): the count of the title list DAT_143ab3268 (UserEnv titles, scenestategame.cpp:4427-4442).</summary>
        public override int TitleCount => s.Me.Titles.Count;
        /// <summary>CGuideConditionInventoryTab::vfunc_3 (unit_1413a76f0.cpp:135): UIInventoryDialog 0x4e2d shown (vfunc_55 == 0), its GetSelectedTab.</summary>
        public override int? InventoryTab => s.GuideHud?.InventoryTab;
        /// <summary>CGuideConditionMasteryMainTabIndex / SubTabIndex::vfunc_3 (unit_1413a9df0_2.cpp:572, 590): UIMasteryDialog 0x4f1e, its tab indexes.</summary>
        public override int? MasteryMainTab => s.GuideHud?.MasteryMainTab;
        public override int? MasterySubTab => s.GuideHud?.MasterySubTab;
        /// <summary>CGuideConditionMenuExpand::vfunc_3 (unit_1413a9df0_2.cpp:519): UIGameMenuDialog 0x4e28's IsMenuExpanded(name).</summary>
        public override bool? MenuExpanded(string name) => s.GuideHud?.MenuExpanded(name);
        /// <summary>CGuideConditionHomeMode::vfunc_3 (unit_1413a9df0_2.cpp:909): CubeMapInformationService::FUN_1410c3650, the plot mode f330.</summary>
        public override int HouseMode => s.CubeMap.PlotMode;
        /// <summary>CGuideConditionIsMyHome::vfunc_3 (unit_1413a8370.cpp:185): FUN_1410d5640 (HomeOptionRows.CanChange).</summary>
        public override bool IsMyHome => s.FieldId is int id
            && Ms2.Ui.HomeOptionRows.CanChange(s.GuideFieldData(id)?.MapType == 3, s.CubeMap.PlotMode == 1, s.CubeMap.Owner, s.OwnAccountId);
        /// <summary>CGuideConditionContinentCode::vfunc_3 (unit_1413a9df0.cpp:115): WorldService::GetFieldData's GetContinentCode; null without it.</summary>
        /// <summary>CGuideConditionCountItem::vfunc_3 (unit_1413a8370.cpp:449): the inventory's FUN_140a60c20(type).</summary>
        public override int CountItemsOfType(int itemType) =>
            s.GuideHud is { } hud ? s.Items.CountOfType(itemType, s.GuideItemTypeOf(hud), ServerTime) : 0;
        /// <summary>CGuideConditionDesignItem::vfunc_3 (unit_1413a8370.cpp:332) over the inventory's items.</summary>
        public override bool HasDesignItem(int itemType) =>
            s.GuideHud is { } hud && s.Items.HasDesignItem(itemType, s.GuideItemTypeOf(hud), s.guideItemMesh, ServerTime);
        public override int? ContinentCode => s.FieldId is int id ? s.GuideFieldData(id)?.ContinentCode : null;
        /// <summary>CGuideConditionBattleState::vfunc_3 (unit_1413a9df0_2.cpp:446): PCBase::vfunc_65 (pcbase.cpp:1286), the emotion
        /// SetEmotion keeps (OnUserBattle and the add user's battle byte).</summary>
        public override bool InBattle => s.SelfInBattle;
        /// <summary>CGuideConditionAchieveExistTrophy::vfunc_3 (unit_1413a9df0.cpp:233): CAchieveManager::HasCompletedOfType.</summary>
        public override bool HasTrophyOfType(int? type) => s.Achieves.HasCompletedOfType(type);
        /// <summary>CGuideConditionRealEstateExpireDays::vfunc_3 (unit_1413a9df0_2.cpp:313): MyPC::GetUGCMapInfo's i64 at +0x30
        /// (Unk_G9Rec m_nExpire, a CMsTime), the field ResponseCube 0x14 reads as HomeId; null without the info.</summary>
        public override long? UgcMapExpire => s.Home.Info is { } info ? info.HomeId : null;
        public override int FieldId => s.FieldId ?? 0;
        public override int JobGroup => s.Me.User?.JobCode ?? 0;
        /// <summary>STILL-OPEN: the client's sub job is PC m_spJobFB8 (pc.cpp:1275); the user block's job, which the Job packet does not refresh here.</summary>
        public override int SubJob => s.Me.User?.Job ?? 0;
        public override int Level => s.Me.User?.Level ?? 0;
        /// <summary>MyPC::GetCurStateId (CStateCondition): the client's state id as <see cref="OwnCurStateId"/> gives it, 1 while
        /// the body stands, not the last state the server set.</summary>
        public override int PlayerState => s.OwnCurStateId(s.OwnSkillStateRunsOf?.Invoke() ?? false);

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

/// <summary>The HUD's side of the guide's dialog reads (CUIService::FindObject of the dialog's id, as each condition asks it).</summary>
public interface IGuideHudState
{
    /// <summary>UIInventoryDialog's GetSelectedTab (m_194) while it is shown; null otherwise.</summary>
    int? InventoryTab { get; }
    /// <summary>UIMasteryDialog's GetMainTabIndex / GetSubTabIndex (-1 without its movie); null without the dialog.</summary>
    int? MasteryMainTab { get; }
    int? MasterySubTab { get; }
    /// <summary>UIGameMenuDialog's IsMenuExpanded(name); null without the dialog.</summary>
    bool? MenuExpanded(string name);
    /// <summary>The item table's type and cube texture mesh of an item id (0 and false for an unknown id).</summary>
    int ItemType(int itemId);
    bool ItemHasCubeTextureMesh(int itemId);
}
