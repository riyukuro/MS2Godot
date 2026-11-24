using System.Numerics;
using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Formats.World;
using Ms2.Net.Packets;
using Ms2.Ui.Skills;

namespace Ms2.Ui.Items;

/// <summary>
/// A player's weapon attachment and battle stance state, ported from the restored client:
/// <list type="bullet">
/// <item>Starter weapon slot assignment: JobData::Load (@0x140643a60, commonfiles/jobtable.cpp:152) reads table/job.xml
/// startItems; FUN_141420a70 / FUN_141420b30 (@0x141420a70 / @0x141420b30, unnamed/unit_141420a70.cpp:25-108) and
/// CItem::FUN_140527a20 (@0x140527a20, commonfiles/item.cpp:1278) place a two-handed weapon (CItemData::FUN_14053d460
/// @0x14053d460, itemdata.cpp:893) in RH (5), a dual-wield OH weapon (CItemData::FUN_14053d510 @0x14053d510, :925, subtype
/// 31 or 34) in RH (5) when RH is empty (or holds a two-handed weapon) and in LH (4) when RH holds a one-handed weapon, and
/// any other weapon in its model's first group type (RH 5 or LH 4).</item>
/// <item>Two-handed LH group linking: CEquipment::FUN_1411ec190 (@0x1411ec190, client/ms2/equipment.cpp:2181-2201) links an
/// item equipped in RH (5) into LH (4) as well when its ItemModelData::HasGroupType(4) (@0x14054b450, itemdatastruct.cpp:2088)
/// is true (e.g. bow 15100004 and cannon 15300032 whose &lt;asset&gt; is in &lt;slot name="LH"&gt;, and knuckle 15500001 which
/// has &lt;asset&gt;s in both &lt;slot name="RH"&gt; and &lt;slot name="LH"&gt;).</item>
/// <item>Sheathed vs hand node choice: PC::FUN_141658150 (@0x141658150, client/ms2/pc.cpp:3962-4033) and FUN_141201d50
/// (@0x141201d50, client/ms2/equipmodel.cpp:931-965). Each RH model (CEquipModel::FUN_141202940 == 5, @0x141202940,
/// equipmodel.cpp:1071) attaches to its sheathed node (CEquipModel::GetPartName @0x1412029d0, :1084) when bPrimary is 0, and
/// to ModelPart::name18 (attachnode) if non-empty else "Weapon_Hand_R_Point" when bPrimary is 1. When no RH item is worn,
/// bPrimary is forced to 0 (pc.cpp:3996). Each LH model (FUN_141202940 == 4) attaches to its sheathed node when bPrimary is
/// 0, when any dummy for the gender has worldmode or worldlerp (pc.cpp:4013-4018), or when
/// !CWeaponActionTable::IsAllowed(nRightType, nLeftType) (pc.cpp:4019); otherwise to attachnode if non-empty else
/// "Weapon_Hand_L_Point".</item>
/// <item>Instant re-parenting and per-gender dummy offset: FUN_141201d50 (@0x141201d50, equipmodel.cpp:931-965) re-parents
/// the WeaponAttachTransformNode directly onto the target bone (pTarget-&gt;AttachChild(m_spNode, true), instant, not animated)
/// and calls FUN_141200390 (@0x141200390, :563-604), which resets the transform node to identity rotation and zero
/// translation and applies the matching &lt;dummy&gt; of ModelPart::entries[gender] whose targetnode equals the new parent bone
/// name and whose sequence is a prefix of the current sequence.</item>
/// <item>Triggers and state gating: PC::FUN_141659000 (@0x141659000, client/ms2/pc.cpp:2491-2511) sets
/// bActive = vfunc_65() &amp;&amp; GetState()-&gt;vfunc_15(), cleared in cube mode (IsInCubeMode), forced on by GetState()-&gt;vfunc_16(),
/// and cleared when m_spWeaponActionD70 is null or its m_nRightHand is 0; on a change it calls PC::FUN_141657c80 (@0x141657c80,
/// :2473-2488) to update m_nD80 (<see cref="WeaponsHeld"/>) and re-parent the weapons.</item>
/// </list>
/// </summary>
public sealed class WeaponStance
{
    /// <summary>One attached weapon model part and its precomputed sheathed and held nodes and offsets.</summary>
    public sealed record Part(int ItemId, EquipSlot EquippedSlot, int EffectiveSlot, ItemModelTable.Asset Asset,
        string SheathedNode, string HeldNode, Vector3 SheathedTranslation, Vector3 SheathedRotation,
        Vector3 HeldTranslation, Vector3 HeldRotation)
    {
        public string NodeFor(bool held) => held ? HeldNode : SheathedNode;

        public (Vector3 Translation, Vector3 Rotation) OffsetFor(bool held, int gender, string sequence = "") =>
            string.IsNullOrEmpty(sequence)
                ? (held ? (HeldTranslation, HeldRotation) : (SheathedTranslation, SheathedRotation))
                : Asset.DummyFor(gender, NodeFor(held), sequence);
    }

