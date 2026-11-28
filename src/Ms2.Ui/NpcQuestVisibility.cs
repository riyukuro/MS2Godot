using Ms2.Formats.World;

namespace Ms2.Ui;

/// <summary>
/// The quest mask of a field's live npcs (questmasknpc.xml): CNpc::UpdateQuestVisibility (@0x141094e60,
/// unit_14108e1e0_npcactorandvibrate.cpp:1528-1538) asks CQuestMaskTable::CheckNpcObject (@0x140764ef0) of an npc whose
/// m_nEB8 is set (CQuestMaskTable::HasNpcMask @0x140764c90, set once when the npc is made, :2106) and hides it with hide
/// reason 5 while the check fails. It runs when the npc is made (CNpc::vfunc_10, :542) and for every npc on the quest
/// manager's dirty flag (WorldService::FUN_141bd3ae0, worldservice_1.cpp:7152-7158, from the world update at :9833-9839).
/// A hidden npc is out of the talk (TalkAction::vfunc_1, actionkeyobject.cpp:1385), the click and its name tag
/// (UIHudEntity::Entity::vfunc_8, uihudentity.cpp:200).
/// </summary>
public sealed class NpcQuestVisibility
{
    /// <summary>The hide reason of a failed npc mask (AddHideReason(5), :1534).</summary>
    public const int HideReasonQuest = 5;

    readonly QuestMaskTable masks;
    readonly int fieldId;
    readonly Func<int, QuestMaskTable.QuestStatus> quest;

    /// <param name="masks">The quest mask table; null when the data has none (every npc shown).</param>
    /// <param name="fieldId">WorldService::GetFieldId: the field the masks are looked up in (HasNpcMaskHere, CheckNpcHere).</param>
    /// <param name="quest">The player's quest standing for the masks.</param>
    public NpcQuestVisibility(QuestMaskTable masks, int fieldId, Func<int, QuestMaskTable.QuestStatus> quest)
    {
        this.masks = masks;
        this.fieldId = fieldId;
        this.quest = quest ?? QuestMaskTable.NoQuests;
    }

    public int FieldId => fieldId;

    /// <summary>CQuestMaskTable::HasNpcMask / HasNpcMaskHere (@0x140764cd0): the table has a mask for the npc in this field.</summary>
    public bool HasMask(int npcId) => masks?.Npc(fieldId, npcId) != null;

    /// <summary>CQuestMaskTable::CheckNpcObject: the table's mask for the npc passes, or there is none.</summary>
    public bool Shows(int npcId) => masks == null || masks.CheckNpc(fieldId, npcId, quest);

    /// <summary>
    /// PCBase::AddHideReason (@0x14166ca20) / RemoveHideReason (@0x14166ca80) on a reason set: false when the bit already
    /// has that value (nothing to apply), else the bit changed and ApplyHideReasons (@0x14166cb30, pcbase.cpp:1188-1203)
    /// shows the actor only when no reason is left.
    /// </summary>
    public static bool SetHideReason(ref int reasons, int reason, bool on)
    {
        int bit = 1 << reason;
        if (((reasons & bit) != 0) == on) return false;
        reasons = on ? reasons | bit : reasons & ~bit;
        return true;
    }
}