    /// <summary>The active target bone and dummy offset of one weapon part in the current stance state.</summary>
    public sealed record Placement(int ItemId, EquipSlot EquippedSlot, int EffectiveSlot, string AssetName,
        string SelfNode, string TargetNode, Vector3 Translation, Vector3 Rotation);

    public int Gender { get; }
    public int RightSubtype { get; }
    public int LeftSubtype { get; }
    /// <summary>PC::m_spWeaponActionD70, resolved by PC::UpdateWeaponAction (@0x141653f00, client/ms2/pc.cpp:2838-2874).</summary>
    public WeaponAction Action { get; }
    public IReadOnlyList<Part> Parts { get; }

    /// <summary>PCBase::m_unk_160 (PCBase::vfunc_65 @0x14166ced0, pcbase.cpp:1284): 1 in battle stance, 0 out of battle.</summary>
    public bool InBattle { get; private set; }
    /// <summary>State::vfunc_15 (@0x1417b8150, client/ms2/state.cpp:684): whether the current actor state allows holding weapons.</summary>
    public bool StateAllowsWeapons { get; private set; } = true;
    /// <summary>PC::IsInCubeMode: cube mode forces weapons to be sheathed unless State::vfunc_16 overrides it.</summary>
    public bool InCubeMode { get; private set; }
    /// <summary>State::vfunc_16 (@0x1417b8160, client/ms2/state.cpp:690): forces weapons into hands.</summary>
    public bool StateForcesWeapons { get; private set; }
    /// <summary>PC::m_nD80 (set by PC::FUN_141657c80 @0x141657c80, client/ms2/pc.cpp:2473-2488): true while weapons sit on the hand nodes.</summary>
    public bool WeaponsHeld { get; private set; }
    /// <summary>The actor's current animation sequence name (passed to FUN_141200390 through PC::FUN_141659790 @0x141659790, pc.cpp:2514).</summary>
    public string Sequence { get; private set; } = "";

    /// <summary>
    /// PC::vfunc_60 (@0x141658a30, client/ms2/pc.cpp:1737-1749): returns m_spWeaponActionD70-&gt;m_strPrefix while m_nD80 != 0,
    /// else "".
    /// </summary>
    public string ActionPrefix => WeaponsHeld && Action != null ? Action.Prefix : "";

    /// <summary>
    /// StateIdle::PlayStandAnimation (@0x1417ce6f0, client/ms2/stateidle.cpp:518-531): action key 1 (BATTLE_IDLE -&gt;
    /// "Attack_Idle_A") when vfunc_65() != 0, else 0 (IDLE -&gt; "Idle_A").
    /// </summary>
    public string StandAction => InBattle ? "BATTLE_IDLE" : "IDLE";

    /// <summary>The group-0 sequence name of <see cref="StandAction"/> in table/pcactiondata.xml.</summary>
    public string StandSequence => InBattle ? "Attack_Idle_A" : "Idle_A";

    public WeaponStance(int gender, int rightSubtype, int leftSubtype, WeaponAction action, IReadOnlyList<Part> parts)
    {
        Gender = gender;
        RightSubtype = rightSubtype;
        LeftSubtype = leftSubtype;
        Action = action;
        Parts = parts ?? Array.Empty<Part>();
    }

    /// <summary>
    /// CAnimationManager::PlayAnimation (@0x14099e1a0, client/ms2/animationmanager.cpp:248-265) and PCBase::FUN_141679270
    /// (@0x141679270, client/ms2/pcbase.cpp:3538-3553): prepends <see cref="ActionPrefix"/> to <paramref name="baseSequence"/>
    /// when non-empty and falls back to <paramref name="baseSequence"/> if the actor has no prefixed sequence.
    /// </summary>
    public string ResolveSequence(string baseSequence, Func<string, bool> hasSequence = null)
    {
        if (string.IsNullOrEmpty(baseSequence)) return "";
        string prefix = ActionPrefix;
        if (prefix.Length == 0) return baseSequence;
        string candidate = prefix + baseSequence;
        return hasSequence == null || hasSequence(candidate) ? candidate : baseSequence;
    }

    /// <summary>The wire ids of the states State::vfunc_15 answers 1 for among those a record carries (gostate.h): idle.</summary>
    public const int StateIdle = 1, StateWalk = 2, StateLand = 4, StateFall = 5, StateJump = 6,
        StateHit = 0xd, StateWaitingSelect = 0xe, StateSpawn = 0x11, StateRevival = 0x18, StateSwimDash = 0x1c, StateClimb = 0x1d, StateGlide = 0x1e,
        StatePvPWinLose = 0x28, StateHold = 0x20;

    /// <summary>
    /// State::vfunc_15 (@0x1417b8150, client/ms2/state.cpp:684, default 0) for the movement states a user sync record
    /// carries, by the record's state id and sub state. The own character and every remote ask this one function:
    /// <list type="bullet">
    /// <item>StateIdle::vfunc_15 (@0x1417cd740, stateidle.cpp:210-217): 0 for sub state 7 (water idle) and 6, else 1;</item>
    /// <item>StateWalk::vfunc_15 (@0x141800a90, statewalk.cpp:376-384): 0 when slow-walking (m_n48 == 0, the gait of
    /// PC::vfunc_115) or sub state 0x19, else 1; <paramref name="running"/> is m_n48;</item>
    /// <item>StateJump (@0x1417d6ac0, statejump.cpp:413), StateFall (@0x1417c7aa0, statefall.cpp:225), StateLand
    /// (@0x1417da780, stateland.cpp:190): 1;</item>
    /// <item>StateHit 0xd (statehit.cpp:346), StateSpawn 0x11 (statespawn.cpp:142), StateRevival 0x18 (statedead.cpp:347),
    /// StatePvPWinLose 0x28 (statepvpwinlose.cpp:73), and the states derived from StateMoveSkill (@0x1417bef20,
    /// state.cpp:1611-1616, returns 1): StateSwimDash 0x1c, StateClimb 0x1d, StateGlide 0x1e: 1;</item>
    /// <item>swim, ladder, rope, dash: the default State::vfunc_15, 0.</item>
    /// </list>
    /// StateWaitingSelect 0xe (@0x1417ff6c0, statewaitingselect.cpp:225-229): 1 only for sub state 0x55. StateHold 0x20
    /// (@0x1417cccf0, stateholddata.cpp:383-388): 1 unless riding (<paramref name="riding"/>, m_bRiding).
    /// STILL-OPEN: StateDeadNPC (statedead.cpp:341) and StateSkillMagicControl (stateskillmagiccontrol.cpp:67) answer 1 too and
    /// are not modelled here; StateSkill answers its motion's m_nHoldWeapon (1 without a motion, 0 without a record;
    /// stateskill.cpp:1095-1104, see <see cref="OnSkill"/>). A character in one of those is treated as not allowing weapons.
    /// </summary>
    public static bool StateHoldsWeapons(int stateId, int subState, bool running = true, bool riding = false) => stateId switch
    {
        StateIdle => subState != 7 && subState != 6,
        StateWalk => running && subState != 0x19,
        StateLand or StateFall or StateJump => true,
        StateWaitingSelect => subState == 0x55,
        StateHold => !riding,
        StateHit or StateSpawn or StateRevival or StateSwimDash or StateClimb or StateGlide or StatePvPWinLose => true,
        _ => false,
    };

    /// <summary>State::vfunc_16 (@0x1417b8160, state.cpp:690, default 0) of the states a record carries: StateHold
    /// (@0x1417ccd20, stateholddata.cpp:391-396) answers 1 unless riding, forcing the weapons into the hands even out of battle.</summary>
    public static bool StateForces(int stateId, bool riding = false) => stateId == StateHold && !riding;

    /// <summary>
    /// PC::FUN_141659000 (@0x141659000, client/ms2/pc.cpp:2491-2511): whether weapons sit in the hand nodes. Battle stance
    /// and the state's vfunc_15, cleared in cube mode, forced on by vfunc_16, and cleared when the weapon action is null or
    /// its m_nRightHand is 0. The one rule <see cref="Evaluate"/> and the game's weapon rig both use.
    /// </summary>
    public static bool HoldsWeapons(bool inBattle, bool stateAllowsWeapons, int rightHand, bool inCubeMode = false, bool stateForcesWeapons = false)
    {
        bool active = inBattle && stateAllowsWeapons;
        if (inCubeMode) active = false;
        if (stateForcesWeapons) active = true;
        return active && rightHand != 0;
    }

    /// <summary>
    /// PC::FUN_141658150 (@0x141658150, client/ms2/pc.cpp:3996-4019): whether a weapon model goes to its hand node. A
    /// right-hand model follows bPrimary; a left-hand model also stays sheathed when a dummy for the gender has worldmode or
    /// worldlerp, or when the weapon action table does not allow the pair.
    /// </summary>
    public static bool UsesHandNode(bool primary, bool leftHandModel, bool hasWorldDummy, bool pairAllowed) =>
        primary && (!leftHandModel || (!hasWorldDummy && pairAllowed));

    /// <summary>
    /// PCBase::SetEmotion (@0x14166cee0, client/ms2/pcbase.cpp:1264-1281) -&gt; PC::vfunc_66 (@0x1416544f0, client/ms2/pc.cpp:1244-1261)
    /// -&gt; PC::FUN_141659000(0) (@0x141659000, :2491-2511). Triggered by CSceneStateGame::OnUserBattle (@0x141717580,
    /// scenestategame.cpp:3269-3288, 1 on entering combat, 0 on the server's battle stance timeout), CSceneStateGame::OnAddUser
    /// (@0x14172b5e0, :11188, initial InBattle), or non-combat idle substates (StateIdle::vfunc_9 @0x1417cdbb0, stateidle.cpp:387).
    /// Returns true if <see cref="WeaponsHeld"/> changed.
    /// </summary>
    public bool SetBattle(bool inBattle)
    {
        InBattle = inBattle;
        return Evaluate();
    }

    /// <summary>
    /// Updates the actor state flags read by PC::FUN_141659000 (@0x141659000, client/ms2/pc.cpp:2491-2511) and re-evaluates
    /// <see cref="WeaponsHeld"/>. Returns true if <see cref="WeaponsHeld"/> changed.
    /// </summary>
    public bool SetState(bool stateAllowsWeapons, bool inCubeMode = false, bool stateForcesWeapons = false)
    {
        StateAllowsWeapons = stateAllowsWeapons;
        InCubeMode = inCubeMode;
        StateForcesWeapons = stateForcesWeapons;
        return Evaluate();
    }

    /// <summary>
    /// StateSkill::vfunc_4 (@0x1417e5350, client/ms2/stateskill.cpp:431-435) and StateSkill::vfunc_15 (@0x1417e92c0, :1095-1104):
    /// starting a skill with m_bBattle sets SetEmotion(1), and vfunc_15 returns pMotion-&gt;m_nHoldWeapon (1 when no motion).
    /// </summary>
    public bool OnSkill(bool isBattleSkill, bool motionHoldsWeapon = true)
    {
        if (isBattleSkill) InBattle = true;
        StateAllowsWeapons = motionHoldsWeapon;
        return Evaluate();
    }

    /// <summary>
    /// PC::FUN_141659790 (@0x141659790, client/ms2/pc.cpp:2514-2550), called by CAnimationManager::PlayAnimation
    /// (client/ms2/animationmanager.cpp:289): updates the current sequence name for sequence-filtered dummy offsets.
    /// </summary>
    public void SetSequence(string sequence) => Sequence = sequence ?? "";

    /// <summary>
    /// PC::FUN_141659000 (@0x141659000, client/ms2/pc.cpp:2491-2511) and PC::FUN_141657c80 (@0x141657c80, :2473-2488).
    /// </summary>
    bool Evaluate()
    {
        bool active = HoldsWeapons(InBattle, StateAllowsWeapons, Action?.RightHand ?? 0, InCubeMode, StateForcesWeapons);
        if (WeaponsHeld == active) return false;
        WeaponsHeld = active;
        return true;
    }

    /// <summary>The active placement (target bone and dummy offset) of each weapon part in the current state.</summary>
    public IReadOnlyList<Placement> Placements()
    {
        if (Parts.Count == 0) return Array.Empty<Placement>();
        var list = new List<Placement>(Parts.Count);
        foreach (var p in Parts)
        {
            string node = p.NodeFor(WeaponsHeld);
            var (t, r) = p.OffsetFor(WeaponsHeld, Gender, Sequence);
            list.Add(new Placement(p.ItemId, p.EquippedSlot, p.EffectiveSlot, p.Asset.Name, p.Asset.SelfNode, node, t, r));
        }
        return list;
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PackSet, Dictionary<int, List<int>>> jobStartItems = new();

    /// <summary>
    /// JobData::Load (@0x140643a60, commonfiles/jobtable.cpp:152): the startItems list of <paramref name="jobGroup"/> in
    /// table/job.xml, empty when the job has none. The GMS2 file (<see cref="JobTable.IsCodeShape"/>) keys its rows by code
    /// and lists them as &lt;startInvenItem&gt;&lt;item itemID&gt;: read from the data, not from a client source (each group
    /// the KMS file also has lists the same ids as its startItems).
    /// </summary>
    public static IReadOnlyList<int> StartItemIds(PackSet packs, int jobGroup)
    {
        if (packs == null) return Array.Empty<int>();
        var map = jobStartItems.GetValue(packs, p =>
        {
            var dict = new Dictionary<int, List<int>>();
            if (!p.Exists("Xml", "table/job.xml")) return dict;
            var root = GameStrings.Parse(p.Read("Xml", "table/job.xml")).Root;
            return root == null ? dict : StartItemsByGroup(root, (parent, child, key) => FeatureElements.Collect(p, parent, child, key));
        });
        return map.TryGetValue(jobGroup, out var list) ? list : Array.Empty<int>();
    }

    /// <summary>The start item lists of table/job.xml's root by job group, in either shape (see <see cref="StartItemIds"/>).</summary>
    /// <param name="collect">FUN_14028faa0(root, "job", key): the available &lt;job&gt; elements, one per group.</param>
    public static Dictionary<int, List<int>> StartItemsByGroup(XElement root, Func<XElement, string, string, List<XElement>> collect)
    {
        var dict = new Dictionary<int, List<int>>();
        bool code = JobTable.IsCodeShape(root);
        string key = code ? "code" : "jobGroupID";
        foreach (var j in collect(root, "job", key))
        {
            int id = ItemData.Atoi((string)j.Attribute(key) ?? "0");
            var ids = (code
                    ? j.Elements("startInvenItem").Elements("item").Select(i => (string)i.Attribute("itemID") ?? "")
                    : ((string)j.Attribute("startItems") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
                .Select(ItemData.Atoi).Where(v => v > 0).ToList();
            dict.TryAdd(id, ids);
        }
        return dict;
    }

    /// <summary>
    /// Assigns each weapon in <paramref name="itemIds"/> its equip slot (RH 5 or LH 4) in order, following
    /// FUN_141420a70 / FUN_141420b30 (@0x141420a70 / @0x141420b30, unnamed/unit_141420a70.cpp:25-108) and
    /// CItem::FUN_140527a20 (@0x140527a20, commonfiles/item.cpp:1278):
    /// <list type="bullet">
    /// <item>a two-handed weapon (CItemData::FUN_14053d460 @0x14053d460, itemdata.cpp:893) goes to RH (5);</item>
    /// <item>a dual-wield OH weapon (CItemData::FUN_14053d510 @0x14053d510, :925, subtype 31 or 34) goes to RH (5) when RH is
    /// empty or holds a two-handed weapon, else LH (4) (:80-104);</item>
    /// <item>any other weapon goes to its model's first group type (ItemModelData::GetFirstGroupType, RH 5 or LH 4).</item>
    /// </list>
    /// </summary>
    public static List<(int ItemId, EquipSlot Slot)> AssignWeaponSlots(IEnumerable<int> itemIds, ItemDataTable items, ItemModelTable models)
    {
        var result = new List<(int ItemId, EquipSlot Slot)>();
        if (itemIds == null || items == null || models == null) return result;
        ItemData rhData = null, lhData = null;
        foreach (int id in itemIds)
        {
            var d = items.Get(id);
            if (d == null) continue;
            int preset = d.ItemPreset != 0 ? d.ItemPreset : d.Id;
            EquipSlot slot;
            if (WornEquipment.IsTwoHanded(d, models))
                slot = EquipSlot.RH;
            else if (WornEquipment.IsDualWieldWeapon(d))
                slot = rhData == null || WornEquipment.IsTwoHanded(rhData, models) || lhData != null ? EquipSlot.RH : EquipSlot.LH;
            else
            {
                int first = models.Get(preset)?.FirstSlotType ?? 0;
                slot = first == 4 ? EquipSlot.LH : first == 5 ? EquipSlot.RH : EquipSlot.Unknown;
            }
            if (slot is not (EquipSlot.RH or EquipSlot.LH)) continue;
            if (slot == EquipSlot.RH) rhData = d;
            else lhData = d;
            result.RemoveAll(x => x.Slot == slot);
            result.Add((id, slot));
        }
        return result;
    }

    /// <summary>Builds the <see cref="WeaponStance"/> of <paramref name="jobGroup"/>'s starter items for <paramref name="gender"/> (0 male, 1 female).</summary>
    public static WeaponStance ForJob(PackSet packs, int jobGroup, int gender, ItemDataTable items = null, ItemModelTable models = null, WeaponActionTable actions = null)
    {
        items ??= new ItemDataTable(packs);
        models ??= new ItemModelTable(packs);
        actions ??= WeaponActionTable.Load(packs);
        var equips = AssignWeaponSlots(StartItemIds(packs, jobGroup), items, models);
        return ForEquips(equips, gender, items, models, actions);
    }

    /// <summary>
    /// Builds the <see cref="WeaponStance"/> for the worn items of <paramref name="equips"/> and <paramref name="gender"/>
    /// (0 male, 1 female), following CEquipment::FUN_1411ec190 (@0x1411ec190, client/ms2/equipment.cpp:2181-2201),
    /// PC::UpdateWeaponAction (@0x141653f00, client/ms2/pc.cpp:2838-2874) and PC::FUN_141658150 (@0x141658150, :3962-4033).
    /// </summary>
    public static WeaponStance ForEquips(IEnumerable<(int ItemId, EquipSlot Slot)> equips, int gender,
        ItemDataTable items, ItemModelTable models, WeaponActionTable actions)
    {
        int rhId = 0, lhId = 0;
        if (equips != null)
            foreach (var (id, slot) in equips)
            {
                if (slot == EquipSlot.RH) rhId = id;
                else if (slot == EquipSlot.LH) lhId = id;
            }

        var rhData = rhId > 0 ? items?.Get(rhId) : null;
        var lhData = lhId > 0 ? items?.Get(lhId) : null;
        int rhPreset = rhData != null ? (rhData.ItemPreset != 0 ? rhData.ItemPreset : rhData.Id) : 0;
        var rhModel = rhPreset > 0 ? models?.Get(rhPreset) : null;

        // CEquipment::FUN_1411ec190 (@0x1411ec190, equipment.cpp:2181-2201): an item equipped in RH (5) whose model also
        // has group type 4 (LH) is stored in slot 4 of m_mapLayer as well (spLeft == spRight)
        bool sameItem = false;
        if (rhData != null && lhData == null && rhModel != null && rhModel.HasSlotType(4))
        {
            lhId = rhId;
            lhData = rhData;
            sameItem = true;
        }

        int rightSub = rhData?.EquipSubtype ?? 0;
        int leftSub = lhData?.EquipSubtype ?? 0;
        actions ??= WeaponActionTable.Empty;

        // PC::UpdateWeaponAction (@0x141653f00, pc.cpp:2838-2874): if (spRight == spLeft || !IsAllowed(nRight, nLeft)) nLeft = 0
        int actionLeft = sameItem || !actions.IsAllowed(rightSub, leftSub) ? 0 : leftSub;
        var action = actions.FindAction(rightSub, actionLeft);

        var parts = new List<Part>();
        if (rhData != null && models != null)
        {
            bool dual = WornEquipment.IsDualWieldWeapon(rhData);
            foreach (var a in models.GetAssets(rhPreset))
            {
                if (a.Pony || (a.Gender != 2 && a.Gender != gender)) continue;
                if (a.EffectiveSlot(5, dual) != 5) continue;     // PC::FUN_141658150 (:3980): FUN_141202940(model) == 5
                string sheathed = a.SheathedNode(5, dual);
                string held = a.AttachNode.Length > 0 ? a.AttachNode : "Weapon_Hand_R_Point";
                var (st, sr) = a.DummyFor(gender, sheathed);
                var (ht, hr) = a.DummyFor(gender, held);
                parts.Add(new Part(rhId, EquipSlot.RH, 5, a, sheathed, held, st, sr, ht, hr));
            }
        }
        if (lhData != null && models != null)
        {
            int lhPreset = lhData.ItemPreset != 0 ? lhData.ItemPreset : lhData.Id;
            bool dual = WornEquipment.IsDualWieldWeapon(lhData);
            var lhSlot = sameItem ? EquipSlot.RH : EquipSlot.LH;
            foreach (var a in models.GetAssets(lhPreset))
            {
                if (a.Pony || (a.Gender != 2 && a.Gender != gender)) continue;
                if (a.EffectiveSlot(4, dual) != 4) continue;     // PC::FUN_141658150 (:4009): FUN_141202940(model) == 4
                string sheathed = a.SheathedNode(4, dual);
                // PC::FUN_141658150 (:3996, :4013-4030): no RH item forces bPrimary = 0; worldmode/worldlerp or !IsAllowed keeps "" (sheathed)
                bool canHoldLh = rhData != null && !a.HasWorldDummy(gender) && actions.IsAllowed(rightSub, leftSub);
                string held = canHoldLh ? (a.AttachNode.Length > 0 ? a.AttachNode : "Weapon_Hand_L_Point") : sheathed;
                var (st, sr) = a.DummyFor(gender, sheathed);
                var (ht, hr) = a.DummyFor(gender, held);
                parts.Add(new Part(lhId, lhSlot, 4, a, sheathed, held, st, sr, ht, hr));
            }
        }

        return new WeaponStance(gender, rightSub, leftSub, action, parts);
    }
}
